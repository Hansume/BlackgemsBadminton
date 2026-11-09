using System;
using System.Collections;
using System.Collections.Generic;
using Badminton.AI;
using Badminton.Core;
using Badminton.Player;
using Badminton.Scoring;
using Badminton.Tour;
using Badminton.UI;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using F = Badminton.UI.UIFactory;

namespace Badminton.TournamentMode
{
    /// <summary>Các màn hình của chế độ Giải đấu.</summary>
    public enum TScreen
    {
        Hub, Slots, SingleSetup, CustomSetup, EventPick, Profile,
        SeasonHome, Calendar, Ranking, WeeklyResults,
        Bracket, PreMatch, PostMatch, PostTournament, Champion, Message,
    }

    /// <summary>Người chơi đang đi theo nhánh nào của menu Giải đấu.</summary>
    public enum FlowKind { Season, Single, Custom }

    /// <summary>Lựa chọn tích luỹ dọc theo luồng màn hình.</summary>
    public class TournamentFlow
    {
        public FlowKind Kind;
        public bool ContinueSave;   // Slots: "Tiếp tục" (true) hay "Mùa giải mới" (false)
        public int Slot = -1;
        public int EventIndex;
        public int TierIndex;
        public int CustomSize = 8;
        public bool CustomGroups;
    }

    /// <summary>
    /// Giao diện chế độ Giải đấu (chỉ offline – không bao giờ tạo NetworkManager).
    /// Dùng chung canvas + theme với GameUI; GameUI ẩn màn hình của mình (EnterExternal) khi lớp này mở.
    /// Điều hướng kiểu ngăn xếp; mỗi màn hình dựng lại khi hiện. Các phần màn hình nằm ở các file partial:
    ///  TournamentUI.Setup.cs (chọn chế độ/nội dung/VĐV, hồ sơ), TournamentUI.Bracket.cs (nhánh/bảng đấu, công bố
    ///  kết quả, trước/sau trận, sau giải, vô địch), TournamentUI.Season.cs (lịch, BXH, kết quả tuần).
    /// </summary>
    public partial class TournamentUI : MonoBehaviour
    {
        public static readonly string[] EventNames = { "ĐƠN NAM", "ĐƠN NỮ", "ĐÔI NAM", "ĐÔI NỮ", "ĐÔI NAM NỮ" };
        public static bool IsDoubles(int eventIndex) => eventIndex >= 2;

        GameUI _host;
        MatchController Match => _host.match;
        RectTransform _root;
        GameObject _screenGo;
        readonly List<TScreen> _stack = new List<TScreen>();
        TournamentFlow _flow = new TournamentFlow();
        TournamentSession _ses;
        TournamentSettings _settings;
        string _message;

        /// <summary>Đang đánh trận 3D của giải.</summary>
        public bool InMatch { get; private set; }
        /// <summary>Chỉ dùng để test tự động: AI điều khiển luôn nhân vật của người chơi.</summary>
        [NonSerialized] public bool AutoPlayForTest;

        public bool IsOpen => _root != null && _root.gameObject.activeSelf;
        public TScreen Current => _stack.Count > 0 ? _stack[_stack.Count - 1] : TScreen.Hub;
        public TournamentSession Session => _ses;

        MatchRulesConfig Rules => Match != null && Match.rules != null ? Match.rules.rules : new MatchRulesConfig();

        public void Init(GameUI host)
        {
            _host = host;
            _root = F.Stretch(F.Rect("Tournament UI", host.UICanvas.transform));
            _root.gameObject.SetActive(false);
            _settings = TournamentSettings.Load();
            GameControls.Pause.performed += OnEsc;
        }

        void OnDestroy()
        {
            GameControls.Pause.performed -= OnEsc;
            ConfirmDialog.Close();
        }

        // ───────────────────────────── ĐIỀU HƯỚNG ─────────────────────────────

        /// <summary>Mở chế độ Giải đấu từ màn hình chính.</summary>
        public void Open()
        {
            _root.gameObject.SetActive(true);
            _root.SetAsLastSibling();
            _stack.Clear();
            _flow = new TournamentFlow();
            _ses = null;
            if (_settings == null || _settings.database == null || _settings.database.data.athletes.Count == 0)
            {
                _message = "Thiếu dữ liệu giải đấu.\nTrong Unity chạy menu \"Badminton > Giải đấu > Tạo dữ liệu giải đấu\".";
                Push(TScreen.Message);
                return;
            }
            Push(TScreen.Hub);
        }

        /// <summary>Đóng (GameUI gọi khi về màn hình chính).</summary>
        public void Close()
        {
            StopFlow();
            ConfirmDialog.Close();
            _stack.Clear();
            if (_screenGo != null) Destroy(_screenGo);
            _screenGo = null;
            if (_root != null) _root.gameObject.SetActive(false);
            _host?.SetMenuTitle(true);
        }

        public void Push(TScreen s) { _stack.Add(s); Rebuild(); }

        /// <summary>Giải đơn lẻ / tùy chỉnh đang đánh dở (không lưu được) – rời đi thì mất.</summary>
        bool UnsavedTournament => _ses != null && !_ses.IsSeason && _ses.Tour != null && !_ses.Tour.Finished;

        public void Back()
        {
            if (Current == TScreen.Bracket && UnsavedTournament)
            {
                ConfirmLeaveTournament(() => { _ses.Tour = null; PopOrMenu(); });
                return;
            }
            PopOrMenu();
        }

        void PopOrMenu()
        {
            if (_stack.Count <= 1) { _host.ShowMainMenu(); return; }
            _stack.RemoveAt(_stack.Count - 1);
            Rebuild();
        }

        public void BackTo(TScreen s)
        {
            int i = _stack.LastIndexOf(s);
            if (i < 0) { Push(s); return; }
            _stack.RemoveRange(i + 1, _stack.Count - i - 1);
            Rebuild();
        }

        public void ResetTo(params TScreen[] stack)
        {
            _stack.Clear();
            _stack.AddRange(stack);
            Rebuild();
        }

        public void ToMainMenu()
        {
            if (UnsavedTournament) ConfirmLeaveTournament(() => { _ses = null; _host.ShowMainMenu(); });
            else _host.ShowMainMenu();
        }

        void ConfirmLeaveTournament(Action onLeave) =>
            ConfirmDialog.Show(_host.UICanvas.transform, "BỎ GIẢI ĐANG ĐẤU?",
                "Giải đơn lẻ / tùy chỉnh không được lưu. Rời đi bây giờ sẽ mất toàn bộ tiến trình của giải.",
                "RỜI GIẢI", onLeave, "Ở LẠI", null, dangerous: true);

        void OnEsc(InputAction.CallbackContext _)
        {
            if (!IsOpen) return;
            if (ConfirmDialog.IsOpen) ConfirmDialog.Cancel();
            else if (Current == TScreen.Bracket && _revealing) _skipReveal = true;
            else Back();
        }

        void Rebuild()
        {
            StopFlow();
            ConfirmDialog.Close();
            if (_screenGo != null) Destroy(_screenGo);
            var r = F.Stretch(F.Rect(Current + " Screen", _root));
            _screenGo = r.gameObject;
            _host.SetMenuTitle(UsesMenuTitle(Current));
            switch (Current)
            {
                case TScreen.Hub: BuildHub(r); break;
                case TScreen.Slots: BuildSlots(r); break;
                case TScreen.SingleSetup: BuildSingleSetup(r); break;
                case TScreen.CustomSetup: BuildCustomSetup(r); break;
                case TScreen.EventPick: BuildEventPick(r); break;
                case TScreen.Profile: BuildProfile(r); break;
                case TScreen.SeasonHome: BuildSeasonHome(r); break;
                case TScreen.Calendar: BuildCalendar(r); break;
                case TScreen.Ranking: BuildRanking(r); break;
                case TScreen.WeeklyResults: BuildWeeklyResults(r); break;
                case TScreen.Bracket: BuildBracket(r); break;
                case TScreen.PreMatch: BuildPreMatch(r); break;
                case TScreen.PostMatch: BuildPostMatch(r); break;
                case TScreen.PostTournament: BuildPostTournament(r); break;
                case TScreen.Champion: BuildChampion(r); break;
                case TScreen.Message: BuildMessage(r); break;
            }
        }

        // ───────────────────────────── KHUNG CHUNG ─────────────────────────────

        void TopBar(RectTransform screen)
        {
            var bar = F.HStack(screen, "TopBar", 12);
            F.Place(bar, new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -30), Vector2.zero);
            F.Button(bar, "MENU CHÍNH", ToMainMenu, 230, 56, false, 26);
            F.Button(bar, "QUAY LẠI", Back, 190, 56, false, 26);
        }

        /// <summary>Kiểu "menu": tiêu đề lớn của game vẫn hiện, cột nút ở giữa.</summary>
        RectTransform Frame(RectTransform screen, string title, string subtitle, bool topBar = true)
        {
            var T = F.Theme;
            if (topBar) TopBar(screen);
            var col = F.VStack(screen, "Column", 14);
            F.Place(col, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -90), Vector2.zero);
            var h = F.Label(col, title, 44, T.accent, TextAlignmentOptions.Center, T.titleFont);
            F.Size(h, 1000, 60);
            if (!string.IsNullOrEmpty(subtitle))
            {
                var s = F.Label(col, subtitle, 26, T.textDim, TextAlignmentOptions.Center, T.headingFont);
                s.textWrappingMode = TextWrappingModes.Normal;
                F.Size(s, 1100, 40);
            }
            return col;
        }

        /// <summary>
        /// Kiểu "trang dữ liệu": ẩn tiêu đề game, nền tối, tiêu đề trang ở trên; trả về vùng nội dung
        /// (từ y = -140 tới đáy - bottom).
        /// </summary>
        RectTransform Page(RectTransform screen, string title, string subtitle, float bottom = 30f)
        {
            var T = F.Theme;
            var dim = F.Image(screen, "Dim", new Color(0.01f, 0.02f, 0.06f, 0.72f));
            F.Stretch(dim.rectTransform);
            TopBar(screen);
            var h = F.Label(screen, title, 46, T.accent, TextAlignmentOptions.Center, T.titleFont);
            F.Place(h.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -26), new Vector2(1100, 60));
            h.overflowMode = TextOverflowModes.Truncate;
            if (!string.IsNullOrEmpty(subtitle))
            {
                var s = F.Label(screen, subtitle, 26, T.textDim, TextAlignmentOptions.Center, T.headingFont);
                F.Place(s.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -88), new Vector2(1300, 40));
                s.overflowMode = TextOverflowModes.Truncate;
            }
            return F.Stretch(F.Rect("Content", screen), 50, bottom, 50, 140);
        }

        static Button Nav(RectTransform col, string label, Action onClick, bool primary = false, bool enabled = true, float width = 540)
        {
            var b = F.Button(col, label, onClick, width, 72, primary);
            SetEnabled(b, enabled);
            return b;
        }

        public static void SetEnabled(Button b, bool enabled)
        {
            b.interactable = enabled;
            var cg = b.GetComponent<CanvasGroup>();
            if (cg == null) cg = b.gameObject.AddComponent<CanvasGroup>();
            cg.alpha = enabled ? 1f : 0.4f;
        }

        static void SegRow(RectTransform col, string label, string[] options, int selected, Action<int> onSelect, float optionWidth)
        {
            var T = F.Theme;
            var row = F.HStack(col, label, 16);
            var l = F.Label(row, label, 28, T.text, TextAlignmentOptions.Right, T.headingFont);
            F.Size(l, 200, 60);
            F.Segmented(row, options, selected, onSelect, optionWidth);
        }

        void BuildMessage(RectTransform r)
        {
            var col = Frame(r, "GIẢI ĐẤU", null);
            var m = F.Label(col, _message ?? "", 30, F.Theme.text, TextAlignmentOptions.Center);
            m.textWrappingMode = TextWrappingModes.Normal;
            F.Size(m, 1100, 160);
            Nav(col, "VỀ MENU CHÍNH", () => _host.ShowMainMenu(), primary: true);
        }

        // ───────────────────────────── CẦU NỐI TRẬN 3D ─────────────────────────────

        static float ColorDist(Color a, Color b) => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b);

        /// <summary>Bắt đầu trận 3D của người chơi ở vòng hiện tại.</summary>
        public void StartPlayerMatch()
        {
            var eng = _ses?.Tour;
            var slot = eng?.PlayerSlot();
            if (slot == null) return;
            var W = _ses.World;
            int me = _ses.PlayerEntry, opp = slot.a == me ? slot.b : slot.a;
            var meE = W.Entry(me); var opE = W.Entry(opp);
            bool dbl = meE.b >= 0 && Match.players.Length >= 4;

            // Người chơi = VĐV theo giới tính đã chọn ở menu (đôi nam nữ); đồng đội AI là người còn lại.
            bool female = PlayerProfile.Female;
            int myAth = W.PlayerAthlete(me, female), mateAth = W.PartnerAthlete(me, female);

            // Độ khó AI CHỈ theo rating của từng VĐV.
            var diffs = new AIDifficulty[4];
            diffs[0] = _settings.DifficultyFor(W.Ratings[myAth]);  // chỉ dùng khi test tự động
            diffs[1] = _settings.DifficultyFor(W.Ratings[opE.a]);
            if (dbl)
            {
                diffs[2] = _settings.DifficultyFor(W.Ratings[mateAth]);
                diffs[3] = _settings.DifficultyFor(W.Ratings[opE.b]);
            }

            // Màu đồng phục đội người chơi = màu quần áo người chơi chọn ở menu.
            var O = W.Athlete(opE.a);
            Color myShirt = PlayerProfile.ShirtColor, oppShirt = TournamentSession.Rgb(O.shirt);
            if (ColorDist(myShirt, oppShirt) < _settings.ui.kitClashThreshold)
                oppShirt = myShirt.grayscale > 0.5f ? new Color(0.12f, 0.2f, 0.45f) : new Color(1f, 0.82f, 0.15f);
            Color myShorts = PlayerProfile.ShortsColor, oppShorts = TournamentSession.Rgb(O.shorts);

            Match.Configure(1, AutoPlayForTest);
            Match.doublesConfig = _settings.doubles;
            Match.ConfigureCustom(dbl, diffs, new[] { W.ShortName(me), W.ShortName(opp) }, new[] { myShirt, oppShirt },
                new[] { W.Country(me), W.Country(opp) });
            Match.UseOfficialBreaks = true;

            // Người chơi: ngoại hình tự chỉnh (menu → NHÂN VẬT). AI: ngoại hình riêng từng VĐV; 2 người cùng đội luôn khác nhau.
            var lookMe = AthleteLook.FromProfile();
            var lookOpp = AthleteLook.From(O, oppShirt, oppShorts);
            if (dbl)
            {
                var lookMate = AthleteLook.From(W.Athlete(mateAth), myShirt, myShorts);
                var lookOppMate = AthleteLook.From(W.Athlete(opE.b), oppShirt, oppShorts);
                var keepMe = lookMe;
                AthleteLook.MakeDistinct(ref keepMe, ref lookMate);   // chỉ chỉnh đồng đội, giữ nguyên nhân vật người chơi
                AthleteLook.MakeDistinct(ref lookOpp, ref lookOppMate);
                PlayerAppearance.Apply(Match.players[2].motor.gameObject, lookMate);
                PlayerAppearance.Apply(Match.players[3].motor.gameObject, lookOppMate);
            }
            PlayerAppearance.Apply(Match.players[0].motor.gameObject, lookMe);
            PlayerAppearance.Apply(Match.players[1].motor.gameObject, lookOpp);

            BroadcastScoreboard.HeaderLogo = eng.S.name;
            BroadcastScoreboard.HeaderLine = $"{_ses.TierName(eng.S)}  ·  {Events.Name(eng.S.ev)}  ·  {eng.Round.name}";

            _ses.MarkMatchStarted(slot.id); // đóng game giữa trận ⇒ lần sau bị xử thua
            InMatch = true;
            StopFlow();
            ConfirmDialog.Close();
            _root.gameObject.SetActive(false);
            _host.SetMenuTitle(true);
            _host.ShowHud();
            Match.StartOfflineMatch();
        }

        /// <summary>GameUI gọi khi trận 3D kết thúc (winnerTeam: 0 = người chơi).</summary>
        public void OnMatchFinished(int winnerTeam)
        {
            if (!InMatch) return;
            var m = Match.Match;
            var r = MatchResultGenerator.FromGames(m.CompletedGames, winnerTeam);
            FinishPlayerMatch(r, _settings.ui.afterMatchDelay);
        }

        /// <summary>Thoát giữa trận ⇒ xử thua (bỏ cuộc). Tỷ số ghi nhận tới thời điểm bỏ cuộc.</summary>
        public void ForfeitCurrentMatch()
        {
            if (!InMatch) return;
            var m = Match.Match;
            var games = new List<GameScore>();
            if (m != null)
            {
                games.AddRange(m.CompletedGames);
                if (!m.IsOver && m.Score[0] + m.Score[1] > 0) games.Add(new GameScore(m.Score[0], m.Score[1]));
            }
            var r = MatchResultGenerator.FromGames(games, 1);
            r.retired = true;
            Match.ResetToIdle();
            FinishPlayerMatch(r, 0f);
        }

        void FinishPlayerMatch(MatchResult r, float delay)
        {
            _ses.Tour.SubmitPlayerResult(r);
            _ses.ClearMatchStarted();
            _lastResult = r;
            _ses.Save();
            InMatch = false;
            StartCoroutine(AfterMatch(delay));
        }

        IEnumerator AfterMatch(float delay)
        {
            if (delay > 0f) yield return new WaitForSecondsRealtime(delay);
            foreach (var p in Match.players) if (p?.motor != null) PlayerAppearance.Reset(p.motor.gameObject);
            BroadcastScoreboard.HeaderLogo = BroadcastScoreboard.HeaderLine = null;
            _host.RestartAttract();
            _host.EnterExternal();
            _root.gameObject.SetActive(true);
            _root.SetAsLastSibling();
            int i = _stack.LastIndexOf(TScreen.Bracket);
            if (i >= 0) _stack.RemoveRange(i + 1, _stack.Count - i - 1);
            Push(TScreen.PostMatch);
        }
    }
}
