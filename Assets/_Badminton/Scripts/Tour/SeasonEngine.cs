using System;
using System.Collections.Generic;
using Badminton.Core;

namespace Badminton.Tour
{
    [Serializable]
    public class CalendarItem
    {
        public int week;          // tuần trong năm 1..52
        public string name;
        public int tier;          // chỉ số cấp giải; giải chung kết: -1
        public bool finals;
        public bool done;
        public bool playerPlayed;
        public string playerResult; // "VÔ ĐỊCH", "TỨ KẾT"… (nếu người chơi dự)
    }

    /// <summary>Điểm xếp hạng đã nhận (mảng song song cho gọn file lưu).</summary>
    [Serializable]
    public class RankRecords
    {
        public List<int> ev = new List<int>(), entry = new List<int>(), week = new List<int>(), points = new List<int>();

        public int Count => entry.Count;

        public void Add(TourEvent e, int en, int w, int p) { ev.Add((int)e); entry.Add(en); week.Add(w); points.Add(p); }

        /// <summary>Xoá các bản ghi đã ra khỏi cửa sổ trượt.</summary>
        public void Prune(int minWeekExclusive)
        {
            for (int i = Count - 1; i >= 0; i--)
                if (week[i] <= minWeekExclusive) { ev.RemoveAt(i); entry.RemoveAt(i); week.RemoveAt(i); points.RemoveAt(i); }
        }
    }

    /// <summary>Tóm tắt một giải (cho mục "Kết quả tuần này").</summary>
    [Serializable]
    public class Digest
    {
        public int week;
        public string name;
        public int tier;
        public bool finals;
        public TourEvent ev;
        public int champion = -1, runnerUp = -1;
        public int[] semis = new int[0];
        public string finalScore;
        public bool playerPlayed;
    }

    [Serializable]
    public class PlayerMatchRecord
    {
        public int season, week;
        public string tour, round;
        public int opponent;
        public bool won;
        public string score;   // theo góc nhìn người chơi
    }

    /// <summary>Thành tích nổi bật (vô địch / á quân / bán kết) của MỌI VĐV – cho hồ sơ VĐV AI.</summary>
    [Serializable]
    public class ResultRecord
    {
        public int season, week, entry, depth, tier;
        public bool finals;
        public string tour;
    }

    [Serializable]
    public class Achievement
    {
        public int season, week;
        public string tour;
        public int tier;
        public bool finals;
        public string placement;
        public int points;
    }

    /// <summary>Toàn bộ dữ liệu một mùa giải (một slot lưu).</summary>
    [Serializable]
    public class SeasonSave
    {
        public int version = 1;
        public long seed;
        public int seasonNo = 1;
        public TourEvent ev;
        public int playerEntry = -1;
        public string playerName;

        public int[] ratings = new int[0];
        public List<CalendarItem> calendar = new List<CalendarItem>();
        public int calIndex;

        public RankRecords ranking = new RankRecords();
        public List<Digest> lastWeek = new List<Digest>();      // kết quả tuần vừa qua (hiện khi sang tuần mới)
        public int lastWeekNo;

        public bool hasActive;
        public TourState active = new TourState();
        /// <summary>
        /// id trận của người chơi đang đánh dở (-1 = không). Ghi lại TRƯỚC khi vào trận 3D; nếu game bị đóng /
        /// tải lại giữa trận thì lần tải sau trận đó bị xử thua (không thể né thua bằng cách thoát game).
        /// </summary>
        public int matchInProgress = -1;
        /// <summary>Chỉ số (trong calendar) của giải người chơi đang dự.</summary>
        public int activeItem = -1;

        public List<PlayerMatchRecord> matches = new List<PlayerMatchRecord>();
        public List<Achievement> achievements = new List<Achievement>();
        /// <summary>Thành tích bán kết trở lên của mọi suất (giữ mùa hiện tại + mùa trước).</summary>
        public List<ResultRecord> results = new List<ResultRecord>();
        public int rankBefore, rankAfter, pointsGained;
    }

    public enum Eligibility { Direct, Qualifying, No }

    /// <summary>
    /// Engine mùa giải (C# thuần): lịch theo tuần, danh sách tham dự (AI có xác suất rút lui/bỏ giải cấp thấp),
    /// sinh kết quả tức thì các giải người chơi không dự, bảng xếp hạng thế giới = tổng N kết quả tốt nhất trong
    /// 52 tuần gần nhất (cửa sổ trượt), trôi rating AI ±1–3 sau mỗi giải, giải chung kết cuối mùa (8 người).
    /// </summary>
    public sealed class SeasonEngine
    {
        public readonly SeasonSave S;
        public readonly TourWorld W;
        public readonly TourBalance B;
        public readonly AthleteDatabase Db;
        public readonly MatchRulesConfig Rules;
        int _bootstrapPlayer = -1;

        public SeasonEngine(SeasonSave s, AthleteDatabase db, TourBalance b, MatchRulesConfig rules = null)
        {
            S = s; Db = db; B = b; Rules = rules ?? new MatchRulesConfig();
            if (S.ratings == null || S.ratings.Length != db.athletes.Count)
            {
                S.ratings = new int[db.athletes.Count];
                for (int i = 0; i < S.ratings.Length; i++) S.ratings[i] = db.athletes[i].rating;
            }
            W = new TourWorld(db, S.ratings);
            // TourWorld giữ mảng riêng ⇒ đồng bộ ngược khi rating đổi (xem ApplyDrift).
        }

        // ───────────────────────────── TẠO MÙA GIẢI ─────────────────────────────

        public static SeasonSave Create(AthleteDatabase db, TourBalance b, long seed, TourEvent ev, int playerEntry, string playerName,
                                        MatchRulesConfig rules = null)
        {
            var s = new SeasonSave { seed = seed, ev = ev, playerEntry = playerEntry, playerName = playerName };
            var eng = new SeasonEngine(s, db, b, rules);
            // Mùa "số 0" (52 tuần trước) chỉ để có bảng xếp hạng ban đầu hợp lý: mọi giải là AI, sinh tức thì.
            // VĐV người chọn cũng thi đấu như AI ở mùa này (để có thứ hạng thật), rating không bị trôi.
            eng.S.seasonNo = 0;
            eng.S.calendar = BuildCalendar(b);
            eng._bootstrapPlayer = playerEntry;
            eng.S.playerEntry = -1;
            for (int i = 0; i < eng.S.calendar.Count; i = eng.WeekEnd(i)) eng.RunAiWeek(i, false);
            eng.S.playerEntry = playerEntry;
            eng._bootstrapPlayer = -1;
            eng.S.seasonNo = 1;
            eng.S.calendar = BuildCalendar(b);
            eng.S.calIndex = 0;
            eng.S.lastWeek.Clear();
            return s;
        }

        public static List<CalendarItem> BuildCalendar(TourBalance b)
        {
            var list = new List<CalendarItem>();
            foreach (var c in b.season.calendar)
                list.Add(new CalendarItem { week = c.week, name = c.name, tier = c.tier });
            list.Sort((x, y) => x.week != y.week ? x.week.CompareTo(y.week) : x.tier != y.tier ? x.tier.CompareTo(y.tier) : string.CompareOrdinal(x.name, y.name));
            list.Add(new CalendarItem { week = b.season.finalsWeek, name = b.finals.name, tier = -1, finals = true });
            return list;
        }

        /// <summary>Sang mùa mới: rating AI kéo nhẹ về rating gốc, lịch mới. Bảng xếp hạng giữ nguyên (cửa sổ trượt).</summary>
        public void StartNextSeason()
        {
            float k = B.season.newSeasonRegression;
            var playerAthletes = PlayerAthletes();
            for (int i = 0; i < S.ratings.Length; i++)
            {
                if (playerAthletes.Contains(i)) continue;
                S.ratings[i] = (int)Math.Round(S.ratings[i] + (Db.athletes[i].rating - S.ratings[i]) * k);
                W.Ratings[i] = S.ratings[i];
            }
            S.seasonNo++;
            S.calendar = BuildCalendar(B);
            S.calIndex = 0;
            S.results.RemoveAll(r => r.season < S.seasonNo - 1); // giữ mùa vừa xong + mùa mới
        }

        /// <summary>Thành tích bán kết trở lên của một suất trong một mùa.</summary>
        public List<ResultRecord> ResultsOf(int entry, int season)
        {
            var l = S.results.FindAll(r => r.entry == entry && r.season == season);
            l.Sort((a, b) => b.week.CompareTo(a.week));
            return l;
        }

        public static string DepthText(int depth) => depth == 0 ? "VÔ ĐỊCH" : depth == 1 ? "Á QUÂN" : depth == 2 ? "BÁN KẾT" : "TỨ KẾT";

        // ───────────────────────────── THỜI GIAN ─────────────────────────────

        public static int Abs(int season, int week) => (season - 1) * 52 + week;
        public bool SeasonOver => S.calIndex >= S.calendar.Count;
        /// <summary>Tuần của giải này có nhiều giải song song không (khi đó các giải không có vòng loại).</summary>
        public bool IsParallel(CalendarItem item)
        {
            int n = 0;
            foreach (var c in S.calendar) if (c.week == item.week) n++;
            return n > 1;
        }

        /// <summary>Giải đầu tiên (cấp cao nhất) của tuần hiện tại.</summary>
        public CalendarItem Current => SeasonOver ? null : S.calendar[S.calIndex];

        /// <summary>Chỉ số sau giải cuối cùng của tuần bắt đầu ở `start`.</summary>
        public int WeekEnd(int start)
        {
            int i = start;
            while (i < S.calendar.Count && S.calendar[i].week == S.calendar[start].week) i++;
            return i;
        }

        /// <summary>Chỉ số (trong calendar) các giải diễn ra ở tuần hiện tại (cấp cao trước).</summary>
        public List<int> CurrentWeekIndices()
        {
            var l = new List<int>();
            if (SeasonOver) return l;
            for (int i = S.calIndex; i < WeekEnd(S.calIndex); i++) l.Add(i);
            return l;
        }
        public int CurrentAbsWeek => Abs(S.seasonNo, SeasonOver ? 52 : Current.week);

        // ───────────────────────────── BẢNG XẾP HẠNG ─────────────────────────────

        /// <summary>Điểm xếp hạng của mọi suất ở nội dung ev tại tuần tuyệt đối atWeek.</summary>
        public Dictionary<int, int> PointsTable(TourEvent ev, int atWeek)
        {
            int n = B.season.bestResults, from = atWeek - B.season.windowWeeks;
            var lists = new Dictionary<int, List<int>>();
            var R = S.ranking;
            for (int i = 0; i < R.Count; i++)
            {
                if (R.ev[i] != (int)ev || R.week[i] <= from || R.week[i] > atWeek) continue;
                if (!lists.TryGetValue(R.entry[i], out var l)) lists[R.entry[i]] = l = new List<int>();
                l.Add(R.points[i]);
            }
            var result = new Dictionary<int, int>();
            foreach (var kv in lists)
            {
                kv.Value.Sort((a, b) => b.CompareTo(a));
                int sum = 0;
                for (int i = 0; i < kv.Value.Count && i < n; i++) sum += kv.Value[i];
                result[kv.Key] = sum;
            }
            return result;
        }

        /// <summary>Danh sách suất theo thứ hạng (điểm giảm dần; bằng điểm ⇒ rating cao hơn xếp trên).</summary>
        public List<int> Ranking(TourEvent ev, int atWeek)
        {
            var pts = PointsTable(ev, atWeek);
            var l = W.EntryIds(ev);
            l.Sort((x, y) =>
            {
                pts.TryGetValue(x, out int px); pts.TryGetValue(y, out int py);
                int c = py.CompareTo(px);
                if (c != 0) return c;
                c = W.Rating(y).CompareTo(W.Rating(x));
                return c != 0 ? c : x.CompareTo(y);
            });
            return l;
        }

        public List<int> Ranking(TourEvent ev) => Ranking(ev, CurrentAbsWeek);

        public int RankOf(int entry)
        {
            if (entry < 0) return 0;
            var e = W.Entry(entry);
            return Ranking(e.ev).IndexOf(entry) + 1;
        }

        public int PointsOf(int entry)
        {
            if (entry < 0) return 0;
            var e = W.Entry(entry);
            PointsTable(e.ev, CurrentAbsWeek).TryGetValue(entry, out int p);
            return p;
        }

        // ───────────────────────────── ĐIỀU KIỆN & DANH SÁCH THAM DỰ ─────────────────────────────

        public Eligibility PlayerEligibility(CalendarItem item)
        {
            var ranking = Ranking(S.ev);
            int rank = ranking.IndexOf(S.playerEntry) + 1;
            if (item.finals) return rank > 0 && rank <= B.finals.field ? Eligibility.Direct : Eligibility.No;
            var t = B.Tier(item.tier);
            int n = ranking.Count;
            if (rank <= Math.Ceiling(t.directRankFraction * n)) return Eligibility.Direct;
            if (t.hasQualifying && rank <= Math.Ceiling(t.qualifyingRankFraction * n))
                return IsParallel(item) ? Eligibility.Direct : Eligibility.Qualifying; // tuần song song: không có vòng loại
            return Eligibility.No;
        }

        /// <summary>
        /// Danh sách suất vào thẳng + suất vòng loại cho một giải. AI có thể rút lui/bỏ giải (xác suất theo cấp giải và
        /// thứ hạng). includePlayer: người chơi đăng ký (đã đủ điều kiện) – không bao giờ bị loại khỏi danh sách.
        /// </summary>
        public (List<int> direct, List<int> qual) BuildEntries(CalendarItem item, TourEvent ev, bool includePlayer, DetRandom rng,
                                                               HashSet<int> exclude = null)
        {
            int player = ev == S.ev ? S.playerEntry : -1;
            var el = includePlayer && player >= 0 ? PlayerEligibility(item) : Eligibility.No;
            var ranking = Ranking(ev);
            if (exclude != null && exclude.Count > 0) ranking = ranking.FindAll(e => !exclude.Contains(e));
            // Giải song song cấp thấp: không đủ người cho nhánh 32 ⇒ thu nhỏ nhánh (16, 8…).
            int avail = ranking.Count - (player >= 0 && !includePlayer && ranking.Contains(player) ? 1 : 0);
            int draw = B.mainDraw;
            while (draw > 4 && draw > avail) draw /= 2;
            var tier = B.Tier(item.tier);
            if (IsParallel(item) || draw < B.mainDraw) tier = tier.WithoutQualifying(); // chia người cho các giải cùng tuần
            return BuildEntries(ranking, tier, draw, player, includePlayer, el, rng);
        }

        /// <summary>
        /// Danh sách tham dự từ một bảng xếp hạng bất kỳ (dùng chung cho mùa giải và giải đơn lẻ).
        /// el = Direct ⇒ người chơi chắc chắn vào nhánh chính; Qualifying ⇒ chắc chắn có suất vòng loại.
        /// </summary>
        public static (List<int> direct, List<int> qual) BuildEntries(List<int> ranking, TierDef t, int mainDraw, int player,
                                                                      bool includePlayer, Eligibility el, DetRandom rng)
        {
            int qualifiers = t.hasQualifying ? t.qualifiers : 0;
            int directCount = mainDraw - qualifiers;
            int qualCount = t.hasQualifying ? t.qualifyingDraw : 0;

            var accepted = new List<int>();
            for (int i = 0; i < ranking.Count; i++)
            {
                int e = ranking[i];
                if (e == player) { if (includePlayer) accepted.Add(e); continue; }
                double p = i < 10 ? t.playTop10 : i < 32 ? t.playTop32 : t.playRest;
                if (rng.Chance(p)) accepted.Add(e);
            }
            // Thiếu người ⇒ bổ sung theo thứ hạng (người dự bị).
            for (int i = 0; i < ranking.Count && accepted.Count < directCount + qualCount; i++)
            {
                int e = ranking[i];
                if (e == player || accepted.Contains(e)) continue;
                accepted.Add(e);
            }
            accepted.Sort((x, y) => ranking.IndexOf(x).CompareTo(ranking.IndexOf(y)));

            if (includePlayer && player >= 0)
            {
                int idx = accepted.IndexOf(player);
                int limit = el == Eligibility.Direct || qualCount == 0 ? directCount : directCount + qualCount;
                limit = Math.Max(1, Math.Min(limit, accepted.Count));
                if (idx >= limit) { accepted.RemoveAt(idx); accepted.Insert(limit - 1, player); }
            }

            var direct = accepted.GetRange(0, Math.Min(directCount, accepted.Count));
            var qual = qualCount > 0 && accepted.Count > directCount
                ? accepted.GetRange(directCount, Math.Min(qualCount, accepted.Count - directCount))
                : new List<int>();
            if (qual.Count != qualCount) qual.Clear(); // không đủ người đánh vòng loại ⇒ bỏ vòng loại
            if (qual.Count == 0 && qualifiers > 0)
            {
                // Không có vòng loại ⇒ lấp suất Q bằng người kế tiếp.
                for (int i = directCount; i < accepted.Count && direct.Count < mainDraw; i++) direct.Add(accepted[i]);
            }
            if (qual.Count > 0 && direct.Count != directCount)
            {
                // Không đủ người vào thẳng ⇒ bỏ vòng loại, dồn người đánh vòng loại vào nhánh chính.
                direct.AddRange(qual);
                qual.Clear();
            }
            if (qual.Count == 0)
            {
                // Nhánh phải là lũy thừa của 2: bớt người xếp hạng thấp nhất (không bao giờ bớt người chơi).
                int p2 = 1;
                while (p2 * 2 <= direct.Count) p2 *= 2;
                for (int i = direct.Count - 1; i >= 0 && direct.Count > p2; i--)
                    if (direct[i] != player) direct.RemoveAt(i);
            }
            return (direct, qual);
        }

        string Uid(CalendarItem item, TourEvent ev) => $"S{S.seasonNo}W{item.week}{ev}{(item.tier >= 0 ? item.tier : 9)}";

        /// <summary>Tạo giải cho một nội dung của tuần (người chơi tham dự nếu includePlayer).</summary>
        public TourState CreateTournament(CalendarItem item, TourEvent ev, bool includePlayer, HashSet<int> exclude = null)
        {
            string uid = Uid(item, ev);
            var rng = new DetRandom(DetRandom.Mix((ulong)S.seed, DetRandom.Hash(uid), 7));
            int player = includePlayer && ev == S.ev ? S.playerEntry : -1;
            TourState t;
            if (item.finals)
            {
                var ranking = Ranking(ev);
                var field = ranking.GetRange(0, Math.Min(B.finals.field, ranking.Count));
                if (player >= 0 && !field.Contains(player)) player = -1;
                t = TourEngine.CreateGroups(B, W, uid, item.name, -1, true, ev, S.seed, field, player);
            }
            else
            {
                var (direct, qual) = BuildEntries(item, ev, player >= 0, rng, exclude);
                var tier = B.Tier(item.tier);
                t = TourEngine.CreateKnockout(B, W, uid, item.name, item.tier, ev, S.seed, direct, qual,
                                              qual.Count > 0 ? tier.qualifiers : 0, player);
            }
            t.week = Abs(S.seasonNo, item.week);
            return t;
        }

        // ───────────────────────────── TUẦN THI ĐẤU ─────────────────────────────

        /// <summary>
        /// Dựng mọi giải của tuần (bắt đầu ở calendar[start]) cho một nội dung. Giải cấp cao chọn người trước;
        /// một suất không dự 2 giải cùng tuần. playerItem: chỉ số giải người chơi dự (-1 = không dự / nội dung khác).
        /// Tất định ⇒ dựng lại lúc kết thúc tuần cho đúng danh sách như lúc người chơi đăng ký.
        /// </summary>
        List<(int idx, TourState state)> BuildWeek(int start, TourEvent ev, int playerItem)
        {
            var used = new HashSet<int>();
            if (playerItem >= 0 && S.playerEntry >= 0) used.Add(S.playerEntry); // người chơi chỉ dự giải đã chọn
            var list = new List<(int, TourState)>();
            for (int i = start; i < WeekEnd(start); i++)
            {
                bool mine = i == playerItem;
                if (mine) used.Remove(S.playerEntry);
                var st = CreateTournament(S.calendar[i], ev, mine, used);
                foreach (int e in st.entrants) used.Add(e);
                list.Add((i, st));
            }
            return list;
        }

        /// <summary>Người chơi đăng ký một giải của tuần này (calIdx = chỉ số trong lịch; -1 = giải đầu tiên).</summary>
        public TourState EnterCurrent(int calIdx = -1)
        {
            if (SeasonOver) return null;
            if (calIdx < 0) calIdx = S.calIndex;
            foreach (var (idx, st) in BuildWeek(S.calIndex, S.ev, calIdx))
                if (idx == calIdx) S.active = st;
            S.activeItem = calIdx;
            S.hasActive = true;
            S.rankBefore = RankOf(S.playerEntry);
            return S.active;
        }

        /// <summary>
        /// Kết thúc tuần: hoàn tất giải người chơi đang dự (nếu có), sinh kết quả tức thì mọi giải còn lại,
        /// cộng điểm xếp hạng, trôi rating AI, lưu tóm tắt "Kết quả tuần này", sang tuần kế tiếp.
        /// </summary>
        public void FinishWeek()
        {
            var item = Current;
            if (item == null) return;
            int week = Abs(S.seasonNo, item.week);
            int start = S.calIndex, end = WeekEnd(start);
            int playerItem = S.hasActive ? (S.activeItem >= 0 ? S.activeItem : start) : -1;
            if (!S.hasActive) S.rankBefore = RankOf(S.playerEntry);
            S.lastWeek.Clear();
            S.lastWeekNo = item.week;
            S.pointsGained = 0;

            foreach (var ev in Events.All)
            {
                bool mineEv = S.hasActive && ev == S.ev;
                foreach (var (idx, st) in BuildWeek(start, ev, mineEv ? playerItem : -1))
                {
                    TourEngine eng;
                    if (mineEv && idx == playerItem)
                    {
                        eng = new TourEngine(S.active, W, B, Rules);
                        eng.ResolveToEnd();
                        RecordPlayer(eng, S.calendar[idx]);
                    }
                    else
                    {
                        eng = new TourEngine(st, W, B, Rules);
                        eng.ResolveToEnd();
                    }
                    Record(eng, week, true);
                }
            }
            for (int i = start; i < end; i++) S.calendar[i].done = true;
            S.hasActive = false;
            S.active = new TourState();
            S.activeItem = -1;
            S.calIndex = end;
            S.ranking.Prune(CurrentAbsWeek - B.season.windowWeeks - 1);
            S.rankAfter = RankOf(S.playerEntry);
        }

        /// <summary>Tuần của mùa "số 0" (dựng bảng xếp hạng ban đầu).</summary>
        void RunAiWeek(int start, bool digest)
        {
            int week = Abs(S.seasonNo, S.calendar[start].week);
            foreach (var ev in Events.All)
                foreach (var (_, st) in BuildWeek(start, ev, -1))
                {
                    var eng = new TourEngine(st, W, B, Rules);
                    eng.ResolveToEnd();
                    Record(eng, week, digest);
                }
        }

        void Record(TourEngine eng, int week, bool digest)
        {
            var t = eng.S;
            foreach (int e in t.entrants)
            {
                int p = eng.Points(e);
                if (p > 0) S.ranking.Add(t.ev, e, week, p);
                if (e != S.playerEntry && e != _bootstrapPlayer) ApplyDrift(eng, e);
                int depth = eng.Depth(e);
                if (digest && depth >= 0 && depth <= 2)
                    S.results.Add(new ResultRecord { season = S.seasonNo, week = week - Abs(S.seasonNo, 0), entry = e, depth = depth,
                                                     tier = t.tier, finals = t.finals, tour = t.name });
            }
            if (!digest) return;
            var d = new Digest { week = week, name = t.name, tier = t.tier, finals = t.finals, ev = t.ev, champion = t.champion,
                                 playerPlayed = t.playerEntry >= 0 };
            var semis = new List<int>();
            foreach (var m in t.slots)
            {
                var r = t.rounds[m.round];
                if (r.stage == TourStage.Qualifying || r.stage == TourStage.Group) continue;
                if (r.size == 2) { d.runnerUp = m.Loser; d.finalScore = m.result.ScoreText(m.result.winner == 0); }
                if (r.size == 4) semis.Add(m.Loser);
            }
            d.semis = semis.ToArray();
            S.lastWeek.Add(d);
        }

        /// <summary>Rating AI thay đổi nhẹ theo thành tích so với kỳ vọng (±ratingDriftMax).</summary>
        void ApplyDrift(TourEngine eng, int entry)
        {
            var (wins, exp) = eng.Performance(entry);
            int delta = (int)Math.Round((wins - exp) * B.season.ratingDriftGain);
            delta = Math.Max(-B.season.ratingDriftMax, Math.Min(B.season.ratingDriftMax, delta));
            if (delta == 0) return;
            var e = W.Entry(entry);
            Bump(e.a, delta);
            if (e.b >= 0) Bump(e.b, delta);
        }

        void Bump(int athlete, int delta)
        {
            int v = Math.Max(B.season.ratingMin, Math.Min(B.season.ratingMax, S.ratings[athlete] + delta));
            S.ratings[athlete] = v;
            W.Ratings[athlete] = v;
        }

        HashSet<int> PlayerAthletes()
        {
            var h = new HashSet<int>();
            if (S.playerEntry >= 0) { var e = W.Entry(S.playerEntry); h.Add(e.a); if (e.b >= 0) h.Add(e.b); }
            return h;
        }

        void RecordPlayer(TourEngine eng, CalendarItem item)
        {
            var t = eng.S;
            int p = S.playerEntry;
            foreach (var m in t.slots)
            {
                if (!m.hasResult || !m.Has(p)) continue;
                bool isA = m.a == p;
                S.matches.Add(new PlayerMatchRecord
                {
                    season = S.seasonNo, week = item.week, tour = t.name, round = t.rounds[m.round].name,
                    opponent = isA ? m.b : m.a, won = m.Winner == p, score = m.result.ScoreText(isA),
                });
            }
            int pts = eng.Points(p);
            S.pointsGained = pts;
            item.playerPlayed = true;
            item.playerResult = eng.PlacementText(p);
            S.achievements.Add(new Achievement
            {
                season = S.seasonNo, week = item.week, tour = t.name, tier = t.tier, finals = t.finals,
                placement = item.playerResult, points = pts,
            });
        }

        /// <summary>Đối đầu giữa người chơi và đối thủ (thắng, thua) qua các trận đã lưu + giải đang đánh.</summary>
        public (int won, int lost) HeadToHead(int opponent)
        {
            int w = 0, l = 0;
            foreach (var r in S.matches) if (r.opponent == opponent) { if (r.won) w++; else l++; }
            if (S.hasActive)
                foreach (var m in S.active.slots)
                    if (m.hasResult && m.Has(S.playerEntry) && m.Has(opponent)) { if (m.Winner == S.playerEntry) w++; else l++; }
            return (w, l);
        }
    }
}
