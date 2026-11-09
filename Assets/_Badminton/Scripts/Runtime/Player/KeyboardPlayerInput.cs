using System;
using Badminton.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Badminton.Player
{
    /// <summary>
    /// Điều khiển bằng bàn phím (+ gamepad) qua bộ phím chung GameControls (đổi phím được trong Cài đặt).
    /// </summary>
    public class KeyboardPlayerInput : MonoBehaviour, IPlayerInputSource
    {
        static readonly ShotType[] ShotKeys = { ShotType.Clear, ShotType.Smash, ShotType.Drop, ShotType.Serve };

        bool _hasPress;
        ShotPress _press;
        readonly Action<InputAction.CallbackContext>[] _handlers = new Action<InputAction.CallbackContext>[ShotKeys.Length];

        /// <summary>Khi false (đang mở menu, tạm dừng...) thì không nhận phím.</summary>
        public static bool GameplayEnabled = true;

        public Vector2 Move => GameplayEnabled && enabled
            ? Vector2.ClampMagnitude(GameControls.Move.ReadValue<Vector2>(), 1f)
            : Vector2.zero;

        public bool TryConsumeShot(out ShotPress press)
        {
            press = _press;
            if (!_hasPress) return false;
            _hasPress = false;
            return true;
        }

        void OnEnable()
        {
            GameControls.Ensure();
            for (int i = 0; i < ShotKeys.Length; i++)
            {
                var type = ShotKeys[i];
                _handlers[i] = _ => OnShot(type);
                GameControls.Shot(type).performed += _handlers[i];
            }
        }

        void OnDisable()
        {
            for (int i = 0; i < ShotKeys.Length; i++)
                if (_handlers[i] != null) GameControls.Shot(ShotKeys[i]).performed -= _handlers[i];
            _hasPress = false;
        }

        void OnShot(ShotType type)
        {
            if (!GameplayEnabled) return;
            // Chỉ giữ lần bấm mới nhất; PlayerHitter tự bỏ qua nếu quá cũ (input buffer).
            _press = new ShotPress { Type = type, Aim = Move, Time = Time.timeAsDouble };
            _hasPress = true;
        }
    }
}
