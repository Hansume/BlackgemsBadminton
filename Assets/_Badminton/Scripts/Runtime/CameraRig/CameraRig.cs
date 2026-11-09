using Badminton.Core;
using UnityEngine;

namespace Badminton.Cameras
{
    /// <summary>
    /// Camera cố định kiểu truyền hình: đặt sau + trên đường biên cuối của một nửa sân,
    /// nhìn dọc sân về phía lưới. Chỉ pan ngang rất nhẹ theo quả cầu.
    ///
    /// - Offline / broadcast: đặt ở nửa sân Near.
    /// - Online: mỗi client gọi SetLocalSide(side của mình) để người chơi local luôn ở dưới màn hình.
    ///   Bật broadcastCamera để ép về góc truyền hình cố định (Near, không pan).
    ///
    /// [ExecuteAlways] để chỉnh thông số trong Edit mode và thấy kết quả ngay trong Game view.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Camera))]
    public class CameraRig : MonoBehaviour
    {
        public CameraRigSettings settings;

        [Tooltip("Nửa sân mà camera đứng phía sau (online: nửa sân của người chơi local).")]
        public CourtSide localSide = CourtSide.Near;

        [Tooltip("Góc truyền hình cố định: luôn đứng sau nửa Near, không pan.")]
        public bool broadcastCamera;

        [Tooltip("Đối tượng để pan theo (quả cầu). Có thể để trống.")]
        public Transform panTarget;

        Camera _cam;
        float _panX;          // độ lệch pan hiện tại (m, theo trục X thế giới)
        float _panVelocity;   // dùng cho SmoothDamp

        public CourtSide EffectiveSide => broadcastCamera ? CourtSide.Near : localSide;

        /// <summary>Cộng thêm vào FOV (độ) – đánh đôi mở rộng nhẹ để thấy đủ 4 người.</summary>
        [System.NonSerialized] public float extraFov;

        public void SetLocalSide(CourtSide side) { localSide = side; SnapNow(); }
        public void SetBroadcast(bool on) { broadcastCamera = on; SnapNow(); }

        /// <summary>Đặt camera ngay vào vị trí đích, bỏ qua smoothing (dùng khi đổi sân).</summary>
        public void SnapNow()
        {
            _panX = TargetPanX();
            _panVelocity = 0f;
            Apply();
        }

        void OnEnable()
        {
            _cam = GetComponent<Camera>();
            SnapNow();
        }

        void LateUpdate()
        {
            if (settings == null) return;

            if (Application.isPlaying)
                _panX = Mathf.SmoothDamp(_panX, TargetPanX(), ref _panVelocity, settings.panSmoothTime);
            else
                _panX = 0f; // Edit mode: luôn ở giữa để dễ căn chỉnh

            Apply();
        }

        float TargetPanX()
        {
            if (settings == null || broadcastCamera || panTarget == null) return 0f;
            return Mathf.Clamp(panTarget.position.x * settings.panFollowFactor, -settings.panLimit, settings.panLimit);
        }

        void Apply()
        {
            if (settings == null) return;
            if (_cam == null) _cam = GetComponent<Camera>();

            float s = (float)EffectiveSide; // -1 (Near) hoặc +1 (Far)

            // Vị trí: sau đường biên cuối của nửa sân `s`, cao `height`.
            var pos = new Vector3(_panX, settings.height, s * (CourtDimensions.HalfLength + settings.distanceBehindBaseline));

            // Hướng nhìn: quay mặt về lưới (yaw 0 nếu ở Near, 180 nếu ở Far), cúi xuống `pitch` độ.
            float yaw = s < 0 ? 0f : 180f;
            var rot = Quaternion.Euler(settings.pitch, yaw, 0f);

            transform.SetPositionAndRotation(pos, rot);
            _cam.fieldOfView = ComputeFov(pos, rot);
        }

        /// <summary>
        /// FOV dọc. Nếu màn hình hẹp, tăng FOV đủ để 2 góc biên cuối GẦN camera (chỗ sân trông rộng nhất
        /// trên màn hình) vẫn nằm trong khung hình theo chiều ngang.
        /// Công thức: điểm p trong hệ toạ độ camera nằm trong khung ngang khi |p.x|/p.z ≤ tan(hFov/2),
        /// và tan(hFov/2) = tan(vFov/2) · aspect.
        /// </summary>
        float ComputeFov(Vector3 camPos, Quaternion camRot)
        {
            float fov = settings.fieldOfView + extraFov;
            if (!settings.keepCourtWidthOnNarrowScreens || _cam.aspect <= 0f) return fov;

            float s = (float)EffectiveSide;
            float halfW = CourtDimensions.HalfDoublesWidth + settings.sideMargin;
            float z = s * CourtDimensions.HalfLength;
            var inv = Quaternion.Inverse(camRot);

            // Khi pan, một góc sẽ gần mép khung hơn ⇒ xét cả 2 góc, lấy góc lệch nhiều nhất.
            Vector3 pR = inv * (new Vector3(+halfW, 0f, z) - camPos);
            Vector3 pL = inv * (new Vector3(-halfW, 0f, z) - camPos);
            if (pR.z <= 0.01f) return fov;

            float maxTan = Mathf.Max(Mathf.Abs(pR.x) / pR.z, Mathf.Abs(pL.x) / pL.z);
            float neededTanHalfV = maxTan / _cam.aspect;
            float neededFov = 2f * Mathf.Atan(neededTanHalfV) * Mathf.Rad2Deg;
            return Mathf.Clamp(Mathf.Max(fov, neededFov), 1f, 100f);
        }

#if UNITY_EDITOR
        // Vẽ khung để thấy vị trí camera trong Scene view.
        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(transform.position, new Vector3(transform.position.x, 0f, transform.position.z));
        }
#endif
    }
}
