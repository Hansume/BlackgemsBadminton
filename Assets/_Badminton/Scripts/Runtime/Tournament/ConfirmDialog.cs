using System;
using TMPro;
using UnityEngine;
using F = Badminton.UI.UIFactory;

namespace Badminton.TournamentMode
{
    /// <summary>
    /// Hộp thoại xác nhận dạng modal (che toàn màn hình, chặn click phía sau).
    /// Dùng cho: thoát trận giải đang đánh (xử thua), ghi đè slot lưu, xoá slot…
    /// </summary>
    public static class ConfirmDialog
    {
        static GameObject _open;
        static Action _onCancel;

        public static bool IsOpen => _open != null;

        public static void Show(Transform canvas, string title, string message, string okLabel, Action onOk,
                                string cancelLabel = "HUỶ", Action onCancel = null, bool dangerous = false)
        {
            Close();
            var T = F.Theme;
            var root = F.Stretch(F.Rect("Confirm Dialog", canvas));
            root.SetAsLastSibling();
            _open = root.gameObject;
            _onCancel = onCancel;

            var dim = F.Image(root, "Dim", new Color(0, 0, 0, 0.7f));
            F.Stretch(dim.rectTransform); // raycastTarget = true ⇒ chặn click xuống dưới

            var panel = F.Image(root, "Panel", T.navy);
            F.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1000, 420));
            var stripe = F.Image(panel.transform, "Stripe", dangerous ? T.danger : T.accent);
            F.Place(stripe.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(1000, 8));

            var col = F.VStack(panel.transform, "Column", 24);
            F.Place(col, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var t = F.Label(col, title, 48, dangerous ? T.danger : T.accent, TextAlignmentOptions.Center, T.titleFont);
            F.Size(t, 900, 64);
            var m = F.Label(col, message, 30, T.text, TextAlignmentOptions.Center);
            m.textWrappingMode = TextWrappingModes.Normal;
            F.Size(m, 880, 130);

            var row = F.HStack(col, "Buttons", 24);
            F.Button(row, cancelLabel, Cancel, 440, 72, false, 28);
            F.Button(row, okLabel, () => { Close(); onOk?.Invoke(); }, 440, 72, true, 28);
        }

        /// <summary>Esc / nút huỷ.</summary>
        public static void Cancel()
        {
            var cb = _onCancel;
            Close();
            cb?.Invoke();
        }

        public static void Close()
        {
            if (_open != null) UnityEngine.Object.Destroy(_open);
            _open = null;
            _onCancel = null;
        }
    }
}
