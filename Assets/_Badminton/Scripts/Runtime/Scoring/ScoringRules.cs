using Badminton.Core;
using UnityEngine;

namespace Badminton.Scoring
{
    /// <summary>
    /// NGUỒN LUẬT DUY NHẤT cho mọi chế độ (online, đấu với AI, giải đấu): luật tính điểm 3×21 + nhịp nghỉ.
    /// Luật điểm (21 điểm, cách 2, trần 30, 3 game thắng 2, nghỉ ở 11) nằm trong <see cref="rules"/>
    /// và được MatchState (Core, thuần C#) áp dụng – cùng một code cho host, client, AI và unit test.
    ///
    /// Thời gian nghỉ có 2 bộ:
    ///  - "Chính thức" (60 s / 120 s, đúng luật) – dùng cho chế độ GIẢI ĐẤU (offline), có nút bỏ qua.
    ///  - "Nhanh" (vài giây) – dùng cho online và "Đấu với AI" như trước đây (không đổi hành vi cũ).
    /// MatchController chọn bộ nào qua cờ UseOfficialBreaks.
    /// </summary>
    [CreateAssetMenu(menuName = "Badminton/Scoring Rules", fileName = "ScoringRules")]
    public class ScoringRules : ScriptableObject
    {
        [Tooltip("Luật tính điểm: 21 điểm/game, thắng cách 2, trần 30 (29–29 thì điểm kế tiếp quyết định), " +
                 "3 game thắng 2, nghỉ khi bên dẫn đạt 11, game quyết định đổi sân ở 11.")]
        public MatchRulesConfig rules = new MatchRulesConfig();

        [Header("Nhịp trận")]
        [Tooltip("Dừng sau mỗi điểm để xem kết quả (s).")]
        public float pointPause = 1.6f;

        [Header("Nghỉ NHANH (online, đấu với AI)")]
        [Tooltip("Nghỉ khi bên dẫn đạt 11 điểm (s).")]
        public float intervalPause = 4f;
        [Tooltip("Nghỉ giữa 2 game (s).")]
        public float gameBreak = 5f;

        [Header("Nghỉ CHÍNH THỨC (giải đấu – luật thật)")]
        [Tooltip("Nghỉ khi bên dẫn đạt 11 điểm (s). Luật thật: 60 s.")]
        public float officialIntervalPause = 60f;
        [Tooltip("Nghỉ giữa 2 game (s). Luật thật: 120 s.")]
        public float officialGameBreak = 120f;
        [Tooltip("Cho phép người chơi bấm 'BỎ QUA NGHỈ' (chỉ trận offline).")]
        public bool allowSkipBreak = true;
        [Tooltip("Không cho bỏ qua trong vài giây đầu (s) – tránh bấm nhầm khi đang đánh.")]
        public float minBreakBeforeSkip = 1.5f;

        public float IntervalPause(bool official) => official ? officialIntervalPause : intervalPause;
        public float GameBreak(bool official) => official ? officialGameBreak : gameBreak;
    }
}
