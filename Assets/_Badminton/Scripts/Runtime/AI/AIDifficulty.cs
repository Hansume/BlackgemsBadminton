using UnityEngine;

namespace Badminton.AI
{
    [CreateAssetMenu(menuName = "Badminton/AI Difficulty", fileName = "AI_")]
    public class AIDifficulty : ScriptableObject
    {
        public string displayName = "Thường";

        [Header("Phản xạ & di chuyển")]
        [Tooltip("Thời gian phản ứng sau khi đối thủ đánh (s).")]
        public float reactionTime = 0.25f;
        [Tooltip("Tốc độ chạy so với tối đa (0..1).")]
        [Range(0.3f, 1f)] public float speedFactor = 0.9f;
        [Tooltip("Sai số vị trí đứng so với điểm chạm lý tưởng (m).")]
        public float positionError = 0.25f;

        [Header("Đánh cầu")]
        [Tooltip("Sai số thời điểm bấm phím (s, độ lệch chuẩn). Lớn ⇒ hay bấm sớm/muộn ⇒ cú đánh kém.")]
        public float timingError = 0.06f;
        [Tooltip("Xác suất đập khi cầu đủ cao.")]
        [Range(0f, 1f)] public float smashChance = 0.45f;
        [Tooltip("Mức độ đưa cầu ra xa đối thủ (0 = đánh giữa sân, 1 = luôn nhắm góc).")]
        [Range(0f, 1f)] public float placement = 0.6f;

        [Header("Phán đoán")]
        [Tooltip("Xác suất nhận ra cầu sẽ ra ngoài và bỏ không đánh.")]
        [Range(0f, 1f)] public float lineJudgement = 0.6f;
        [Tooltip("Xác suất 'đứng hình' bỏ lỡ hoàn toàn một cú (lỗi ngớ ngẩn).")]
        [Range(0f, 0.3f)] public float blunderChance = 0.05f;
    }
}
