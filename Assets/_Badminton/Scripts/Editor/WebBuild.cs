using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// One-click / command-line WebGL build for the Badminton project.
///
/// Editor:   menu  Build > Web > Build WebGL
/// CLI:      Unity.exe -batchmode -quit -projectPath . -executeMethod WebBuild.BuildFromCommandLine [-buildPath Builds/WebGL] [-development]
/// </summary>
public static class WebBuild
{
    const string DefaultOutput = "Builds/WebGL";

    [MenuItem("Build/Web/Apply Web Player Settings")]
    public static void ApplyWebSettings()
    {
        var web = UnityEditor.Build.NamedBuildTarget.WebGL;

        // ── Nén ──
        // Brotli: nhỏ hơn Gzip ~15–20%. Decompression fallback: loader tự giải nén bằng JS khi server
        // không gửi header Content-Encoding ⇒ chạy được trên mọi host tĩnh (itch.io, GitHub Pages, Netlify...).
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
        PlayerSettings.WebGL.decompressionFallback = true;
        PlayerSettings.WebGL.dataCaching = true;                 // lần sau mở game không tải lại .data
        PlayerSettings.WebGL.nameFilesAsHashes = true;           // tên file theo hash ⇒ cập nhật không dính cache cũ
        PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
        PlayerSettings.WebGL.showDiagnostics = false;
        PlayerSettings.WebGL.template = "APPLICATION:Default";

        // ── Kích thước code ──
        PlayerSettings.SetIl2CppCodeGeneration(web, UnityEditor.Build.Il2CppCodeGeneration.OptimizeSize);
        PlayerSettings.SetIl2CppCompilerConfiguration(web, Il2CppCompilerConfiguration.Master);
        PlayerSettings.SetManagedStrippingLevel(web, ManagedStrippingLevel.Medium); // link.xml giữ lại UGS/Netcode
        PlayerSettings.stripEngineCode = true;                   // bỏ module engine không dùng (cloth, terrain, video...)

        // ── Bộ nhớ ──
        PlayerSettings.WebGL.initialMemorySize = 64;
        PlayerSettings.WebGL.maximumMemorySize = 1024;
        PlayerSettings.WebGL.memoryGrowthMode = WebGLMemoryGrowthMode.Geometric;

        // ── Khung hình ──
        PlayerSettings.defaultWebScreenWidth = 1280;
        PlayerSettings.defaultWebScreenHeight = 720;
        PlayerSettings.runInBackground = true;                   // không đứng hình khi chuyển tab (quan trọng khi online)

        AssetDatabase.SaveAssets();
        Debug.Log("[WebBuild] Web player settings applied.");
    }

    [MenuItem("Build/Web/Build WebGL")]
    public static void BuildFromMenu() => Build(DefaultOutput, development: false);

    [MenuItem("Build/Web/Build WebGL (Development)")]
    public static void BuildDevFromMenu() => Build(DefaultOutput, development: true);

    // Entry point for -executeMethod
    public static void BuildFromCommandLine()
    {
        var args = Environment.GetCommandLineArgs();
        string output = DefaultOutput;
        int i = Array.IndexOf(args, "-buildPath");
        if (i >= 0 && i + 1 < args.Length) output = args[i + 1];
        bool dev = args.Contains("-development");

        bool ok = Build(output, dev);
        if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
    }

    static bool Build(string output, bool development)
    {
        if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL))
        {
            const string msg = "WebGL Build Support module is not installed for this Unity version. " +
                               "Install it via Unity Hub > Installs > (this version) > Add modules > Web Build Support.";
            Debug.LogError("[WebBuild] " + msg);
            if (!Application.isBatchMode) EditorUtility.DisplayDialog("WebGL module missing", msg, "OK");
            return false;
        }

        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL);

        ApplyWebSettings();

        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        if (scenes.Length == 0)
        {
            Debug.LogError("[WebBuild] No enabled scenes in Build Settings.");
            return false;
        }

        Directory.CreateDirectory(output);
        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = output,
            target = BuildTarget.WebGL,
            targetGroup = BuildTargetGroup.WebGL,
            options = development ? BuildOptions.Development : BuildOptions.None,
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        var summary = report.summary;
        if (summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"[WebBuild] Succeeded: {Path.GetFullPath(output)} ({summary.totalSize / (1024f * 1024f):F1} MB)");
            if (!Application.isBatchMode) EditorUtility.RevealInFinder(Path.Combine(output, "index.html"));
            return true;
        }

        Debug.LogError($"[WebBuild] Failed: {summary.result}, {summary.totalErrors} error(s).");
        return false;
    }
}
