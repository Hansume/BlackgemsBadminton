using System.Collections.Generic;
using Badminton.Player;
using Badminton.Scoring;
using Badminton.TournamentMode;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using F = Badminton.UI.UIFactory;

namespace Badminton.UI
{
    /// <summary>
    /// Toàn bộ giao diện game (dựng bằng code lúc chạy):
    /// main menu (Chơi online / Đấu với AI / Tập với tường / Cài đặt), sảnh online (ghép trận nhanh,
    /// tạo phòng, nhập mã phòng, sẵn sàng), cài đặt (âm lượng, đồ hoạ, phím bấm), HUD trong trận
    /// (bảng tỷ số truyền hình, thông báo, ping, replay), tạm dừng và màn hình kết thúc trận.
    /// Khi ở menu, nền phía sau là một trận AI đấu AI để sân luôn "sống".
    /// </summary>
    public class GameUI : MonoBehaviour
    {
        enum Screen { Main, AI, Online, Room, Settings, Hud, Pause, MatchOver, External }

        public UITheme theme;
        public MatchController match;
        public string practiceScene = "Practice_Wall";

        Canvas _canvas;
        GameObject _titleGroup;
        readonly Dictionary<Screen, GameObject> _screens = new Dictionary<Screen, GameObject>();
        Screen _current = Screen.Main, _settingsBack = Screen.Main;
        GameObject _menuBackdrop, _hud;

        // HUD
        CanvasGroup _banner;
        TextMeshProUGUI _bannerText, _ping, _hint, _matchOverText;
        GameObject _replayTag, _letterTop, _letterBottom;
        float _hintUntil;

        // Online
        TextMeshProUGUI _onlineStatus, _roomCode, _roomStatus, _roomPlayers;
        TMP_InputField _codeInput, _ipInput;
        Button _readyButton;
        bool _ready;
        OnlineState _lastOnlineState = OnlineState.Offline;

        // Nghỉ giữa game (đồng hồ + nút bỏ qua)
        GameObject _breakPanel;
        TextMeshProUGUI _breakText;
        Button _skipBreakButton;

        // Chế độ Giải đấu (màn hình riêng, dùng chung canvas)
        TournamentUI _tournament;
        // Màn hình chỉnh nhân vật
        CharacterEditorUI _character;
        Color[] _origColors;

        // Cài đặt – phím
        readonly List<(Button button, InputAction action, int index)> _bindButtons = new List<(Button, InputAction, int)>();

        void Awake()
        {
            F.Theme = theme;
            GameControls.Ensure();
            BuildCanvas();
        }

        void Start()
        {
            BuildMenuBackdrop();
            BuildMain();
            BuildAIMenu();
            BuildOnline();
            BuildRoom();
            BuildSettings();
            BuildHud();
            BuildPause();
            BuildMatchOver();

            GameControls.Pause.performed += OnPausePressed;
            GameControls.CameraToggle.performed += OnCameraToggle;
            match.MatchFinished += OnMatchFinished;
            if (OnlineServices.Current != null) HookOnline(); // đã tạo sẵn (link mời / tham số test)

            _tournament = GetComponent<TournamentUI>();
            if (_tournament == null) _tournament = gameObject.AddComponent<TournamentUI>();
            _tournament.Init(this);
            _character = GetComponent<CharacterEditorUI>();
            if (_character == null) _character = gameObject.AddComponent<CharacterEditorUI>();
            _character.Init(this);
            _origColors = new Color[match.players.Length];
            for (int i = 0; i < _origColors.Length; i++) _origColors[i] = match.players[i].color;

            Show(Screen.Main);
            StartAttractMode();
        }

        void OnDestroy()
        {
            GameControls.Pause.performed -= OnPausePressed;
            GameControls.CameraToggle.performed -= OnCameraToggle;
            if (OnlineServices.Current != null) OnlineServices.Current.Changed -= RefreshOnline;
        }

        // ───────────────────────────── KHUNG ─────────────────────────────

        void BuildCanvas()
        {
            var go = new GameObject("Game UI Canvas");
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 10;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();

            if (FindFirstObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem));
                var module = es.AddComponent<InputSystemUIInputModule>();
                module.AssignDefaultActions();
                es.transform.SetParent(transform, false);
            }
        }

        GameObject NewScreen(Screen s)
        {
            var r = F.Stretch(F.Rect(s + " Screen", _canvas.transform));
            _screens[s] = r.gameObject;
            r.gameObject.SetActive(false);
            return r.gameObject;
        }

        // ───────────────────────────── API cho chế độ Giải đấu ─────────────────────────────

        public Canvas UICanvas => _canvas;

        /// <summary>Ẩn mọi màn hình của GameUI (giữ nền menu) để TournamentUI hiển thị màn hình riêng.</summary>
        public void EnterExternal() => Show(Screen.External);

        /// <summary>Hiện HUD trong trận (trận đấu của chế độ Giải đấu).</summary>
        public void ShowHud() => Show(Screen.Hud);

        /// <summary>Chạy lại trận AI nền phía sau menu.</summary>
        public void RestartAttract() => StartAttractMode();

        /// <summary>Ẩn/hiện tiêu đề lớn "SHUTTLE ARENA" (các trang dữ liệu của Giải đấu cần chỗ).</summary>
        public void SetMenuTitle(bool visible) { if (_titleGroup != null) _titleGroup.SetActive(visible); }

        /// <summary>Về màn hình chính (từ bất kỳ màn hình nào của chế độ Giải đấu).</summary>
        public void ShowMainMenu()
        {
            if (_current == Screen.Hud || _current == Screen.Pause || _current == Screen.MatchOver)
            {
                match.SetPaused(false);
                StartAttractMode();
            }
            _tournament?.Close();
            Show(Screen.Main);
        }

        void Show(Screen s)
        {
            _current = s;
            foreach (var kv in _screens) kv.Value.SetActive(kv.Key == s);
            bool inGame = s == Screen.Hud || s == Screen.Pause || s == Screen.MatchOver;
            _menuBackdrop.SetActive(!inGame);
            _hud.SetActive(inGame);
            KeyboardPlayerInput.GameplayEnabled = s == Screen.Hud;
            if (s == Screen.Hud) _hintUntil = Time.unscaledTime + 8f;
            if (s == Screen.Settings) RefreshBindings();
        }

        void BuildMenuBackdrop()
        {
            var T = F.Theme;
            var r = F.Stretch(F.Rect("Menu Backdrop", _canvas.transform));
            _menuBackdrop = r.gameObject;
            var dim = F.Image(r, "Dim", new Color(0.02f, 0.04f, 0.09f, 0.55f));
            F.Stretch(dim.rectTransform);
            dim.raycastTarget = false;

            var tg = F.Stretch(F.Rect("Title Group", r));
            _titleGroup = tg.gameObject;
            var title = F.Label(tg, T.gameTitle, 120, T.text, TextAlignmentOptions.Center, T.titleFont);
            F.Place(title.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -70), new Vector2(1400, 140));
            title.characterSpacing = 6;
            var bar = F.Image(tg, "TitleBar", T.accent);
            F.Place(bar.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -212), new Vector2(360, 8));
            var sub = F.Label(tg, "CẦU LÔNG 3D ONLINE", 34, T.textDim, TextAlignmentOptions.Center, T.headingFont);
            F.Place(sub.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -228), new Vector2(900, 50));
            sub.characterSpacing = 12;
        }

        RectTransform MenuColumn(GameObject screen, string heading)
        {
            var col = F.VStack(screen.transform, "Column", 16);
            F.Place(col, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -90), Vector2.zero);
            if (!string.IsNullOrEmpty(heading))
            {
                var h = F.Label(col, heading, 44, F.Theme.accent, TextAlignmentOptions.Center, F.Theme.titleFont);
                F.Size(h, 700, 64);
            }
            return col;
        }

        // ───────────────────────────── MAIN MENU ─────────────────────────────

        void BuildMain()
        {
            var s = NewScreen(Screen.Main);
            var col = MenuColumn(s, null);

            var nameRow = F.HStack(col, "Name", 12);
            var nl = F.Label(nameRow, "TÊN:", 30, F.Theme.textDim, TextAlignmentOptions.Right, F.Theme.headingFont);
            F.Size(nl, 90, 70);
            var name = F.Input(nameRow, "Tên của bạn", GameSettings.PlayerName, 16, 370);
            name.onEndEdit.AddListener(GameSettings.SetPlayerName);

            F.Button(col, "CHƠI ONLINE", () => { HookOnline(); Show(Screen.Online); }, primary: true);
            var tour = F.Button(col, "GIẢI ĐẤU", () => { EnterExternal(); _tournament.Open(); }, primary: true);
            // Nhãn "MỚI" ở góc để nút nổi bật.
            var tag = F.Image(tour.transform, "Tag", F.Theme.danger);
            F.Place(tag.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(10, 14), new Vector2(84, 32));
            tag.raycastTarget = false;
            var tagText = F.Label(tag.transform, "MỚI", 22, Color.white, TextAlignmentOptions.Center, F.Theme.titleFont);
            F.Stretch(tagText.rectTransform);
            F.Button(col, "NHÂN VẬT", () => { EnterExternal(); _character.Open(); });
            F.Button(col, "ĐẤU VỚI AI", () => Show(Screen.AI));
            F.Button(col, "TẬP VỚI TƯỜNG", () => { match.ResetToIdle(); SceneManager.LoadScene(practiceScene); });
            F.Button(col, "CÀI ĐẶT", () => OpenSettings(Screen.Main));

            var help = F.Label(s.transform, "WASD di chuyển · J Clear · K Smash · L Drop · I Net/Lift · Space giao cầu", 26,
                               F.Theme.textDim, TextAlignmentOptions.Center);
            F.Place(help.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 40), new Vector2(1600, 40));
        }

        void BuildAIMenu()
        {
            var s = NewScreen(Screen.AI);
            var col = MenuColumn(s, "ĐẤU VỚI AI");
            string[] names = { "DỄ", "THƯỜNG", "KHÓ", "RẤT KHÓ" };
            int count = Mathf.Min(names.Length, match.difficulties != null ? match.difficulties.Length : 3);
            for (int i = 0; i < count; i++)
            {
                int d = i;
                F.Button(col, names[i], () => StartOffline(d, false)); // 3 mức cùng kiểu, không gợi ý mức nào
            }
            F.Button(col, "XEM AI ĐẤU AI", () => StartOffline(1, true));
            F.Button(col, "QUAY LẠI", () => Show(Screen.Main), 300, 64);
        }

        void StartOffline(int difficulty, bool watch)
        {
            match.Configure(difficulty, watch);
            match.StartOfflineMatch();
            if (!watch)
            {
                // Nhân vật người chơi (menu → NHÂN VẬT): ngoại hình + cờ quốc tịch trên bảng tỷ số.
                PlayerAppearance.Apply(match.players[0].motor.gameObject, AthleteLook.FromProfile());
                match.players[0].color = PlayerProfile.ShirtColor;
            }
            Show(Screen.Hud);
        }

        /// <summary>Nền menu: một trận AI đấu AI chạy phía sau.</summary>
        void StartAttractMode()
        {
            // Trận nền: nhân vật về ngoại hình / màu gốc.
            for (int i = 0; i < match.players.Length; i++)
            {
                if (match.players[i]?.motor == null) continue;
                PlayerAppearance.Reset(match.players[i].motor.gameObject);
                if (_origColors != null && i < _origColors.Length) match.players[i].color = _origColors[i];
            }
            match.Configure(1, true);
            match.StartOfflineMatch();
            match.players[0].name = "AI XANH";
        }

        // ───────────────────────────── ONLINE ─────────────────────────────

        void BuildOnline()
        {
            var s = NewScreen(Screen.Online);
            var col = MenuColumn(s, "CHƠI ONLINE");

            F.Button(col, "GHÉP TRẬN NHANH", () => Online(o => o.QuickMatch()), primary: true);
            F.Button(col, "TẠO PHÒNG", () => Online(o => o.CreateRoom()));

            var joinRow = F.HStack(col, "Join", 12);
            _codeInput = F.Input(joinRow, "MÃ PHÒNG", "", 6, 250);
            _codeInput.characterValidation = TMP_InputField.CharacterValidation.Alphanumeric;
            _codeInput.onValueChanged.AddListener(v => { if (v != v.ToUpperInvariant()) _codeInput.text = v.ToUpperInvariant(); });
            F.Button(joinRow, "VÀO PHÒNG", () => Online(o => o.JoinRoom(_codeInput.text)), 198, 70, false, 30);

            var lan = F.Label(col, "— KẾT NỐI TRỰC TIẾP (LAN / TEST) —", 24, F.Theme.textDim, TextAlignmentOptions.Center, F.Theme.headingFont);
            F.Size(lan, 700, 40);
            var lanRow = F.HStack(col, "LAN", 12);
            _ipInput = F.Input(lanRow, "127.0.0.1", "127.0.0.1", 64, 250, 64);
            F.Button(lanRow, "VÀO", () => Online(o => o.JoinDirect(_ipInput.text, 7777)), 96, 64, false, 28);
            F.Button(lanRow, "TẠO", () => Online(o => o.HostDirect(7777)), 96, 64, false, 28);

            _onlineStatus = F.Label(col, "", 28, F.Theme.text, TextAlignmentOptions.Center);
            _onlineStatus.textWrappingMode = TextWrappingModes.Normal;
            F.Size(_onlineStatus, 900, 80);
            F.Button(col, "QUAY LẠI", () => { OnlineServices.Current?.Leave(); Show(Screen.Main); }, 300, 64);
        }

        bool _onlineHooked;

        /// <summary>Tạo dịch vụ online (NetworkManager) khi người chơi vào mục "Chơi online" lần đầu.</summary>
        void HookOnline()
        {
            if (_onlineHooked) return;
            var o = OnlineServices.Ensure();
            if (o == null) return;
            o.Changed += RefreshOnline;
            _onlineHooked = true;
            RefreshOnline();
        }

        void Online(System.Action<IOnlineService> action)
        {
            HookOnline();
            var o = OnlineServices.Current;
            if (o == null) { _onlineStatus.text = "Module online chưa được cài đặt trong bản build này."; return; }
            match.ResetToIdle(); // dừng trận nền
            match.Configure(match.DifficultyIndex, false); // online: người chơi tự điều khiển
            action(o);
            RefreshOnline();
        }

        void BuildRoom()
        {
            var s = NewScreen(Screen.Room);
            var col = MenuColumn(s, "PHÒNG CHỜ");
            _roomCode = F.Label(col, "MÃ PHÒNG: ------", 56, F.Theme.text, TextAlignmentOptions.Center, F.Theme.titleFont);
            F.Size(_roomCode, 900, 80);
            _roomPlayers = F.Label(col, "", 34, F.Theme.text, TextAlignmentOptions.Center, F.Theme.headingFont);
            _roomPlayers.textWrappingMode = TextWrappingModes.Normal;
            F.Size(_roomPlayers, 900, 110);
            _readyButton = F.Button(col, "SẴN SÀNG", () =>
            {
                _ready = !_ready;
                OnlineServices.Current?.SetReady(_ready);
                F.SetButtonLabel(_readyButton, _ready ? "HUỶ SẴN SÀNG" : "SẴN SÀNG");
                F.SetPrimary(_readyButton, !_ready);
            }, primary: true);
            _roomStatus = F.Label(col, "", 26, F.Theme.textDim, TextAlignmentOptions.Center);
            _roomStatus.textWrappingMode = TextWrappingModes.Normal;
            F.Size(_roomStatus, 900, 70);
            F.Button(col, "RỜI PHÒNG", () => { OnlineServices.Current?.Leave(); Show(Screen.Online); }, 300, 64);
        }

        void RefreshOnline()
        {
            var o = OnlineServices.Current;
            if (o == null) return;

            _onlineStatus.text = o.CloudAvailable || o.State != OnlineState.Offline
                ? o.Status
                : o.Status + "\n<size=22>(Chưa cấu hình Unity Gaming Services – vẫn chơi được bằng Kết nối trực tiếp)</size>";

            _roomCode.text = string.IsNullOrEmpty(o.RoomCode) ? "PHÒNG LAN" : $"MÃ PHÒNG: <color=#{ColorUtility.ToHtmlStringRGB(F.Theme.accent)}>{o.RoomCode}</color>";
            var sb = new System.Text.StringBuilder();
            foreach (var p in o.Players)
                sb.AppendLine($"{(p.IsHost ? "[CHỦ PHÒNG] " : "")}{p.Name}{(p.IsLocal ? " (bạn)" : "")}  —  " +
                              (p.Ready ? "<color=#3CC86E>SẴN SÀNG</color>" : "<color=#AAB9D2>chưa sẵn sàng</color>"));
            if (o.Players.Count < 2) sb.AppendLine("<color=#AAB9D2>Đang chờ đối thủ...</color>");
            _roomPlayers.text = sb.ToString();
            _roomStatus.text = o.Status;

            // Điều hướng theo trạng thái dịch vụ online.
            if (o.State != _lastOnlineState)
            {
                if (o.State == OnlineState.InRoom)
                {
                    _ready = false;
                    F.SetButtonLabel(_readyButton, "SẴN SÀNG");
                    F.SetPrimary(_readyButton, true);
                    Show(Screen.Room);
                }
                else if (o.State == OnlineState.InMatch) Show(Screen.Hud);
                else if ((o.State == OnlineState.Offline || o.State == OnlineState.Error) &&
                         (_lastOnlineState == OnlineState.InMatch || _lastOnlineState == OnlineState.InRoom))
                {
                    Show(Screen.Online);
                    StartAttractMode();
                }
                _lastOnlineState = o.State;
            }
        }

        // ───────────────────────────── CÀI ĐẶT ─────────────────────────────

        void OpenSettings(Screen back) { _settingsBack = back; Show(Screen.Settings); }

        void BuildSettings()
        {
            var s = NewScreen(Screen.Settings);
            var T = F.Theme;
            var head = F.Label(s.transform, "CÀI ĐẶT", 48, T.accent, TextAlignmentOptions.Center, T.titleFont);
            F.Place(head.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -280), new Vector2(800, 64));

            var panel = F.Image(s.transform, "Panel", T.navy);
            F.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -120), new Vector2(1500, 600));

            // Cột trái: âm thanh + đồ hoạ.
            var left = F.VStack(panel.transform, "Left", 10, TextAnchor.UpperLeft);
            F.Place(left, new Vector2(0, 1), new Vector2(0, 1), new Vector2(50, -36), Vector2.zero);
            void SliderRow(string label, float v, System.Action<float> set)
            {
                var row = F.HStack(left, label, 20);
                var l = F.Label(row, label, 30, T.text, TextAlignmentOptions.Left, T.headingFont);
                F.Size(l, 230, 56);
                F.Slider(row, v, set, 380);
            }
            SliderRow("ÂM LƯỢNG", GameSettings.MasterVolume, GameSettings.SetMaster);
            SliderRow("HIỆU ỨNG", GameSettings.SfxVolume, GameSettings.SetSfx);
            SliderRow("KHÁN GIẢ", GameSettings.CrowdVolume, GameSettings.SetCrowd);

            void ChoiceRow(string label, string[] opts, int sel, System.Action<int> set)
            {
                var row = F.HStack(left, label, 20);
                var l = F.Label(row, label, 30, T.text, TextAlignmentOptions.Left, T.headingFont);
                F.Size(l, 230, 64);
                F.Segmented(row, opts, sel, set, 186);
            }
            ChoiceRow("ĐỒ HOẠ", new[] { "THẤP", "CAO" }, (int)GameSettings.GraphicsQuality, i => GameSettings.SetQuality((GameSettings.Quality)i));
            ChoiceRow("REPLAY SMASH", new[] { "TẮT", "BẬT" }, GameSettings.SmashReplay ? 1 : 0, i => GameSettings.SetReplay(i == 1));

            // Cột phải: phím bấm.
            var right = F.VStack(panel.transform, "Right", 6, TextAnchor.UpperLeft);
            F.Place(right, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-50, -28), Vector2.zero);
            var kh = F.Label(right, "PHÍM BẤM (bấm để đổi, Esc huỷ)", 26, T.textDim, TextAlignmentOptions.Left, T.headingFont);
            F.Size(kh, 600, 40);
            foreach (var (label, action, index) in GameControls.Rebindable)
            {
                var row = F.HStack(right, label, 16);
                var l = F.Label(row, label, 26, T.text, TextAlignmentOptions.Left);
                F.Size(l, 360, 46);
                var a = action; int idx = index;
                Button b = null;
                b = F.Button(row, "-", () =>
                {
                    F.SetButtonLabel(b, "...");
                    GameControls.StartRebind(a, idx, RefreshBindings);
                }, 200, 46, false, 26);
                _bindButtons.Add((b, a, idx));
            }

            var bottom = F.HStack(s.transform, "Bottom", 20);
            F.Place(bottom, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 50), Vector2.zero);
            F.Button(bottom, "MẶC ĐỊNH PHÍM", () => { GameControls.ResetAll(); RefreshBindings(); }, 340, 64);
            F.Button(bottom, "XONG", () => Show(_settingsBack), 300, 64, true);
        }

        void RefreshBindings()
        {
            foreach (var (button, action, index) in _bindButtons)
                F.SetButtonLabel(button, GameControls.DisplayName(action, index));
        }

        // ───────────────────────────── HUD ─────────────────────────────

        void BuildHud()
        {
            var T = F.Theme;
            var r = F.Stretch(F.Rect("HUD", _canvas.transform));
            _hud = r.gameObject;
            r.SetAsFirstSibling(); // HUD nằm dưới các menu

            // Dải đen điện ảnh khi replay.
            _letterTop = F.Image(r, "LetterTop", Color.black).gameObject;
            F.Place((RectTransform)_letterTop.transform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(4000, 90));
            _letterBottom = F.Image(r, "LetterBottom", Color.black).gameObject;
            F.Place((RectTransform)_letterBottom.transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), Vector2.zero, new Vector2(4000, 90));

            BroadcastScoreboard.Build(r, match);

            // Thông báo giữa màn hình.
            var banner = F.Image(r, "Banner", T.navy);
            F.Place(banner.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 120), new Vector2(1100, 96));
            var accent = F.Image(banner.transform, "Accent", T.accent);
            F.Place(accent.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), Vector2.zero, new Vector2(1100, 6));
            _bannerText = F.Label(banner.transform, "", 46, T.text, TextAlignmentOptions.Center, T.titleFont);
            F.Stretch(_bannerText.rectTransform, 20, 0, 20, 0);
            _banner = banner.gameObject.AddComponent<CanvasGroup>();
            _banner.alpha = 0f;
            _banner.blocksRaycasts = false;

            // Replay.
            var rep = F.Image(r, "Replay", T.danger);
            F.Place(rep.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-40, -110), new Vector2(220, 64));
            var rt = F.Label(rep.transform, "REPLAY", 40, Color.white, TextAlignmentOptions.Center, T.titleFont);
            F.Stretch(rt.rectTransform);
            _replayTag = rep.gameObject;

            _ping = F.Label(r, "", 26, T.textDim, TextAlignmentOptions.Right, T.headingFont);
            F.Place(_ping.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-40, -36), new Vector2(400, 40));

            _hint = F.Label(r, "", 26, T.textDim, TextAlignmentOptions.Center);
            F.Place(_hint.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 26), new Vector2(1800, 40));

            // Đồng hồ nghỉ (giải đấu: 60 s ở điểm 11, 120 s giữa game) + nút bỏ qua.
            var bp = F.Image(r, "Break", T.navy);
            F.Place(bp.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 90), new Vector2(760, 90));
            _breakPanel = bp.gameObject;
            var bpRow = F.HStack(bp.transform, "Row", 24);
            F.Place(bpRow, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            _breakText = F.Label(bpRow, "", 36, T.text, TextAlignmentOptions.Left, T.titleFont);
            F.Size(_breakText, 400, 70);
            _skipBreakButton = F.Button(bpRow, "BỎ QUA (ENTER)", () => match.SkipBreak(), 280, 64, true, 28);
            _breakPanel.SetActive(false);
        }

        void Update()
        {
            if (!_hud.activeSelf) return;

            // Thông báo: hiện rồi mờ dần.
            float left = match.AnnouncementUntil - Time.time;
            _bannerText.text = match.Announcement;
            _banner.alpha = Mathf.MoveTowards(_banner.alpha, left > 0f ? 1f : 0f, Time.unscaledDeltaTime * 6f);

            // Đồng hồ nghỉ: chỉ hiện với nghỉ dài (giải đấu); nghỉ nhanh vài giây thì banner là đủ.
            bool showBreak = match.UseOfficialBreaks && match.BreakRemaining > 0f;
            _breakPanel.SetActive(showBreak);
            if (showBreak)
            {
                int sec = Mathf.CeilToInt(match.BreakRemaining);
                _breakText.text = $"{(match.BreakIsBetweenGames ? "NGHỈ GIỮA GAME" : "NGHỈ 11 ĐIỂM")}  <color=#{ColorUtility.ToHtmlStringRGB(F.Theme.accent)}>{sec / 60}:{sec % 60:00}</color>";
                _skipBreakButton.interactable = match.CanSkipBreak;
                var kb = Keyboard.current;
                if (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) match.SkipBreak();
            }

            bool replay = match.ReplayPlaying;
            _replayTag.SetActive(replay);
            _letterTop.SetActive(replay);
            _letterBottom.SetActive(replay);

            var o = OnlineServices.Current;
            if (match.IsOnline && o != null)
            {
                int ms = o.PingMs;
                string col = ms < 80 ? "3CC86E" : ms < 160 ? "FFC828" : "DC323C";
                _ping.text = $"PING <color=#{col}>{ms} ms</color>";
            }
            else _ping.text = "";

            bool showHint = Time.unscaledTime < _hintUntil && !match.AutoPlayLocal;
            _hint.text = showHint
                ? $"{Key(GameControls.Move, 1)}{Key(GameControls.Move, 3)}{Key(GameControls.Move, 2)}{Key(GameControls.Move, 4)} di chuyển · " +
                  $"{Key(GameControls.Shot(Core.ShotType.Clear), 0)} cầu dài (Clear/Lift) · {Key(GameControls.Shot(Core.ShotType.Smash), 0)} Smash · " +
                  $"{Key(GameControls.Shot(Core.ShotType.Drop), 0)} cầu ngắn (Drop/Net) · " +
                  $"{Key(GameControls.Shot(Core.ShotType.Serve), 0)} giao cầu (giữ lên = giao ngắn) · C camera · Esc menu"
                : "";
        }

        static string Key(InputAction a, int i) => GameControls.DisplayName(a, i);

        // ───────────────────────────── TẠM DỪNG / KẾT THÚC ─────────────────────────────

        void BuildPause()
        {
            var s = NewScreen(Screen.Pause);
            var dim = F.Image(s.transform, "Dim", new Color(0, 0, 0, 0.55f));
            F.Stretch(dim.rectTransform);
            dim.transform.SetAsFirstSibling();
            var col = MenuColumn(s, "TẠM DỪNG");
            F.Button(col, "TIẾP TỤC", ResumeGame, primary: true);
            F.Button(col, "CÀI ĐẶT", () => OpenSettings(Screen.Pause));
            _pauseExit = F.Button(col, "VỀ MENU", ExitToMenu);
        }

        Button _pauseExit;

        void BuildMatchOver()
        {
            var s = NewScreen(Screen.MatchOver);
            var col = MenuColumn(s, "KẾT THÚC TRẬN");
            _matchOverText = F.Label(col, "", 56, F.Theme.text, TextAlignmentOptions.Center, F.Theme.titleFont);
            F.Size(_matchOverText, 1200, 90);
            F.Button(col, "ĐẤU LẠI", () => { if (match.IsOnline) ExitToMenu(); else { match.StartOfflineMatch(); Show(Screen.Hud); } }, primary: true);
            F.Button(col, "VỀ MENU", ExitToMenu);
        }

        void OnMatchFinished(int winner)
        {
            if (_tournament != null && _tournament.InMatch) { _tournament.OnMatchFinished(winner); return; }
            if (_current != Screen.Hud) return;
            var m = match.Match;
            _matchOverText.text = $"{match.players[winner].name.ToUpperInvariant()} THẮNG {m.Games[winner]}-{m.Games[1 - winner]}";
            CancelInvoke(nameof(ShowMatchOver));
            Invoke(nameof(ShowMatchOver), 3f);
        }

        void ShowMatchOver() { if (_current == Screen.Hud) Show(Screen.MatchOver); }

        void OnPausePressed(InputAction.CallbackContext _)
        {
            if (_current == Screen.Pause && ConfirmDialog.IsOpen) { ConfirmDialog.Cancel(); return; }
            switch (_current)
            {
                case Screen.Hud:
                    if (!match.IsOnline) match.SetPaused(true);
                    if (_pauseExit != null) F.SetButtonLabel(_pauseExit, _tournament != null && _tournament.InMatch ? "BỎ CUỘC (XỬ THUA)" : "VỀ MENU");
                    Show(Screen.Pause);
                    break;
                case Screen.Pause: ResumeGame(); break;
                case Screen.Settings: Show(_settingsBack); break;
                case Screen.AI:
                case Screen.Online: Show(Screen.Main); break;
            }
        }

        void OnCameraToggle(InputAction.CallbackContext _)
        {
            if (_current == Screen.Hud && match.cameraRig != null)
                match.cameraRig.SetBroadcast(!match.cameraRig.broadcastCamera);
        }

        void ResumeGame()
        {
            match.SetPaused(false);
            Show(Screen.Hud);
        }

        void ExitToMenu()
        {
            if (_tournament != null && _tournament.InMatch)
            {
                ConfirmDialog.Show(_canvas.transform, "BỎ CUỘC?",
                    "Thoát trận đang đánh dở sẽ bị XỬ THUA. Kết quả được ghi vào nhánh đấu.",
                    "BỎ CUỘC", () => { match.SetPaused(false); _tournament.ForfeitCurrentMatch(); },
                    "ĐÁNH TIẾP", null, dangerous: true);
                return;
            }
            match.SetPaused(false);
            if (match.IsOnline) OnlineServices.Current?.Leave();
            match.IsOnline = false;
            Show(Screen.Main);
            StartAttractMode();
        }
    }
}
