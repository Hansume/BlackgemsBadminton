using System;
using UnityEngine;

namespace Badminton.Core
{
    public enum ShotType : byte
    {
        // Giá trị số cố định: asset cú đánh và gói tin mạng lưu kiểu cú theo số.
        Clear = 0,       // J – cầu dài; cầu thấp ⇒ ShotLibrary tự đổi thành Lift
        Smash = 1,       // K – đập
        Drop = 2,        // L – cầu ngắn; cầu thấp ⇒ ShotLibrary tự đổi thành NetShot
        // 3 = NetOrLift (phím I cũ) – đã bỏ, không dùng lại số này.
        NetShot = 4,
        Lift = 5,
        Serve = 6,       // Space – giao cầu cao
        ShortServe = 7,  // Space + giữ W – giao cầu ngắn
    }

    public enum ShotAimMode : byte
    {
        /// <summary>Nhắm điểm rơi, giải ngược ra vận tốc với góc nâng cho trước (clear, drop, lift, net, giao cầu).</summary>
        Target,
        /// <summary>Tốc độ cố định, bắn thẳng về điểm đích (smash).</summary>
        Direct,
    }

    /// <summary>Thông số một loại cú đánh. Nhúng trong ShotDefinition (ScriptableObject).</summary>
    [Serializable]
    public struct ShotParams
    {
        public ShotAimMode mode;

        [Header("Điểm đích (tính từ lưới, trên sân đối phương)")]
        [Tooltip("Khoảng cách điểm rơi mục tiêu tới lưới (m). Biên cuối = 6.7.")]
        public float targetDepth;
        [Tooltip("Giữ W/S thay đổi độ sâu ± bao nhiêu mét (theo góc nhìn người đánh: W = dài hơn).")]
        public float aimDepth;
        [Tooltip("Giữ A/D đưa điểm đích sang ngang tối đa bao nhiêu mét so với trục giữa sân.")]
        public float aimWidth;

        [Header("Quỹ đạo")]
        [Tooltip("Target: góc nâng ban đầu (độ). Direct: bỏ qua.")]
        public float elevation;
        [Tooltip("Direct: tốc độ cú đánh (m/s). Target: tốc độ tối đa cho phép.")]
        public float speed;

        [Header("Sai số")]
        [Tooltip("Bán kính lệch điểm rơi khi timing hoàn hảo (m).")]
        public float scatter;
        [Tooltip("Bán kính lệch điểm rơi khi timing tệ nhất (m).")]
        public float badScatter;
        [Tooltip("Timing tệ nhất làm cú đánh ngắn/yếu đi bao nhiêu (0.3 = mất 30%).")]
        [Range(0f, 0.8f)] public float badPowerLoss;
    }

    /// <summary>Bối cảnh tại thời điểm chạm cầu.</summary>
    public struct ShotContext
    {
        public Vector3 Contact;
        public CourtSide Side;       // nửa sân của người đánh
        public Vector2 Aim;          // x: phải(+)/trái(−), y: dài(+)/ngắn(−) – theo góc nhìn người đánh
        public float Quality;        // 1 = timing hoàn hảo, 0 = tệ nhất
        public float? TargetXOverride; // toạ độ X thế giới bắt buộc (giao cầu vào ô chéo)
    }

    /// <summary>
    /// Đổi (loại cú đánh + hướng ngắm + chất lượng timing) thành vận tốc ban đầu của quả cầu.
    /// Thuần C#, tất định theo seed của System.Random ⇒ server tái tạo được đúng cú đánh.
    /// </summary>
    public static class ShotResolver
    {
        const float NetMargin = 0.12f; // cầu phải qua mép lưới ít nhất ngần này (m)

        public static Vector3 Resolve(in ShotParams p, in ShotContext c, ShuttlePhysics physics, float dt, System.Random rng)
        {
            Vector3 target = Target(p, c, rng);

            // Timing kém ⇒ cú đánh hụt lực: kéo điểm đích lại gần người đánh.
            float q = Mathf.Clamp01(c.Quality);
            float power = 1f - p.badPowerLoss * (1f - q);
            Vector3 flat = target - c.Contact; flat.y = 0f;
            target = new Vector3(c.Contact.x, 0f, c.Contact.z) + flat * power;

            return p.mode == ShotAimMode.Direct
                ? DirectShot(p, c, target, power, physics, dt)
                : TargetShot(p, c, target, physics, dt);
        }

        /// <summary>Điểm đích trên sàn sân đối phương (thế giới), đã cộng sai số ngẫu nhiên.</summary>
        public static Vector3 Target(in ShotParams p, in ShotContext c, System.Random rng)
        {
            float toward = -(int)c.Side;                 // dấu Z của sân đối phương
            float right = CourtDimensions.RightSign(c.Side);

            float depth = p.targetDepth + Mathf.Clamp(c.Aim.y, -1f, 1f) * p.aimDepth;
            float x = c.TargetXOverride ?? right * Mathf.Clamp(c.Aim.x, -1f, 1f) * p.aimWidth;

            float radius = Mathf.Lerp(p.badScatter, p.scatter, Mathf.Clamp01(c.Quality));
            x += Gauss(rng) * radius * 0.5f;
            depth += Gauss(rng) * radius * 0.5f;

            return new Vector3(x, 0f, toward * Mathf.Max(0.3f, depth));
        }

        static Vector3 TargetShot(in ShotParams p, in ShotContext c, Vector3 target, ShuttlePhysics physics, float dt)
        {
            // 1) Góc mặc định quá "tốn sức" (cần nhanh hơn tốc độ tối đa) ⇒ hạ góc dần cho cầu đi xa hơn
            //    (đánh từ cuối sân thì quỹ đạo thấp hơn, giống người thật).
            float startEl = p.elevation;
            if (p.speed > 0f)
                for (float el = p.elevation; el >= 25f; el -= 5f)
                {
                    startEl = el;
                    if (physics.SolveLaunchVelocity(c.Contact, target, el, dt).magnitude <= p.speed) break;
                }

            // 2) Nếu quỹ đạo đâm lưới thì nâng góc dần.
            Vector3 v = Vector3.zero;
            for (float el = startEl; el <= 80f; el += 4f)
            {
                v = physics.SolveLaunchVelocity(c.Contact, target, el, dt);
                if (p.speed > 0f && v.magnitude > p.speed) v = v.normalized * p.speed;
                if (ClearsNet(c.Contact, v, physics, dt)) break;
            }
            return v;
        }

        static Vector3 DirectShot(in ShotParams p, in ShotContext c, Vector3 target, float power, ShuttlePhysics physics, float dt)
        {
            // Smash: bắn thẳng về điểm đích; nếu đường thẳng đâm lưới thì nâng góc từng độ.
            float speed = p.speed * Mathf.Lerp(0.75f, 1f, power);
            Vector3 d = target - c.Contact;
            Vector3 dirH = new Vector3(d.x, 0f, d.z).normalized;
            float el = Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;

            Vector3 v = Vector3.zero;
            for (int i = 0; i < 30; i++, el += 1f)
            {
                float r = el * Mathf.Deg2Rad;
                v = (dirH * Mathf.Cos(r) + Vector3.up * Mathf.Sin(r)) * speed;
                if (ClearsNet(c.Contact, v, physics, dt)) break;
            }
            return v;
        }

        /// <summary>Mô phỏng thử: cầu có qua lưới (cao hơn mép lưới + NetMargin) không.</summary>
        public static bool ClearsNet(Vector3 from, Vector3 v, ShuttlePhysics physics, float dt)
        {
            var s = ShuttleState.Launched(from, v);
            float startSide = Mathf.Sign(from.z);
            for (float t = 0f; t < 4f; t += dt)
            {
                Vector3 prev = s.Position;
                var r = physics.Step(ref s, dt);
                if (r.Has(ShuttleEvents.HitNet)) return false;
                if (Mathf.Sign(s.Position.z) != startSide)
                {
                    // Vừa qua mặt phẳng lưới: kiểm tra khoảng hở so với mép lưới.
                    float k = prev.z / (prev.z - s.Position.z);
                    Vector3 cross = Vector3.Lerp(prev, s.Position, k);
                    return cross.y >= CourtDimensions.NetTopHeightAt(cross.x) + NetMargin;
                }
                if (r.Has(ShuttleEvents.LandedFirst) || s.Phase != ShuttlePhase.Flying) return false;
            }
            return false;
        }

        /// <summary>Phân phối chuẩn xấp xỉ (Box–Muller), cắt ở ±2.5σ.</summary>
        public static float Gauss(System.Random rng)
        {
            double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
            double z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            return Mathf.Clamp((float)z, -2.5f, 2.5f);
        }
    }
}
