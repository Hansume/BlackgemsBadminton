using System;
using UnityEngine;

namespace Badminton.TournamentMode
{
    /// <summary>Thông tin tóm tắt của một slot lưu mùa giải (hiện trên màn hình chọn slot).</summary>
    [Serializable]
    public class SaveSlotHeader
    {
        public int version = 1;
        public string savedAt;      // ISO 8601
        public string eventName;    // "Đơn nam"…
        public string athleteName;  // VĐV người chơi chọn
        public int seasonNo = 1;
        public int week = 1;
    }

    /// <summary>
    /// Chỉ mục các slot lưu mùa giải. Lưu bằng PlayerPrefs (trên WebGL PlayerPrefs nằm trong IndexedDB của
    /// trình duyệt). GĐ1 chỉ lưu phần header; dữ liệu mùa giải đầy đủ (lịch, BXH, giải đang đánh dở…)
    /// được thêm ở GĐ6 dưới key riêng của từng slot.
    /// </summary>
    public static class TournamentSaveIndex
    {
        /// <summary>Số slot lưu mùa giải.</summary>
        public const int SlotCount = 3;

        static string HeaderKey(int slot) => $"bm.tour.slot{slot}.header";
        public static string DataKey(int slot) => $"bm.tour.slot{slot}.data";

        public static bool HasSlot(int slot) => PlayerPrefs.HasKey(HeaderKey(slot));

        public static bool HasAnySave()
        {
            for (int i = 0; i < SlotCount; i++) if (HasSlot(i)) return true;
            return false;
        }

        public static SaveSlotHeader GetHeader(int slot)
        {
            if (!HasSlot(slot)) return null;
            try { return JsonUtility.FromJson<SaveSlotHeader>(PlayerPrefs.GetString(HeaderKey(slot))); }
            catch (Exception e) { Debug.LogWarning($"[TournamentSave] Header slot {slot} hỏng: {e.Message}"); return null; }
        }

        public static void WriteHeader(int slot, SaveSlotHeader h)
        {
            try
            {
                h.savedAt = DateTime.Now.ToString("s");
                PlayerPrefs.SetString(HeaderKey(slot), JsonUtility.ToJson(h));
                PlayerPrefs.Save();
            }
            catch (Exception e) { Debug.LogWarning($"[TournamentSave] Không lưu được slot {slot}: {e.Message}"); }
        }

        public static void Delete(int slot)
        {
            PlayerPrefs.DeleteKey(HeaderKey(slot));
            PlayerPrefs.DeleteKey(DataKey(slot));
            PlayerPrefs.Save();
        }
    }
}
