using System.Collections;
using System.Collections.Generic;
using Badminton.Audio;
using Badminton.Tour;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using F = Badminton.UI.UIFactory;
using W = Badminton.TournamentMode.TourWidgets;

namespace Badminton.TournamentMode
{
    public partial class TournamentUI
    {
        // ───────────── trạng thái màn hình nhánh đấu ─────────────
        class SlotView
        {
            public MatchSlot slot;
            public RectTransform root;
            public TextMeshProUGUI[] names = new TextMeshProUGUI[2];
            public TextMeshProUGUI[] scores = new TextMeshProUGUI[2];
            public TextMeshProUGUI status;
        }

        readonly Dictionary<int, SlotView> _views = new Dictionary<int, SlotView>();
        MatchResult _lastResult;
        Coroutine _flowRoutine;
        bool _revealing, _skipReveal;
        Button _actionBtn, _revealAllBtn;
        TextMeshProUGUI _statusText;
        RectTransform _bracketContent;
        ScrollRect _bracketScroll;
        float _zoom = 1f;
        bool _showQualifying;
        int _tabRoundCache = -1;

        const float BoxW = 390f, BoxH = 84f, ColGap = 64f, RowGap = 14f, HeaderH = 46f;

        void StopFlow()
        {
            if (_flowRoutine != null) StopCoroutine(_flowRoutine);
            _flowRoutine = null;
            _revealing = false;
            _skipReveal = false;
        }

        TourEngine Eng => _ses?.Tour;

        // ───────────────────────────── MÀN HÌNH NHÁNH / BẢNG ĐẤU ─────────────────────────────

        void BuildBracket(RectTransform r)
        {
            var eng = Eng;
            if (eng == null) { ResetTo(TScreen.Hub); return; }
            var T = F.Theme;
            var s = eng.S;
            if (!s.finished && !s.roundStarted) { eng.BeginRound(); _ses.Save(); }

            bool hasQual = s.rounds.Count > 0 && s.rounds[0].stage == TourStage.Qualifying;
            if (_tabRoundCache != s.currentRound || !hasQual)
            {
                _showQualifying = hasQual && !s.finished && s.Round.stage == TourStage.Qualifying;
                _tabRoundCache = s.currentRound;
            }

            string sub = $"{_ses.TierName(s)}  ·  {Events.Name(s.ev)}  ·  " + (s.finished ? "ĐÃ KẾT THÚC" : s.Round.name);
            var c = Page(r, s.name, sub, 120);
            _views.Clear();

            // Thanh công cụ: tab vòng loại / nhánh chính, zoom.
            var tools = F.HStack(c, "Tools", 12, TextAnchor.MiddleLeft);
            F.Place(tools, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, Vector2.zero);
            if (hasQual)
                F.Segmented(tools, new[] { "VÒNG LOẠI", "NHÁNH CHÍNH" }, _showQualifying ? 0 : 1,
                    i => { _showQualifying = i == 0; Rebuild(); }, 230);
            var zoomRow = F.HStack(c, "Zoom", 10);
            F.Place(zoomRow, new Vector2(1, 1), new Vector2(1, 1), Vector2.zero, Vector2.zero);
            F.Button(zoomRow, "-", () => SetZoom(_zoom - 0.15f), 64, 56, false, 34);
            F.Button(zoomRow, "+", () => SetZoom(_zoom + 0.15f), 64, 56, false, 34);
            var hint = F.Label(c, "Kéo để cuộn · +/- để phóng to/thu nhỏ", 22, T.textDim, TextAlignmentOptions.Right);
            F.Place(hint.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-150, -14), new Vector2(520, 30));

            if (s.format == TourFormat.Groups) BuildGroupsView(c);
            else
            {
                var rounds = new List<int>();
                for (int i = 0; i < s.rounds.Count; i++)
                    if ((s.rounds[i].stage == TourStage.Qualifying) == _showQualifying) rounds.Add(i);
                Vector2 size = BracketSize(rounds);
                _bracketContent = W.FreeScroll(c, size, out _bracketScroll);
                F.Stretch((RectTransform)_bracketContent.parent.parent, 0, 0, 0, 70);
                DrawBracket(_bracketContent, rounds, Vector2.zero);
                _bracketContent.localScale = Vector3.one * _zoom;
            }

            // Thanh dưới: trạng thái + nút hành động lớn.
            _statusText = F.Label(r, "", 28, T.text, TextAlignmentOptions.Left, T.headingFont);
            F.Place(_statusText.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(60, 30), new Vector2(900, 60));
            _statusText.textWrappingMode = TextWrappingModes.Normal;
            var bottom = F.HStack(r, "Actions", 16);
            F.Place(bottom, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-60, 26), Vector2.zero);
            _revealAllBtn = F.Button(bottom, "HIỆN TẤT CẢ", () => _skipReveal = true, 300, 76);
            _revealAllBtn.gameObject.SetActive(false);
            _actionBtn = F.Button(bottom, "VÀO TRẬN", () => { }, 420, 84, true, 38);
            _actionBtn.gameObject.SetActive(false);

            Canvas.ForceUpdateCanvases();
            ScrollToPlayer();
            _flowRoutine = StartCoroutine(BracketFlow());
        }

        void SetZoom(float z)
        {
            _zoom = Mathf.Clamp(z, 0.45f, 1.6f);
            if (_bracketContent != null) _bracketContent.localScale = Vector3.one * _zoom;
        }

        Vector2 BracketSize(List<int> rounds)
        {
            if (rounds.Count == 0) return new Vector2(400, 200);
            int n0 = 0;
            foreach (var _ in Eng.S.SlotsIn(rounds[0])) n0++;
            return new Vector2(rounds.Count * (BoxW + ColGap) + ColGap, HeaderH + n0 * (BoxH + RowGap) + RowGap);
        }

        /// <summary>Vẽ cây nhánh đấu các vòng `rounds` (mỗi vòng số trận giảm một nửa) tại gốc `origin` (trên-trái).</summary>
        void DrawBracket(RectTransform parent, List<int> rounds, Vector2 origin)
        {
            var T = F.Theme;
            var s = Eng.S;
            float[] prevY = null;
            for (int c = 0; c < rounds.Count; c++)
            {
                var slots = new List<MatchSlot>(s.SlotsIn(rounds[c]));
                slots.Sort((a, b) => a.index.CompareTo(b.index));
                float x = origin.x + ColGap + c * (BoxW + ColGap);
                var head = F.Label(parent, s.rounds[rounds[c]].name, 24,
                    rounds[c] == s.currentRound && !s.finished ? T.accent : T.textDim, TextAlignmentOptions.Center, T.headingFont);
                F.Place(head.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, origin.y - 8), new Vector2(BoxW, 32));

                var ys = new float[slots.Count];
                for (int k = 0; k < slots.Count; k++)
                {
                    ys[k] = c == 0 || prevY == null || prevY.Length < 2 * k + 2
                        ? origin.y - HeaderH - RowGap - k * (BoxH + RowGap) - BoxH / 2f
                        : (prevY[2 * k] + prevY[2 * k + 1]) / 2f;
                    if (c > 0 && prevY != null && prevY.Length >= 2 * k + 2)
                    {
                        float xl = x - ColGap, xm = x - ColGap / 2f;
                        bool path = Eng.IsPlayerMatch(slots[k]);
                        Color lc = path ? T.accent : new Color(1, 1, 1, 0.25f);
                        LineH(parent, xl, xm, prevY[2 * k], lc);
                        LineH(parent, xl, xm, prevY[2 * k + 1], lc);
                        LineV(parent, xm, prevY[2 * k], prevY[2 * k + 1], lc);
                        LineH(parent, xm, x, ys[k], lc);
                    }
                    var v = SlotBox(parent, slots[k], new Vector2(x, ys[k]));
                    _views[slots[k].id] = v;
                }
                prevY = ys;
            }
        }

        static void LineH(RectTransform p, float x0, float x1, float y, Color c)
        {
            var img = F.Image(p, "L", c);
            img.raycastTarget = false;
            F.Place(img.rectTransform, new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(Mathf.Min(x0, x1), y), new Vector2(Mathf.Abs(x1 - x0), 3));
        }

        static void LineV(RectTransform p, float x, float y0, float y1, Color c)
        {
            var img = F.Image(p, "L", c);
            img.raycastTarget = false;
            F.Place(img.rectTransform, new Vector2(0, 1), new Vector2(0.5f, 0), new Vector2(x, Mathf.Min(y0, y1)), new Vector2(3, Mathf.Abs(y1 - y0)));
        }

        /// <summary>Ô một trận trên nhánh: 2 dòng (cờ, hạt giống, tên, điểm từng game). y = tâm ô.</summary>
        SlotView SlotBox(RectTransform parent, MatchSlot m, Vector2 pos)
        {
            var T = F.Theme;
            bool mine = Eng.IsPlayerMatch(m);
            var border = F.Image(parent, "Match " + m.id, mine ? T.accent : new Color(1, 1, 1, 0.12f));
            F.Place(border.rectTransform, new Vector2(0, 1), new Vector2(0, 0.5f), pos, new Vector2(BoxW, BoxH));
            border.raycastTarget = false;
            var bg = F.Image(border.transform, "Bg", T.navy);
            F.Stretch(bg.rectTransform, 3, 3, 3, 3);
            bg.raycastTarget = false;

            var v = new SlotView { slot = m, root = border.rectTransform };
            for (int i = 0; i < 2; i++)
            {
                int e = i == 0 ? m.a : m.b;
                float y = i == 0 ? -BoxH * 0.27f : BoxH * 0.23f;
                var rowRt = F.Rect("Side" + i, bg.transform);
                F.Place(rowRt, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, -y + 0f), new Vector2(BoxW - 6, BoxH / 2f - 4));
                if (e >= 0)
                {
                    var fl = W.Flag(rowRt, _ses.World.Country(e), 30, 20);
                    F.Place(fl, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(10, 0), new Vector2(30, 20));
                }
                int seed = e >= 0 ? Eng.S.SeedOf(e) : 0;
                string label = e >= 0 ? _ses.World.ShortName(e) : e <= -2 ? "VƯỢT QUA VÒNG LOẠI " + (-e - 1) : "—";
                if (seed > 0) label = $"<color=#{ColorUtility.ToHtmlStringRGB(T.textDim)}>[{seed}]</color> " + label;
                var name = F.Label(rowRt, label, 22, e == _ses.PlayerEntry ? T.accent : T.text, TextAlignmentOptions.Left, T.bodyFont);
                name.overflowMode = TextOverflowModes.Truncate;
                F.Place(name.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(48, 0), new Vector2(205, 30));
                var sc = F.Label(rowRt, "", 22, T.text, TextAlignmentOptions.Right, T.headingFont);
                F.Place(sc.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-8, 0), new Vector2(120, 30));
                v.names[i] = name;
                v.scores[i] = sc;
            }
            v.status = F.Label(bg.transform, "", 20, T.accent, TextAlignmentOptions.Right, T.headingFont);
            F.Place(v.status.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-8, 0), new Vector2(140, 30));
            v.status.fontStyle = FontStyles.Italic;
            UpdateSlotView(v);
            return v;
        }

        void UpdateSlotView(SlotView v)
        {
            var T = F.Theme;
            var m = v.slot;
            bool show = m.hasResult && m.revealed;
            for (int i = 0; i < 2; i++)
            {
                v.scores[i].text = "";
                v.names[i].fontStyle = FontStyles.Normal;
            }
            if (show)
            {
                for (int i = 0; i < 2; i++)
                {
                    var sb = new System.Text.StringBuilder();
                    var mine = i == 0 ? m.result.a : m.result.b;
                    var theirs = i == 0 ? m.result.b : m.result.a;
                    for (int g = 0; g < mine.Length; g++)
                    {
                        if (g > 0) sb.Append("  ");
                        bool won = mine[g] > theirs[g];
                        sb.Append(won ? "<b>" : "<color=#AAB9D2>").Append(mine[g]).Append(won ? "</b>" : "</color>");
                    }
                    if (m.result.retired && m.result.winner != i) sb.Append(" BC");
                    v.scores[i].text = sb.ToString();
                }
                int w = m.result.winner;
                v.names[w].fontStyle = FontStyles.Bold;
                v.names[1 - w].color = new Color(v.names[1 - w].color.r, v.names[1 - w].color.g, v.names[1 - w].color.b, 0.55f);
                v.status.text = "";
            }
            else if (m.started && m.Ready) v.status.text = Eng.IsPlayerMatch(m) ? "TRẬN CỦA BẠN" : "ĐANG THI ĐẤU…";
            else v.status.text = m.Ready ? "CHƯA ĐẤU" : "";
        }

        void ScrollToPlayer()
        {
            if (_bracketScroll == null || _bracketContent == null) return;
            SlotView target = null;
            foreach (var v in _views.Values)
                if (Eng.IsPlayerMatch(v.slot) && (target == null || v.slot.round > target.slot.round)) target = v;
            if (target == null) return;
            var vp = (RectTransform)_bracketContent.parent;
            Vector2 p = target.root.anchoredPosition * _zoom;
            float x = Mathf.Clamp(p.x - vp.rect.width / 2f + BoxW / 2f, 0, Mathf.Max(0, _bracketContent.sizeDelta.x * _zoom - vp.rect.width));
            float y = Mathf.Clamp(-p.y - vp.rect.height / 2f, 0, Mathf.Max(0, _bracketContent.sizeDelta.y * _zoom - vp.rect.height));
            _bracketContent.anchoredPosition = new Vector2(-x, y);
        }

        // ───────────────────────────── VÒNG BẢNG ─────────────────────────────

        void BuildGroupsView(RectTransform c)
        {
            var T = F.Theme;
            var s = Eng.S;
            var list = W.VScroll(c, out _bracketScroll, 18);
            F.Stretch((RectTransform)list.parent.parent, 0, 0, 0, 70);
            _bracketContent = null;

            for (int g = 0; g < s.groups.Count; g++)
            {
                var panel = F.Image(list, "Group " + g, new Color(0.04f, 0.08f, 0.17f, 0.95f));
                var vl = panel.gameObject.AddComponent<VerticalLayoutGroup>();
                vl.padding = new RectOffset(16, 16, 12, 12); vl.spacing = 4;
                vl.childControlWidth = vl.childControlHeight = true; vl.childForceExpandHeight = false;
                var fit = panel.gameObject.AddComponent<ContentSizeFitter>();
                fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                var title = F.Label(panel.transform, s.groups[g].name, 30, T.accent, TextAlignmentOptions.Left, T.titleFont);
                F.Size(title, -1, 40);
                Header(panel.transform, new[] { ("#", 50f), ("", 52f), ("VẬN ĐỘNG VIÊN", 620f), ("TRẬN", 90f), ("THẮNG", 100f), ("THUA", 100f), ("HS GAME", 130f), ("HS ĐIỂM", 130f) });
                var st = Eng.Standings(g, true);
                for (int i = 0; i < st.Count; i++)
                {
                    var row = st[i];
                    bool mine = row.entry == _ses.PlayerEntry;
                    var rr = W.Row(panel.transform, 46, null, i < 2 ? new Color(0.07f, 0.22f, 0.14f, 0.95f) : (Color?)null);
                    W.Cell(rr, (i + 1).ToString(), 50, 24, T.textDim, TextAlignmentOptions.Center, T.headingFont);
                    W.Flag(rr, _ses.World.Country(row.entry), 40, 26);
                    W.Cell(rr, _ses.World.ShortName(row.entry), 620, 24, mine ? T.accent : T.text);
                    W.Cell(rr, row.played.ToString(), 90, 24, T.text, TextAlignmentOptions.Center);
                    W.Cell(rr, row.won.ToString(), 100, 24, T.good, TextAlignmentOptions.Center, T.headingFont);
                    W.Cell(rr, row.lost.ToString(), 100, 24, T.danger, TextAlignmentOptions.Center, T.headingFont);
                    W.Cell(rr, W.Signed(row.GameDiff), 130, 24, T.text, TextAlignmentOptions.Center);
                    W.Cell(rr, W.Signed(row.PointDiff), 130, 24, T.text, TextAlignmentOptions.Center);
                }
                // Các trận trong bảng theo lượt.
                foreach (var m in s.slots)
                {
                    if (m.group != g) continue;
                    var mr = W.Row(panel.transform, 40, null, new Color(0, 0, 0, 0.25f));
                    W.Cell(mr, s.rounds[m.round].name, 170, 20, T.textDim, TextAlignmentOptions.Left, T.headingFont);
                    bool show = m.hasResult && m.revealed;
                    var na = W.Cell(mr, _ses.World.ShortName(m.a), 380, 22, m.a == _ses.PlayerEntry ? T.accent : T.text, TextAlignmentOptions.Right);
                    var sc = W.Cell(mr, show ? m.result.ScoreText(true) : m.started ? "ĐANG THI ĐẤU…" : "CHƯA ĐẤU", 300, 22,
                        show ? T.text : T.accent, TextAlignmentOptions.Center, T.headingFont);
                    var nb = W.Cell(mr, _ses.World.ShortName(m.b), 380, 22, m.b == _ses.PlayerEntry ? T.accent : T.text);
                    if (show) { (m.result.winner == 0 ? na : nb).fontStyle = FontStyles.Bold; }
                    var v = new SlotView { slot = m, root = (RectTransform)mr, status = sc };
                    v.names[0] = na; v.names[1] = nb; v.scores[0] = sc; v.scores[1] = sc;
                    _views[m.id] = v;
                }
            }

            // Vòng loại trực tiếp sau vòng bảng.
            var po = new List<int>();
            for (int i = 0; i < s.rounds.Count; i++) if (s.rounds[i].stage == TourStage.Playoff) po.Add(i);
            var poTitle = F.Label(list, "VÒNG LOẠI TRỰC TIẾP (2 NGƯỜI ĐẦU MỖI BẢNG)", 28, T.accent, TextAlignmentOptions.Left, T.headingFont);
            F.Size(poTitle, -1, 44);
            var size = BracketSize(po);
            var holder = F.Rect("Playoff", list);
            F.Size(holder, size.x, size.y);
            holder.sizeDelta = size;
            var inner = F.Rect("Inner", holder);
            inner.anchorMin = inner.anchorMax = new Vector2(0, 1); inner.pivot = new Vector2(0, 1);
            inner.anchoredPosition = Vector2.zero; inner.sizeDelta = size;
            DrawBracket(inner, po, Vector2.zero);
        }

        void UpdateAnyView(SlotView v)
        {
            if (Eng.S.format == TourFormat.Groups && v.slot.group >= 0)
            {
                var T = F.Theme;
                var m = v.slot;
                bool show = m.hasResult && m.revealed;
                v.status.text = show ? m.result.ScoreText(true) : m.started ? "ĐANG THI ĐẤU…" : "CHƯA ĐẤU";
                v.status.color = show ? T.text : T.accent;
                if (show) (m.result.winner == 0 ? v.names[0] : v.names[1]).fontStyle = FontStyles.Bold;
            }
            else UpdateSlotView(v);
        }

        // ───────────────────────────── LUỒNG CÔNG BỐ KẾT QUẢ ─────────────────────────────

        void SetAction(string label, System.Action onClick)
        {
            if (_actionBtn == null) return;
            _actionBtn.gameObject.SetActive(label != null);
            if (label == null) return;
            F.SetButtonLabel(_actionBtn, label);
            _actionBtn.onClick.RemoveAllListeners();
            _actionBtn.onClick.AddListener(() => { AudioManager.Instance?.Click(); onClick(); });
        }

        void SetStatus(string text) { if (_statusText != null) _statusText.text = text; }

        /// <summary>
        /// Điều phối nhánh đấu: bắt đầu vòng ⇒ nếu người chơi còn trận thì chờ "VÀO TRẬN"; nếu không thì công bố lần
        /// lượt kết quả AI của vòng (hiệu ứng lật, có "HIỆN TẤT CẢ") ⇒ popup bị loại / vô địch ⇒ sang vòng sau.
        /// </summary>
        IEnumerator BracketFlow()
        {
            var eng = Eng;
            yield return null;
            int guard = 0;
            while (guard++ < 40)
            {
                if (eng.Finished)
                {
                    SetStatus(eng.S.champion >= 0 ? "Nhà vô địch: " + _ses.World.ShortName(eng.S.champion) : "");
                    SetAction("KẾT THÚC GIẢI", () => Push(TScreen.PostTournament));
                    yield break;
                }
                if (!eng.S.roundStarted) { eng.BeginRound(); _ses.Save(); }

                var mine = eng.PlayerSlot();
                if (mine != null)
                {
                    int opp = mine.a == _ses.PlayerEntry ? mine.b : mine.a;
                    SetStatus($"{eng.Round.name}: bạn gặp {_ses.World.ShortName(opp)}. Các trận khác đang thi đấu…");
                    SetAction("VÀO TRẬN", () => Push(TScreen.PreMatch));
                    yield break;
                }

                var pending = eng.PendingReveals();
                if (pending.Count > 0)
                {
                    SetAction(null, null);
                    SetStatus($"Công bố kết quả {eng.Round.name}…");
                    yield return Reveal(pending);
                    _ses.Save();
                }

                if (!_ses.EliminationShown && eng.PlayerEliminated())
                {
                    _ses.EliminationShown = true;
                    ShowEliminated();
                    yield break;
                }

                if (eng.RoundComplete())
                {
                    eng.AdvanceRound();
                    _ses.Save();
                    if (eng.Finished && eng.PlayerChampion) { Push(TScreen.Champion); yield break; }
                    yield return new WaitForSecondsRealtime(_settings.ui.advanceDelay);
                    _flowRoutine = null;
                    Rebuild(); // vẽ lại nhánh với đối thủ vòng sau (BuildBracket tự chạy lại luồng)
                    yield break;
                }
                yield break;
            }
        }

        IEnumerator Reveal(List<MatchSlot> pending)
        {
            _revealing = true;
            _skipReveal = false;
            if (_revealAllBtn != null) _revealAllBtn.gameObject.SetActive(true);
            foreach (var m in pending)
            {
                m.revealed = true;
                if (_skipReveal) continue;
                if (!_views.TryGetValue(m.id, out var v)) continue;
                // Hiệu ứng lật: thu chiều cao về 0, đổi nội dung, mở ra lại.
                var rt = v.root;
                float fo = _settings.ui.revealFlipOut, fi = _settings.ui.revealFlipIn;
                for (float t = 0; t < fo && !_skipReveal; t += Time.unscaledDeltaTime) { rt.localScale = new Vector3(1, 1 - t / fo, 1); yield return null; }
                UpdateAnyView(v);
                AudioManager.Instance?.Click();
                for (float t = 0; t < fi && !_skipReveal; t += Time.unscaledDeltaTime) { rt.localScale = new Vector3(1, t / fi, 1); yield return null; }
                rt.localScale = Vector3.one;
                float wait = _settings.ui.revealGap;
                while (wait > 0 && !_skipReveal) { wait -= Time.unscaledDeltaTime; yield return null; }
            }
            foreach (var m in pending) if (_views.TryGetValue(m.id, out var v)) { v.root.localScale = Vector3.one; UpdateAnyView(v); }
            if (_revealAllBtn != null) _revealAllBtn.gameObject.SetActive(false);
            _revealing = false;
            _skipReveal = false;
        }

        void ShowEliminated()
        {
            var eng = Eng;
            string where = eng.PlacementText(_ses.PlayerEntry);
            if (where == "VÒNG LOẠI") where = "VÒNG LOẠI";
            else if (where.StartsWith("THỨ")) where = "VÒNG BẢNG";
            SetStatus("Bạn đã bị loại.");
            string why = _ses.ForfeitedOnLoad ? "Trận vừa rồi bị xử thua vì bạn thoát game giữa trận. " : "";
            _ses.ForfeitedOnLoad = false;
            ConfirmDialog.Show(_host.UICanvas.transform, "BẠN ĐÃ DỪNG BƯỚC Ở " + where,
                why + "Bạn muốn xem kết quả các vòng còn lại cho tới khi có nhà vô địch, hay kết thúc giải ngay?",
                "XEM KẾT QUẢ CÁC VÒNG CÒN LẠI",
                () => { eng.ResolveToEnd(); _ses.Save(); Rebuild(); },
                "KẾT THÚC GIẢI",
                () => { eng.ResolveToEnd(); _ses.Save(); Push(TScreen.PostTournament); });
        }

        // ───────────────────────────── TRƯỚC TRẬN ─────────────────────────────

        void BuildPreMatch(RectTransform r)
        {
            var eng = Eng;
            var slot = eng?.PlayerSlot();
            if (slot == null) { BackTo(TScreen.Bracket); return; }
            var T = F.Theme;
            int me = _ses.PlayerEntry, opp = slot.a == me ? slot.b : slot.a;
            var c = Page(r, eng.Round.name, $"{eng.S.name}  ·  {_ses.TierName(eng.S)}  ·  {Events.Name(eng.S.ev)}", 130);

            PlayerCard(c, me, new Vector2(0.25f, 0.5f), true);
            PlayerCard(c, opp, new Vector2(0.75f, 0.5f), false);
            var vs = F.Label(c, "VS", 90, T.accent, TextAlignmentOptions.Center, T.titleFont);
            F.Place(vs.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 60), new Vector2(200, 120));

            string h2h;
            if (_ses.Season != null)
            {
                var (won, lost) = _ses.Season.HeadToHead(opp);
                h2h = won + lost == 0 ? "CHƯA TỪNG GẶP" : $"{won} – {lost}";
            }
            else h2h = "CHƯA TỪNG GẶP";
            var hh = F.Label(c, "LỊCH SỬ ĐỐI ĐẦU\n<size=40>" + h2h + "</size>", 26, T.text, TextAlignmentOptions.Center, T.headingFont);
            hh.textWrappingMode = TextWrappingModes.Normal;
            F.Place(hh.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -80), new Vector2(360, 120));

            var bottom = F.HStack(r, "Actions", 16);
            F.Place(bottom, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 28), Vector2.zero);
            var note = F.Label(bottom, "Thoát giữa trận sẽ bị xử THUA", 24, T.textDim, TextAlignmentOptions.Right);
            F.Size(note, 420, 60);
            F.Button(bottom, "BẮT ĐẦU TRẬN", StartPlayerMatch, 460, 88, true, 40);
        }

        void PlayerCard(RectTransform parent, int e, Vector2 anchor, bool isPlayer)
        {
            var T = F.Theme;
            var world = _ses.World;
            var entry = world.Entry(e);
            var card = F.Image(parent, "Card", T.navy);
            F.Place(card.rectTransform, anchor, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(620, 600));
            var stripe = F.Image(card.transform, "Stripe", isPlayer ? T.accent : T.danger);
            F.Place(stripe.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(620, 8));
            var col = F.VStack(card.transform, "Col", 10, TextAnchor.UpperCenter);
            F.Place(col, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -36), Vector2.zero);
            Line(col, isPlayer ? "BẠN" : "ĐỐI THỦ", 26, isPlayer ? T.accent : T.danger);
            W.Flag(col, world.Country(e), 150, 100);
            var nm = F.Label(col, world.Name(e).Replace(" / ", "\n"), 38, T.text, TextAlignmentOptions.Center, T.titleFont);
            nm.textWrappingMode = TextWrappingModes.Normal;
            F.Size(nm, 580, entry.b >= 0 ? 100 : 54);
            Line(col, Countries.Get(world.Country(e)).Name.ToUpperInvariant(), 26, T.textDim);
            int seed = Eng.S.SeedOf(e);
            Line(col, seed > 0 ? $"HẠT GIỐNG SỐ {seed}" : "KHÔNG ĐƯỢC XẾP HẠT GIỐNG", 26, T.text);
            Line(col, $"RATING {world.Rating(e)}   ·   HẠNG #{_ses.RankOf(e)}", 28, T.accent);
            if (!isPlayer) Line(col, "ĐỘ KHÓ AI: " + _ses.DifficultyName(e), 28, T.text);
            else if (entry.b >= 0) Line(col, "ĐỒNG ĐỘI: " + world.AthleteName(entry.b) + " (AI)", 24, T.textDim);
        }

        // ───────────────────────────── SAU TRẬN ─────────────────────────────

        void BuildPostMatch(RectTransform r)
        {
            var T = F.Theme;
            var res = _lastResult;
            if (res == null || Eng == null) { BackTo(TScreen.Bracket); return; }
            bool won = res.winner == 0;
            var c = Page(r, res.retired ? "BỎ CUỘC" : won ? "CHIẾN THẮNG!" : "THẤT BẠI", Eng.S.name);
            var col = F.VStack(c, "Col", 14);
            F.Place(col, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 60), Vector2.zero);
            var big = F.Label(col, won ? "BẠN THẮNG" : "BẠN THUA", 80, won ? T.good : T.danger, TextAlignmentOptions.Center, T.titleFont);
            F.Size(big, 1000, 110);
            for (int g = 0; g < res.a.Length; g++)
            {
                var l = F.Label(col, $"GAME {g + 1}:   {res.a[g]} – {res.b[g]}", 44, res.a[g] > res.b[g] ? T.text : T.textDim,
                    TextAlignmentOptions.Center, T.headingFont);
                F.Size(l, 800, 60);
            }
            if (res.retired) Line(col, "Trận đấu dừng giữa chừng – bạn bị xử thua.", 28, T.textDim);
            var go = Nav(col, "XEM NHÁNH ĐẤU", () => BackTo(TScreen.Bracket), primary: true);
            _flowRoutine = StartCoroutine(AutoContinue(_settings.ui.postMatchAutoContinue));
        }

        IEnumerator AutoContinue(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            _flowRoutine = null;
            if (Current == TScreen.PostMatch) BackTo(TScreen.Bracket);
        }

        // ───────────────────────────── SAU GIẢI ─────────────────────────────

        void BuildPostTournament(RectTransform r)
        {
            var T = F.Theme;
            var eng = Eng;
            if (eng == null) { ResetTo(TScreen.Hub); return; }
            if (!eng.Finished) { eng.ResolveToEnd(); }
            int me = _ses.PlayerEntry;
            string placement = eng.PlacementText(me);
            int pts = eng.Points(me);
            string tourName = eng.S.name, champ = eng.S.champion >= 0 ? _ses.World.ShortName(eng.S.champion) : "—";
            int rankBefore = 0, rankAfter = 0;
            if (_ses.IsSeason && _ses.Season.S.hasActive)
            {
                _ses.FinishSeasonWeek();          // sinh kết quả các nội dung còn lại, cộng điểm, sang tuần mới, lưu
                rankBefore = _ses.Season.S.rankBefore;
                rankAfter = _ses.Season.S.rankAfter;
                pts = _ses.Season.S.pointsGained;
            }
            else if (_ses.IsSeason) { rankBefore = _ses.Season.S.rankBefore; rankAfter = _ses.Season.S.rankAfter; }

            var c = Page(r, "KẾT THÚC GIẢI", tourName);
            var col = F.VStack(c, "Col", 16);
            F.Place(col, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 40), Vector2.zero);
            Line(col, "THÀNH TÍCH CỦA BẠN", 30, T.textDim);
            var big = F.Label(col, placement, 96, placement == "VÔ ĐỊCH" ? T.accent : T.text, TextAlignmentOptions.Center, T.titleFont);
            F.Size(big, 1100, 120);
            Line(col, "NHÀ VÔ ĐỊCH: " + champ, 30, T.text);
            if (eng.S.tier >= 0 || eng.S.finals)
                Line(col, _ses.IsSeason ? $"ĐIỂM XẾP HẠNG NHẬN ĐƯỢC:  +{pts:N0}" : $"ĐIỂM (GIẢI ĐƠN LẺ – KHÔNG TÍNH VÀO BXH):  {pts:N0}", 34, T.good);
            if (_ses.IsSeason && rankBefore > 0)
            {
                int d = rankBefore - rankAfter;
                string arrow = d > 0 ? $"<color=#3CC86E>TĂNG {d} BẬC</color>" : d < 0 ? $"<color=#DC323C>GIẢM {-d} BẬC</color>" : "GIỮ NGUYÊN";
                Line(col, $"THỨ HẠNG THẾ GIỚI:  #{rankBefore}  →  #{rankAfter}   ({arrow})", 34, T.text);
            }
            if (_ses.IsSeason)
                Nav(col, "TIẾP TỤC MÙA GIẢI", () => { ResetTo(TScreen.Hub, TScreen.SeasonHome, TScreen.WeeklyResults); }, primary: true);
            else
            {
                Nav(col, "VỀ MENU GIẢI ĐẤU", () => { _ses = null; ResetTo(TScreen.Hub); }, primary: true);
                Nav(col, "XEM LẠI NHÁNH ĐẤU", () => BackTo(TScreen.Bracket));
            }
        }

        // ───────────────────────────── VÔ ĐỊCH ─────────────────────────────

        void BuildChampion(RectTransform r)
        {
            var T = F.Theme;
            var eng = Eng;
            if (eng == null) { ResetTo(TScreen.Hub); return; }
            var dim = F.Image(r, "Dim", new Color(0.01f, 0.02f, 0.06f, 0.82f));
            F.Stretch(dim.rectTransform);
            var confetti = F.Stretch(F.Rect("Confetti", r));

            // Cúp vô địch dựng bằng khối (low-poly).
            var cup = F.Rect("Trophy", r);
            F.Place(cup, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 150), new Vector2(260, 300));
            Color gold = T.accent, dark = T.accentDark;
            Block(cup, new Vector2(0, 70), new Vector2(200, 130), gold);       // thân cúp
            Block(cup, new Vector2(-120, 90), new Vector2(40, 90), dark);      // quai trái
            Block(cup, new Vector2(120, 90), new Vector2(40, 90), dark);       // quai phải
            Block(cup, new Vector2(0, -30), new Vector2(46, 80), dark);        // chân
            Block(cup, new Vector2(0, -85), new Vector2(170, 34), gold);       // đế
            Block(cup, new Vector2(0, -115), new Vector2(210, 26), dark);
            Block(cup, new Vector2(-50, 95), new Vector2(26, 70), new Color(1, 1, 1, 0.35f)); // ánh sáng

            var title = F.Label(r, "VÔ ĐỊCH!", 130, gold, TextAlignmentOptions.Center, T.titleFont);
            F.Place(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -90), new Vector2(1400, 160));
            title.characterSpacing = 8;
            var who = F.Label(r, _ses.World.Name(_ses.PlayerEntry), 44, T.text, TextAlignmentOptions.Center, T.headingFont);
            F.Place(who.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -190), new Vector2(1600, 60));
            var tour = F.Label(r, $"{eng.S.name}  ·  {_ses.TierName(eng.S)}  ·  {Events.Name(eng.S.ev)}", 32, T.textDim, TextAlignmentOptions.Center, T.headingFont);
            F.Place(tour.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -250), new Vector2(1600, 50));
            var btn = F.Button(r, "TIẾP TỤC", () => Push(TScreen.PostTournament), 420, 84, true, 38);
            F.Place((RectTransform)btn.transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 50), new Vector2(420, 84));

            AudioManager.Instance?.Whistle();
            AudioManager.Instance?.Cheer(true);
            _flowRoutine = StartCoroutine(Celebrate(confetti, title.rectTransform));
        }

        static void Block(RectTransform p, Vector2 pos, Vector2 size, Color c)
        {
            var img = F.Image(p, "B", c);
            img.raycastTarget = false;
            F.Place(img.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, size);
        }

        /// <summary>Pháo giấy rơi + chữ VÔ ĐỊCH nhấp nháy + tiếng reo hò lặp lại.</summary>
        IEnumerator Celebrate(RectTransform area, RectTransform title)
        {
            Color[] palette = { F.Theme.accent, F.Theme.good, F.Theme.danger, new Color(0.25f, 0.6f, 1f), Color.white, new Color(0.9f, 0.4f, 1f) };
            var pieces = new List<(RectTransform rt, Vector2 vel, float spin)>();
            var rng = new System.Random();
            float cheerAt = 2.5f, t = 0f;
            while (true)
            {
                t += Time.unscaledDeltaTime;
                if (pieces.Count < 140)
                    for (int i = 0; i < 3; i++)
                    {
                        var img = F.Image(area, "C", palette[rng.Next(palette.Length)]);
                        img.raycastTarget = false;
                        var rt = img.rectTransform;
                        F.Place(rt, new Vector2(0, 1), new Vector2(0.5f, 0.5f), new Vector2((float)rng.NextDouble() * 1920f, 20f),
                                new Vector2(10 + rng.Next(10), 16 + rng.Next(14)));
                        pieces.Add((rt, new Vector2(((float)rng.NextDouble() - 0.5f) * 120f, -(180f + (float)rng.NextDouble() * 260f)),
                                    ((float)rng.NextDouble() - 0.5f) * 720f));
                    }
                for (int i = pieces.Count - 1; i >= 0; i--)
                {
                    var (rt, v, spin) = pieces[i];
                    rt.anchoredPosition += v * Time.unscaledDeltaTime;
                    rt.localRotation = Quaternion.Euler(0, 0, rt.localEulerAngles.z + spin * Time.unscaledDeltaTime);
                    if (rt.anchoredPosition.y < -1150f) { Destroy(rt.gameObject); pieces.RemoveAt(i); }
                }
                title.localScale = Vector3.one * (1f + 0.04f * Mathf.Sin(t * 5f));
                if (t > cheerAt) { cheerAt += 3.5f; AudioManager.Instance?.Cheer(t < 8f); }
                yield return null;
            }
        }
    }
}
