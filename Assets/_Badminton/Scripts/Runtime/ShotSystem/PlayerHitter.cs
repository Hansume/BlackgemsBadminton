using System;
using Badminton.Core;
using Badminton.Player;
using Badminton.Shuttle;
using UnityEngine;

namespace Badminton.Shots
{
    public enum TimingGrade : byte { Perfect, Good, Early, Late }

    /// <summary>Thông tin một cú đánh vừa thực hiện – UI, âm thanh, trọng tài, mạng đều dùng.</summary>
    public struct HitInfo
    {
        public int PlayerId;
        public ShotType Type;
        public Vector3 Contact;
        public Vector3 Velocity;
        public float Quality;
        public TimingGrade Grade;
        public bool Jumped;
        public Vector2 Aim;
        public int Seed;
    }

    /// <summary>
    /// Đánh cầu có timing.
    ///
    /// Cách hoạt động mỗi FixedUpdate khi đang có lần bấm phím chờ (input buffer):
    ///  1. Cầu chưa vào vùng với tới ⇒ chờ.
    ///  2. Cầu trong vùng với tới và bước sau còn GẦN tâm hơn ⇒ chờ tiếp (đánh ở điểm gần nhất).
    ///  3. Ngược lại ⇒ đánh. Chất lượng = (cầu gần "điểm ngọt" tới đâu) × (bấm sớm bao lâu).
    ///     - Bấm quá sớm: chất lượng giảm theo thời gian chờ.
    ///     - Bấm muộn: cầu đã qua điểm gần nhất ⇒ chạm xa tâm ⇒ chất lượng giảm.
    /// </summary>
    [DefaultExecutionOrder(-30)]
    public class PlayerHitter : MonoBehaviour
    {
        public int playerId;
        public PlayerMotor motor;
        public Shuttlecock shuttle;
        public ShotLibrary shots;
        public RacketSwing swing;

        [Tooltip("Điểm cầm cầu khi chuẩn bị giao (con của nhân vật).")]
        public Transform serveAnchor;

        [Tooltip("Chế độ tập: giao cầu bất cứ lúc nào cầu không bay.")]
        public bool freeServe = true;
        /// <summary>MatchController bật khi đến lượt người này giao cầu.</summary>
        [NonSerialized] public bool canServe;
        /// <summary>Trận đấu: X thế giới của tâm ô giao cầu chéo (MatchController đặt).</summary>
        [NonSerialized] public float? serveTargetX;
        /// <summary>Đánh đôi: giới hạn độ sâu cú giao cầu (m tính từ lưới) để rơi trước vạch giao cầu dài đôi. 0 = không giới hạn.</summary>
        [NonSerialized] public float serveDepthCap;

        public event Action<HitInfo> Hit;
        /// <summary>Bấm phím nhưng không đánh được – kèm lý do (UI hiện nhắc nhở).</summary>
        public event Action<ShotType, string> Rejected;

        System.Random _rng;
        bool _pending;
        ShotPress _press;
        float _lastHit = -10f;

        public bool HasPendingPress => _pending;

        /// <summary>Bỏ lần bấm phím đang chờ (trận mới không thừa hưởng phím bấm của trận cũ).</summary>
        public void CancelPending() => _pending = false;

        void Awake()
        {
            _rng = new System.Random(1000 + playerId);
            if (motor == null) motor = GetComponent<PlayerMotor>();
        }

        /// <summary>Đặt lại seed (server và client dùng chung seed để tái tạo cú đánh – GĐ5).</summary>
        public void SetSeed(int seed) => _rng = new System.Random(seed);

        void FixedUpdate()
        {
            var input = motor.Input;
            if (input != null && input.TryConsumeShot(out var p)) { _press = p; _pending = true; }
            if (!_pending) return;

            double age = Time.timeAsDouble - _press.Time;
            if (age > motor.tuning.inputBuffer) { _pending = false; return; }

            if (_press.Type == ShotType.Serve) { TryServe(); return; }
            TryHit((float)age);
        }

        // ───────────────────────────── GIAO CẦU ─────────────────────────────

        void TryServe()
        {
            _pending = false;
            bool allowed = canServe ? shuttle.State.Phase == ShuttlePhase.Idle : freeServe && !shuttle.IsFlying;
            if (!allowed) return;

            // Giữ W khi giao ⇒ giao cầu ngắn, ngược lại giao cầu cao.
            ShotType type = _press.Aim.y > 0.5f ? ShotType.ShortServe : ShotType.Serve;
            Vector3 contact = serveAnchor != null ? serveAnchor.position : transform.position + Vector3.up;
            Execute(type, contact, 1f, TimingGrade.Perfect, false, serveTargetX);
            canServe = false;
        }

        // ───────────────────────────── ĐÁNH CẦU ─────────────────────────────

        void TryHit(float pressAge)
        {
            if (!shuttle.IsFlying) return;
            if (Time.time - _lastHit < motor.tuning.hitCooldown) return;
            if (shuttle.LastHitterId == playerId) return; // không đánh 2 lần liên tiếp

            Vector3 sp = shuttle.Position;
            if (CourtDimensions.SideOf(sp.z) != motor.side) return; // không với qua lưới

            // Smash được phép với cao hơn nhờ bật nhảy.
            bool smashIntent = _press.Type == ShotType.Smash;
            Vector3 center = motor.HitCenter;
            bool jump = false;
            if (smashIntent && sp.y > center.y + motor.tuning.reach * 0.5f)
            {
                center += Vector3.up * shots.jumpReach;
                jump = true;
            }

            float reach = motor.tuning.reach;
            float d = Vector3.Distance(sp, center);
            if (d > reach) return; // chưa tới tầm – giữ buffer chờ tiếp

            // Bước vật lý kế tiếp cầu còn tới gần tâm hơn ⇒ chờ để chạm đúng điểm gần nhất.
            // NGOẠI LỆ: cú cần cầu ở trên cao (smash ≥ 1.9 m, drop, clear) – nếu chờ thêm cầu sẽ tụt dưới
            // độ cao tối thiểu ⇒ đánh ngay (đập ở điểm cao nhất, đúng kỹ thuật thật).
            var next = shuttle.State;
            shuttle.Physics.Step(ref next, Time.fixedDeltaTime);
            bool expiring = pressAge + Time.fixedDeltaTime > motor.tuning.inputBuffer;
            var intended = shots.Get(shots.Resolve(_press.Type, sp.y));
            float minH = intended != null ? intended.minContactHeight : 0f;
            bool dropsBelowMin = minH > 0f && sp.y >= minH && next.Position.y < minH + 0.03f;
            // Smash: đập ngay khi cầu vào "điểm ngọt" quanh vợt (kể cả lúc bật nhảy) – không chờ cầu rơi thấp
            // vì càng cao càng có góc cắm. Cầu sắp tụt dưới độ cao tối thiểu thì dropsBelowMin xử lý ở trên.
            bool smashNow = _press.Type == ShotType.Smash && sp.y >= minH && d <= shots.sweetSpot * reach;
            if (!expiring && !dropsBelowMin && !smashNow && next.Phase == ShuttlePhase.Flying &&
                CourtDimensions.SideOf(next.Position.z) == motor.side &&
                Vector3.Distance(next.Position, center) < d)
                return;

            _pending = false;
            ShotType type = shots.Resolve(_press.Type, sp.y);
            var def = shots.Get(type);
            if (def == null) return;
            if (!def.CanHitAt(sp.y))
            {
                Rejected?.Invoke(type, type == ShotType.Smash ? "Cầu quá thấp để đập!" : "Không đánh được ở độ cao này");
                return;
            }

            // ── Chất lượng timing ──
            float sweet = shots.sweetSpot * reach;
            float qDist = d <= sweet ? 1f : 1f - Mathf.InverseLerp(sweet, reach, d) * 0.7f;          // muộn/xa tâm
            float early = Mathf.InverseLerp(shots.perfectWindow, motor.tuning.inputBuffer, pressAge);  // 0 = đúng lúc
            float qTime = Mathf.Lerp(1f, shots.earlyFloor, early);                                    // sớm
            float quality = qDist * qTime;

            TimingGrade grade = quality >= 0.85f ? TimingGrade.Perfect
                              : quality >= 0.6f ? TimingGrade.Good
                              : qTime < qDist ? TimingGrade.Early : TimingGrade.Late;

            Execute(type, sp, quality, grade, jump, null);
        }

        void Execute(ShotType type, Vector3 contact, float quality, TimingGrade grade, bool jumped, float? targetX)
        {
            if (shots.Get(type) == null) return;

            // Seed tất định: online thì seed lấy theo tick ⇒ client dự đoán và server tính lại ra cùng kết quả.
            int seed = seedProvider != null ? seedProvider() : _rng.Next();
            Vector3 vel = ResolveVelocity(type, contact, _press.Aim, quality, targetX, seed);

            shuttle.Launch(contact, vel, playerId);
            _lastHit = Time.time;
            if (swing != null) swing.Play(type);
            if (jumped) motor.PlayJump();

            Hit?.Invoke(new HitInfo
            {
                PlayerId = playerId, Type = type, Contact = contact, Velocity = vel,
                Quality = quality, Grade = grade, Jumped = jumped, Aim = _press.Aim, Seed = seed,
            });
        }

        // ───────────────────────────── MẠNG (GĐ5) ─────────────────────────────

        /// <summary>Online: hàm cấp seed cho cú đánh tiếp theo (theo tick mô phỏng). Null ⇒ dùng RNG nội bộ.</summary>
        [NonSerialized] public Func<int> seedProvider;

        /// <summary>Tính vận tốc cú đánh – dùng chung cho đánh tại chỗ và cho server tái tạo cú đánh của client.</summary>
        public Vector3 ResolveVelocity(ShotType type, Vector3 contact, Vector2 aim, float quality, float? targetX, int seed)
        {
            var prm = shots.ParamsFor(type, contact);
            if (serveDepthCap > 0f && (type == ShotType.Serve || type == ShotType.ShortServe))
            {
                prm.targetDepth = Mathf.Min(prm.targetDepth, serveDepthCap);
                prm.aimDepth = Mathf.Min(prm.aimDepth, 0.2f);
            }
            var ctx = new ShotContext { Contact = contact, Side = motor.side, Aim = aim, Quality = quality, TargetXOverride = targetX };
            return ShotResolver.Resolve(prm, ctx, shuttle.Physics, Time.fixedDeltaTime, new System.Random(seed));
        }

        /// <summary>
        /// Cú đánh do mạng xác nhận (cầu đã được đặt quỹ đạo ở nơi khác): chỉ phát sự kiện Hit
        /// để animation, âm thanh, trọng tài phản ứng.
        /// </summary>
        public void NotifyRemoteHit(HitInfo h)
        {
            _lastHit = Time.time;
            _pending = false;
            if (h.Type == ShotType.Serve || h.Type == ShotType.ShortServe) canServe = false;
            if (swing != null) swing.Play(h.Type);
            if (h.Jumped) motor.PlayJump();
            Hit?.Invoke(h);
        }
    }
}
