namespace Badminton.Core
{
    /// <summary>
    /// Luật giao cầu ĐÁNH ĐÔI (tính điểm rally) – dùng cùng MatchState (MatchState tính theo ĐỘI 0/1).
    ///  • Ô giao cầu theo điểm của đội giao: chẵn ⇒ ô phải, lẻ ⇒ ô trái (MatchState.ServeCourt).
    ///  • Đội giao thắng điểm ⇒ người giao ĐỔI Ô với đồng đội rồi giao tiếp từ ô kia.
    ///  • Đội nhận thắng điểm ⇒ giành quyền giao, KHÔNG ai đổi ô; người giao là người đang đứng ở ô ứng với
    ///    điểm của đội mình.
    ///  • Người nhận là người của đội nhận đứng ở ô chéo (cùng tên phải/trái với ô người giao).
    ///  • Sang game mới: mỗi đội về vị trí ban đầu (thành viên 0 ở ô phải).
    /// Thành viên được đánh số 0/1 trong mỗi đội.
    /// </summary>
    public sealed class DoublesServeState
    {
        /// <summary>Thành viên (0/1) đang đứng ở ô PHẢI của mỗi đội.</summary>
        public readonly int[] RightMember = new int[2];

        public void Reset() { RightMember[0] = 0; RightMember[1] = 0; }

        /// <summary>
        /// Gọi SAU MatchState.AwardRally. serverBefore = đội giao cầu của pha vừa xong.
        /// </summary>
        public void OnRally(int winnerTeam, int serverBefore, bool gameEnded)
        {
            if (gameEnded) { Reset(); return; }
            if (winnerTeam == serverBefore) RightMember[winnerTeam] ^= 1;
        }

        public int MemberIn(int team, ServiceCourt court) => court == ServiceCourt.Right ? RightMember[team] : 1 - RightMember[team];

        public ServiceCourt CourtOf(int team, int member) => RightMember[team] == member ? ServiceCourt.Right : ServiceCourt.Left;

        public int ServerMember(MatchState m) => MemberIn(m.Server, m.ServeCourt);
        public int ReceiverMember(MatchState m) => MemberIn(m.Receiver, m.ServeCourt);
    }
}
