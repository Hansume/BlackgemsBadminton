using System;
using System.Collections.Generic;

namespace Badminton
{
    public enum OnlineState { Offline, Connecting, InRoom, InMatch, Error }

    public struct OnlinePlayerInfo
    {
        public string Name;
        public bool Ready;
        public bool IsLocal;
        public bool IsHost;
    }

    /// <summary>
    /// Dịch vụ chơi online mà UI sử dụng. Cài đặt nằm ở module Networking (asmdef riêng, phụ thuộc
    /// Netcode/Relay/Lobby) ⇒ phần game + UI không phụ thuộc thư viện mạng và vẫn build/test offline được.
    /// </summary>
    public interface IOnlineService
    {
        OnlineState State { get; }
        /// <summary>Thông báo trạng thái cho người chơi (đang kết nối, lỗi...).</summary>
        string Status { get; }
        /// <summary>Mã phòng 6 ký tự (khi đang ở trong phòng).</summary>
        string RoomCode { get; }
        int PingMs { get; }
        bool IsHost { get; }
        /// <summary>Unity Gaming Services (Relay + Lobby) đã được cấu hình cho project chưa.</summary>
        bool CloudAvailable { get; }
        IReadOnlyList<OnlinePlayerInfo> Players { get; }

        void QuickMatch();
        void CreateRoom();
        void JoinRoom(string code);
        /// <summary>Kết nối trực tiếp không qua Relay (LAN / test nội bộ).</summary>
        void HostDirect(ushort port);
        void JoinDirect(string address, ushort port);
        void SetReady(bool ready);
        void Leave();

        event Action Changed;
    }

    /// <summary>
    /// Điểm truy cập dịch vụ online. Module mạng KHÔNG tự tạo NetworkManager khi khởi động game nữa:
    /// nó chỉ đăng ký Factory; NetworkManager được tạo khi người chơi bấm "Chơi online" (Ensure),
    /// hoặc khi mở bằng link mời / tham số dòng lệnh test. Các chế độ offline (Giải đấu, Đấu với AI…)
    /// vì vậy không bao giờ có NetworkManager.
    /// </summary>
    public static class OnlineServices
    {
        static IOnlineService _current;

        public static IOnlineService Current
        {
            get
            {
                if (_current is UnityEngine.Object o && o == null) _current = null; // đã bị huỷ cùng scene
                return _current;
            }
            set => _current = value;
        }

        /// <summary>Module mạng đăng ký hàm tạo dịch vụ (tạo NetworkManager) – gọi khi cần.</summary>
        public static Func<IOnlineService> Factory;

        /// <summary>Ký tự ngăn cách tên và mã quốc tịch khi gửi qua mạng (không gõ được từ bàn phím).</summary>
        public const char CountrySep = '\u001F';

        /// <summary>Tên người chơi kèm quốc tịch (menu → NHÂN VẬT) để máy đối thủ hiện đúng cờ.</summary>
        public static string TaggedLocalName() => GameSettings.PlayerName + CountrySep + PlayerProfile.Country;

        /// <summary>Tách tên + mã quốc tịch (bản cũ không gửi quốc tịch ⇒ country = null).</summary>
        public static string SplitName(string tagged, out string country)
        {
            country = null;
            if (string.IsNullOrEmpty(tagged)) return tagged;
            int i = tagged.IndexOf(CountrySep);
            if (i < 0) return tagged;
            country = tagged.Substring(i + 1);
            return tagged.Substring(0, i);
        }

        public static string DisplayName(string tagged) => SplitName(tagged, out _);

        /// <summary>Tạo dịch vụ online nếu chưa có (null nếu bản build không có module mạng).</summary>
        public static IOnlineService Ensure()
        {
            if (Current == null && Factory != null) Current = Factory();
            return Current;
        }
    }
}
