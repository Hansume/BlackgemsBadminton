using Badminton.Core;
using Badminton.Shuttle;
using UnityEngine;

namespace Badminton.Shots
{
    /// <summary>
    /// Vòng căn thời gian đánh cầu (trận đấu người/AI và online), đặt tại điểm rơi dự đoán.
    /// Chỉ hiện khi cầu đang bay về phía người chơi local. Một màu cố định; vòng CO DẦN và chạm
    /// kích thước gốc đúng lúc bấm phím bắt đầu đạt perfect.
    ///
    /// Cửa sổ "perfect" tính theo đúng công thức chấm điểm của <see cref="PlayerHitter"/>:
    /// bấm sớm tối đa <c>perfectLead</c> giây trước điểm chạm vẫn đạt chất lượng ≥ 0.85.
    /// </summary>
    [DefaultExecutionOrder(150)]
    public class TimingRing : MonoBehaviour
    {
        public Shuttlecock shuttle;
        public Renderer ring;
        [Tooltip("Người chơi local (MatchController gán mỗi lượt giao cầu). Null ⇒ ẩn.")]
        public PlayerHitter hitter;

        public float baseSize = 0.7f;
        [Tooltip("Vòng to thêm bao nhiêu lần khi cầu còn xa (co dần về baseSize lúc perfect).")]
        public float approachScale = 1.6f;
        [Tooltip("Thời gian (s) trước điểm perfect mà vòng bắt đầu co lại.")]
        public float approachTime = 0.9f;
        public float maxSimTime = 3f;
        public Color color = new Color(1f, 1f, 1f, 0.95f);

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        MaterialPropertyBlock _mpb;
        Vector3 _landing;
        bool _hasLanding;

        void OnEnable()
        {
            _mpb = new MaterialPropertyBlock();
            if (shuttle != null)
            {
                shuttle.TrajectoryChanged += OnTrajectoryChanged;
                shuttle.Landed += OnLanded;
            }
            Show(false);
        }

        void OnDisable()
        {
            if (shuttle != null)
            {
                shuttle.TrajectoryChanged -= OnTrajectoryChanged;
                shuttle.Landed -= OnLanded;
            }
            Show(false);
        }

        void OnTrajectoryChanged()
        {
            _hasLanding = false;
            if (shuttle == null || !shuttle.IsFlying) return;
            var p = shuttle.Predict();
            if (p.Outcome != ShuttleOutcome.Floor) return;
            _landing = p.Point;
            _hasLanding = true;
        }

        void OnLanded(Vector3 _, bool __) { _hasLanding = false; Show(false); }

        void LateUpdate()
        {
            if (hitter == null || hitter.motor == null || shuttle == null || ring == null || !shuttle.IsFlying ||
                shuttle.LastHitterId == hitter.playerId || !_hasLanding ||
                CourtDimensions.SideOf(_landing.z) != hitter.motor.side)
            {
                Show(false);
                return;
            }

            var motor = hitter.motor;
            var shots = hitter.shots;
            Vector3 center = motor.HitCenter;

            // ── Thời điểm cầu gần tâm đánh nhất (chỉ xét phần quỹ đạo bên sân mình) ──
            float dt = Time.fixedDeltaTime;
            var s = shuttle.State;
            float t = 0f, tBest = -1f, dBest = float.MaxValue;
            while (t <= maxSimTime)
            {
                if (CourtDimensions.SideOf(s.Position.z) == motor.side)
                {
                    float d = Vector3.Distance(s.Position, center);
                    if (d < dBest) { dBest = d; tBest = t; }
                }
                if (s.Phase != ShuttlePhase.Flying) break;
                shuttle.Physics.Step(ref s, dt);
                t += dt;
            }
            if (tBest < 0f) { Show(false); return; }

            // Cửa sổ perfect: bấm trước tối đa perfectLead giây vẫn đạt chất lượng ≥ 0.85 (khi chạm đúng tâm).
            float pw = shots != null ? shots.perfectWindow : 0.08f;
            float floor = shots != null ? shots.earlyFloor : 0.35f;
            float buffer = motor.tuning.inputBuffer;
            float perfectLead = pw + Mathf.Clamp01(0.15f / Mathf.Max(0.01f, 1f - floor)) * (buffer - pw);

            // Co dần: to approachScale lần khi còn xa, về baseSize lúc vào cửa sổ perfect.
            float k = Mathf.Clamp01((tBest - perfectLead) / Mathf.Max(0.01f, approachTime));
            float size = baseSize * (1f + (approachScale - 1f) * k);

            var tr = ring.transform;
            tr.position = new Vector3(_landing.x, 0.006f, _landing.z);
            tr.localScale = new Vector3(size, size, 1f);
            ring.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColor, color);
            ring.SetPropertyBlock(_mpb);
            Show(true);
        }

        void Show(bool on) { if (ring != null && ring.enabled != on) ring.enabled = on; }
    }
}
