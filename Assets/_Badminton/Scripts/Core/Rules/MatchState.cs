using System;
using System.Collections.Generic;

namespace Badminton.Core
{
    /// <summary>Ô giao cầu, gọi theo góc nhìn của chính người đứng trong ô (phải/trái của họ).</summary>
    public enum ServiceCourt : byte { Right, Left }

    /// <summary>Luật thi đấu, có thể đổi để test nhanh (ví dụ game 5 điểm).</summary>
    [Serializable]
    public class MatchRulesConfig
    {
        public int pointsToWin = 21;
        public int pointCap = 30;     // chạm mốc này là thắng game dù chỉ hơn 1
        public int gamesToWin = 2;    // 3 game thắng 2
        public int intervalAt = 11;   // nghỉ giữa game khi điểm cao nhất chạm mốc này
        /// <summary>
        /// Đổi sân THẬT khi đến lúc đổi sân. Tắt (mặc định) ⇒ vẫn báo ChangeEnds để UI thông báo
        /// "Đổi sân", nhưng mỗi người giữ nguyên nửa sân – người chơi không bị ngược hướng điều khiển.
        /// </summary>
        public bool swapSides = false;
    }

    /// <summary>Trạng thái trận dạng phẳng, gọn để đồng bộ qua mạng.</summary>
    [Serializable]
    public struct MatchSnapshot
    {
        public int Score0, Score1, Games0, Games1, Server, GameNumber, MatchWinner;
        public CourtSide Player0Side;
        public bool IntervalDone, DecidingEndsChanged;
    }

    /// <summary>Tỷ số một game đã kết thúc, theo thứ tự người chơi 0 – người chơi 1.</summary>
    [Serializable]
    public struct GameScore
    {
        public int P0, P1;
        public GameScore(int p0, int p1) { P0 = p0; P1 = p1; }
        public int Winner => P0 > P1 ? 0 : 1;
        public override string ToString() => P0 + "-" + P1;
    }

    /// <summary>Những gì vừa xảy ra sau khi cộng một điểm.</summary>
    public struct PointOutcome
    {
        public int Scorer;
        public bool Interval;       // nghỉ giữa game (một bên vừa đạt 11)
        public bool GameWon;
        public bool MatchWon;
        public bool ChangeEnds;     // đổi sân (sau mỗi game, và ở game quyết định khi một bên đạt 11)
        public int GameWinner;      // hợp lệ khi GameWon
    }

    /// <summary>
    /// Trạng thái trận đơn – tính điểm rally (ai thắng rally được 1 điểm và giao cầu tiếp).
    /// Thuần C#, không phụ thuộc Unity/network ⇒ chạy giống hệt trên host/server và trong unit test.
    /// Người chơi được đánh số 0 và 1.
    /// </summary>
    public sealed class MatchState
    {
        public readonly MatchRulesConfig Rules;

        public readonly int[] Score = new int[2];   // điểm game hiện tại
        public readonly int[] Games = new int[2];   // số game đã thắng
        public readonly CourtSide[] Side = new CourtSide[2];
        /// <summary>Tỷ số các game đã xong (ví dụ 21-18, 19-21, 22-20) – dùng cho kết quả giải đấu.</summary>
        public readonly List<GameScore> CompletedGames = new List<GameScore>(3);
        public int GameNumber { get; private set; } = 1;
        public int Server { get; private set; }
        public int MatchWinner { get; private set; } = -1;
        public bool IsOver => MatchWinner >= 0;

        bool _intervalDone;
        bool _decidingEndsChanged;

        public MatchState(MatchRulesConfig rules, int firstServer = 0, CourtSide player0Side = CourtSide.Near)
        {
            Rules = rules ?? new MatchRulesConfig();
            Server = firstServer;
            Side[0] = player0Side;
            Side[1] = Opposite(player0Side);
        }

        public static int Other(int p) => 1 - p;
        public static CourtSide Opposite(CourtSide s) => s == CourtSide.Near ? CourtSide.Far : CourtSide.Near;

        public int Receiver => Other(Server);

        /// <summary>Điểm của người giao chẵn ⇒ giao từ ô PHẢI, lẻ ⇒ ô TRÁI (luật BWF đánh đơn).</summary>
        public ServiceCourt ServeCourt => Score[Server] % 2 == 0 ? ServiceCourt.Right : ServiceCourt.Left;

        public bool IsDecidingGame => GameNumber == Rules.gamesToWin * 2 - 1;

        /// <summary>Cộng điểm cho người thắng rally và xử lý hết các hệ quả (giao cầu, nghỉ, hết game, đổi sân).</summary>
        public PointOutcome AwardRally(int winner)
        {
            if (IsOver) throw new InvalidOperationException("Trận đã kết thúc.");

            var o = new PointOutcome { Scorer = winner, GameWinner = -1 };
            Score[winner]++;
            Server = winner; // ai thắng rally thì giao tiếp

            if (IsGameWon(Score[winner], Score[Other(winner)]))
            {
                o.GameWon = true;
                o.GameWinner = winner;
                Games[winner]++;
                CompletedGames.Add(new GameScore(Score[0], Score[1]));

                if (Games[winner] >= Rules.gamesToWin)
                {
                    o.MatchWon = true;
                    MatchWinner = winner;
                    return o;
                }

                // Sang game mới: đổi sân, người thắng game giao trước.
                Score[0] = Score[1] = 0;
                GameNumber++;
                _intervalDone = false;
                _decidingEndsChanged = false;
                SwapEnds();
                o.ChangeEnds = true;
                return o;
            }

            int lead = Math.Max(Score[0], Score[1]);
            if (!_intervalDone && lead >= Rules.intervalAt)
            {
                _intervalDone = true;
                o.Interval = true;

                // Game quyết định: đổi sân khi điểm cao nhất đạt 11 (luật BWF).
                if (IsDecidingGame && !_decidingEndsChanged)
                {
                    _decidingEndsChanged = true;
                    SwapEnds();
                    o.ChangeEnds = true;
                }
            }
            return o;
        }

        /// <summary>Chụp toàn bộ trạng thái (gửi qua mạng cho client).</summary>
        public MatchSnapshot Snapshot() => new MatchSnapshot
        {
            Score0 = Score[0], Score1 = Score[1], Games0 = Games[0], Games1 = Games[1],
            Server = Server, GameNumber = GameNumber, MatchWinner = MatchWinner,
            Player0Side = Side[0], IntervalDone = _intervalDone, DecidingEndsChanged = _decidingEndsChanged,
        };

        /// <summary>Khôi phục trạng thái từ snapshot (client nhận từ host).</summary>
        public void Restore(in MatchSnapshot s)
        {
            Score[0] = s.Score0; Score[1] = s.Score1; Games[0] = s.Games0; Games[1] = s.Games1;
            Server = s.Server; GameNumber = s.GameNumber; MatchWinner = s.MatchWinner;
            Side[0] = s.Player0Side; Side[1] = Opposite(s.Player0Side);
            _intervalDone = s.IntervalDone; _decidingEndsChanged = s.DecidingEndsChanged;
        }

        /// <summary>Đủ 21 và hơn 2 điểm, hoặc chạm mốc 30.</summary>
        public bool IsGameWon(int mine, int theirs) => IsGameWon(Rules, mine, theirs);

        public static bool IsGameWon(MatchRulesConfig r, int mine, int theirs) =>
            mine >= r.pointCap || (mine >= r.pointsToWin && mine - theirs >= 2);

        /// <summary>
        /// Tỷ số CHUNG CUỘC của một game có hợp lệ không (thứ tự không quan trọng):
        ///  • thắng đúng 21 và thua ≤ 19;   • trên 21 thì phải hơn ĐÚNG 2 (22-20 … 30-28);
        ///  • hoặc 30-29 (trần điểm).        Mọi tỷ số khác (21-20, 23-20, 31-29, 25-20…) là sai luật.
        /// Dùng cho bộ sinh kết quả trận AI và unit test.
        /// </summary>
        public static bool IsValidFinalGameScore(MatchRulesConfig r, int a, int b)
        {
            int w = Math.Max(a, b), l = Math.Min(a, b);
            if (l < 0) return false;
            if (w == r.pointsToWin) return l <= r.pointsToWin - 2;                 // 21-0 … 21-19
            if (w > r.pointsToWin && w <= r.pointCap)
                return w - l == 2 || (w == r.pointCap && l == r.pointCap - 1);     // 22-20 … 30-28, 30-29
            return false;
        }

        void SwapEnds()
        {
            if (!Rules.swapSides) return; // chỉ thông báo, không đổi vị trí
            Side[0] = Opposite(Side[0]);
            Side[1] = Opposite(Side[1]);
        }
    }
}
