using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Badminton.Replay
{
    /// <summary>
    /// Replay chậm sau cú smash ghi điểm.
    ///
    /// Ghi lại vị trí/góc quay của các transform được theo dõi (người chơi + các khớp + quả cầu)
    /// với tần số cố định 60 Hz vào bộ đệm vòng ~4 giây. Khi phát lại: mọi thứ khác đã đứng yên
    /// (MatchController đang ở trạng thái PointOver), ReplaySystem chạy SAU CÙNG trong LateUpdate
    /// và ghi đè transform bằng dữ liệu đã ghi, nội suy giữa 2 mẫu.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public class ReplaySystem : MonoBehaviour
    {
        [Tooltip("Gốc các đối tượng cần ghi (người chơi, quả cầu). Toàn bộ transform con cũng được ghi.")]
        public Transform[] roots;
        public TrailRenderer shuttleTrail;

        public float bufferSeconds = 4f;
        public float sampleRate = 60f;

        public bool IsPlaying { get; private set; }

        Transform[] _tracked;
        struct Frame { public float t; public Vector3[] p; public Quaternion[] r; }
        Frame[] _frames;
        int _head, _count;
        float _nextSample;

        // Phát lại
        float _playT, _playEnd, _speed;
        Vector3[] _saveP;
        Quaternion[] _saveR;

        void Start()
        {
            var list = new List<Transform>();
            foreach (var root in roots)
                if (root != null) list.AddRange(root.GetComponentsInChildren<Transform>(true));
            _tracked = list.ToArray();

            int n = Mathf.CeilToInt(bufferSeconds * sampleRate);
            _frames = new Frame[n];
            for (int i = 0; i < n; i++)
                _frames[i] = new Frame { p = new Vector3[_tracked.Length], r = new Quaternion[_tracked.Length] };
        }

        void LateUpdate()
        {
            if (IsPlaying) { Apply(); return; }
            if (Time.time < _nextSample) return;
            _nextSample = Time.time + 1f / sampleRate;

            ref var f = ref _frames[_head];
            f.t = Time.time;
            for (int i = 0; i < _tracked.Length; i++)
            {
                f.p[i] = _tracked[i].position;
                f.r[i] = _tracked[i].rotation;
            }
            _head = (_head + 1) % _frames.Length;
            _count = Mathf.Min(_count + 1, _frames.Length);
        }

        /// <summary>
        /// Phát lại đoạn [endTime − window, endTime + tail] với tốc độ speed (0.4 = chậm 2.5 lần).
        /// Dùng trong coroutine: yield return replay.Play(...).
        /// </summary>
        public IEnumerator Play(float endTime, float window = 1.1f, float tail = 0.25f, float speed = 0.4f)
        {
            if (_count < 2) yield break;

            // Chờ ghi xong phần "đuôi" sau thời điểm cầu chạm sàn.
            while (Time.time < endTime + tail) yield return null;

            float oldest = Oldest().t;
            _playT = Mathf.Max(oldest, endTime - window);
            _playEnd = endTime + tail;
            _speed = speed;
            IsPlaying = true;
            if (shuttleTrail != null) shuttleTrail.Clear();

            // Lưu tư thế hiện tại để khôi phục sau replay.
            _saveP = new Vector3[_tracked.Length];
            _saveR = new Quaternion[_tracked.Length];
            for (int i = 0; i < _tracked.Length; i++) { _saveP[i] = _tracked[i].position; _saveR[i] = _tracked[i].rotation; }

            while (IsPlaying && _playT < _playEnd)
            {
                _playT += Time.deltaTime * _speed;
                yield return null;
            }
            Stop();
        }

        /// <summary>
        /// Dừng replay ngay (an toàn khi không phát). BẮT BUỘC gọi khi coroutine Play bị huỷ giữa chừng
        /// (StopAllCoroutines lúc thoát/bắt đầu trận) – nếu không IsPlaying kẹt ở true và replay cũ
        /// đè hình lên trận mới mãi mãi.
        /// </summary>
        public void Stop()
        {
            if (!IsPlaying) return;
            IsPlaying = false;
            if (_saveP != null)
                for (int i = 0; i < _tracked.Length; i++) _tracked[i].SetPositionAndRotation(_saveP[i], _saveR[i]);
            _saveP = null; _saveR = null;
            if (shuttleTrail != null) shuttleTrail.Clear();
        }

        /// <summary>Xoá bộ đệm ghi hình (trận mới không replay lại cảnh của trận cũ).</summary>
        public void ClearBuffer() { _head = 0; _count = 0; }

        Frame Oldest() => _frames[(_head - _count + _frames.Length) % _frames.Length];

        void Apply()
        {
            // Tìm 2 mẫu bao quanh _playT rồi nội suy.
            int n = _frames.Length;
            int start = (_head - _count + n) % n;
            for (int k = 0; k < _count - 1; k++)
            {
                ref var a = ref _frames[(start + k) % n];
                ref var b = ref _frames[(start + k + 1) % n];
                if (_playT < a.t || _playT > b.t) continue;
                float w = b.t > a.t ? (_playT - a.t) / (b.t - a.t) : 0f;
                for (int i = 0; i < _tracked.Length; i++)
                    _tracked[i].SetPositionAndRotation(Vector3.Lerp(a.p[i], b.p[i], w), Quaternion.Slerp(a.r[i], b.r[i], w));
                if (shuttleTrail != null) shuttleTrail.emitting = true; // Shuttlecock tắt trail khi cầu nằm yên – bật lại cho replay
                return;
            }
        }
    }
}
