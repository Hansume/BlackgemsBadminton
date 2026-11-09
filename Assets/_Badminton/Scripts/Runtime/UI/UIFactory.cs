using System;
using Badminton.Audio;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Badminton.UI
{
    /// <summary>
    /// Dựng uGUI bằng code (không cần prefab): panel, chữ, nút, thanh trượt, ô nhập.
    /// Mọi kích thước tính theo canvas tham chiếu 1920×1080.
    /// </summary>
    public static class UIFactory
    {
        public static UITheme Theme;

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static RectTransform Stretch(RectTransform r, float left = 0, float bottom = 0, float right = 0, float top = 0)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(left, bottom);
            r.offsetMax = new Vector2(-right, -top);
            return r;
        }

        /// <summary>Đặt theo neo (anchor) + pivot + vị trí + kích thước.</summary>
        public static RectTransform Place(RectTransform r, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            r.anchorMin = r.anchorMax = anchor;
            r.pivot = pivot;
            r.anchoredPosition = pos;
            r.sizeDelta = size;
            return r;
        }

        public static Image Image(Transform parent, string name, Color c)
        {
            var r = Rect(name, parent);
            var img = r.gameObject.AddComponent<Image>();
            img.color = c;
            return img;
        }

        public static TextMeshProUGUI Label(Transform parent, string text, float size, Color color,
                                           TextAlignmentOptions align = TextAlignmentOptions.Center,
                                           TMP_FontAsset font = null)
        {
            var r = Rect("Text", parent);
            var t = r.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = font != null ? font : Theme.bodyFont;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.raycastTarget = false;
            t.overflowMode = TextOverflowModes.Overflow;
            return t;
        }

        public static LayoutElement Size(Component c, float w, float h)
        {
            var le = c.GetComponent<LayoutElement>();
            if (le == null) le = c.gameObject.AddComponent<LayoutElement>();
            if (w >= 0) le.preferredWidth = w;
            if (h >= 0) le.preferredHeight = h;
            return le;
        }

        public static RectTransform VStack(Transform parent, string name, float spacing, TextAnchor align = TextAnchor.MiddleCenter)
        {
            var r = Rect(name, parent);
            var g = r.gameObject.AddComponent<VerticalLayoutGroup>();
            g.spacing = spacing;
            g.childAlignment = align;
            g.childControlWidth = g.childControlHeight = true;
            g.childForceExpandWidth = g.childForceExpandHeight = false;
            var f = r.gameObject.AddComponent<ContentSizeFitter>();
            f.verticalFit = f.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            return r;
        }

        public static RectTransform HStack(Transform parent, string name, float spacing, TextAnchor align = TextAnchor.MiddleCenter)
        {
            var r = Rect(name, parent);
            var g = r.gameObject.AddComponent<HorizontalLayoutGroup>();
            g.spacing = spacing;
            g.childAlignment = align;
            g.childControlWidth = g.childControlHeight = true;
            g.childForceExpandWidth = g.childForceExpandHeight = false;
            var f = r.gameObject.AddComponent<ContentSizeFitter>();
            f.verticalFit = f.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            return r;
        }

        public static Button Button(Transform parent, string label, Action onClick, float width = 460, float height = 76,
                                    bool primary = false, float fontSize = 34)
        {
            var img = Image(parent, "Button " + label, primary ? Theme.accent : Theme.navyLight);
            var btn = img.gameObject.AddComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f);
            colors.selectedColor = new Color(1.1f, 1.1f, 1.1f);
            colors.colorMultiplier = 1.4f;
            btn.colors = colors;
            btn.onClick.AddListener(() => { AudioManager.Instance?.Click(); onClick?.Invoke(); });

            // Vạch nhấn bên trái kiểu đồ hoạ truyền hình.
            var bar = Image(img.transform, "Accent", primary ? Theme.accentDark : Theme.accent);
            Place(bar.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(8, height));
            bar.raycastTarget = false;

            var t = Label(img.transform, label, fontSize, primary ? Theme.navy : Theme.text, TextAlignmentOptions.Center, Theme.headingFont);
            Stretch(t.rectTransform, 12, 0, 12, 0);
            Size(img, width, height);
            ((RectTransform)img.transform).sizeDelta = new Vector2(width, height);
            return btn;
        }

        public static void SetButtonLabel(Button b, string text) => b.GetComponentInChildren<TextMeshProUGUI>().text = text;

        public static Slider Slider(Transform parent, float value, Action<float> onChange, float width = 420)
        {
            var root = Rect("Slider", parent);
            root.sizeDelta = new Vector2(width, 36);
            Size(root, width, 36);
            var s = root.gameObject.AddComponent<Slider>();

            var bg = Image(root, "Background", new Color(0, 0, 0, 0.45f));
            Stretch(bg.rectTransform, 0, 12, 0, 12);

            var fillArea = Stretch(Rect("Fill Area", root), 6, 12, 18, 12);
            var fill = Image(fillArea, "Fill", Theme.accent);
            fill.rectTransform.sizeDelta = Vector2.zero;

            var handleArea = Stretch(Rect("Handle Area", root), 12, 0, 12, 0);
            var handle = Image(handleArea, "Handle", Color.white);
            handle.rectTransform.sizeDelta = new Vector2(24, 0);

            s.fillRect = fill.rectTransform;
            s.handleRect = handle.rectTransform;
            s.targetGraphic = handle;
            s.minValue = 0; s.maxValue = 1;
            s.value = value;
            s.onValueChanged.AddListener(v => onChange?.Invoke(v));
            return s;
        }

        public static TMP_InputField Input(Transform parent, string placeholder, string value, int limit, float width = 460, float height = 70)
        {
            var bg = Image(parent, "Input", new Color(0, 0, 0, 0.55f));
            Size(bg, width, height);
            bg.rectTransform.sizeDelta = new Vector2(width, height);

            var area = Stretch(Rect("Text Area", bg.transform), 18, 6, 18, 6);
            area.gameObject.AddComponent<RectMask2D>();

            var ph = Label(area, placeholder, 30, Theme.textDim, TextAlignmentOptions.Left);
            ph.fontStyle = FontStyles.Italic;
            Stretch(ph.rectTransform);
            var txt = Label(area, "", 32, Theme.text, TextAlignmentOptions.Left);
            Stretch(txt.rectTransform);
            txt.richText = false;

            var input = bg.gameObject.AddComponent<TMP_InputField>();
            input.textViewport = area;
            input.textComponent = txt;
            input.placeholder = ph;
            input.fontAsset = Theme.bodyFont;
            input.characterLimit = limit;
            input.text = value ?? "";
            input.caretColor = Theme.accent;
            input.selectionColor = new Color(1f, 0.8f, 0.2f, 0.35f);
            return input;
        }

        /// <summary>Hai nút bật/tắt dạng chọn một (ví dụ: Thấp / Cao).</summary>
        public static Button[] Segmented(Transform parent, string[] options, int selected, Action<int> onSelect, float width = 200)
        {
            var row = HStack(parent, "Segmented", 8);
            var buttons = new Button[options.Length];
            for (int i = 0; i < options.Length; i++)
            {
                int idx = i;
                buttons[i] = Button(row, options[i], null, width, 60, i == selected, 28);
                buttons[i].onClick.AddListener(() =>
                {
                    for (int k = 0; k < buttons.Length; k++) SetPrimary(buttons[k], k == idx);
                    onSelect?.Invoke(idx);
                });
            }
            return buttons;
        }

        public static void SetPrimary(Button b, bool primary)
        {
            b.GetComponent<Image>().color = primary ? Theme.accent : Theme.navyLight;
            b.GetComponentInChildren<TextMeshProUGUI>().color = primary ? Theme.navy : Theme.text;
        }
    }
}
