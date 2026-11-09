using System;
using System.Collections.Generic;
using Badminton.Core;
using Badminton.Player;
using Badminton.Shots;
using Badminton.Shuttle;
using UnityEngine;

namespace Badminton.AI
{
    /// <summary>
    /// AI đối thủ – cài IPlayerInputSource nên điều khiển nhân vật y hệt người chơi (không "ăn gian"
    /// bằng cách đặt vị trí trực tiếp): chỉ trả về hướng di chuyển + lần bấm phím.
    ///
    /// Mỗi khi đối thủ đánh cầu: chờ reactionTime ⇒ dùng ShuttlePhysics.Predict (cùng hàm với mô phỏng)
    /// lấy quỹ đạo ⇒ chọn điểm chạm SỚM NHẤT mà chạy kịp ⇒ chạy tới (cộng sai số vị trí)
    /// ⇒ bấm phím gần thời điểm chạm (cộng sai số timing) ⇒ về vị trí trung tâm.
    /// </summary>
    [DefaultExecutionOrder(-45)] // trước PlayerMotor để Move mới nhất được dùng ngay
    public class AIController : MonoBehaviour, IPlayerInputSource
    {
        public AIDifficulty difficulty;
        public PlayerMotor motor;
        public PlayerHitter hitter;
        public Shuttlecock shuttle;
        public PlayerMotor opponent;
        /// <summary>Đánh đôi: điều phối với đồng đội (đội hình + ai đánh quả nào). Null khi đánh đơn.</summary>
        [NonSerialized] public DoublesTeam team;

        [Tooltip("Chờ bao lâu trước khi giao cầu (s).")]
        public float serveDelay = 1.0f;
        [Tooltip("Vị trí chờ: cách lưới bao xa (m).")]
        public float baseDepth = 3.6f;

        /// <summary>Hàm kiểm tra điểm rơi có hợp lệ không (MatchController đặt: trong sân / trong ô giao cầu).</summary>
        public Func<Vector3, bool> isLandingIn = p => CourtDimensions.IsInsideSingles(p.x, p.z);

        public Vector2 Move { get; private set; }

        public bool TryConsumeShot(out ShotPress press)
        {
            press = _press;
            if (!_hasPress) return false;
            _hasPress = false;
            return true;
        }

        System.Random _rng;
        bool _hasPress;
        ShotPress _press;

        // Kế hoạch cho cú đánh sắp tới
        bool _chasing, _willHit, _pressed;
        Vector3 _targetFoot;
        float _pressAt, _giveUpAt, _replanAt = -1f, _serveAt = -1f;
        ShotType _shot;
        Vector2 _aim;
        readonly List<Vector3> _path = new List<Vector3>(512);

        void Awake()
        {
            _rng = new System.Random(4242 + GetInstanceID());
            if (motor == null) motor = GetComponent<PlayerMotor>();
            if (hitter == null) hitter = GetComponent<PlayerHitter>();
        }

        void OnEnable() { if (shuttle != null) shuttle.TrajectoryChanged += OnTrajectoryChanged; }
        void OnDisable() { if (shuttle != null) shuttle.TrajectoryChanged -= OnTrajectoryChanged; Move = Vector2.zero; }

        public void ResetPlan()
        {
            _chasing = false;
            _replanAt = -1f;
            _serveAt = -1f;
            _hasPress = false;
            Move = Vector2.zero;
        }

        void OnTrajectoryChanged()
        {
            if (shuttle.LastHitterId == hitter.playerId) { _chasing = false; return; } // mình vừa đánh ⇒ về giữa sân
            // Lần đầu thấy cú đánh mới ⇒ phản ứng sau reactionTime; chạm lưới/đổi hướng ⇒ tính lại ngay.
            float delay = _chasing ? 0f : difficulty.reactionTime;
            _replanAt = Time.time + delay;
        }

        void FixedUpdate()
        {
            if (difficulty == null || motor == null) return;

            // ── Giao cầu ──
            if (hitter.canServe && shuttle.State.Phase == ShuttlePhase.Idle)
            {
                Move = Vector2.zero;
                if (_serveAt < 0f) _serveAt = Time.time + serveDelay;
                if (Time.time >= _serveAt)
                {
                    _serveAt = -1f;
                    bool shortServe = _rng.NextDouble() < 0.35;
                    Press(ShotType.Serve, new Vector2(0f, shortServe ? 1f : 0f));
                }
                return;
            }

            if (_replanAt > 0f && Time.time >= _replanAt) { _replanAt = -1f; Plan(); }

            if (_chasing && Time.time > _giveUpAt) _chasing = false;

            // ── Di chuyển ──
            Vector3 target = _chasing ? _targetFoot : BasePosition();
            Move = Steer(target);

            // ── Bấm phím ──
            if (_chasing && _willHit && !_pressed && Time.time >= _pressAt)
            {
                _pressed = true;
                Press(_shot, _aim);
            }
        }

        void Press(ShotType type, Vector2 aim)
        {
            _press = new ShotPress { Type = type, Aim = aim, Time = Time.timeAsDouble };
            _hasPress = true;
        }

        Vector3 BasePosition() => team != null ? team.BasePosition(this) : new Vector3(0f, 0f, (int)motor.side * baseDepth);

        /// <summary>Hướng di chuyển (theo góc nhìn nhân vật) để tới target, giảm tốc khi gần tới.</summary>
        Vector2 Steer(Vector3 target)
        {
            Vector3 d = target - motor.Position; d.y = 0f;
            float dist = d.magnitude;
            if (dist < 0.06f) return Vector2.zero;
            var local = new Vector2(Vector3.Dot(d, motor.Right), Vector3.Dot(d, motor.Forward)) / dist;
            return local * Mathf.Clamp01(dist / 0.5f) * difficulty.speedFactor;
        }

        // ───────────────────────────── LẬP KẾ HOẠCH ─────────────────────────────

        void Plan()
        {
            if (!shuttle.IsFlying || shuttle.LastHitterId == hitter.playerId) { _chasing = false; return; }

            _path.Clear();
            float dt = Time.fixedDeltaTime;
            var pred = shuttle.Physics.Predict(shuttle.State, dt, 5f, _path);

            // Cầu sẽ rơi ra ngoài phần sân mình ⇒ (tuỳ khả năng phán đoán) bỏ không đánh.
            bool landsMySide = pred.Outcome == ShuttleOutcome.Floor && CourtDimensions.SideOf(pred.Point.z) == motor.side;
            if (landsMySide && !isLandingIn(pred.Point) && _rng.NextDouble() < difficulty.lineJudgement)
            {
                _chasing = false;
                return;
            }

            // Tìm điểm chạm sớm nhất chạy kịp.
            var tuning = motor.tuning;
            bool likesSmash = _rng.NextDouble() < difficulty.smashChance;
            float maxH = tuning.hitCenterHeight + tuning.reach * 0.85f + (likesSmash ? hitter.shots.jumpReach : 0f);
            float speed = tuning.maxSpeed * difficulty.speedFactor;

            int chosen = -1, lastValid = -1;
            for (int i = 1; i < _path.Count; i++)
            {
                Vector3 p = _path[i];
                if (CourtDimensions.SideOf(p.z) != motor.side || p.y > maxH || p.y < 0.25f) continue;
                if (_path[i].y > _path[i - 1].y && p.y > 1.2f) continue; // ưu tiên chạm khi cầu đã đi xuống
                lastValid = i;
                float run = Vector3.Distance(Flat(motor.Position), Flat(FootFor(p))) - tuning.reach * 0.4f;
                if (run <= speed * (i * dt) * 0.85f) { chosen = i; break; }
            }
            if (chosen < 0) chosen = lastValid;
            if (chosen < 0) { _chasing = false; return; }

            Vector3 contact = _path[chosen];
            // Đánh đôi: nhường quả này cho đồng đội (ưu tiên người chơi khi cầu ở vùng giữa).
            if (team != null && !team.ShouldTake(this, FootFor(contact))) { _chasing = false; return; }
            Vector2 err = new Vector2(ShotResolver.Gauss(_rng), ShotResolver.Gauss(_rng)) * difficulty.positionError;
            _targetFoot = FootFor(contact) + new Vector3(err.x, 0f, err.y);

            float contactTime = Time.time + chosen * dt;
            _pressAt = contactTime - 0.05f + ShotResolver.Gauss(_rng) * difficulty.timingError;
            _giveUpAt = contactTime + 0.4f;
            _chasing = true;
            _pressed = false;
            _willHit = _rng.NextDouble() >= difficulty.blunderChance;

            ChooseShot(contact, likesSmash);
        }

        Vector3 FootFor(Vector3 contact)
        {
            Vector3 f = contact - Vector3.up * motor.tuning.hitCenterHeight - motor.Forward * motor.tuning.hitCenterForward;
            f.y = 0f;
            return f;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        void ChooseShot(Vector3 contact, bool likesSmash)
        {
            var shots = hitter.shots;
            float h = contact.y;
            float distNet = Mathf.Abs(contact.z);
            float oppDepth = opponent != null ? Mathf.Abs(opponent.Position.z) : 3.5f;
            double r = _rng.NextDouble();

            if (likesSmash && h >= shots.smash.minContactHeight + 0.1f && distNet > 1.5f)
                _shot = ShotType.Smash;
            else if (h >= 1.8f)
                _shot = oppDepth > 4.2f ? (r < 0.65 ? ShotType.Drop : ShotType.Clear) : (r < 0.7 ? ShotType.Clear : ShotType.Drop);
            // Cầu thấp: Clear ⇒ Lift, Drop ⇒ Net shot / đỡ ngắn (ShotLibrary.Resolve tự đổi theo độ cao).
            else if (distNet < shots.netShotMaxDistance)
                _shot = r < 0.65 ? ShotType.Drop : ShotType.Clear; // sát lưới: chủ yếu bỏ nhỏ lại
            else
                _shot = r < 0.25 ? ShotType.Drop : ShotType.Clear; // xa lưới: chủ yếu vén, thỉnh thoảng đỡ ngắn

            // Hướng: đưa cầu về phía xa đối thủ (theo góc nhìn của AI).
            float oppRight = opponent != null ? opponent.Position.x * CourtDimensions.RightSign(motor.side) : 0f;
            float side = oppRight >= 0f ? -1f : 1f;
            float ax = side * difficulty.placement * (0.5f + 0.5f * (float)_rng.NextDouble()) + ShotResolver.Gauss(_rng) * 0.2f;
            float ay = oppDepth < 3f ? difficulty.placement : (oppDepth > 5f ? -difficulty.placement * 0.5f : 0f);
            _aim = new Vector2(Mathf.Clamp(ax, -1f, 1f), _shot == ShotType.Smash ? 0f : ay);
        }
    }
}
