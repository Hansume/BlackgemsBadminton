using System.Collections.Generic;
using Badminton.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Badminton.Player
{
    /// <summary>
    /// Bộ phím dùng chung toàn game (Input System), tạo một lần và lưu phím đã đổi vào PlayerPrefs.
    /// Mặc định: WASD di chuyển, J Clear, K Smash, L Drop, I Net/Lift, Space giao cầu, Esc tạm dừng.
    /// </summary>
    public static class GameControls
    {
        const string PrefKey = "bm.bindings";

        static InputActionMap _map;
        public static InputAction Move { get; private set; }
        public static InputAction Pause { get; private set; }
        public static InputAction CameraToggle { get; private set; }
        static readonly Dictionary<ShotType, InputAction> _shots = new Dictionary<ShotType, InputAction>();

        /// <summary>Các phím cho phép đổi trong màn hình Cài đặt: (tên hiển thị, action, chỉ số binding).</summary>
        public static readonly List<(string label, InputAction action, int bindingIndex)> Rebindable =
            new List<(string, InputAction, int)>();

        public static InputActionMap Map { get { Ensure(); return _map; } }

        public static InputAction Shot(ShotType t) { Ensure(); return _shots[t]; }

        public static void Ensure()
        {
            if (_map != null) return;
            _map = new InputActionMap("Gameplay");

            Move = _map.AddAction("Move", InputActionType.Value);
            Move.expectedControlType = "Vector2";
            Move.AddCompositeBinding("2DVector")                       // binding 0 = composite, 1..4 = các hướng
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            Move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            Move.AddBinding("<Gamepad>/leftStick");

            AddShot(ShotType.Clear, "Clear", "<Keyboard>/j", "<Gamepad>/buttonNorth");
            AddShot(ShotType.Smash, "Smash", "<Keyboard>/k", "<Gamepad>/rightTrigger");
            AddShot(ShotType.Drop, "Drop", "<Keyboard>/l", "<Gamepad>/buttonWest");
            AddShot(ShotType.Serve, "Serve", "<Keyboard>/space", "<Gamepad>/buttonSouth");

            Pause = _map.AddAction("Pause", InputActionType.Button);
            Pause.AddBinding("<Keyboard>/escape");
            Pause.AddBinding("<Gamepad>/start");
            CameraToggle = _map.AddAction("CameraToggle", InputActionType.Button);
            CameraToggle.AddBinding("<Keyboard>/c");
            CameraToggle.AddBinding("<Gamepad>/select");

            Rebindable.Clear();
            Rebindable.Add(("Lên (về lưới)", Move, 1));
            Rebindable.Add(("Xuống", Move, 2));
            Rebindable.Add(("Trái", Move, 3));
            Rebindable.Add(("Phải", Move, 4));
            Rebindable.Add(("Cầu dài (Clear / Lift)", _shots[ShotType.Clear], 0));
            Rebindable.Add(("Smash (đập)", _shots[ShotType.Smash], 0));
            Rebindable.Add(("Cầu ngắn (Drop / Net)", _shots[ShotType.Drop], 0));
            Rebindable.Add(("Giao cầu", _shots[ShotType.Serve], 0));

            LoadOverrides();
            _map.Enable();
        }

        static void AddShot(ShotType t, string name, string key, string pad)
        {
            var a = _map.AddAction(name, InputActionType.Button);
            a.AddBinding(key);
            a.AddBinding(pad);
            _shots[t] = a;
        }

        public static string DisplayName(InputAction a, int index) =>
            a.GetBindingDisplayString(index, InputBinding.DisplayStringOptions.DontIncludeInteractions).ToUpperInvariant();

        /// <summary>Bắt đầu đổi phím: chờ người chơi bấm một phím bàn phím (Esc để huỷ).</summary>
        public static void StartRebind(InputAction action, int index, System.Action done)
        {
            Ensure();
            _map.Disable();
            action.PerformInteractiveRebinding(index)
                .WithControlsHavingToMatchPath("<Keyboard>")
                .WithCancelingThrough("<Keyboard>/escape")
                .OnMatchWaitForAnother(0.05f)
                .OnComplete(op => { op.Dispose(); SaveOverrides(); _map.Enable(); done?.Invoke(); })
                .OnCancel(op => { op.Dispose(); _map.Enable(); done?.Invoke(); })
                .Start();
        }

        public static void ResetAll()
        {
            Ensure();
            _map.RemoveAllBindingOverrides();
            PlayerPrefs.DeleteKey(PrefKey);
        }

        static void SaveOverrides()
        {
            try { PlayerPrefs.SetString(PrefKey, _map.SaveBindingOverridesAsJson()); PlayerPrefs.Save(); }
            catch (System.Exception e) { Debug.LogWarning("[GameControls] Không lưu được phím: " + e.Message); }
        }

        static void LoadOverrides()
        {
            try
            {
                string json = PlayerPrefs.GetString(PrefKey, "");
                if (!string.IsNullOrEmpty(json)) _map.LoadBindingOverridesFromJson(json);
            }
            catch (System.Exception e) { Debug.LogWarning("[GameControls] Không đọc được phím đã lưu: " + e.Message); }
        }
    }
}
