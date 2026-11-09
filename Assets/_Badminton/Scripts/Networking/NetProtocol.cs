using Badminton.Core;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Badminton.Networking
{
    /// <summary>
    /// Giao thức mạng: dùng "named message" của Netcode for GameObjects thay vì NetworkObject/NetworkVariable
    /// ⇒ kiểm soát hoàn toàn dữ liệu gửi đi (nhỏ gọn, phù hợp WebSocket trên WebGL).
    ///
    /// Client → Host : bm.hello (tên), bm.ready, bm.in (input mỗi tick), bm.hit (yêu cầu đánh cầu + tick)
    /// Host → Client : bm.lobby, bm.start, bm.snap (vị trí người chơi 30 Hz), bm.shut (trạng thái cầu),
    ///                 bm.launch (cú đánh đã xác nhận), bm.match (tỷ số / lượt giao / kết quả), bm.ann (thông báo)
    /// </summary>
    public static class Msg
    {
        public const string Hello = "bm.hello";
        public const string Ready = "bm.ready";
        public const string Lobby = "bm.lobby";
        public const string Start = "bm.start";
        public const string Input = "bm.in";
        public const string Hit = "bm.hit";
        public const string Snap = "bm.snap";
        public const string Shuttle = "bm.shut";
        public const string Launch = "bm.launch";
        public const string Match = "bm.match";
        public const string Announce = "bm.ann";

        public static FastBufferWriter Writer(int size = 256) => new FastBufferWriter(size, Allocator.Temp, 4096);

        public static void Send(NetworkManager nm, string name, ulong to, FastBufferWriter w, NetworkDelivery d = NetworkDelivery.ReliableSequenced)
        {
            if (nm == null || !nm.IsListening || nm.CustomMessagingManager == null) return;
            nm.CustomMessagingManager.SendNamedMessage(name, to, w, d);
        }

        // ── Kiểu dữ liệu game ↔ buffer ──

        public static void Write(this FastBufferWriter w, in MatchSnapshot s)
        {
            w.WriteValueSafe(s.Score0); w.WriteValueSafe(s.Score1);
            w.WriteValueSafe(s.Games0); w.WriteValueSafe(s.Games1);
            w.WriteValueSafe(s.Server); w.WriteValueSafe(s.GameNumber); w.WriteValueSafe(s.MatchWinner);
            w.WriteValueSafe((sbyte)s.Player0Side);
            w.WriteValueSafe(s.IntervalDone); w.WriteValueSafe(s.DecidingEndsChanged);
        }

        public static MatchSnapshot ReadSnapshot(this FastBufferReader r)
        {
            var s = new MatchSnapshot();
            r.ReadValueSafe(out s.Score0); r.ReadValueSafe(out s.Score1);
            r.ReadValueSafe(out s.Games0); r.ReadValueSafe(out s.Games1);
            r.ReadValueSafe(out s.Server); r.ReadValueSafe(out s.GameNumber); r.ReadValueSafe(out s.MatchWinner);
            r.ReadValueSafe(out sbyte side); s.Player0Side = (CourtSide)side;
            r.ReadValueSafe(out s.IntervalDone); r.ReadValueSafe(out s.DecidingEndsChanged);
            return s;
        }

        public static void Write(this FastBufferWriter w, in ShuttleState s)
        {
            w.WriteValueSafe(s.Position); w.WriteValueSafe(s.Velocity);
            w.WriteValueSafe((byte)s.Phase); w.WriteValueSafe(s.Bounces);
        }

        public static ShuttleState ReadShuttle(this FastBufferReader r)
        {
            var s = new ShuttleState();
            r.ReadValueSafe(out s.Position); r.ReadValueSafe(out s.Velocity);
            r.ReadValueSafe(out byte phase); s.Phase = (ShuttlePhase)phase;
            r.ReadValueSafe(out s.Bounces);
            return s;
        }
    }

    /// <summary>
    /// Nguồn điều khiển cho người chơi ở xa, chạy trên HOST: lấy hướng di chuyển mới nhất client gửi lên.
    /// Cú đánh của client KHÔNG đi qua đây mà qua bm.hit (có tick để tua lại – lag compensation).
    /// </summary>
    public class NetworkInputSource : Player.IPlayerInputSource
    {
        public Vector2 Move { get; private set; }
        /// <summary>Tick phía client của gói input mới nhất (host báo lại để client hiệu chỉnh dự đoán).</summary>
        public int LastClientTick { get; private set; } = -1;

        public void Push(int clientTick, Vector2 move)
        {
            if (clientTick <= LastClientTick) return; // gói cũ đến muộn (UDP/WebSocket không thứ tự) ⇒ bỏ
            LastClientTick = clientTick;
            Move = Vector2.ClampMagnitude(move, 1f);
        }

        public void Reset() { Move = Vector2.zero; LastClientTick = -1; }

        public bool TryConsumeShot(out Player.ShotPress press) { press = default; return false; }
    }
}
