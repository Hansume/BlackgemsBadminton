using System;
using System.Collections.Generic;
using System.Text;
using Badminton.Core;

namespace Badminton.Tour
{
    /// <summary>
    /// Kết quả một trận: winner = 0 (bên A) hoặc 1 (bên B); a[i]/b[i] = điểm game thứ i.
    /// retired = trận kết thúc do bỏ cuộc (người chơi thoát giữa trận) – khi đó tỷ số có thể dở dang.
    /// </summary>
    [Serializable]
    public class MatchResult
    {
        public int winner = -1;
        public int[] a = new int[0];
        public int[] b = new int[0];
        public bool retired;

        public int GamesA { get { int n = 0; for (int i = 0; i < a.Length; i++) if (a[i] > b[i]) n++; return n; } }
        public int GamesB { get { int n = 0; for (int i = 0; i < a.Length; i++) if (b[i] > a[i]) n++; return n; } }
        public int PointsA { get { int n = 0; foreach (var x in a) n += x; return n; } }
        public int PointsB { get { int n = 0; foreach (var x in b) n += x; return n; } }

        /// <summary>"21-18 19-21 22-20" theo góc nhìn bên A (fromA = true) hoặc bên B.</summary>
        public string ScoreText(bool fromA = true)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < a.Length; i++)
            {
                if (i > 0) sb.Append("  ");
                sb.Append(fromA ? a[i] : b[i]).Append('-').Append(fromA ? b[i] : a[i]);
            }
            if (retired) sb.Append(sb.Length > 0 ? "  (bỏ cuộc)" : "Bỏ cuộc");
            return sb.ToString();
        }

        public MatchResult Swapped() => new MatchResult
        {
            winner = winner < 0 ? -1 : 1 - winner, a = (int[])b.Clone(), b = (int[])a.Clone(), retired = retired,
        };
    }

    /// <summary>
    /// Sinh KẾT QUẢ CUỐI CÙNG cho trận AI vs AI – không chạy vật lý, không mô phỏng từng rally.
    ///  1. P(A thắng) = 1 / (1 + 10^((RB − RA + nhiễu) / hệ_số)).
    ///  2. Trận càng cân bằng càng dễ đánh 3 game và điểm game càng sát; chênh lệch lớn thường 2-0 với điểm thua 8–15.
    ///  3. Deuce: 22-20 phổ biến, 24-22 ít hơn, 30-29 rất hiếm.
    /// Mọi tỷ số đều hợp lệ theo luật 3×21 (kiểm tra bằng IsValid + unit test).
    /// </summary>
    public static class MatchResultGenerator
    {
        public static double WinProbability(double ratingA, double ratingB, float scale) =>
            1.0 / (1.0 + Math.Pow(10.0, (ratingB - ratingA) / Math.Max(1f, scale)));

        public static MatchResult Generate(int ratingA, int ratingB, DetRandom rng, ResultGenConfig cfg, MatchRulesConfig rules = null)
        {
            rules ??= new MatchRulesConfig();
            double noise = rng.Gauss() * cfg.ratingNoise;
            double p = WinProbability(ratingA + noise, ratingB, cfg.ratingScale);
            int winner = rng.NextDouble() < p ? 0 : 1;

            // Độ chênh lệch 0 (ngang tài) … 1 (một chiều).
            double gap = Math.Abs(2.0 * p - 1.0);
            double pThree = Lerp(cfg.threeGamesEven, cfg.threeGamesLopsided, gap);
            bool three = rng.NextDouble() < pThree;

            // Người thắng trận có phải người được đánh giá cao hơn không (thắng "bất ngờ" thì các game sát hơn).
            bool favouriteWon = (winner == 0) == (p >= 0.5);

            var gameWinners = new List<int>();
            if (three)
            {
                int lostGame = rng.Next(2); // bên thua trận thắng game 1 hoặc game 2
                for (int g = 0; g < 3; g++) gameWinners.Add(g == lostGame ? 1 - winner : winner);
            }
            else { gameWinners.Add(winner); gameWinners.Add(winner); }

            var r = new MatchResult { winner = winner, a = new int[gameWinners.Count], b = new int[gameWinners.Count] };
            for (int g = 0; g < gameWinners.Count; g++)
            {
                int gw = gameWinners[g];
                // Game do người mạnh hơn thắng ⇒ điểm thua phụ thuộc chênh lệch; game của người yếu hơn ⇒ sát.
                bool strongerWonGame = (gw == winner) == favouriteWon;
                double mean = strongerWonGame ? Lerp(cfg.loserPointsEven, cfg.loserPointsLopsided, gap) : cfg.loserPointsEven;
                var (w, l) = GameScore(mean, cfg, rng, rules);
                if (gw == 0) { r.a[g] = w; r.b[g] = l; }
                else { r.a[g] = l; r.b[g] = w; }
            }
            return r;
        }

        /// <summary>Tỷ số một game (điểm người thắng, điểm người thua).</summary>
        static (int, int) GameScore(double loserMean, ResultGenConfig cfg, DetRandom rng, MatchRulesConfig rules)
        {
            int target = rules.pointsToWin;             // 21
            int cap = rules.pointCap;                   // 30
            double raw = loserMean + rng.Gauss() * cfg.loserPointsSpread;

            if (raw >= target - 1.5) // ≥ 19.5 ⇒ đã hoà 20-20 ⇒ deuce
            {
                int loser = target - 1;                 // 20
                while (loser < cap - 1 && rng.NextDouble() < cfg.deuceContinue) loser++;
                int winnerPts = loser == cap - 1 ? cap : loser + 2; // 29 ⇒ 30-29, còn lại cách 2
                if (winnerPts > cap) winnerPts = cap;
                return (winnerPts, loser);
            }
            int l = (int)Math.Round(raw);
            l = Math.Max(0, Math.Min(target - 2, l));
            return (target, l);
        }

        static double Lerp(double a, double b, double t) => a + (b - a) * Math.Max(0, Math.Min(1, t));

        /// <summary>Kết quả có hợp lệ theo luật không (mọi game hợp lệ, người thắng thắng đủ số game, game cuối là của người thắng).</summary>
        public static bool IsValid(MatchResult r, MatchRulesConfig rules = null)
        {
            rules ??= new MatchRulesConfig();
            if (r == null || r.winner < 0 || r.a.Length != r.b.Length || r.a.Length == 0) return false;
            int need = rules.gamesToWin;
            int ga = 0, gb = 0;
            for (int i = 0; i < r.a.Length; i++)
            {
                if (ga >= need || gb >= need) return false; // đánh thêm game sau khi trận đã xong
                if (!MatchState.IsValidFinalGameScore(rules, r.a[i], r.b[i])) return false;
                if (r.a[i] > r.b[i]) ga++; else gb++;
            }
            return r.winner == 0 ? ga == need && gb < need : gb == need && ga < need;
        }

        /// <summary>Tạo kết quả từ trận 3D của người chơi (MatchState.CompletedGames).</summary>
        public static MatchResult FromGames(IList<GameScore> games, int winner)
        {
            var r = new MatchResult { winner = winner, a = new int[games.Count], b = new int[games.Count] };
            for (int i = 0; i < games.Count; i++) { r.a[i] = games[i].P0; r.b[i] = games[i].P1; }
            return r;
        }
    }
}
