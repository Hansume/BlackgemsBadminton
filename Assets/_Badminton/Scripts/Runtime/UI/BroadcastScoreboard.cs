using Badminton.Scoring;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using F = Badminton.UI.UIFactory;

namespace Badminton.UI
{
    /// <summary>
    /// Bảng tỷ số góc trên-trái kiểu truyền hình:
    ///  [logo giải (tên giả) + số game] | [vạch giao cầu] [cờ màu] [TÊN] [game thắng] [ĐIỂM]
    /// Vạch màu vàng bên trái đánh dấu người đang giao cầu; điểm "nảy" lên khi thay đổi.
    /// </summary>
    public class BroadcastScoreboard : MonoBehaviour
    {
        public MatchController match;

        class Row
        {
            public Image serveBar, flagA, flagB, flagC;
            public RectTransform flagHolder;   // cờ thật (vẽ lại khi đổi quốc gia)
            public string flagShown;
            public TextMeshProUGUI name, games, points;
            public int lastPoints = -1;
            public float pop;
        }

        readonly Row[] _rows = new Row[2];
        TextMeshProUGUI _gameLabel, _logoName, _stripText;
        GameObject _strip;
        string _logoShown;

        /// <summary>Chế độ Giải đấu: tên giải hư cấu (logo) và dòng "CẤP GIẢI · TÊN GIẢI · VÒNG". Null = mặc định.</summary>
        public static string HeaderLogo, HeaderLine;

        const float RowH = 56f;

        public static BroadcastScoreboard Build(Transform canvas, MatchController match)
        {
            var T = F.Theme;
            var root = F.Rect("Broadcast Scoreboard", canvas);
            F.Place(root, new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -36), new Vector2(620, RowH * 2 + 4));
            var sb = root.gameObject.AddComponent<BroadcastScoreboard>();
            sb.match = match;

            // Khối logo giải đấu (tên giả).
            var logo = F.Image(root, "Logo", T.navy);
            F.Place(logo.rectTransform, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, new Vector2(118, RowH * 2 + 4));
            var stripe = F.Image(logo.transform, "Stripe", T.accent);
            F.Place(stripe.rectTransform, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, new Vector2(118, 6));
            var name = F.Label(logo.transform, T.tournamentName.Replace(" ", "\n"), 24, T.text, TextAlignmentOptions.Center, T.titleFont);
            name.textWrappingMode = TextWrappingModes.Normal;
            name.enableAutoSizing = true; name.fontSizeMin = 14; name.fontSizeMax = 24;
            sb._logoName = name;
            F.Place(name.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -14), new Vector2(110, 60));
            sb._gameLabel = F.Label(logo.transform, "GAME 1", 22, T.accent, TextAlignmentOptions.Center, T.headingFont);
            F.Place(sb._gameLabel.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 8), new Vector2(110, 30));

            for (int i = 0; i < 2; i++)
            {
                var row = new Row();
                float y = -i * (RowH + 4);
                var bg = F.Image(root, "Row " + i, T.navy);
                F.Place(bg.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(122, y), new Vector2(498, RowH));

                row.serveBar = F.Image(bg.transform, "Serve", T.accent);
                F.Place(row.serveBar.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(8, RowH));

                // Cờ giả: 3 sọc dọc theo màu áo người chơi.
                row.flagA = F.Image(bg.transform, "FlagA", Color.white);
                F.Place(row.flagA.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(20, 0), new Vector2(13, 26));
                row.flagB = F.Image(bg.transform, "FlagB", Color.white);
                F.Place(row.flagB.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(33, 0), new Vector2(13, 26));
                row.flagC = F.Image(bg.transform, "FlagC", Color.white);
                F.Place(row.flagC.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(46, 0), new Vector2(13, 26));
                row.flagHolder = F.Rect("Flag", bg.transform);
                F.Place(row.flagHolder, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(18, 0), new Vector2(42, 28));

                row.name = F.Label(bg.transform, "PLAYER", 30, T.text, TextAlignmentOptions.Left, T.headingFont);
                row.name.overflowMode = TextOverflowModes.Ellipsis; // tên dài thì cắt "..."
                F.Place(row.name.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(72, 0), new Vector2(270, RowH));

                var gbox = F.Image(bg.transform, "Games", T.navyLight);
                F.Place(gbox.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-74, 0), new Vector2(54, RowH));
                row.games = F.Label(gbox.transform, "0", 30, T.textDim, TextAlignmentOptions.Center, T.headingFont);
                F.Stretch(row.games.rectTransform);

                var pbox = F.Image(bg.transform, "Points", Color.white);
                F.Place(pbox.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), Vector2.zero, new Vector2(72, RowH));
                row.points = F.Label(pbox.transform, "0", 40, T.navy, TextAlignmentOptions.Center, T.titleFont);
                F.Stretch(row.points.rectTransform);

                sb._rows[i] = row;
            }

            // Dải thông tin giải đấu (tên giải + vòng) ngay dưới bảng tỷ số.
            var strip = F.Image(root, "Tournament Strip", T.accent);
            F.Place(strip.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -(RowH * 2 + 8)), new Vector2(620, 38));
            sb._stripText = F.Label(strip.transform, "", 24, T.navy, TextAlignmentOptions.Left, T.headingFont);
            sb._stripText.overflowMode = TextOverflowModes.Truncate;
            F.Stretch(sb._stripText.rectTransform, 14, 0, 10, 0);
            sb._strip = strip.gameObject;
            sb._strip.SetActive(false);
            return sb;
        }

        void Update()
        {
            var m = match != null ? match.Match : null;
            for (int i = 0; i < 2; i++)
            {
                var row = _rows[i];
                var p = match.players[i];
                row.name.text = p.name.ToUpperInvariant();
                Color c = p.color;
                // Cờ thật theo mã quốc gia; không có mã (vd. đối thủ online bản cũ) ⇒ 3 sọc theo màu áo.
                string code = string.IsNullOrEmpty(p.flagCode) ? null : p.flagCode;
                if (code != row.flagShown)
                {
                    row.flagShown = code;
                    for (int k = row.flagHolder.childCount - 1; k >= 0; k--) Destroy(row.flagHolder.GetChild(k).gameObject);
                    if (code != null) F.Stretch(TournamentMode.TourWidgets.Flag(row.flagHolder, code, 42, 28));
                    row.flagA.enabled = row.flagB.enabled = row.flagC.enabled = code == null;
                }
                if (code == null)
                {
                    row.flagA.color = c;
                    row.flagB.color = Color.white;
                    row.flagC.color = Color.Lerp(c, Color.black, 0.45f);
                }

                int pts = m != null ? m.Score[i] : 0;
                row.points.text = pts.ToString();
                row.games.text = m != null ? m.Games[i].ToString() : "0";
                row.serveBar.enabled = m != null && m.Server == i && !m.IsOver;

                if (pts != row.lastPoints)
                {
                    if (row.lastPoints >= 0) row.pop = 1f;
                    row.lastPoints = pts;
                }
                row.pop = Mathf.MoveTowards(row.pop, 0f, Time.unscaledDeltaTime * 4f);
                row.points.transform.localScale = Vector3.one * (1f + 0.35f * row.pop * row.pop);
            }
            if (m != null) _gameLabel.text = m.IsOver ? "KẾT THÚC" : $"GAME {m.GameNumber}";

            string logo = HeaderLogo ?? F.Theme.tournamentName;
            if (logo != _logoShown) { _logoShown = logo; _logoName.text = logo.Replace(" ", "\n"); }
            bool strip = !string.IsNullOrEmpty(HeaderLine);
            if (_strip.activeSelf != strip) _strip.SetActive(strip);
            if (strip) _stripText.text = HeaderLine;
        }
    }
}
