using Badminton.Core;
using UnityEngine;

namespace Badminton.Shots
{
    /// <summary>Bộ cú đánh dùng cho một nhân vật + các thông số timing chung.</summary>
    [CreateAssetMenu(menuName = "Badminton/Shot Library", fileName = "ShotLibrary")]
    public class ShotLibrary : ScriptableObject
    {
        public ShotDefinition clear, smash, drop, netShot, lift, serve, shortServe;

        // Phím J = cầu DÀI: cầu ≥ clear.minContactHeight ⇒ Clear (qua đầu), thấp hơn ⇒ Lift (vén).
        // Phím L = cầu NGẮN: cầu ≥ drop.minContactHeight ⇒ Drop (qua đầu), thấp hơn ⇒ Net shot.
        // Ngưỡng "qua đầu" chỉnh ở minContactHeight của asset Clear / Drop.

        [Header("Net shot khi đứng xa lưới (cú đỡ ngắn)")]
        [Tooltip("Điểm chạm cầu cách lưới xa hơn mức này ⇒ Net shot thành cú đỡ ngắn.")]
        public float netShotMaxDistance = 2.6f;
        [Tooltip("Cú đỡ ngắn: điểm rơi cách lưới (m).")]
        public float blockDepth = 1.5f;
        [Tooltip("Cú đỡ ngắn: nhân độ lệch điểm rơi (kém chính xác hơn net shot sát lưới).")]
        public float blockScatterMul = 1.5f;
        [Tooltip("Cú đỡ ngắn: tốc độ tối đa (m/s) – đủ để cầu qua lưới từ giữa/cuối sân.")]
        public float blockSpeed = 25f;

        [Header("Timing")]
        [Tooltip("Bấm trước điểm chạm trong khoảng này (s) vẫn tính là hoàn hảo.")]
        public float perfectWindow = 0.08f;
        [Tooltip("Cầu cách tâm vùng với tới ít hơn tỉ lệ này × reach ⇒ chạm đúng 'điểm ngọt'.")]
        [Range(0.1f, 1f)] public float sweetSpot = 0.45f;
        [Tooltip("Bấm sớm tối đa: quality còn lại thấp nhất do bấm sớm.")]
        [Range(0f, 1f)] public float earlyFloor = 0.35f;

        [Header("Nhảy đập")]
        [Tooltip("Smash cho phép với cao thêm bao nhiêu mét nhờ bật nhảy.")]
        public float jumpReach = 0.55f;

        public ShotDefinition Get(ShotType t) => t switch
        {
            ShotType.Clear => clear,
            ShotType.Smash => smash,
            ShotType.Drop => drop,
            ShotType.NetShot => netShot,
            ShotType.Lift => lift,
            ShotType.Serve => serve,
            ShotType.ShortServe => shortServe != null ? shortServe : serve,
            _ => null,
        };

        /// <summary>
        /// Đổi phím bấm thành kỹ thuật thực tế theo độ cao cầu lúc chạm:
        /// Clear → Clear/Lift, Drop → Drop/NetShot. Các cú khác giữ nguyên.
        /// </summary>
        public ShotType Resolve(ShotType t, float shuttleHeight)
        {
            if (t == ShotType.Clear && clear != null && shuttleHeight < clear.minContactHeight) return ShotType.Lift;
            if (t == ShotType.Drop && drop != null && shuttleHeight < drop.minContactHeight) return ShotType.NetShot;
            return t;
        }

        /// <summary>
        /// Thông số cú đánh tại điểm chạm. Net shot đánh từ xa lưới ⇒ cú đỡ ngắn (rơi xa lưới hơn, kém chính xác).
        /// Chỉ dựa vào điểm chạm ⇒ server tái tạo cú đánh của client ra cùng kết quả.
        /// </summary>
        public ShotParams ParamsFor(ShotType t, Vector3 contact)
        {
            var p = Get(t).parameters;
            if (t == ShotType.NetShot && Mathf.Abs(contact.z) > netShotMaxDistance)
            {
                p.targetDepth = blockDepth;
                p.scatter *= blockScatterMul;
                p.badScatter *= blockScatterMul;
                p.speed = Mathf.Max(p.speed, blockSpeed);
            }
            return p;
        }
    }
}
