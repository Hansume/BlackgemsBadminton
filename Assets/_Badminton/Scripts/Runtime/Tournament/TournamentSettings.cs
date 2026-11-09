using Badminton.AI;
using Badminton.Tour;
using UnityEngine;

namespace Badminton.TournamentMode
{
    /// <summary>
    /// Toàn bộ cấu hình chế độ Giải đấu – đặt tại Resources/Tournament/TournamentSettings.asset để tự nạp lúc chạy.
    /// Mọi con số cân bằng (hệ số rating, phân bố tỷ số, điểm thưởng, lịch, số VĐV…) nằm trong "balance".
    /// </summary>
    /// <summary>Nhịp hiệu ứng / chuyển màn hình của chế độ Giải đấu.</summary>
    [System.Serializable]
    public class TourUiConfig
    {
        [Tooltip("Hiệu ứng lật ô kết quả: thời gian úp (s).")] public float revealFlipOut = 0.12f;
        [Tooltip("Hiệu ứng lật ô kết quả: thời gian mở (s).")] public float revealFlipIn = 0.14f;
        [Tooltip("Nghỉ giữa 2 kết quả được công bố (s).")] public float revealGap = 0.22f;
        [Tooltip("Trận 3D kết thúc → chờ bao lâu rồi về màn hình sau trận (s).")] public float afterMatchDelay = 3f;
        [Tooltip("Màn hình sau trận tự chuyển sang nhánh đấu sau (s).")] public float postMatchAutoContinue = 3.5f;
        [Tooltip("Dừng một chút trước khi vẽ vòng tiếp theo (s).")] public float advanceDelay = 0.35f;
        [Tooltip("Màu áo 2 bên giống nhau hơn ngưỡng này (tổng chênh RGB) ⇒ đổi áo đối thủ.")] public float kitClashThreshold = 0.45f;
    }

    [CreateAssetMenu(menuName = "Badminton/Giải đấu/Tournament Settings", fileName = "TournamentSettings")]
    public class TournamentSettings : ScriptableObject
    {
        public const string ResourcePath = "Tournament/TournamentSettings";

        public AthleteDatabaseAsset database;
        [Tooltip("4 bậc độ khó AI theo thứ tự: Dễ, Thường, Khó, Rất khó.")]
        public AIDifficulty[] aiTiers = new AIDifficulty[4];
        [Tooltip("Mọi con số cân bằng của chế độ Giải đấu.")]
        public TourBalance balance = new TourBalance();
        [Tooltip("Thông số đánh đôi: AI đồng đội, vị trí giao/nhận, camera.")]
        public DoublesConfig doubles = new DoublesConfig();
        [Tooltip("Nhịp hiệu ứng giao diện (công bố kết quả, chuyển màn hình…).")]
        public TourUiConfig ui = new TourUiConfig();

        static TournamentSettings _cached;

        public static TournamentSettings Load()
        {
            if (_cached == null) _cached = Resources.Load<TournamentSettings>(ResourcePath);
            return _cached;
        }

        public AIDifficulty DifficultyFor(int rating)
        {
            int t = balance.difficulty.Tier(rating);
            for (int i = t; i >= 0; i--) if (aiTiers != null && i < aiTiers.Length && aiTiers[i] != null) return aiTiers[i];
            return null;
        }
    }
}
