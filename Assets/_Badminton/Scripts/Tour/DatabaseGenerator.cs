using System;
using System.Collections.Generic;

namespace Badminton.Tour
{
    /// <summary>
    /// Sinh database VĐV hư cấu (tất định theo seed): mỗi nội dung đơn N VĐV, mỗi nội dung đôi M cặp
    /// (cặp đôi cùng quốc gia; đôi nam nữ = 1 nam + 1 nữ). VĐV đôi là người khác với VĐV đơn.
    /// Rating xếp theo đường cong từ topRating xuống bottomRating cộng nhiễu nhỏ.
    /// </summary>
    public static class DatabaseGenerator
    {
        static readonly string[] ShirtPalette =
        {
            "E63946", "F4A261", "2A9D8F", "457B9D", "1D3557", "E9C46A", "8338EC", "FF006E", "3A86FF", "06D6A0",
            "FB5607", "FFBE0B", "118AB2", "EF476F", "7209B7", "2EC4B6", "FF9F1C", "43AA8B", "F94144", "577590",
        };
        static readonly string[] ShortsPalette = { "1B1B1B", "2B2D42", "FFFFFF", "1D3557", "3D405B", "F1FAEE" };
        static readonly string[] HairPalette = { "1A1A1A", "2B1B12", "3B2A1E", "5A3825", "8C6239", "C9A15B", "E3C27A", "6B4226" };

        public static AthleteDatabase Generate(DatabaseConfig cfg)
        {
            var db = new AthleteDatabase();
            var rng = new DetRandom((ulong)cfg.seed * 7919UL + 17UL);
            var used = new HashSet<string>();

            foreach (var ev in Events.All)
            {
                bool doubles = Events.IsDoubles(ev);
                int n = doubles ? cfg.pairsPerEvent : cfg.singlesPerEvent;
                var ratings = RatingCurve(cfg, n, rng);
                for (int i = 0; i < n; i++)
                {
                    var country = Countries.PickWeighted(rng);
                    var entry = new Entry { id = db.entries.Count, ev = ev };
                    if (!doubles)
                    {
                        entry.a = Add(db, country, ev == TourEvent.MS ? Gender.Male : Gender.Female, ratings[i], rng, used);
                    }
                    else
                    {
                        Gender g1 = ev == TourEvent.WD ? Gender.Female : Gender.Male;
                        Gender g2 = ev == TourEvent.MD ? Gender.Male : Gender.Female;
                        // Mỗi người trong cặp lệch nhẹ quanh rating của cặp.
                        int d = rng.Range(-3, 3);
                        entry.a = Add(db, country, g1, ratings[i] + d, rng, used);
                        entry.b = Add(db, country, g2, ratings[i] - d, rng, used);
                    }
                    db.entries.Add(entry);
                }
            }
            return db;
        }

        /// <summary>Danh sách rating giảm dần (đã cộng nhiễu, kẹp 1..99).</summary>
        static int[] RatingCurve(DatabaseConfig cfg, int n, DetRandom rng)
        {
            var r = new int[n];
            for (int i = 0; i < n; i++)
            {
                double t = n <= 1 ? 0 : (double)i / (n - 1);
                double v = cfg.topRating - (cfg.topRating - cfg.bottomRating) * Math.Pow(t, cfg.curve);
                v += rng.Range(-cfg.noise, cfg.noise);
                r[i] = Math.Max(1, Math.Min(99, (int)Math.Round(v)));
            }
            Array.Sort(r);
            Array.Reverse(r);
            return r;
        }

        static int Add(AthleteDatabase db, CountryInfo c, Gender g, int rating, DetRandom rng, HashSet<string> used)
        {
            var name = NameGenerator.Generate(c.Style, g, rng, used);
            var a = new Athlete
            {
                id = db.athletes.Count,
                name = name.Full,
                family = name.Family,
                country = c.Code,
                gender = g,
                rating = Math.Max(1, Math.Min(99, rating)),
                shirt = Hex(rng.Pick(ShirtPalette)),
                shorts = Hex(rng.Pick(ShortsPalette)),
                skin = Hex(rng.Pick(c.Skin)),
                hair = Hex(rng.Pick(HairPalette)),
            };
            db.athletes.Add(a);
            return a.id;
        }

        public static int Hex(string h) => Convert.ToInt32(h, 16);
    }
}
