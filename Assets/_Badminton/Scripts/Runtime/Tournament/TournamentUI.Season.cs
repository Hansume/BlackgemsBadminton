using Badminton.Tour;
using TMPro;
using UnityEngine;
using F = Badminton.UI.UIFactory;
using W = Badminton.TournamentMode.TourWidgets;

namespace Badminton.TournamentMode
{
    public partial class TournamentUI
    {
        TourEvent _rankEvent;
        bool _rankEventSet;
        string _rankSearch = "";
        RectTransform _rankList;

        string TierLabel(CalendarItem it) => it.finals ? _settings.balance.finals.name : _settings.balance.Tier(it.tier).name;
        Color TierColorOf(CalendarItem it) =>
            TournamentSession.Hex(it.finals ? _settings.balance.finals.color : _settings.balance.Tier(it.tier).color);
        int ChampionPoints(CalendarItem it) => it.finals ? _settings.balance.finals.champion : _settings.balance.Tier(it.tier).champion;

        static string EligText(Eligibility e) => e switch
        {
            Eligibility.Direct => "ĐỦ ĐIỀU KIỆN · VÀO THẲNG",
            Eligibility.Qualifying => "ĐỦ ĐIỀU KIỆN · ĐÁNH VÒNG LOẠI",
            _ => "KHÔNG ĐỦ THỨ HẠNG",
        };

        // ───────────────────────────── TRANG MÙA GIẢI ─────────────────────────────

        void BuildSeasonHome(RectTransform r)
        {
            var T = F.Theme;
            if (_ses?.Season == null) { ResetTo(TScreen.Hub); return; }
            var se = _ses.Season;
            var s = se.S;
            int me = s.playerEntry;
            var c = Page(r, $"MÙA GIẢI {s.seasonNo}", $"SLOT {_ses.Slot + 1}  ·  {Events.Name(s.ev)}  ·  {_ses.World.Name(me)}");

            // Thẻ thông tin người chơi.
            var card = F.Image(c, "Me", T.navy);
            F.Place(card.rectTransform, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, new Vector2(620, 420));
            var col = F.VStack(card.transform, "Col", 10, TextAnchor.UpperCenter);
            F.Place(col, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -26), Vector2.zero);
            W.Flag(col, _ses.World.Country(me), 120, 80);
            var nm = F.Label(col, _ses.World.ShortName(me), 36, T.text, TextAlignmentOptions.Center, T.titleFont);
            F.Size(nm, 580, 50);
            Line(col, $"HẠNG THẾ GIỚI  #{se.RankOf(me)}", 34, T.accent);
            Line(col, $"{se.PointsOf(me):N0} ĐIỂM  ·  RATING {_ses.World.Rating(me)}", 28, T.text);
            Line(col, $"ĐÃ DỰ {s.achievements.FindAll(a => a.season == s.seasonNo).Count} GIẢI MÙA NÀY", 24, T.textDim);

            // Tuần hiện tại.
            var right = F.VStack(c, "Week", 14, TextAnchor.UpperLeft);
            F.Place(right, new Vector2(0, 1), new Vector2(0, 1), new Vector2(680, 0), Vector2.zero);
            if (se.SeasonOver)
            {
                var done = F.Label(right, "MÙA GIẢI ĐÃ KẾT THÚC", 48, T.accent, TextAlignmentOptions.Left, T.titleFont);
                F.Size(done, 1000, 64);
                Nav(right, "BẮT ĐẦU MÙA " + (s.seasonNo + 1), () => { se.StartNextSeason(); _ses.Save(); Rebuild(); }, primary: true, width: 620);
            }
            else
            {
                var weekIdx = se.CurrentWeekIndices();
                var it0 = se.Current;
                var head = F.HStack(right, "Head", 14, TextAnchor.MiddleLeft);
                var wk = F.Label(head, $"TUẦN {it0.week}", 40, T.text, TextAlignmentOptions.Left, T.titleFont);
                F.Size(wk, 200, 56);
                var cnt = F.Label(head, weekIdx.Count > 1 ? $"{weekIdx.Count} GIẢI DIỄN RA SONG SONG – CHỌN 1 GIẢI HOẶC NGHỈ" : "",
                    24, T.textDim, TextAlignmentOptions.Left, T.headingFont);
                F.Size(cnt, 760, 56);

                if (s.hasActive)
                {
                    var act = s.calendar[s.activeItem >= 0 ? s.activeItem : s.calIndex];
                    var tn = F.Label(right, act.name, 48, T.accent, TextAlignmentOptions.Left, T.titleFont);
                    F.Size(tn, 1000, 62);
                    Line2(right, "Bạn đang thi đấu giải này.", 26, T.textDim);
                    Nav(right, "TIẾP TỤC GIẢI ĐANG ĐÁNH", () => Push(TScreen.Bracket), primary: true, width: 620);
                }
                else
                {
                    foreach (int idx in weekIdx)
                    {
                        var it = s.calendar[idx];
                        int ci = idx;
                        var el = se.PlayerEligibility(it);
                        var tcard = F.Image(right, "Tour " + idx, new Color(0.04f, 0.08f, 0.17f, 0.95f));
                        F.Size(tcard, 1080, weekIdx.Count > 1 ? 150 : 190);
                        var cv = F.VStack(tcard.transform, "C", 6, TextAnchor.UpperLeft);
                        F.Place(cv, new Vector2(0, 1), new Vector2(0, 1), new Vector2(20, -12), Vector2.zero);
                        var h = F.HStack(cv, "H", 14, TextAnchor.MiddleLeft);
                        W.TierTag(h, TierLabel(it), TierColorOf(it), 240, 40);
                        var tn = F.Label(h, it.name, 40, T.accent, TextAlignmentOptions.Left, T.titleFont);
                        F.Size(tn, 760, 52);
                        Line2(cv, $"Vô địch: {ChampionPoints(it):N0} điểm   ·   " +
                                  (it.finals ? "8 VĐV/cặp điểm cao nhất mùa · vòng bảng" : B32(it)) + "   ·   " + EligText(el), 24,
                              el == Eligibility.No ? T.danger : T.textDim);
                        var btn = F.Button(tcard.transform, "THAM DỰ", () => { _ses.EnterSeasonTournament(ci); Push(TScreen.Bracket); },
                                           260, 64, true, 30);
                        F.Place((RectTransform)btn.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-18, 14), new Vector2(260, 64));
                        SetEnabled(btn, el != Eligibility.No);
                    }
                    Nav(right, "NGHỈ TUẦN NÀY", () =>
                    {
                        _ses.FinishSeasonWeek();
                        ResetTo(TScreen.Hub, TScreen.SeasonHome, TScreen.WeeklyResults);
                    }, width: 620);
                }
            }

            var bottom = F.HStack(r, "Nav", 16);
            F.Place(bottom, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 30), Vector2.zero);
            F.Button(bottom, "LỊCH THI ĐẤU", () => Push(TScreen.Calendar), 330, 72);
            F.Button(bottom, "KẾT QUẢ TUẦN NÀY", () => Push(TScreen.WeeklyResults), 360, 72);
            F.Button(bottom, "BẢNG XẾP HẠNG", () => { _rankEventSet = false; Push(TScreen.Ranking); }, 330, 72);
            F.Button(bottom, "HỒ SƠ CỦA TÔI", () => { _profileEntry = me; Push(TScreen.Profile); }, 330, 72);
        }

        string B32(CalendarItem it)
        {
            var t = _settings.balance.Tier(it.tier);
            return t.hasQualifying && !_ses.Season.IsParallel(it) ? "Nhánh 32 + vòng loại 16" : "Nhánh tối đa 32";
        }

        static void Line2(Transform col, string text, float size, Color color)
        {
            var l = F.Label(col, text, size, color, TextAlignmentOptions.Left, F.Theme.headingFont);
            l.textWrappingMode = TextWrappingModes.Normal;
            F.Size(l, 1100, size + 14);
        }

        // ───────────────────────────── LỊCH THI ĐẤU ─────────────────────────────

        void BuildCalendar(RectTransform r)
        {
            var T = F.Theme;
            var se = _ses.Season;
            var c = Page(r, "LỊCH THI ĐẤU", $"MÙA {se.S.seasonNo}  ·  {Events.Name(se.S.ev)}");
            var list = W.VScroll(c, out _);
            F.Stretch((RectTransform)list.parent.parent);
            Header(list, new[] { ("TUẦN", 100f), ("CẤP GIẢI", 250f), ("TÊN GIẢI", 520f), ("VÔ ĐỊCH", 170f), ("ĐIỀU KIỆN / KẾT QUẢ", 560f) });
            for (int i = 0; i < se.S.calendar.Count; i++)
            {
                var it = se.S.calendar[i];
                bool cur = !se.SeasonOver && !it.done && it.week == se.Current.week;
                var row = W.Row(list, 56, null, cur ? new Color(0.35f, 0.27f, 0.03f, 0.95f) : (Color?)null);
                W.Cell(row, it.week.ToString(), 100, 28, cur ? Color.white : T.textDim, TextAlignmentOptions.Center, T.headingFont);
                W.TierTag(row, TierLabel(it), TierColorOf(it), 240, 36);
                W.Cell(row, it.name, 520, 26, T.text, TextAlignmentOptions.Left, T.headingFont);
                W.Cell(row, ChampionPoints(it).ToString("N0"), 170, 26, T.accent, TextAlignmentOptions.Center);
                string status; Color sc;
                if (it.done) { status = it.playerPlayed ? "BẠN: " + it.playerResult : "ĐÃ DIỄN RA (KHÔNG DỰ)"; sc = it.playerPlayed ? T.good : T.textDim; }
                else
                {
                    var el = se.PlayerEligibility(it);
                    status = (cur ? "TUẦN NÀY · " : "") + EligText(el);
                    sc = el == Eligibility.No ? T.danger : T.text;
                }
                W.Cell(row, status, 560, 24, sc);
            }
        }

        // ───────────────────────────── BẢNG XẾP HẠNG ─────────────────────────────

        void BuildRanking(RectTransform r)
        {
            if (!_rankEventSet) { _rankEvent = _ses.Event; _rankEventSet = true; _rankSearch = ""; }
            var c = Page(r, "BẢNG XẾP HẠNG THẾ GIỚI",
                $"Tổng {_settings.balance.season.bestResults} kết quả tốt nhất trong {_settings.balance.season.windowWeeks} tuần gần nhất", 30);
            var tools = F.HStack(c, "Tools", 14, TextAnchor.MiddleLeft);
            F.Place(tools, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, Vector2.zero);
            F.Segmented(tools, EventNames, (int)_rankEvent, i => { _rankEvent = Events.All[i]; FillRanking(); }, 200);
            var input = F.Input(tools, "Tìm VĐV…", _rankSearch, 24, 420, 60);
            input.onValueChanged.AddListener(v => { _rankSearch = v; FillRanking(); });
            _rankList = W.VScroll(c, out _);
            F.Stretch((RectTransform)_rankList.parent.parent, 0, 0, 0, 76);
            FillRanking();
        }

        void FillRanking()
        {
            if (_rankList == null) return;
            foreach (Transform ch in _rankList) Destroy(ch.gameObject);
            var T = F.Theme;
            var ranking = _ses.Ranking(_rankEvent);
            Header(_rankList, new[] { ("HẠNG", 100f), ("", 52f), ("VẬN ĐỘNG VIÊN", 800f), ("QUỐC GIA", 240f), ("ĐIỂM", 180f), ("RATING", 140f) });
            string q = (_rankSearch ?? "").Trim().ToLowerInvariant();
            for (int i = 0; i < ranking.Count; i++)
            {
                int e = ranking[i];
                string name = _ses.World.Name(e);
                var country = Countries.Get(_ses.World.Country(e));
                if (q.Length > 0 && !name.ToLowerInvariant().Contains(q) && !country.Name.ToLowerInvariant().Contains(q)) continue;
                bool mine = e == _ses.PlayerEntry;
                var row = W.Row(_rankList, 50, () => { _profileEntry = e; Push(TScreen.Profile); },
                                mine ? new Color(0.35f, 0.27f, 0.03f, 0.95f) : (Color?)null);
                W.Cell(row, (i + 1).ToString(), 100, 28, i < 8 ? T.accent : T.textDim, TextAlignmentOptions.Center, T.titleFont);
                W.Flag(row, country.Code, 44, 28);
                W.Cell(row, name + (mine ? "  (BẠN)" : ""), 800, 26, mine ? Color.white : T.text);
                W.Cell(row, country.Name, 240, 24, T.textDim);
                W.Cell(row, _ses.PointsOf(e).ToString("N0"), 180, 26, T.text, TextAlignmentOptions.Right, T.headingFont);
                W.Cell(row, _ses.World.Rating(e).ToString(), 140, 26, T.accent, TextAlignmentOptions.Center);
            }
        }

        // ───────────────────────────── KẾT QUẢ TUẦN NÀY ─────────────────────────────

        void BuildWeeklyResults(RectTransform r)
        {
            var T = F.Theme;
            var s = _ses.Season.S;
            var c = Page(r, "KẾT QUẢ TUẦN NÀY", s.lastWeek.Count > 0 ? $"Tuần {s.lastWeekNo} · mùa {s.seasonNo}" : "Chưa có giải nào kết thúc");
            var list = W.VScroll(c, out _, 10);
            F.Stretch((RectTransform)list.parent.parent, 0, 90, 0, 0);
            if (s.lastWeek.Count == 0) W.Cell(W.Row(list, 56, null), "Kết quả các giải sẽ hiện ở đây khi sang tuần mới.", 1400, 26, T.textDim);
            foreach (var d in s.lastWeek)
            {
                var head = W.Row(list, 52, null, new Color(0.02f, 0.05f, 0.12f, 0.95f));
                var tierName = d.finals ? _settings.balance.finals.name : _settings.balance.Tier(d.tier).name;
                var tierCol = TournamentSession.Hex(d.finals ? _settings.balance.finals.color : _settings.balance.Tier(d.tier).color);
                W.TierTag(head, tierName, tierCol, 240, 36);
                W.Cell(head, d.name, 520, 28, T.text, TextAlignmentOptions.Left, T.headingFont);
                W.Cell(head, Events.Name(d.ev) + (d.ev == _ses.Event ? "  ·  NỘI DUNG CỦA BẠN" : ""), 560, 24, d.ev == _ses.Event ? T.accent : T.textDim);

                ResultLine(list, "VÔ ĐỊCH", d.champion, d.finalScore, T.accent);
                ResultLine(list, "Á QUÂN", d.runnerUp, null, T.text);
                foreach (int sf in d.semis) ResultLine(list, "BÁN KẾT", sf, null, T.textDim);
            }
            var b = F.Button(r, "TIẾP TỤC", () => BackTo(TScreen.SeasonHome), 420, 76, true);
            F.Place((RectTransform)b.transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 28), new Vector2(420, 76));
        }

        void ResultLine(RectTransform list, string label, int e, string score, Color c)
        {
            if (e < 0) return;
            var T = F.Theme;
            bool mine = e == _ses.PlayerEntry;
            var row = W.Row(list, 44, () => { _profileEntry = e; Push(TScreen.Profile); });
            W.Cell(row, label, 180, 24, c, TextAlignmentOptions.Left, T.headingFont);
            W.Flag(row, _ses.World.Country(e), 40, 26);
            W.Cell(row, _ses.World.Name(e) + (mine ? "  (BẠN)" : ""), 820, 24, mine ? T.accent : T.text);
            if (score != null) W.Cell(row, score, 360, 22, T.textDim, TextAlignmentOptions.Right);
        }
    }
}
