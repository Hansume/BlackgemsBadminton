using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Badminton.Scoring;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Badminton.Networking
{
    /// <summary>
    /// Dịch vụ online: Unity Gaming Services (đăng nhập ẩn danh + Sessions = Lobby + Relay qua WSS)
    /// hoặc kết nối trực tiếp (WebSocket, LAN/test). Sau khi kết nối, phòng chờ (tên, Sẵn sàng) và trận đấu
    /// chạy trên Netcode for GameObjects bằng named message.
    ///
    /// Tự khởi tạo: khi scene có MatchController, tạo GameObject chứa NetworkManager + UnityTransport
    /// + OnlineService + NetMatchSync (không cần đặt tay trong scene).
    /// </summary>
    public class OnlineService : MonoBehaviour, IOnlineService
    {
        const string SessionType = "shuttle-arena-1v1";
        const float ReconnectWindow = 30f;

        public MatchController match;

        NetworkManager _nm;
        UnityTransport _transport;
        NetMatchSync _sync;
        ISession _session;

        OnlineState _state = OnlineState.Offline;
        string _status = "";
        bool _isHost, _directMode;
        string _directAddress; ushort _directPort;
        ulong _remoteId = ulong.MaxValue;
        string _hostName = "", _clientName = "";
        bool _hostReady, _clientReady, _hasClient;
        bool _inMatch;
        float _reconnectDeadline = -1f;
        bool _reconnecting;
        int _matchSeed;
        readonly List<OnlinePlayerInfo> _players = new List<OnlinePlayerInfo>();

        public OnlineState State => _state;
        public string Status => _status;
        public string RoomCode => _session != null ? _session.Code : "";
        public bool IsHost => _isHost;
        public bool CloudAvailable => !string.IsNullOrEmpty(Application.cloudProjectId);
        public IReadOnlyList<OnlinePlayerInfo> Players => _players;
        public int PingMs => _sync != null && _nm != null && _nm.IsListening
            ? _sync.PingMs(_isHost ? (_remoteId == ulong.MaxValue ? 0 : _remoteId) : NetworkManager.ServerClientId) : 0;

        public event Action Changed;

        // ───────────────────────────── TỰ KHỞI TẠO ─────────────────────────────

        /// <summary>
        /// Không tạo NetworkManager lúc khởi động: chỉ đăng ký Factory (tạo khi bấm "Chơi online").
        /// Ngoại lệ: mở bằng link mời (?room=) hoặc tham số test (-bmHost/-bmJoin/-bmCreate/-bmCode) ⇒ tạo ngay.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook()
        {
            OnlineServices.Factory = EnsureForScene;
            SceneManager.sceneLoaded += (_, __) => { if (NeedsAutoStart()) EnsureForScene(); };
        }

        static bool NeedsAutoStart()
        {
            var args = Environment.GetCommandLineArgs();
            foreach (var a in new[] { "-bmHost", "-bmJoin", "-bmCreate", "-bmCode" })
                if (Array.IndexOf(args, a) >= 0) return true;
            return !string.IsNullOrEmpty(QueryParam(Application.absoluteURL ?? "", "room"));
        }

        static IOnlineService EnsureForScene()
        {
            var existing = FindFirstObjectByType<OnlineService>();
            if (existing != null) return existing;
            var mc = FindFirstObjectByType<MatchController>();
            if (mc == null) return null;

            var go = new GameObject("Online (NetworkManager)");
            var transport = go.AddComponent<UnityTransport>();
            var nm = go.AddComponent<NetworkManager>();
            nm.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                TickRate = (uint)SimulationBootstrap.TickRate,
                EnableSceneManagement = false,
                ConnectionApproval = false,
            };
            var sync = go.AddComponent<NetMatchSync>();
            sync.match = mc;
            var svc = go.AddComponent<OnlineService>();
            svc.match = mc;
            return svc;
        }

        void Awake()
        {
            _nm = GetComponent<NetworkManager>();
            _transport = GetComponent<UnityTransport>();
            _sync = GetComponent<NetMatchSync>();
            OnlineServices.Current = this;

            _nm.OnServerStarted += OnServerStarted;
            _nm.OnClientStarted += OnClientStarted;
            _nm.OnClientConnectedCallback += OnClientConnected;
            _nm.OnClientDisconnectCallback += OnClientDisconnected;
        }

        bool _autoReady;

        /// <summary>
        /// Tham số dòng lệnh để test mạng tự động (không cần người bấm):
        ///   -bmHost         mở phòng LAN cổng 7777
        ///   -bmJoin &lt;ip&gt;    kết nối tới phòng LAN
        ///   -bmCreate       tạo phòng online (Relay + Lobby), ghi mã phòng ra log
        ///   -bmCode &lt;mã&gt;    vào phòng online (Relay + Lobby) bằng mã 6 ký tự
        ///   -bmAI           AI điều khiển nhân vật của máy này
        /// Cả hai trường hợp đều tự bấm "Sẵn sàng".
        /// </summary>
        System.Collections.IEnumerator Start()
        {
            // Ghi log mỗi điểm khi chơi online (tiện kiểm tra bản build qua file log).
            match.RallyEnded += r =>
            {
                if (!match.IsOnline) return;
                Debug.Log($"[Online] RALLY {r.Reason} winner={r.Winner} score={match.Match?.Score[0]}-{match.Match?.Score[1]} ping={PingMs}ms host={_isHost}");
            };

            var args = Environment.GetCommandLineArgs();
            int join = Array.IndexOf(args, "-bmJoin");
            int code = Array.IndexOf(args, "-bmCode");
            bool host = Array.IndexOf(args, "-bmHost") >= 0;
            bool create = Array.IndexOf(args, "-bmCreate") >= 0;

            // Link mời trên web: https://.../index.html?room=ABC123  (thêm &ai=1 để AI tự chơi – dùng test).
            string url = Application.absoluteURL ?? "";
            string urlRoom = QueryParam(url, "room");
            bool urlAi = QueryParam(url, "ai") == "1";
            if (!string.IsNullOrEmpty(urlRoom))
            {
                Debug.Log($"[Online] Link mời: phòng {urlRoom} (AI={urlAi})");
                yield return new WaitForSecondsRealtime(1f);
                match.ResetToIdle();
                match.Configure(1, urlAi);
                if (urlAi) _autoReady = true;
                JoinRoom(urlRoom);
                yield break;
            }

            if (join < 0 && code < 0 && !host && !create) yield break;

            yield return new WaitForSecondsRealtime(1f); // đợi menu dựng xong
            match.ResetToIdle();
            match.Configure(1, Array.IndexOf(args, "-bmAI") >= 0);
            _autoReady = true;
            if (host) HostDirect(7777);
            else if (create)
            {
                CreateRoom(); // qua Relay + Lobby; mã phòng được ghi ra log để máy kia vào
                while (_state == OnlineState.Connecting) yield return null;
                Debug.Log("[Online] ROOM_CODE=" + RoomCode + " status=" + _status);
            }
            else if (code >= 0 && code + 1 < args.Length) JoinRoom(args[code + 1]); // qua Relay + Lobby
            else JoinDirect(join + 1 < args.Length ? args[join + 1] : "127.0.0.1", 7777);
        }

        static string QueryParam(string url, string key)
        {
            int q = url.IndexOf('?');
            if (q < 0) return null;
            foreach (var part in url.Substring(q + 1).Split('&', '#'))
            {
                var kv = part.Split('=');
                if (kv.Length == 2 && kv[0] == key) return Uri.UnescapeDataString(kv[1]);
            }
            return null;
        }

        /// <summary>Link mời cho phòng hiện tại (bản web) – người nhận mở link là vào phòng.</summary>
        public string InviteLink
        {
            get
            {
                if (string.IsNullOrEmpty(RoomCode) || Application.platform != RuntimePlatform.WebGLPlayer) return "";
                string baseUrl = Application.absoluteURL ?? "";
                int q = baseUrl.IndexOf('?');
                if (q >= 0) baseUrl = baseUrl.Substring(0, q);
                return baseUrl + "?room=" + RoomCode;
            }
        }

        /// <summary>Test tự động: bấm Sẵn sàng ngay khi phòng có đủ 2 người.</summary>
        public void EnableAutoReady() => _autoReady = true;

        void LateUpdate()
        {
            if (_autoReady && _state == OnlineState.InRoom && (_isHost ? _hasClient && !_hostReady : !_clientReady && _nm.IsConnectedClient))
                SetReady(true);
        }

        void OnDestroy()
        {
            if (ReferenceEquals(OnlineServices.Current, this)) OnlineServices.Current = null;
            if (_nm != null && _nm.IsListening) _nm.Shutdown();
        }

        void Update()
        {
            // Đếm ngược chờ kết nối lại (thời gian thực – không phụ thuộc Time.timeScale).
            if (_reconnectDeadline > 0f)
            {
                float left = _reconnectDeadline - Time.unscaledTime;
                if (_isHost)
                {
                    match.Announce($"ĐỐI THỦ MẤT KẾT NỐI – chờ {Mathf.CeilToInt(Mathf.Max(0, left))} giây", 1.1f);
                    if (left <= 0f)
                    {
                        _reconnectDeadline = -1f;
                        _sync.End();
                        _inMatch = false;
                        match.Forfeit(0, "ĐỐI THỦ BỎ CUỘC");
                        SetStatus(OnlineState.InRoom, "Đối thủ đã rời trận.");
                    }
                }
                else if (left <= 0f)
                {
                    _reconnectDeadline = -1f;
                    Fail("Không kết nối lại được với chủ phòng.");
                }
            }
        }

        // ───────────────────────────── IOnlineService ─────────────────────────────

        public void QuickMatch() => Run("Đang tìm trận...", async () =>
        {
            await EnsureSignedIn();
            var quick = new QuickJoinOptions { CreateSession = true, Timeout = TimeSpan.FromSeconds(4) };
            _session = await MultiplayerService.Instance.MatchmakeSessionAsync(quick, SessionOptions(isPrivate: false));
            AfterSessionJoined();
        });

        public void CreateRoom() => Run("Đang tạo phòng...", async () =>
        {
            await EnsureSignedIn();
            _session = await MultiplayerService.Instance.CreateSessionAsync(SessionOptions(isPrivate: true));
            AfterSessionJoined();
        });

        public void JoinRoom(string code) => Run("Đang vào phòng...", async () =>
        {
            code = (code ?? "").Trim().ToUpperInvariant();
            if (code.Length < 6) throw new Exception("Mã phòng gồm 6 ký tự.");
            await EnsureSignedIn();
            Step("Đang vào phòng " + code + "...");
            _session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code);
            Step("Đã vào phòng " + code);
            AfterSessionJoined();
        });

        public void HostDirect(ushort port)
        {
            if (Application.platform == RuntimePlatform.WebGLPlayer)
            {
                SetStatus(OnlineState.Error, "Trình duyệt không thể làm chủ phòng LAN. Hãy dùng Tạo phòng (Relay) hoặc host từ bản PC.");
                return;
            }
            ResetRoom();
            _directMode = true;
            _isHost = true;
            _transport.UseWebSockets = true; // WebSocket để cả bản WebGL cũng vào được
            _transport.SetConnectionData("127.0.0.1", port, "0.0.0.0");
            if (!_nm.StartHost()) { Fail("Không mở được cổng " + port); return; }
            SetStatus(OnlineState.InRoom, $"Đang chờ đối thủ kết nối tới cổng {port}...");
        }

        public void JoinDirect(string address, ushort port)
        {
            ResetRoom();
            _directMode = true;
            _isHost = false;
            _directAddress = string.IsNullOrWhiteSpace(address) ? "127.0.0.1" : address.Trim();
            _directPort = port;
            StartDirectClient();
            SetStatus(OnlineState.Connecting, $"Đang kết nối {_directAddress}:{port}...");
        }

        void StartDirectClient()
        {
            _transport.UseWebSockets = true;
            _transport.SetConnectionData(_directAddress, _directPort);
            if (!_nm.StartClient()) Fail("Không khởi động được kết nối.");
        }

        public void SetReady(bool ready)
        {
            if (_isHost)
            {
                _hostReady = ready;
                BroadcastLobby();
                TryStartMatch();
            }
            else
            {
                _clientReady = ready;
                using var w = Msg.Writer(16);
                w.WriteValueSafe(ready);
                Msg.Send(_nm, Msg.Ready, NetworkManager.ServerClientId, w);
                RebuildPlayers();
            }
        }

        public void Leave()
        {
            _reconnectDeadline = -1f;
            _sync.End();
            _inMatch = false;
            match.SetNetworkPause(false);
            if (_session != null)
            {
                var s = _session;
                _session = null;
                _ = s.LeaveAsync(); // tự dừng mạng của session
            }
            if (_nm.IsListening) _nm.Shutdown();
            ResetRoom();
            SetStatus(OnlineState.Offline, "");
        }

        // ───────────────────────────── UNITY GAMING SERVICES ─────────────────────────────

        /// <summary>Hiện bước đang thực hiện lên UI + log (giúp chẩn đoán khi kết nối chậm/treo).</summary>
        void Step(string text)
        {
            Debug.Log("[Online] " + text);
            if (_state == OnlineState.Connecting) { _status = text; Changed?.Invoke(); }
        }

        async Task EnsureSignedIn()
        {
            if (!CloudAvailable)
                throw new Exception("Project chưa liên kết Unity Cloud (Edit > Project Settings > Services). " +
                                    "Vẫn có thể chơi bằng Kết nối trực tiếp.");
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                // Mỗi tab trình duyệt một "profile" riêng ⇒ 2 tab cùng máy là 2 người chơi khác nhau.
                var options = new InitializationOptions().SetProfile("p" + UnityEngine.Random.Range(0, 1_000_000));
                Step("Khởi tạo dịch vụ online...");
                await UnityServices.InitializeAsync(options);
            }
            if (!AuthenticationService.Instance.IsSignedIn)
            {
                Step("Đăng nhập...");
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }
            Step("Đã đăng nhập – đang kết nối Relay...");

            // Giao thức Relay theo nền tảng: WebGL ⇒ WSS (trình duyệt không có UDP), PC ⇒ DTLS.
            // UnityTransport PHẢI khớp (WebSocket khi WSS) trước khi session khởi động NetworkManager.
            // Relay cho phép 2 người dùng 2 giao thức khác nhau trong cùng phòng (web ↔ PC vẫn chơi được).
            _transport.UseWebSockets = UseWss;
        }

#if UNITY_WEBGL
        const bool UseWss = true;
#else
        const bool UseWss = false;
#endif

        static SessionOptions SessionOptions(bool isPrivate) =>
            new SessionOptions { MaxPlayers = 2, IsPrivate = isPrivate, Type = SessionType }
                // WSS (WebSocket bảo mật) trên WebGL, DTLS trên PC – trùng với RelayProtocol.Default mà
                // JoinSessionByCodeAsync dùng ⇒ host và người vào phòng luôn cấu hình transport nhất quán.
                .WithRelayNetwork(new RelayNetworkOptions(UseWss ? RelayProtocol.WSS : RelayProtocol.DTLS));

        void AfterSessionJoined()
        {
            _directMode = false;
            _isHost = _session.IsHost;
            _session.RemovedFromSession += () => { if (!_inMatch) Fail("Bạn đã rời phòng."); };
            // Lưu ý thứ tự: kết nối Netcode (và cả lệnh bắt đầu trận!) có thể tới TRƯỚC khi hàm Join/Create
            // trả về ⇒ chỉ cập nhật trạng thái nếu vẫn đang "Connecting", không kéo người chơi từ trận về phòng chờ.
            string link = InviteLink;
            string msg = _isHost ? "Đã tạo phòng – chia sẻ mã cho bạn bè hoặc chờ ghép trận." : "Đã vào phòng.";
            if (_isHost && !string.IsNullOrEmpty(link)) msg += "\n<size=22>Link mời: <color=#FFC828>" + link + "</color></size>";
            if (_state == OnlineState.Connecting)
                SetStatus(OnlineState.InRoom, msg);
            else
            {
                if (_state == OnlineState.InRoom && _isHost) _status = msg;
                Changed?.Invoke(); // cập nhật mã phòng + link lên UI
            }
            // Session tự khởi động NetworkManager (host/client) ⇒ phần còn lại do callback NGO xử lý.
        }

        async void Run(string status, Func<Task> op)
        {
            ResetRoom();
            SetStatus(OnlineState.Connecting, status);
            try { await op(); }
            catch (Exception e)
            {
                Debug.LogWarning("[Online] " + e + (e.InnerException != null ? "\nINNER: " + e.InnerException : ""));
                Fail(Friendly(e));
            }
        }

        static string Friendly(Exception e)
        {
            if (e is SessionException se) return se.Error switch
            {
                SessionError.SessionNotFound => "Không tìm thấy phòng với mã này.",
                _ => "Lỗi dịch vụ online: " + se.Message,
            };
            return e.Message;
        }

        // ───────────────────────────── NETCODE CALLBACKS ─────────────────────────────

        void OnServerStarted()
        {
            _isHost = true; // session có thể khởi động host TRƯỚC khi CreateSessionAsync trả về
            _hostName = OnlineServices.TaggedLocalName();
            _nm.CustomMessagingManager.RegisterNamedMessageHandler(Msg.Hello, OnHello);
            _nm.CustomMessagingManager.RegisterNamedMessageHandler(Msg.Ready, OnReadyMsg);
            RebuildPlayers();
        }

        void OnClientStarted()
        {
            if (_nm.IsServer) return;
            _nm.CustomMessagingManager.RegisterNamedMessageHandler(Msg.Lobby, OnLobby);
            _nm.CustomMessagingManager.RegisterNamedMessageHandler(Msg.Start, OnStart);
        }

        void OnClientConnected(ulong id)
        {
            if (_nm.IsServer)
            {
                if (id == _nm.LocalClientId) return;
                if (_hasClient && id != _remoteId) { _nm.DisconnectClient(id); return; } // phòng đã đủ 2 người
                _remoteId = id;
                _hasClient = true;
                _sync.SetRemote(id);
            }
            else if (id == _nm.LocalClientId)
            {
                // Đã nối tới host ⇒ giới thiệu tên.
                _reconnectDeadline = -1f;
                _reconnecting = false;
                using var w = Msg.Writer(128);
                w.WriteValueSafe(OnlineServices.TaggedLocalName()); // tên + quốc tịch
                w.WriteValueSafe(_inMatch);
                Msg.Send(_nm, Msg.Hello, NetworkManager.ServerClientId, w);
                if (!_inMatch) SetStatus(OnlineState.InRoom, "Đã kết nối – bấm SẴN SÀNG khi muốn bắt đầu.");
            }
        }

        void OnClientDisconnected(ulong id)
        {
            if (_nm.IsServer)
            {
                if (id != _remoteId) return;
                _hasClient = false;
                _clientReady = false;
                _remoteId = ulong.MaxValue;
                if (_inMatch && !match.Match.IsOver)
                {
                    // Tạm dừng trận, chờ 30 giây để đối thủ kết nối lại.
                    match.SetNetworkPause(true);
                    _reconnectDeadline = Time.unscaledTime + ReconnectWindow;
                }
                else
                {
                    _inMatch = false;
                    _sync.End();
                    SetStatus(OnlineState.InRoom, "Đối thủ đã rời phòng. Đang chờ người khác...");
                }
                RebuildPlayers();
                return;
            }

            // Client mất kết nối với host.
            if (_inMatch && !_reconnecting) TryReconnect();
            else if (!_reconnecting) Fail("Mất kết nối với chủ phòng.");
        }

        async void TryReconnect()
        {
            _reconnecting = true;
            _reconnectDeadline = Time.unscaledTime + ReconnectWindow;
            match.Announce("MẤT KẾT NỐI – đang kết nối lại...", ReconnectWindow);
            SetStatus(OnlineState.InMatch, "Đang kết nối lại...");
            while (_reconnecting && Time.unscaledTime < _reconnectDeadline)
            {
                try
                {
                    if (_directMode) { if (!_nm.IsListening) StartDirectClient(); }
                    else if (_session != null) await _session.ReconnectAsync();
                }
                catch (Exception e) { Debug.LogWarning("[Online] Kết nối lại thất bại: " + e.Message); }
                await Task.Delay(2500);
                if (_nm != null && _nm.IsConnectedClient) return; // OnClientConnected sẽ xử lý tiếp
            }
        }

        // ───────────────────────────── PHÒNG CHỜ ─────────────────────────────

        void OnHello(ulong sender, Unity.Netcode.FastBufferReader r)
        {
            r.ReadValueSafe(out string name);
            r.ReadValueSafe(out bool wasInMatch);
            _clientName = string.IsNullOrWhiteSpace(name) ? "Khách" : name;

            if (_inMatch && _reconnectDeadline > 0f)
            {
                // Client quay lại trận đang dở ⇒ tiếp tục.
                _reconnectDeadline = -1f;
                SendStart(resume: true);
                _sync.SetRemote(sender);
                _sync.Begin(_nm, true, sender, _matchSeed, _hostName, _clientName, resume: true);
                return;
            }
            BroadcastLobby();
        }

        void OnReadyMsg(ulong sender, Unity.Netcode.FastBufferReader r)
        {
            r.ReadValueSafe(out bool ready);
            _clientReady = ready;
            BroadcastLobby();
            TryStartMatch();
        }

        void BroadcastLobby()
        {
            RebuildPlayers();
            if (!_hasClient) return;
            using var w = Msg.Writer(256);
            w.WriteValueSafe(_hostName);
            w.WriteValueSafe(_clientName);
            w.WriteValueSafe(_hostReady);
            w.WriteValueSafe(_clientReady);
            Msg.Send(_nm, Msg.Lobby, _remoteId, w);
        }

        void OnLobby(ulong sender, Unity.Netcode.FastBufferReader r)
        {
            r.ReadValueSafe(out _hostName);
            r.ReadValueSafe(out _clientName);
            r.ReadValueSafe(out _hostReady);
            r.ReadValueSafe(out _clientReady);
            _hasClient = true;
            RebuildPlayers();
        }

        /// <summary>Cả hai bấm Sẵn sàng ⇒ host gửi lệnh bắt đầu (kèm seed chung cho các cú đánh).</summary>
        void TryStartMatch()
        {
            if (!_isHost || !_hasClient || !_hostReady || !_clientReady || _inMatch) return;
            _matchSeed = UnityEngine.Random.Range(1, int.MaxValue);
            SendStart(resume: false);
            _inMatch = true;
            _hostReady = _clientReady = false;
            _sync.Begin(_nm, true, _remoteId, _matchSeed, _hostName, _clientName, resume: false);
            SetStatus(OnlineState.InMatch, "");
        }

        void SendStart(bool resume)
        {
            using var w = Msg.Writer(256);
            w.WriteValueSafe(_matchSeed);
            w.WriteValueSafe(_hostName);
            w.WriteValueSafe(_clientName);
            w.WriteValueSafe(resume);
            Msg.Send(_nm, Msg.Start, _remoteId, w);
        }

        void OnStart(ulong sender, Unity.Netcode.FastBufferReader r)
        {
            r.ReadValueSafe(out _matchSeed);
            r.ReadValueSafe(out _hostName);
            r.ReadValueSafe(out _clientName);
            r.ReadValueSafe(out bool resume);
            bool continuing = resume && _inMatch;
            _inMatch = true;
            _clientReady = false;
            _sync.Begin(_nm, false, NetworkManager.ServerClientId, _matchSeed, _hostName, _clientName, resume: continuing);
            if (continuing) match.Announce("ĐÃ KẾT NỐI LẠI", 2f);
            SetStatus(OnlineState.InMatch, "");
        }

        // ───────────────────────────── TIỆN ÍCH ─────────────────────────────

        void RebuildPlayers()
        {
            _players.Clear();
            string host = _isHost ? GameSettings.PlayerName : OnlineServices.DisplayName(_hostName);
            _players.Add(new OnlinePlayerInfo { Name = string.IsNullOrEmpty(host) ? "Chủ phòng" : host, Ready = _hostReady, IsHost = true, IsLocal = _isHost });
            if (_isHost ? _hasClient : true)
                _players.Add(new OnlinePlayerInfo { Name = _isHost ? OnlineServices.DisplayName(_clientName) : GameSettings.PlayerName, Ready = _clientReady, IsLocal = !_isHost });
            Changed?.Invoke();
        }

        void ResetRoom()
        {
            _hasClient = false;
            _hostReady = _clientReady = false;
            _remoteId = ulong.MaxValue;
            _clientName = "";
            _hostName = OnlineServices.TaggedLocalName();
            _players.Clear();
        }

        void SetStatus(OnlineState s, string text)
        {
            _state = s;
            _status = text;
            Changed?.Invoke();
        }

        void Fail(string message)
        {
            _reconnecting = false;
            _inMatch = false;
            _sync.End();
            if (_nm != null && _nm.IsListening) _nm.Shutdown();
            if (_session != null) { var s = _session; _session = null; _ = s.LeaveAsync(); }
            match.SetNetworkPause(false);
            SetStatus(OnlineState.Error, message);
        }
    }
}
