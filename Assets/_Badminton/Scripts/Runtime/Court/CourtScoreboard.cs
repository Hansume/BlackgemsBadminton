using TMPro;
using UnityEngine;

namespace Badminton.Courts
{
    /// <summary>
    /// Bảng điểm điện tử nhỏ đặt cạnh sân, hiển thị ở CẢ HAI mặt (camera có thể ở sau Near hoặc Far).
    /// </summary>
    public class CourtScoreboard : MonoBehaviour
    {
        public TMP_Text leftScore;
        public TMP_Text rightScore;
        [Tooltip("Chữ ở mặt sau (nhìn từ phía Far).")]
        public TMP_Text leftScoreBack;
        public TMP_Text rightScoreBack;

        public void SetScore(int left, int right)
        {
            Set(leftScore, left);
            Set(rightScore, right);
            // Mặt sau nhìn từ phía đối diện ⇒ đảo trái/phải để vẫn đọc theo cùng thứ tự người chơi.
            Set(leftScoreBack, right);
            Set(rightScoreBack, left);
        }

        static void Set(TMP_Text t, int v) { if (t != null) t.text = v.ToString(); }
    }
}
