using System;
using System.Collections;
using System.Collections.Generic;
using Badminton.AI;
using Badminton.Audio;
using Badminton.Cameras;
using Badminton.Core;
using Badminton.Courts;
using Badminton.Player;
using Badminton.Replay;
using Badminton.Shots;
using Badminton.Shuttle;
using UnityEngine;

namespace Badminton.Scoring
{
    /// <summary>
    /// Điều phối một trận đơn. Toàn bộ luật nằm trong Core (MatchState + RallyJudge); lớp này nối chúng
    /// với scene: đặt người chơi vào ô giao cầu, khoá di chuyển trước khi giao, nghe sự kiện quả cầu,
    /// cộng điểm, nghỉ giữa game, đổi sân, camera, bảng điểm, âm thanh, ăn mừng, replay.
    ///
    /// Chế độ mạng (GĐ5):
    ///  - IsAuthority = true  (offline / host): tự phán quyết và cộng điểm.
    ///  - IsAuthority = false (client): KHÔNG tự phán quyết; lớp mạng gọi ApplySnapshot + EnterServe /
    ///    ShowRallyResult theo dữ liệu host gửi xuống.
    /// </summary>
    // Khởi tạo TRƯỚC GameUI: Start() của lớp này gọi ResetToIdle(); nếu chạy sau GameUI nó sẽ xoá mất
    // trận AI nền mà menu vừa bắt đầu (lỗi thấy khi quay về menu từ scene tập).
    [DefaultExecutionOrder(-20)]
    public class MatchController : MonoBehaviour
    {
        public enum Phase { Idle, PreServe, Rally, PointOver, Break, MatchOver }

        [Serializable]
        public class Competitor
        {
            public string name = "Người chơi";
            public Color color = Color.blue;
            public PlayerMotor motor;
            public PlayerHitter hitter;
            public PlayerAnimator animator;
            public KeyboardPlayerInput keyboard;  // có thể null
            public AIController ai;
            /// <summary>Mã quốc gia để bảng tỷ số vẽ cờ thật – null thì vẽ 3 sọc theo màu áo.</summary>
            [NonSerialized] public string flagCode;
        }

        public ScoringRules rules;
        public Shuttlecock shuttle;
        public Competitor[] players = new Competitor[2];
        public CameraRig cameraRig;
        public CourtScoreboard courtScoreboard;
        public ReplaySystem replay;
        public LandingMarker landingMarker;
        [Tooltip("Vòng căn thời gian đánh cầu cho người chơi local (vàng sớm / xanh perfect / đỏ muộn).")]
        public TimingRing timingRing;
        public AIDifficulty[] difficulties;
        [Tooltip("Người chơi điều khiển trên máy này (camera đặt sau sân của người này).")]
        public int localPlayer = 0;

        public Phase CurrentPhase { get; private set; } = Phase.Idle;
        public MatchState Match { get; private set; }
        public int DifficultyIndex { get; private set; } = 1;
        public bool AutoPlayLocal { get; private set; }
        public bool IsAuthority { get; set; } = true;
        public bool IsOnline { get; set; }
        public bool Paused { get; private set; }
        /// <summary>
        /// Dùng thời gian nghỉ CHÍNH THỨC (60 s / 120 s) của ScoringRules – chế độ giải đấu bật lên.
        /// Tắt (mặc định) ⇒ nghỉ nhanh như trước (online, đấu với AI).
        /// </summary>
        public bool UseOfficialBreaks { get; set; }
        /// <summary>Số giây nghỉ còn lại (0 khi không nghỉ).</summary>
        public float BreakRemaining { get; private set; }
        /// <summary>Đang nghỉ giữa game (true) hay nghỉ ở 11 điểm (false) – hợp lệ khi BreakRemaining &gt; 0.</summary>
        public bool BreakIsBetweenGames { get; private set; }
        /// <summary>Người chơi có thể bấm "BỎ QUA NGHỈ" lúc này không (chỉ offline).</summary>
        public bool CanSkipBreak => BreakRemaining > 0f && !IsOnline && rules.allowSkipBreak && _breakElapsed >= rules.minBreakBeforeSkip;
        float _breakElapsed;
        bool _skipBreak;
        public string Announcement { get; private set; } = "";
        public float AnnouncementUntil { get; private set; }
        public bool ReplayPlaying => replay != null && replay.IsPlaying;

        public event Action<Phase> PhaseChanged;
        public event Action ScoreChanged;
        public event Action<RallyResult> RallyEnded;
        public event Action<HitInfo> PlayerHit;
        public event Action<int> MatchFinished;
        public event Action<string, float> Announced;
        /// <summary>Host vừa dựng xong lượt giao cầu mới (lớp mạng gửi cho client).</summary>
        public event Action ServeSetUp;

        readonly RallyJudge _judge = new RallyJudge();
        ShotType _lastShotType;
        int _lastShotPlayer = -1;

        // ───────── Đánh đôi (chỉ offline) ─────────
        /// <summary>Trận đánh đôi: 4 người – index 0 &amp; 2 là đội 0, 1 &amp; 3 là đội 1. MatchState/RallyJudge tính theo ĐỘI.</summary>
        public bool Doubles { get; private set; }
        public DoublesServeState DoublesServe { get; } = new DoublesServeState();
        readonly DoublesTeam[] _teams = { new DoublesTeam(), new DoublesTeam() };
        public static int TeamOf(int index) => index & 1;
        public int LocalTeam => localPlayer & 1;
        int ActiveCount => Doubles ? 4 : 2;
        [Tooltip("Thông số đánh đôi (chế độ Giải đấu ghi đè bằng TournamentSettings.doubles).")]
        public DoublesConfig doublesConfig = new DoublesConfig();

        // ───────── Cấu hình riêng cho chế độ Giải đấu ─────────
        bool _custom;
        AIDifficulty[] _customDifficulty;
        string[] _customNames;

        void Start()
        {
            for (int i = 0; i < players.Length; i++)
            {
                if (players[i] == null || players[i].hitter == null) continue;
                int id = i;
                players[i].hitter.playerId = TeamOf(i); // đánh đôi: cả đội chung 1 id ⇒ không đánh 2 lần liên tiếp
                players[i].hitter.freeServe = false;
                players[i].hitter.Hit += h => OnHit(id, h);
                players[i].hitter.Rejected += (_, why) => { if (id == localPlayer) Announce(why, 1.2f); };
            }
            shuttle.Landed += OnLanded;
            shuttle.Stepped += OnShuttleStep;
            if (cameraRig != null) cameraRig.panTarget = shuttle.transform;
            SetDoubles(false);
            ResetToIdle();
        }

        // ───────────────────────────── API cho UI ─────────────────────────────

        public void Configure(int difficulty, bool autoPlayLocal)
        {
            DifficultyIndex = Mathf.Clamp(difficulty, 0, (difficulties?.Length ?? 1) - 1);
            AutoPlayLocal = autoPlayLocal;
            ClearCustom();
        }

        /// <summary>
        /// Chế độ Giải đấu: trận offline cấu hình riêng – đơn/đôi, độ khó AI cho TỪNG nhân vật (theo rating VĐV),
        /// tên 2 đội, màu áo đội, cờ. Người chơi điều khiển index 0. Gọi SAU Configure() và TRƯỚC StartOfflineMatch().
        /// </summary>
        public void ConfigureCustom(bool doubles, AIDifficulty[] perPlayerDifficulty, string[] teamNames, Color[] teamColors, string[] flagCodes)
        {
            _custom = true;
            _customDifficulty = perPlayerDifficulty;
            _customNames = teamNames;
            SetDoubles(doubles);
            for (int i = 0; i < players.Length; i++)
            {
                if (players[i] == null) continue;
                if (teamColors != null) players[i].color = teamColors[TeamOf(i)];
                players[i].flagCode = flagCodes != null ? flagCodes[TeamOf(i)] : null;
            }
        }

        /// <summary>Bỏ cấu hình riêng (về trận thường / menu).</summary>
        public void ClearCustom()
        {
            _custom = false;
            _customDifficulty = null;
            _customNames = null;
            UseOfficialBreaks = false;
            foreach (var p in players) if (p != null) p.flagCode = null;
            SetDoubles(false);
        }

        /// <summary>Bật/tắt đánh đôi: hiện/ẩn 2 nhân vật đồng đội, camera mở rộng nhẹ.</summary>
        public void SetDoubles(bool on)
        {
            Doubles = on && players.Length >= 4 && players[2]?.motor != null && players[3]?.motor != null;
            for (int i = 2; i < players.Length; i++)
                if (players[i]?.motor != null) players[i].motor.gameObject.SetActive(Doubles);
            if (cameraRig != null) cameraRig.extraFov = Doubles ? doublesConfig.cameraExtraFov : 0f;
        }

        public void SetNames(string local, string remote)
        {
            players[localPlayer].name = local;
            players[1 - localPlayer].name = remote;
        }

        /// <summary>
        /// Cấu hình trận online. Host = người chơi 0 (bàn phím) và mô phỏng người chơi 1 theo input từ mạng.
        /// Client = người chơi 1 (bàn phím, dự đoán phía client); người chơi 0 là "puppet" nội suy từ snapshot.
        /// </summary>
        public void ConfigureOnline(bool isHost, string hostName, string clientName, IPlayerInputSource remoteInput)
        {
            StopRoutines();
            ClearCustom(); // online luôn là đánh đơn
            IsOnline = true;
            IsAuthority = isHost;
            localPlayer = isHost ? 0 : 1;
            // Tên gửi qua mạng kèm quốc tịch ⇒ cờ thật cho cả 2 người; cờ của chính mình luôn theo menu NHÂN VẬT.
            players[0].name = OnlineServices.SplitName(hostName, out string hostCountry);
            players[1].name = OnlineServices.SplitName(clientName, out string clientCountry);
            players[0].flagCode = isHost ? PlayerProfile.Country : hostCountry;
            players[1].flagCode = isHost ? clientCountry : PlayerProfile.Country;
            foreach (var p in players) { p.motor.puppet = false; p.hitter.seedProvider = null; }

            // AutoPlayLocal (test): AI điều khiển nhân vật của máy này thay cho bàn phím.
            var me = players[localPlayer];
            if (me.ai != null)
            {
                me.ai.opponent = players[1 - localPlayer].motor;
                if (difficulties != null && difficulties.Length > DifficultyIndex) me.ai.difficulty = difficulties[DifficultyIndex];
            }
            IPlayerInputSource localInput = AutoPlayLocal && me.ai != null ? me.ai : me.keyboard;

            if (isHost)
            {
                SetPlayerInput(0, localInput);
                SetPlayerInput(1, remoteInput);
            }
            else
            {
                SetPlayerInput(1, localInput);
                SetPlayerInput(0, null);
                players[0].motor.puppet = true;
            }
        }

        /// <summary>Host tạm dừng mô phỏng khi đối thủ mất kết nối (chờ kết nối lại).</summary>
        public void SetNetworkPause(bool paused)
        {
            Time.timeScale = paused ? 0f : 1f;
            KeyboardPlayerInput.GameplayEnabled = !paused;
        }

        /// <summary>Pha cầu bị gián đoạn (mất kết nối rồi nối lại) ⇒ đánh lại điểm từ lượt giao cầu.</summary>
        public void ReplayPointFromServe()
        {
            if (Match == null || Match.IsOver) return;
            StopRoutines();
            Announce("TIẾP TỤC TRẬN ĐẤU", 2f);
            SetupServe();
        }

        /// <summary>Kết thúc trận do đối thủ bỏ cuộc / mất kết nối quá lâu.</summary>
        public void Forfeit(int winner, string reason)
        {
            StopRoutines();
            SetNetworkPause(false);
            SetPhase(Phase.MatchOver);
            foreach (var p in players) p.motor.frozen = true;
            Announce($"{reason} – {players[winner].name.ToUpper()} THẮNG!", 6f);
            MatchFinished?.Invoke(winner);
        }

        /// <summary>Bắt đầu trận offline (người vs AI, hoặc AI vs AI).</summary>
        public void StartOfflineMatch()
        {
            IsOnline = false;
            IsAuthority = true;
            localPlayer = 0;
            foreach (var p in players) { p.motor.puppet = false; p.hitter.seedProvider = null; }
            ApplyOfflineControllers();
            StartMatch();
        }

        /// <summary>Bắt đầu trận (offline hoặc host). Lớp mạng tự gán bộ điều khiển trước khi gọi.</summary>
        public void StartMatch(int firstServer = 0)
        {
            StopRoutines();
            SetPaused(false);
            ClearLeftovers();
            Match = new MatchState(rules.rules, firstServer, CourtSide.Near);
            DoublesServe.Reset();
            ScoreChanged?.Invoke();
            AudioManager.Instance?.Whistle();
            Announce("BẮT ĐẦU TRẬN ĐẤU", 1.5f);
            SetupServe();
        }

        /// <summary>Huỷ trận, về trạng thái chờ (đang ở menu).</summary>
        public void ResetToIdle()
        {
            StopRoutines();
            SetPaused(false);
            Match = null;
            ClearLeftovers();
            SetPhase(Phase.Idle);
            foreach (var p in players) { p.motor.frozen = true; p.hitter.canServe = false; p.ai?.ResetPlan(); }
            DoublesServe.Reset();
            PlaceForServe(0, 1, ServiceCourt.Right, new[] { CourtSide.Near, CourtSide.Far });
            shuttle.Hold(players[0].hitter.serveAnchor);
            if (cameraRig != null) cameraRig.SetLocalSide(players[localPlayer].motor.side);
            if (timingRing != null) timingRing.hitter = null;
            UpdateCourtBoard();
        }

        /// <summary>Tạm dừng (chỉ offline – trận online không dừng được).</summary>
        public void SetPaused(bool paused)
        {
            Paused = paused && !IsOnline;
            Time.timeScale = Paused ? 0f : 1f;
            KeyboardPlayerInput.GameplayEnabled = !Paused;
        }

        /// <summary>Gán nguồn điều khiển cho người chơi idx (bàn phím / AI / mạng).</summary>
        public void SetPlayerInput(int idx, IPlayerInputSource src)
        {
            var p = players[idx];
            if (p.ai != null) p.ai.enabled = ReferenceEquals(src, p.ai);
            if (p.keyboard != null) p.keyboard.enabled = ReferenceEquals(src, p.keyboard);
            p.motor.SetInput(src);
        }

        void ApplyOfflineControllers()
        {
            var diff = difficulties != null && difficulties.Length > DifficultyIndex ? difficulties[DifficultyIndex] : null;
            foreach (var t in _teams) { t.receiverOnly = -1; t.attacking = false; }
            for (int i = 0; i < players.Length; i++)
            {
                var p = players[i];
                if (p == null || p.motor == null) continue;
                int team = TeamOf(i);
                bool useAi = i != localPlayer || AutoPlayLocal;
                if (p.ai != null)
                {
                    var d = _custom && _customDifficulty != null && i < _customDifficulty.Length && _customDifficulty[i] != null
                        ? _customDifficulty[i] : diff;
                    if (d != null) p.ai.difficulty = d;
                    p.ai.opponent = players[1 - team].motor;
                    p.ai.team = Doubles ? _teams[team] : null;
                    _teams[team].cfg = doublesConfig;
                }
                if (Doubles && i < 4)
                {
                    int member = i < 2 ? 0 : 1;
                    _teams[team].motors[member] = p.motor;
                    _teams[team].ais[member] = p.ai;
                    _teams[team].human[member] = !useAi;
                }
                if (i >= ActiveCount) { SetPlayerInput(i, null); continue; }
                SetPlayerInput(i, useAi ? p.ai : (IPlayerInputSource)p.keyboard);
            }
            if (_custom && _customNames != null)
            {
                players[0].name = _customNames[0];
                players[1].name = _customNames[1];
            }
            else
            {
                players[0].name = AutoPlayLocal ? "AI XANH" : GameSettings.PlayerName;
                players[1].name = "AI " + (diff != null ? diff.displayName : "");
                // Cờ: người chơi luôn mang quốc tịch chọn ở menu; AI nhận một quốc tịch ngẫu nhiên khác.
                players[0].flagCode = AutoPlayLocal ? RandomCountry(null) : PlayerProfile.Country;
                players[1].flagCode = RandomCountry(players[0].flagCode);
            }
        }

        // ───────────────────────────── LƯỢT GIAO CẦU ─────────────────────────────

        void SetupServe()
        {
            EnterServe();
            _judge.BeginRally(Match.Server, Match.ServeCourt, Match.Side, Doubles,
                              Doubles ? DoublesServe.ReceiverMember(Match) : -1);
            ServeSetUp?.Invoke();
        }

        /// <summary>
        /// Đặt người chơi vào ô giao/nhận, khoá di chuyển, đưa cầu vào tay người giao.
        /// Client gọi trực tiếp (sau ApplySnapshot) để hiển thị giống host.
        /// </summary>
        public void EnterServe()
        {
            SetPhase(Phase.PreServe);
            int serverTeam = Match.Server, recvTeam = Match.Receiver;
            var court = Match.ServeCourt;
            int serverIdx = Doubles ? IndexOf(serverTeam, DoublesServe.ServerMember(Match)) : serverTeam;
            int recvIdx = Doubles ? IndexOf(recvTeam, DoublesServe.ReceiverMember(Match)) : recvTeam;
            PlaceForServe(serverIdx, recvIdx, court, Match.Side);

            foreach (var p in players)
            {
                p.motor.frozen = true;
                p.hitter.canServe = false;
                p.hitter.serveDepthCap = Doubles ? doublesConfig.serveDepth : 0f;
                p.ai?.ResetPlan();
            }
            var sh = players[serverIdx].hitter;
            sh.canServe = true;
            // Ô chéo: X của tâm ô giao cầu cùng tên (phải/trái) bên người nhận.
            sh.serveTargetX = (court == ServiceCourt.Right ? 1f : -1f) * CourtDimensions.RightSign(Match.Side[recvTeam]) * (Doubles ? doublesConfig.serveTargetX : 1.3f);
            shuttle.Hold(sh.serveAnchor);

            // AI đội nhận: chỉ coi là "trong" nếu rơi vào ô giao cầu chéo (đơn/đôi).
            CourtSide rs = Match.Side[recvTeam];
            bool dbl = Doubles;
            foreach (int i in TeamIndices(recvTeam))
                if (players[i].ai != null) players[i].ai.isLandingIn = q => CourtDimensions.IsInsideServiceCourt(q.x, q.z, rs, court, dbl);
            if (Doubles)
            {
                // Chỉ người nhận được đỡ giao cầu; cả 2 đội về đội hình phòng thủ.
                _teams[recvTeam].receiverOnly = recvIdx < 2 ? 0 : 1;
                _teams[serverTeam].receiverOnly = -1;
                _teams[0].attacking = _teams[1].attacking = false;
            }

            if (cameraRig != null) cameraRig.SetLocalSide(Match.Side[LocalTeam]);
            if (timingRing != null) timingRing.hitter = AutoPlayLocal ? null : players[localPlayer].hitter;
            UpdateCourtBoard();
        }

        static int IndexOf(int team, int member) => member == 0 ? team : team + 2;

        static readonly System.Random _flagRng = new System.Random();

        static string RandomCountry(string except)
        {
            var all = Badminton.Tour.Countries.All;
            for (int i = 0; i < 10; i++)
            {
                var c = all[_flagRng.Next(all.Count)].Code;
                if (c != except) return c;
            }
            return all[0].Code == except ? all[1].Code : all[0].Code;
        }

        IEnumerable<int> TeamIndices(int team)
        {
            yield return team;
            if (Doubles) yield return team + 2;
        }

        void PlaceForServe(int serverIdx, int recvIdx, ServiceCourt court, CourtSide[] sides)
        {
            if (!Doubles)
            {
                for (int i = 0; i < 2; i++)
                {
                    var m = players[i].motor;
                    m.side = sides[i];
                    m.Teleport(CourtDimensions.ServiceStandPoint(sides[i], court, receiver: i != serverIdx));
                }
                return;
            }
            // Đánh đôi: mỗi người đứng ở ô của mình; người giao/nhận đứng gần vạch giao cầu ngắn, đồng đội lùi sau.
            int serverTeam = TeamOf(serverIdx);
            for (int i = 0; i < 4; i++)
            {
                int team = TeamOf(i), member = i < 2 ? 0 : 1;
                var m = players[i].motor;
                var side = sides[team];
                m.side = side;
                var myCourt = DoublesServe.CourtOf(team, member);
                var dc = doublesConfig;
                float x = (myCourt == ServiceCourt.Right ? 1f : -1f) * CourtDimensions.RightSign(side) * dc.standX;
                float dz = i == serverIdx ? dc.serverDepth : i == recvIdx ? dc.receiverDepth
                         : team == serverTeam ? dc.serverPartnerDepth : dc.receiverPartnerDepth;
                m.Teleport(new Vector3(x, 0f, (int)side * dz));
            }
        }

        // ───────────────────────────── PHA CẦU ─────────────────────────────

        void OnHit(int id, HitInfo h)
        {
            if (Match == null) return;

            if (CurrentPhase == Phase.PreServe)
            {
                // Cú giao cầu đã đánh ⇒ bắt đầu pha cầu, mở khoá di chuyển.
                SetPhase(Phase.Rally);
                foreach (var p in players) p.motor.frozen = false;
            }
            else if (CurrentPhase == Phase.Rally)
            {
                // Từ cú thứ 2: AI xét trong/ngoài theo sân đơn/đôi (không còn là ô giao cầu).
                bool dbl = Doubles;
                foreach (var p in players)
                    if (p.ai != null) p.ai.isLandingIn = q => CourtDimensions.IsInside(q.x, q.z, dbl);
            }

            if (CurrentPhase != Phase.Rally) return;
            int team = TeamOf(id);
            if (Doubles) { _teams[team].OnTeamHit(h.Type); _teams[1 - team].OnOpponentHit(h.Type); }
            _lastShotType = h.Type;
            _lastShotPlayer = team;
            if (IsAuthority)
            {
                if (Doubles)
                {
                    var foul = _judge.OnHit(team, id < 2 ? 0 : 1);
                    if (foul != null) { Finish(foul); return; }
                }
                else _judge.OnHit(team);
            }
            PlayerHit?.Invoke(h);

            bool isServe = h.Type == ShotType.Serve || h.Type == ShotType.ShortServe;
            if (id == localPlayer && !AutoPlayLocal && !isServe && h.Grade != TimingGrade.Good)
                Announce(h.Grade switch
                {
                    TimingGrade.Perfect => "PERFECT!",
                    TimingGrade.Early => "SỚM",
                    _ => "MUỘN",
                }, 0.7f);
        }

        void OnShuttleStep(ShuttleStepResult r)
        {
            if (!IsAuthority || CurrentPhase != Phase.Rally) return;
            if (r.Has(ShuttleEvents.CrossedUnderNet)) Finish(_judge.OnUnderNet());
        }

        void OnLanded(Vector3 p, bool _)
        {
            if (!IsAuthority || CurrentPhase != Phase.Rally) return;
            Finish(_judge.OnLanded(p));
        }

        void Finish(RallyResult? result)
        {
            if (result == null) return;
            var r = result.Value;
            bool replayIt = ShowRallyResult(r);
            StartCoroutine(AfterPoint(r.Winner, replayIt));
        }

        /// <summary>
        /// Hiển thị kết quả pha cầu: thông báo, khán giả, ăn mừng, replay.
        /// Trả về true nếu có replay. Client gọi khi nhận kết quả từ host.
        /// </summary>
        public bool ShowRallyResult(RallyResult r)
        {
            SetPhase(Phase.PointOver);
            RallyEnded?.Invoke(r);

            string who = players[r.Winner].name;
            Announce(r.Reason switch
            {
                RallyEndReason.In => $"TRONG! Điểm cho {who}",
                RallyEndReason.Out => $"NGOÀI! Điểm cho {who}",
                RallyEndReason.NotOverNet => $"KHÔNG QUA LƯỚI – điểm cho {who}",
                RallyEndReason.UnderNet => $"CHUI LƯỚI – điểm cho {who}",
                RallyEndReason.ServiceFault => $"LỖI GIAO CẦU – điểm cho {who}",
                RallyEndReason.WrongReceiver => $"SAI NGƯỜI ĐỠ GIAO CẦU – điểm cho {who}",
                _ => who,
            }, rules.pointPause);

            bool smashWinner = r.Reason == RallyEndReason.In && _lastShotType == ShotType.Smash && _lastShotPlayer == r.Winner;
            AudioManager.Instance?.Cheer(smashWinner);
            foreach (int i in TeamIndices(r.Winner)) players[i].animator?.Celebrate(smashWinner ? 1.8f : 1.1f);

            // Replay chỉ ghi 2 nhân vật chính ⇒ tắt khi đánh đôi.
            bool doReplay = smashWinner && GameSettings.SmashReplay && replay != null && !Doubles;
            if (doReplay)
            {
                // Khoá di chuyển trong lúc replay: nếu không, nhân vật thật (và bóng của nó) vẫn chạy theo phím
                // dưới lớp hình replay. EnterServe ở lượt sau đặt lại vị trí và tự mở khoá.
                foreach (var p in players) p.motor.frozen = true;
                StartCoroutine(replay.Play(Time.time));
            }
            return doReplay;
        }

        IEnumerator AfterPoint(int winner, bool replaying)
        {
            yield return new WaitForSeconds(rules.pointPause);
            while (ReplayPlaying) yield return null;

            int serverBefore = Match.Server;
            var o = Match.AwardRally(winner);
            if (Doubles) DoublesServe.OnRally(winner, serverBefore, o.GameWon);
            ScoreChanged?.Invoke();
            UpdateCourtBoard();

            if (o.MatchWon)
            {
                SetPhase(Phase.MatchOver);
                foreach (var p in players) p.motor.frozen = true;
                foreach (int i in TeamIndices(winner)) players[i].animator?.Celebrate(4f);
                AudioManager.Instance?.Cheer(true);
                AudioManager.Instance?.Whistle();
                Announce($"{players[winner].name.ToUpper()} THẮNG TRẬN {Match.Games[winner]}-{Match.Games[1 - winner]}!", 6f);
                MatchFinished?.Invoke(winner);
                yield break;
            }

            if (o.GameWon || o.Interval)
            {
                SetPhase(Phase.Break);
                string msg = o.GameWon
                    ? $"{players[o.GameWinner].name} thắng game {Match.GameNumber - 1}! Đổi sân."
                    : o.ChangeEnds ? "NGHỈ GIỮA GAME – Đổi sân" : "NGHỈ GIỮA GAME (11 điểm)";
                float pause = o.GameWon ? rules.GameBreak(UseOfficialBreaks) : rules.IntervalPause(UseOfficialBreaks);
                AudioManager.Instance?.Whistle();
                Announce(msg, UseOfficialBreaks ? 4f : pause); // nghỉ dài: banner 4 s rồi HUD hiện đồng hồ đếm ngược
                yield return BreakCountdown(pause, o.GameWon);
            }

            SetupServe();
        }

        /// <summary>Đếm ngược thời gian nghỉ; dừng sớm nếu người chơi bấm bỏ qua.</summary>
        IEnumerator BreakCountdown(float seconds, bool betweenGames)
        {
            BreakIsBetweenGames = betweenGames;
            BreakRemaining = seconds;
            _breakElapsed = 0f;
            _skipBreak = false;
            while (BreakRemaining > 0f && !_skipBreak)
            {
                yield return null;
                _breakElapsed += Time.deltaTime;
                BreakRemaining = Mathf.Max(0f, seconds - _breakElapsed);
            }
            if (_skipBreak) AudioManager.Instance?.Whistle();
            BreakRemaining = 0f;
            _skipBreak = false;
        }

        /// <summary>Người chơi bấm "BỎ QUA NGHỈ" (chỉ trận offline).</summary>
        public void SkipBreak()
        {
            if (CanSkipBreak) _skipBreak = true;
        }

        // ───────────────────────────── CLIENT (GĐ5) ─────────────────────────────

        /// <summary>Client: nhận trạng thái trận từ host.</summary>
        public void ApplySnapshot(in MatchSnapshot s)
        {
            if (Match == null) Match = new MatchState(rules.rules);
            Match.Restore(s);
            ScoreChanged?.Invoke();
            UpdateCourtBoard();
            if (s.MatchWinner >= 0 && CurrentPhase != Phase.MatchOver)
            {
                SetPhase(Phase.MatchOver);
                foreach (var p in players) p.motor.frozen = true;
                Announce($"{players[s.MatchWinner].name.ToUpper()} THẮNG TRẬN!", 6f);
                MatchFinished?.Invoke(s.MatchWinner);
            }
        }

        /// <summary>Client: host báo đã bắt đầu pha cầu (cú giao cầu đã đánh).</summary>
        public void ClientRallyStarted()
        {
            if (CurrentPhase != Phase.PreServe) return;
            SetPhase(Phase.Rally);
            foreach (var p in players) p.motor.frozen = false;
        }

        // ───────────────────────────── TIỆN ÍCH ─────────────────────────────

        /// <summary>
        /// Dừng mọi coroutine của trận (điểm, nghỉ, replay) và dừng hẳn replay đang phát.
        /// StopAllCoroutines một mình KHÔNG đủ: coroutine replay bị huỷ giữa chừng sẽ để ReplaySystem kẹt
        /// ở trạng thái đang phát (hình replay cũ đè lên trận mới, AfterPoint chờ mãi ⇒ trận treo).
        /// </summary>
        void StopRoutines()
        {
            StopAllCoroutines();
            BreakRemaining = 0f;
            _skipBreak = false;
            if (replay != null) replay.Stop();
        }

        /// <summary>Xoá dấu vết của trận trước khi bắt đầu trận mới / về menu.</summary>
        void ClearLeftovers()
        {
            if (replay != null) replay.ClearBuffer();
            shuttle.ClearLastHitter();
            foreach (var p in players)
            {
                p.animator?.ResetPose();
                p.hitter.CancelPending();
            }
            _lastShotPlayer = -1;
        }

        void SetPhase(Phase p)
        {
            if (CurrentPhase == p) return;
            CurrentPhase = p;
            PhaseChanged?.Invoke(p);
        }

        void UpdateCourtBoard()
        {
            if (courtScoreboard == null) return;
            if (Match != null) courtScoreboard.SetScore(Match.Score[0], Match.Score[1]);
            else courtScoreboard.SetScore(0, 0);
        }

        public void Announce(string text, float seconds)
        {
            Announcement = text;
            AnnouncementUntil = Time.time + seconds;
            Announced?.Invoke(text, seconds);
        }
    }
}
