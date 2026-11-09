using System;
using System.Collections.Generic;
using UnityEngine;

namespace Badminton.Tour
{
    // Mọi con số cân bằng của chế độ Giải đấu. Các lớp ở đây là [Serializable] thuần, được bọc trong
    // ScriptableObject "TournamentSettings" (Runtime) để chỉnh trong Inspector.

    /// <summary>Bộ sinh kết quả trận AI vs AI (không mô phỏng – chỉ sinh tỷ số cuối cùng).</summary>
    [Serializable]
    public class ResultGenConfig
    {
        [Tooltip("Hệ số trong P(A thắng) = 1 / (1 + 10^((RatingB − RatingA) / hệ_số)). Nhỏ ⇒ người rating cao thắng chắc hơn.")]
        public float ratingScale = 25f;
        [Tooltip("Nhiễu ngẫu nhiên cộng vào chênh lệch rating mỗi trận (độ lệch chuẩn, điểm rating) – tạo bất ngờ.")]
        public float ratingNoise = 2.5f;

        [Header("Số game")]
        [Tooltip("Xác suất đánh đủ 3 game khi hai bên ngang tài (P ≈ 50%).")]
        [Range(0f, 1f)] public float threeGamesEven = 0.42f;
        [Tooltip("Xác suất đánh đủ 3 game khi chênh lệch rất lớn (P ≈ 100%).")]
        [Range(0f, 1f)] public float threeGamesLopsided = 0.06f;

        [Header("Điểm của bên thua một game")]
        [Tooltip("Điểm trung bình của bên thua khi hai bên ngang tài.")]
        public float loserPointsEven = 17f;
        [Tooltip("Điểm trung bình của bên thua khi chênh lệch rất lớn.")]
        public float loserPointsLopsided = 10.5f;
        [Tooltip("Độ lệch chuẩn điểm của bên thua.")]
        public float loserPointsSpread = 2.8f;
        [Tooltip("Khi đã vào deuce (20-20): xác suất mỗi lượt deuce kéo dài thêm (22-20 → 23-21 → …). Tới 29-29 thì 30-29.")]
        [Range(0f, 0.9f)] public float deuceContinue = 0.38f;
    }

    /// <summary>Ánh xạ Rating (1–100) sang 4 bậc độ khó AI có sẵn.</summary>
    [Serializable]
    public class DifficultyMapConfig
    {
        [Tooltip("Rating < mốc này ⇒ Dễ.")] public int normalFrom = 40;
        [Tooltip("Rating < mốc này ⇒ Thường.")] public int hardFrom = 65;
        [Tooltip("Rating ≥ mốc này ⇒ Rất khó (còn lại Khó).")] public int veryHardFrom = 85;

        public static readonly string[] Names = { "DỄ", "THƯỜNG", "KHÓ", "RẤT KHÓ" };

        /// <summary>0 = Dễ, 1 = Thường, 2 = Khó, 3 = Rất khó.</summary>
        public int Tier(int rating) => rating >= veryHardFrom ? 3 : rating >= hardFrom ? 2 : rating >= normalFrom ? 1 : 0;
    }

    /// <summary>Cấp giải hư cấu.</summary>
    [Serializable]
    public class TierDef
    {
        [Tooltip("Tên cấp giải hư cấu.")] public string name = "TITAN 1000";
        [Tooltip("Mức (1000/750/500/300/100) – để sắp xếp uy tín.")] public int level = 1000;
        [Tooltip("Màu nhận diện cấp giải (hex).")] public string color = "FFC828";

        [Header("Điểm xếp hạng theo vòng đạt được")]
        [Tooltip("Vô địch.")] public int champion = 12000;
        [Tooltip("Mỗi vòng thấp hơn còn bao nhiêu % so với vòng trên (0.82 = giảm 18%).")]
        [Range(0.5f, 0.95f)] public float roundFactor = 0.82f;
        [Tooltip("Bị loại ở chung kết vòng loại: % điểm của vòng đầu nhánh chính.")]
        [Range(0f, 1f)] public float qualFinalFactor = 0.5f;
        [Tooltip("Bị loại ở vòng loại đầu tiên: % điểm của vòng đầu nhánh chính.")]
        [Range(0f, 1f)] public float qualFirstFactor = 0.3f;

        [Header("Thể thức")]
        [Tooltip("Có vòng loại không (thường chỉ giải cấp thấp 300/100).")] public bool hasQualifying;
        [Tooltip("Số suất vào nhánh chính dành cho người thắng vòng loại.")] public int qualifiers = 4;
        [Tooltip("Số người đánh vòng loại.")] public int qualifyingDraw = 16;

        [Header("Điều kiện tham dự của người chơi (theo thứ hạng thế giới / số suất của nội dung)")]
        [Tooltip("Vào thẳng nhánh chính nếu thứ hạng ≤ tỷ lệ này × số VĐV của nội dung.")]
        [Range(0f, 1f)] public float directRankFraction = 0.5f;
        [Tooltip("Được đánh vòng loại nếu thứ hạng ≤ tỷ lệ này × số VĐV (0 = không có vòng loại).")]
        [Range(0f, 1f)] public float qualifyingRankFraction = 0f;

        [Header("Xác suất AI đăng ký thi đấu (còn lại: rút lui / bỏ giải)")]
        [Tooltip("Nhóm top 10 thế giới.")] [Range(0f, 1f)] public float playTop10 = 0.95f;
        [Tooltip("Hạng 11–32.")] [Range(0f, 1f)] public float playTop32 = 0.95f;
        [Tooltip("Hạng sau 32.")] [Range(0f, 1f)] public float playRest = 0.9f;

        /// <summary>Bản sao không có vòng loại (giải diễn ra song song cùng tuần với giải khác).</summary>
        public TierDef WithoutQualifying()
        {
            var c = (TierDef)MemberwiseClone();
            c.hasQualifying = false;
            return c;
        }

        /// <summary>Điểm cho vòng đạt được: depth 0 = vô địch, 1 = á quân, 2 = bán kết, 3 = tứ kết…</summary>
        public int PointsForDepth(int depth)
        {
            double p = champion * Math.Pow(roundFactor, depth);
            return (int)(Math.Round(p / 10.0) * 10);
        }
    }

    /// <summary>Giải chung kết cuối mùa (vòng bảng + loại trực tiếp).</summary>
    [Serializable]
    public class FinalsDef
    {
        [Tooltip("Tên giải chung kết cuối mùa (hư cấu).")] public string name = "GRAND FINALS";
        public string color = "E04CFF";
        [Tooltip("Vô địch.")] public int champion = 14000;
        [Tooltip("Á quân.")] public int runnerUp = 11900;
        [Tooltip("Bán kết.")] public int semifinal = 9800;
        [Tooltip("Thứ 3 bảng.")] public int groupThird = 7600;
        [Tooltip("Thứ 4 bảng.")] public int groupFourth = 6600;
        [Tooltip("Số VĐV/cặp dự giải (2 bảng × 4).")] public int field = 8;
    }

    [Serializable]
    public class CalendarSlotDef
    {
        [Tooltip("Tuần trong năm (1–52). Nhiều giải cùng tuần ⇒ người chơi chọn 1 trong số đó; AI chia nhau, giải cấp cao chọn người trước.")] public int week = 1;
        [Tooltip("Tên giải hư cấu.")] public string name = "VALEMONT OPEN";
        [Tooltip("Chỉ số cấp giải trong danh sách tiers (0 = cao nhất).")] public int tier = 2;
    }

    /// <summary>Mùa giải: lịch, bảng xếp hạng, trôi rating.</summary>
    [Serializable]
    public class SeasonConfig
    {
        [Tooltip("Bảng xếp hạng = tổng N kết quả tốt nhất…")] public int bestResults = 10;
        [Tooltip("…trong cửa sổ trượt bao nhiêu tuần gần nhất.")] public int windowWeeks = 52;
        [Tooltip("Tuần diễn ra giải chung kết cuối mùa.")] public int finalsWeek = 50;
        [Tooltip("Rating AI thay đổi tối đa ± bao nhiêu sau mỗi giải.")] public int ratingDriftMax = 3;
        [Tooltip("Hệ số đổi (số trận thắng − số trận thắng kỳ vọng) thành điểm rating.")] public float ratingDriftGain = 1.6f;
        [Tooltip("Đầu mùa mới: kéo rating về rating gốc bao nhiêu % (0 = giữ nguyên, 1 = về hẳn gốc).")]
        [Range(0f, 1f)] public float newSeasonRegression = 0.3f;
        [Tooltip("Rating tối thiểu / tối đa.")] public int ratingMin = 20, ratingMax = 99;

        public List<CalendarSlotDef> calendar = new List<CalendarSlotDef>();
    }

    /// <summary>Database VĐV hư cấu.</summary>
    [Serializable]
    public class DatabaseConfig
    {
        [Tooltip("Số VĐV mỗi nội dung đơn.")] public int singlesPerEvent = 64;
        [Tooltip("Số cặp mỗi nội dung đôi.")] public int pairsPerEvent = 48;
        [Tooltip("Rating của người mạnh nhất.")] public int topRating = 95;
        [Tooltip("Rating của người yếu nhất.")] public int bottomRating = 38;
        [Tooltip("Hình dạng đường cong rating (<1 ⇒ nhiều người mạnh hơn, >1 ⇒ ít người mạnh).")]
        public float curve = 0.6f;
        [Tooltip("Nhiễu rating ± (điểm).")] public int noise = 2;
        [Tooltip("Seed sinh database (đổi seed ⇒ database khác).")] public int seed = 20261108;
    }

    /// <summary>Người chơi (thi đấu bằng tên đặt ở menu chính – không chọn VĐV).</summary>
    [Serializable]
    public class PlayerSetupConfig
    {
        [Tooltip("Suất thi đấu tự gán cho người chơi ở mỗi nội dung: vị trí theo thứ hạng rating " +
                 "(0 = mạnh nhất, 0.5 = giữa bảng, 1 = yếu nhất). Quyết định hạt giống / thứ hạng ban đầu, KHÔNG ảnh hưởng độ khó.")]
        [Range(0f, 1f)] public float startRankFraction = 0.5f;
    }

    /// <summary>Giải tùy chỉnh (4/8/16 VĐV).</summary>
    [Serializable]
    public class CustomTournamentConfig
    {
        [Tooltip("Tên giải tùy chỉnh.")] public string name = "GIẢI TÙY CHỈNH";
        [Tooltip("Tỷ lệ đối thủ có thứ hạng CAO HƠN người chơi trong giải (0 = toàn người yếu hơn, 1 = toàn người mạnh hơn).")]
        [Range(0f, 1f)] public float strongerFraction = 0.5f;
    }

    /// <summary>Gom toàn bộ cấu hình cân bằng của chế độ Giải đấu.</summary>
    [Serializable]
    public class TourBalance
    {
        public ResultGenConfig results = new ResultGenConfig();
        public DifficultyMapConfig difficulty = new DifficultyMapConfig();
        [Tooltip("5 cấp giải từ cao xuống thấp: 1000, 750, 500, 300, 100.")]
        public List<TierDef> tiers = DefaultTiers();
        public FinalsDef finals = new FinalsDef();
        public SeasonConfig season = DefaultSeason();
        public DatabaseConfig database = new DatabaseConfig();
        public CustomTournamentConfig custom = new CustomTournamentConfig();
        public PlayerSetupConfig player = new PlayerSetupConfig();
        [Tooltip("Kích thước nhánh chính giải loại trực tiếp.")] public int mainDraw = 32;
        [Tooltip("Số hạt giống nhánh 32.")] public int seeds = 8;

        public TierDef Tier(int i) => tiers[Math.Max(0, Math.Min(tiers.Count - 1, i))];

        public static List<TierDef> DefaultTiers() => new List<TierDef>
        {
            new TierDef { name = "TITAN 1000", level = 1000, color = "FFC828", champion = 12000,
                directRankFraction = 0.45f, playTop10 = 0.97f, playTop32 = 0.95f, playRest = 0.9f },
            new TierDef { name = "ZENITH 750", level = 750, color = "3CC8E6", champion = 11000,
                directRankFraction = 0.55f, playTop10 = 0.9f, playTop32 = 0.9f, playRest = 0.9f },
            new TierDef { name = "PRISM 500", level = 500, color = "3CC86E", champion = 9200,
                directRankFraction = 0.7f, playTop10 = 0.7f, playTop32 = 0.85f, playRest = 0.9f },
            new TierDef { name = "HORIZON 300", level = 300, color = "FF8C3C", champion = 7000, hasQualifying = true,
                directRankFraction = 0.8f, qualifyingRankFraction = 1f, playTop10 = 0.25f, playTop32 = 0.6f, playRest = 0.95f },
            new TierDef { name = "SPARK 100", level = 100, color = "AAB9D2", champion = 5500, hasQualifying = true,
                directRankFraction = 1f, qualifyingRankFraction = 1f, playTop10 = 0.05f, playTop32 = 0.3f, playRest = 0.95f },
        };

        public static SeasonConfig DefaultSeason()
        {
            var s = new SeasonConfig();
            void C(int w, string n, int t) => s.calendar.Add(new CalendarSlotDef { week = w, name = n, tier = t });
            C(2,  "KESTREL BAY CLASSIC", 3);
            C(4,  "NORTHGATE OPEN",      2);
            C(7,  "AURORA OPEN",         0);
            C(9,  "SILVERPINE CUP",      4);
            C(9,  "MISTRAL BAY OPEN",    2);   // tuần có 2 giải song song
            C(12, "MARIGOLD COAST OPEN", 1);
            C(14, "IRONWOOD CLASSIC",    3);
            C(17, "LUMEN CITY OPEN",     2);
            C(20, "CORAL HARBOR CUP",    4);
            C(23, "STARFALL OPEN",       0);
            C(26, "WILLOWMERE CLASSIC",  3);
            C(29, "BLUE MESA OPEN",      2);
            C(32, "TEMPEST ISLES OPEN",  1);
            C(35, "AMBERFIELD CUP",      4);
            C(35, "PINECREST OPEN",      3);   // tuần có 2 giải song song
            C(38, "HIGHCROFT OPEN",      2);
            C(41, "CRYSTAL FJORD OPEN",  0);
            C(44, "REDWATER CLASSIC",    3);
            C(47, "SUNSPIRE OPEN",       1);
            return s;
        }
    }
}
