using Badminton.Core;
using UnityEngine;

namespace Badminton.Shuttle
{
    [CreateAssetMenu(menuName = "Badminton/Shuttle Settings", fileName = "ShuttleSettings")]
    public class ShuttleSettings : ScriptableObject
    {
        public ShuttlePhysicsParams physics = new ShuttlePhysicsParams();

        [Header("Hiển thị")]
        [Tooltip("Phóng to model cầu để dễ nhìn từ camera xa (cầu thật chỉ dài ~8.5 cm).")]
        [Range(1f, 5f)] public float visualScale = 2.5f;

        [Tooltip("Trail chỉ hiện khi cầu bay nhanh hơn mức này (m/s).")]
        public float trailMinSpeed = 10f;
        [Tooltip("Tốc độ mà trail đạt độ đậm tối đa (m/s).")]
        public float trailFullSpeed = 40f;
    }
}
