using System.Collections.Generic;
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
        int _profileEntry = -1;

        static bool UsesMenuTitle(TScreen s) =>
            s == TScreen.Hub || s == TScreen.Slots || s == TScreen.SingleSetup || s == TScreen.CustomSetup ||
            s == TScreen.EventPick || s == TScreen.Message;

        TourEvent FlowEvent => Events.All[Mathf.Clamp(_flow.EventIndex, 0, 4)];

        // ───────────────────────────── HUB ─────────────────────────────

        void BuildHub(RectTransform r)
        {
            var col = Frame(r, "GIẢI ĐẤU", "Chơi đơn · mọi đối thủ đều là AI · chạy offline", topBar: false);
            Nav(col, "MÙA GIẢI MỚI", () => { _flow = new TournamentFlow { Kind = FlowKind.Season }; Push(TScreen.Slots); }, primary: true);
            Nav(col, "TIẾP TỤC MÙA GIẢI", () => { _flow = new TournamentFlow { Kind = FlowKind.Season, ContinueSave = true }; Push(TScreen.Slots); },
                enabled: TournamentSaveIndex.HasAnySave());
            Nav(col, "GIẢI ĐẤU ĐƠN LẺ", () => { _flow = new TournamentFlow { Kind = FlowKind.Single }; Push(TScreen.SingleSetup); });
            Nav(col, "GIẢI TÙY CHỈNH", () => { _flow = new TournamentFlow { Kind = FlowKind.Custom }; Push(TScreen.CustomSetup); });
            F.Button(col, "QUAY LẠI", () => _host.ShowMainMenu(), 300, 64);
        }

        // ───────────────────────────── SLOT LƯU ─────────────────────────────

        void BuildSlots(RectTransform r)
        {
            bool cont = _flow.ContinueSave;
            var col = Frame(r, cont ? "TIẾP TỤC MÙA GIẢI" : "MÙA GIẢI MỚI", cont ? "Chọn slot để tiếp tục" : "Chọn slot để lưu mùa giải mới");
            for (int i = 0; i < TournamentSaveIndex.SlotCount; i++)
            {
                int slot = i;
                var h = TournamentSaveIndex.GetHeader(slot);
                string label = h == null
                    ? $"SLOT {slot + 1}  —  TRỐNG"
                    : $"SLOT {slot + 1}  —  {h.eventName} · {h.athleteName} · MÙA {h.seasonNo} TUẦN {h.week}";
                var row = F.HStack(col, "Slot " + slot, 12);
                var b = F.Button(row, label, () => OnSlotClicked(slot, h != null), 960, 76, false, 28);
                b.GetComponentInChildren<TextMeshProUGUI>().overflowMode = TextOverflowModes.Truncate;
                SetEnabled(b, !cont || h != null);
                var del = F.Button(row, "XOÁ", () => ConfirmDialog.Show(_host.UICanvas.transform, "XOÁ SLOT?",
                    $"Xoá vĩnh viễn mùa giải ở slot {slot + 1}. Không thể hoàn tác.", "XOÁ",
                    () => { TournamentSaveIndex.Delete(slot); Rebuild(); }, dangerous: true), 130, 76, false, 28);
                SetEnabled(del, h != null);
            }
        }

        void OnSlotClicked(int slot, bool occupied)
        {
            _flow.Slot = slot;
            if (_flow.ContinueSave)
            {
                var ses = TournamentSession.LoadSeason(_settings, Rules, slot);
                if (ses == null)
                {
                    ConfirmDialog.Show(_host.UICanvas.transform, "KHÔNG ĐỌC ĐƯỢC FILE LƯU",
                        "Dữ liệu slot này bị hỏng hoặc thuộc phiên bản cũ.", "ĐÓNG", null);
                    return;
                }
                _ses = ses;
                if (_ses.Tour != null) ResetTo(TScreen.Hub, TScreen.SeasonHome, TScreen.Bracket);
                else ResetTo(TScreen.Hub, TScreen.SeasonHome);
                if (_ses.ForfeitedOnLoad)
                    ConfirmDialog.Show(_host.UICanvas.transform, "TRẬN ĐẤU BỊ XỬ THUA",
                        "Lần trước bạn đã thoát game khi trận đấu đang diễn ra nên trận đó bị xử thua (bỏ cuộc).",
                        "ĐÃ HIỂU", () => { if (Current == TScreen.Bracket) Rebuild(); }, "ĐÓNG",
                        () => { if (Current == TScreen.Bracket) Rebuild(); });
                return;
            }
            if (occupied)
                ConfirmDialog.Show(_host.UICanvas.transform, "GHI ĐÈ SLOT?",
                    $"Slot {slot + 1} đang có mùa giải. Tạo mùa giải mới sẽ xoá dữ liệu cũ.", "GHI ĐÈ",
                    () => Push(TScreen.EventPick), dangerous: true);
            else Push(TScreen.EventPick);
        }

        // ───────────────────────────── THIẾT LẬP GIẢI ─────────────────────────────

        void BuildSingleSetup(RectTransform r)
        {
            var col = Frame(r, "GIẢI ĐẤU ĐƠN LẺ", "Một giải loại trực tiếp 32 VĐV/cặp – chọn cấp giải");
            var names = new string[_settings.balance.tiers.Count];
            for (int i = 0; i < names.Length; i++) names[i] = _settings.balance.tiers[i].name;
            SegRow(col, "CẤP GIẢI", names, _flow.TierIndex, i => { _flow.TierIndex = i; Rebuild(); }, 210);
            var t = _settings.balance.Tier(_flow.TierIndex);
            var info = F.Label(col, $"Vô địch: {t.champion:N0} điểm  ·  {(t.hasQualifying ? "Có vòng loại (16 → 4 suất)" : "Không có vòng loại")}",
                26, F.Theme.textDim, TextAlignmentOptions.Center, F.Theme.headingFont);
            F.Size(info, 1000, 40);
            Nav(col, "TIẾP TỤC", () => Push(TScreen.EventPick), primary: true);
        }

        void BuildCustomSetup(RectTransform r)
        {
            var col = Frame(r, "GIẢI TÙY CHỈNH", "Tự chọn số VĐV và thể thức");
            int sizeIdx = _flow.CustomSize == 4 ? 0 : _flow.CustomSize == 16 ? 2 : 1;
            SegRow(col, "SỐ VĐV", new[] { "4", "8", "16" }, sizeIdx, i => _flow.CustomSize = i == 0 ? 4 : i == 1 ? 8 : 16, 130);
            SegRow(col, "THỂ THỨC", new[] { "LOẠI TRỰC TIẾP", "VÒNG BẢNG" }, _flow.CustomGroups ? 1 : 0, i => _flow.CustomGroups = i == 1, 260);
            Nav(col, "TIẾP TỤC", () => Push(TScreen.EventPick), primary: true);
        }

        void BuildEventPick(RectTransform r)
        {
            var col = Frame(r, "CHỌN NỘI DUNG",
                $"Bạn thi đấu với tên: {GameSettings.PlayerName} ({(PlayerProfile.Female ? "nữ" : "nam")}, {Countries.Get(PlayerProfile.Country).Name})  ·  " +
                "đổi nhân vật ở menu chính → NHÂN VẬT");
            bool doublesReady = Match.players.Length >= 4;
            for (int i = 0; i < EventNames.Length; i++)
            {
                int e = i;
                var ev = Events.All[e];
                bool genderOk = ev == TourEvent.XD || (PlayerProfile.Female ? ev == TourEvent.WS || ev == TourEvent.WD
                                                                             : ev == TourEvent.MS || ev == TourEvent.MD);
                Nav(col, EventNames[e], () => OnEventChosen(e), enabled: genderOk && (!IsDoubles(e) || doublesReady));
            }
        }

        void OnEventChosen(int e)
        {
            _flow.EventIndex = e;
            var ev = Events.All[e];
            if (_flow.Kind == FlowKind.Season)
                _ses = TournamentSession.NewSeason(_settings, Rules, _flow.Slot, ev); // dựng BXH mùa trước
            else
            {
                _ses = TournamentSession.Preview(_settings, Rules, _flow.Kind == FlowKind.Single ? SessionKind.Single : SessionKind.Custom, ev);
                _ses.TierIndex = _flow.TierIndex;
                _ses.CustomSize = _flow.CustomSize;
                _ses.CustomGroups = _flow.CustomGroups;
            }
            StartWithPlayer();
        }

        static void Header(Transform list, (string, float)[] cols)
        {
            var row = W.Row(list, 40, null, new Color(0, 0, 0, 0));
            foreach (var (t, w) in cols)
                W.Cell(row, t, w, 20, F.Theme.accent, w <= 160 ? TextAlignmentOptions.Center : TextAlignmentOptions.Left, F.Theme.headingFont);
        }

        /// <summary>Gán suất thi đấu cho người chơi (tên = tên ở menu chính) rồi vào thẳng giải / trang mùa giải.</summary>
        void StartWithPlayer()
        {
            _ses.ChoosePlayer(_ses.AutoPlayerEntry());
            switch (_ses.Kind)
            {
                case SessionKind.Season:
                    ResetTo(TScreen.Hub, TScreen.SeasonHome);
                    break;
                case SessionKind.Single:
                    _ses.StartSingle();
                    ResetTo(TScreen.Hub, TScreen.Bracket);
                    break;
                default:
                    _ses.StartCustom();
                    ResetTo(TScreen.Hub, TScreen.Bracket);
                    break;
            }
        }

        // ───────────────────────────── HỒ SƠ ─────────────────────────────

        void BuildProfile(RectTransform r)
        {
            var T = F.Theme;
            int e = _profileEntry >= 0 ? _profileEntry : _ses?.PlayerEntry ?? -1;
            if (_ses == null || e < 0) { Page(r, "HỒ SƠ VẬN ĐỘNG VIÊN", null); return; }
            var world = _ses.World;
            var entry = world.Entry(e);
            var A = world.Athlete(entry.a);
            var country = Countries.Get(world.Country(e));
            bool isPlayer = e == _ses.PlayerEntry;
            var c = Page(r, isPlayer ? "HỒ SƠ CỦA TÔI" : "HỒ SƠ VẬN ĐỘNG VIÊN", Events.Name(entry.ev));

            // Thẻ bên trái: cờ, tên, quốc gia, rating, hạng.
            var card = F.Image(c, "Card", T.navy);
            F.Place(card.rectTransform, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, new Vector2(700, 760));
            var col = F.VStack(card.transform, "Info", 12, TextAnchor.UpperCenter);
            F.Place(col, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -30), Vector2.zero);
            W.Flag(col, world.Country(e), 180, 120);
            var nm = F.Label(col, world.Name(e).Replace(" / ", "\n"), 40, T.text, TextAlignmentOptions.Center, T.titleFont);
            nm.textWrappingMode = TextWrappingModes.Normal;
            F.Size(nm, 640, entry.b >= 0 ? 110 : 60);
            Line(col, country.Name.ToUpperInvariant(), 28, T.textDim);
            Line(col, $"RATING  {world.Rating(e)}   ·   ĐỘ KHÓ AI: {_ses.DifficultyName(e)}", 28, T.accent);
            int rank = _ses.RankOf(e);
            Line(col, _ses.Season != null ? $"HẠNG THẾ GIỚI  #{rank}   ·   {_ses.PointsOf(e):N0} ĐIỂM" : $"HẠNG (THEO RATING)  #{rank}", 28, T.text);
            if (entry.b >= 0)
            {
                var B = world.Athlete(entry.b);
                Line(col, $"{world.AthleteName(A.id)}: {world.Ratings[A.id]}   ·   {world.AthleteName(B.id)}: {world.Ratings[B.id]}", 24, T.textDim);
            }
            if (!isPlayer && _ses.Season != null && _ses.PlayerEntry >= 0)
            {
                var (won, lost) = _ses.Season.HeadToHead(e);
                Line(col, $"ĐỐI ĐẦU VỚI BẠN:  {won} THẮNG – {lost} THUA", 26, T.text);
            }

            // Bên phải: thành tích trong mùa (người chơi) hoặc ghi chú.
            var right = F.Rect("Right", c);
            right.anchorMin = new Vector2(0, 0); right.anchorMax = new Vector2(1, 1);
            right.offsetMin = new Vector2(730, 0); right.offsetMax = Vector2.zero;
            var hdr = F.Label(right, "THÀNH TÍCH TRONG MÙA", 30, T.accent, TextAlignmentOptions.Left, T.headingFont);
            F.Place(hdr.rectTransform, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, new Vector2(900, 44));
            var list = W.VScroll(right, out _);
            F.Stretch((RectTransform)list.parent.parent, 0, 0, 0, 54);
            if (_ses.Season == null)
            {
                W.Cell(W.Row(list, 50, null), "Chỉ có ở chế độ Mùa giải.", 900, 26, T.textDim);
                return;
            }
            var s = _ses.Season.S;
            if (isPlayer)
            {
                if (s.achievements.Count == 0) W.Cell(W.Row(list, 50, null), "Chưa dự giải nào.", 900, 26, T.textDim);
                for (int i = s.achievements.Count - 1; i >= 0; i--)
                {
                    var a = s.achievements[i];
                    var row = W.Row(list, 52, null);
                    W.Cell(row, $"M{a.season} T{a.week}", 120, 22, T.textDim);
                    W.TierTag(row, a.finals ? _settings.balance.finals.name : _settings.balance.Tier(a.tier).name,
                        TournamentSession.Hex(a.finals ? _settings.balance.finals.color : _settings.balance.Tier(a.tier).color), 210, 34);
                    W.Cell(row, a.tour, 360, 24, T.text);
                    W.Cell(row, a.placement, 170, 24, T.accent, TextAlignmentOptions.Left, T.headingFont);
                    W.Cell(row, "+" + a.points.ToString("N0"), 140, 24, T.good, TextAlignmentOptions.Right);
                }
            }
            else
            {
                int shown = 0;
                var recs = _ses.Season.ResultsOf(e, s.seasonNo);
                int titles = recs.FindAll(x => x.depth == 0).Count, finals = recs.FindAll(x => x.depth <= 1).Count;
                var sum = W.Row(list, 50, null, new Color(0.02f, 0.05f, 0.12f, 0.95f));
                W.Cell(sum, $"MÙA {s.seasonNo}:  {titles} CHỨC VÔ ĐỊCH  ·  {finals} LẦN VÀO CHUNG KẾT  ·  {recs.Count} LẦN VÀO BÁN KẾT TRỞ LÊN",
                       1000, 24, T.accent, TextAlignmentOptions.Left, T.headingFont);
                foreach (var a in recs)
                {
                    var row = W.Row(list, 52, null);
                    W.Cell(row, $"M{a.season} T{a.week}", 120, 22, T.textDim);
                    W.TierTag(row, a.finals ? _settings.balance.finals.name : _settings.balance.Tier(a.tier).name,
                        TournamentSession.Hex(a.finals ? _settings.balance.finals.color : _settings.balance.Tier(a.tier).color), 210, 34);
                    W.Cell(row, a.tour, 380, 24, T.text);
                    W.Cell(row, SeasonEngine.DepthText(a.depth), 170, 24, a.depth == 0 ? T.accent : T.text, TextAlignmentOptions.Left, T.headingFont);
                    shown++;
                }
                foreach (var m in s.matches)
                {
                    if (m.opponent != e) continue;
                    var row = W.Row(list, 52, null);
                    W.Cell(row, $"M{m.season} T{m.week} · {m.tour} · {m.round}", 560, 22, T.textDim);
                    W.Cell(row, m.won ? "BẠN THẮNG" : "BẠN THUA", 170, 24, m.won ? T.good : T.danger);
                    W.Cell(row, m.score, 220, 22, T.text);
                    shown++;
                }
                if (shown == 0) W.Cell(W.Row(list, 50, null), "Chưa có dữ liệu đối đầu / thành tích nổi bật gần đây.", 900, 24, T.textDim);
            }
        }

        static void Line(Transform col, string text, float size, Color color)
        {
            var l = F.Label(col, text, size, color, TextAlignmentOptions.Center, F.Theme.headingFont);
            l.textWrappingMode = TextWrappingModes.Normal;
            F.Size(l, 660, size + 14);
        }
    }
}
