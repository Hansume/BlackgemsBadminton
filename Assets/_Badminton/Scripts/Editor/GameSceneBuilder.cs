using System.Collections.Generic;
using System.IO;
using Badminton.AI;
using Badminton.Audio;
using Badminton.Cameras;
using Badminton.Core;
using Badminton.Courts;
using Badminton.Player;
using Badminton.Replay;
using Badminton.Scoring;
using Badminton.Shuttle;
using Badminton.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using B = Badminton.EditorTools.CourtSceneBuilder;
using P = Badminton.EditorTools.PracticeSceneBuilder;

namespace Badminton.EditorTools
{
    /// <summary>
    /// Dựng scene chính Game.unity: sân + 2 nhân vật có rig/animation + AI + MatchController
    /// + âm thanh + replay + toàn bộ UI (menu, cài đặt, HUD kiểu truyền hình).
    /// Mọi asset cấu hình (ScriptableObject) chỉ được tạo nếu chưa có ⇒ thông số bạn đã chỉnh được giữ nguyên.
    /// </summary>
    public static class GameSceneBuilder
    {
        public const string GameScenePath = B.Root + "/Scenes/Game.unity";
        const string AiDir = P.SettingsDir + "/AI";
        const string FontDir = B.Root + "/Art/Fonts";

        /// <summary>Ký tự tiếng Việt + ASCII nạp sẵn vào atlas font (tránh giật khi chữ xuất hiện lần đầu).</summary>
        static string Charset()
        {
            var sb = new System.Text.StringBuilder();
            for (char c = (char)32; c < 127; c++) sb.Append(c);
            sb.Append("ÀÁÂÃÈÉÊÌÍÒÓÔÕÙÚÝàáâãèéêìíòóôõùúýĂăĐđĨĩŨũƠơƯư");
            for (char c = 'Ạ'; c <= 'ỹ'; c++) sb.Append(c);
            sb.Append("•—–·…×"); // chỉ các ký hiệu font Barlow có
            return sb.ToString();
        }

        [MenuItem("Badminton/5. Tạo scene chính (Game.unity – menu + trận đấu)", priority = 5)]
        public static void CreateGameScene()
        {
            if (!File.Exists(B.ScenePath))
            {
                EditorUtility.DisplayDialog("Thiếu Court.unity", "Chạy menu \"Badminton > 1. Tạo scene sân đấu\" trước.", "OK");
                return;
            }
            if (File.Exists(GameScenePath) &&
                !EditorUtility.DisplayDialog("Game.unity đã tồn tại", "Tạo lại sẽ ghi đè scene này. Tiếp tục?", "Ghi đè", "Huỷ"))
                return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            BuildScene();
        }

        /// <summary>Dựng scene không hỏi xác nhận (dùng cho tự động hoá).</summary>
        public static void BuildScene()
        {
            B.EnsureFolders();
            if (!AssetDatabase.IsValidFolder(P.ShotsDir)) AssetDatabase.CreateFolder(P.SettingsDir, "Shots");
            if (!AssetDatabase.IsValidFolder(AiDir)) AssetDatabase.CreateFolder(P.SettingsDir, "AI");

            // ── Asset cấu hình ──
            var shuttleSettings = P.LoadOrCreate<ShuttleSettings>(P.SettingsDir + "/ShuttleSettings.asset", null);
            if (Mathf.Approximately(shuttleSettings.visualScale, 2.5f)) { shuttleSettings.visualScale = 3.2f; EditorUtility.SetDirty(shuttleSettings); }
            var tuning = P.LoadOrCreate<PlayerTuning>(P.SettingsDir + "/PlayerTuning.asset", null);
            var shots = P.CreateShotLibrary();
            var rules = P.LoadOrCreate<ScoringRules>(P.SettingsDir + "/ScoringRules.asset", null);
            var diffs = CreateDifficulties();
            var audio = AudioSynthBuilder.Build();
            var theme = CreateTheme();

            // ── Scene: bản sao Court.unity ──
            AssetDatabase.DeleteAsset(GameScenePath);
            AssetDatabase.DeleteAsset(B.Root + "/Scenes/Match_AI.unity"); // scene thử của GĐ3, đã được thay thế
            AssetDatabase.CopyAsset(B.ScenePath, GameScenePath);
            var scene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
            var placeholders = GameObject.Find("_Placeholders (xoá ở GĐ2)");
            if (placeholders != null) Object.DestroyImmediate(placeholders);

            var shuttle = P.BuildShuttle(shuttleSettings);
            var blueColor = new Color(0.15f, 0.40f, 0.95f);
            var yellowColor = new Color(1f, 0.82f, 0.15f);
            var (blue, blueHit) = P.BuildPlayer("Player Blue", 0, CourtSide.Near, new Vector3(0.9f, 0f, -3f),
                B.Mat("Player_Blue", blueColor), tuning, shots, shuttle, keyboard: true);
            var (yellow, yellowHit) = P.BuildPlayer("Player Yellow", 1, CourtSide.Far, new Vector3(-0.9f, 0f, 3.6f),
                B.Mat("Player_Yellow", yellowColor), tuning, shots, shuttle, keyboard: true);

            AIController AddAi(PlayerMotor m, Shots.PlayerHitter h, PlayerMotor opp)
            {
                var ai = m.gameObject.AddComponent<AIController>();
                ai.motor = m; ai.hitter = h; ai.shuttle = shuttle; ai.opponent = opp; ai.difficulty = diffs[1];
                return ai;
            }
            var blueAi = AddAi(blue, blueHit, yellow);
            var yellowAi = AddAi(yellow, yellowHit, blue);
            blue.inputSource = blue.GetComponent<KeyboardPlayerInput>();
            yellow.inputSource = yellowAi;

            // Đồng đội cho đánh đôi (chế độ Giải đấu) – mặc định ẩn, MatchController.SetDoubles bật lên.
            var (blue2, blue2Hit) = P.BuildPlayer("Player Blue 2", 0, CourtSide.Near, new Vector3(-0.9f, 0f, -4.6f),
                B.Mat("Player_Blue", blueColor), tuning, shots, shuttle, keyboard: false);
            var (yellow2, yellow2Hit) = P.BuildPlayer("Player Yellow 2", 1, CourtSide.Far, new Vector3(0.9f, 0f, 4.6f),
                B.Mat("Player_Yellow", yellowColor), tuning, shots, shuttle, keyboard: false);
            var blue2Ai = AddAi(blue2, blue2Hit, yellow);
            var yellow2Ai = AddAi(yellow2, yellow2Hit, blue);
            blue2.inputSource = blue2Ai;
            yellow2.inputSource = yellow2Ai;

            var marker = P.BuildLandingMarker(shuttle);

            // ── Hệ thống trận đấu ──
            var systems = new GameObject("Game Systems");
            var mc = systems.AddComponent<MatchController>();
            mc.rules = rules;
            mc.shuttle = shuttle;
            mc.cameraRig = Object.FindFirstObjectByType<CameraRig>();
            mc.courtScoreboard = Object.FindFirstObjectByType<CourtScoreboard>();
            mc.landingMarker = marker;
            mc.timingRing = P.BuildTimingRing(shuttle);
            mc.difficulties = diffs;
            mc.localPlayer = 0;
            mc.players = new[]
            {
                new MatchController.Competitor { name = "Bạn", color = blueColor, motor = blue, hitter = blueHit,
                    animator = blue.GetComponent<PlayerAnimator>(), keyboard = blue.GetComponent<KeyboardPlayerInput>(), ai = blueAi },
                new MatchController.Competitor { name = "AI", color = yellowColor, motor = yellow, hitter = yellowHit,
                    animator = yellow.GetComponent<PlayerAnimator>(), keyboard = yellow.GetComponent<KeyboardPlayerInput>(), ai = yellowAi },
                new MatchController.Competitor { name = "Đồng đội", color = blueColor, motor = blue2, hitter = blue2Hit,
                    animator = blue2.GetComponent<PlayerAnimator>(), ai = blue2Ai },
                new MatchController.Competitor { name = "AI 2", color = yellowColor, motor = yellow2, hitter = yellow2Hit,
                    animator = yellow2.GetComponent<PlayerAnimator>(), ai = yellow2Ai },
            };
            blue2.gameObject.SetActive(false);
            yellow2.gameObject.SetActive(false);

            var am = systems.AddComponent<AudioManager>();
            am.library = audio;
            am.shuttle = shuttle;
            am.players = new[] { blue, yellow, blue2, yellow2 };

            var replay = systems.AddComponent<ReplaySystem>();
            replay.roots = new[] { blue.transform, yellow.transform, shuttle.transform };
            replay.shuttleTrail = shuttle.trail;
            mc.replay = replay;

            var uiGo = new GameObject("Game UI");
            var ui = uiGo.AddComponent<GameUI>();
            ui.theme = theme;
            ui.match = mc;

            EditorSceneManager.SaveScene(scene);

            // Build: Game.unity đầu tiên (menu), Practice_Wall thứ 2 (menu "Tập với tường").
            var list = new List<EditorBuildSettingsScene> { new(GameScenePath, true) };
            if (File.Exists(P.PracticeScenePath)) list.Add(new EditorBuildSettingsScene(P.PracticeScenePath, true));
            foreach (var s in EditorBuildSettings.scenes)
                if (s.path != GameScenePath && s.path != P.PracticeScenePath && File.Exists(s.path))
                    list.Add(new EditorBuildSettingsScene(s.path, false));
            EditorBuildSettings.scenes = list.ToArray();

            Debug.Log("[GameSceneBuilder] Đã tạo " + GameScenePath);
        }

        internal static AIDifficulty[] CreateDifficulties()
        {
            AIDifficulty D(string file, string label, float reaction, float speed, float posErr, float timing,
                           float smash, float place, float judge, float blunder) =>
                P.LoadOrCreate<AIDifficulty>($"{AiDir}/{file}.asset", d =>
                {
                    d.displayName = label; d.reactionTime = reaction; d.speedFactor = speed; d.positionError = posErr;
                    d.timingError = timing; d.smashChance = smash; d.placement = place; d.lineJudgement = judge; d.blunderChance = blunder;
                });

            return new[]
            {
                //    file        nhãn       phản xạ tốc độ  lệch-vị-trí timing  smash  góc   phán-đoán lỗi-ngớ-ngẩn
                D("AI_Easy",   "Dễ",     0.40f, 0.75f, 0.45f, 0.12f, 0.15f, 0.30f, 0.30f, 0.12f),
                D("AI_Normal", "Thường", 0.25f, 0.90f, 0.25f, 0.06f, 0.40f, 0.60f, 0.60f, 0.05f),
                D("AI_Hard",   "Khó",    0.14f, 1.00f, 0.10f, 0.025f, 0.65f, 0.85f, 0.90f, 0.01f),
                // Rất khó: phản ứng nhanh hơn, sai số nhỏ hơn bậc Khó (VĐV rating ≥ 85 ở chế độ Giải đấu).
                D("AI_VeryHard", "Rất khó", 0.09f, 1.00f, 0.05f, 0.012f, 0.75f, 0.95f, 0.97f, 0.003f),
            };
        }

        // ───────────────────────────── FONT & THEME ─────────────────────────────

        static UITheme CreateTheme()
        {
            var theme = P.LoadOrCreate<UITheme>(P.SettingsDir + "/UITheme.asset", null);
            if (theme.titleFont == null) theme.titleFont = FontAsset("BarlowCondensed-Bold");
            if (theme.headingFont == null) theme.headingFont = FontAsset("BarlowCondensed-SemiBold");
            if (theme.bodyFont == null) theme.bodyFont = FontAsset("Barlow-Medium");
            EditorUtility.SetDirty(theme);
            AssetDatabase.SaveAssets();
            return theme;
        }

        /// <summary>
        /// Tạo TMP font asset DYNAMIC từ file TTF (Barlow – giấy phép OFL, hỗ trợ tiếng Việt).
        /// Dynamic: ký tự chưa có trong atlas sẽ được thêm lúc chạy (file TTF được đóng gói theo build).
        /// </summary>
        static TMP_FontAsset FontAsset(string ttfName)
        {
            string path = $"{FontDir}/{ttfName} SDF.asset";
            var fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (fa != null) return fa;

            var font = AssetDatabase.LoadAssetAtPath<Font>($"{FontDir}/{ttfName}.ttf");
            if (font == null) { Debug.LogError("[GameSceneBuilder] Không tìm thấy font " + ttfName); return null; }

            fa = TMP_FontAsset.CreateFontAsset(font, 72, 8, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
            fa.name = ttfName + " SDF";
            AssetDatabase.CreateAsset(fa, path);
            fa.material.name = ttfName + " SDF Material";
            AssetDatabase.AddObjectToAsset(fa.material, fa);
            for (int i = 0; i < fa.atlasTextures.Length; i++)
            {
                fa.atlasTextures[i].name = ttfName + " SDF Atlas " + i;
                AssetDatabase.AddObjectToAsset(fa.atlasTextures[i], fa);
            }
            BakeGlyphs(fa);
            return fa;
        }

        [MenuItem("Badminton/Sửa font tiếng Việt cho WebGL (bake glyph)", priority = 50)]
        public static void BakeAllFonts()
        {
            foreach (var name in new[] { "BarlowCondensed-Bold", "BarlowCondensed-SemiBold", "Barlow-Medium" })
            {
                var fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>($"{FontDir}/{name} SDF.asset");
                if (fa == null) { Debug.LogError("[GameSceneBuilder] Không tìm thấy " + name + " SDF.asset"); continue; }
                BakeGlyphs(fa);
            }
            Debug.Log("[GameSceneBuilder] Đã bake glyph tiếng Việt cho 3 font.");
        }

        /// <summary>
        /// Bake sẵn toàn bộ ký tự tiếng Việt vào atlas rồi khoá ở chế độ STATIC.
        /// Lý do: font Dynamic + "Clear Dynamic Data On Build" bị xoá sạch glyph khi build,
        /// bản WebGL phải tự rasterize chữ lúc chạy (FreeType) và hay lỗi/thiếu dấu.
        /// </summary>
        static void BakeGlyphs(TMP_FontAsset fa)
        {
            fa.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            fa.ClearFontAssetData(false);
            fa.TryAddCharacters(Charset(), out string missing);
            if (!string.IsNullOrEmpty(missing)) Debug.LogWarning($"[GameSceneBuilder] Font {fa.name} thiếu ký tự: {missing}");

            // Atlas phụ (multi-atlas) sinh thêm phải được lưu vào file asset, nếu không build sẽ mất.
            for (int i = 0; i < fa.atlasTextures.Length; i++)
            {
                var tex = fa.atlasTextures[i];
                if (tex == null) continue;
                tex.name = fa.name + " Atlas " + i;
                if (!AssetDatabase.Contains(tex)) AssetDatabase.AddObjectToAsset(tex, fa);
            }

            fa.atlasPopulationMode = AtlasPopulationMode.Static;
            EditorUtility.SetDirty(fa);
            var so = new SerializedObject(fa); // clearDynamicDataOnBuild là internal
            so.FindProperty("m_ClearDynamicDataOnBuild").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
        }
    }
}
