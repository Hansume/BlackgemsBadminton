using Badminton.Core;
using UnityEngine;

namespace Badminton.Player
{
    /// <summary>Một lần bấm phím đánh cầu.</summary>
    public struct ShotPress
    {
        public ShotType Type;
        /// <summary>Hướng WASD tại lúc bấm – x: phải/trái, y: dài/ngắn (theo góc nhìn người đánh).</summary>
        public Vector2 Aim;
        /// <summary>Thời điểm bấm (Time.timeAsDouble). GĐ3 dùng tính timing, GĐ5 dùng cho lag compensation.</summary>
        public double Time;
    }

    /// <summary>
    /// Nguồn điều khiển nhân vật. Bàn phím, AI (GĐ3) và input từ mạng (GĐ5) đều cài interface này
    /// ⇒ PlayerMotor/ShotSystem không cần biết ai đang điều khiển.
    /// </summary>
    public interface IPlayerInputSource
    {
        /// <summary>Hướng di chuyển theo góc nhìn người chơi: y+ = tiến về lưới, x+ = sang phải. Độ dài ≤ 1.</summary>
        Vector2 Move { get; }

        /// <summary>Lấy (và xoá) lần bấm đánh cầu đang chờ, nếu có.</summary>
        bool TryConsumeShot(out ShotPress press);
    }
}
