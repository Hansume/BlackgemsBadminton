using System;
using System.IO;
using Badminton.Audio;
using UnityEditor;
using UnityEngine;
using B = Badminton.EditorTools.CourtSceneBuilder;

namespace Badminton.EditorTools
{
    /// <summary>
    /// Tổng hợp toàn bộ âm thanh của game bằng code (không cần file âm thanh bên ngoài / bản quyền):
    /// tiếng vợt, cầu chạm lưới/sàn, giày rít, khán giả (nền, reo hò, vỗ tay), tiếng click UI, còi.
    /// Ghi ra WAV mono 16-bit 22 kHz (nhỏ gọn cho WebGL), import với Load Type phù hợp.
    /// </summary>
    public static class AudioSynthBuilder
    {
        const int Rate = 22050;
        const string AudioDir = B.Root + "/Art/Audio";
        const string LibraryPath = B.Root + "/Settings/AudioLibrary.asset";

        [MenuItem("Badminton/Tạo lại âm thanh (tổng hợp)", priority = 21)]
        public static void BuildMenu() { var lib = Build(force: true); Selection.activeObject = lib; }

        /// <summary>Tạo các file âm thanh (nếu chưa có hoặc force) và AudioLibrary.</summary>
        public static AudioLibrary Build(bool force = false)
        {
            if (!AssetDatabase.IsValidFolder(AudioDir)) AssetDatabase.CreateFolder(B.Root + "/Art", "Audio");
            var rng = new System.Random(2024);

            AudioClip Make(string name, Func<float[]> synth, bool loop = false)
            {
                string path = $"{AudioDir}/{name}.wav";
                if (force || !File.Exists(path))
                {
                    WriteWav(path, synth());
                    AssetDatabase.ImportAsset(path);
                    var imp = (AudioImporter)AssetImporter.GetAtPath(path);
                    var s = imp.defaultSampleSettings;
                    // Âm ngắn: giải nén sẵn (phát tức thì). Âm dài: nén Vorbis cho nhẹ.
                    s.loadType = loop ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
                    s.compressionFormat = AudioCompressionFormat.Vorbis;
                    s.quality = 0.6f;
                    imp.defaultSampleSettings = s;
                    imp.forceToMono = true;
                    imp.SaveAndReimport();
                }
                return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            }

            var lib = AssetDatabase.LoadAssetAtPath<AudioLibrary>(LibraryPath);
            if (lib == null) { lib = ScriptableObject.CreateInstance<AudioLibrary>(); AssetDatabase.CreateAsset(lib, LibraryPath); }

            lib.hitSoft = Make("hit_soft", () => Hit(rng, 0.10f, 1500f, 0.35f, 0.55f));
            lib.hitClear = Make("hit_clear", () => Hit(rng, 0.14f, 1150f, 0.6f, 0.85f));
            lib.hitSmash = Make("hit_smash", () => Hit(rng, 0.20f, 900f, 1.0f, 1f));
            lib.netHit = Make("net_hit", () => Thud(rng, 0.12f, 140f, 0.5f));
            lib.shuttleLand = Make("shuttle_land", () => Thud(rng, 0.05f, 420f, 0.35f));
            lib.squeaks = new[]
            {
                Make("squeak_1", () => Squeak(0.16f, 1900f, 3100f, 38f)),
                Make("squeak_2", () => Squeak(0.22f, 2300f, 1700f, 45f)),
                Make("squeak_3", () => Squeak(0.12f, 2600f, 3400f, 52f)),
            };
            lib.crowdLoop = Make("crowd_loop", () => CrowdLoop(rng, 8f), loop: true);
            lib.cheer = Make("crowd_cheer", () => Cheer(rng, 3f), loop: true);
            lib.applause = Make("crowd_applause", () => Applause(rng, 2.4f), loop: true);
            lib.uiClick = Make("ui_click", () => Blip(0.05f, 1200f));
            lib.whistle = Make("whistle", () => Whistle(rng, 0.45f));

            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();
            return lib;
        }

        // ───────────────────────────── BỘ TỔNG HỢP ─────────────────────────────

        static float[] Buffer(float seconds) => new float[Mathf.CeilToInt(seconds * Rate)];
        static float Noise(System.Random r) => (float)(r.NextDouble() * 2.0 - 1.0);

        /// <summary>Tiếng vợt: tiếng "bốp" = nhiễu ngắn (dây vợt) + tông cộng hưởng tắt nhanh + "thịch" trầm.</summary>
        static float[] Hit(System.Random r, float len, float tone, float body, float gain)
        {
            var b = Buffer(len);
            float lp = 0f;
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)Rate;
                float attack = Mathf.Clamp01(t / 0.0015f);
                float crack = Noise(r) * Mathf.Exp(-t * 90f);
                lp += (crack - lp) * 0.35f;                                              // lọc thông thấp đơn giản
                float ring = Mathf.Sin(2f * Mathf.PI * tone * t) * Mathf.Exp(-t * 55f) * 0.45f;
                float thump = Mathf.Sin(2f * Mathf.PI * 170f * t) * Mathf.Exp(-t * 35f) * body;
                b[i] = attack * (lp * 0.9f + ring + thump) * gain;
            }
            return Normalize(b, 0.9f * gain);
        }

        /// <summary>Tiếng chạm mềm (lưới, cầu rơi): nhiễu lọc thấp + sin tần số thấp.</summary>
        static float[] Thud(System.Random r, float len, float freq, float gain)
        {
            var b = Buffer(len);
            float lp = 0f;
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)Rate;
                lp += (Noise(r) - lp) * 0.12f;
                b[i] = (lp * 1.5f + Mathf.Sin(2f * Mathf.PI * freq * t) * 0.6f) * Mathf.Exp(-t * 40f) * Mathf.Clamp01(t / 0.002f);
            }
            return Normalize(b, gain);
        }

        /// <summary>Giày rít: sóng sin quét tần số + rung (vibrato) + hài bậc 2.</summary>
        static float[] Squeak(float len, float f0, float f1, float vibrato)
        {
            var b = Buffer(len);
            double phase = 0;
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)Rate, u = t / len;
                float f = Mathf.Lerp(f0, f1, u) * (1f + 0.04f * Mathf.Sin(2f * Mathf.PI * vibrato * t));
                phase += 2.0 * Math.PI * f / Rate;
                float env = Mathf.Sin(Mathf.PI * u) * (0.7f + 0.3f * Mathf.Sin(2f * Mathf.PI * 70f * t));
                b[i] = ((float)Math.Sin(phase) + 0.3f * (float)Math.Sin(phase * 2)) * env;
            }
            return Normalize(b, 0.55f);
        }

        /// <summary>
        /// Tiếng ồn khán giả nền: nhiều "đám đông" nhiễu lọc thấp, biên độ lên xuống chậm ngẫu nhiên.
        /// Cuối đoạn được trộn chéo với đầu đoạn để lặp (loop) không bị giật.
        /// </summary>
        static float[] CrowdLoop(System.Random r, float len)
        {
            var b = Buffer(len);
            const int layers = 6;
            var lp = new float[layers];
            var amp = new float[layers];
            var target = new float[layers];
            for (int i = 0; i < b.Length; i++)
            {
                float sum = 0f;
                for (int k = 0; k < layers; k++)
                {
                    if (i % 2205 == 0) target[k] = 0.3f + (float)r.NextDouble() * 0.7f; // đổi biên độ mỗi 0.1 s
                    amp[k] += (target[k] - amp[k]) * 0.0004f;
                    float cutoff = 0.04f + k * 0.025f;
                    lp[k] += (Noise(r) - lp[k]) * cutoff;
                    sum += lp[k] * amp[k];
                }
                b[i] = sum;
            }
            // Crossfade 1 giây cuối vào đầu ⇒ loop liền mạch.
            int fade = Rate;
            for (int i = 0; i < fade; i++)
            {
                float w = i / (float)fade;
                b[i] = b[i] * w + b[b.Length - fade + i] * (1f - w);
            }
            Array.Resize(ref b, b.Length - fade);
            return Normalize(b, 0.6f);
        }

        /// <summary>Reo hò: nhiễu dải trung lớn dần nhanh rồi tắt chậm, kèm vài tiếng "ồ" (tông trượt).</summary>
        static float[] Cheer(System.Random r, float len)
        {
            var b = Buffer(len);
            float lp = 0f, hp = 0f;
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)Rate;
                float env = Mathf.Clamp01(t / 0.25f) * Mathf.Exp(-Mathf.Max(0f, t - 0.4f) * 1.4f);
                float n = Noise(r);
                lp += (n - lp) * 0.3f;
                hp = lp - hp * 0.05f; // dải trung
                float voices = 0f;
                for (int v = 0; v < 5; v++)
                {
                    float f = 220f + v * 55f + Mathf.Sin(t * (2 + v)) * 30f;
                    voices += Mathf.Sin(2f * Mathf.PI * f * t + v) * 0.08f;
                }
                b[i] = (hp * 0.9f + voices) * env;
            }
            return Normalize(b, 0.8f);
        }

        /// <summary>Vỗ tay: rất nhiều tiếng "bép" ngắn ngẫu nhiên, mật độ giảm dần.</summary>
        static float[] Applause(System.Random r, float len)
        {
            var b = Buffer(len);
            int claps = (int)(len * 180);
            for (int c = 0; c < claps; c++)
            {
                // Mật độ tiếng vỗ giảm dần về cuối.
                float when = (float)Math.Pow(r.NextDouble(), 1.6) * len;
                int start = (int)(when * Rate);
                int dur = (int)(0.012f * Rate);
                float g = 0.3f + (float)r.NextDouble() * 0.7f;
                float lp = 0f;
                for (int j = 0; j < dur && start + j < b.Length; j++)
                {
                    lp += (Noise(r) - lp) * 0.6f;
                    b[start + j] += lp * g * Mathf.Exp(-j / (float)dur * 5f);
                }
            }
            return Normalize(b, 0.7f);
        }

        static float[] Blip(float len, float f)
        {
            var b = Buffer(len);
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)Rate;
                b[i] = Mathf.Sin(2f * Mathf.PI * f * t) * Mathf.Exp(-t * 60f);
            }
            return Normalize(b, 0.5f);
        }

        /// <summary>Còi trọng tài: 2 tông gần nhau (tạo tiếng "rè") + rung biên độ.</summary>
        static float[] Whistle(System.Random r, float len)
        {
            var b = Buffer(len);
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)Rate;
                float env = Mathf.Clamp01(t / 0.02f) * Mathf.Clamp01((len - t) / 0.05f);
                float trill = 0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * 32f * t);
                b[i] = (Mathf.Sin(2f * Mathf.PI * 2850f * t) + Mathf.Sin(2f * Mathf.PI * 2930f * t) * 0.7f + Noise(r) * 0.05f) * env * trill;
            }
            return Normalize(b, 0.4f);
        }

        static float[] Normalize(float[] b, float peak)
        {
            float m = 1e-6f;
            foreach (var x in b) m = Mathf.Max(m, Mathf.Abs(x));
            float k = peak / m;
            for (int i = 0; i < b.Length; i++) b[i] *= k;
            return b;
        }

        static void WriteWav(string path, float[] samples)
        {
            using var fs = new FileStream(path, FileMode.Create);
            using var w = new BinaryWriter(fs);
            int dataLen = samples.Length * 2;
            w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + dataLen);
            w.Write(new[] { 'W', 'A', 'V', 'E' });
            w.Write(new[] { 'f', 'm', 't', ' ' }); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(dataLen);
            foreach (var s in samples) w.Write((short)(Mathf.Clamp(s, -1f, 1f) * 32767f));
        }
    }
}
