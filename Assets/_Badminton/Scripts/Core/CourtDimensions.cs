using UnityEngine;

namespace Badminton.Core
{
    /// <summary>
    /// Nửa sân. Quy ước toạ độ toàn dự án:
    ///   - Lưới nằm trên trục X tại z = 0, mặt sân là y = 0.
    ///   - Chiều dài sân theo trục Z, chiều rộng theo trục X.
    ///   - Near = nửa sân z &lt; 0 (phía camera broadcast), Far = nửa sân z &gt; 0.
    /// Giá trị enum chính là dấu của trục Z để nhân trực tiếp vào toạ độ.
    /// </summary>
    public enum CourtSide { Near = -1, Far = 1 }

    /// <summary>
    /// Kích thước sân chuẩn BWF (đơn vị mét). Thuần C#, dùng chung cho dựng sân,
    /// xét cầu trong/ngoài, AI và server.
    /// Theo luật BWF, vạch kẻ thuộc về phần sân mà nó giới hạn ⇒ mép NGOÀI vạch biên là biên sân.
    /// </summary>
    public static class CourtDimensions
    {
        public const float Length = 13.40f;
        public const float HalfLength = Length * 0.5f;           // 6.70 – từ lưới tới biên cuối
        public const float DoublesWidth = 6.10f;
        public const float SinglesWidth = 5.18f;
        public const float HalfDoublesWidth = DoublesWidth * 0.5f; // 3.05
        public const float HalfSinglesWidth = SinglesWidth * 0.5f; // 2.59

        public const float ShortServiceLineFromNet = 1.98f;
        public const float DoublesLongServiceFromBaseline = 0.76f;
        public const float DoublesLongServiceFromNet = HalfLength - DoublesLongServiceFromBaseline; // 5.94

        public const float LineWidth = 0.04f;

        public const float NetHeightCenter = 1.524f;
        public const float NetHeightPost = 1.55f;
        public const float NetDepth = 0.76f;                 // chiều cao tấm lưới (từ dải trắng xuống)
        public const float NetTapeHeight = 0.075f;           // dải trắng trên cùng
        public const float PostX = HalfDoublesWidth;         // cột lưới đặt trên biên dọc đánh đôi

        /// <summary>Chiều cao mép trên lưới tại toạ độ x (lưới võng dạng parabol giữa 2 cột).</summary>
        public static float NetTopHeightAt(float x)
        {
            float t = Mathf.Clamp01(Mathf.Abs(x) / PostX);
            return Mathf.Lerp(NetHeightCenter, NetHeightPost, t * t);
        }

        /// <summary>Điểm rơi (x,z) có nằm trong sân đơn không – tính cả vạch là TRONG.</summary>
        public static bool IsInsideSingles(float x, float z)
            => Mathf.Abs(x) <= HalfSinglesWidth && Mathf.Abs(z) <= HalfLength;

        /// <summary>Điểm rơi (x,z) có nằm trong sân ĐÔI không (biên dọc ngoài) – tính cả vạch là TRONG.</summary>
        public static bool IsInsideDoubles(float x, float z)
            => Mathf.Abs(x) <= HalfDoublesWidth && Mathf.Abs(z) <= HalfLength;

        /// <summary>
        /// Ô giao cầu ĐÔI: từ vạch giao cầu ngắn (1.98 m) tới vạch giao cầu dài đánh đôi (cách biên cuối 0.76 m),
        /// rộng tới biên dọc đánh đôi. Tính cả vạch.
        /// </summary>
        public static bool IsInsideDoublesServiceCourt(float x, float z, CourtSide side, ServiceCourt court)
        {
            if (SideOf(z) != side) return false;
            float dz = Mathf.Abs(z);
            if (dz < ShortServiceLineFromNet || dz > DoublesLongServiceFromNet) return false;
            float localX = x * RightSign(side);
            float half = LineWidth * 0.5f;
            return court == ServiceCourt.Right
                ? localX >= -half && localX <= HalfDoublesWidth
                : localX <= half && localX >= -HalfDoublesWidth;
        }

        public static bool IsInside(float x, float z, bool doubles) => doubles ? IsInsideDoubles(x, z) : IsInsideSingles(x, z);

        public static bool IsInsideServiceCourt(float x, float z, CourtSide side, ServiceCourt court, bool doubles) =>
            doubles ? IsInsideDoublesServiceCourt(x, z, side, court) : IsInsideSinglesServiceCourt(x, z, side, court);

        /// <summary>Nửa sân chứa điểm z.</summary>
        public static CourtSide SideOf(float z) => z < 0f ? CourtSide.Near : CourtSide.Far;

        /// <summary>
        /// Toạ độ X thế giới tương ứng với "bên phải" (dir = +1) hoặc "bên trái" (−1) của người đứng ở nửa sân `side`.
        /// Người ở Near nhìn về +Z nên phải = +X; người ở Far nhìn về −Z nên phải = −X.
        /// </summary>
        public static float RightSign(CourtSide side) => side == CourtSide.Near ? 1f : -1f;

        /// <summary>
        /// Điểm (x,z) có nằm trong ô giao cầu ĐƠN `court` (phải/trái theo góc nhìn người nhận) ở nửa sân `side` không.
        /// Ô giao cầu đơn: từ vạch giao cầu ngắn (1.98 m) tới biên cuối, giữa vạch giữa và biên dọc đơn. Tính cả vạch.
        /// </summary>
        public static bool IsInsideSinglesServiceCourt(float x, float z, CourtSide side, ServiceCourt court)
        {
            if (SideOf(z) != side) return false;
            float dz = Mathf.Abs(z);
            if (dz < ShortServiceLineFromNet || dz > HalfLength) return false;

            // Đổi x về hệ "phải của người nhận": dương = ô phải.
            float localX = x * RightSign(side);
            float half = LineWidth * 0.5f; // vạch giữa rộng 4 cm, thuộc về cả 2 ô
            return court == ServiceCourt.Right
                ? localX >= -half && localX <= HalfSinglesWidth
                : localX <= half && localX >= -HalfSinglesWidth;
        }

        /// <summary>Vị trí đứng giao/nhận cầu chuẩn trong ô `court` của nửa sân `side`.</summary>
        public static Vector3 ServiceStandPoint(CourtSide side, ServiceCourt court, bool receiver)
        {
            float x = (court == ServiceCourt.Right ? 1f : -1f) * RightSign(side) * 0.9f;
            float dz = receiver ? 3.6f : 3.0f;
            return new Vector3(x, 0f, (int)side * dz);
        }
    }
}
