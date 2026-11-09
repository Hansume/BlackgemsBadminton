using Badminton.AI;
using Badminton.Tour;
using Badminton.TournamentMode;
using UnityEditor;
using UnityEngine;

namespace Badminton.EditorTools
{
    /// <summary>Tạo dữ liệu chế độ Giải đấu: database VĐV hư cấu + TournamentSettings (Resources) + AI Rất khó.</summary>
    public static class TournamentDataBuilder
    {
        const string ResDir = "Assets/_Badminton/Resources/Tournament";
        const string DbPath = ResDir + "/AthleteDatabase.asset";
        const string SettingsPath = ResDir + "/TournamentSettings.asset";

        [MenuItem("Badminton/Giải đấu/Tạo dữ liệu giải đấu (database VĐV + cấu hình)", priority = 60)]
        public static void Build() => Build(false);

        [MenuItem("Badminton/Giải đấu/Sinh lại database VĐV (theo seed trong cấu hình)", priority = 61)]
        public static void RegenerateDatabase() => Build(true);

        public static void Build(bool regenerateDb)
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Badminton/Resources")) AssetDatabase.CreateFolder("Assets/_Badminton", "Resources");
            if (!AssetDatabase.IsValidFolder(ResDir)) AssetDatabase.CreateFolder("Assets/_Badminton/Resources", "Tournament");

            var settings = AssetDatabase.LoadAssetAtPath<TournamentSettings>(SettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<TournamentSettings>();
                AssetDatabase.CreateAsset(settings, SettingsPath);
            }

            var db = AssetDatabase.LoadAssetAtPath<AthleteDatabaseAsset>(DbPath);
            if (db == null)
            {
                db = ScriptableObject.CreateInstance<AthleteDatabaseAsset>();
                AssetDatabase.CreateAsset(db, DbPath);
                regenerateDb = true;
            }
            if (regenerateDb)
            {
                db.data = DatabaseGenerator.Generate(settings.balance.database);
                EditorUtility.SetDirty(db);
            }

            var diffs = GameSceneBuilder.CreateDifficulties(); // tạo AI_VeryHard nếu chưa có
            settings.database = db;
            settings.aiTiers = new AIDifficulty[] { diffs[0], diffs[1], diffs[2], diffs.Length > 3 ? diffs[3] : diffs[2] };
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            Debug.Log($"[TournamentData] {db.data.athletes.Count} VĐV, {db.data.entries.Count} suất thi đấu → {DbPath}");
        }
    }
}
