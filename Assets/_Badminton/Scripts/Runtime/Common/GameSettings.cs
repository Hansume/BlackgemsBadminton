using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Badminton
{
    /// <summary>
    /// Cài đặt người chơi (âm lượng, chất lượng đồ hoạ, tên hiển thị), lưu PlayerPrefs.
    /// Trên WebGL PlayerPrefs được lưu vào IndexedDB của trình duyệt.
    /// </summary>
    public static class GameSettings
    {
        public enum Quality { Low, High }

        public static float MasterVolume { get; private set; } = 0.8f;
        public static float SfxVolume { get; private set; } = 1f;
        public static float CrowdVolume { get; private set; } = 0.6f;
        public static Quality GraphicsQuality { get; private set; } = Quality.High;
        public static bool SmashReplay { get; private set; } = true;
        public static string PlayerName { get; private set; } = "Người chơi";

        public static event Action Changed;

        static bool _loaded;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            MasterVolume = PlayerPrefs.GetFloat("bm.vol.master", MasterVolume);
            SfxVolume = PlayerPrefs.GetFloat("bm.vol.sfx", SfxVolume);
            CrowdVolume = PlayerPrefs.GetFloat("bm.vol.crowd", CrowdVolume);
            GraphicsQuality = (Quality)PlayerPrefs.GetInt("bm.quality", (int)GraphicsQuality);
            SmashReplay = PlayerPrefs.GetInt("bm.replay", 1) == 1;
            PlayerName = PlayerPrefs.GetString("bm.name", "Người chơi " + UnityEngine.Random.Range(100, 999));
            ApplyQuality();
            AudioListener.volume = MasterVolume;
        }

        public static void SetMaster(float v) { MasterVolume = v; AudioListener.volume = v; Save(); }
        public static void SetSfx(float v) { SfxVolume = v; Save(); }
        public static void SetCrowd(float v) { CrowdVolume = v; Save(); }
        public static void SetReplay(bool on) { SmashReplay = on; Save(); }

        public static void SetPlayerName(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0) return;
            PlayerName = name.Length > 16 ? name.Substring(0, 16) : name;
            Save();
        }

        public static void SetQuality(Quality q) { GraphicsQuality = q; ApplyQuality(); Save(); }

        /// <summary>
        /// Thấp: render scale 0.7 (vẽ ít pixel hơn – nhẹ cho máy yếu / màn hình HiDPI). Cao: render scale 1.
        /// </summary>
        static void ApplyQuality()
        {
            // Trong Editor, sửa URP asset lúc Play sẽ ghi thẳng vào file asset ⇒ chỉ áp dụng ở bản build.
            if (Application.isEditor) return;
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                urp.renderScale = GraphicsQuality == Quality.High ? 1f : 0.7f;
                urp.msaaSampleCount = GraphicsQuality == Quality.High ? 2 : 1;
            }
        }

        static void Save()
        {
            try
            {
                PlayerPrefs.SetFloat("bm.vol.master", MasterVolume);
                PlayerPrefs.SetFloat("bm.vol.sfx", SfxVolume);
                PlayerPrefs.SetFloat("bm.vol.crowd", CrowdVolume);
                PlayerPrefs.SetInt("bm.quality", (int)GraphicsQuality);
                PlayerPrefs.SetInt("bm.replay", SmashReplay ? 1 : 0);
                PlayerPrefs.SetString("bm.name", PlayerName);
                PlayerPrefs.Save();
            }
            catch (Exception e) { Debug.LogWarning("[GameSettings] Không lưu được cài đặt: " + e.Message); }
            Changed?.Invoke();
        }
    }
}
