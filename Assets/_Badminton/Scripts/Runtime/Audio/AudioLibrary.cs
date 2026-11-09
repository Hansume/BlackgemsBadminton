using UnityEngine;

namespace Badminton.Audio
{
    /// <summary>Danh sách âm thanh của game (các clip được tổng hợp bằng code – xem Editor/AudioSynthBuilder).</summary>
    [CreateAssetMenu(menuName = "Badminton/Audio Library", fileName = "AudioLibrary")]
    public class AudioLibrary : ScriptableObject
    {
        [Header("Tiếng đánh cầu")]
        public AudioClip hitSoft;
        public AudioClip hitClear;
        public AudioClip hitSmash;

        [Header("Cầu")]
        public AudioClip netHit;
        public AudioClip shuttleLand;

        [Header("Giày")]
        public AudioClip[] squeaks;

        [Header("Khán giả")]
        public AudioClip crowdLoop;
        public AudioClip cheer;
        public AudioClip applause;

        [Header("UI")]
        public AudioClip uiClick;
        public AudioClip whistle;
    }
}
