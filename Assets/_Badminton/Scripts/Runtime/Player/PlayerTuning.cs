using UnityEngine;

namespace Badminton.Player
{
    [CreateAssetMenu(menuName = "Badminton/Player Tuning", fileName = "PlayerTuning")]
    public class PlayerTuning : ScriptableObject
    {
        [Header("Di chuyển")]
        [Range(1f, 10f)] public float maxSpeed = 5.5f;
        [Tooltip("Gia tốc khi bắt đầu chạy / đổi hướng (m/s²).")]
        public float acceleration = 28f;
        [Tooltip("Giảm tốc khi thả phím (m/s²). Lớn hơn acceleration ⇒ dừng gắt như bước chặn chân.")]
        public float deceleration = 40f;
        [Tooltip("Tốc độ xoay người (độ/giây).")]
        public float turnSpeed = 540f;

        [Header("Giới hạn vùng di chuyển")]
        [Tooltip("Khoảng cách tối thiểu tới lưới (m) – không bao giờ chạm lưới.")]
        public float netClearance = 0.35f;
        [Tooltip("Được chạy ra sau đường biên cuối bao xa (m).")]
        public float backMargin = 1.5f;
        [Tooltip("Được chạy ra ngoài biên dọc đánh đôi bao xa (m).")]
        public float sideMargin = 0.8f;

        [Header("Tầm với của vợt")]
        [Tooltip("Tâm vùng với tới: độ cao so với sàn (m).")]
        public float hitCenterHeight = 1.4f;
        [Tooltip("Tâm vùng với tới: lệch về phía trước người (m).")]
        public float hitCenterForward = 0.3f;
        [Tooltip("Bán kính vùng với tới quanh vợt (m).")]
        public float reach = 1.2f;

        [Header("Đánh cầu")]
        [Tooltip("Bấm phím sớm tối đa bao lâu trước khi cầu tới vẫn được tính (s).")]
        public float inputBuffer = 0.25f;
        [Tooltip("Thời gian tối thiểu giữa 2 cú đánh (s).")]
        public float hitCooldown = 0.3f;
    }
}
