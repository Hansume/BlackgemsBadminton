using Badminton.Core;
using UnityEngine;

namespace Badminton.Shots
{
    /// <summary>Một loại cú đánh – mỗi loại là 1 asset để tinh chỉnh độc lập trong Inspector.</summary>
    [CreateAssetMenu(menuName = "Badminton/Shot Definition", fileName = "Shot_")]
    public class ShotDefinition : ScriptableObject
    {
        public ShotType type;
        public ShotParams parameters;

        [Header("Điều kiện đánh")]
        [Tooltip("Cầu phải cao hơn mức này mới đánh được (smash cần cầu trên cao).")]
        public float minContactHeight = 0f;
        [Tooltip("Cầu phải thấp hơn mức này (0 = không giới hạn).")]
        public float maxContactHeight = 0f;

        public bool CanHitAt(float height) =>
            height >= minContactHeight && (maxContactHeight <= 0f || height <= maxContactHeight);
    }
}
