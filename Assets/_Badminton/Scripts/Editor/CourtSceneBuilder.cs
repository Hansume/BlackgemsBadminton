using System.Collections.Generic;
using System.IO;
using System.Linq;
using Badminton.Cameras;
using Badminton.Core;
using Badminton.Courts;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using C = Badminton.Core.CourtDimensions;

namespace Badminton.EditorTools
{
    /// <summary>
    /// Dựng sân cầu lông + nhà thi đấu low-poly bằng code (menu "Badminton").
    /// Toàn bộ vật thể nằm dưới GameObject gốc "BadmintonCourt" – mỗi lần dựng lại sẽ xoá và tạo mới,
    /// nên ĐỪNG chỉnh tay bên trong nó (muốn đổi màu thì sửa Material trong Art/Materials,
    /// màu đã chỉnh sẽ được giữ nguyên khi dựng lại).
    ///
    /// Tối ưu WebGL: không collider, không đổ bóng real-time, đánh dấu Batching Static,
    /// vật liệu flat color dùng chung (SRP Batcher + GPU Instancing).
    /// </summary>
    public static class CourtSceneBuilder
    {
        internal const string Root = "Assets/_Badminton";
        internal const string ScenePath = Root + "/Scenes/Court.unity";
        internal const string MatDir = Root + "/Art/Materials";
        internal const string TexDir = Root + "/Art/Textures";
        internal const string MeshDir = Root + "/Art/Meshes";
        internal const string RigSettingsPath = Root + "/Settings/CameraRigSettings.asset";
        internal const string CourtRootName = "BadmintonCourt";

        // Tên thương hiệu GIẢ cho bảng LED.
        static readonly (string name, Color color)[] FakeBrands =
        {
            ("VOLTARA", new Color(0.95f, 0.35f, 0.10f)),
            ("SKYNEST", new Color(0.10f, 0.55f, 0.95f)),
            ("KAIROS AIR", new Color(0.85f, 0.10f, 0.30f)),
            ("LUMEN SPORTS", new Color(0.10f, 0.75f, 0.45f)),
            ("ORBITA", new Color(0.55f, 0.25f, 0.90f)),
            ("NOVAFIT", new Color(0.95f, 0.75f, 0.10f)),
        };

        // ───────────────────────────── MENU ─────────────────────────────

        [MenuItem("Badminton/1. Tạo scene sân đấu (Court.unity)", priority = 1)]
        public static void CreateCourtScene()
        {
            if (File.Exists(ScenePath) &&
                !EditorUtility.DisplayDialog("Court.unity đã tồn tại",
                    "Tạo lại scene sẽ ghi đè Court.unity. Tiếp tục?", "Ghi đè", "Huỷ"))
                return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            BuildScene();
        }

        /// <summary>Dựng Court.unity không hỏi xác nhận (tự động hoá).</summary>
        public static void BuildScene()
        {
            EnsureFolders();
            OptimizeUrpForWebGL();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SetupLighting();
            BuildCourt();
            SetupCamera();
            BuildPlaceholders();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuildSettings();
            Debug.Log("[CourtSceneBuilder] Đã tạo " + ScenePath);
        }

        [MenuItem("Badminton/2. Dựng lại sân trong scene hiện tại", priority = 2)]
        public static void RebuildCourtInCurrentScene()
        {
            EnsureFolders();
            BuildCourt();
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log("[CourtSceneBuilder] Đã dựng lại sân. Nhớ Ctrl+S để lưu scene.");
        }

        [MenuItem("Badminton/Tối ưu URP cho WebGL", priority = 20)]
        public static void OptimizeUrpForWebGL()
        {
            // WebGL dùng quality level mặc định của nền tảng (Mobile_RPAsset trong template).
            // Sửa qua SerializedObject để không phụ thuộc API nội bộ của URP.
            var assets = new HashSet<UniversalRenderPipelineAsset>();
            for (int i = 0; i < QualitySettings.names.Length; i++)
                if (QualitySettings.GetRenderPipelineAssetAt(i) is UniversalRenderPipelineAsset a &&
                    a.name.Contains("Mobile"))
                    assets.Add(a);

            foreach (var asset in assets)
            {
                var so = new SerializedObject(asset);
                SetProp(so, "m_SupportsHDR", 0);                    // HDR tốn băng thông GPU trên web
                SetProp(so, "m_MainLightShadowsSupported", 0);      // dùng blob shadow thay cho shadow map
                SetProp(so, "m_AdditionalLightsRenderingMode", 0);  // chỉ 1 directional light
                SetProp(so, "m_RequireDepthTexture", 0);
                SetProp(so, "m_RequireOpaqueTexture", 0);
                SetProp(so, "m_UseSRPBatcher", 1);
                so.ApplyModifiedPropertiesWithoutUndo();
                Debug.Log($"[CourtSceneBuilder] Đã tối ưu {asset.name} cho WebGL.");
            }
            AssetDatabase.SaveAssets();
        }

        internal static void SetProp(SerializedObject so, string name, int value)
        {
            var p = so.FindProperty(name);
            if (p == null) return;
            if (p.propertyType == SerializedPropertyType.Boolean) p.boolValue = value != 0;
            else p.intValue = value;
        }

        // ───────────────────────── ÁNH SÁNG / CAMERA ─────────────────────────

        internal static void SetupLighting()
        {
            // Ánh sáng nhà thi đấu: 1 directional chiếu gần như thẳng đứng, không shadow,
            // cộng ambient phẳng khá sáng để mọi chỗ đều sáng đều.
            var go = new GameObject("Arena Light");
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            light.color = new Color(1f, 0.97f, 0.92f);
            light.shadows = LightShadows.None;
            go.transform.rotation = Quaternion.Euler(75f, 20f, 0f);

            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.50f, 0.52f, 0.58f);
            RenderSettings.fog = false;
            RenderSettings.sun = light;
        }

        internal static void SetupCamera()
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.04f, 0.05f, 0.08f);
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 120f;
            go.AddComponent<AudioListener>();

            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;   // post-processing đắt trên WebGL
            data.antialiasing = AntialiasingMode.None;

            var rig = go.AddComponent<CameraRig>();
            rig.settings = LoadOrCreateRigSettings();
            rig.SnapNow();
        }

        internal static CameraRigSettings LoadOrCreateRigSettings()
        {
            var s = AssetDatabase.LoadAssetAtPath<CameraRigSettings>(RigSettingsPath);
            if (s != null) return s;
            s = ScriptableObject.CreateInstance<CameraRigSettings>();
            AssetDatabase.CreateAsset(s, RigSettingsPath);
            AssetDatabase.SaveAssets();
            return s;
        }

        // ─────────────────────────────── SÂN ───────────────────────────────

        internal static void BuildCourt()
        {
            var old = GameObject.Find(CourtRootName);
            if (old != null) Object.DestroyImmediate(old);

            var root = new GameObject(CourtRootName).transform;
            BuildFloors(Group(root, "Floors"));
            BuildLines(Group(root, "Lines"));
            BuildNet(Group(root, "Net"));
            BuildAdBoards(Group(root, "LED Boards"));
            BuildOfficials(Group(root, "Officials"));
            BuildStands(Group(root, "Stands"));
            BuildScoreboard(Group(root, "Scoreboard"));
        }

        internal static void BuildFloors(Transform p)
        {
            // Các lớp sàn có độ cao lệch nhau vài mm để tránh z-fighting.
            Box("Arena Floor", p, new Vector3(0, -0.054f, 0), new Vector3(44, 0.1f, 50), Mat("Floor_Blue", new Color(0.10f, 0.30f, 0.62f)));
            Box("Court Mat", p, new Vector3(0, -0.05f, 0), new Vector3(C.DoublesWidth + 1.8f, 0.1f, C.Length + 2.4f), Mat("Court_Green", new Color(0.12f, 0.50f, 0.32f)));

            var red = Mat("Mat_Orange", new Color(0.86f, 0.30f, 0.14f));
            float matStart = C.HalfLength + 1.2f, matDepth = 1.6f;
            foreach (int s in new[] { -1, 1 })
                Box("End Mat", p, new Vector3(0, -0.052f, s * (matStart + matDepth / 2f)), new Vector3(C.DoublesWidth + 3f, 0.1f, matDepth), red);
        }

        internal static void BuildLines(Transform p)
        {
            // Mỗi vạch 4 cm, nằm hoàn toàn BÊN TRONG phần sân mà nó giới hạn (luật BWF).
            var white = Mat("Line_White", new Color(0.97f, 0.97f, 0.97f));
            const float y = 0.001f, h = 0.002f, lw = C.LineWidth;

            // Vạch ngang KHÔNG static: CourtLineVisibility làm dày lúc chạy khi vạch ở xa mỏng dưới 1.5 px.
            void Line(string n, float x, float z, float sx, float sz) =>
                Box(n, p, new Vector3(x, y, z), new Vector3(sx, h, sz), white, isStatic: sz >= sx);

            foreach (int s in new[] { -1, 1 })
            {
                Line("Baseline", 0, s * (C.HalfLength - lw / 2), C.DoublesWidth, lw);
                Line("Doubles Long Service", 0, s * (C.DoublesLongServiceFromNet - lw / 2), C.DoublesWidth, lw);
                Line("Short Service", 0, s * (C.ShortServiceLineFromNet + lw / 2), C.DoublesWidth, lw);

                float cLen = C.HalfLength - C.ShortServiceLineFromNet;
                Line("Centre Line", 0, s * (C.ShortServiceLineFromNet + cLen / 2), lw, cLen);

                Line("Doubles Sideline", s * (C.HalfDoublesWidth - lw / 2), 0, lw, C.Length);
                Line("Singles Sideline", s * (C.HalfSinglesWidth - lw / 2), 0, lw, C.Length);
            }
            p.gameObject.AddComponent<CourtLineVisibility>();
        }

        internal static void BuildNet(Transform p)
        {
            var netGo = new GameObject("Net Mesh");
            netGo.transform.SetParent(p, false);
            netGo.AddComponent<MeshFilter>().sharedMesh = SaveMesh(BuildNetStrip(C.NetDepth, true), "NetMesh");
            Setup(netGo.AddComponent<MeshRenderer>(), NetMaterial());
            MarkStatic(netGo);

            var tapeGo = new GameObject("Net Tape");
            tapeGo.transform.SetParent(p, false);
            tapeGo.AddComponent<MeshFilter>().sharedMesh = SaveMesh(BuildNetStrip(C.NetTapeHeight, false), "NetTapeMesh");
            Setup(tapeGo.AddComponent<MeshRenderer>(), UnlitDoubleSided("Net_Tape", Color.white));
            MarkStatic(tapeGo);

            var yellow = Mat("Post_Yellow", new Color(0.98f, 0.80f, 0.10f));
            foreach (int s in new[] { -1, 1 })
            {
                Cylinder("Net Post", p, new Vector3(s * C.PostX, C.NetHeightPost / 2, 0), new Vector3(0.08f, C.NetHeightPost / 2, 0.08f), yellow);
                Box("Post Base", p, new Vector3(s * (C.PostX + 0.05f), 0.04f, 0), new Vector3(0.35f, 0.08f, 0.45f), yellow);
            }
        }

        /// <summary>
        /// Dải mesh thẳng đứng chạy giữa 2 cột, mép trên võng theo CourtDimensions.NetTopHeightAt.
        /// UV tính theo mét / 4 cm để texture lưới lặp đều mắt 4 cm.
        /// </summary>
        internal static Mesh BuildNetStrip(float depth, bool gridUv)
        {
            const int seg = 24;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float x = Mathf.Lerp(-C.PostX, C.PostX, i / (float)seg);
                float top = C.NetTopHeightAt(x);
                verts.Add(new Vector3(x, top, 0));
                verts.Add(new Vector3(x, top - depth, 0));
                float u = gridUv ? x / 0.04f : i / (float)seg;
                uvs.Add(new Vector2(u, gridUv ? top / 0.04f : 1));
                uvs.Add(new Vector2(u, gridUv ? (top - depth) / 0.04f : 0));
                if (i == seg) continue;
                int a = i * 2;
                tris.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 });
            }
            var m = new Mesh { name = gridUv ? "Net" : "NetTape" };
            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        // ─────────────────────────── MÔI TRƯỜNG ───────────────────────────

        internal static void BuildAdBoards(Transform p)
        {
            var body = Mat("Board_Body", new Color(0.08f, 0.08f, 0.10f));
            const float h = 0.8f, len = 2.8f, gap = 0.1f, thick = 0.12f;
            int brand = 0;

            // Đầu sân: hàng bảng chạy theo trục X, mặt quay vào sân.
            float endZ = C.HalfLength + 2.9f;
            foreach (int s in new[] { -1, 1 })
                for (int i = -2; i <= 2; i++)
                    Board(p, new Vector3(i * (len + gap), 0, s * endZ), new Vector3(0, 0, -s), len, h, thick, body, FakeBrands[brand++ % FakeBrands.Length]);

            // Hai bên sân: chạy theo trục Z.
            float sideX = C.HalfDoublesWidth + 3.4f;
            foreach (int s in new[] { -1, 1 })
                for (int i = -2; i <= 2; i++)
                    Board(p, new Vector3(s * sideX, 0, i * (len + gap)), new Vector3(-s, 0, 0), len, h, thick, body, FakeBrands[brand++ % FakeBrands.Length]);
        }

        internal static void Board(Transform p, Vector3 basePos, Vector3 facing, float len, float h, float thick,
                          Material body, (string name, Color color) brand)
        {
            var rot = Quaternion.LookRotation(-facing); // trục +Z của bảng hướng RA NGOÀI sân
            var g = new GameObject("Board " + brand.name).transform;
            g.SetParent(p, false);
            g.SetPositionAndRotation(basePos, rot);

            Box("Body", g, new Vector3(0, h / 2, 0), new Vector3(len, h, thick), body);
            // Mặt LED: unlit để trông như đang phát sáng mà không cần light.
            Box("LED", g, new Vector3(0, h / 2, -thick / 2 - 0.004f), new Vector3(len - 0.06f, h - 0.08f, 0.005f),
                Unlit("LED_" + brand.name.Replace(" ", ""), brand.color));
            Text3D("Text", g, new Vector3(0, h / 2, -thick / 2 - 0.012f), Quaternion.identity,
                   new Vector2(len - 0.2f, h - 0.2f), brand.name, Color.white);
        }

        internal static void BuildOfficials(Transform p)
        {
            var metal = Mat("Chair_Metal", new Color(0.55f, 0.58f, 0.62f));
            var seat = Mat("Chair_Seat", new Color(0.15f, 0.18f, 0.25f));
            var shirt = Mat("Official_Shirt", new Color(0.18f, 0.22f, 0.35f));
            var skin = Mat("Skin", new Color(0.85f, 0.66f, 0.52f));

            // ── Ghế trọng tài chính: đặt ngang lưới, phía +X ──
            BuildUmpireChair(p, metal, seat, shirt, skin);

            // ── Trọng tài giao cầu: ghế thấp phía đối diện ──
            LowChairWithPerson("Service Judge", p, new Vector3(-C.PostX - 0.9f, 0, 0.3f), metal, seat, shirt, skin);

            // ── Trọng tài biên: 2 người sau mỗi biên cuối, 2 người mỗi biên dọc ──
            foreach (int s in new[] { -1, 1 })
            {
                LowChairWithPerson("Line Judge", p, new Vector3(-C.HalfSinglesWidth, 0, s * (C.HalfLength + 2.2f)), metal, seat, shirt, skin);
                LowChairWithPerson("Line Judge", p, new Vector3(+C.HalfSinglesWidth, 0, s * (C.HalfLength + 2.2f)), metal, seat, shirt, skin);
                LowChairWithPerson("Line Judge", p, new Vector3(s * (C.HalfDoublesWidth + 2.2f), 0, -s * 4.2f), metal, seat, shirt, skin);
            }
        }

        /// <summary>
        /// Ghế trọng tài chính trên cao (quay mặt vào sân, hướng -X). Đệm ngồi đặt trên chân đế đặc nối liền
        /// sàn ghế; trọng tài ngồi khít mặt đệm, đùi duỗi về phía sân, bàn chân đặt trên sàn ghế.
        /// </summary>
        internal static Transform BuildUmpireChair(Transform p, Material metal, Material seat, Material shirt, Material skin)
        {
            var chair = new GameObject("Umpire Chair").transform;
            chair.SetParent(p, false);
            chair.localPosition = new Vector3(C.PostX + 1.0f, 0, 0);
            const float platH = 1.5f, platThick = 0.05f;
            const float platTop = platH + platThick / 2;     // mặt sàn ghế
            const float baseH = 0.42f, cushionH = 0.06f;
            const float seatTop = platTop + baseH + cushionH; // mặt đệm ngồi ≈ cao 0.48 m so với sàn ghế
            const float seatX = 0.05f;

            foreach (var (lx, lz) in new[] { (-0.3f, -0.3f), (0.3f, -0.3f), (-0.3f, 0.3f), (0.3f, 0.3f) })
                Box("Leg", chair, new Vector3(lx, platH / 2, lz), new Vector3(0.05f, platH, 0.05f), metal);
            Box("Platform", chair, new Vector3(0, platH, 0), new Vector3(0.75f, platThick, 0.75f), metal);
            Box("Seat Base", chair, new Vector3(seatX, platTop + baseH / 2, 0), new Vector3(0.40f, baseH, 0.40f), metal);
            Box("Seat", chair, new Vector3(seatX, platTop + baseH + cushionH / 2, 0), new Vector3(0.45f, cushionH, 0.45f), seat);
            Box("Back", chair, new Vector3(0.28f, seatTop + 0.30f, 0), new Vector3(0.05f, 0.6f, 0.45f), seat);
            for (int i = 1; i <= 4; i++) // bậc thang
                Box("Step", chair, new Vector3(0.45f, i * platH / 5f, 0), new Vector3(0.25f, 0.04f, 0.5f), metal);
            // Bảng số "1" phía trước ghế, quay vào sân (hướng -X).
            Box("Sign", chair, new Vector3(-0.39f, platH - 0.35f, 0), new Vector3(0.03f, 0.55f, 0.55f), Mat("Sign_Blue", new Color(0.10f, 0.25f, 0.70f)));
            Text3D("Number", chair, new Vector3(-0.41f, platH - 0.35f, 0), Quaternion.LookRotation(Vector3.right),
                   new Vector2(0.45f, 0.45f), "1", Color.white);
            var umpire = Person("Umpire", chair, new Vector3(seatX, seatTop, 0), shirt, skin);
            SeatedLegs(umpire, Vector3.left, seatTop - platTop,
                       Mat("Official_Pants", new Color(0.10f, 0.11f, 0.14f)), Mat("Shoe_Black", new Color(0.05f, 0.05f, 0.06f)));
            return chair;
        }

        /// <summary>Menu: dựng lại ghế trọng tài chính trong scene đang mở (giữ nguyên phần còn lại của scene).</summary>
        [MenuItem("Badminton/Sửa ghế trọng tài trong scene đang mở")]
        public static void RebuildUmpireChairInOpenScene()
        {
            var old = GameObject.Find("Umpire Chair");
            if (old == null) { Debug.LogWarning("[Court] Scene đang mở không có 'Umpire Chair'."); return; }
            var parent = old.transform.parent;
            int sibling = old.transform.GetSiblingIndex();
            UnityEngine.Object.DestroyImmediate(old);
            var chair = BuildUmpireChair(parent,
                Mat("Chair_Metal", new Color(0.55f, 0.58f, 0.62f)), Mat("Chair_Seat", new Color(0.15f, 0.18f, 0.25f)),
                Mat("Official_Shirt", new Color(0.18f, 0.22f, 0.35f)), Mat("Skin", new Color(0.85f, 0.66f, 0.52f)));
            chair.SetSiblingIndex(sibling);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(chair.gameObject.scene);
        }

        /// <summary>
        /// Chân người ngồi: đùi nằm ngang trên mặt ghế duỗi theo hướng dir, cẳng chân thả thẳng xuống sàn
        /// (dropToFloor = khoảng cách từ mặt ghế xuống sàn), có giày. Gốc toạ độ = mặt ghế (như Person).
        /// </summary>
        internal static void SeatedLegs(Transform person, Vector3 dir, float dropToFloor, Material pants, Material shoe)
        {
            var legs = new GameObject("Legs").transform;
            legs.SetParent(person, false);
            legs.localRotation = Quaternion.FromToRotation(Vector3.left, dir); // dựng theo hướng -X rồi xoay
            // Đùi 0.36 m ⇒ mũi giày vẫn nằm trong mép sàn ghế trọng tài (sàn rộng 0.75 m).
            const float thighLen = 0.36f, thick = 0.13f, footLen = 0.20f, footH = 0.06f;
            float shinLen = dropToFloor - footH;
            float shinX = -thighLen + thick / 2;
            foreach (float z in new[] { -0.09f, 0.09f })
            {
                Box("Thigh", legs, new Vector3(-thighLen / 2, thick / 2, z), new Vector3(thighLen, thick, thick), pants);
                Box("Shin", legs, new Vector3(shinX, -shinLen / 2 + thick / 2, z), new Vector3(thick, shinLen + thick, thick), pants);
                Box("Shoe", legs, new Vector3(shinX - 0.02f, -dropToFloor + footH / 2, z), new Vector3(footLen, footH, 0.11f), shoe);
            }
        }

        internal static void LowChairWithPerson(string name, Transform p, Vector3 pos, Material metal, Material seat, Material shirt, Material skin)
        {
            var g = new GameObject(name).transform;
            g.SetParent(p, false);
            g.localPosition = pos;
            Box("Seat", g, new Vector3(0, 0.22f, 0), new Vector3(0.45f, 0.44f, 0.45f), metal);
            Box("Cushion", g, new Vector3(0, 0.46f, 0), new Vector3(0.45f, 0.04f, 0.45f), seat);
            Person("Person", g, new Vector3(0, 0.48f, 0), shirt, skin);
        }

        /// <summary>Người ngồi low-poly: thân capsule + đầu cầu. Gốc toạ độ = mặt ghế.</summary>
        internal static Transform Person(string name, Transform p, Vector3 seatPos, Material shirt, Material skin)
        {
            var g = new GameObject(name).transform;
            g.SetParent(p, false);
            g.localPosition = seatPos;
            Capsule("Body", g, new Vector3(0, 0.32f, 0), new Vector3(0.36f, 0.32f, 0.30f), shirt);
            Sphere("Head", g, new Vector3(0, 0.78f, 0), Vector3.one * 0.22f, skin);
            return g;
        }

        internal static void BuildStands(Transform p)
        {
            // Khán đài tối dạng bậc thang bao quanh 4 phía + khán giả là các khối nhỏ màu trầm.
            var tierMat = Mat("Stand_Dark", new Color(0.07f, 0.07f, 0.09f));
            var crowdMats = new[]
            {
                Mat("Crowd_1", new Color(0.35f, 0.12f, 0.12f)), Mat("Crowd_2", new Color(0.12f, 0.18f, 0.35f)),
                Mat("Crowd_3", new Color(0.30f, 0.30f, 0.30f)), Mat("Crowd_4", new Color(0.35f, 0.30f, 0.12f)),
                Mat("Crowd_5", new Color(0.15f, 0.28f, 0.18f)), Mat("Crowd_6", new Color(0.40f, 0.40f, 0.42f)),
            };
            var rng = new System.Random(12345); // seed cố định ⇒ dựng lại vẫn giống hệt

            const int rows = 6;
            const float rowDepth = 1.0f, rowRise = 0.55f, seatSpacing = 0.65f;

            void StandBlock(string name, Vector3 origin, Vector3 outward, float width)
            {
                var rot = Quaternion.LookRotation(outward); // +Z cục bộ = hướng ra xa sân
                var g = new GameObject(name).transform;
                g.SetParent(p, false);
                g.SetPositionAndRotation(origin, rot);
                for (int r = 0; r < rows; r++)
                {
                    float top = (r + 1) * rowRise;
                    Box("Tier", g, new Vector3(0, top / 2, r * rowDepth + rowDepth / 2), new Vector3(width, top, rowDepth), tierMat);
                    int seats = Mathf.FloorToInt(width / seatSpacing);
                    for (int i = 0; i < seats; i++)
                    {
                        if (rng.NextDouble() > 0.7) continue; // ~70% ghế có người
                        float x = -width / 2 + seatSpacing * (i + 0.5f);
                        float hh = 0.5f + (float)rng.NextDouble() * 0.15f;
                        Box("Fan", g, new Vector3(x, top + hh / 2, r * rowDepth + 0.5f), new Vector3(0.4f, hh, 0.3f),
                            crowdMats[rng.Next(crowdMats.Length)]);
                    }
                }
            }

            float endZ = C.HalfLength + 3.6f, sideX = C.HalfDoublesWidth + 4.2f;
            StandBlock("Stand Far", new Vector3(0, 0, endZ), Vector3.forward, 2 * sideX + 2 * rows * rowDepth);
            StandBlock("Stand Near", new Vector3(0, 0, -endZ), Vector3.back, 2 * sideX + 2 * rows * rowDepth);
            StandBlock("Stand Right", new Vector3(sideX, 0, 0), Vector3.right, 2 * endZ);
            StandBlock("Stand Left", new Vector3(-sideX, 0, 0), Vector3.left, 2 * endZ);
        }

        internal static void BuildScoreboard(Transform p)
        {
            // Bảng điểm điện tử nhỏ cạnh cột lưới phía trọng tài giao cầu, mặt quay về camera broadcast.
            var g = new GameObject("Court Scoreboard").transform;
            g.SetParent(p, false);
            g.SetPositionAndRotation(new Vector3(-C.PostX - 1.6f, 0, -0.9f), Quaternion.Euler(0, 20f, 0));

            var dark = Mat("Board_Body", new Color(0.08f, 0.08f, 0.10f));
            Box("Pole", g, new Vector3(0, 0.45f, 0.05f), new Vector3(0.06f, 0.9f, 0.06f), dark);
            Box("Panel", g, new Vector3(0, 1.1f, 0), new Vector3(1.0f, 0.5f, 0.08f), dark);
            Box("Screen", g, new Vector3(0, 1.1f, -0.042f), new Vector3(0.92f, 0.42f, 0.005f), Unlit("Scoreboard_Screen", new Color(0.02f, 0.02f, 0.03f)));

            var sb = g.gameObject.AddComponent<CourtScoreboard>();
            sb.leftScore = Text3D("Left", g, new Vector3(-0.22f, 1.1f, -0.05f), Quaternion.identity, new Vector2(0.4f, 0.36f), "0", new Color(1f, 0.85f, 0.2f));
            sb.rightScore = Text3D("Right", g, new Vector3(0.22f, 1.1f, -0.05f), Quaternion.identity, new Vector2(0.4f, 0.36f), "0", new Color(1f, 0.85f, 0.2f));

            // Mặt sau: màn hình + chữ quay 180° để camera phía Far cũng đọc được.
            Box("Screen Back", g, new Vector3(0, 1.1f, 0.042f), new Vector3(0.92f, 0.42f, 0.005f), Unlit("Scoreboard_Screen", new Color(0.02f, 0.02f, 0.03f)));
            var back = Quaternion.Euler(0, 180f, 0);
            sb.leftScoreBack = Text3D("Left Back", g, new Vector3(0.22f, 1.1f, 0.05f), back, new Vector2(0.4f, 0.36f), "0", new Color(1f, 0.85f, 0.2f));
            sb.rightScoreBack = Text3D("Right Back", g, new Vector3(-0.22f, 1.1f, 0.05f), back, new Vector2(0.4f, 0.36f), "0", new Color(1f, 0.85f, 0.2f));
        }

        internal static void BuildPlaceholders()
        {
            // Hình nhân tạm để căn camera (Giai đoạn 2 thay bằng nhân vật thật).
            var g = new GameObject("_Placeholders (xoá ở GĐ2)").transform;
            Capsule("Near Player (Blue)", g, new Vector3(0.6f, 0.9f, -3.8f), new Vector3(0.5f, 0.9f, 0.5f), Mat("Player_Blue", new Color(0.15f, 0.40f, 0.95f)), false);
            Capsule("Far Player (Yellow)", g, new Vector3(-0.5f, 0.9f, 3.8f), new Vector3(0.5f, 0.9f, 0.5f), Mat("Player_Yellow", new Color(1f, 0.82f, 0.15f)), false);
        }

        // ───────────────────────────── HELPERS ─────────────────────────────

        internal static Transform Group(Transform parent, string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }

        internal static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material m, bool isStatic)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.DestroyImmediate(go.GetComponent<Collider>()); // vật lý tự viết, không cần collider
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            Setup(go.GetComponent<MeshRenderer>(), m);
            if (isStatic) MarkStatic(go);
            return go;
        }

        internal static GameObject Box(string n, Transform p, Vector3 pos, Vector3 size, Material m, bool isStatic = true) => Prim(PrimitiveType.Cube, n, p, pos, size, m, isStatic);
        internal static GameObject Cylinder(string n, Transform p, Vector3 pos, Vector3 scale, Material m, bool isStatic = true) => Prim(PrimitiveType.Cylinder, n, p, pos, scale, m, isStatic);
        internal static GameObject Capsule(string n, Transform p, Vector3 pos, Vector3 scale, Material m, bool isStatic = true) => Prim(PrimitiveType.Capsule, n, p, pos, scale, m, isStatic);
        internal static GameObject Sphere(string n, Transform p, Vector3 pos, Vector3 scale, Material m, bool isStatic = true) => Prim(PrimitiveType.Sphere, n, p, pos, scale, m, isStatic);

        internal static void Setup(Renderer r, Material m)
        {
            r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        internal static void MarkStatic(GameObject go) =>
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);

        internal static TMP_Text Text3D(string name, Transform parent, Vector3 pos, Quaternion rot, Vector2 size, string text, Color color)
        {
            if (!TmpReady()) return null;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            var t = go.AddComponent<TextMeshPro>();
            t.text = text;
            t.color = color;
            t.fontStyle = FontStyles.Bold;
            t.alignment = TextAlignmentOptions.Center;
            t.rectTransform.sizeDelta = size;
            t.enableAutoSizing = true;
            t.fontSizeMin = 0.1f;
            t.fontSizeMax = 30f;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            return t;
        }

        internal static bool? _tmpReady;
        internal static bool TmpReady()
        {
            if (_tmpReady.HasValue) return _tmpReady.Value;
            _tmpReady = Resources.Load<TMP_Settings>("TMP Settings") != null;
            if (!_tmpReady.Value)
                Debug.LogWarning("[CourtSceneBuilder] Chưa import TMP Essential Resources ⇒ bỏ qua chữ trên bảng LED/bảng điểm. " +
                                 "Vào Window > TextMeshPro > Import TMP Essential Resources rồi chạy lại menu Badminton.");
            return _tmpReady.Value;
        }

        // ── Vật liệu: tạo 1 lần rồi tái sử dụng; đã tồn tại thì GIỮ NGUYÊN màu bạn đã chỉnh ──

        internal static Material GetOrCreate(string name, string shaderName, Color color, out bool created)
        {
            string path = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            created = m == null;
            if (created)
            {
                m = new Material(Shader.Find(shaderName));
                m.SetColor("_BaseColor", color);
                m.enableInstancing = true; // GPU instancing cho vật thể lặp lại
                AssetDatabase.CreateAsset(m, path);
            }
            return m;
        }

        internal static Material Mat(string name, Color c)
        {
            var m = GetOrCreate(name, "Universal Render Pipeline/Simple Lit", c, out bool created);
            if (created) { m.SetFloat("_Smoothness", 0.15f); EditorUtility.SetDirty(m); }
            return m;
        }

        internal static Material Unlit(string name, Color c) => GetOrCreate(name, "Universal Render Pipeline/Unlit", c, out _);

        internal static Material UnlitDoubleSided(string name, Color c)
        {
            var m = GetOrCreate(name, "Universal Render Pipeline/Unlit", c, out bool created);
            if (created) { m.SetFloat("_Cull", (float)CullMode.Off); EditorUtility.SetDirty(m); }
            return m;
        }

        /// <summary>
        /// Lưới bán trong suốt: Unlit Transparent, 2 mặt, texture mắt lưới 4 cm tự sinh.
        /// Ở xa, mipmap làm các sợi lưới hoà thành một lớp mờ – đúng cảm giác lưới trên TV.
        /// </summary>
        internal static Material NetMaterial()
        {
            var m = GetOrCreate("Net", "Universal Render Pipeline/Unlit", new Color(0.05f, 0.05f, 0.06f, 1f), out bool created);
            if (!created) return m;

            m.SetTexture("_BaseMap", NetTexture());
            MakeTransparent(m);
            m.SetFloat("_Cull", (float)CullMode.Off);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Chuyển material URP Unlit/Lit sang Transparent (alpha blend) bằng cách đặt property + keyword.</summary>
        internal static void MakeTransparent(Material m)
        {
            m.SetFloat("_Surface", 1);   // Transparent
            m.SetFloat("_Blend", 0);     // Alpha blend
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
        }

        internal static Texture2D NetTexture()
        {
            string path = TexDir + "/NetGrid.png";
            if (!File.Exists(path))
            {
                const int n = 8; // 8×8 px cho mỗi mắt lưới, sợi lưới dày 1 px
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                        tex.SetPixel(x, y, x == 0 || y == 0 ? new Color(1, 1, 1, 0.9f) : new Color(1, 1, 1, 0f));
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path);

                var imp = (TextureImporter)AssetImporter.GetAtPath(path);
                imp.alphaIsTransparency = true;
                imp.wrapMode = TextureWrapMode.Repeat;
                imp.filterMode = FilterMode.Bilinear;
                imp.mipmapEnabled = true;
                imp.textureCompression = TextureImporterCompression.Uncompressed;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        internal static Mesh SaveMesh(Mesh mesh, string name)
        {
            string path = $"{MeshDir}/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(mesh, existing);
                Object.DestroyImmediate(mesh);
                return existing;
            }
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        internal static void EnsureFolders()
        {
            foreach (var dir in new[] { Root + "/Scenes", MatDir, TexDir, MeshDir, Root + "/Settings" })
                if (!AssetDatabase.IsValidFolder(dir))
                {
                    var parent = Path.GetDirectoryName(dir)!.Replace('\\', '/');
                    AssetDatabase.CreateFolder(parent, Path.GetFileName(dir));
                }
        }

        internal static void AddSceneToBuildSettings()
        {
            // Court.unity là scene đầu tiên; các scene khác giữ lại nhưng tắt.
            var list = new List<EditorBuildSettingsScene> { new(ScenePath, true) };
            list.AddRange(EditorBuildSettings.scenes.Where(s => s.path != ScenePath).Select(s => new EditorBuildSettingsScene(s.path, false)));
            EditorBuildSettings.scenes = list.ToArray();
        }
    }
}
