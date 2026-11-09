using Badminton.Core;
using UnityEngine;

namespace Badminton.Shuttle
{
    /// <summary>
    /// Vòng kết quả trên sàn sau khi cầu chạm sàn (xanh = trong, đỏ = ngoài).
    /// Điểm rơi khi cầu đang bay do <see cref="Badminton.Shots.TimingRing"/> hiển thị (vòng thu nhỏ dần).
    /// </summary>
    public class LandingMarker : MonoBehaviour
    {
        public Shuttlecock shuttle;
        public Renderer resultRing;
        public float resultShowTime = 2.5f;

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly Color InColor = new Color(0.2f, 1f, 0.35f);
        static readonly Color OutColor = new Color(1f, 0.2f, 0.2f);

        MaterialPropertyBlock _mpb;
        float _resultUntil;

        void OnEnable()
        {
            _mpb = new MaterialPropertyBlock();
            if (shuttle == null) return;
            shuttle.TrajectoryChanged += OnTrajectoryChanged;
            shuttle.Landed += OnLanded;
            Show(resultRing, false);
        }

        void OnDisable()
        {
            if (shuttle == null) return;
            shuttle.TrajectoryChanged -= OnTrajectoryChanged;
            shuttle.Landed -= OnLanded;
        }

        void OnTrajectoryChanged()
        {
            // Cầu bay lại (pha mới / tường tập bắn cầu mới) ⇒ tắt vòng kết quả của pha trước.
            if (shuttle.IsFlying) Show(resultRing, false);
        }

        void OnLanded(Vector3 point, bool inside)
        {
            Show(resultRing, true);
            Place(resultRing, point, inside ? InColor : OutColor);
            _resultUntil = Time.time + resultShowTime;
        }

        void LateUpdate() // sau coroutine của MatchController (EnterServe) ⇒ ẩn ngay trong cùng frame
        {
            // Ẩn khi hết giờ HOẶC khi cầu đã về tay người giao (pha mới) – tránh vòng kết quả
            // còn nằm trên sân trong khi cầu đã biến khỏi chỗ rơi.
            if (resultRing != null && resultRing.enabled &&
                (Time.time > _resultUntil || (shuttle != null && shuttle.State.Phase == ShuttlePhase.Idle)))
                Show(resultRing, false);
        }

        void Place(Renderer r, Vector3 p, Color c)
        {
            if (r == null) return;
            r.transform.position = new Vector3(p.x, 0.004f, p.z);
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColor, c);
            r.SetPropertyBlock(_mpb);
        }

        static void Show(Renderer r, bool on) { if (r != null) r.enabled = on; }
    }
}
