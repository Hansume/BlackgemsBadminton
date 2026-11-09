using System;
using System.Collections.Generic;

namespace Badminton.Tour
{
    /// <summary>
    /// Bốc thăm nhánh loại trực tiếp theo chuẩn:
    ///  • Hạt giống 1 ở đỉnh nhánh (dòng 0), hạt giống 2 ở đáy nhánh (dòng N-1).
    ///  • Hạt giống 3–4 bốc thăm vào 2 phần tư còn lại (dòng q và 3q-1).
    ///  • Hạt giống 5–8 bốc thăm vào 4 phần tám còn lại (dòng q-1, 2q-1, 2q, 3q).
    ///  • Người không hạt giống (và suất vòng loại) xếp ngẫu nhiên vào chỗ trống.
    ///  • Hạn chế 2 VĐV cùng quốc gia gặp nhau ở vòng 1 (đổi chỗ người không hạt giống nếu được).
    /// </summary>
    public static class DrawEngine
    {
        public const int Empty = int.MinValue;

        /// <summary>Các nhóm dòng dành cho hạt giống: [ [1], [2], [3,4], [5..8] ].</summary>
        public static List<int[]> SeedLineGroups(int n, int seedCount)
        {
            int q = n / 4;
            var groups = new List<int[]> { new[] { 0 }, new[] { n - 1 } };
            if (seedCount > 2 && n >= 8) groups.Add(new[] { q, 3 * q - 1 });
            if (seedCount > 4 && n >= 16) groups.Add(new[] { q - 1, 2 * q - 1, 2 * q, 3 * q });
            return groups;
        }

        public static int SeedCountFor(int drawSize, int seedsFor32) =>
            drawSize >= 32 ? seedsFor32 : drawSize >= 16 ? 4 : 2;

        /// <param name="ranked">Suất vào thẳng, đã sắp theo thứ hạng (người đầu = hạt giống 1).</param>
        /// <param name="placeholders">Số suất dành cho người vượt qua vòng loại (giá trị -2, -3, …).</param>
        public static int[] Place(int n, IList<int> ranked, int seedCount, int placeholders, DetRandom rng, Func<int, string> countryOf)
        {
            if (ranked.Count + placeholders != n) throw new ArgumentException($"Cần {n} suất, có {ranked.Count}+{placeholders}");
            var lines = new int[n];
            for (int i = 0; i < n; i++) lines[i] = Empty;
            var seedLines = new HashSet<int>();

            int idx = 0;
            foreach (var g in SeedLineGroups(n, seedCount))
            {
                var shuffled = new List<int>(g);
                rng.Shuffle(shuffled);
                foreach (int line in shuffled)
                {
                    if (idx >= ranked.Count || idx >= seedCount) break;
                    lines[line] = ranked[idx++];
                    seedLines.Add(line);
                }
            }

            var rest = new List<int>();
            for (int i = idx; i < ranked.Count; i++) rest.Add(ranked[i]);
            for (int k = 0; k < placeholders; k++) rest.Add(-2 - k);
            rng.Shuffle(rest);
            int r = 0;
            for (int i = 0; i < n; i++) if (lines[i] == Empty) lines[i] = rest[r++];

            AvoidSameCountry(lines, seedLines, rng, countryOf);
            return lines;
        }

        static bool Conflict(int x, int y, Func<int, string> countryOf) =>
            x >= 0 && y >= 0 && countryOf(x) == countryOf(y);

        /// <summary>Đổi chỗ người không hạt giống để 2 người cùng quốc gia không gặp nhau ở vòng 1 (nếu được).</summary>
        public static void AvoidSameCountry(int[] lines, HashSet<int> fixedLines, DetRandom rng, Func<int, string> countryOf)
        {
            int n = lines.Length;
            var order = new List<int>();
            for (int i = 0; i < n; i++) if (!fixedLines.Contains(i)) order.Add(i);

            for (int pass = 0; pass < 4; pass++)
            {
                bool changed = false;
                for (int p = 0; p < n; p += 2)
                {
                    if (!Conflict(lines[p], lines[p + 1], countryOf)) continue;
                    int mover = !fixedLines.Contains(p + 1) ? p + 1 : !fixedLines.Contains(p) ? p : -1;
                    if (mover < 0) continue;
                    rng.Shuffle(order);
                    foreach (int j in order)
                    {
                        if (j / 2 == p / 2) continue;
                        int partnerOfMover = mover ^ 1, partnerOfJ = j ^ 1;
                        // Sau khi đổi: lines[j] đứng cạnh partnerOfMover; lines[mover] đứng cạnh partnerOfJ.
                        if (Conflict(lines[j], lines[partnerOfMover], countryOf)) continue;
                        if (Conflict(lines[mover], lines[partnerOfJ], countryOf)) continue;
                        (lines[mover], lines[j]) = (lines[j], lines[mover]);
                        changed = true;
                        break;
                    }
                }
                if (!changed) break;
            }
        }

        public static int CountSameCountryFirstRound(int[] lines, Func<int, string> countryOf)
        {
            int c = 0;
            for (int p = 0; p + 1 < lines.Length; p += 2) if (Conflict(lines[p], lines[p + 1], countryOf)) c++;
            return c;
        }
    }
}
