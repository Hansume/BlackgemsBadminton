using System;
using System.Collections.Generic;

namespace Badminton.Tour
{
    public enum Gender : byte { Male, Female }

    /// <summary>5 nội dung thi đấu.</summary>
    public enum TourEvent : byte { MS, WS, MD, WD, XD }

    public static class Events
    {
        public static readonly TourEvent[] All = { TourEvent.MS, TourEvent.WS, TourEvent.MD, TourEvent.WD, TourEvent.XD };
        public static bool IsDoubles(TourEvent e) => e >= TourEvent.MD;

        public static string Name(TourEvent e) => e switch
        {
            TourEvent.MS => "ĐƠN NAM",
            TourEvent.WS => "ĐƠN NỮ",
            TourEvent.MD => "ĐÔI NAM",
            TourEvent.WD => "ĐÔI NỮ",
            _ => "ĐÔI NAM NỮ",
        };

        public static string Code(TourEvent e) => e.ToString();
    }

    /// <summary>Một vận động viên HƯ CẤU.</summary>
    [Serializable]
    public class Athlete
    {
        public int id;
        public string name;      // tên đầy đủ (hư cấu)
        public string family;    // họ – dùng cho tên ngắn trên nhánh đấu
        public string country;   // mã quốc gia (xem Countries)
        public Gender gender;
        public int rating;       // 1..100 (rating gốc ở đầu database; mùa giải lưu rating hiện tại riêng)
        public int shirt;        // màu áo RGB 0xRRGGBB
        public int shorts;       // màu quần
        public int skin;         // màu da
        public int hair;         // màu tóc
    }

    /// <summary>Một suất thi đấu ở một nội dung: đơn = 1 VĐV, đôi = 2 VĐV (b = -1 khi đánh đơn).</summary>
    [Serializable]
    public class Entry
    {
        public int id;
        public TourEvent ev;
        public int a;
        public int b = -1;
        public bool IsDoubles => b >= 0;
    }

    /// <summary>Toàn bộ VĐV + suất thi đấu. id của Athlete/Entry trùng chỉ số trong danh sách.</summary>
    [Serializable]
    public class AthleteDatabase
    {
        public List<Athlete> athletes = new List<Athlete>();
        public List<Entry> entries = new List<Entry>();

        public IEnumerable<Entry> EntriesOf(TourEvent ev)
        {
            foreach (var e in entries) if (e.ev == ev) yield return e;
        }
    }

    /// <summary>
    /// Thế giới thi đấu: database + rating hiện tại (rating AI trôi theo mùa giải).
    /// Mọi engine (giải đấu, mùa giải) chỉ hỏi qua lớp này.
    /// </summary>
    public sealed class TourWorld
    {
        public readonly AthleteDatabase Db;
        public readonly int[] Ratings;

        public TourWorld(AthleteDatabase db, int[] ratings = null)
        {
            Db = db;
            Ratings = new int[db.athletes.Count];
            for (int i = 0; i < Ratings.Length; i++)
                Ratings[i] = ratings != null && i < ratings.Length ? ratings[i] : db.athletes[i].rating;
        }

        public Entry Entry(int id) => Db.entries[id];
        public Athlete Athlete(int id) => Db.athletes[id];

        /// <summary>
        /// Tên hiển thị thay thế cho một số VĐV (id VĐV → tên). Dùng cho VĐV người chơi điều khiển:
        /// hiển thị bằng tên người chơi đặt ở menu chính thay cho tên hư cấu trong database.
        /// </summary>
        public readonly Dictionary<int, string> NameOverride = new Dictionary<int, string>();

        public string AthleteName(int id) => NameOverride.TryGetValue(id, out var n) ? n : Db.athletes[id].name;
        string AthleteFamily(int id) => NameOverride.TryGetValue(id, out var n) ? n : Db.athletes[id].family;

        /// <summary>Quốc tịch thay thế (id VĐV → mã quốc gia) – quốc tịch người chơi chọn ở menu.</summary>
        public readonly Dictionary<int, string> CountryOverride = new Dictionary<int, string>();

        /// <summary>VĐV mà người chơi điều khiển trong suất thi đấu (đôi nam nữ: theo giới tính người chơi).</summary>
        public int PlayerAthlete(int entryId, bool female)
        {
            var e = Db.entries[entryId];
            if (e.b >= 0 && Db.athletes[e.a].gender != Db.athletes[e.b].gender)
                return (Db.athletes[e.b].gender == Gender.Female) == female ? e.b : e.a;
            return e.a;
        }

        /// <summary>Đồng đội AI của người chơi (-1 khi đánh đơn).</summary>
        public int PartnerAthlete(int entryId, bool female)
        {
            var e = Db.entries[entryId];
            if (e.b < 0) return -1;
            return PlayerAthlete(entryId, female) == e.a ? e.b : e.a;
        }

        /// <summary>Gán tên (tên ở menu) và quốc tịch (chọn ở menu) cho VĐV người chơi điều khiển.</summary>
        public void SetPlayer(int entryId, string name, string country, bool female)
        {
            NameOverride.Clear();
            CountryOverride.Clear();
            if (entryId < 0) return;
            int a = PlayerAthlete(entryId, female);
            if (!string.IsNullOrWhiteSpace(name)) NameOverride[a] = name.Trim();
            if (!string.IsNullOrEmpty(country)) CountryOverride[a] = country;
        }

        public void SetPlayerName(int entryId, string name) => SetPlayer(entryId, name, null, false);

        public string AthleteCountry(int id) => CountryOverride.TryGetValue(id, out var c) ? c : Db.athletes[id].country;

        /// <summary>Rating của suất thi đấu: đơn = rating VĐV, đôi = trung bình 2 người.</summary>
        public int Rating(int entryId)
        {
            if (entryId < 0) return 0;
            var e = Db.entries[entryId];
            return e.b < 0 ? Ratings[e.a] : (Ratings[e.a] + Ratings[e.b] + 1) / 2;
        }

        /// <summary>Quốc tịch của suất thi đấu (đôi: theo quốc tịch người chơi nếu người chơi trong cặp).</summary>
        public string Country(int entryId)
        {
            if (entryId < 0) return "";
            var e = Db.entries[entryId];
            if (e.b >= 0 && CountryOverride.ContainsKey(e.b)) return CountryOverride[e.b];
            return AthleteCountry(e.a);
        }

        public string Name(int entryId)
        {
            if (entryId < 0) return "";
            var e = Db.entries[entryId];
            return e.b < 0 ? AthleteName(e.a) : AthleteName(e.a) + " / " + AthleteName(e.b);
        }

        /// <summary>Tên ngắn cho ô nhánh đấu: đơn = tên đầy đủ, đôi = "HỌ1 / HỌ2".</summary>
        public string ShortName(int entryId)
        {
            if (entryId < 0) return "";
            var e = Db.entries[entryId];
            return e.b < 0 ? AthleteName(e.a) : AthleteFamily(e.a) + " / " + AthleteFamily(e.b);
        }

        public List<int> EntryIds(TourEvent ev)
        {
            var l = new List<int>();
            foreach (var e in Db.entries) if (e.ev == ev) l.Add(e.id);
            return l;
        }

        /// <summary>Thứ hạng theo rating (dùng khi chưa có bảng xếp hạng – giải đơn lẻ / tùy chỉnh).</summary>
        public List<int> RankByRating(TourEvent ev)
        {
            var l = EntryIds(ev);
            l.Sort((x, y) => { int c = Rating(y).CompareTo(Rating(x)); return c != 0 ? c : x.CompareTo(y); });
            return l;
        }
    }
}
