using Badminton.Core;
using Badminton.Player;
using Badminton.Shots;
using Badminton.Shuttle;
using UnityEngine;

namespace Badminton.Audio
{
    /// <summary>
    /// Âm thanh trận đấu: tiếng vợt (theo loại cú đánh), cầu chạm lưới/sàn, tiếng giày rít khi
    /// đổi hướng gấp, tiếng khán giả nền + reo hò khi ghi điểm.
    /// Âm thanh 2D có pan trái/phải theo vị trí trên sân (rẻ hơn âm 3D, đủ cho góc camera cố định).
    /// Lưu ý WebGL: trình duyệt chỉ cho phát âm thanh sau lần click/bấm phím đầu tiên.
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        public AudioLibrary library;
        public Shuttlecock shuttle;
        public PlayerMotor[] players;

        [Header("Tiếng giày rít")]
        [Tooltip("Thay đổi vận tốc tối thiểu trong ~0.13 s để phát tiếng rít (m/s).")]
        public float squeakDeltaV = 2.6f;
        public float squeakCooldown = 0.45f;

        const int PoolSize = 10;
        AudioSource[] _pool;
        int _next;
        AudioSource _crowd;
        Vector3[] _lastVel;
        float[] _squeakReady;
        int _step;

        void Awake()
        {
            Instance = this;
            _pool = new AudioSource[PoolSize];
            for (int i = 0; i < PoolSize; i++)
            {
                var src = gameObject.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 0f;
                _pool[i] = src;
            }
            _crowd = gameObject.AddComponent<AudioSource>();
            _crowd.loop = true;
            _crowd.playOnAwake = false;
        }

        void Start()
        {
            if (library != null && library.crowdLoop != null)
            {
                _crowd.clip = library.crowdLoop;
                _crowd.Play();
            }
            if (shuttle != null)
            {
                shuttle.NetHit += p => Play(library.netHit, 0.7f, p, 1f + Random.Range(-0.05f, 0.05f));
                shuttle.Landed += (p, _) => Play(library.shuttleLand, 0.6f, p, Random.Range(0.9f, 1.1f));
            }
            if (players != null)
            {
                _lastVel = new Vector3[players.Length];
                _squeakReady = new float[players.Length];
                foreach (var m in players)
                {
                    var h = m != null ? m.GetComponent<PlayerHitter>() : null;
                    if (h != null) h.Hit += OnHit;
                }
            }
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void Update()
        {
            if (_crowd != null) _crowd.volume = GameSettings.CrowdVolume * 0.5f;
        }

        void FixedUpdate()
        {
            if (players == null || library == null || library.squeaks == null || library.squeaks.Length == 0) return;
            for (int i = 0; i < players.Length; i++)
            {
                var m = players[i];
                if (m == null) continue;
                Vector3 v = m.Velocity;
                // Giày rít khi phanh gấp / đổi hướng đột ngột: so với vận tốc ~8 bước trước.
                float dv = (v - _lastVel[i]).magnitude;
                if (dv > squeakDeltaV && Mathf.Max(_lastVel[i].magnitude, v.magnitude) > 2.5f && Time.time > _squeakReady[i])
                {
                    _squeakReady[i] = Time.time + squeakCooldown;
                    var clip = library.squeaks[Random.Range(0, library.squeaks.Length)];
                    Play(clip, 0.45f, m.Position, Random.Range(0.9f, 1.15f));
                }
                if (_step % 8 == 0) _lastVel[i] = v;
            }
            _step++;
        }

        void OnHit(HitInfo h)
        {
            AudioClip clip; float vol, pitch;
            switch (h.Type)
            {
                case ShotType.Smash: clip = library.hitSmash; vol = 1f; pitch = h.Jumped ? 0.95f : 1f; break;
                case ShotType.Clear:
                case ShotType.Lift:
                case ShotType.Serve: clip = library.hitClear; vol = 0.85f; pitch = 1f; break;
                default: clip = library.hitSoft; vol = 0.65f; pitch = 1.05f; break;
            }
            vol *= Mathf.Lerp(0.6f, 1f, h.Quality); // cú đánh "ngọt" nghe đanh hơn
            Play(clip, vol, h.Contact, pitch + Random.Range(-0.04f, 0.04f));
        }

        /// <summary>Khán giả phản ứng khi kết thúc điểm (to hơn với điểm hay / cú đập).</summary>
        public void Cheer(bool big)
        {
            if (library == null) return;
            PlayCrowd(big ? library.cheer : library.applause, big ? 0.9f : 0.6f);
        }

        public void PlayUi(AudioClip clip) => Play(clip, 0.5f, null, 1f);
        public void Click() { if (library != null) PlayUi(library.uiClick); }
        public void Whistle() { if (library != null) PlayUi(library.whistle); }

        void PlayCrowd(AudioClip clip, float vol)
        {
            if (clip == null) return;
            var src = NextSource();
            src.panStereo = 0f;
            src.pitch = Random.Range(0.95f, 1.05f);
            src.PlayOneShot(clip, vol * GameSettings.CrowdVolume);
        }

        void Play(AudioClip clip, float vol, Vector3? worldPos, float pitch)
        {
            if (clip == null) return;
            var src = NextSource();
            // Pan theo trục X của sân, nhìn từ camera (camera ở Far thì đảo chiều).
            float pan = 0f;
            if (worldPos.HasValue)
            {
                var cam = Camera.main;
                float x = worldPos.Value.x;
                if (cam != null && cam.transform.forward.z < 0f) x = -x;
                pan = Mathf.Clamp(x / 6f, -0.8f, 0.8f);
            }
            src.panStereo = pan;
            src.pitch = pitch;
            src.PlayOneShot(clip, vol * GameSettings.SfxVolume);
        }

        AudioSource NextSource()
        {
            var s = _pool[_next];
            _next = (_next + 1) % _pool.Length;
            return s;
        }
    }
}
