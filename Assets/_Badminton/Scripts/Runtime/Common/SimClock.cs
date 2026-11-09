using UnityEngine;

namespace Badminton
{
    /// <summary>
    /// Đồng hồ mô phỏng: đếm số bước FixedUpdate (60 Hz) kể từ khi chạy game.
    /// Netcode dùng tick này để đánh dấu thời điểm (cú đánh, snapshot) và tua lại quá khứ (lag compensation).
    /// Chạy TRƯỚC mọi script khác trong mỗi bước vật lý.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class SimClock : MonoBehaviour
    {
        /// <summary>Tick của bước vật lý đang chạy.</summary>
        public static int Tick { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            if (FindFirstObjectByType<SimClock>() != null) return;
            var go = new GameObject("[SimClock]") { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(go);
            go.AddComponent<SimClock>();
        }

        void FixedUpdate() => Tick++;
    }
}
