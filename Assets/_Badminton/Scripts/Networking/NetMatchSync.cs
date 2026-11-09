using System.Collections.Generic;
using Badminton.Core;
using Badminton.Scoring;
using Badminton.Shots;
using Badminton.Shuttle;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace Badminton.Networking
{
    /// <summary>
    /// Đồng bộ một trận 1v1 qua mạng – mô hình SERVER-AUTHORITATIVE (host vừa chơi vừa làm server).
    ///
    /// HOST (người chơi 0):
    ///  • Chạy toàn bộ mô phỏng: cả 2 nhân vật, quả cầu, trọng tài, tính điểm (MatchController.IsAuthority).
    ///  • Người chơi 1 được điều khiển bằng input client gửi lên mỗi tick (NetworkInputSource).
    ///  • LAG COMPENSATION: lưu lịch sử quả cầu + vị trí người chơi 1 theo tick (~0.5 s). Khi client báo
    ///    "đánh trúng ở tick T", host TUA LẠI về T (giới hạn 150 ms), kiểm tra cầu có thật sự trong tầm với
    ///    lúc đó không, rồi phát cầu từ T và mô phỏng nhanh tới hiện tại.
    ///
    /// CLIENT (người chơi 1):
    ///  • CLIENT-SIDE PREDICTION: tự mô phỏng nhân vật của mình ngay khi bấm phím (không chờ server),
    ///    sau đó hiệu chỉnh nhẹ theo vị trí host xác nhận.
    ///  • INTERPOLATION: nhân vật đối thủ hiển thị trễ ~100 ms, nội suy giữa 2 snapshot ⇒ mượt dù mạng giật.
    ///  • QUỸ ĐẠO TẤT ĐỊNH: mỗi cú đánh host chỉ gửi (vị trí, vận tốc, tick); client tự mô phỏng quả cầu
    ///    bằng cùng ShuttlePhysics, chỉ hiệu chỉnh khi lệch (đồng bộ định kỳ).
    /// </summary>
    [DefaultExecutionOrder(50)] // sau mọi mô phỏng trong bước vật lý ⇒ ghi lịch sử đúng trạng thái cuối tick
    public class NetMatchSync : MonoBehaviour
    {
        const int HistorySize = 64;          // ~1 giây ở 60 Hz
        const int MaxRewindTicks = 9;        // 150 ms
        const int InterpDelayTicks = 6;      // hiển thị đối thủ trễ 100 ms
        const int SnapEvery = 2;             // snapshot người chơi 30 Hz
        const int ShuttleSyncEvery = 10;     // đồng bộ quả cầu 6 Hz
        const float ReachTolerance = 0.45f;  // nới tầm với khi xét cú đánh của client (sai số mạng)

        public MatchController match;

        public bool Active { get; private set; }
        public bool IsHost { get; private set; }
        public int EstServerTick => IsHost ? SimClock.Tick : SimClock.Tick + Mathf.RoundToInt(_tickOffset);

        NetworkManager _nm;
        UnityTransport _transport;
        ulong _remoteId;
        int _matchSeed;
        readonly NetworkInputSource _remoteInput = new NetworkInputSource();

        // Lịch sử (host)
        struct ShuttleHist { public int tick; public ShuttleState state; public int lastHitter; }
        struct PlayerHist { public int tick; public Vector3 center; }
        readonly ShuttleHist[] _shuttleHist = new ShuttleHist[HistorySize];
        readonly PlayerHist[] _p1Hist = new PlayerHist[HistorySize];

        // Client
        float _tickOffset;
        bool _hasOffset;
        struct Snap { public int tick; public Vector3 pos, vel; }
        readonly List<Snap> _puppet = new List<Snap>();
        readonly Vector3[] _localHist = new Vector3[128];

        Shuttlecock Shuttle => match.shuttle;

        // ───────────────────────────── VÒNG ĐỜI ─────────────────────────────

        /// <summary>Bắt đầu đồng bộ trận. resume = host tiếp tục trận đang dở sau khi client kết nối lại.</summary>
        public void Begin(NetworkManager nm, bool isHost, ulong remoteId, int seed, string hostName, string clientName, bool resume)
        {
            _nm = nm;
            _transport = nm.GetComponent<UnityTransport>();
            IsHost = isHost;
            _remoteId = remoteId;
            _matchSeed = seed;
            _hasOffset = false;
            _puppet.Clear();
            _remoteInput.Reset();

            // Luôn đăng ký lại: sau khi kết nối lại, NGO tạo CustomMessagingManager mới (handler cũ mất).
            RegisterHandlers();
            Active = true;

            if (!resume) match.ConfigureOnline(isHost, hostName, clientName, _remoteInput);
            var p0 = match.players[0].hitter;
            var p1 = match.players[1].hitter;
            if (isHost)
            {
                p0.seedProvider = () => Seed(SimClock.Tick);
                if (!resume)
                {
                    p0.Hit += OnHostSideHit;
                    p1.Hit += OnHostSideHit;
                    match.ServeSetUp += SendServeSetup;
                    match.RallyEnded += SendRallyResult;
                    match.MatchFinished += SendFinalScore;
                    match.Announced += SendAnnouncement;
                    match.StartMatch(firstServer: 0);
                }
                else
                {
                    match.SetNetworkPause(false);
                    match.ReplayPointFromServe(); // pha cầu bị gián đoạn ⇒ đánh lại từ giao cầu
                }
            }
            else
            {
                p1.seedProvider = () => Seed(EstServerTick);
                if (!resume) p1.Hit += OnClientLocalHit;
            }
        }

        public void End()
        {
            if (!Active) return;
            Active = false;
            UnregisterHandlers();
            var p0 = match.players[0].hitter;
            var p1 = match.players[1].hitter;
            p0.Hit -= OnHostSideHit; p1.Hit -= OnHostSideHit; p1.Hit -= OnClientLocalHit;
            match.ServeSetUp -= SendServeSetup;
            match.RallyEnded -= SendRallyResult;
            match.MatchFinished -= SendFinalScore;
            match.Announced -= SendAnnouncement;
            p0.seedProvider = p1.seedProvider = null;
            match.players[0].motor.puppet = false;
        }

        /// <summary>Client kết nối lại sau khi rớt mạng: id mới.</summary>
        public void SetRemote(ulong id) => _remoteId = id;

        int Seed(int tick) => unchecked(_matchSeed * 7919 + tick);

        void RegisterHandlers()
        {
            var cm = _nm.CustomMessagingManager;
            if (IsHost)
            {
                cm.RegisterNamedMessageHandler(Msg.Input, OnInput);
                cm.RegisterNamedMessageHandler(Msg.Hit, OnHitRequest);
            }
            else
            {
                cm.RegisterNamedMessageHandler(Msg.Snap, OnSnap);
                cm.RegisterNamedMessageHandler(Msg.Shuttle, OnShuttleSync);
                cm.RegisterNamedMessageHandler(Msg.Launch, OnLaunch);
                cm.RegisterNamedMessageHandler(Msg.Match, OnMatch);
                cm.RegisterNamedMessageHandler(Msg.Announce, OnAnnounce);
            }
        }

        void UnregisterHandlers()
        {
            var cm = _nm != null ? _nm.CustomMessagingManager : null;
            if (cm == null) return;
            foreach (var n in new[] { Msg.Input, Msg.Hit, Msg.Snap, Msg.Shuttle, Msg.Launch, Msg.Match, Msg.Announce })
                cm.UnregisterNamedMessageHandler(n);
        }

        // ───────────────────────────── MỖI TICK ─────────────────────────────

        void FixedUpdate()
        {
            if (!Active || _nm == null || !_nm.IsListening) return;
            if (IsHost) HostTick(); else ClientTick();
        }

        void HostTick()
        {
            int t = SimClock.Tick;
            int i = t % HistorySize;
            _shuttleHist[i] = new ShuttleHist { tick = t, state = Shuttle.State, lastHitter = Shuttle.LastHitterId };
            _p1Hist[i] = new PlayerHist { tick = t, center = match.players[1].motor.HitCenter };

            if (t % SnapEvery == 0)
            {
                var m0 = match.players[0].motor; var m1 = match.players[1].motor;
                using var w = Msg.Writer();
                w.WriteValueSafe(t);
                w.WriteValueSafe(m0.Position); w.WriteValueSafe(m0.Velocity);
                w.WriteValueSafe(m1.Position);
                w.WriteValueSafe(_remoteInput.LastClientTick);
                Msg.Send(_nm, Msg.Snap, _remoteId, w, NetworkDelivery.Unreliable);
            }
            if (t % ShuttleSyncEvery == 0 && Shuttle.IsInPlay) SendShuttle(NetworkDelivery.Unreliable);
        }

        void ClientTick()
        {
            int t = SimClock.Tick;
            var me = match.players[1].motor;
            _localHist[t % _localHist.Length] = me.Position;

            // Gửi input của tick này lên host.
            using (var w = Msg.Writer(32))
            {
                w.WriteValueSafe(t);
                w.WriteValueSafe(me.Input != null && !me.frozen ? me.Input.Move : Vector2.zero);
                Msg.Send(_nm, Msg.Input, NetworkManager.ServerClientId, w, NetworkDelivery.UnreliableSequenced);
            }

            // Nội suy đối thủ (puppet) ở thời điểm trễ InterpDelayTicks so với server.
            if (_puppet.Count >= 2)
            {
                float renderTick = EstServerTick - InterpDelayTicks;
                while (_puppet.Count > 2 && _puppet[1].tick <= renderTick) _puppet.RemoveAt(0);
                var a = _puppet[0]; var b = _puppet[1];
                float k = b.tick > a.tick ? Mathf.Clamp01((renderTick - a.tick) / (b.tick - a.tick)) : 1f;
                match.players[0].motor.ForceState(Vector3.Lerp(a.pos, b.pos, k), Vector3.Lerp(a.vel, b.vel, k));
            }
        }

        public int PingMs(ulong id) => _transport != null ? (int)_transport.GetCurrentRtt(id) : 0;

        float RttTicks => (IsHost ? 0 : PingMs(NetworkManager.ServerClientId)) / 1000f * SimulationBootstrap.TickRate;

        // ───────────────────────────── HOST: NHẬN ─────────────────────────────

        void OnInput(ulong sender, FastBufferReader r)
        {
            r.ReadValueSafe(out int clientTick);
            r.ReadValueSafe(out Vector2 move);
            _remoteInput.Push(clientTick, move);
        }

        /// <summary>
        /// Client báo đã đánh cầu ở tick T. Host tua lại tới T để xét (lag compensation) rồi tái tạo cú đánh
        /// bằng cùng seed ⇒ ra đúng quỹ đạo client đã dự đoán.
        /// </summary>
        void OnHitRequest(ulong sender, FastBufferReader r)
        {
            r.ReadValueSafe(out int tick);
            r.ReadValueSafe(out byte type);
            r.ReadValueSafe(out Vector3 contact);
            r.ReadValueSafe(out Vector2 aim);
            r.ReadValueSafe(out float quality);
            r.ReadValueSafe(out byte grade);
            r.ReadValueSafe(out bool jumped);
            r.ReadValueSafe(out int seed);

            var shot = (ShotType)type;
            var hitter = match.players[1].hitter;
            var motor = match.players[1].motor;
            int now = SimClock.Tick;
            float? targetX = null;
            int fromTick = now;

            if (shot == ShotType.Serve || shot == ShotType.ShortServe)
            {
                if (match.CurrentPhase != MatchController.Phase.PreServe || !hitter.canServe) return;
                contact = hitter.serveAnchor.position; // vị trí giao do host quyết định
                targetX = hitter.serveTargetX;
            }
            else
            {
                if (match.CurrentPhase != MatchController.Phase.Rally) return;
                fromTick = Mathf.Clamp(tick, now - MaxRewindTicks, now);
                var past = _shuttleHist[fromTick % HistorySize];
                var who = _p1Hist[fromTick % HistorySize];
                if (past.tick != fromTick || who.tick != fromTick) return;           // ngoài lịch sử
                if (past.state.Phase != ShuttlePhase.Flying || past.lastHitter == 1) return;
                if (CourtDimensions.SideOf(past.state.Position.z) != motor.side) return;
                float reach = motor.tuning.reach + ReachTolerance + (jumped ? hitter.shots.jumpReach : 0f);
                if (Vector3.Distance(past.state.Position, who.center) > reach) return; // không với tới ⇒ trượt
                // Dùng điểm chạm client báo nếu khớp với lịch sử (giữ tất định), ngược lại dùng của host.
                if (Vector3.Distance(contact, past.state.Position) > 0.6f) contact = past.state.Position;
            }

            Vector3 vel = hitter.ResolveVelocity(shot, contact, aim, quality, targetX, seed);
            Shuttle.Launch(contact, vel, 1);
            Shuttle.FastForward(now - fromTick, silent: false); // tua tới hiện tại
            hitter.NotifyRemoteHit(new HitInfo
            {
                PlayerId = 1, Type = shot, Contact = contact, Velocity = vel, Quality = quality,
                Grade = (TimingGrade)grade, Jumped = jumped, Aim = aim, Seed = seed,
            }); // ⇒ MatchController (trọng tài) + OnHostSideHit (gửi bm.launch)
        }

        // ───────────────────────────── HOST: GỬI ─────────────────────────────

        void OnHostSideHit(HitInfo h)
        {
            using var w = Msg.Writer();
            w.WriteValueSafe(SimClock.Tick);
            w.Write(Shuttle.State);
            w.WriteValueSafe((byte)h.PlayerId);
            w.WriteValueSafe((byte)h.Type);
            w.WriteValueSafe(h.Contact);
            w.WriteValueSafe(h.Quality);
            w.WriteValueSafe((byte)h.Grade);
            w.WriteValueSafe(h.Jumped);
            Msg.Send(_nm, Msg.Launch, _remoteId, w);
        }

        void SendShuttle(NetworkDelivery d)
        {
            using var w = Msg.Writer();
            w.WriteValueSafe(SimClock.Tick);
            w.Write(Shuttle.State);
            w.WriteValueSafe((sbyte)Shuttle.LastHitterId);
            Msg.Send(_nm, Msg.Shuttle, _remoteId, w, d);
        }

        enum MatchMsg : byte { Serve, Result, Score }

        void SendServeSetup() => SendMatch(MatchMsg.Serve, default);
        void SendRallyResult(RallyResult r) => SendMatch(MatchMsg.Result, r);
        void SendFinalScore(int _) => SendMatch(MatchMsg.Score, default);

        void SendMatch(MatchMsg kind, RallyResult r)
        {
            if (match.Match == null) return;
            using var w = Msg.Writer();
            w.WriteValueSafe((byte)kind);
            w.Write(match.Match.Snapshot());
            w.WriteValueSafe((byte)r.Winner); w.WriteValueSafe((byte)r.Faulter);
            w.WriteValueSafe((byte)r.Reason); w.WriteValueSafe(r.LandingPoint);
            Msg.Send(_nm, Msg.Match, _remoteId, w);
        }

        void SendAnnouncement(string text, float seconds)
        {
            // Chỉ chuyển thông báo nghỉ/kết thúc; kết quả điểm client tự hiển thị, "PERFECT" là của riêng host.
            if (match.CurrentPhase != MatchController.Phase.Break && match.CurrentPhase != MatchController.Phase.MatchOver) return;
            using var w = Msg.Writer(512);
            w.WriteValueSafe(text);
            w.WriteValueSafe(seconds);
            Msg.Send(_nm, Msg.Announce, _remoteId, w);
        }

        // ───────────────────────────── CLIENT: GỬI ─────────────────────────────

        /// <summary>Client vừa đánh cầu (đã dự đoán tại chỗ) ⇒ gửi yêu cầu kèm tick để host xét.</summary>
        void OnClientLocalHit(HitInfo h)
        {
            using var w = Msg.Writer();
            w.WriteValueSafe(EstServerTick);
            w.WriteValueSafe((byte)h.Type);
            w.WriteValueSafe(h.Contact);
            w.WriteValueSafe(h.Aim);
            w.WriteValueSafe(h.Quality);
            w.WriteValueSafe((byte)h.Grade);
            w.WriteValueSafe(h.Jumped);
            w.WriteValueSafe(h.Seed);
            Msg.Send(_nm, Msg.Hit, NetworkManager.ServerClientId, w);
        }

        // ───────────────────────────── CLIENT: NHẬN ─────────────────────────────

        void OnSnap(ulong sender, FastBufferReader r)
        {
            r.ReadValueSafe(out int tick);
            r.ReadValueSafe(out Vector3 p0); r.ReadValueSafe(out Vector3 v0);
            r.ReadValueSafe(out Vector3 p1);
            r.ReadValueSafe(out int ack);

            // Ước lượng độ lệch đồng hồ: tick server "bây giờ" ≈ tick trong gói + nửa RTT.
            float sample = tick + RttTicks * 0.5f - SimClock.Tick;
            _tickOffset = _hasOffset ? Mathf.Lerp(_tickOffset, sample, 0.1f) : sample;
            _hasOffset = true;

            if (_puppet.Count == 0 || tick > _puppet[_puppet.Count - 1].tick)
                _puppet.Add(new Snap { tick = tick, pos = p0, vel = v0 });
            if (_puppet.Count > 30) _puppet.RemoveAt(0);

            // Hiệu chỉnh dự đoán: so vị trí host xác nhận với vị trí mình đã có ở cùng tick input.
            var me = match.players[1].motor;
            if (ack >= 0 && SimClock.Tick - ack < _localHist.Length && !me.frozen)
            {
                Vector3 err = p1 - _localHist[ack % _localHist.Length];
                err.y = 0f;
                if (err.sqrMagnitude > 0.05f * 0.05f) me.Nudge(err * (err.magnitude > 1f ? 1f : 0.25f));
            }
            else if (me.frozen && (p1 - me.Position).sqrMagnitude > 0.01f) me.Teleport(p1);
        }

        void OnShuttleSync(ulong sender, FastBufferReader r)
        {
            r.ReadValueSafe(out int tick);
            var s = r.ReadShuttle();
            r.ReadValueSafe(out sbyte lastHitter);

            // Mô phỏng trạng thái host tới "bây giờ" rồi so với cầu của mình.
            var sim = s;
            int ahead = Mathf.Max(0, EstServerTick - tick);
            for (int i = 0; i < ahead && sim.Phase == ShuttlePhase.Flying; i++) Shuttle.Physics.Step(ref sim, Time.fixedDeltaTime);
            bool differentHitter = lastHitter != Shuttle.LastHitterId;
            if (differentHitter || (sim.Position - Shuttle.Position).sqrMagnitude > 0.08f * 0.08f)
            {
                Shuttle.SetState(s, lastHitter);
                Shuttle.FastForward(ahead, silent: true);
            }
        }

        void OnLaunch(ulong sender, FastBufferReader r)
        {
            r.ReadValueSafe(out int tick);
            var s = r.ReadShuttle();
            r.ReadValueSafe(out byte hitter);
            r.ReadValueSafe(out byte type);
            r.ReadValueSafe(out Vector3 contact);
            r.ReadValueSafe(out float quality);
            r.ReadValueSafe(out byte grade);
            r.ReadValueSafe(out bool jumped);

            int ahead = Mathf.Max(0, EstServerTick - tick);
            Shuttle.SetState(s, hitter);
            Shuttle.FastForward(ahead, silent: true);

            // Cú đánh của mình: đã dự đoán & phát animation rồi, chỉ cần hiệu chỉnh quỹ đạo (ở trên).
            if (hitter == 1) return;
            match.players[hitter].hitter.NotifyRemoteHit(new HitInfo
            {
                PlayerId = hitter, Type = (ShotType)type, Contact = contact, Velocity = s.Velocity,
                Quality = quality, Grade = (TimingGrade)grade, Jumped = jumped,
            });
        }

        void OnMatch(ulong sender, FastBufferReader r)
        {
            r.ReadValueSafe(out byte kind);
            var snap = r.ReadSnapshot();
            r.ReadValueSafe(out byte winner); r.ReadValueSafe(out byte faulter);
            r.ReadValueSafe(out byte reason); r.ReadValueSafe(out Vector3 land);

            switch ((MatchMsg)kind)
            {
                case MatchMsg.Serve:
                    match.ApplySnapshot(snap);
                    match.EnterServe();
                    _puppet.Clear();
                    break;
                case MatchMsg.Result:
                    match.ShowRallyResult(new RallyResult { Winner = winner, Faulter = faulter, Reason = (RallyEndReason)reason, LandingPoint = land });
                    break;
                case MatchMsg.Score:
                    match.ApplySnapshot(snap);
                    break;
            }
        }

        void OnAnnounce(ulong sender, FastBufferReader r)
        {
            r.ReadValueSafe(out string text);
            r.ReadValueSafe(out float seconds);
            match.Announce(text, seconds);
        }
    }
}
