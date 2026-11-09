using System;
using UnityEngine;

namespace Badminton
{
    /// <summary>
    /// Nhân vật của người chơi (chỉnh ở menu chính → "NHÂN VẬT"), lưu PlayerPrefs (WebGL: IndexedDB).
    /// Dùng cho chế độ Giải đấu (ngoại hình + quốc tịch + giới tính) và trận "Đấu với AI".
    /// </summary>
    public static class PlayerProfile
    {
        /// <summary>15 quốc tịch có thể chọn (mã trong Badminton.Tour.Countries).</summary>
        public static readonly string[] CountryCodes =
            { "VIE", "CHN", "JPN", "KOR", "INA", "MAS", "THA", "IND", "SGP", "DEN", "ENG", "FRA", "GER", "ESP", "USA" };

        public static readonly string[] MaleHair = { "ĐẦU TRÒN", "CẮT SÁT", "MÁI DỰNG" };
        public static readonly string[] FemaleHair = { "ĐUÔI NGỰA", "BÚI TÓC", "TÓC DÀI" };

        /// <summary>
        /// Bảng 10 màu DÙNG CHUNG cho áo, quần, vợt, giày:
        /// đỏ, cam, vàng, xanh lá, xanh ngọc, xanh dương, tím, hồng, trắng, đen.
        /// </summary>
        public static readonly Color32[] Palette =
        {
            new Color32(220, 40, 50, 255), new Color32(251, 133, 0, 255), new Color32(255, 200, 40, 255), new Color32(60, 190, 90, 255),
            new Color32(0, 190, 190, 255), new Color32(58, 120, 255, 255), new Color32(131, 56, 236, 255), new Color32(255, 0, 110, 255),
            new Color32(242, 242, 242, 255), new Color32(25, 25, 30, 255),
        };
        public static readonly string[] PaletteNames = { "ĐỎ", "CAM", "VÀNG", "XANH LÁ", "XANH NGỌC", "XANH DƯƠNG", "TÍM", "HỒNG", "TRẮNG", "ĐEN" };

        const int DefShirt = 5, DefShorts = 9, DefRacket = 0, DefShoe = 8;

        public static bool Female { get; private set; }
        public static int HairStyle { get; private set; }
        public static int Shirt { get; private set; } = DefShirt;
        public static int Shorts { get; private set; } = DefShorts;
        public static int Racket { get; private set; } = DefRacket;
        public static int Shoe { get; private set; } = DefShoe;
        public static string Country { get; private set; } = "VIE";

        public static event Action Changed;

        static bool _loaded;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            Female = PlayerPrefs.GetInt("bm.char.female", 0) == 1;
            HairStyle = Mathf.Clamp(PlayerPrefs.GetInt("bm.char.hair", 0), 0, 2);
            Country = PlayerPrefs.GetString("bm.char.country", "VIE");
            if (Array.IndexOf(CountryCodes, Country) < 0) Country = "VIE";

            if (PlayerPrefs.GetInt("bm.char.v", 1) >= 2)
            {
                Shirt = Idx(PlayerPrefs.GetInt("bm.char.shirt", DefShirt));
                Shorts = Idx(PlayerPrefs.GetInt("bm.char.shorts", DefShorts));
                Racket = Idx(PlayerPrefs.GetInt("bm.char.racket", DefRacket));
                Shoe = Idx(PlayerPrefs.GetInt("bm.char.shoe", DefShoe));
            }
            else if (PlayerPrefs.HasKey("bm.char.outfit"))
            {
                // Chuyển từ phiên bản cũ (áo+quần chung 1 màu, mỗi loại 1 bảng màu riêng) sang bảng màu chung.
                int oldIdx = Mathf.Clamp(PlayerPrefs.GetInt("bm.char.outfit", 5), 0, 9);
                Color oldShirt = OldOutfits[oldIdx];
                Shirt = Nearest(oldShirt);
                // Bản cũ: quần = tông sẫm của áo; áo trắng ⇒ quần xanh đậm. Màu sẫm ⇒ gần nhất là ĐEN.
                Color oldShorts = oldIdx == 9 ? new Color(0.12f, 0.16f, 0.3f) : Color.Lerp(oldShirt, Color.black, 0.55f);
                Shorts = Nearest(oldShorts);
                Racket = Nearest(OldRackets[Mathf.Clamp(PlayerPrefs.GetInt("bm.char.racket", 0), 0, 9)]);
                Shoe = Nearest(OldShoes[Mathf.Clamp(PlayerPrefs.GetInt("bm.char.shoe", 0), 0, 9)]);
                Save();
            }
        }

        static int Idx(int i) => Mathf.Clamp(i, 0, Palette.Length - 1);

        static int Nearest(Color c)
        {
            int best = 0; float bd = float.MaxValue;
            for (int i = 0; i < Palette.Length; i++)
            {
                Color p = Palette[i];
                float d = (p.r - c.r) * (p.r - c.r) + (p.g - c.g) * (p.g - c.g) + (p.b - c.b) * (p.b - c.b);
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        // Bảng màu của phiên bản cũ – chỉ dùng để chuyển dữ liệu.
        static readonly Color32[] OldOutfits =
        {
            new Color32(230, 57, 70, 255), new Color32(251, 133, 0, 255), new Color32(255, 200, 40, 255), new Color32(60, 190, 90, 255),
            new Color32(42, 157, 143, 255), new Color32(58, 134, 255, 255), new Color32(29, 53, 120, 255), new Color32(131, 56, 236, 255),
            new Color32(255, 0, 110, 255), new Color32(240, 240, 240, 255),
        };
        static readonly Color32[] OldRackets =
        {
            new Color32(217, 38, 38, 255), new Color32(20, 20, 24, 255), new Color32(240, 240, 240, 255), new Color32(255, 200, 40, 255),
            new Color32(58, 134, 255, 255), new Color32(60, 190, 90, 255), new Color32(251, 133, 0, 255), new Color32(131, 56, 236, 255),
            new Color32(255, 0, 110, 255), new Color32(0, 200, 200, 255),
        };
        static readonly Color32[] OldShoes =
        {
            new Color32(242, 242, 242, 255), new Color32(25, 25, 28, 255), new Color32(217, 38, 38, 255), new Color32(58, 134, 255, 255),
            new Color32(255, 200, 40, 255), new Color32(60, 190, 90, 255), new Color32(251, 133, 0, 255), new Color32(255, 0, 110, 255),
            new Color32(140, 140, 150, 255), new Color32(0, 200, 200, 255),
        };

        public static void SetFemale(bool f) { Female = f; Save(); }
        public static void SetHair(int i) { HairStyle = Mathf.Clamp(i, 0, 2); Save(); }
        public static void SetShirt(int i) { Shirt = Idx(i); Save(); }
        public static void SetShorts(int i) { Shorts = Idx(i); Save(); }
        public static void SetRacket(int i) { Racket = Idx(i); Save(); }
        public static void SetShoe(int i) { Shoe = Idx(i); Save(); }
        public static void SetCountry(string code) { if (Array.IndexOf(CountryCodes, code) >= 0) { Country = code; Save(); } }

        public static void ResetDefaults()
        {
            Female = false; HairStyle = 0; Shirt = DefShirt; Shorts = DefShorts; Racket = DefRacket; Shoe = DefShoe; Country = "VIE";
            Save();
        }

        public static Color ShirtColor => Palette[Shirt];
        public static Color ShortsColor => Palette[Shorts];
        public static Color RacketColor => Palette[Racket];
        public static Color ShoeColor => Palette[Shoe];

        static void Save()
        {
            try
            {
                PlayerPrefs.SetInt("bm.char.female", Female ? 1 : 0);
                PlayerPrefs.SetInt("bm.char.hair", HairStyle);
                PlayerPrefs.SetInt("bm.char.v", 2);
                PlayerPrefs.SetInt("bm.char.shirt", Shirt);
                PlayerPrefs.SetInt("bm.char.shorts", Shorts);
                PlayerPrefs.SetInt("bm.char.racket", Racket);
                PlayerPrefs.SetInt("bm.char.shoe", Shoe);
                PlayerPrefs.SetString("bm.char.country", Country);
                PlayerPrefs.Save();
            }
            catch (Exception e) { Debug.LogWarning("[PlayerProfile] Không lưu được: " + e.Message); }
            Changed?.Invoke();
        }
    }
}
