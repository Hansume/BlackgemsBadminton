using UnityEngine;

namespace Badminton.Cameras
{
    /// <summary>
    /// Thông số góc máy kiểu truyền hình. Sửa trực tiếp asset này trong Inspector
    /// (kể cả khi đang Play hoặc ở Edit mode) – camera cập nhật ngay.
    ///
    /// Giá trị mặc định (16:9): biên cuối xa ≈ 22% từ mép trên, dải trắng lưới ≈ 34%,
    /// biên cuối gần ≈ 93%. Muốn lưới xuống ~40% thì cần height ≈ 10 m (ngoài khoảng 7–9 m của spec).
    /// </summary>
    [CreateAssetMenu(menuName = "Badminton/Camera Rig Settings", fileName = "CameraRigSettings")]
    public class CameraRigSettings : ScriptableObject
    {
        [Header("Vị trí (tính từ đường biên cuối phía camera)")]
        [Tooltip("Khoảng cách ra phía sau đường biên cuối (m).")]
        [Range(3f, 15f)] public float distanceBehindBaseline = 7f;

        [Tooltip("Độ cao camera so với mặt sân (m).")]
        [Range(3f, 15f)] public float height = 9f;

        [Header("Ống kính")]
        [Tooltip("Góc nghiêng xuống (độ). Tăng ⇒ lưới dịch lên trên màn hình.")]
        [Range(20f, 60f)] public float pitch = 35f;

        [Tooltip("FOV dọc (độ). Nhỏ ⇒ ống kính tele, sân trông thon, phối cảnh nén hơn.")]
        [Range(20f, 60f)] public float fieldOfView = 39f;

        [Tooltip("Màn hình hẹp hơn 16:9 (cửa sổ trình duyệt dọc...) thì tự mở rộng FOV để không cắt biên dọc sân.")]
        public bool keepCourtWidthOnNarrowScreens = true;

        [Tooltip("Lề thêm 2 bên sân khi tự tính FOV cho màn hình hẹp (m).")]
        [Range(0f, 2f)] public float sideMargin = 0.4f;

        [Header("Pan ngang theo cầu")]
        [Tooltip("Biên độ pan ngang tối đa (m).")]
        [Range(0f, 2f)] public float panLimit = 0.5f;

        [Tooltip("Tỉ lệ: cầu lệch 1 m sang ngang ⇒ camera pan bao nhiêu m (trước khi giới hạn).")]
        [Range(0f, 1f)] public float panFollowFactor = 0.2f;

        [Tooltip("Thời gian làm mượt pan (s). Lớn ⇒ camera chậm, mềm.")]
        [Range(0.05f, 2f)] public float panSmoothTime = 0.6f;
    }
}
