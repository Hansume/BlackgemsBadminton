using Badminton.Core;
using UnityEngine;

namespace Badminton.Shots
{
    /// <summary>
    /// Animation vung vợt procedural tạm thời (GĐ4 thay bằng Animator + clip thật).
    /// Xoay "vai" (pivot) quanh trục X cục bộ: cú trên đầu vung từ sau ra trước,
    /// cú thấp (net, lift, giao cầu) vung từ dưới lên.
    /// </summary>
    public class RacketSwing : MonoBehaviour
    {
        public Transform pivot;
        public float duration = 0.22f;

        Quaternion _rest;
        float _t = 1f;
        float _from, _to;

        void Awake()
        {
            if (pivot == null) pivot = transform;
            _rest = pivot.localRotation;
        }

        public void Play(ShotType type)
        {
            bool overhead = type == ShotType.Clear || type == ShotType.Smash || type == ShotType.Drop;
            // Góc theo trục X: âm = vợt ngả ra sau/lên, dương = vợt hạ về trước.
            (_from, _to) = overhead ? (-70f, 80f) : (110f, -40f);
            _t = 0f;
        }

        void Update()
        {
            if (_t >= 1f) return;
            _t = Mathf.Min(1f, _t + Time.deltaTime / duration);

            // 0→0.5: vung; 0.5→1: thu vợt về tư thế chờ.
            float angle = _t < 0.5f
                ? Mathf.Lerp(_from, _to, EaseOut(_t * 2f))
                : Mathf.Lerp(_to, 0f, (_t - 0.5f) * 2f);
            pivot.localRotation = _rest * Quaternion.Euler(angle, 0f, 0f);
        }

        static float EaseOut(float x) => 1f - (1f - x) * (1f - x);
    }
}
