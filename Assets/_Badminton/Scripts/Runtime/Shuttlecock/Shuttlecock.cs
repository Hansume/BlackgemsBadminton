using System;
using Badminton.Core;
using UnityEngine;

namespace Badminton.Shuttle
{
    /// <summary>
    /// Quả cầu trong scene. Mô phỏng bằng ShuttlePhysics (Core) trong FixedUpdate;
    /// Update chỉ nội suy hình ảnh giữa 2 bước vật lý cho mượt trên màn hình 120/144 Hz.
    ///
    /// Model con (`visual`) phải có trục +Z = hướng đầu cầu (đế bần).
    /// </summary>
    [DefaultExecutionOrder(-50)] // chạy trước người chơi để họ thấy vị trí cầu mới nhất
    public class Shuttlecock : MonoBehaviour
    {
        public ShuttleSettings settings;
        public Transform visual;
        public TrailRenderer trail;

        public ShuttlePhysics Physics { get; private set; }
        public ShuttleState State => _state;
        public Vector3 Position => _state.Position;
        public Vector3 Velocity => _state.Velocity;
        public bool IsFlying => _state.Phase == ShuttlePhase.Flying;
        public bool IsInPlay => _state.Phase == ShuttlePhase.Flying || _state.Phase == ShuttlePhase.Bouncing;

        /// <summary>Id người đánh cuối cùng (−1 = máy bắn cầu / chưa ai đánh).</summary>
        public int LastHitterId { get; private set; } = -1;
        public float LastHitTime { get; private set; }

        /// <summary>Lần chạm sàn đầu tiên: (điểm rơi, có nằm trong sân đơn không).</summary>
        public event Action<Vector3, bool> Landed;
        public event Action<Vector3> NetHit;
        /// <summary>Chạm tường: handler có thể gọi SetVelocity để đổi hướng bật lại.</summary>
        public event Action<int, Vector3> WallHit;
        /// <summary>Mọi sự kiện của bước vật lý vừa chạy (trọng tài dùng để bắt lỗi chui lưới...).</summary>
        public event Action<ShuttleStepResult> Stepped;
        /// <summary>Quỹ đạo vừa thay đổi (bị đánh, chạm lưới/tường) – dùng để cập nhật dự đoán điểm rơi.</summary>
        public event Action TrajectoryChanged;

        ShuttleState _state, _prev;
        Transform _holdAnchor;

        void Awake()
        {
            Physics = new ShuttlePhysics(settings != null ? settings.physics : null);
            if (visual != null && settings != null) visual.localScale = Vector3.one * settings.visualScale;
            _state.Position = _prev.Position = transform.position;
        }

        // ───────────────────────────── API ─────────────────────────────

        /// <summary>Phát cầu từ vị trí pos với vận tốc vel (cú đánh, giao cầu, máy bắn cầu).</summary>
        public void Launch(Vector3 pos, Vector3 vel, int hitterId)
        {
            _holdAnchor = null;
            _state = ShuttleState.Launched(pos, vel);
            _prev = _state;
            LastHitterId = hitterId;
            LastHitTime = Time.time;
            if (trail != null) trail.Clear();
            TrajectoryChanged?.Invoke();
        }

        /// <summary>Quên người đánh cuối (tường tập / máy bắn cầu trả cầu về).</summary>
        public void ClearLastHitter() => LastHitterId = -1;

        public void SetVelocity(Vector3 vel)
        {
            _state.Velocity = vel;
            TrajectoryChanged?.Invoke();
        }

        /// <summary>Cầm cầu trên tay (chuẩn bị giao): cầu bám theo anchor, không mô phỏng.</summary>
        public void Hold(Transform anchor)
        {
            _holdAnchor = anchor;
            _state.Phase = ShuttlePhase.Idle;
            _state.Velocity = Vector3.zero;
        }

        /// <summary>Dự đoán điểm rơi từ trạng thái hiện tại (dùng chung cho AI, UI và server).</summary>
        public ShuttlePrediction Predict(float maxTime = 6f, bool stopAtWall = true) =>
            Physics.Predict(_state, Time.fixedDeltaTime, maxTime, null, stopAtWall);

        // ───────────────────────────── MẠNG (GĐ5) ─────────────────────────────

        /// <summary>
        /// Đặt trạng thái từ mạng. Quỹ đạo cầu là TẤT ĐỊNH nên chỉ cần (vị trí, vận tốc) tại một tick là
        /// mỗi máy tự mô phỏng tiếp được – xem FastForward.
        /// </summary>
        public void SetState(in ShuttleState s, int lastHitterId)
        {
            _holdAnchor = null;
            _state = s;
            _prev = s;
            LastHitterId = lastHitterId;
            LastHitTime = Time.time;
            TrajectoryChanged?.Invoke();
        }

        /// <summary>
        /// Mô phỏng nhanh thêm `steps` bước (bù độ trễ: trạng thái nhận được là của quá khứ).
        /// silent = true ⇒ không phát sự kiện (dùng khi chỉ hiệu chỉnh nhỏ).
        /// </summary>
        public void FastForward(int steps, bool silent)
        {
            for (int i = 0; i < steps && _state.Phase != ShuttlePhase.Idle && _state.Phase != ShuttlePhase.Resting; i++)
            {
                if (silent) Physics.Step(ref _state, Time.fixedDeltaTime);
                else SimulateStep();
            }
            _prev = _state;
        }

        // ─────────────────────────── VÒNG LẶP ───────────────────────────

        void FixedUpdate()
        {
            _prev = _state;

            if (_state.Phase == ShuttlePhase.Idle)
            {
                if (_holdAnchor != null) _state.Position = _holdAnchor.position;
                _prev = _state;
                return;
            }
            SimulateStep();
        }

        void SimulateStep()
        {
            var r = Physics.Step(ref _state, Time.fixedDeltaTime);
            if (r.Events != ShuttleEvents.None) Stepped?.Invoke(r);

            if (r.Has(ShuttleEvents.HitNet)) { NetHit?.Invoke(r.ContactPoint); TrajectoryChanged?.Invoke(); }
            if (r.Has(ShuttleEvents.HitWall)) { WallHit?.Invoke(r.WallIndex, r.ContactPoint); TrajectoryChanged?.Invoke(); }
            if (r.Has(ShuttleEvents.LandedFirst))
                Landed?.Invoke(r.LandingPoint, CourtDimensions.IsInsideSingles(r.LandingPoint.x, r.LandingPoint.z));
        }

        void Update()
        {
            // Nội suy giữa bước vật lý trước và hiện tại.
            float a = Time.fixedDeltaTime > 0f ? Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime) : 1f;
            transform.position = _state.Phase == ShuttlePhase.Idle && _holdAnchor != null
                ? _holdAnchor.position // đang cầm: bám tay mỗi frame, không giật theo FixedUpdate
                : Vector3.Lerp(_prev.Position, _state.Position, a);

            Vector3 v = _state.Velocity;
            if (v.sqrMagnitude > 0.04f)
                transform.rotation = Quaternion.LookRotation(v); // đầu cầu luôn hướng theo vận tốc
            else if (_state.Phase == ShuttlePhase.Resting)
                transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f) * Quaternion.Euler(-15f, 0f, 0f); // nằm nghiêng trên sàn
            else if (_state.Phase == ShuttlePhase.Idle)
                transform.rotation = Quaternion.LookRotation(Vector3.down); // cầm tay: đầu cầu chúc xuống

            UpdateTrail(v.magnitude);
        }

        void UpdateTrail(float speed)
        {
            if (trail == null || settings == null) return;
            bool on = _state.Phase == ShuttlePhase.Flying && speed > settings.trailMinSpeed;
            trail.emitting = on;
            if (!on) return;

            // Trail càng đậm khi cầu càng nhanh.
            float alpha = Mathf.InverseLerp(settings.trailMinSpeed, settings.trailFullSpeed, speed) * 0.6f;
            var c0 = new Color(1f, 1f, 1f, alpha);
            trail.startColor = c0;
            trail.endColor = new Color(1f, 1f, 1f, 0f);
        }
    }
}
