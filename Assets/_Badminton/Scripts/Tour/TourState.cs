using System;
using System.Collections.Generic;

namespace Badminton.Tour
{
    public enum TourFormat : byte { Knockout, Groups }
    public enum TourStage : byte { Qualifying, Main, Group, Playoff }

    /// <summary>Một vòng đấu (hoặc một lượt đấu vòng bảng).</summary>
    [Serializable]
    public class RoundInfo
    {
        public string name;
        public TourStage stage;
        public int size;    // số VĐV/cặp của vòng (vòng loại trực tiếp); lượt vòng bảng = 0
    }

    /// <summary>
    /// Một trận trong giải. a/b = id suất thi đấu; -1 = chưa xác định; ≤ -2 = suất vòng loại Q(k) (k = -2 - giá trị).
    /// Hiển thị: chưa started ⇒ "Chưa đấu"; started mà chưa revealed ⇒ "Đang thi đấu…"; revealed ⇒ tỷ số.
    /// Kết quả trận AI được sinh sẵn khi vòng bắt đầu (hasResult) nhưng chỉ công bố (revealed) sau trận người chơi.
    /// </summary>
    [Serializable]
    public class MatchSlot
    {
        public int id;
        public int round;
        public int index;
        public int a = -1, b = -1;
        public int group = -1;
        public bool started;
        public bool hasResult;
        public bool revealed;
        public MatchResult result = new MatchResult();

        public bool Ready => a >= 0 && b >= 0;
        public int Winner => hasResult ? (result.winner == 0 ? a : b) : -1;
        public int Loser => hasResult ? (result.winner == 0 ? b : a) : -1;
        public bool Has(int entry) => entry >= 0 && (a == entry || b == entry);
    }

    [Serializable]
    public class GroupInfo
    {
        public string name;
        public int[] members = new int[0];
    }

    /// <summary>Toàn bộ trạng thái một giải – lưu được bằng JsonUtility.</summary>
    [Serializable]
    public class TourState
    {
        public string uid;
        public string name;
        public int tier = -1;          // chỉ số cấp giải; -1 = không tính điểm (giải tùy chỉnh)
        public bool finals;            // giải chung kết cuối mùa
        public TourFormat format;
        public TourEvent ev;
        public long seed;
        public int week;               // tuần tuyệt đối (mùa giải)

        public int drawSize;
        public int seedCount;
        public int[] seeded = new int[0];     // suất hạt giống theo thứ tự 1..S
        public int[] mainLines = new int[0];
        public int[] qualLines = new int[0];
        public int qualifiers;
        public int[] entrants = new int[0];   // mọi suất tham dự (cả vòng loại)

        public List<RoundInfo> rounds = new List<RoundInfo>();
        public List<MatchSlot> slots = new List<MatchSlot>();
        public List<GroupInfo> groups = new List<GroupInfo>();

        public int playerEntry = -1;
        public int currentRound;
        public bool roundStarted;
        public bool finished;
        public int champion = -1;

        public RoundInfo Round => rounds[Math.Min(currentRound, rounds.Count - 1)];

        public int SeedOf(int entry)
        {
            for (int i = 0; i < seeded.Length; i++) if (seeded[i] == entry) return i + 1;
            return 0;
        }

        public bool IsQualifierEntrant(int entry)
        {
            foreach (var s in slots) if (s.Has(entry) && rounds[s.round].stage == TourStage.Qualifying) return true;
            return false;
        }

        public IEnumerable<MatchSlot> SlotsIn(int round)
        {
            foreach (var s in slots) if (s.round == round) yield return s;
        }
    }
}
