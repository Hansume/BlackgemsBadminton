using System;
using System.Collections.Generic;
using Badminton.Core;
using Badminton.Tour;
using UnityEngine;

namespace Badminton.TournamentMode
{
    public enum SessionKind { Season, Single, Custom }

    /// <summary>
    /// Phiên chơi của chế độ Giải đấu (một giải đơn lẻ / giải tùy chỉnh / một mùa giải đang mở).
    /// Lớp nối giữa engine C# thuần (Badminton.Tour) và UI/scene. Mùa giải được lưu JSON vào PlayerPrefs
    /// (WebGL: IndexedDB của trình duyệt) theo slot, tự lưu sau mỗi bước quan trọng (kể cả kết quả đã sinh
    /// nhưng chưa công bố).
    /// </summary>
    public sealed class TournamentSession
    {
        public SessionKind Kind;
        public TournamentSettings Settings;
        public TourBalance B => Settings.balance;
        public AthleteDatabase Db;
        public MatchRulesConfig Rules;
        public TourWorld World;
        public SeasonEngine Season;
        public TourEngine Tour;
        public int Slot = -1;
        public TourEvent Event;
        public int PlayerEntry = -1;

        // Giải đơn lẻ / tùy chỉnh
        public int TierIndex;
        public int CustomSize = 8;
        public bool CustomGroups;

        /// <summary>Lần tải này đã xử thua một trận bị bỏ dở do thoát game giữa trận.</summary>
        public bool ForfeitedOnLoad;

        /// <summary>Đã hiện popup "Bạn đã dừng bước…" cho giải hiện tại chưa.</summary>
        public bool EliminationShown;

        public bool IsSeason => Kind == SessionKind.Season;

        TournamentSession(TournamentSettings s, MatchRulesConfig rules)
        {
            Settings = s;
            Db = s.database.data;
            Rules = rules ?? new MatchRulesConfig();
        }

        // ───────────────────────────── TẠO PHIÊN ─────────────────────────────

        /// <summary>Phiên xem trước khi chọn VĐV cho giải đơn lẻ / tùy chỉnh (thứ hạng theo rating).</summary>
        public static TournamentSession Preview(TournamentSettings s, MatchRulesConfig rules, SessionKind kind, TourEvent ev)
        {
            var ses = new TournamentSession(s, rules) { Kind = kind, Event = ev };
            ses.World = new TourWorld(ses.Db);
            return ses;
        }

        /// <summary>Mùa giải mới: dựng bảng xếp hạng từ mùa trước (mọi VĐV là AI) – người chơi chọn VĐV sau.</summary>
        public static TournamentSession NewSeason(TournamentSettings s, MatchRulesConfig rules, int slot, TourEvent ev)
        {
            var ses = new TournamentSession(s, rules) { Kind = SessionKind.Season, Event = ev, Slot = slot };
            long seed = DateTime.Now.Ticks ^ ((long)slot << 40) ^ 0x5EA5011;
            var save = SeasonEngine.Create(ses.Db, s.balance, seed, ev, -1, "", ses.Rules);
            ses.Season = new SeasonEngine(save, ses.Db, s.balance, ses.Rules);
            ses.World = ses.Season.W;
            return ses;
        }

        public static TournamentSession LoadSeason(TournamentSettings s, MatchRulesConfig rules, int slot)
        {
            string json = PlayerPrefs.GetString(TournamentSaveIndex.DataKey(slot), "");
            if (string.IsNullOrEmpty(json)) return null;
            SeasonSave save;
            try { save = JsonUtility.FromJson<SeasonSave>(json); }
            catch (Exception e) { Debug.LogWarning("[TournamentSave] Hỏng dữ liệu slot " + slot + ": " + e.Message); return null; }
            if (save == null || save.playerEntry < 0) return null;
            var ses = new TournamentSession(s, rules) { Kind = SessionKind.Season, Slot = slot, Event = save.ev, PlayerEntry = save.playerEntry };
            ses.Season = new SeasonEngine(save, ses.Db, s.balance, ses.Rules);
            ses.World = ses.Season.W;
            ses.World.SetPlayer(save.playerEntry, GameSettings.PlayerName, PlayerProfile.Country, PlayerProfile.Female); // tên/nhân vật hiện tại ở menu
            save.playerName = ses.World.Name(save.playerEntry);
            if (save.hasActive) ses.Tour = new TourEngine(save.active, ses.World, s.balance, ses.Rules);
            if (save.matchInProgress >= 0)
            {
                // Thoát game giữa trận ⇒ xử thua trận đó.
                var slotM = ses.Tour?.PlayerSlot();
                if (slotM != null && slotM.id == save.matchInProgress)
                {
                    ses.Tour.SubmitPlayerResult(new MatchResult { winner = 1, retired = true });
                    ses.ForfeitedOnLoad = true;
                }
                save.matchInProgress = -1;
                ses.Save();
            }
            ses.EliminationShown = save.hasActive && ses.Tour.PlayerEliminated() && ses.Tour.PendingReveals().Count == 0;
            return ses;
        }

        /// <summary>
        /// Suất thi đấu tự gán cho người chơi ở nội dung hiện tại (vị trí theo rating – balance.player.startRankFraction).
        /// Người chơi luôn thi đấu bằng tên đặt ở menu chính; đánh đôi thì đồng đội là AI.
        /// </summary>
        public int AutoPlayerEntry()
        {
            var r = World.RankByRating(Event);
            int i = (int)Math.Round(Mathf.Clamp01(B.player.startRankFraction) * (r.Count - 1));
            return r[i];
        }

        /// <summary>Gán suất thi đấu của người chơi.</summary>
        public void ChoosePlayer(int entry)
        {
            PlayerEntry = entry;
            // VĐV của bạn mang tên, quốc tịch, giới tính bạn chọn ở menu chính.
            World.SetPlayer(entry, GameSettings.PlayerName, PlayerProfile.Country, PlayerProfile.Female);
            if (Season != null)
            {
                Season.S.playerEntry = entry;
                Season.S.playerName = World.Name(entry);
                Save();
            }
        }

        /// <summary>Giải đơn lẻ: giải loại trực tiếp 32 theo cấp giải (có vòng loại nếu cấp giải có).</summary>
        public void StartSingle()
        {
            var tier = B.Tier(TierIndex);
            var ranking = World.RankByRating(Event);
            string uid = "SINGLE" + DateTime.Now.Ticks;
            long seed = DateTime.Now.Ticks;
            var rng = new DetRandom(DetRandom.Mix((ulong)seed, DetRandom.Hash(uid), 3));
            int rank = ranking.IndexOf(PlayerEntry) + 1, n = ranking.Count;
            // Không đủ hạng ⇒ vẫn được dự (suất đặc cách): vào vòng loại nếu có, không thì vào thẳng.
            var el = rank <= Math.Ceiling(tier.directRankFraction * n) || !tier.hasQualifying ? Eligibility.Direct : Eligibility.Qualifying;
            var (direct, qual) = SeasonEngine.BuildEntries(ranking, tier, B.mainDraw, PlayerEntry, true, el, rng);
            string name = PickSingleName(rng);
            var st = TourEngine.CreateKnockout(B, World, uid, name, TierIndex, Event, seed, direct, qual,
                                               qual.Count > 0 ? tier.qualifiers : 0, PlayerEntry);
            Tour = new TourEngine(st, World, B, Rules);
            EliminationShown = false;
        }

        string PickSingleName(DetRandom rng)
        {
            var cal = B.season.calendar;
            var same = new List<string>();
            foreach (var c in cal) if (c.tier == TierIndex) same.Add(c.name);
            if (same.Count == 0) foreach (var c in cal) same.Add(c.name);
            return same.Count > 0 ? same[rng.Next(same.Count)] : "OPEN";
        }

        /// <summary>Giải tùy chỉnh: 4/8/16 VĐV có thứ hạng gần người chơi, loại trực tiếp hoặc vòng bảng.</summary>
        public void StartCustom()
        {
            var ranking = World.RankByRating(Event);
            int pIdx = ranking.IndexOf(PlayerEntry);
            int n = CustomSize;
            // Lấy n người quanh vị trí người chơi: strongerFraction × (n-1) người xếp trên, còn lại xếp dưới.
            int above = (int)Math.Round(B.custom.strongerFraction * (n - 1));
            int start = Math.Max(0, Math.Min(ranking.Count - n, pIdx - above));
            var field = ranking.GetRange(start, n);
            if (!field.Contains(PlayerEntry)) { field[field.Count - 1] = PlayerEntry; }
            field.Sort((x, y) => ranking.IndexOf(x).CompareTo(ranking.IndexOf(y)));
            string uid = "CUSTOM" + DateTime.Now.Ticks;
            long seed = DateTime.Now.Ticks;
            TourState st = CustomGroups
                ? TourEngine.CreateGroups(B, World, uid, B.custom.name, -1, false, Event, seed, field, PlayerEntry)
                : TourEngine.CreateKnockout(B, World, uid, B.custom.name, -1, Event, seed, field, null, 0, PlayerEntry);
            Tour = new TourEngine(st, World, B, Rules);
            EliminationShown = false;
        }

        /// <summary>Ghi dấu "đang đánh trận" vào file lưu (gọi ngay trước khi vào trận 3D).</summary>
        public void MarkMatchStarted(int slotId)
        {
            if (Season == null) return;
            Season.S.matchInProgress = slotId;
            Save();
        }

        public void ClearMatchStarted()
        {
            if (Season != null) Season.S.matchInProgress = -1;
        }

        /// <summary>Mùa giải: đăng ký một giải của tuần hiện tại (calIdx = chỉ số trong lịch, -1 = giải đầu tiên).</summary>
        public void EnterSeasonTournament(int calIdx = -1)
        {
            var st = Season.EnterCurrent(calIdx);
            Tour = new TourEngine(st, World, B, Rules);
            EliminationShown = false;
            Save();
        }

        /// <summary>Mùa giải: kết thúc tuần (sau khi giải của người chơi xong, hoặc người chơi nghỉ).</summary>
        public void FinishSeasonWeek()
        {
            Season.FinishWeek();
            Tour = null;
            Save();
        }

        // ───────────────────────────── LƯU ─────────────────────────────

        public void Save()
        {
            if (Season == null || Slot < 0 || Season.S.playerEntry < 0) return;
            try
            {
                PlayerPrefs.SetString(TournamentSaveIndex.DataKey(Slot), JsonUtility.ToJson(Season.S));
                var cur = Season.Current;
                TournamentSaveIndex.WriteHeader(Slot, new SaveSlotHeader
                {
                    eventName = Events.Name(Season.S.ev),
                    athleteName = World.ShortName(Season.S.playerEntry),
                    seasonNo = Season.S.seasonNo,
                    week = cur != null ? cur.week : 52,
                }); // WriteHeader gọi PlayerPrefs.Save()
            }
            catch (Exception e) { Debug.LogWarning("[TournamentSave] Không lưu được: " + e.Message); }
        }

        // ───────────────────────────── TIỆN ÍCH HIỂN THỊ ─────────────────────────────

        public string TierName(TourState t) => t.finals ? B.finals.name : t.tier >= 0 ? B.Tier(t.tier).name : "TÙY CHỈNH";

        public Color TierColor(TourState t)
        {
            string hex = t.finals ? B.finals.color : t.tier >= 0 ? B.Tier(t.tier).color : "AAB9D2";
            return Hex(hex);
        }

        public static Color Hex(string hex) => ColorUtility.TryParseHtmlString("#" + hex, out var c) ? c : Color.white;
        public static Color Rgb(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);

        /// <summary>Thứ hạng hiện tại của một suất (mùa giải: bảng xếp hạng thế giới; còn lại: theo rating).</summary>
        public int RankOf(int entry)
        {
            if (Season != null) return Season.RankOf(entry);
            return World.RankByRating(World.Entry(entry).ev).IndexOf(entry) + 1;
        }

        public List<int> Ranking(TourEvent ev) => Season != null ? Season.Ranking(ev) : World.RankByRating(ev);

        public int PointsOf(int entry) => Season != null ? Season.PointsOf(entry) : 0;

        public string DifficultyName(int entry) => DifficultyMapConfig.Names[B.difficulty.Tier(World.Rating(entry))];
    }
}
