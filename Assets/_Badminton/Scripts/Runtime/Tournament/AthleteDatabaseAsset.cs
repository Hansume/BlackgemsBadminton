using Badminton.Tour;
using UnityEngine;

namespace Badminton.TournamentMode
{
    /// <summary>Database VĐV hư cấu (sinh bằng menu "Badminton/Giải đấu/Tạo dữ liệu giải đấu", có thể sửa tay).</summary>
    [CreateAssetMenu(menuName = "Badminton/Giải đấu/Athlete Database", fileName = "AthleteDatabase")]
    public class AthleteDatabaseAsset : ScriptableObject
    {
        [Tooltip("Danh sách VĐV và suất thi đấu. id phải trùng chỉ số trong danh sách.")]
        public AthleteDatabase data = new AthleteDatabase();
    }
}
