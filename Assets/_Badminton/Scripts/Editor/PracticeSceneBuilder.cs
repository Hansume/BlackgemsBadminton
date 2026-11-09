using System.IO;
using Badminton.Cameras;
using Badminton.Core;
using Badminton.Player;
using Badminton.Practice;
using Badminton.Shots;
using Badminton.Shuttle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using B = Badminton.EditorTools.CourtSceneBuilder;

namespace Badminton.EditorTools
{
    /// <summary>
    /// GĐ2: tạo scene Practice_Wall.unity = bản sao Court.unity + quả cầu + 1 người chơi + tường tập.
    /// Các ScriptableObject (ShuttleSettings, PlayerTuning, Shot_*) chỉ được tạo nếu chưa có ⇒
    /// thông số bạn đã chỉnh sẽ không bị ghi đè khi chạy lại menu.
    /// </summary>
    public static class PracticeSceneBuilder
    {
        internal const string PracticeScenePath = B.Root + "/Scenes/Practice_Wall.unity";
        internal const string SettingsDir = B.Root + "/Settings";
        internal const string ShotsDir = SettingsDir + "/Shots";

        [MenuItem("Badminton/3. Tạo scene GĐ2: Tập với tường (Practice_Wall.unity)", priority = 3)]
        public static void CreatePracticeScene()
        {
            if (!File.Exists(B.ScenePath))
            {
                EditorUtility.DisplayDialog("Thiếu Court.unity", "Chạy menu \"Badminton > 1. Tạo scene sân đấu\" trước.", "OK");
                return;
            }
            if (File.Exists(PracticeScenePath) &&
                !EditorUtility.DisplayDialog("Practice_Wall.unity đã tồn tại", "Tạo lại sẽ ghi đè scene này. Tiếp tục?", "Ghi đè", "Huỷ"))
                return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            BuildScene();
        }

        /// <summary>Dựng Practice_Wall.unity không hỏi xác nhận (tự động hoá).</summary>
        public static void BuildScene()
        {
            B.EnsureFolders();
            if (!AssetDatabase.IsValidFolder(ShotsDir)) AssetDatabase.CreateFolder(SettingsDir, "Shots");

            AssetDatabase.DeleteAsset(PracticeScenePath);
            AssetDatabase.CopyAsset(B.ScenePath, PracticeScenePath);
            var scene = EditorSceneManager.OpenScene(PracticeScenePath, OpenSceneMode.Single);

            var placeholders = GameObject.Find("_Placeholders (xoá ở GĐ2)");
            if (placeholders != null) Object.DestroyImmediate(placeholders);

            var shuttleSettings = LoadOrCreate<ShuttleSettings>(SettingsDir + "/ShuttleSettings.asset", null);
            var tuning = LoadOrCreate<PlayerTuning>(SettingsDir + "/PlayerTuning.asset", null);
            var shots = CreateShotLibrary();

            var shuttle = BuildShuttle(shuttleSettings);
            var (motor, hitter) = BuildPlayer(tuning, shots, shuttle);
            var wall = BuildWall(4.5f);
            var marker = BuildLandingMarker(shuttle);

            var mode = new GameObject("Practice Wall Mode").AddComponent<PracticeWallMode>();
            mode.shuttle = shuttle;
            mode.player = motor;
            mode.hitter = hitter;
            mode.landingMarker = marker;
            mode.timingRing = BuildTimingRing(shuttle);
            mode.cameraRig = Object.FindFirstObjectByType<CameraRig>();
            mode.wallZ = wall;

            EditorSceneManager.SaveScene(scene);

            // Scene tập là scene đầu tiên trong build (để test GĐ2 trên web), Court.unity giữ ở sau.
            var list = new System.Collections.Generic.List<EditorBuildSettingsScene>
            {
                new(PracticeScenePath, true), new(B.ScenePath, true),
            };
            foreach (var s in EditorBuildSettings.scenes)
                if (s.path != PracticeScenePath && s.path != B.ScenePath) list.Add(new EditorBuildSettingsScene(s.path, false));
            EditorBuildSettings.scenes = list.ToArray();

            Debug.Log("[PracticeSceneBuilder] Đã tạo " + PracticeScenePath + " – bấm Play để tập.");
        }

        // ───────────────────────────── ASSETS ─────────────────────────────

        internal static T LoadOrCreate<T>(string path, System.Action<T> init) where T : ScriptableObject
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a != null) return a;
            a = ScriptableObject.CreateInstance<T>();
            init?.Invoke(a);
            AssetDatabase.CreateAsset(a, path);
            return a;
        }

        internal static ShotLibrary CreateShotLibrary()
        {
            // Mỗi cú nhắm một điểm đích (độ sâu tính từ lưới, trên sân đối phương). Vận tốc được giải ngược
            // bằng mô phỏng ⇒ cú đánh hợp lý từ mọi vị trí. Smash dùng tốc độ cố định 65 m/s bắn thẳng.
            // Asset cũ của GĐ2 (targetDepth = 0) được tự động chuyển sang thông số mới.
            ShotDefinition Def(string name, ShotType t, ShotAimMode mode, float depth, float aimDepth, float aimWidth,
                               float elevation, float speed, float scatter, float badScatter, float powerLoss,
                               float minH = 0f, float maxH = 0f)
            {
                string path = $"{ShotsDir}/Shot_{name}.asset";
                var d = AssetDatabase.LoadAssetAtPath<ShotDefinition>(path);
                bool fresh = d == null;
                if (fresh) d = ScriptableObject.CreateInstance<ShotDefinition>();
                if (fresh || d.parameters.targetDepth <= 0f)
                {
                    d.type = t;
                    d.parameters = new ShotParams
                    {
                        mode = mode, targetDepth = depth, aimDepth = aimDepth, aimWidth = aimWidth,
                        elevation = elevation, speed = speed,
                        scatter = scatter, badScatter = badScatter, badPowerLoss = powerLoss,
                    };
                    d.minContactHeight = minH;
                    d.maxContactHeight = maxH;
                    if (fresh) AssetDatabase.CreateAsset(d, path); else EditorUtility.SetDirty(d);
                }
                return d;
            }

            var lib = LoadOrCreate<ShotLibrary>(SettingsDir + "/ShotLibrary.asset", null);
            // Tham số: mode, độ sâu, ±sâu (W/S), ngang (A/D), góc nâng, tốc độ, lệch, lệch khi timing xấu, hụt lực
            lib.clear      = Def("Clear",      ShotType.Clear,      ShotAimMode.Target, 5.9f, 0.6f, 2.0f, 45f, 75f, 0.30f, 1.6f, 0.30f, minH: 1.6f); // thấp hơn ⇒ Lift
            lib.smash      = Def("Smash",      ShotType.Smash,      ShotAimMode.Direct, 4.6f, 1.2f, 2.1f, 0f,  65f, 0.30f, 1.4f, 0.25f, minH: 1.9f);
            lib.drop       = Def("Drop",       ShotType.Drop,       ShotAimMode.Target, 2.5f, 0.6f, 2.0f, 10f, 40f, 0.25f, 1.2f, 0.25f, minH: 1.6f); // thấp hơn ⇒ Net shot
            lib.netShot    = Def("NetShot",    ShotType.NetShot,    ShotAimMode.Target, 0.8f, 0.4f, 2.0f, 40f, 15f, 0.15f, 0.7f, 0.20f, maxH: 1.8f);
            lib.lift       = Def("Lift",       ShotType.Lift,       ShotAimMode.Target, 5.7f, 0.6f, 2.0f, 60f, 60f, 0.30f, 1.5f, 0.30f);
            lib.serve      = Def("Serve",      ShotType.Serve,      ShotAimMode.Target, 6.0f, 0.4f, 0.0f, 55f, 50f, 0.20f, 0.20f, 0.00f);
            lib.shortServe = Def("ShortServe", ShotType.ShortServe, ShotAimMode.Target, 2.4f, 0.2f, 0.0f, 20f, 20f, 0.12f, 0.12f, 0.00f);
            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();
            return lib;
        }

        // ──────────────────────────── QUẢ CẦU ────────────────────────────

        internal static Shuttlecock BuildShuttle(ShuttleSettings settings)
        {
            var go = new GameObject("Shuttlecock");
            go.transform.position = new Vector3(0f, 2.5f, 4f);
            var sc = go.AddComponent<Shuttlecock>();
            sc.settings = settings;

            // Model: +Z = đầu cầu (đế bần). Kích thước thật: đế Ø2.6 cm, váy lông dài ~6.5 cm, miệng Ø6.5 cm.
            var visual = new GameObject("Visual").transform;
            visual.SetParent(go.transform, false);
            sc.visual = visual;
            B.Sphere("Cork", visual, Vector3.zero, Vector3.one * 0.026f, B.Mat("Shuttle_Cork", new Color(0.95f, 0.93f, 0.88f)), false);

            var skirt = new GameObject("Skirt");
            skirt.transform.SetParent(visual, false);
            skirt.AddComponent<MeshFilter>().sharedMesh = B.SaveMesh(BuildSkirtMesh(), "ShuttleSkirt");
            B.Setup(skirt.AddComponent<MeshRenderer>(), B.UnlitDoubleSided("Shuttle_Feather", Color.white));

            var trail = go.AddComponent<TrailRenderer>();
            trail.time = 0.18f;
            trail.minVertexDistance = 0.05f;
            trail.widthMultiplier = 0.06f;
            trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.emitting = false;
            // Particles/Unlit nhân vertex color ⇒ startColor/endColor của trail có tác dụng (URP Unlit thì không).
            trail.sharedMaterial = TransparentUnlit("Trail", Color.white, null, "Universal Render Pipeline/Particles/Unlit");
            sc.trail = trail;

            BuildBlobShadow("Shuttle Shadow", go.transform, 0.3f, 0.35f);
            return sc;
        }

        /// <summary>Váy lông cầu: hình nón cụt hở 2 đầu, từ đế (z=0) loe ra phía sau (−Z).</summary>
        internal static Mesh BuildSkirtMesh()
        {
            const int seg = 12;
            const float r0 = 0.012f, r1 = 0.033f, len = 0.068f;
            var v = new Vector3[(seg + 1) * 2];
            var tris = new int[seg * 6];
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                float c = Mathf.Cos(a), s = Mathf.Sin(a);
                v[i * 2] = new Vector3(c * r0, s * r0, -0.004f);
                v[i * 2 + 1] = new Vector3(c * r1, s * r1, -len);
                if (i == seg) continue;
                int b = i * 2, t = i * 6;
                tris[t] = b; tris[t + 1] = b + 1; tris[t + 2] = b + 2;
                tris[t + 3] = b + 1; tris[t + 4] = b + 3; tris[t + 5] = b + 2;
            }
            var m = new Mesh { name = "ShuttleSkirt", vertices = v, triangles = tris };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        // ──────────────────────────── NGƯỜI CHƠI ────────────────────────────

        internal static (PlayerMotor, PlayerHitter) BuildPlayer(PlayerTuning tuning, ShotLibrary shots, Shuttlecock shuttle)
            => BuildPlayer("Player (Blue)", 0, CourtSide.Near, new Vector3(0.5f, 0f, -4f),
                           B.Mat("Player_Blue", new Color(0.15f, 0.40f, 0.95f)), tuning, shots, shuttle, keyboard: true);

        internal static (PlayerMotor, PlayerHitter) BuildPlayer(string name, int id, CourtSide side, Vector3 pos, Material shirt,
                                                                PlayerTuning tuning, ShotLibrary shots, Shuttlecock shuttle, bool keyboard)
        {
            var go = new GameObject(name);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, side == CourtSide.Near ? 0f : 180f, 0f));

            if (keyboard) go.AddComponent<KeyboardPlayerInput>();
            var motor = go.AddComponent<PlayerMotor>();
            motor.tuning = tuning;
            motor.side = side;

            var shorts = B.Mat("Player_Shorts", new Color(0.12f, 0.12f, 0.16f));
            var skin = B.Mat("Skin", new Color(0.85f, 0.66f, 0.52f));
            var shoe = B.Mat("Player_Shoes", new Color(0.95f, 0.95f, 0.95f));
            var racketMat = B.Mat("Racket_Frame", new Color(0.85f, 0.15f, 0.15f));
            var grip = B.Mat("Racket_Grip", new Color(0.1f, 0.1f, 0.1f));

            // ── Rig low-poly: mỗi khớp là 1 pivot, chi mặc định chỉ thẳng xuống (−Y) ──
            var t = go.transform;
            var hips = Pivot("Hips", t, new Vector3(0f, 0.85f, 0f));

            Transform Leg(string n, float x)
            {
                var leg = Pivot(n, hips, new Vector3(x, 0f, 0f));
                B.Capsule("Thigh+Shin", leg, new Vector3(0f, -0.42f, 0f), new Vector3(0.16f, 0.42f, 0.16f), shorts, false);
                B.Box("Shoe", leg, new Vector3(0f, -0.82f, 0.05f), new Vector3(0.13f, 0.07f, 0.26f), shoe, false);
                return leg;
            }
            var legL = Leg("Leg L", -0.12f);
            var legR = Leg("Leg R", 0.12f);

            var torso = Pivot("Torso", hips, new Vector3(0f, 0.05f, 0f));
            B.Capsule("Body", torso, new Vector3(0f, 0.3f, 0f), new Vector3(0.42f, 0.36f, 0.3f), shirt, false);
            B.Sphere("Head", torso, new Vector3(0f, 0.83f, 0f), Vector3.one * 0.24f, skin, false);

            Transform Arm(string n, float x, Material sleeve)
            {
                var sh = Pivot(n, torso, new Vector3(x, 0.55f, 0f));
                B.Capsule("Upper", sh, new Vector3(0f, -0.14f, 0f), new Vector3(0.1f, 0.14f, 0.1f), sleeve, false);
                B.Capsule("Lower", sh, new Vector3(0f, -0.4f, 0f), new Vector3(0.08f, 0.16f, 0.08f), skin, false);
                return sh;
            }
            var shR = Arm("Shoulder R", 0.27f, shirt);
            var shL = Arm("Shoulder L", -0.27f, shirt);

            // Vợt ở tay phải, nối dài theo trục tay (−Y).
            var hand = Pivot("Hand R", shR, new Vector3(0f, -0.56f, 0f));
            B.Box("Grip", hand, new Vector3(0f, -0.1f, 0f), new Vector3(0.03f, 0.2f, 0.03f), grip, false);
            B.Box("Shaft", hand, new Vector3(0f, -0.32f, 0f), new Vector3(0.012f, 0.25f, 0.012f), racketMat, false);
            var head = B.Cylinder("Racket Head", hand, new Vector3(0f, -0.57f, 0f), new Vector3(0.22f, 0.005f, 0.28f), racketMat, false);
            head.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // mặt vợt vuông góc trục tay

            // Tay trái cầm cầu khi giao.
            var anchor = Pivot("Serve Anchor", shL, new Vector3(0f, -0.6f, 0.05f));

            var hitter = go.AddComponent<PlayerHitter>();
            hitter.playerId = id;
            hitter.motor = motor;
            hitter.shuttle = shuttle;
            hitter.shots = shots;
            hitter.serveAnchor = anchor;

            var anim = go.AddComponent<PlayerAnimator>();
            anim.motor = motor;
            anim.hitter = hitter;
            anim.hips = hips; anim.legL = legL; anim.legR = legR;
            anim.torso = torso; anim.shoulderR = shR; anim.shoulderL = shL;

            BuildBlobShadow(name + " Shadow", t, 0.9f, 1f);
            return (motor, hitter);
        }

        static Transform Pivot(string name, Transform parent, Vector3 localPos)
        {
            var p = new GameObject(name).transform;
            p.SetParent(parent, false);
            p.localPosition = localPos;
            return p;
        }

        // ──────────────────────────── MÔI TRƯỜNG TEST ────────────────────────────

        internal static float BuildWall(float z)
        {
            var mat = B.GetOrCreate("Practice_Wall", "Universal Render Pipeline/Simple Lit", new Color(0.75f, 0.85f, 1f, 0.35f), out bool created);
            if (created) { B.MakeTransparent(mat); EditorUtility.SetDirty(mat); }
            // Mặt tường (phía người chơi) đúng tại z.
            B.Box("Practice Wall", null, new Vector3(0f, 4.5f, z + 0.1f), new Vector3(12f, 9f, 0.2f), mat, false);
            return z;
        }

        internal static LandingMarker BuildLandingMarker(Shuttlecock shuttle)
        {
            var go = new GameObject("Landing Marker");
            var lm = go.AddComponent<LandingMarker>();
            lm.shuttle = shuttle;
            var ringMat = TransparentUnlit("Marker_Ring", Color.white, RadialTexture("Ring.png", true));
            lm.resultRing = FlatQuad("Result", go.transform, 0.6f, ringMat);
            return lm;
        }

        /// <summary>Vòng căn thời gian đánh cầu (trận đấu): đổi màu vàng → xanh → đỏ.</summary>
        internal static TimingRing BuildTimingRing(Shuttlecock shuttle)
        {
            var go = new GameObject("Timing Ring");
            var tr = go.AddComponent<TimingRing>();
            tr.shuttle = shuttle;
            var mat = TransparentUnlit("Timing_Ring", Color.white, RadialTexture("Ring.png", true));
            tr.ring = FlatQuad("Ring", go.transform, tr.baseSize, mat);
            tr.ring.enabled = false;
            return tr;
        }

        internal static void BuildBlobShadow(string name, Transform target, float size, float minScale)
        {
            var mat = TransparentUnlit("Blob_Shadow", new Color(0f, 0f, 0f, 0.55f), RadialTexture("Blob.png", false));
            var quad = FlatQuad(name, null, size, mat);
            var bs = quad.gameObject.AddComponent<BlobShadow>();
            bs.target = target;
            bs.size = size;
            bs.minScale = minScale;
        }

        internal static Renderer FlatQuad(string name, Transform parent, float size, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(new Vector3(0f, 0.004f, 0f), Quaternion.Euler(90f, 0f, 0f));
            go.transform.localScale = new Vector3(size, size, 1f);
            var r = go.GetComponent<MeshRenderer>();
            B.Setup(r, m);
            return r;
        }

        internal static Material TransparentUnlit(string name, Color c, Texture2D tex, string shader = "Universal Render Pipeline/Unlit")
        {
            var m = B.GetOrCreate(name, shader, c, out bool created);
            if (!created) return m;
            if (tex != null) m.SetTexture("_BaseMap", tex);
            B.MakeTransparent(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Sinh texture tròn: blob (đậm ở tâm, mờ dần ra mép) hoặc ring (vành tròn).</summary>
        internal static Texture2D RadialTexture(string file, bool ring)
        {
            string path = B.TexDir + "/" + file;
            if (!File.Exists(path))
            {
                const int n = 64;
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                        float a = ring
                            ? Mathf.Clamp01(1f - Mathf.Abs(d - 0.82f) / 0.12f)
                            : Mathf.Clamp01(1f - d) * Mathf.Clamp01(1f - d) * 1.6f;
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(a)));
                    }
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path);
                var imp = (TextureImporter)AssetImporter.GetAtPath(path);
                imp.alphaIsTransparency = true;
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.textureCompression = TextureImporterCompression.Uncompressed;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
