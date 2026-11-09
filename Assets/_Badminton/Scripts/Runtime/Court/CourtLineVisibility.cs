using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Badminton.Courts
{
    /// <summary>
    /// Giữ cho vạch ngang (biên cuối, giao cầu dài, giao cầu ngắn) không "biến mất" ở cuối sân xa camera.
    ///
    /// Vạch thật dày 4 cm; ở cuối sân đối diện, nhìn nghiêng từ camera, nó chỉ còn ~0.5–0.8 pixel.
    /// Không có khử răng cưa (bản web chất lượng THẤP: render scale 0.7, không MSAA) thì vạch có thể lọt
    /// giữa 2 hàng pixel và không được vẽ – ví dụ ở 896×504 vạch giao cầu dài bên kia mất hẳn.
    ///
    /// Mỗi frame: đo độ dày vạch trên màn hình (pixel thật sau render scale); nếu mỏng hơn
    /// <see cref="minPixels"/> thì làm dày vạch ra phía lưới, giữ nguyên mép phía cuối sân.
    /// Vạch gần camera đủ dày nên giữ đúng 4 cm. Chỉ ảnh hưởng hình ảnh – xét trong/ngoài dùng CourtDimensions.
    /// </summary>
    [DefaultExecutionOrder(20000)] // sau CameraRig
    public class CourtLineVisibility : MonoBehaviour
    {
        [Tooltip("Độ dày tối thiểu của vạch trên màn hình (pixel render thật).")]
        public float minPixels = 1.5f;
        [Tooltip("Không làm dày quá số lần này so với vạch thật.")]
        public float maxScale = 6f;

        struct LineInfo
        {
            public Transform t;
            public Vector3 basePos, baseScale;
            public float sign; // dấu z của nửa sân chứa vạch (+1 Far, −1 Near)
        }

        readonly List<LineInfo> _lines = new List<LineInfo>();

        void Awake()
        {
            // Vạch ngang = mỏng theo trục Z, dài theo trục X.
            foreach (Transform t in transform)
            {
                Vector3 s = t.localScale;
                if (s.z >= s.x) continue;
                _lines.Add(new LineInfo
                {
                    t = t, basePos = t.localPosition, baseScale = s,
                    sign = t.position.z >= 0f ? 1f : -1f,
                });
            }
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null) return;

            float renderScale = 1f;
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp) renderScale = urp.renderScale;
            float pixelH = cam.pixelHeight * renderScale;

            foreach (var l in _lines)
            {
                float half = l.baseScale.z * 0.5f;
                Vector3 c = transform.TransformPoint(l.basePos);
                c.x = Mathf.Clamp(cam.transform.position.x, -2f, 2f); // điểm trên vạch gần trục nhìn
                Vector3 a = cam.WorldToViewportPoint(c - Vector3.forward * half);
                Vector3 b = cam.WorldToViewportPoint(c + Vector3.forward * half);
                if (a.z <= 0f || b.z <= 0f) continue; // sau camera

                float px = Mathf.Abs(a.y - b.y) * pixelH;
                float k = px > 0.0001f ? Mathf.Clamp(minPixels / px, 1f, maxScale) : maxScale;

                // Làm dày về phía lưới, giữ nguyên mép phía cuối sân (mép ngoài của biên cuối = biên sân).
                float extra = l.baseScale.z * (k - 1f);
                l.t.localScale = new Vector3(l.baseScale.x, l.baseScale.y, l.baseScale.z * k);
                l.t.localPosition = l.basePos - new Vector3(0f, 0f, l.sign * extra * 0.5f);
            }
        }
    }
}
