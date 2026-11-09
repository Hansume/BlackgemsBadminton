using UnityEngine;

namespace Badminton
{
    /// <summary>
    /// Bóng đổ giả (blob shadow): một quad tròn mờ nằm trên sàn dưới target.
    /// Rẻ hơn rất nhiều so với shadow map real-time trên WebGL, và giúp người chơi
    /// cảm nhận được độ cao của quả cầu từ góc camera xa.
    /// </summary>
    // Chạy SAU ReplaySystem (10000) ⇒ trong replay bóng bám theo tư thế đang phát lại, không theo vị trí thật.
    [DefaultExecutionOrder(10100)]
    public class BlobShadow : MonoBehaviour
    {
        public Transform target;
        [Tooltip("Đường kính bóng khi target sát sàn (m).")]
        public float size = 0.8f;
        [Tooltip("Bóng nhỏ dần còn tỉ lệ này khi target ở độ cao fadeHeight.")]
        [Range(0f, 1f)] public float minScale = 0.4f;
        public float fadeHeight = 6f;
        public float floorY = 0.003f;

        Renderer _renderer;

        void LateUpdate()
        {
            // Nhân vật bị ẩn (vd. đồng đội đánh đôi khi đang đánh đơn) ⇒ ẩn luôn bóng.
            if (_renderer == null) _renderer = GetComponent<Renderer>();
            bool visible = target != null && target.gameObject.activeInHierarchy;
            if (_renderer != null && _renderer.enabled != visible) _renderer.enabled = visible;
            if (!visible) return;
            Vector3 p = target.position;
            float s = size * Mathf.Lerp(1f, minScale, Mathf.Clamp01(p.y / fadeHeight));
            transform.SetPositionAndRotation(new Vector3(p.x, floorY, p.z), Quaternion.Euler(90f, 0f, 0f));
            transform.localScale = new Vector3(s, s, 1f);
        }
    }
}
