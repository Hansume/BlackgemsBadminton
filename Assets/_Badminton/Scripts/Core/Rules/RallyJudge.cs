using UnityEngine;

namespace Badminton.Core
{
    public enum RallyEndReason : byte
    {
        In,              // cầu rơi trong sân đối phương ⇒ người đánh thắng
        Out,             // cầu rơi ngoài sân ⇒ người đánh thua
        NotOverNet,      // cầu chạm lưới rơi về sân mình / không qua lưới
        UnderNet,        // cầu chui dưới lưới
        ServiceFault,    // giao cầu không vào đúng ô chéo (sai ô, ngắn hơn vạch giao cầu ngắn, ra ngoài)
        WrongReceiver,   // đánh đôi: cú giao cầu bị đỡ bởi người KHÔNG phải người nhận ⇒ đội nhận mất điểm
    }

    public struct RallyResult
    {
        public int Winner;
        public RallyEndReason Reason;
        public int Faulter;          // người mắc lỗi / để thua điểm
        public Vector3 LandingPoint;
    }

    /// <summary>
    /// Trọng tài cho MỘT pha cầu: nhận các sự kiện (giao cầu, cú đánh, cầu chạm sàn, cầu chui lưới)
    /// và quyết định ai thắng điểm. Thuần C# ⇒ server dùng để phán quyết, client không tự quyết được.
    /// </summary>
    public sealed class RallyJudge
    {
        int _server;
        ServiceCourt _serveCourt;
        CourtSide[] _sides;
        int _lastHitter = -1;
        int _hits;
        bool _doubles;
        int _receiverMember = -1;

        public bool Finished { get; private set; }
        public int Hits => _hits;
        public int LastHitter => _lastHitter;

        /// <param name="sides">Nửa sân của người chơi (đội) 0 và 1 trong pha cầu này.</param>
        /// <param name="doubles">Đánh đôi: dùng ô giao cầu đôi (ngắn hơn, rộng hơn) và biên dọc ngoài.
        /// Khi đó "người chơi" 0/1 là ĐỘI 0/1.</param>
        /// <param name="receiverMember">Đánh đôi: thành viên (0/1) của đội nhận được phép đỡ giao cầu (-1 = không kiểm tra).</param>
        public void BeginRally(int server, ServiceCourt serveCourt, CourtSide[] sides, bool doubles = false, int receiverMember = -1)
        {
            _doubles = doubles;
            _receiverMember = doubles ? receiverMember : -1;
            _server = server;
            _serveCourt = serveCourt;
            _sides = new[] { sides[0], sides[1] };
            _lastHitter = -1;
            _hits = 0;
            Finished = false;
        }

        public void OnHit(int hitter)
        {
            if (Finished) return;
            _lastHitter = hitter;
            _hits++;
        }

        /// <summary>
        /// Cú đánh có biết thành viên nào đánh (đánh đôi). Trả về kết quả nếu cú đánh phạm luật:
        /// cú đỡ giao cầu (cú thứ 2) phải do đúng người nhận thực hiện.
        /// </summary>
        public RallyResult? OnHit(int team, int member)
        {
            if (Finished) return null;
            OnHit(team);
            if (_hits == 2 && _receiverMember >= 0 && team != _server && member != _receiverMember)
                return End(_server, team, RallyEndReason.WrongReceiver, default);
            return null;
        }

        /// <summary>Cầu chui dưới lưới ⇒ người đánh cuối mắc lỗi.</summary>
        public RallyResult? OnUnderNet()
        {
            if (Finished || _lastHitter < 0) return null;
            return End(MatchState.Other(_lastHitter), _lastHitter, _hits == 1 ? RallyEndReason.ServiceFault : RallyEndReason.UnderNet, default);
        }

        /// <summary>Cầu chạm sàn lần đầu tại p.</summary>
        public RallyResult? OnLanded(Vector3 p)
        {
            if (Finished || _lastHitter < 0) return null;

            int hitter = _lastHitter;
            int opponent = MatchState.Other(hitter);
            CourtSide landSide = CourtDimensions.SideOf(p.z);

            // Rơi ngay bên sân của người đánh ⇒ cầu không qua lưới.
            if (landSide == _sides[hitter])
                return End(opponent, hitter, _hits == 1 ? RallyEndReason.ServiceFault : RallyEndReason.NotOverNet, p);

            if (_hits == 1)
            {
                // Cú giao cầu: phải rơi vào ô giao cầu chéo của người nhận (tính cả vạch).
                bool inBox = CourtDimensions.IsInsideServiceCourt(p.x, p.z, _sides[opponent], _serveCourt, _doubles);
                return inBox
                    ? End(hitter, opponent, RallyEndReason.In, p)
                    : End(opponent, hitter, RallyEndReason.ServiceFault, p);
            }

            return CourtDimensions.IsInside(p.x, p.z, _doubles)
                ? End(hitter, opponent, RallyEndReason.In, p)
                : End(opponent, hitter, RallyEndReason.Out, p);
        }

        RallyResult End(int winner, int faulter, RallyEndReason reason, Vector3 p)
        {
            Finished = true;
            return new RallyResult { Winner = winner, Faulter = faulter, Reason = reason, LandingPoint = p };
        }
    }
}
