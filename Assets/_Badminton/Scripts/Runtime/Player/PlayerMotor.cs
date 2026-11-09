using Badminton.Core;
using UnityEngine;

namespace Badminton.Player
{
    /// <summary>
    /// Di chuyển nhân vật (kinematic, không Rigidbody) trong FixedUpdate để tất định –
    /// cần cho client-side prediction ở GĐ5.
    /// Có gia tốc/giảm tốc, giới hạn trong nửa sân của mình, luôn cách lưới netClearance.
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public class PlayerMotor : MonoBehaviour
    {
        public PlayerTuning tuning;
        public CourtSide side = CourtSide.Near;

        [Tooltip("Component cài IPlayerInputSource (KeyboardPlayerInput, AI...). Để trống = tự tìm trên GameObject.")]
        public MonoBehaviour inputSource;

        [Header("Bật nhảy (hình ảnh)")]
        public float jumpHeight = 0.45f;
        public float jumpDuration = 0.5f;

        /// <summary>Khoá di chuyển (đứng yên chờ giao/nhận cầu).</summary>
        [System.NonSerialized] public bool frozen;

        public IPlayerInputSource Input { get; private set; }

        /// <summary>Đổi nguồn điều khiển lúc chạy (bàn phím ↔ AI ↔ mạng).</summary>
        public void SetInput(IPlayerInputSource source) => Input = source;

        /// <summary>Bật nhảy (nhảy đập cầu). Chỉ là hiệu ứng hình ảnh, không ảnh hưởng vị trí mô phỏng.</summary>
        public void PlayJump() => _jumpT = 0f;
        public Vector3 Velocity => _vel;
        public Vector3 Position => _pos;

        /// <summary>Hướng "về phía lưới" của người chơi.</summary>
        public Vector3 Forward => new Vector3(0f, 0f, -(int)side);
        /// <summary>Hướng "bên phải" theo góc nhìn người chơi (sân Far bị lật 180°).</summary>
        public Vector3 Right => new Vector3(-(int)side, 0f, 0f);

        /// <summary>Tâm vùng với tới của vợt (thế giới).</summary>
        public Vector3 HitCenter => _pos + Vector3.up * tuning.hitCenterHeight + Forward * tuning.hitCenterForward;

        Vector3 _pos, _prevPos, _vel;
        float _yaw;
        float _jumpT = 1f;

        void Awake()
        {
            Input = inputSource as IPlayerInputSource ?? GetComponent<IPlayerInputSource>();
            Teleport(transform.position);
        }

        /// <summary>Đặt vị trí ngay (đầu rally, đổi sân).</summary>
        public void Teleport(Vector3 p)
        {
            _pos = _prevPos = ClampToHalf(new Vector3(p.x, 0f, p.z));
            _vel = Vector3.zero;
            _yaw = side == CourtSide.Near ? 0f : 180f;
            _jumpT = 1f;
            transform.SetPositionAndRotation(_pos, Quaternion.Euler(0f, _yaw, 0f));
        }

        // ───────────────────────────── MẠNG (GĐ5) ─────────────────────────────

        /// <summary>
        /// Puppet: không tự mô phỏng, vị trí do mạng đặt (đối thủ trên máy client – nội suy từ snapshot).
        /// </summary>
        [System.NonSerialized] public bool puppet;

        /// <summary>Đặt trạng thái từ mạng (puppet). Vận tốc dùng cho animation chạy.</summary>
        public void ForceState(Vector3 pos, Vector3 vel)
        {
            _prevPos = _pos;
            _pos = new Vector3(pos.x, 0f, pos.z);
            _vel = vel;
            float baseYaw = side == CourtSide.Near ? 0f : 180f;
            float lean = Mathf.Clamp(Vector3.Dot(_vel, Right) / tuning.maxSpeed, -1f, 1f) * 25f;
            _yaw = Mathf.MoveTowardsAngle(_yaw, baseYaw + lean, tuning.turnSpeed * Time.fixedDeltaTime);
        }

        /// <summary>
        /// Hiệu chỉnh nhẹ vị trí dự đoán (client-side prediction) theo vị trí server xác nhận.
        /// </summary>
        public void Nudge(Vector3 delta)
        {
            delta.y = 0f;
            _pos = ClampToHalf(_pos + delta);
        }

        void FixedUpdate()
        {
            if (puppet) return;
            float dt = Time.fixedDeltaTime;
            Vector2 move = Input != null && !frozen ? Input.Move : Vector2.zero;

            // Vận tốc mong muốn theo góc nhìn người chơi → thế giới.
            Vector3 wish = (Right * move.x + Forward * move.y) * tuning.maxSpeed;

            // Có input ⇒ tăng tốc; thả phím ⇒ giảm tốc mạnh hơn (dừng gọn).
            float rate = move.sqrMagnitude > 0.01f ? tuning.acceleration : tuning.deceleration;
            _vel = Vector3.MoveTowards(_vel, wish, rate * dt);

            _prevPos = _pos;
            Vector3 next = _pos + _vel * dt;
            Vector3 clamped = ClampToHalf(next);
            if (!Mathf.Approximately(clamped.x, next.x)) _vel.x = 0f; // đụng biên thì triệt tiêu vận tốc theo trục đó
            if (!Mathf.Approximately(clamped.z, next.z)) _vel.z = 0f;
            _pos = clamped;

            // Người chơi luôn quay mặt về lưới (tay vợt bên phải), chỉ xoay nhẹ theo hướng chạy ngang.
            float baseYaw = side == CourtSide.Near ? 0f : 180f;
            float lean = Mathf.Clamp(Vector3.Dot(_vel, Right) / tuning.maxSpeed, -1f, 1f) * 25f;
            _yaw = Mathf.MoveTowardsAngle(_yaw, baseYaw + lean, tuning.turnSpeed * dt);
        }

        void Update()
        {
            // Nội suy hình ảnh giữa 2 bước FixedUpdate.
            float a = Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime);
            Vector3 p = Vector3.Lerp(_prevPos, _pos, a);
            if (_jumpT < 1f)
            {
                _jumpT = Mathf.Min(1f, _jumpT + Time.deltaTime / jumpDuration);
                p.y = Mathf.Sin(_jumpT * Mathf.PI) * jumpHeight; // cung parabol đơn giản
            }
            transform.SetPositionAndRotation(p, Quaternion.Euler(0f, _yaw, 0f));
        }

        Vector3 ClampToHalf(Vector3 p)
        {
            if (tuning == null) return p;
            float s = (float)side;
            float maxX = CourtDimensions.HalfDoublesWidth + tuning.sideMargin;
            // |z| trong khoảng [netClearance, HalfLength + backMargin], và đúng dấu của nửa sân mình.
            float dz = Mathf.Clamp(p.z * s, tuning.netClearance, CourtDimensions.HalfLength + tuning.backMargin);
            return new Vector3(Mathf.Clamp(p.x, -maxX, maxX), 0f, dz * s);
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            if (tuning == null) return;
            Gizmos.color = new Color(0f, 1f, 0.4f, 0.35f);
            Vector3 c = Application.isPlaying ? HitCenter
                : transform.position + Vector3.up * tuning.hitCenterHeight + Forward * tuning.hitCenterForward;
            Gizmos.DrawWireSphere(c, tuning.reach); // vùng với tới của vợt
        }
#endif
    }
}
