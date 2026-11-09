using System;
using System.Collections.Generic;
using Badminton.Player;
using Badminton.Tour;
using Badminton.TournamentMode;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using F = Badminton.UI.UIFactory;

namespace Badminton.UI
{
    /// <summary>
    /// Màn hình "NHÂN VẬT" (menu chính): chỉnh giới tính, kiểu tóc (3 kiểu/giới tính), màu quần áo / vợt / giày
    /// (10 màu mỗi loại), quốc tịch (15 nước). Có xem trước 3D: camera riêng quay nhân vật thật trên sân ra RenderTexture.
    /// Lưu ngay khi đổi (PlayerProfile → PlayerPrefs).
    /// Giao diện dựng MỘT LẦN khi mở; mỗi lần bấm chỉ cập nhật trạng thái chọn (không dựng lại ⇒ không nháy).
    /// </summary>
    public class CharacterEditorUI : MonoBehaviour
    {
        GameUI _host;
        RectTransform _root, _content;
        RenderTexture _rt;
        Camera _cam;
        RawImage _preview;

        // Các phần tử cần cập nhật khi đổi lựa chọn.
        Button[] _genderBtns, _hairBtns;
        readonly List<(Image border, Image inner)> _shirtSw = new List<(Image, Image)>();
        readonly List<(Image border, Image inner)> _shortsSw = new List<(Image, Image)>();
        readonly List<(Image border, Image inner)> _racketSw = new List<(Image, Image)>();
        readonly List<(Image border, Image inner)> _shoeSw = new List<(Image, Image)>();
        readonly List<(string code, Image bg, TextMeshProUGUI label)> _countryBtns = new List<(string, Image, TextMeshProUGUI)>();
        TextMeshProUGUI _caption;

        public bool IsOpen => _root != null && _root.gameObject.activeSelf;

        public void Init(GameUI host)
        {
            _host = host;
            _root = F.Stretch(F.Rect("Character Editor", host.UICanvas.transform));
            _root.gameObject.SetActive(false);
            GameControls.Pause.performed += OnEsc;
        }

        void OnDestroy()
        {
            GameControls.Pause.performed -= OnEsc;
            ReleasePreview();
        }

        void OnEsc(InputAction.CallbackContext _) { if (IsOpen) Close(); }

        public void Open()
        {
            _root.gameObject.SetActive(true);
            _root.SetAsLastSibling();
            _host.SetMenuTitle(false);
            _host.match.ResetToIdle(); // dừng trận AI nền: nhân vật đứng yên để xem trước
            SetupPreview();
            Build();
            Refresh();
        }

        public void Close()
        {
            if (!IsOpen) return;
            ReleasePreview();
            PlayerAppearance.Reset(_host.match.players[0].motor.gameObject);
            _root.gameObject.SetActive(false);
            if (_content != null) Destroy(_content.gameObject);
            _content = null;
            _host.SetMenuTitle(true);
            _host.RestartAttract();
            _host.ShowMainMenu();
        }

        // ───────────────────────────── XEM TRƯỚC 3D ─────────────────────────────

        void SetupPreview()
        {
            ReleasePreview();
            _rt = new RenderTexture(640, 800, 24) { name = "Character Preview" };
            var go = new GameObject("Character Preview Camera");
            _cam = go.AddComponent<Camera>();
            _cam.targetTexture = _rt;
            _cam.fieldOfView = 45f;
            _cam.depth = -20f;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.05f, 0.09f, 0.18f);
            PlacePreviewCamera();
        }

        void PlacePreviewCamera()
        {
            if (_cam == null) return;
            var m = _host.match.players[0].motor;
            Vector3 p = m.transform.position;
            // Nhân vật nhìn về lưới ⇒ camera đứng phía trước, chếch sang phải, thấy toàn thân.
            _cam.transform.position = p + m.Forward * 3.1f + m.Right * 0.8f + Vector3.up * 1.3f;
            _cam.transform.LookAt(p + Vector3.up * 0.9f);
        }

        void ReleasePreview()
        {
            if (_cam != null) Destroy(_cam.gameObject);
            _cam = null;
            if (_rt != null) { _rt.Release(); Destroy(_rt); }
            _rt = null;
        }

        void LateUpdate()
        {
            if (IsOpen) PlacePreviewCamera();
        }

        // ───────────────────────────── DỰNG GIAO DIỆN (1 LẦN) ─────────────────────────────

        void Build()
        {
            if (_content != null) Destroy(_content.gameObject);
            _shirtSw.Clear(); _shortsSw.Clear(); _racketSw.Clear(); _shoeSw.Clear(); _countryBtns.Clear();
            var T = F.Theme;
            _content = F.Stretch(F.Rect("Content", _root));
            var dim = F.Image(_content, "Dim", new Color(0.01f, 0.02f, 0.06f, 0.78f));
            F.Stretch(dim.rectTransform);

            var title = F.Label(_content, "NHÂN VẬT", 50, T.accent, TextAlignmentOptions.Center, T.titleFont);
            F.Place(title.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -24), new Vector2(900, 64));
            var sub = F.Label(_content, $"Tên: {GameSettings.PlayerName}  ·  dùng trong chế độ Giải đấu và Đấu với AI", 26, T.textDim,
                              TextAlignmentOptions.Center, T.headingFont);
            F.Place(sub.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -88), new Vector2(1400, 40));

            // Cột tuỳ chọn bên trái.
            var col = F.VStack(_content, "Options", 14, TextAnchor.UpperLeft);
            F.Place(col, new Vector2(0, 1), new Vector2(0, 1), new Vector2(70, -150), Vector2.zero);

            Row(col, "GIỚI TÍNH", r => _genderBtns = Choices(r, new[] { "NAM", "NỮ" }, i =>
            {
                PlayerProfile.SetFemale(i == 1);
                if (PlayerProfile.HairStyle > 2) PlayerProfile.SetHair(0);
                Refresh();
            }));
            Row(col, "KIỂU TÓC", r => _hairBtns = Choices(r, PlayerProfile.MaleHair, i => { PlayerProfile.SetHair(i); Refresh(); }));
            // 4 hàng dùng chung bảng 10 màu.
            Row(col, "MÀU ÁO", r => Swatches(r, PlayerProfile.Palette, _shirtSw, i => { PlayerProfile.SetShirt(i); Refresh(); }));
            Row(col, "MÀU QUẦN", r => Swatches(r, PlayerProfile.Palette, _shortsSw, i => { PlayerProfile.SetShorts(i); Refresh(); }));
            Row(col, "MÀU VỢT", r => Swatches(r, PlayerProfile.Palette, _racketSw, i => { PlayerProfile.SetRacket(i); Refresh(); }));
            Row(col, "MÀU GIÀY", r => Swatches(r, PlayerProfile.Palette, _shoeSw, i => { PlayerProfile.SetShoe(i); Refresh(); }));

            var nl = F.Label(col, "QUỐC TỊCH", 28, T.text, TextAlignmentOptions.Left, T.headingFont);
            F.Size(nl, 400, 44);
            var codes = PlayerProfile.CountryCodes;
            for (int row = 0; row < 3; row++)
            {
                var hr = F.HStack(col, "Countries " + row, 10, TextAnchor.MiddleLeft);
                for (int k = 0; k < 5; k++)
                {
                    int idx = row * 5 + k;
                    if (idx < codes.Length) CountryButton(hr, codes[idx]);
                }
            }

            // Khung xem trước bên phải.
            var frame = F.Image(_content, "Preview Frame", T.accent);
            F.Place(frame.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-70, -150), new Vector2(520, 650));
            _preview = new GameObject("Preview", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            _preview.transform.SetParent(frame.transform, false);
            F.Stretch(_preview.rectTransform, 5, 5, 5, 5);
            _preview.texture = _rt;
            _preview.uvRect = new Rect(0.0625f, 0f, 0.875f, 1f); // khung 520×650 ≈ tỉ lệ 0.8 của RT 640×800
            var capBg = F.Image(frame.transform, "CapBg", T.accent);
            F.Place(capBg.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 1), new Vector2(0, -2), new Vector2(520, 44));
            _caption = F.Label(capBg.transform, "", 26, T.navy, TextAlignmentOptions.Center, T.headingFont);
            F.Stretch(_caption.rectTransform);

            var bottom = F.HStack(_content, "Bottom", 18);
            F.Place(bottom, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 30), Vector2.zero);
            F.Button(bottom, "MẶC ĐỊNH", () => { PlayerProfile.ResetDefaults(); Refresh(); }, 300, 72);
            F.Button(bottom, "XONG", Close, 360, 72, true);
        }

        static void Row(RectTransform col, string label, Action<RectTransform> build)
        {
            var row = F.HStack(col, label, 18, TextAnchor.MiddleLeft);
            var l = F.Label(row, label, 28, F.Theme.text, TextAlignmentOptions.Left, F.Theme.headingFont);
            F.Size(l, 200, 66);
            build(row);
        }

        /// <summary>Nhóm nút chọn một (trạng thái chọn do Refresh() tô, không tự đổi màu khi bấm).</summary>
        static Button[] Choices(RectTransform row, string[] labels, Action<int> onSelect)
        {
            var g = F.HStack(row, "Choices", 8);
            var buttons = new Button[labels.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                int idx = i;
                buttons[i] = F.Button(g, labels[i], () => onSelect(idx), 200, 60, false, 28);
            }
            return buttons;
        }

        static void Swatches(RectTransform row, Color32[] colors, List<(Image, Image)> store, Action<int> set)
        {
            for (int i = 0; i < colors.Length; i++)
            {
                int idx = i;
                var border = F.Image(row, "Swatch " + i, new Color(1, 1, 1, 0.15f));
                F.Size(border, 62, 62);
                border.rectTransform.sizeDelta = new Vector2(62, 62);
                var inner = F.Image(border.transform, "Color", colors[i]);
                F.Stretch(inner.rectTransform, 4, 4, 4, 4);
                inner.raycastTarget = false;
                var b = border.gameObject.AddComponent<Button>();
                b.transition = Selectable.Transition.None; // không nháy màu khi bấm
                b.onClick.AddListener(() => { Audio.AudioManager.Instance?.Click(); set(idx); });
                store.Add((border, inner));
            }
        }

        void CountryButton(RectTransform row, string code)
        {
            var T = F.Theme;
            var bg = F.Image(row, "Country " + code, T.navyLight);
            F.Size(bg, 200, 62);
            bg.rectTransform.sizeDelta = new Vector2(200, 62);
            var b = bg.gameObject.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            b.onClick.AddListener(() => { Audio.AudioManager.Instance?.Click(); PlayerProfile.SetCountry(code); Refresh(); });
            var flag = TourWidgets.Flag(bg.transform, code, 48, 32);
            F.Place(flag, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(12, 0), new Vector2(48, 32));
            var name = F.Label(bg.transform, Countries.Get(code).Name, 22, T.text, TextAlignmentOptions.Left, T.headingFont);
            name.overflowMode = TextOverflowModes.Truncate;
            F.Place(name.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(70, 0), new Vector2(124, 40));
            _countryBtns.Add((code, bg, name));
        }

        // ───────────────────────────── CẬP NHẬT TRẠNG THÁI CHỌN ─────────────────────────────

        void Refresh()
        {
            var T = F.Theme;
            for (int i = 0; i < _genderBtns.Length; i++) F.SetPrimary(_genderBtns[i], (i == 1) == PlayerProfile.Female);
            var hairNames = PlayerProfile.Female ? PlayerProfile.FemaleHair : PlayerProfile.MaleHair;
            for (int i = 0; i < _hairBtns.Length; i++)
            {
                F.SetButtonLabel(_hairBtns[i], hairNames[i]);
                F.SetPrimary(_hairBtns[i], i == PlayerProfile.HairStyle);
            }
            MarkSwatches(_shirtSw, PlayerProfile.Shirt);
            MarkSwatches(_shortsSw, PlayerProfile.Shorts);
            MarkSwatches(_racketSw, PlayerProfile.Racket);
            MarkSwatches(_shoeSw, PlayerProfile.Shoe);
            foreach (var (code, bg, label) in _countryBtns)
            {
                bool sel = code == PlayerProfile.Country;
                bg.color = sel ? T.accent : T.navyLight;
                label.color = sel ? T.navy : T.text;
            }
            _caption.text = Countries.Get(PlayerProfile.Country).Name.ToUpperInvariant();
            PlayerAppearance.Apply(_host.match.players[0].motor.gameObject, AthleteLook.FromProfile());
        }

        static void MarkSwatches(List<(Image border, Image inner)> list, int selected)
        {
            for (int i = 0; i < list.Count; i++)
            {
                bool sel = i == selected;
                list[i].border.color = sel ? F.Theme.accent : new Color(1, 1, 1, 0.15f);
                float pad = sel ? 6 : 4;
                F.Stretch(list[i].inner.rectTransform, pad, pad, pad, pad);
            }
        }
    }
}
