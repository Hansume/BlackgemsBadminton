using Badminton.Core;
using Badminton.Player;
using UnityEngine;

namespace Badminton.AI
{
    /// <summary>Thông số đánh đôi (chỉnh trong TournamentSettings.asset → doubles).</summary>
    [System.Serializable]
    public class DoublesConfig
    {
        [Header("AI đồng đội")]
        [Tooltip("AI chỉ giành quả cầu khi gần điểm chạm hơn NGƯỜI CHƠI ít nhất ngần này (m). Lớn ⇒ nhường người chơi nhiều hơn.")]
        public float humanPriority = 1.0f;
        [Tooltip("Hai AI cùng đội: chênh lệch khoảng cách dưới ngưỡng này (m) coi như bằng nhau (thành viên 0 đánh).")]
        public float aiTieMargin = 0.05f;
        [Tooltip("Đội hình TẤN CÔNG (trước–sau): người trên đứng cách lưới (m).")]
        public float frontDepth = 2.4f;
        [Tooltip("Đội hình TẤN CÔNG: người sau đứng cách lưới (m).")]
        public float backDepth = 4.9f;
        [Tooltip("Đội hình PHÒNG THỦ (song song): khoảng cách tới lưới (m).")]
        public float defenceDepth = 3.9f;
        [Tooltip("Đội hình PHÒNG THỦ: mỗi người lệch khỏi đường giữa (m).")]
        public float defenceHalfWidth = 1.35f;

        [Header("Giao cầu đôi")]
        [Tooltip("Độ sâu tối đa cú giao cầu cao (m tính từ lưới). Vạch giao cầu dài đánh đôi ở 5.94 m.")]
        public float serveDepth = 5.35f;
        [Tooltip("Điểm nhắm giao cầu: lệch khỏi đường giữa (m) về phía ô chéo.")]
        public float serveTargetX = 1.5f;
        [Tooltip("Vị trí đứng: lệch khỏi đường giữa (m).")]
        public float standX = 0.9f;
        [Tooltip("Người giao đứng cách lưới (m).")] public float serverDepth = 2.5f;
        [Tooltip("Người nhận đứng cách lưới (m).")] public float receiverDepth = 2.6f;
        [Tooltip("Đồng đội người giao đứng cách lưới (m).")] public float serverPartnerDepth = 4.6f;
        [Tooltip("Đồng đội người nhận đứng cách lưới (m).")] public float receiverPartnerDepth = 4.0f;

        [Header("Camera")]
        [Tooltip("Cộng thêm vào FOV khi đánh đôi để thấy đủ 4 người (độ).")]
        public float cameraExtraFov = 3f;
    }

    /// <summary>
    /// Điều phối một ĐỘI đánh đôi (2 thành viên, có thể có 1 người chơi thật):
    ///  • Đội hình: TẤN CÔNG (trước–sau) khi đội mình vừa đánh cầu cắm xuống (smash/drop/bỏ nhỏ) hoặc đối thủ
    ///    vừa đánh cầu cao sang; PHÒNG THỦ (song song trái–phải) khi đội mình vừa đánh cầu cao hoặc đối thủ tấn công.
    ///  • Ai đánh quả nào: người gần điểm chạm hơn. Nếu đồng đội là NGƯỜI CHƠI thì AI nhường – chỉ đánh khi gần hơn
    ///    người chơi ít nhất humanPriority mét (cầu ở vùng giữa hai người ⇒ để người chơi đánh, không giành cầu).
    ///  • Đỡ giao cầu: chỉ người nhận được đánh.
    /// </summary>
    public sealed class DoublesTeam
    {
        public readonly PlayerMotor[] motors = new PlayerMotor[2];
        public readonly AIController[] ais = new AIController[2];
        public readonly bool[] human = new bool[2];

        /// <summary>Đang đỡ giao cầu: chỉ thành viên này được đánh (-1 = không giới hạn).</summary>
        public int receiverOnly = -1;
        public bool attacking;

        public DoublesConfig cfg = new DoublesConfig();

        int MemberOf(AIController ai) => ais[0] == ai ? 0 : 1;

        static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

        bool Active(int m) => motors[m] != null && motors[m].gameObject.activeInHierarchy;

        /// <summary>AI này có nên đánh quả cầu sẽ tới chỗ footPoint không.</summary>
        public bool ShouldTake(AIController ai, Vector3 footPoint)
        {
            int me = MemberOf(ai), mate = 1 - me;
            if (receiverOnly >= 0) return receiverOnly == me;
            if (!Active(mate)) return true;
            float dMe = Flat(motors[me].Position, footPoint), dMate = Flat(motors[mate].Position, footPoint);
            if (human[mate]) return dMe + cfg.humanPriority < dMate;
            return dMe < dMate - cfg.aiTieMargin || (Mathf.Abs(dMe - dMate) <= cfg.aiTieMargin && me == 0);
        }

        /// <summary>Đội mình vừa đánh cầu.</summary>
        public void OnTeamHit(ShotType t)
        {
            receiverOnly = -1;
            attacking = t == ShotType.Smash || t == ShotType.Drop || t == ShotType.NetShot || t == ShotType.ShortServe;
        }

        /// <summary>Đối thủ vừa đánh cầu (cú giao cầu: giữ quyền đỡ của người nhận).</summary>
        public void OnOpponentHit(ShotType t)
        {
            if (t != ShotType.Serve && t != ShotType.ShortServe) receiverOnly = -1;
            attacking = t == ShotType.Clear || t == ShotType.Lift || t == ShotType.Serve;
        }

        /// <summary>Vị trí chờ của AI theo đội hình hiện tại.</summary>
        public Vector3 BasePosition(AIController ai)
        {
            int me = MemberOf(ai), mate = 1 - me;
            var m = motors[me];
            float s = (int)m.side;
            float right = CourtDimensions.RightSign(m.side);
            if (!Active(mate)) return new Vector3(0f, 0f, s * ai.baseDepth);

            var p = m.Position; var q = motors[mate].Position;
            if (attacking)
            {
                // Người đang gần lưới hơn đứng trên, người kia đứng sau (đứng giữa sân theo chiều ngang).
                bool iAmFront = Mathf.Abs(p.z) < Mathf.Abs(q.z) - 0.05f || (Mathf.Abs(Mathf.Abs(p.z) - Mathf.Abs(q.z)) <= 0.05f && me == 1);
                return new Vector3(0f, 0f, s * (iAmFront ? cfg.frontDepth : cfg.backDepth));
            }
            // Phòng thủ: người đang ở bên phải (theo góc nhìn đội) giữ nửa phải, người kia nửa trái.
            bool iAmRight = p.x * right > q.x * right || (Mathf.Approximately(p.x, q.x) && me == 0);
            return new Vector3((iAmRight ? 1f : -1f) * right * cfg.defenceHalfWidth, 0f, s * cfg.defenceDepth);
        }
    }
}
