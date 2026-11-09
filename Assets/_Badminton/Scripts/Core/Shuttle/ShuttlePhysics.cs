using System;
using System.Collections.Generic;
using UnityEngine;

namespace Badminton.Core
{
    /// <summary>Thông số vật lý quả cầu. Serializable để nhúng vào ScriptableObject (ShuttleSettings).</summary>
    [Serializable]
    public class ShuttlePhysicsParams
    {
        [Tooltip("Gia tốc trọng trường (m/s²).")]
        public float gravity = 9.81f;

        [Tooltip("Vận tốc rơi tối đa (m/s). Cầu lông thật ≈ 6.7 m/s.")]
        public float terminalVelocity = 6.7f;

        [Tooltip("Số bước con trong mỗi FixedUpdate. Nhiều hơn ⇒ chính xác hơn với cú đập nhanh.")]
        [Range(1, 8)] public int substeps = 2;

        [Header("Chạm sàn")]
        [Tooltip("Hệ số nảy theo phương đứng (0 = không nảy).")]
        [Range(0f, 1f)] public float floorRestitution = 0.25f;
        [Tooltip("Phần vận tốc ngang còn lại sau mỗi lần nảy.")]
        [Range(0f, 1f)] public float floorFriction = 0.35f;
        [Tooltip("Vận tốc nảy nhỏ hơn mức này thì cầu nằm yên.")]
        public float restSpeed = 0.5f;
        public int maxBounces = 3;

        [Header("Chạm lưới")]
        [Tooltip("Cầu chạm trong dải này tính từ mép trên lưới ⇒ trượt qua lưới (net cord).")]
        public float netCordBand = 0.04f;

        [Header("Giải ngược vận tốc (aim)")]
        public float maxSolveSpeed = 150f;

        /// <summary>k = g / Vt². Khi |v| = Vt thì lực cản cân bằng trọng lực ⇒ đúng vận tốc rơi tối đa.</summary>
        public float DragK => gravity / (terminalVelocity * terminalVelocity);
    }

    public enum ShuttlePhase : byte
    {
        Idle,      // đang cầm trên tay / chưa giao
        Flying,    // đang bay, chưa chạm sàn
        Bouncing,  // đã chạm sàn, đang nảy nhỏ
        Resting,   // nằm yên trên sàn
    }

    /// <summary>Toàn bộ trạng thái của quả cầu – đủ nhỏ để gửi qua mạng (GĐ5).</summary>
    [Serializable]
    public struct ShuttleState
    {
        public Vector3 Position;
        public Vector3 Velocity;
        public ShuttlePhase Phase;
        public byte Bounces;

        public bool IsFlying => Phase == ShuttlePhase.Flying;

        public static ShuttleState Launched(Vector3 pos, Vector3 vel) =>
            new ShuttleState { Position = pos, Velocity = vel, Phase = ShuttlePhase.Flying };
    }

    [Flags]
    public enum ShuttleEvents
    {
        None = 0,
        LandedFirst = 1 << 0,      // lần chạm sàn ĐẦU TIÊN – dùng để xét trong/ngoài
        HitNet = 1 << 1,
        NetCord = 1 << 2,          // chạm mép lưới và trượt qua
        CrossedNet = 1 << 3,       // đi qua mặt phẳng lưới sang nửa sân kia
        CrossedUnderNet = 1 << 4,  // chui dưới lưới (lỗi)
        HitWall = 1 << 5,
        Stopped = 1 << 6,
    }

    public struct ShuttleStepResult
    {
        public ShuttleEvents Events;
        public Vector3 LandingPoint;  // hợp lệ khi có LandedFirst
        public Vector3 ContactPoint;  // điểm chạm lưới / tường
        public int WallIndex;

        public bool Has(ShuttleEvents e) => (Events & e) != 0;
    }

    /// <summary>Tường phẳng hình chữ nhật đứng (dùng cho chế độ tập với tường).</summary>
    [Serializable]
    public struct ShuttleWall
    {
        public Vector3 Point;     // tâm chân tường
        public Vector3 Normal;    // pháp tuyến hướng về phía người chơi
        public float HalfWidth;
        public float Height;
        public float Restitution;
    }

    public enum ShuttleOutcome { None, Floor, Wall, Timeout }

    public struct ShuttlePrediction
    {
        public ShuttleOutcome Outcome;
        public Vector3 Point;
        public float Time;
        public bool HitNet;
    }

    /// <summary>
    /// Vật lý quả cầu tự viết (không dùng Rigidbody):
    ///     a = g − k·|v|·v ,  k = g / Vt²
    /// Lực cản tỉ lệ bình phương vận tốc nên cầu giảm tốc RẤT nhanh sau cú đánh mạnh
    /// rồi rơi gần như thẳng đứng với vận tốc ≈ Vt.
    ///
    /// Tích phân "nửa ẩn" (semi-implicit) cho lực cản:
    ///     v' = (v + g·dt) / (1 + k·|v|·dt)
    /// Ổn định với mọi dt kể cả cú đập 100 m/s (Euler hiện có thể đổi dấu vận tốc), và cho
    /// đúng điểm cân bằng v = −Vt. Cùng code + cùng dt ⇒ cùng kết quả ⇒ dự đoán (AI, điểm rơi)
    /// và mô phỏng trên server/client trùng khớp nhau (tất định).
    /// </summary>
    public sealed class ShuttlePhysics
    {
        public readonly ShuttlePhysicsParams Params;
        public readonly List<ShuttleWall> Walls = new List<ShuttleWall>();

        public ShuttlePhysics(ShuttlePhysicsParams p) { Params = p ?? new ShuttlePhysicsParams(); }

        // ───────────────────────────── MÔ PHỎNG ─────────────────────────────

        public ShuttleStepResult Step(ref ShuttleState s, float dt)
        {
            var res = new ShuttleStepResult { WallIndex = -1 };
            if (s.Phase == ShuttlePhase.Idle || s.Phase == ShuttlePhase.Resting) return res;

            int n = Mathf.Max(1, Params.substeps);
            float h = dt / n;
            for (int i = 0; i < n && s.Phase != ShuttlePhase.Resting; i++)
                SubStep(ref s, h, ref res);
            return res;
        }

        void SubStep(ref ShuttleState s, float h, ref ShuttleStepResult res)
        {
            float g = Params.gravity, k = Params.DragK;

            // 1) Tích phân vận tốc + vị trí
            Vector3 v = s.Velocity;
            v = (v + new Vector3(0f, -g * h, 0f)) / (1f + k * v.magnitude * h);
            Vector3 p0 = s.Position;
            Vector3 p1 = p0 + v * h;

            // 2) Lưới: kiểm tra đoạn p0→p1 có cắt mặt phẳng z = 0 không
            if ((p0.z < 0f) != (p1.z < 0f) && Mathf.Abs(p0.z - p1.z) > 1e-6f)
                CollideNet(p0, ref p1, ref v, ref res);

            // 3) Tường tập
            for (int i = 0; i < Walls.Count; i++)
                if (CollideWall(Walls[i], p0, ref p1, ref v))
                {
                    res.Events |= ShuttleEvents.HitWall;
                    res.WallIndex = i;
                    res.ContactPoint = p1;
                    break;
                }

            // 4) Sàn
            if (p1.y <= 0f)
            {
                if (s.Phase == ShuttlePhase.Flying)
                {
                    // Nội suy đúng điểm y = 0 trên đoạn p0→p1 để xét trong/ngoài chính xác.
                    float dy = p0.y - p1.y;
                    Vector3 land = dy > 1e-6f ? Vector3.Lerp(p0, p1, p0.y / dy) : p1;
                    land.y = 0f;
                    res.Events |= ShuttleEvents.LandedFirst;
                    res.LandingPoint = land;
                }

                p1.y = 0f;
                s.Bounces++;
                v.y = -v.y * Params.floorRestitution;
                v.x *= Params.floorFriction;
                v.z *= Params.floorFriction;
                s.Phase = ShuttlePhase.Bouncing;

                if (v.y < Params.restSpeed || s.Bounces >= Params.maxBounces)
                {
                    v = Vector3.zero;
                    s.Phase = ShuttlePhase.Resting;
                    res.Events |= ShuttleEvents.Stopped;
                }
            }

            s.Position = p1;
            s.Velocity = v;
        }

        void CollideNet(Vector3 p0, ref Vector3 p1, ref Vector3 v, ref ShuttleStepResult res)
        {
            float t = p0.z / (p0.z - p1.z);
            Vector3 c = Vector3.Lerp(p0, p1, t);
            float fromSide = p0.z < 0f ? -1f : 1f;

            if (Mathf.Abs(c.x) > CourtDimensions.PostX) { res.Events |= ShuttleEvents.CrossedNet; return; }

            float top = CourtDimensions.NetTopHeightAt(c.x);
            float bottom = top - CourtDimensions.NetDepth;

            if (c.y > top) { res.Events |= ShuttleEvents.CrossedNet; return; }
            if (c.y < bottom) { res.Events |= ShuttleEvents.CrossedNet | ShuttleEvents.CrossedUnderNet; return; }

            res.Events |= ShuttleEvents.HitNet;
            res.ContactPoint = c;

            if (c.y >= top - Params.netCordBand)
            {
                // Chạm mép lưới: mất gần hết vận tốc, lật qua và rơi sát lưới bên kia.
                float to = -fromSide;
                p1 = new Vector3(c.x, c.y, to * 0.03f);
                v = new Vector3(v.x * 0.15f, Mathf.Min(v.y, 0f) * 0.2f, to * 0.25f);
                res.Events |= ShuttleEvents.NetCord | ShuttleEvents.CrossedNet;
            }
            else
            {
                // Đâm vào lưới: bị lưới "đỡ" lại, rơi xuống phía người đánh.
                p1 = new Vector3(c.x, c.y, fromSide * 0.03f);
                v = new Vector3(v.x * 0.1f, Mathf.Min(v.y, 0f) * 0.1f, fromSide * 0.3f);
            }
        }

        static bool CollideWall(in ShuttleWall w, Vector3 p0, ref Vector3 p1, ref Vector3 v)
        {
            float d0 = Vector3.Dot(p0 - w.Point, w.Normal);
            float d1 = Vector3.Dot(p1 - w.Point, w.Normal);
            if (!(d0 > 0f && d1 <= 0f)) return false;

            Vector3 c = Vector3.Lerp(p0, p1, d0 / (d0 - d1));
            Vector3 tangent = Vector3.Cross(w.Normal, Vector3.up).normalized;
            if (Mathf.Abs(Vector3.Dot(c - w.Point, tangent)) > w.HalfWidth || c.y > w.Height || c.y < 0f) return false;

            // Phản xạ: thành phần pháp tuyến đảo chiều × restitution, thành phần tiếp tuyến giảm bớt.
            Vector3 vn = Vector3.Dot(v, w.Normal) * w.Normal;
            v = (v - vn) * 0.6f - vn * w.Restitution;
            p1 = c + w.Normal * 0.02f;
            return true;
        }

        // ───────────────────────────── DỰ ĐOÁN ─────────────────────────────

        /// <summary>
        /// Chạy thử mô phỏng trên BẢN SAO trạng thái để biết cầu rơi ở đâu, lúc nào.
        /// Dùng cùng hàm Step và cùng dt với mô phỏng thật ⇒ kết quả khớp tuyệt đối.
        /// </summary>
        public ShuttlePrediction Predict(ShuttleState s, float dt, float maxTime = 6f,
                                         List<Vector3> path = null, bool stopAtWall = true)
        {
            var pred = new ShuttlePrediction();
            if (s.Phase != ShuttlePhase.Flying) return pred;

            path?.Add(s.Position);
            for (float t = dt; t <= maxTime; t += dt)
            {
                var r = Step(ref s, dt);
                path?.Add(s.Position);
                if (r.Has(ShuttleEvents.HitNet)) pred.HitNet = true;

                if (stopAtWall && r.Has(ShuttleEvents.HitWall))
                    return Done(ShuttleOutcome.Wall, r.ContactPoint, t);
                if (r.Has(ShuttleEvents.LandedFirst))
                    return Done(ShuttleOutcome.Floor, r.LandingPoint, t);
            }
            return Done(ShuttleOutcome.Timeout, s.Position, maxTime);

            ShuttlePrediction Done(ShuttleOutcome o, Vector3 p, float t)
            {
                pred.Outcome = o; pred.Point = p; pred.Time = t;
                return pred;
            }
        }

        /// <summary>
        /// Giải ngược: tìm vận tốc ban đầu (với góc nâng cho trước) để cầu đi qua độ cao target.y
        /// tại đúng vị trí ngang target. Lực cản phi tuyến ⇒ không có công thức đóng,
        /// dùng chia đôi (bisection) trên tốc độ vì tầm xa tăng đơn điệu theo tốc độ.
        /// Bỏ qua lưới/tường. Dùng cho máy bắn cầu, AI và (GĐ3) đánh cầu có điểm ngắm.
        /// </summary>
        public Vector3 SolveLaunchVelocity(Vector3 from, Vector3 target, float elevationDeg, float dt)
        {
            Vector3 flat = target - from; flat.y = 0f;
            float dist = flat.magnitude;
            Vector3 dir = dist > 1e-4f ? flat / dist : Vector3.forward;
            float el = elevationDeg * Mathf.Deg2Rad;
            Vector3 unit = dir * Mathf.Cos(el) + Vector3.up * Mathf.Sin(el);

            float lo = 0.5f, hi = Params.maxSolveSpeed;
            for (int i = 0; i < 24; i++)
            {
                float mid = 0.5f * (lo + hi);
                if (FreeRange(from, unit * mid, target.y, dt) < dist) lo = mid; else hi = mid;
            }
            return unit * hi;
        }

        /// <summary>Tầm xa ngang khi cầu đi xuống qua độ cao y (không va chạm).</summary>
        float FreeRange(Vector3 p, Vector3 v, float y, float dt)
        {
            float g = Params.gravity, k = Params.DragK;
            Vector3 start = p;
            bool crossed = false;
            for (float t = 0f; t < 8f; t += dt)
            {
                v = (v + new Vector3(0f, -g * dt, 0f)) / (1f + k * v.magnitude * dt);
                p += v * dt;
                if (v.y < 0f && p.y <= y) { crossed = true; break; }
            }
            if (!crossed) return 0f; // không với tới độ cao đích ⇒ coi như "quá ngắn"
            p.y = start.y;
            return Vector3.Distance(start, p);
        }
    }
}
