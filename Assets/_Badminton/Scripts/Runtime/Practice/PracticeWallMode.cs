using Badminton.Cameras;
using Badminton.Core;
using Badminton.Player;
using Badminton.Shots;
using Badminton.Shuttle;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Badminton.Practice
{
    /// <summary>
    /// Chế độ test GĐ2: một người chơi đánh cầu vào tường dựng ở nửa sân bên kia.
    ///
    /// Tường có 2 kiểu bật lại:
    ///  - Feeder  : tường "thông minh" – giải ngược vận tốc để cầu bay về một điểm ngẫu nhiên
    ///              trong nửa sân người chơi (giống máy bắn cầu). Dễ tập liên tục.
    ///  - Physical: phản xạ vật lý thuần (restitution). Cầu lông mất tốc rất nhanh nên thường rơi gần tường.
    ///
    /// Phím phụ: R = bắn cầu mới, C = đổi camera broadcast,
    ///           T = đổi kiểu tường, F1 = ẩn/hiện HUD.
    /// </summary>
    public class PracticeWallMode : MonoBehaviour
    {
        public enum WallMode { Feeder, Physical }

        public Shuttlecock shuttle;
        public PlayerMotor player;
        public PlayerHitter hitter;
        public CameraRig cameraRig;
        public LandingMarker landingMarker;
        [Tooltip("Vòng căn thời gian đánh cầu (thu nhỏ dần, tắt khi cầu chạm sàn).")]
        public TimingRing timingRing;
        [Tooltip("Scene menu chính (Esc để quay về).")]
        public string menuScene = "Game";

        [Header("Tường")]
        public WallMode wallMode = WallMode.Feeder;
        [Tooltip("Vị trí mặt tường theo trục Z (nửa sân Far).")]
        public float wallZ = 4.5f;
        public float wallHalfWidth = 6f;
        public float wallHeight = 9f;
        [Range(0f, 1f)] public float wallRestitution = 0.6f;

        [Header("Máy bắn cầu / tường Feeder")]
        [Tooltip("Vùng điểm rơi ngẫu nhiên trong nửa sân người chơi: x trong ±feedHalfWidth, |z| trong [feedMinDepth, feedMaxDepth].")]
        public float feedHalfWidth = 2.2f;
        public float feedMinDepth = 2.2f;
        public float feedMaxDepth = 6.2f;
        [Tooltip("Khoảng góc nâng khi bật lại (độ) – thấp = cầu nhanh, cao = cầu bổng.")]
        public Vector2 feedElevation = new Vector2(20f, 60f);
        [Tooltip("Tự bắn cầu mới sau khi cầu chạm sàn (s). 0 = tắt.")]
        public float autoFeedDelay = 1.5f;

        int _rally, _bestRally;
        string _lastShot = "-", _lastLanding = "-", _message = "";
        float _messageUntil, _feedAt = -1f;
        bool _showHud = true;
        System.Random _rng = new System.Random(7);

        void Start()
        {
            shuttle.Physics.Walls.Add(new ShuttleWall
            {
                Point = new Vector3(0f, 0f, wallZ),
                Normal = Vector3.back, // tường quay mặt về phía người chơi (Near)
                HalfWidth = wallHalfWidth,
                Height = wallHeight,
                Restitution = wallRestitution,
            });

            shuttle.WallHit += OnWallHit;
            shuttle.Landed += OnLanded;
            shuttle.NetHit += _ => Flash("Chạm lưới!");
            hitter.Hit += OnPlayerHit;
            hitter.Rejected += (t, why) => Flash(why);

            if (cameraRig != null) cameraRig.panTarget = shuttle.transform;
            if (timingRing != null) timingRing.hitter = hitter;
            Feed();
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.rKey.wasPressedThisFrame) Feed();
                if (kb.cKey.wasPressedThisFrame && cameraRig != null)
                {
                    cameraRig.SetBroadcast(!cameraRig.broadcastCamera);
                    Flash("Broadcast camera: " + (cameraRig.broadcastCamera ? "BẬT" : "TẮT"));
                }
                if (kb.tKey.wasPressedThisFrame)
                {
                    wallMode = wallMode == WallMode.Feeder ? WallMode.Physical : WallMode.Feeder;
                    Flash("Kiểu tường: " + wallMode);
                }
                if (kb.f1Key.wasPressedThisFrame) _showHud = !_showHud;
                if (kb.escapeKey.wasPressedThisFrame && Application.CanStreamedLevelBeLoaded(menuScene))
                    UnityEngine.SceneManagement.SceneManager.LoadScene(menuScene); // về menu chính
            }

            if (_feedAt > 0f && Time.time >= _feedAt) { _feedAt = -1f; Feed(); }
        }

        /// <summary>Bắn cầu từ tường về phía người chơi.</summary>
        public void Feed()
        {
            _feedAt = -1f;
            var from = new Vector3(RandRange(-1.5f, 1.5f), RandRange(2.2f, 3.2f), wallZ - 0.1f);
            shuttle.Launch(from, FeedVelocity(from), -1);
        }

        Vector3 FeedVelocity(Vector3 from)
        {
            var target = new Vector3(RandRange(-feedHalfWidth, feedHalfWidth), 0f, -RandRange(feedMinDepth, feedMaxDepth));
            float el = RandRange(feedElevation.x, feedElevation.y);
            return shuttle.Physics.SolveLaunchVelocity(from, target, el, Time.fixedDeltaTime);
        }

        void OnWallHit(int index, Vector3 point)
        {
            _rally++;
            _bestRally = Mathf.Max(_bestRally, _rally);
            shuttle.ClearLastHitter(); // tường trả cầu ⇒ người chơi được đánh tiếp
            if (wallMode == WallMode.Feeder) shuttle.SetVelocity(FeedVelocity(point));
        }

        void OnPlayerHit(HitInfo h)
        {
            _lastShot = $"{h.Type}  {h.Velocity.magnitude * 3.6f:0} km/h  {h.Grade} ({h.Quality:0.00}){(h.Jumped ? " NHẢY" : "")}";
        }

        void OnLanded(Vector3 p, bool inside)
        {
            bool mySide = CourtDimensions.SideOf(p.z) == player.side;
            _lastLanding = $"{(inside ? "TRONG" : "NGOÀI")} ({p.x:0.00}, {p.z:0.00}){(mySide ? " – sân mình" : "")}";
            if (mySide) _rally = 0; // để rơi bên sân mình ⇒ mất chuỗi
            if (autoFeedDelay > 0f) _feedAt = Time.time + autoFeedDelay;
        }

        void Flash(string msg) { _message = msg; _messageUntil = Time.time + 1.5f; }

        float RandRange(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

        // HUD debug tạm (GĐ4 thay bằng UI kiểu truyền hình). OnGUI chạy được cả trên WebGL.
        void OnGUI()
        {
            if (!_showHud) return;
            var st = shuttle.State;
            GUILayout.BeginArea(new Rect(12, 12, 380, 260), GUI.skin.box);
            GUILayout.Label("<b>GĐ2 – TẬP VỚI TƯỜNG</b>  (F1 ẩn)", Rich);
            GUILayout.Label($"Chuỗi: {_rally}   Kỷ lục: {_bestRally}");
            GUILayout.Label($"Cú đánh cuối: {_lastShot}");
            GUILayout.Label($"Cầu: {st.Phase}  cao {st.Position.y:0.00} m  tốc độ {st.Velocity.magnitude * 3.6f:0} km/h");
            GUILayout.Label($"Điểm rơi: {_lastLanding}");
            GUILayout.Label($"Tường: {wallMode} (T)   Camera: {(cameraRig != null && cameraRig.broadcastCamera ? "Broadcast" : "Người chơi")} (C)");
            GUILayout.Space(4);
            GUILayout.Label("WASD chạy • J Clear • K Smash • L Drop • I Net/Lift • Space giao\nR bắn cầu mới • Esc về menu");
            if (Time.time < _messageUntil) GUILayout.Label($"<color=yellow><b>{_message}</b></color>", Rich);
            GUILayout.EndArea();
        }

        static GUIStyle _rich;
        static GUIStyle Rich => _rich ??= new GUIStyle(GUI.skin.label) { richText = true };
    }
}
