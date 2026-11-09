using Badminton.Core;
using Badminton.Shots;
using UnityEngine;

namespace Badminton.Player
{
    /// <summary>
    /// Animation procedural cho nhân vật low-poly (không cần file animation / Mixamo):
    /// idle (hạ trọng tâm), chạy, lunge lên lưới, nhảy đập, đánh trên đầu, thuận tay, trái tay,
    /// giao cầu (tay trái cầm cầu, vung dưới tay) và ăn mừng.
    ///
    /// Mỗi khớp là một pivot; chân/tay mặc định chỉ thẳng xuống (−Y).
    /// Quy ước góc Euler cục bộ: X âm = đưa chi ra TRƯỚC (−90 = ngang trước, −180 = thẳng lên),
    /// Z dương = dang ra ngoài bên phải. Mọi tư thế được tính lại mỗi frame rồi làm mượt.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class PlayerAnimator : MonoBehaviour
    {
        enum Act { None, Overhead, Smash, Forehand, Backhand, Underarm }

        public PlayerMotor motor;
        public PlayerHitter hitter;

        [Header("Khớp")]
        public Transform hips, legL, legR, torso, shoulderR, shoulderL;

        [Header("Thông số")]
        public float hipHeight = 0.85f;
        public float smoothing = 18f;

        [Header("Kích thước giày (khớp với PracticeSceneBuilder.BuildPlayer)")]
        public float soleDepth = 0.855f;     // từ khớp hông chân tới đế giày
        public float shoeCenterZ = 0.05f;
        public float shoeHalfLength = 0.13f;
        public float shoeHalfWidth = 0.065f;

        float _runPhase;
        Act _act;
        float _actT = 1f, _actDur = 0.35f;
        float _lungeT = 1f;
        float _celebrateT = 1f, _celebrateDur = 1.6f;

        // Tư thế hiện tại (để làm mượt)
        Vector3 _legL, _legR, _torso, _shR, _shL;
        float _hipY;

        void Awake()
        {
            if (motor == null) motor = GetComponent<PlayerMotor>();
            if (hitter == null) hitter = GetComponent<PlayerHitter>();
            _hipY = hipHeight;
        }

        void OnEnable() { if (hitter != null) hitter.Hit += OnHit; }
        void OnDisable() { if (hitter != null) hitter.Hit -= OnHit; }

        /// <summary>Ăn mừng (giơ 2 tay, nhún nhảy) khi thắng điểm.</summary>
        public void Celebrate(float duration = 1.6f) { _celebrateDur = duration; _celebrateT = 0f; }

        /// <summary>Huỷ mọi động tác dở dang (vung vợt, lunge, ăn mừng) – dùng khi bắt đầu trận mới.</summary>
        public void ResetPose() { _actT = 1f; _lungeT = 1f; _celebrateT = 1f; }

        void OnHit(HitInfo h)
        {
            // Vị trí điểm chạm so với người: phải(+)/trái(−) theo góc nhìn người chơi.
            Vector3 rel = h.Contact - motor.Position;
            float side = Vector3.Dot(rel, motor.Right);

            switch (h.Type)
            {
                case ShotType.Smash: Play(h.Jumped ? Act.Smash : Act.Overhead, 0.32f); break;
                case ShotType.Clear:
                case ShotType.Drop:
                    if (h.Contact.y > 1.7f) Play(Act.Overhead, 0.38f);
                    else Play(side >= 0f ? Act.Forehand : Act.Backhand, 0.34f);
                    break;
                case ShotType.Serve:
                case ShotType.ShortServe: Play(Act.Underarm, 0.4f); break;
                default:
                    // Cầu thấp: thấp & xa sang ngang ⇒ thuận/trái tay, còn lại vung dưới tay.
                    if (h.Contact.y > 1.1f && Mathf.Abs(side) > 0.35f) Play(side >= 0f ? Act.Forehand : Act.Backhand, 0.34f);
                    else Play(Act.Underarm, 0.36f);
                    // Bước lunge khi với cầu thấp gần lưới.
                    if (h.Contact.y < 1.0f && Mathf.Abs(motor.Position.z) < 3.2f) _lungeT = 0f;
                    break;
            }
        }

        void Play(Act a, float duration) { _act = a; _actDur = duration; _actT = 0f; }

        void LateUpdate()
        {
            if (motor == null || motor.tuning == null) return;
            float dt = Time.deltaTime;

            // ── Chạy ──
            Vector3 v = motor.Velocity;
            float s01 = Mathf.Clamp01(v.magnitude / motor.tuning.maxSpeed);
            _runPhase += dt * Mathf.Lerp(0f, 12f, s01);
            float fwd = Vector3.Dot(v, motor.Forward) / motor.tuning.maxSpeed;
            float lat = Vector3.Dot(v, motor.Right) / motor.tuning.maxSpeed;
            float stride = Mathf.Sin(_runPhase) * 42f * s01;

            Vector3 legL = new Vector3(-stride, 0f, -6f);
            Vector3 legR = new Vector3(stride, 0f, 6f);
            Vector3 tor = new Vector3(10f + fwd * 10f, 0f, -lat * 8f); // nghiêng theo hướng chạy
            Vector3 shR = new Vector3(-110f, 0f, 25f);                  // tư thế chờ: vợt giơ trước-phải
            Vector3 shL = new Vector3(-35f + stride * 0.6f, 0f, -20f);
            float hip = hipHeight - 0.08f - s01 * 0.03f + Mathf.Abs(Mathf.Sin(_runPhase)) * 0.05f * s01;

            // Đang chuẩn bị giao: tay trái đưa cầu ra trước.
            if (hitter != null && hitter.canServe) shL = new Vector3(-80f, 0f, -5f);

            // ── Lunge ──
            if (_lungeT < 1f)
            {
                _lungeT = Mathf.Min(1f, _lungeT + dt / 0.65f);
                float w = Mathf.Sin(_lungeT * Mathf.PI); // vào rồi ra
                legR = Vector3.Lerp(legR, new Vector3(-75f, 0f, 4f), w);
                legL = Vector3.Lerp(legL, new Vector3(40f, 0f, -6f), w);
                tor.x += 18f * w;
                hip -= 0.3f * w;
            }

            // ── Động tác vung vợt ──
            if (_actT < 1f)
            {
                _actT = Mathf.Min(1f, _actT + dt / _actDur);
                ApplyAction(_actT, ref shR, ref tor);
            }

            // ── Ăn mừng ──
            if (_celebrateT < 1f)
            {
                _celebrateT = Mathf.Min(1f, _celebrateT + dt / _celebrateDur);
                float w = Mathf.Clamp01(Mathf.Min(_celebrateT * 6f, (1f - _celebrateT) * 4f));
                float wave = Mathf.Sin(_celebrateT * 30f) * 12f;
                shR = Vector3.Lerp(shR, new Vector3(-170f + wave, 0f, 20f), w);
                shL = Vector3.Lerp(shL, new Vector3(-170f - wave, 0f, -20f), w);
                hip += Mathf.Abs(Mathf.Sin(_celebrateT * 18f)) * 0.12f * w;
                tor.x = Mathf.Lerp(tor.x, -8f, w);
            }

            // ── Làm mượt & áp dụng ──
            float k = 1f - Mathf.Exp(-smoothing * dt);
            float kFast = _actT < 1f ? 1f : k; // vung vợt cần bám sát, không làm mượt
            _legL = Vector3.Lerp(_legL, legL, k);
            _legR = Vector3.Lerp(_legR, legR, k);
            _torso = Vector3.Lerp(_torso, tor, kFast);
            _shR = LerpAngles(_shR, shR, kFast);
            _shL = LerpAngles(_shL, shL, k);
            _hipY = Mathf.Lerp(_hipY, hip, k);

            Set(this.legL, _legL);
            Set(this.legR, _legR);
            // Chân là khối cứng (không có đầu gối): không cho hông hạ thấp hơn mức
            // làm đế giày lún xuống sân – hông luôn đủ cao để bàn chân thấp nhất chạm mặt sân.
            if (hips != null) hips.localPosition = new Vector3(0f, Mathf.Max(_hipY, MinHipHeight()), 0f);
            Set(torso, _torso);
            Set(shoulderR, _shR);
            Set(shoulderL, _shL);
        }

        /// <summary>Đường cong góc vai phải + xoắn thân cho từng động tác (t: 0→1).</summary>
        void ApplyAction(float t, ref Vector3 sh, ref Vector3 tor)
        {
            switch (_act)
            {
                case Act.Overhead:
                case Act.Smash:
                {
                    // Gồng tay ra sau đầu → quất mạnh qua trước → thu về.
                    float x = t < 0.35f ? Mathf.Lerp(-110f, -200f, Ease(t / 0.35f))
                            : t < 0.6f ? Mathf.Lerp(-200f, -35f, Ease((t - 0.35f) / 0.25f))
                            : Mathf.Lerp(-35f, -110f, (t - 0.6f) / 0.4f);
                    sh = new Vector3(x, 0f, 15f);
                    float twist = t < 0.35f ? -25f * (t / 0.35f) : Mathf.Lerp(-25f, 20f, Mathf.Clamp01((t - 0.35f) / 0.3f));
                    tor.y += twist * (1f - Mathf.Clamp01((t - 0.7f) / 0.3f));
                    if (_act == Act.Smash) tor.x += 18f * Mathf.Sin(t * Mathf.PI);
                    break;
                }
                case Act.Forehand:
                {
                    // Tay ngang trước, quét từ phải-sau sang trước-trái.
                    float y = t < 0.3f ? Mathf.Lerp(0f, 85f, Ease(t / 0.3f))
                            : t < 0.6f ? Mathf.Lerp(85f, -45f, Ease((t - 0.3f) / 0.3f))
                            : Mathf.Lerp(-45f, 0f, (t - 0.6f) / 0.4f);
                    float x = Mathf.Lerp(-110f, -85f, Mathf.Sin(t * Mathf.PI));
                    sh = new Vector3(x, y, 25f);
                    tor.y += Mathf.Lerp(-30f, 25f, Mathf.Clamp01(t / 0.6f)) * Mathf.Sin(t * Mathf.PI);
                    break;
                }
                case Act.Backhand:
                {
                    // Tay vắt chéo sang trái-sau rồi quất ra phải.
                    float y = t < 0.3f ? Mathf.Lerp(0f, -110f, Ease(t / 0.3f))
                            : t < 0.6f ? Mathf.Lerp(-110f, 25f, Ease((t - 0.3f) / 0.3f))
                            : Mathf.Lerp(25f, 0f, (t - 0.6f) / 0.4f);
                    sh = new Vector3(-95f, y, 10f);
                    tor.y += Mathf.Lerp(35f, -20f, Mathf.Clamp01(t / 0.6f)) * Mathf.Sin(t * Mathf.PI);
                    break;
                }
                case Act.Underarm:
                {
                    // Vung dưới tay: từ sau-thấp lên trước-cao.
                    float x = t < 0.3f ? Mathf.Lerp(-110f, 35f, Ease(t / 0.3f))
                            : t < 0.65f ? Mathf.Lerp(35f, -140f, Ease((t - 0.3f) / 0.35f))
                            : Mathf.Lerp(-140f, -110f, (t - 0.65f) / 0.35f);
                    sh = new Vector3(x, 0f, 20f);
                    tor.x += 12f * Mathf.Sin(t * Mathf.PI);
                    break;
                }
            }
        }

        /// <summary>
        /// Độ cao hông tối thiểu để đế giày (gót + mũi) của cả 2 chân không xuyên xuống sân,
        /// tính theo góc chân hiện tại. Hông không xoay, chân gắn ở y = 0 trong hông.
        /// </summary>
        float MinHipHeight()
        {
            float lowest = 0f;
            void Check(Transform leg)
            {
                if (leg == null) return;
                Quaternion r = leg.localRotation;
                // 4 góc đế giày (gót/mũi × trong/ngoài)
                for (int i = 0; i < 4; i++)
                {
                    var corner = new Vector3((i & 1) == 0 ? -shoeHalfWidth : shoeHalfWidth, -soleDepth,
                                             shoeCenterZ + ((i & 2) == 0 ? -shoeHalfLength : shoeHalfLength));
                    lowest = Mathf.Min(lowest, (r * corner).y + leg.localPosition.y);
                }
            }
            Check(legL);
            Check(legR);
            return -lowest;
        }

        static float Ease(float x) => 1f - (1f - x) * (1f - x);

        static Vector3 LerpAngles(Vector3 a, Vector3 b, float k) =>
            new Vector3(Mathf.LerpAngle(a.x, b.x, k), Mathf.LerpAngle(a.y, b.y, k), Mathf.LerpAngle(a.z, b.z, k));

        static void Set(Transform t, Vector3 euler) { if (t != null) t.localRotation = Quaternion.Euler(euler); }
    }
}
