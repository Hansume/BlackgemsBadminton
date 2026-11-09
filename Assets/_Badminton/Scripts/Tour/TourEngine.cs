using System;
using System.Collections.Generic;
using Badminton.Core;

namespace Badminton.Tour
{
    /// <summary>Một dòng bảng xếp hạng vòng bảng.</summary>
    public class StandingRow
    {
        public int entry;
        public int played, won, lost;
        public int gamesFor, gamesAgainst, pointsFor, pointsAgainst;
        public int GameDiff => gamesFor - gamesAgainst;
        public int PointDiff => pointsFor - pointsAgainst;
    }

    /// <summary>
    /// Engine giải đấu – C# thuần, không MonoBehaviour. Bọc một TourState (lưu được) và điều khiển:
    /// tạo giải (loại trực tiếp có/không vòng loại, vòng bảng + loại trực tiếp), bắt đầu vòng (sinh sẵn kết quả
    /// các trận AI – chưa công bố), nhận kết quả trận người chơi, công bố, sang vòng, tính điểm xếp hạng.
    /// </summary>
    public sealed class TourEngine
    {
        public readonly TourState S;
        public readonly TourWorld W;
        public readonly TourBalance B;
        public readonly MatchRulesConfig Rules;

        public TourEngine(TourState s, TourWorld w, TourBalance b, MatchRulesConfig rules = null)
        {
            S = s; W = w; B = b; Rules = rules ?? new MatchRulesConfig();
        }

        // ───────────────────────────── TẠO GIẢI ─────────────────────────────

        public static string RoundName(int size) => size switch
        {
            2 => "CHUNG KẾT",
            4 => "BÁN KẾT",
            8 => "TỨ KẾT",
            _ => "VÒNG 1/" + size,
        };

        /// <summary>
        /// Giải loại trực tiếp. direct = suất vào thẳng (đã sắp theo thứ hạng); qual = suất đánh vòng loại
        /// (đã sắp theo thứ hạng, có thể rỗng). Kích thước nhánh = direct + số suất vòng loại (phải là 2^k).
        /// </summary>
        public static TourState CreateKnockout(TourBalance b, TourWorld w, string uid, string name, int tier, TourEvent ev,
                                               long seed, IList<int> direct, IList<int> qual, int qualifiers, int playerEntry)
        {
            bool hasQual = qual != null && qual.Count > 0 && qualifiers > 0;
            int n = direct.Count + (hasQual ? qualifiers : 0);
            if (n < 2 || (n & (n - 1)) != 0) throw new ArgumentException("Kích thước nhánh phải là lũy thừa của 2: " + n);

            var s = new TourState
            {
                uid = uid, name = name, tier = tier, format = TourFormat.Knockout, ev = ev, seed = seed,
                drawSize = n, playerEntry = playerEntry, qualifiers = hasQual ? qualifiers : 0,
            };
            var rng = new DetRandom(DetRandom.Mix((ulong)seed, DetRandom.Hash(uid), 1));
            s.seedCount = Math.Min(DrawEngine.SeedCountFor(n, b.seeds), direct.Count);
            s.seeded = new int[s.seedCount];
            for (int i = 0; i < s.seedCount; i++) s.seeded[i] = direct[i];
            s.mainLines = DrawEngine.Place(n, direct, s.seedCount, hasQual ? qualifiers : 0, rng, w.Country);

            var all = new List<int>(direct);
            int round = 0;
            if (hasQual)
            {
                int qn = qual.Count;
                if ((qn & (qn - 1)) != 0 || qn % qualifiers != 0) throw new ArgumentException("Vòng loại phải có 2^k suất: " + qn);
                s.qualLines = DrawEngine.Place(qn, qual, Math.Min(qualifiers, qn / 2), 0, rng, w.Country);
                all.AddRange(qual);
                int qRounds = 0;
                for (int sz = qn; sz > qualifiers; sz /= 2) qRounds++;
                for (int r = 0; r < qRounds; r++)
                {
                    int size = qn >> r;
                    s.rounds.Add(new RoundInfo { name = r == qRounds - 1 ? "CHUNG KẾT VÒNG LOẠI" : "VÒNG LOẠI " + (r + 1), stage = TourStage.Qualifying, size = size });
                    for (int k = 0; k < size / 2; k++)
                        s.slots.Add(new MatchSlot { id = s.slots.Count, round = round, index = k,
                            a = r == 0 ? s.qualLines[2 * k] : -1, b = r == 0 ? s.qualLines[2 * k + 1] : -1 });
                    round++;
                }
            }
            for (int size = n; size >= 2; size /= 2, round++)
            {
                s.rounds.Add(new RoundInfo { name = RoundName(size), stage = TourStage.Main, size = size });
                bool first = size == n;
                for (int k = 0; k < size / 2; k++)
                    s.slots.Add(new MatchSlot { id = s.slots.Count, round = round, index = k,
                        a = first ? s.mainLines[2 * k] : -1, b = first ? s.mainLines[2 * k + 1] : -1 });
            }
            s.entrants = all.ToArray();
            return s;
        }

        /// <summary>
        /// Vòng bảng (bảng 4, vòng tròn 3 lượt) + loại trực tiếp cho 2 người đầu mỗi bảng.
        /// entrants đã sắp theo thứ hạng; số lượng 4 / 8 / 16 ⇒ 1 / 2 / 4 bảng.
        /// </summary>
        public static TourState CreateGroups(TourBalance b, TourWorld w, string uid, string name, int tier, bool finals,
                                             TourEvent ev, long seed, IList<int> entrants, int playerEntry)
        {
            int n = entrants.Count;
            if (n % 4 != 0 || n < 4) throw new ArgumentException("Vòng bảng cần 4/8/16 suất: " + n);
            int gCount = n / 4;
            var s = new TourState
            {
                uid = uid, name = name, tier = tier, finals = finals, format = TourFormat.Groups, ev = ev, seed = seed,
                drawSize = n, playerEntry = playerEntry, entrants = new List<int>(entrants).ToArray(),
            };
            var rng = new DetRandom(DetRandom.Mix((ulong)seed, DetRandom.Hash(uid), 2));
            s.seedCount = gCount;
            s.seeded = new int[gCount];
            for (int i = 0; i < gCount; i++) s.seeded[i] = entrants[i];

            // Chia bảng theo nhóm hạt giống: nhóm 1 (hạt giống 1..G) xếp lần lượt A, B, …; các nhóm sau bốc thăm.
            var members = new List<int>[gCount];
            for (int g = 0; g < gCount; g++) members[g] = new List<int>();
            for (int pot = 0; pot < 4; pot++)
            {
                var potList = new List<int>();
                for (int i = 0; i < gCount; i++) potList.Add(entrants[pot * gCount + i]);
                if (pot > 0) rng.Shuffle(potList);
                for (int g = 0; g < gCount; g++) members[g].Add(potList[g]);
            }
            for (int g = 0; g < gCount; g++)
                s.groups.Add(new GroupInfo { name = "BẢNG " + (char)('A' + g), members = members[g].ToArray() });

            // 3 lượt đấu vòng tròn.
            int[][,] pairs =
            {
                new[,] { { 0, 3 }, { 1, 2 } },
                new[,] { { 0, 2 }, { 3, 1 } },
                new[,] { { 0, 1 }, { 2, 3 } },
            };
            int round = 0;
            for (int md = 0; md < 3; md++, round++)
            {
                s.rounds.Add(new RoundInfo { name = "LƯỢT ĐẤU " + (md + 1), stage = TourStage.Group, size = 0 });
                for (int g = 0; g < gCount; g++)
                    for (int k = 0; k < 2; k++)
                        s.slots.Add(new MatchSlot { id = s.slots.Count, round = round, index = g * 2 + k, group = g,
                            a = members[g][pairs[md][k, 0]], b = members[g][pairs[md][k, 1]] });
            }
            for (int size = gCount * 2; size >= 2; size /= 2, round++)
            {
                s.rounds.Add(new RoundInfo { name = RoundName(size), stage = TourStage.Playoff, size = size });
                for (int k = 0; k < size / 2; k++)
                    s.slots.Add(new MatchSlot { id = s.slots.Count, round = round, index = k });
            }
            return s;
        }

        // ───────────────────────────── DIỄN BIẾN ─────────────────────────────

        public RoundInfo Round => S.Round;
        public bool Finished => S.finished;

        public bool IsPlayerMatch(MatchSlot m) => S.playerEntry >= 0 && m.Has(S.playerEntry);

        /// <summary>Trận của người chơi ở vòng hiện tại (null nếu không có / đã xong).</summary>
        public MatchSlot PlayerSlot()
        {
            if (S.finished) return null;
            foreach (var m in S.SlotsIn(S.currentRound)) if (IsPlayerMatch(m) && m.Ready && !m.hasResult) return m;
            return null;
        }

        /// <summary>
        /// Bắt đầu vòng hiện tại: mọi trận đủ 2 bên chuyển sang "Đang thi đấu…", kết quả các trận AI được
        /// sinh sẵn (tất định theo seed) nhưng CHƯA công bố.
        /// </summary>
        public void BeginRound()
        {
            if (S.finished || S.roundStarted) return;
            S.roundStarted = true;
            foreach (var m in S.SlotsIn(S.currentRound))
            {
                if (!m.Ready) continue;
                m.started = true;
                if (!IsPlayerMatch(m) && !m.hasResult) Resolve(m);
            }
        }

        void Resolve(MatchSlot m)
        {
            var rng = new DetRandom(DetRandom.Mix((ulong)S.seed, DetRandom.Hash(S.uid), (ulong)(m.id + 1000)));
            m.result = MatchResultGenerator.Generate(W.Rating(m.a), W.Rating(m.b), rng, B.results, Rules);
            m.hasResult = true;
            m.revealed = false;
        }

        /// <summary>Ghi kết quả trận 3D của người chơi. result theo góc nhìn người chơi (bên 0 = người chơi).</summary>
        public void SubmitPlayerResult(MatchResult playerView)
        {
            var m = PlayerSlot();
            if (m == null) return;
            m.result = m.a == S.playerEntry ? playerView : playerView.Swapped();
            m.hasResult = true;
            m.revealed = true;
        }

        public bool RoundComplete()
        {
            foreach (var m in S.SlotsIn(S.currentRound)) if (m.Ready && !m.hasResult) return false;
            return true;
        }

        /// <summary>Các trận đã có kết quả nhưng chưa công bố ở vòng hiện tại (theo thứ tự trên nhánh).</summary>
        public List<MatchSlot> PendingReveals()
        {
            var l = new List<MatchSlot>();
            foreach (var m in S.SlotsIn(S.currentRound)) if (m.hasResult && !m.revealed) l.Add(m);
            return l;
        }

        public void RevealAllInRound()
        {
            foreach (var m in S.SlotsIn(S.currentRound)) if (m.hasResult) m.revealed = true;
        }

        public bool IsLastRound => S.currentRound >= S.rounds.Count - 1;

        /// <summary>Sang vòng tiếp theo (vòng hiện tại phải xong). Điền người thắng vào vòng sau / tính bảng.</summary>
        public void AdvanceRound()
        {
            if (S.finished || !RoundComplete()) return;
            BeginRound(); // vòng không có trận nào sẵn sàng (hiếm) vẫn coi là đã bắt đầu
            RevealAllInRound();
            int r = S.currentRound;
            var stage = S.rounds[r].stage;

            if (IsLastRound)
            {
                foreach (var m in S.SlotsIn(r)) S.champion = m.Winner;
                S.finished = true;
                return;
            }

            var next = S.rounds[r + 1];
            if (stage == TourStage.Qualifying && next.stage == TourStage.Main)
            {
                // Người thắng chung kết vòng loại k ⇒ thay suất Q(k) ở vòng 1 nhánh chính.
                foreach (var q in S.SlotsIn(r))
                {
                    int placeholder = -2 - q.index;
                    foreach (var m in S.SlotsIn(r + 1))
                    {
                        if (m.a == placeholder) m.a = q.Winner;
                        if (m.b == placeholder) m.b = q.Winner;
                    }
                    for (int i = 0; i < S.mainLines.Length; i++) if (S.mainLines[i] == placeholder) S.mainLines[i] = q.Winner;
                }
            }
            else if (stage == TourStage.Group && next.stage == TourStage.Playoff)
            {
                FillPlayoffFromGroups(r + 1);
            }
            else if (stage != TourStage.Group)
            {
                var cur = new List<MatchSlot>(S.SlotsIn(r));
                foreach (var m in S.SlotsIn(r + 1))
                {
                    m.a = cur[2 * m.index].Winner;
                    m.b = cur[2 * m.index + 1].Winner;
                }
            }
            S.currentRound++;
            S.roundStarted = false;
        }

        void FillPlayoffFromGroups(int round)
        {
            int g = S.groups.Count;
            var top = new int[g, 2];
            for (int i = 0; i < g; i++)
            {
                var st = Standings(i, false);
                top[i, 0] = st[0].entry;
                top[i, 1] = st[1].entry;
            }
            var po = new List<MatchSlot>(S.SlotsIn(round));
            if (g == 1) { po[0].a = top[0, 0]; po[0].b = top[0, 1]; }
            else if (g == 2)
            {
                po[0].a = top[0, 0]; po[0].b = top[1, 1];   // nhất A – nhì B
                po[1].a = top[1, 0]; po[1].b = top[0, 1];   // nhất B – nhì A
            }
            else
            {
                // 4 bảng: A1-B2, C1-D2 (nửa trên) | B1-A2, D1-C2 (nửa dưới).
                int[,] map = { { 0, 1 }, { 2, 3 }, { 1, 0 }, { 3, 2 } };
                for (int k = 0; k < 4; k++) { po[k].a = top[map[k, 0], 0]; po[k].b = top[map[k, 1], 1]; }
            }
        }

        /// <summary>Người chơi đã bị loại chưa (theo các kết quả hiện có).</summary>
        public bool PlayerEliminated()
        {
            int p = S.playerEntry;
            if (p < 0) return false;
            foreach (var m in S.slots)
            {
                if (!m.hasResult || !m.Has(p)) continue;
                if (S.rounds[m.round].stage != TourStage.Group && m.Loser == p) return true;
            }
            if (S.format == TourFormat.Groups)
            {
                int g = GroupOf(p);
                if (g >= 0 && GroupComplete(g))
                {
                    var st = Standings(g, false);
                    if (st[0].entry != p && st[1].entry != p) return true;
                }
            }
            return false;
        }

        /// <summary>Người chơi vô địch.</summary>
        public bool PlayerChampion => S.finished && S.champion == S.playerEntry && S.playerEntry >= 0;

        /// <summary>Sinh và công bố toàn bộ các vòng còn lại (người chơi đã bị loại / không tham dự).</summary>
        public void ResolveToEnd()
        {
            int guard = 0;
            while (!S.finished && guard++ < 64)
            {
                BeginRound();
                foreach (var m in S.SlotsIn(S.currentRound))
                    if (m.Ready && !m.hasResult) Resolve(m); // phòng hờ: trận của người chơi chưa đánh ⇒ sinh luôn
                AdvanceRound();
            }
        }

        // ───────────────────────────── VÒNG BẢNG ─────────────────────────────

        public int GroupOf(int entry)
        {
            for (int g = 0; g < S.groups.Count; g++)
                if (Array.IndexOf(S.groups[g].members, entry) >= 0) return g;
            return -1;
        }

        public bool GroupComplete(int g)
        {
            foreach (var m in S.slots) if (m.group == g && !m.hasResult) return false;
            return true;
        }

        /// <summary>
        /// Bảng xếp hạng: số trận thắng → đối đầu (khi đúng 2 người bằng nhau) → hiệu số game → hiệu số điểm.
        /// onlyRevealed = chỉ tính trận đã công bố (dùng cho hiển thị).
        /// </summary>
        public List<StandingRow> Standings(int g, bool onlyRevealed)
        {
            var rows = new Dictionary<int, StandingRow>();
            foreach (int e in S.groups[g].members) rows[e] = new StandingRow { entry = e };
            foreach (var m in S.slots)
            {
                if (m.group != g || !m.hasResult || (onlyRevealed && !m.revealed)) continue;
                var ra = rows[m.a]; var rb = rows[m.b];
                ra.played++; rb.played++;
                if (m.result.winner == 0) { ra.won++; rb.lost++; } else { rb.won++; ra.lost++; }
                ra.gamesFor += m.result.GamesA; ra.gamesAgainst += m.result.GamesB;
                rb.gamesFor += m.result.GamesB; rb.gamesAgainst += m.result.GamesA;
                ra.pointsFor += m.result.PointsA; ra.pointsAgainst += m.result.PointsB;
                rb.pointsFor += m.result.PointsB; rb.pointsAgainst += m.result.PointsA;
            }
            var list = new List<StandingRow>(rows.Values);
            list.Sort((x, y) =>
            {
                int c = y.won.CompareTo(x.won);
                if (c != 0) return c;
                int tied = 0;
                foreach (var r in list) if (r.won == x.won) tied++;
                if (tied == 2)
                {
                    int h = HeadToHead(x.entry, y.entry, onlyRevealed);
                    if (h != 0) return h;
                }
                c = y.GameDiff.CompareTo(x.GameDiff);
                if (c != 0) return c;
                c = y.PointDiff.CompareTo(x.PointDiff);
                if (c != 0) return c;
                c = W.Rating(y.entry).CompareTo(W.Rating(x.entry));
                return c != 0 ? c : x.entry.CompareTo(y.entry);
            });
            return list;
        }

        /// <summary>-1 nếu x thắng y trong trận đối đầu, 1 nếu y thắng, 0 nếu chưa gặp.</summary>
        int HeadToHead(int x, int y, bool onlyRevealed)
        {
            foreach (var m in S.slots)
            {
                if (m.group < 0 || !m.hasResult || (onlyRevealed && !m.revealed)) continue;
                if (m.Has(x) && m.Has(y)) return m.Winner == x ? -1 : 1;
            }
            return 0;
        }

        // ───────────────────────────── THÀNH TÍCH & ĐIỂM ─────────────────────────────

        /// <summary>
        /// "Độ sâu" thành tích ở nhánh chính: 0 = vô địch, 1 = á quân, 2 = bán kết, 3 = tứ kết, 4 = 1/16, 5 = 1/32.
        /// -1 = chỉ đánh vòng loại; -2 = không tham dự. Vòng bảng: 10 = thứ 3 bảng, 11 = thứ 4 bảng.
        /// </summary>
        public int Depth(int entry)
        {
            int lastMainRound = -1;
            bool inQual = false, inGroup = false;
            foreach (var m in S.slots)
            {
                if (!m.Has(entry)) continue;
                var st = S.rounds[m.round].stage;
                if (st == TourStage.Qualifying) inQual = true;
                else if (st == TourStage.Group) inGroup = true;
                else if (m.round > lastMainRound) lastMainRound = m.round;
            }
            if (lastMainRound >= 0)
            {
                if (S.finished && S.champion == entry) return 0;
                int size = S.rounds[lastMainRound].size;
                int d = 0;
                while ((1 << d) < size) d++;
                return d; // thua ở vòng có size người: 2 ⇒ 1, 4 ⇒ 2, …
            }
            if (inGroup)
            {
                int g = GroupOf(entry);
                var st = Standings(g, false);
                int pos = st.FindIndex(r => r.entry == entry);
                return pos >= 3 ? 11 : 10;
            }
            return inQual ? -1 : -2;
        }

        /// <summary>Vòng loại: người này bị loại ở vòng loại nào (0 = vòng loại đầu, ≥1 = sau đó).</summary>
        int QualExitRound(int entry)
        {
            int last = -1;
            foreach (var m in S.slots)
                if (m.Has(entry) && S.rounds[m.round].stage == TourStage.Qualifying && m.round > last) last = m.round;
            return last;
        }

        public string PlacementText(int entry)
        {
            int d = Depth(entry);
            switch (d)
            {
                case 0: return "VÔ ĐỊCH";
                case 1: return "Á QUÂN";
                case 2: return "BÁN KẾT";
                case 3: return "TỨ KẾT";
                case 10: return "THỨ 3 BẢNG";
                case 11: return "THỨ 4 BẢNG";
                case -1: return "VÒNG LOẠI";
                case -2: return "—";
                default: return "VÒNG 1/" + (1 << d);
            }
        }

        /// <summary>Điểm xếp hạng nhận được (theo cấp giải và vòng đạt được).</summary>
        public int Points(int entry)
        {
            if (S.finals)
            {
                var f = B.finals;
                switch (Depth(entry))
                {
                    case 0: return f.champion;
                    case 1: return f.runnerUp;
                    case 2: return f.semifinal;
                    case 10: return f.groupThird;
                    case 11: return f.groupFourth;
                    default: return 0;
                }
            }
            if (S.tier < 0) return 0;
            var t = B.Tier(S.tier);
            int depth = Depth(entry);
            int firstMainDepth = 0;
            while ((1 << firstMainDepth) < S.drawSize) firstMainDepth++;
            if (depth >= 0) return t.PointsForDepth(depth);
            if (depth == -1)
            {
                int basePts = t.PointsForDepth(firstMainDepth);
                int qr = QualExitRound(entry);
                bool lostQualFinal = qr >= 0 && S.rounds[qr].name.StartsWith("CHUNG KẾT");
                return (int)(Math.Round(basePts * (lostQualFinal ? t.qualFinalFactor : t.qualFirstFactor) / 10.0) * 10);
            }
            return 0;
        }

        /// <summary>(số trận thắng thực tế, số trận thắng kỳ vọng theo rating) – dùng cho trôi rating AI.</summary>
        public (int wins, double expected) Performance(int entry)
        {
            int wins = 0; double exp = 0;
            foreach (var m in S.slots)
            {
                if (!m.hasResult || !m.Has(entry) || m.result.retired) continue;
                int opp = m.a == entry ? m.b : m.a;
                exp += MatchResultGenerator.WinProbability(W.Rating(entry), W.Rating(opp), B.results.ratingScale);
                if (m.Winner == entry) wins++;
            }
            return (wins, exp);
        }

        /// <summary>Đường đi của người chơi trên nhánh (để highlight).</summary>
        public bool OnPlayerPath(MatchSlot m) => IsPlayerMatch(m);
    }
}
