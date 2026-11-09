using System;
using System.Collections.Generic;

namespace Badminton.Tour
{
    /// <summary>
    /// Bộ sinh số ngẫu nhiên TẤT ĐỊNH (SplitMix64) – cùng seed ⇒ cùng dãy số trên mọi nền tảng
    /// (Editor, WebGL/IL2CPP). Kết quả trận AI lấy seed = Hash(seed mùa giải, mã giải, mã trận) nên
    /// không phụ thuộc thứ tự tính và tải lại file lưu vẫn ra đúng tỷ số cũ.
    /// </summary>
    public sealed class DetRandom
    {
        ulong _s;

        public DetRandom(ulong seed) { _s = seed; }
        public DetRandom(long seed) : this(unchecked((ulong)seed)) { }

        public ulong NextULong()
        {
            unchecked
            {
                ulong z = _s += 0x9E3779B97F4A7C15UL;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        /// <summary>[0, 1)</summary>
        public double NextDouble() => (NextULong() >> 11) * (1.0 / 9007199254740992.0);

        /// <summary>[0, max)</summary>
        public int Next(int max) => max <= 0 ? 0 : (int)(NextDouble() * max);

        /// <summary>[min, max]</summary>
        public int Range(int min, int max) => min + Next(max - min + 1);

        public bool Chance(double p) => NextDouble() < p;

        /// <summary>Phân phối chuẩn N(0,1) (Box–Muller), cắt ở ±3σ.</summary>
        public double Gauss()
        {
            double u1 = 1.0 - NextDouble(), u2 = NextDouble();
            double z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            return Math.Max(-3.0, Math.Min(3.0, z));
        }

        public void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        public T Pick<T>(IList<T> list) => list[Next(list.Count)];

        // ───────────── Hash tất định (không dùng string.GetHashCode – khác nhau giữa các runtime) ─────────────

        public static ulong Hash(string s)
        {
            unchecked
            {
                ulong h = 14695981039346656037UL;
                foreach (char c in s ?? "") { h ^= c; h *= 1099511628211UL; }
                return h;
            }
        }

        public static ulong Mix(ulong a, ulong b)
        {
            unchecked
            {
                ulong z = a ^ (b + 0x9E3779B97F4A7C15UL + (a << 6) + (a >> 2));
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        public static ulong Mix(ulong a, ulong b, ulong c) => Mix(Mix(a, b), c);
    }
}
