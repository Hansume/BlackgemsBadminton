using TMPro;
using UnityEngine;

namespace Badminton.UI
{
    /// <summary>Font và màu sắc của giao diện kiểu truyền hình thể thao.</summary>
    [CreateAssetMenu(menuName = "Badminton/UI Theme", fileName = "UITheme")]
    public class UITheme : ScriptableObject
    {
        [Header("Font (TMP, dynamic – hỗ trợ tiếng Việt)")]
        public TMP_FontAsset titleFont;   // Barlow Condensed Bold
        public TMP_FontAsset headingFont; // Barlow Condensed SemiBold
        public TMP_FontAsset bodyFont;    // Barlow Medium

        [Header("Giải đấu (tên giả)")]
        public string gameTitle = "SHUTTLE ARENA";
        public string tournamentName = "AURORA OPEN";

        [Header("Màu")]
        public Color navy = new Color32(11, 26, 51, 240);
        public Color navyLight = new Color32(25, 48, 88, 240);
        public Color accent = new Color32(255, 200, 40, 255);
        public Color accentDark = new Color32(200, 140, 0, 255);
        public Color text = Color.white;
        public Color textDim = new Color32(170, 185, 210, 255);
        public Color danger = new Color32(220, 50, 60, 255);
        public Color good = new Color32(60, 200, 110, 255);
    }
}
