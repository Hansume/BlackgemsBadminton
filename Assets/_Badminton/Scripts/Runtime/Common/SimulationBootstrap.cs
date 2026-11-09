using UnityEngine;

namespace Badminton
{
    /// <summary>
    /// Cấu hình nhịp mô phỏng cho toàn game, chạy tự động trước khi load scene đầu tiên.
    /// 60 Hz = tick rate mô phỏng cầu + di chuyển, cũng là tick rate mạng dự kiến ở GĐ5.
    /// </summary>
    public static class SimulationBootstrap
    {
        public const int TickRate = 60;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            Time.fixedDeltaTime = 1f / TickRate;
            // Tránh "vòng xoáy chết": máy yếu không bị dồn quá nhiều bước vật lý trong 1 frame.
            Time.maximumDeltaTime = 0.1f;
            Application.targetFrameRate = -1; // WebGL: theo requestAnimationFrame của trình duyệt
        }
    }
}
