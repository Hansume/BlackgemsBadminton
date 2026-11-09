using System.Collections.Generic;
using System.Linq;
using Badminton.Core;
using Badminton.Tour;
using NUnit.Framework;
using UnityEngine;

namespace Badminton.Tests
{
    /// <summary>GĐ2–3: database VĐV hư cấu, bộ sinh tỷ số, bốc thăm, engine giải đấu, mùa giải.</summary>
    public class TourEngineTests
    {
        static readonly TourBalance B = new TourBalance();
        static AthleteDatabase _db;
        static AthleteDatabase Db => _db ??= DatabaseGenerator.Generate(B.database);
        static readonly MatchRulesConfig R = new MatchRulesConfig();

        // ───────────── Database ─────────────

        [Test]
        public void Database_HasExpectedCounts_AndValidData()
        {
            var w = new TourWorld(Db);
            Assert.AreEqual(64, w.EntryIds(TourEvent.MS).Count);
            Assert.AreEqual(64, w.EntryIds(TourEvent.WS).Count);
            Assert.AreEqual(48, w.EntryIds(TourEvent.MD).Count);
            Assert.AreEqual(48, w.EntryIds(TourEvent.WD).Count);
            Assert.AreEqual(48, w.EntryIds(TourEvent.XD).Count);
            var names = new HashSet<string>();
            foreach (var a in Db.athletes)
            {
                Assert.IsTrue(a.rating >= 1 && a.rating <= 100, a.name);
                Assert.IsFalse(string.IsNullOrWhiteSpace(a.name));
                Assert.IsTrue(names.Add(a.name), "Trùng tên: " + a.name);
                Assert.IsNotNull(Countries.Get(a.country));
            }
            foreach (var e in Db.entries)
            {
                var A = Db.athletes[e.a];
                if (e.ev == TourEvent.MS || e.ev == TourEvent.MD) Assert.AreEqual(Gender.Male, A.gender);
                if (e.ev == TourEvent.WS || e.ev == TourEvent.WD) Assert.AreEqual(Gender.Female, A.gender);
                if (e.ev == TourEvent.XD) Assert.AreNotEqual(Db.athletes[e.a].gender, Db.athletes[e.b].gender);
                if (e.b >= 0) Assert.AreEqual(A.country, Db.athletes[e.b].country, "Cặp đôi cùng quốc gia");
            }
        }

        [Test]
        public void Database_IsDeterministic_AndUsesNoBlacklistedNames()
        {
            var again = DatabaseGenerator.Generate(B.database);
            Assert.AreEqual(Db.athletes.Count, again.athletes.Count);
            for (int i = 0; i < Db.athletes.Count; i++) Assert.AreEqual(Db.athletes[i].name, again.athletes[i].name);
            Assert.IsTrue(NameGenerator.IsBlacklisted("Chong Wei", "Lee"), "Danh sách chặn phải nhận ra tên thật");
            foreach (var a in Db.athletes)
            {
                var parts = a.name.Split(' ');
                Assert.IsFalse(NameGenerator.IsBlacklisted(string.Join("", parts.Skip(1)), parts[0]), a.name);
            }
        }

        [Test]
        public void Database_RatingsSpanAllDifficultyTiers()
        {
            var w = new TourWorld(Db);
            var tiers = new int[4];
            foreach (int e in w.EntryIds(TourEvent.MS)) tiers[B.difficulty.Tier(w.Rating(e))]++;
            for (int t = 0; t < 4; t++) Assert.Greater(tiers[t], 0, "Thiếu bậc " + DifficultyMapConfig.Names[t]);
            Assert.AreEqual(0, B.difficulty.Tier(39));
            Assert.AreEqual(1, B.difficulty.Tier(40));
            Assert.AreEqual(2, B.difficulty.Tier(65));
            Assert.AreEqual(3, B.difficulty.Tier(85));
        }

        // ───────────── Bộ sinh tỷ số ─────────────

        [Test]
        public void Generated_Scores_AreAlwaysValid()
        {
            var rng = new DetRandom(12345UL);
            for (int i = 0; i < 100000; i++)
            {
                int ra = rng.Range(1, 100), rb = rng.Range(1, 100);
                var r = MatchResultGenerator.Generate(ra, rb, rng, B.results, R);
                Assert.IsTrue(MatchResultGenerator.IsValid(r, R), $"#{i} {ra} vs {rb}: {r.ScoreText()}");
            }
        }

        [Test]
        public void Validator_RejectsBadMatches()
        {
            Assert.IsFalse(MatchResultGenerator.IsValid(new MatchResult { winner = 0, a = new[] { 21, 21 }, b = new[] { 20, 15 } }));
            Assert.IsFalse(MatchResultGenerator.IsValid(new MatchResult { winner = 0, a = new[] { 21, 15, 21 }, b = new[] { 10, 21, 30 } }));
            Assert.IsFalse(MatchResultGenerator.IsValid(new MatchResult { winner = 0, a = new[] { 21, 21, 21 }, b = new[] { 10, 12, 5 } }));
            Assert.IsFalse(MatchResultGenerator.IsValid(new MatchResult { winner = 1, a = new[] { 21, 21 }, b = new[] { 10, 12 } }));
            Assert.IsTrue(MatchResultGenerator.IsValid(new MatchResult { winner = 1, a = new[] { 21, 18, 29 }, b = new[] { 19, 21, 30 } }));
        }

        [Test]
        public void Generated_Distribution_IsPlausible()
        {
            var rng = new DetRandom(777UL);
            int evenThree = 0, lopsidedThree = 0, deuce = 0, games = 0, strongWins = 0;
            var lopsidedLoserPts = new List<int>();
            const int N = 20000;
            for (int i = 0; i < N; i++)
            {
                var even = MatchResultGenerator.Generate(80, 80, rng, B.results, R);
                if (even.a.Length == 3) evenThree++;
                for (int g = 0; g < even.a.Length; g++) { games++; if (Mathf.Max(even.a[g], even.b[g]) > 21) deuce++; }

                var lop = MatchResultGenerator.Generate(92, 45, rng, B.results, R);
                if (lop.a.Length == 3) lopsidedThree++;
                if (lop.winner == 0) strongWins++;
                if (lop.winner == 0 && lop.a.Length == 2) { lopsidedLoserPts.Add(lop.b[0]); lopsidedLoserPts.Add(lop.b[1]); }
            }
            float evenRate = evenThree / (float)N, lopRate = lopsidedThree / (float)N;
            Debug.Log($"[TourTest] 3 game: ngang tài {evenRate:P0}, chênh lệch {lopRate:P0}; deuce {deuce / (float)games:P1}; mạnh thắng {strongWins / (float)N:P1}; điểm thua TB {lopsidedLoserPts.Average():F1}");
            Assert.Greater(evenRate, 0.3f);
            Assert.Less(lopRate, 0.12f);
            Assert.Greater(strongWins / (float)N, 0.97f);
            double avg = lopsidedLoserPts.Average();
            Assert.IsTrue(avg >= 8 && avg <= 15, "Điểm thua trung bình trận chênh lệch: " + avg);
            Assert.IsTrue(deuce > 0 && deuce < games * 0.3f, "Tỷ lệ deuce hợp lý");
        }

        [Test]
        public void Generator_IsDeterministic()
        {
            var r1 = MatchResultGenerator.Generate(70, 66, new DetRandom(99UL), B.results, R);
            var r2 = MatchResultGenerator.Generate(70, 66, new DetRandom(99UL), B.results, R);
            Assert.AreEqual(r1.ScoreText(), r2.ScoreText());
            Assert.AreEqual(r1.winner, r2.winner);
        }

        // ───────────── Bốc thăm ─────────────

        [Test]
        public void Draw_PlacesSeedsInStandardPositions()
        {
            var w = new TourWorld(Db);
            var ranked = w.RankByRating(TourEvent.MS).GetRange(0, 32);
            for (int s = 0; s < 50; s++)
            {
                var lines = DrawEngine.Place(32, ranked, 8, 0, new DetRandom((ulong)s), w.Country);
                Assert.AreEqual(ranked[0], lines[0], "HG1 ở đỉnh");
                Assert.AreEqual(ranked[1], lines[31], "HG2 ở đáy");
                CollectionAssert.AreEquivalent(new[] { ranked[2], ranked[3] }, new[] { lines[8], lines[23] }, "HG3-4 ở 2 phần tư còn lại");
                CollectionAssert.AreEquivalent(ranked.GetRange(4, 4), new[] { lines[7], lines[15], lines[16], lines[24] }, "HG5-8 ở 4 phần tám");
                CollectionAssert.AreEquivalent(ranked, lines, "Mọi suất đều có mặt đúng 1 lần");
            }
        }

        [Test]
        public void Draw_ReducesSameCountryFirstRound()
        {
            var w = new TourWorld(Db);
            var ranked = w.RankByRating(TourEvent.MS).GetRange(0, 32);
            int conflicts = 0;
            for (int s = 0; s < 200; s++)
                conflicts += DrawEngine.CountSameCountryFirstRound(DrawEngine.Place(32, ranked, 8, 0, new DetRandom((ulong)s), w.Country), w.Country);
            Debug.Log($"[TourTest] Cặp cùng quốc gia vòng 1 trung bình: {conflicts / 200f:F2}");
            Assert.Less(conflicts / 200f, 0.3f);
        }

        // ───────────── Giải đấu ─────────────

        static TourState Knockout(TourWorld w, long seed, int player = -1)
        {
            var ranked = w.RankByRating(TourEvent.MS).GetRange(0, 32);
            return TourEngine.CreateKnockout(B, w, "T" + seed, "TEST OPEN", 0, TourEvent.MS, seed, ranked, null, 0, player);
        }

        [Test]
        public void Knockout_RunsToChampion_AllScoresValid()
        {
            var w = new TourWorld(Db);
            var eng = new TourEngine(Knockout(w, 1), w, B, R);
            Assert.AreEqual(5, eng.S.rounds.Count);
            Assert.AreEqual("VÒNG 1/32", eng.S.rounds[0].name);
            Assert.AreEqual("CHUNG KẾT", eng.S.rounds[4].name);
            eng.ResolveToEnd();
            Assert.IsTrue(eng.Finished);
            Assert.GreaterOrEqual(eng.S.champion, 0);
            Assert.AreEqual(31, eng.S.slots.Count);
            foreach (var m in eng.S.slots) Assert.IsTrue(MatchResultGenerator.IsValid(m.result, R), m.result.ScoreText());
            Assert.AreEqual(0, eng.Depth(eng.S.champion));
            Assert.AreEqual(12000, eng.Points(eng.S.champion));
        }

        [Test]
        public void Simulate1000Tournaments_TopSeedWinsAboutAThird()
        {
            var w = new TourWorld(Db);
            int topWins = 0, seededChamp = 0;
            for (int i = 0; i < 1000; i++)
            {
                var eng = new TourEngine(Knockout(w, 5000 + i), w, B, R);
                eng.ResolveToEnd();
                if (eng.S.champion == eng.S.seeded[0]) topWins++;
                if (eng.S.SeedOf(eng.S.champion) > 0) seededChamp++;
            }
            Debug.Log($"[TourTest] 1000 giải: HG1 vô địch {topWins / 10f:F1}%, nhà vô địch là hạt giống {seededChamp / 10f:F1}%");
            Assert.IsTrue(topWins >= 250 && topWins <= 350, "HG1 vô địch " + topWins / 10f + "%");
            Assert.Greater(seededChamp, 700);
        }

        [Test]
        public void PlayerMatch_IsNotGenerated_AndRevealsWaitForPlayer()
        {
            var w = new TourWorld(Db);
            int player = w.RankByRating(TourEvent.MS)[20];
            var eng = new TourEngine(Knockout(w, 42, player), w, B, R);
            eng.BeginRound();
            var mine = eng.PlayerSlot();
            Assert.IsNotNull(mine);
            Assert.IsFalse(mine.hasResult, "Trận người chơi không được sinh kết quả");
            Assert.IsTrue(mine.started);
            var pending = eng.PendingReveals();
            Assert.AreEqual(15, pending.Count, "15 trận AI có kết quả nhưng chưa công bố");
            Assert.IsFalse(eng.RoundComplete());

            eng.SubmitPlayerResult(new MatchResult { winner = 0, a = new[] { 21, 21 }, b = new[] { 15, 17 } });
            Assert.IsTrue(eng.RoundComplete());
            Assert.AreEqual(player, mine.Winner);
            eng.RevealAllInRound();
            eng.AdvanceRound();
            Assert.AreEqual(1, eng.S.currentRound);
            eng.BeginRound();
            Assert.IsNotNull(eng.PlayerSlot(), "Người chơi vào vòng 2");
            Assert.AreEqual(7, eng.PendingReveals().Count, "7 trận AI vòng 2 chờ công bố");
        }

        [Test]
        public void PlayerLoss_Eliminates_ThenResolveToEnd()
        {
            var w = new TourWorld(Db);
            int player = w.RankByRating(TourEvent.MS)[20];
            var eng = new TourEngine(Knockout(w, 43, player), w, B, R);
            eng.BeginRound();
            eng.SubmitPlayerResult(new MatchResult { winner = 1, a = new[] { 10, 12 }, b = new[] { 21, 21 } });
            Assert.IsTrue(eng.PlayerEliminated());
            eng.ResolveToEnd();
            Assert.IsTrue(eng.Finished);
            Assert.AreEqual(5, eng.Depth(player));
            Assert.AreEqual("VÒNG 1/32", eng.PlacementText(player));
        }

        [Test]
        public void Qualifying_FeedsQualifiersIntoMainDraw()
        {
            var w = new TourWorld(Db);
            var ranked = w.RankByRating(TourEvent.MS);
            var direct = ranked.GetRange(0, 28);
            var qual = ranked.GetRange(28, 16);
            var t = TourEngine.CreateKnockout(B, w, "Q1", "QUAL CUP", 3, TourEvent.MS, 9, direct, qual, 4, -1);
            Assert.AreEqual("VÒNG LOẠI 1", t.rounds[0].name);
            Assert.AreEqual("CHUNG KẾT VÒNG LOẠI", t.rounds[1].name);
            Assert.AreEqual(4, t.mainLines.Count(x => x <= -2), "4 suất Q trong nhánh chính");
            var eng = new TourEngine(t, w, B, R);
            eng.BeginRound(); eng.AdvanceRound();
            eng.BeginRound(); eng.AdvanceRound();
            Assert.AreEqual(0, t.mainLines.Count(x => x <= -2), "Đã thay suất Q bằng người vượt qua vòng loại");
            foreach (var m in t.SlotsIn(2)) Assert.IsTrue(m.Ready);
            eng.ResolveToEnd();
            Assert.IsTrue(eng.Finished);
            int qLoser = t.SlotsIn(0).First().Loser;
            Assert.AreEqual(-1, eng.Depth(qLoser));
            Assert.Greater(eng.Points(qLoser), 0);
            Assert.Less(eng.Points(qLoser), eng.Points(t.SlotsIn(2).First().Loser));
        }

        [Test]
        public void Groups_RoundRobin_StandingsAndSemis()
        {
            var w = new TourWorld(Db);
            var top8 = w.RankByRating(TourEvent.MS).GetRange(0, 8);
            var t = TourEngine.CreateGroups(B, w, "F1", "FINALS", -1, true, TourEvent.MS, 3, top8, -1);
            Assert.AreEqual(2, t.groups.Count);
            Assert.AreEqual(top8[0], t.groups[0].members[0]);
            Assert.AreEqual(top8[1], t.groups[1].members[0]);
            Assert.AreEqual(5, t.rounds.Count); // 3 lượt + bán kết + chung kết
            var eng = new TourEngine(t, w, B, R);
            for (int md = 0; md < 3; md++) { eng.BeginRound(); eng.AdvanceRound(); }
            var a = eng.Standings(0, false); var b = eng.Standings(1, false);
            foreach (var row in a) Assert.AreEqual(3, row.played);
            var sf = t.SlotsIn(3).ToList();
            Assert.AreEqual(a[0].entry, sf[0].a); Assert.AreEqual(b[1].entry, sf[0].b);
            Assert.AreEqual(b[0].entry, sf[1].a); Assert.AreEqual(a[1].entry, sf[1].b);
            eng.ResolveToEnd();
            Assert.AreEqual(B.finals.champion, eng.Points(t.champion));
            Assert.AreEqual(B.finals.groupFourth, eng.Points(a[3].entry));
        }

        [Test]
        public void Groups_TwoWayTie_UsesHeadToHead()
        {
            var w = new TourWorld(Db);
            var four = w.RankByRating(TourEvent.MS).GetRange(0, 4);
            var t = TourEngine.CreateGroups(B, w, "G4", "MINI", -1, false, TourEvent.MS, 1, four, -1);
            var eng = new TourEngine(t, w, B, R);
            int x = t.groups[0].members[0], y = t.groups[0].members[1], z = t.groups[0].members[2], q = t.groups[0].members[3];
            // x, y mỗi người 2 thắng; y thắng x nhưng hiệu số game kém hơn ⇒ đối đầu quyết định ⇒ y trên x.
            void Set(int p1, int p2, int winner, int[] sa, int[] sb)
            {
                var m = t.slots.First(s => s.group == 0 && s.Has(p1) && s.Has(p2));
                bool p1IsA = m.a == p1;
                var r = new MatchResult { winner = winner == p1 ? (p1IsA ? 0 : 1) : (p1IsA ? 1 : 0), a = p1IsA ? sa : sb, b = p1IsA ? sb : sa };
                m.result = r; m.hasResult = true; m.revealed = true;
            }
            Set(y, x, y, new[] { 19, 21, 21 }, new[] { 21, 19, 19 });
            Set(x, z, x, new[] { 21, 21 }, new[] { 2, 3 });
            Set(x, q, x, new[] { 21, 21 }, new[] { 4, 5 });
            Set(y, z, z, new[] { 15, 15 }, new[] { 21, 21 });
            Set(y, q, y, new[] { 21, 21 }, new[] { 19, 19 });
            Set(z, q, q, new[] { 10, 10 }, new[] { 21, 21 });
            var st = eng.Standings(0, false);
            // x: 2 thắng, y: 2 thắng, z: 1, q: 1  ⇒ y (thắng đối đầu) xếp trên x dù hiệu số game kém hơn.
            Assert.AreEqual(y, st[0].entry);
            Assert.AreEqual(x, st[1].entry);
        }

        // ───────────── Mùa giải ─────────────

        [Test]
        public void Season_CreateRunAndSave_RoundTrips()
        {
            var w = new TourWorld(Db);
            int player = w.RankByRating(TourEvent.MS)[30];
            var save = SeasonEngine.Create(Db, B, 2026, TourEvent.MS, player, "Test");
            var se = new SeasonEngine(save, Db, B, R);
            Assert.AreEqual(1, save.seasonNo);
            Assert.AreEqual(20, save.calendar.Count, "19 giải (2 tuần có 2 giải song song) + chung kết cuối mùa");
            Assert.Greater(save.ranking.Count, 100, "Bảng xếp hạng ban đầu được dựng từ mùa trước");
            var top = se.Ranking(TourEvent.MS);
            Assert.Greater(se.PointsOf(top[0]), se.PointsOf(top[40]));

            // Tuần 1: người chơi nghỉ; tuần 2: dự giải và thua ngay trận đầu.
            se.FinishWeek();
            Assert.AreEqual(1, save.calIndex);
            Assert.AreEqual(5, save.lastWeek.Count, "Kết quả tuần này: 5 nội dung");
            var el = se.PlayerEligibility(se.Current);
            Assert.AreNotEqual(Eligibility.No, el);
            var t = se.EnterCurrent();
            var eng = new TourEngine(t, se.W, B, R);
            eng.BeginRound();
            Assert.IsNotNull(eng.PlayerSlot());
            eng.SubmitPlayerResult(new MatchResult { winner = 1, a = new[] { 10, 12 }, b = new[] { 21, 21 } });

            // Lưu / tải giữa chừng (kết quả đã sinh nhưng chưa công bố cũng được lưu).
            string json = JsonUtility.ToJson(save);
            var loaded = JsonUtility.FromJson<SeasonSave>(json);
            Assert.IsTrue(loaded.hasActive);
            Assert.AreEqual(save.active.slots.Count, loaded.active.slots.Count);
            for (int i = 0; i < save.active.slots.Count; i++)
                Assert.AreEqual(save.active.slots[i].result.ScoreText(), loaded.active.slots[i].result.ScoreText());
            Debug.Log($"[TourTest] Kích thước file lưu mùa giải: {json.Length / 1024f:F0} KB");
            Assert.Less(json.Length, 400 * 1024);

            var se2 = new SeasonEngine(loaded, Db, B, R);
            se2.FinishWeek();
            Assert.AreEqual(1, loaded.achievements.Count);
            Assert.AreEqual(2, loaded.calIndex);
            Assert.IsTrue(loaded.matches.Count >= 1);
            Assert.AreEqual(1, se2.HeadToHead(loaded.matches[0].opponent).lost);
        }

        [Test]
        public void Season_ParallelWeek_NoEntrantPlaysTwice_PlayerCanPickLowerTier()
        {
            var w = new TourWorld(Db);
            foreach (var ev in new[] { TourEvent.MS, TourEvent.MD })
            {
                int player = w.RankByRating(ev)[3];   // top 4: thường sẽ dự giải cấp cao
                var save = SeasonEngine.Create(Db, B, 555, ev, player, "Test");
                var se = new SeasonEngine(save, Db, B, R);
                while (se.CurrentWeekIndices().Count < 2) se.FinishWeek();
                var idx = se.CurrentWeekIndices();
                Assert.Less(save.calendar[idx[0]].tier, save.calendar[idx[1]].tier, "Giải cấp cao xếp trước");

                // Người chơi chọn giải CẤP THẤP hơn.
                var mine = se.EnterCurrent(idx[1]);
                Assert.AreEqual(idx[1], save.activeItem);
                Assert.AreEqual(save.calendar[idx[1]].name, mine.name);
                Assert.Contains(player, mine.entrants);
                CollectionAssert.AllItemsAreUnique(mine.entrants);

                se.FinishWeek();
                Assert.AreEqual(idx[1] + 1, save.calIndex, "Sang tuần sau, bỏ qua cả 2 giải của tuần");
                Assert.IsTrue(save.calendar[idx[0]].done && save.calendar[idx[1]].done);
                Assert.IsTrue(save.calendar[idx[1]].playerPlayed);
                Assert.IsFalse(save.calendar[idx[0]].playerPlayed);
                Assert.AreEqual(10, save.lastWeek.Count, "2 giải × 5 nội dung");
                Debug.Log($"[TourTest] {ev}: tuần {save.lastWeekNo} có 2 giải; giải của bạn: {mine.name} ({mine.drawSize} suất)");
            }
        }

        [Test]
        public void Season_ParallelWeek_EntrantsDisjoint()
        {
            var w = new TourWorld(Db);
            int player = w.RankByRating(TourEvent.WS)[10];
            var save = SeasonEngine.Create(Db, B, 777, TourEvent.WS, player, "Test");
            var se = new SeasonEngine(save, Db, B, R);
            while (se.CurrentWeekIndices().Count < 2) se.FinishWeek();
            var idx = se.CurrentWeekIndices();
            foreach (var ev in Events.All)
            {
                var a = se.CreateTournament(save.calendar[idx[0]], ev, false);
                var used = new HashSet<int>(a.entrants);
                var b = se.CreateTournament(save.calendar[idx[1]], ev, false, used);
                foreach (int e in b.entrants) Assert.IsFalse(used.Contains(e), $"{ev}: suất {e} dự 2 giải cùng tuần");
                Assert.GreaterOrEqual(b.drawSize, 8, ev + ": giải song song vẫn đủ người");
            }
        }

        [Test]
        public void Season_FullSeason_RatingsDriftAndFinalsHappen()
        {
            var w = new TourWorld(Db);
            int player = w.RankByRating(TourEvent.WS)[50];
            var save = SeasonEngine.Create(Db, B, 99, TourEvent.WS, player, "Test");
            var se = new SeasonEngine(save, Db, B, R);
            var before = (int[])save.ratings.Clone();
            while (!se.SeasonOver) se.FinishWeek();
            int changed = 0, maxDelta = 0;
            for (int i = 0; i < before.Length; i++)
            {
                if (before[i] != save.ratings[i]) changed++;
                maxDelta = Mathf.Max(maxDelta, Mathf.Abs(before[i] - save.ratings[i]));
            }
            var pa = Db.entries[player].a;
            Assert.AreEqual(before[pa], save.ratings[pa], "Rating người chơi không tự đổi");
            Assert.Greater(changed, before.Length / 4, "Rating AI thay đổi qua mùa");
            Debug.Log($"[TourTest] Cả mùa: {changed} VĐV đổi rating, lệch tối đa {maxDelta}");
            Assert.IsTrue(save.lastWeek.Exists(d => d.finals), "Tuần cuối là giải chung kết");
            se.StartNextSeason();
            Assert.AreEqual(2, save.seasonNo);
            Assert.AreEqual(0, save.calIndex);
        }
    }
}
