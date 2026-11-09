using System;
using Badminton.Tour;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using F = Badminton.UI.UIFactory;

namespace Badminton.TournamentMode
{
    /// <summary>Các thành phần UI dùng chung của chế độ Giải đấu (dựng bằng code như phần UI còn lại).</summary>
    public static class TourWidgets
    {
        // ───────────────────────────── CỜ ─────────────────────────────

        /// <summary>Cờ quốc gia dạng khối màu đơn giản (low-poly).</summary>
        public static RectTransform Flag(Transform parent, string code, float w, float h)
        {
            var spec = Countries.Get(code).Flag;
            var root = F.Rect("Flag " + code, parent);
            root.sizeDelta = new Vector2(w, h);
            F.Size(root, w, h);
            int n = spec.Stripes.Length;
            for (int i = 0; i < n; i++)
            {
                var img = F.Image(root, "S" + i, TournamentSession.Hex(spec.Stripes[i]));
                var r = img.rectTransform;
                if (spec.Vertical) { r.anchorMin = new Vector2((float)i / n, 0); r.anchorMax = new Vector2((float)(i + 1) / n, 1); }
                else { r.anchorMin = new Vector2(0, 1f - (float)(i + 1) / n); r.anchorMax = new Vector2(1, 1f - (float)i / n); }
                r.offsetMin = r.offsetMax = Vector2.zero;
                img.raycastTarget = false;
            }
            if (spec.Canton != null)
            {
                var c = F.Image(root, "Canton", TournamentSession.Hex(spec.Canton));
                c.rectTransform.anchorMin = new Vector2(0, 0.46f); c.rectTransform.anchorMax = new Vector2(0.42f, 1);
                c.rectTransform.offsetMin = c.rectTransform.offsetMax = Vector2.zero;
                c.raycastTarget = false;
            }
            if (spec.Cross != null)
            {
                float cx = spec.NordicCross ? 0.36f : 0.5f, t = 0.2f;
                var v = F.Image(root, "CrossV", TournamentSession.Hex(spec.Cross));
                v.rectTransform.anchorMin = new Vector2(cx - t * h / w / 2f, 0); v.rectTransform.anchorMax = new Vector2(cx + t * h / w / 2f, 1);
                v.rectTransform.offsetMin = v.rectTransform.offsetMax = Vector2.zero;
                var hz = F.Image(root, "CrossH", TournamentSession.Hex(spec.Cross));
                hz.rectTransform.anchorMin = new Vector2(0, 0.5f - t / 2f); hz.rectTransform.anchorMax = new Vector2(1, 0.5f + t / 2f);
                hz.rectTransform.offsetMin = hz.rectTransform.offsetMax = Vector2.zero;
                v.raycastTarget = hz.raycastTarget = false;
            }
            if (spec.CornerStars != null)
            {
                // Sao lớn ở góc trên-trái + 4 sao nhỏ xếp vòng cung bên phải (hình thoi – kiểu low-poly).
                Color sc = TournamentSession.Hex(spec.CornerStars);
                void Star(Vector2 anchor, float size)
                {
                    var st = F.Image(root, "Star", sc);
                    F.Place(st.rectTransform, anchor, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size));
                    st.rectTransform.localRotation = Quaternion.Euler(0, 0, 45f);
                    st.raycastTarget = false;
                }
                Star(new Vector2(0.17f, 0.72f), h * 0.26f);
                Star(new Vector2(0.33f, 0.90f), h * 0.08f);
                Star(new Vector2(0.39f, 0.78f), h * 0.08f);
                Star(new Vector2(0.39f, 0.62f), h * 0.08f);
                Star(new Vector2(0.33f, 0.50f), h * 0.08f);
            }
            if (spec.Dot != null && spec.Dot2 != null)
            {
                // Chấm 2 màu (trên/dưới) – hình vuông không xoay.
                float d = h * 0.46f;
                var top = F.Image(root, "DotTop", TournamentSession.Hex(spec.Dot));
                F.Place(top.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(d, d / 2f));
                var bot = F.Image(root, "DotBottom", TournamentSession.Hex(spec.Dot2));
                F.Place(bot.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(d, d / 2f));
                top.raycastTarget = bot.raycastTarget = false;
            }
            else if (spec.Dot != null)
            {
                var d = F.Image(root, "Dot", TournamentSession.Hex(spec.Dot));
                F.Place(d.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(h * 0.42f, h * 0.42f));
                d.rectTransform.localRotation = Quaternion.Euler(0, 0, 45f); // hình thoi – kiểu low-poly
                d.raycastTarget = false;
            }
            return root;
        }

        /// <summary>3 màu tiêu biểu của cờ (cho cờ mini trên bảng tỷ số).</summary>
        public static Color[] FlagColors3(string code)
        {
            var spec = Countries.Get(code).Flag;
            var s = spec.Stripes;
            if (spec.CornerStars != null) // sao ở góc trái ⇒ sọc vàng bên trái (khác cờ Việt Nam: sao ở giữa)
                return new[] { TournamentSession.Hex(spec.CornerStars), TournamentSession.Hex(s[0]), TournamentSession.Hex(s[0]) };
            string a = s[0], c = s[s.Length - 1];
            string b = spec.Dot ?? spec.CornerStars ?? spec.Cross ?? spec.Canton ?? (s.Length > 2 ? s[s.Length / 2] : s[0]);
            if (s.Length == 1) { c = spec.Dot2 ?? a; }
            return new[] { TournamentSession.Hex(a), TournamentSession.Hex(b), TournamentSession.Hex(c) };
        }

        // ───────────────────────────── CUỘN ─────────────────────────────

        /// <summary>Vùng cuộn dọc chứa danh sách; trả về content (có VerticalLayoutGroup).</summary>
        public static RectTransform VScroll(Transform parent, out ScrollRect sr, float spacing = 6f)
        {
            var root = F.Image(parent, "Scroll", new Color(0, 0, 0, 0.35f));
            sr = root.gameObject.AddComponent<ScrollRect>();
            var vp = F.Stretch(F.Rect("Viewport", root.transform), 8, 8, 8, 8);
            vp.gameObject.AddComponent<RectMask2D>();
            var vpImg = vp.gameObject.AddComponent<Image>(); vpImg.color = new Color(0, 0, 0, 0.001f); // nhận kéo
            var content = F.Rect("Content", vp);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1); content.anchoredPosition = Vector2.zero; content.sizeDelta = Vector2.zero;
            var g = content.gameObject.AddComponent<VerticalLayoutGroup>();
            g.spacing = spacing; g.childControlWidth = true; g.childControlHeight = true;
            g.childForceExpandWidth = true; g.childForceExpandHeight = false;
            var fit = content.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.viewport = vp; sr.content = content; sr.horizontal = false; sr.vertical = true;
            sr.scrollSensitivity = 45f; sr.movementType = ScrollRect.MovementType.Clamped;
            return content;
        }

        /// <summary>Vùng cuộn 2 chiều với content kích thước cố định (nhánh đấu).</summary>
        public static RectTransform FreeScroll(Transform parent, Vector2 contentSize, out ScrollRect sr)
        {
            var root = F.Image(parent, "Scroll", new Color(0, 0, 0, 0.35f));
            sr = root.gameObject.AddComponent<ScrollRect>();
            var vp = F.Stretch(F.Rect("Viewport", root.transform), 6, 6, 6, 6);
            vp.gameObject.AddComponent<RectMask2D>();
            var vpImg = vp.gameObject.AddComponent<Image>(); vpImg.color = new Color(0, 0, 0, 0.001f);
            var content = F.Rect("Content", vp);
            content.anchorMin = content.anchorMax = new Vector2(0, 1);
            content.pivot = new Vector2(0, 1);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = contentSize;
            sr.viewport = vp; sr.content = content; sr.horizontal = true; sr.vertical = true;
            sr.scrollSensitivity = 45f; sr.movementType = ScrollRect.MovementType.Clamped;
            return content;
        }

        // ───────────────────────────── HÀNG / Ô ─────────────────────────────

        /// <summary>Một hàng bấm được (nền tối, HStack bên trong).</summary>
        public static RectTransform Row(Transform parent, float height, Action onClick, Color? bg = null)
        {
            var img = F.Image(parent, "Row", bg ?? new Color(0.06f, 0.11f, 0.22f, 0.92f));
            F.Size(img, -1, height);
            if (onClick != null)
            {
                var b = img.gameObject.AddComponent<Button>();
                var cb = b.colors; cb.highlightedColor = new Color(1.25f, 1.25f, 1.25f); cb.pressedColor = new Color(0.8f, 0.8f, 0.8f);
                cb.colorMultiplier = 1.5f; b.colors = cb;
                b.onClick.AddListener(() => { Audio.AudioManager.Instance?.Click(); onClick(); });
            }
            var g = img.gameObject.AddComponent<HorizontalLayoutGroup>();
            g.spacing = 14; g.padding = new RectOffset(14, 14, 0, 0);
            g.childAlignment = TextAnchor.MiddleLeft;
            g.childControlWidth = g.childControlHeight = true;
            g.childForceExpandWidth = g.childForceExpandHeight = false;
            return img.rectTransform;
        }

        public static TextMeshProUGUI Cell(Transform row, string text, float width, float size = 26, Color? color = null,
                                           TextAlignmentOptions align = TextAlignmentOptions.Left, TMP_FontAsset font = null)
        {
            var t = F.Label(row, text, size, color ?? F.Theme.text, align, font);
            t.overflowMode = TextOverflowModes.Truncate;
            F.Size(t, width, -1);
            return t;
        }

        /// <summary>Nhãn cấp giải có màu.</summary>
        public static RectTransform TierTag(Transform parent, string text, Color c, float w = 190, float h = 36)
        {
            var img = F.Image(parent, "Tier", c);
            F.Size(img, w, h);
            img.rectTransform.sizeDelta = new Vector2(w, h);
            var t = F.Label(img.transform, text, 22, F.Theme.navy, TextAlignmentOptions.Center, F.Theme.headingFont);
            F.Stretch(t.rectTransform, 4, 0, 4, 0);
            t.enableAutoSizing = true; t.fontSizeMin = 12; t.fontSizeMax = 22;
            return img.rectTransform;
        }

        public static string Signed(int v) => v > 0 ? "+" + v : v.ToString();
    }

    /// <summary>Ngoại hình riêng của một VĐV (suy ra cố định từ dữ liệu VĐV).</summary>
    public struct AthleteLook
    {
        public Color shirt, shorts, skin, hair, band;
        public bool female;
        public int hairStyle;   // 0..2 (nam: đầu tròn / cắt sát / mái dựng; nữ: đuôi ngựa / búi / tóc dài)
        public float build;     // độ to ngang của thân (0.9 … 1.12)
        public float height;    // tỉ lệ thân trên (0.95 … 1.05)
        public bool headband;
        public bool gear;           // dùng màu vợt / giày riêng
        public Color racket, shoes;

        /// <summary>
        /// Đảm bảo 2 người cùng đội (cùng áo) nhìn khác nhau: khác kiểu tóc nếu cùng giới, dáng người lệch nhau rõ,
        /// màu tóc khác nếu trùng.
        /// </summary>
        public static void MakeDistinct(ref AthleteLook a, ref AthleteLook b)
        {
            if (a.female == b.female && a.hairStyle == b.hairStyle) b.hairStyle = (b.hairStyle + 1) % 3;
            if (Mathf.Abs(a.build - b.build) < 0.08f) b.build = a.build > 1.0f ? a.build - 0.12f : a.build + 0.12f;
            if (Mathf.Abs(a.height - b.height) < 0.03f) b.height = a.height > 1.0f ? a.height - 0.05f : a.height + 0.05f;
            float dh = Mathf.Abs(a.hair.r - b.hair.r) + Mathf.Abs(a.hair.g - b.hair.g) + Mathf.Abs(a.hair.b - b.hair.b);
            if (dh < 0.25f) b.hair = a.hair.grayscale < 0.3f ? new Color(0.55f, 0.38f, 0.2f) : new Color(0.08f, 0.07f, 0.06f);
            if (a.headband == b.headband) b.headband = !a.headband;
        }

        /// <summary>Ngoại hình nhân vật của người chơi (menu chính → NHÂN VẬT).</summary>
        public static AthleteLook FromProfile()
        {
            var c = Countries.Get(PlayerProfile.Country);
            return new AthleteLook
            {
                shirt = PlayerProfile.ShirtColor, shorts = PlayerProfile.ShortsColor,
                skin = TournamentSession.Hex(c.Skin[0]), hair = new Color(0.16f, 0.11f, 0.07f),
                female = PlayerProfile.Female, hairStyle = PlayerProfile.HairStyle,
                build = PlayerProfile.Female ? 0.94f : 1.02f, height = 1f, headband = false,
                gear = true, racket = PlayerProfile.RacketColor, shoes = PlayerProfile.ShoeColor,
            };
        }

        /// <summary>Tạo ngoại hình từ VĐV; áo/quần dùng màu đồng phục của đội.</summary>
        public static AthleteLook From(Athlete a, Color teamShirt, Color teamShorts)
        {
            uint h = (uint)(a.id * 2654435761u);
            return new AthleteLook
            {
                shirt = teamShirt, shorts = teamShorts,
                skin = TournamentSession.Rgb(a.skin), hair = TournamentSession.Rgb(a.hair),
                female = a.gender == Gender.Female,
                hairStyle = (int)(h % 3),
                build = 0.9f + ((h >> 4) % 23) / 100f,
                height = 0.95f + ((h >> 9) % 11) / 100f,
                headband = ((h >> 14) % 4) == 0,
                band = TournamentSession.Rgb(a.shirt) == teamShirt ? Color.white : TournamentSession.Rgb(a.shirt),
            };
        }
    }

    /// <summary>
    /// Ngoại hình nhân vật low-poly trong trận giải đấu: màu áo / quần / da (MaterialPropertyBlock – không sửa asset),
    /// kiểu tóc + màu tóc, băng đầu, dáng người. Tóc/băng đầu tạo lúc chạy từ khối cơ bản (không cần dựng lại scene).
    /// Reset() trả nhân vật về như cũ cho các chế độ khác.
    /// </summary>
    public static class PlayerAppearance
    {
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        const string HairRoot = "Tour Look";
        static readonly System.Collections.Generic.Dictionary<int, (Vector3 body, Vector3 torso)> _orig =
            new System.Collections.Generic.Dictionary<int, (Vector3, Vector3)>();

        public static void Apply(GameObject root, Color shirt, Color shorts, Color skin) =>
            Apply(root, new AthleteLook { shirt = shirt, shorts = shorts, skin = skin, hair = new Color(0.1f, 0.08f, 0.06f), build = 1f, height = 1f, hairStyle = -1 });

        public static void Apply(GameObject root, AthleteLook look)
        {
            var torso = root.transform.Find("Hips/Torso");
            var body = torso != null ? torso.Find("Body") : null;
            var head = torso != null ? torso.Find("Head") : null;

            // Dáng người: thân to/nhỏ, thân trên cao/thấp (lưu kích thước gốc để Reset).
            if (torso != null && body != null)
            {
                int id = root.GetInstanceID();
                if (!_orig.ContainsKey(id)) _orig[id] = (body.localScale, torso.localScale);
                var o = _orig[id];
                body.localScale = new Vector3(o.body.x * look.build, o.body.y, o.body.z * Mathf.Lerp(1f, look.build, 0.7f));
                torso.localScale = o.torso * look.height;
            }

            // Màu áo / quần / da.
            var mpb = new MaterialPropertyBlock();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r.transform.IsChildOf(root.transform) && r.transform.parent != null && r.transform.parent.name == HairRoot) continue;
                var m = r.sharedMaterial;
                if (m == null) continue;
                string n = m.name;
                Color c;
                if (n.StartsWith("Player_Blue") || n.StartsWith("Player_Yellow")) c = look.shirt;
                else if (n.StartsWith("Player_Shorts")) c = look.shorts;
                else if (n.StartsWith("Skin")) c = look.skin;
                else if (look.gear && n.StartsWith("Racket_Frame")) c = look.racket;
                else if (look.gear && n.StartsWith("Player_Shoes")) c = look.shoes;
                else continue;
                SetColor(r, mpb, c);
            }

            // Tóc + băng đầu.
            if (torso == null || head == null) return;
            var hairRoot = torso.Find(HairRoot);
            if (hairRoot != null) UnityEngine.Object.Destroy(hairRoot.gameObject);
            if (look.hairStyle < 0) return;
            var hr = new GameObject(HairRoot).transform;
            hr.SetParent(torso, false);
            var mat = head.GetComponent<Renderer>().sharedMaterial;
            Vector3 hc = head.localPosition; // tâm đầu (bán kính ~0.12)

            void Piece(PrimitiveType type, Vector3 pos, Vector3 scale, Color c, float rotX = 0f)
            {
                var go = GameObject.CreatePrimitive(type);
                UnityEngine.Object.Destroy(go.GetComponent<Collider>());
                go.name = "Hair";
                go.transform.SetParent(hr, false);
                go.transform.localPosition = hc + pos;
                go.transform.localRotation = Quaternion.Euler(rotX, 0f, 0f);
                go.transform.localScale = scale;
                var r = go.GetComponent<Renderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                SetColor(r, new MaterialPropertyBlock(), c);
            }

            Color hair = look.hair;
            if (!look.female)
            {
                switch (look.hairStyle)
                {
                    case 0: Piece(PrimitiveType.Sphere, new Vector3(0f, 0.045f, -0.02f), new Vector3(0.26f, 0.17f, 0.27f), hair); break;      // đầu tròn
                    case 1: Piece(PrimitiveType.Sphere, new Vector3(0f, 0.04f, -0.015f), new Vector3(0.252f, 0.12f, 0.258f), hair); break;    // cắt sát
                    default:
                        Piece(PrimitiveType.Sphere, new Vector3(0f, 0.045f, -0.02f), new Vector3(0.26f, 0.15f, 0.27f), hair);
                        Piece(PrimitiveType.Cube, new Vector3(0f, 0.115f, 0.045f), new Vector3(0.14f, 0.06f, 0.11f), hair, -20f);            // mái dựng
                        break;
                }
            }
            else
            {
                Piece(PrimitiveType.Sphere, new Vector3(0f, 0.045f, -0.02f), new Vector3(0.265f, 0.17f, 0.275f), hair);
                switch (look.hairStyle)
                {
                    case 0: Piece(PrimitiveType.Cube, new Vector3(0f, -0.02f, -0.15f), new Vector3(0.07f, 0.22f, 0.07f), hair, 18f); break; // đuôi ngựa
                    case 1: Piece(PrimitiveType.Sphere, new Vector3(0f, 0.07f, -0.12f), new Vector3(0.11f, 0.11f, 0.11f), hair); break;      // búi
                    default: Piece(PrimitiveType.Cube, new Vector3(0f, -0.07f, -0.085f), new Vector3(0.24f, 0.22f, 0.07f), hair); break;     // tóc dài
                }
            }
            if (look.headband)
                Piece(PrimitiveType.Cylinder, new Vector3(0f, 0.035f, -0.005f), new Vector3(0.255f, 0.018f, 0.262f), look.band);
        }

        static void SetColor(Renderer r, MaterialPropertyBlock mpb, Color c)
        {
            r.GetPropertyBlock(mpb);
            mpb.SetColor(BaseColor, c);
            mpb.SetColor(ColorId, c);
            r.SetPropertyBlock(mpb);
        }

        /// <summary>Trả nhân vật về như cũ (màu gốc, không tóc, dáng gốc).</summary>
        public static void Reset(GameObject root)
        {
            var torso = root.transform.Find("Hips/Torso");
            if (torso != null)
            {
                var hairRoot = torso.Find(HairRoot);
                if (hairRoot != null) UnityEngine.Object.Destroy(hairRoot.gameObject);
                if (_orig.TryGetValue(root.GetInstanceID(), out var o))
                {
                    var body = torso.Find("Body");
                    if (body != null) body.localScale = o.body;
                    torso.localScale = o.torso;
                }
            }
            foreach (var r in root.GetComponentsInChildren<Renderer>(true)) r.SetPropertyBlock(null);
        }
    }
}
