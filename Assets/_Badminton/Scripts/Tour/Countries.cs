using System.Collections.Generic;

namespace Badminton.Tour
{
    /// <summary>Phong cách tên theo khu vực (bộ sinh tên dùng).</summary>
    public enum NameStyle : byte
    {
        Chinese, Japanese, Korean, Indonesian, Malaysian, Thai, Vietnamese, Indian,
        Nordic, French, German, British, Spanish, Dutch, American, SingaporeChinese,
    }

    /// <summary>
    /// Cờ vẽ bằng khối màu đơn giản (low-poly): các sọc ngang/dọc + tuỳ chọn ô góc (canton),
    /// chấm tròn giữa (emblem) hoặc chữ thập (kiểu Bắc Âu / Anh). Màu dạng hex "RRGGBB".
    /// </summary>
    public sealed class FlagSpec
    {
        public string[] Stripes;
        public bool Vertical;
        public string Canton;      // ô góc trên-trái
        public string Dot;         // chấm giữa
        public string Dot2;        // nửa dưới của chấm giữa (vd. Hàn Quốc: đỏ trên, xanh dưới)
        public string CornerStars; // 1 sao lớn góc trên-trái + 4 sao nhỏ xếp vòng cung (Trung Quốc)
        public string Cross;       // chữ thập
        public bool NordicCross;   // chữ thập lệch trái kiểu Bắc Âu
    }

    public sealed class CountryInfo
    {
        public string Code;     // mã 3 chữ
        public string Name;     // tên tiếng Việt
        public NameStyle Style;
        public float Weight;    // tỷ trọng VĐV khi sinh database
        public string[] Skin;   // bảng màu da
        public FlagSpec Flag;
    }

    /// <summary>Quốc gia có thật (được phép dùng cờ thật); tên VĐV vẫn là hư cấu.</summary>
    public static class Countries
    {
        static readonly string[] SkinEastAsia = { "F1C9A5", "E8B990", "F3D0B0", "DDAA80" };
        static readonly string[] SkinSouthEast = { "D9A06E", "C68A5A", "E0AE7E", "B97A4A" };
        static readonly string[] SkinSouthAsia = { "B07045", "9C6038", "C08055", "8C5530" };
        static readonly string[] SkinEurope = { "F5D3B8", "EEC4A4", "F8DCC6", "E2B08C" };
        static readonly string[] SkinMixed = { "F5D3B8", "C68A5A", "8C5530", "E2B08C", "6E4428" };

        public static readonly List<CountryInfo> All = new List<CountryInfo>
        {
            C("CHN", "Trung Quốc", NameStyle.Chinese, 10, SkinEastAsia, new FlagSpec { Stripes = new[] { "DE2910" }, CornerStars = "FFDE00" }),
            C("JPN", "Nhật Bản", NameStyle.Japanese, 8, SkinEastAsia, new FlagSpec { Stripes = new[] { "FFFFFF" }, Dot = "BC002D" }),
            C("KOR", "Hàn Quốc", NameStyle.Korean, 6, SkinEastAsia, new FlagSpec { Stripes = new[] { "FFFFFF" }, Dot = "CD2E3A", Dot2 = "0047A0" }),
            C("INA", "Indonesia", NameStyle.Indonesian, 7, SkinSouthEast, new FlagSpec { Stripes = new[] { "CE1126", "FFFFFF" } }),
            C("MAS", "Malaysia", NameStyle.Malaysian, 5, SkinSouthEast, new FlagSpec { Stripes = new[] { "CC0001", "FFFFFF", "CC0001", "FFFFFF", "CC0001", "FFFFFF" }, Canton = "010066" }),
            C("THA", "Thái Lan", NameStyle.Thai, 5, SkinSouthEast, new FlagSpec { Stripes = new[] { "A51931", "F4F5F8", "2D2A4A", "2D2A4A", "F4F5F8", "A51931" } }),
            C("VIE", "Việt Nam", NameStyle.Vietnamese, 3, SkinSouthEast, new FlagSpec { Stripes = new[] { "DA251D" }, Dot = "FFFF00" }),
            C("IND", "Ấn Độ", NameStyle.Indian, 6, SkinSouthAsia, new FlagSpec { Stripes = new[] { "FF9933", "FFFFFF", "138808" }, Dot = "000080" }),
            C("DEN", "Đan Mạch", NameStyle.Nordic, 6, SkinEurope, new FlagSpec { Stripes = new[] { "C8102E" }, Cross = "FFFFFF", NordicCross = true }),
            C("SWE", "Thụy Điển", NameStyle.Nordic, 1, SkinEurope, new FlagSpec { Stripes = new[] { "006AA7" }, Cross = "FECC00", NordicCross = true }),
            C("FRA", "Pháp", NameStyle.French, 2, SkinMixed, new FlagSpec { Stripes = new[] { "002395", "FFFFFF", "ED2939" }, Vertical = true }),
            C("GER", "Đức", NameStyle.German, 2, SkinEurope, new FlagSpec { Stripes = new[] { "000000", "DD0000", "FFCE00" } }),
            C("ENG", "Anh", NameStyle.British, 2, SkinEurope, new FlagSpec { Stripes = new[] { "FFFFFF" }, Cross = "CE1124" }),
            C("ESP", "Tây Ban Nha", NameStyle.Spanish, 1, SkinEurope, new FlagSpec { Stripes = new[] { "AA151B", "F1BF00", "F1BF00", "AA151B" } }),
            C("NED", "Hà Lan", NameStyle.Dutch, 1, SkinEurope, new FlagSpec { Stripes = new[] { "AE1C28", "FFFFFF", "21468B" } }),
            C("USA", "Hoa Kỳ", NameStyle.American, 1, SkinMixed, new FlagSpec { Stripes = new[] { "B22234", "FFFFFF", "B22234", "FFFFFF", "B22234", "FFFFFF", "B22234" }, Canton = "3C3B6E" }),
            C("CAN", "Canada", NameStyle.American, 1, SkinMixed, new FlagSpec { Stripes = new[] { "FF0000", "FFFFFF", "FFFFFF", "FF0000" }, Vertical = true, Dot = "FF0000" }),
            C("SGP", "Singapore", NameStyle.SingaporeChinese, 2, SkinEastAsia, new FlagSpec { Stripes = new[] { "EF3340", "FFFFFF" } }),
        };

        static readonly Dictionary<string, CountryInfo> _byCode = new Dictionary<string, CountryInfo>();

        static CountryInfo C(string code, string name, NameStyle style, float w, string[] skin, FlagSpec flag) =>
            new CountryInfo { Code = code, Name = name, Style = style, Weight = w, Skin = skin, Flag = flag };

        public static CountryInfo Get(string code)
        {
            if (_byCode.Count == 0) foreach (var c in All) _byCode[c.Code] = c;
            return code != null && _byCode.TryGetValue(code, out var ci) ? ci : All[0];
        }

        public static CountryInfo PickWeighted(DetRandom rng)
        {
            float total = 0f;
            foreach (var c in All) total += c.Weight;
            double r = rng.NextDouble() * total;
            foreach (var c in All) { r -= c.Weight; if (r <= 0) return c; }
            return All[All.Count - 1];
        }
    }
}
