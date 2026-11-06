using System;
using System.Diagnostics;
using System.IO;
using HybridCLR.Editor.Commands;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using YooAsset;
using YooAsset.Editor;
// BuildResult/BuildReport 与 YooAsset.Editor 同名类型重名，别名区分
using PlayerBuildResult = UnityEditor.Build.Reporting.BuildResult;
using PlayerBuildReport = UnityEditor.Build.Reporting.BuildReport;

/// <summary>
/// CI 多目标一键构建：同一份 main 分支代码打出各端包（平台差异靠宏 + Platform/ Bridge 层，不开分支）。
///
/// 批处理用法（Tools/ci/build_all.sh 已封装）：
///   Unity -batchmode -quit -projectPath . -executeMethod CIBuild.BuildFromArgs -ciTarget steam
///   可选目标：win / steam / android / webgl / ios / all（all = 本机可打的全部，iOS 需 macOS）
///
/// 流程：切平台+注宏 → HybridCLR 全量生成 → 拷热更/AOT dll → 烘焙 UI+契约校验 → YooAsset SBP(LZ4) → BuildPlayer。
/// 平台宏：STEAM（Standalone）、MINIGAME（WebGL→微信小游戏转换）、OFFLINE_BUNDLE（资源全内置离线模式；
/// 上线走 CDN 热更时去掉该宏并配置 GameApp.RemoteHostURL）。
/// 产物：Build/&lt;target&gt;_&lt;bundleVersion-gitHash&gt;/；资源包版本同后缀。
/// 注意：微信小游戏需再用官方"Unity 转换工具"二次转换 WebGL 产物（首包瘦身前提，见 Docs/多平台构建指南.md）。
/// </summary>
public static class CIBuild
{
    private const string PackageName = "DefaultPackage";
    private const string OutputRoot = "Build";

    // ---------------- 批处理入口 ----------------

    [MenuItem("Build/CI/Steam (Standalone + STEAM)", priority = 300)]
    public static void BuildSteam()
    {
        Build(BuildTarget.StandaloneWindows64, BuildTargetGroup.Standalone, "STEAM;OFFLINE_BUNDLE", "steam");
    }

    [MenuItem("Build/CI/Windows (Standalone 裸包)", priority = 301)]
    public static void BuildWindows()
    {
        Build(BuildTarget.StandaloneWindows64, BuildTargetGroup.Standalone, "OFFLINE_BUNDLE", "win");
    }

    [MenuItem("Build/CI/Android (Google Play)", priority = 302)]
    public static void BuildAndroid()
    {
        Build(BuildTarget.Android, BuildTargetGroup.Android, "OFFLINE_BUNDLE", "android");
    }

    [MenuItem("Build/CI/iOS (需 macOS)", priority = 303)]
    public static void BuildIos()
    {
#if UNITY_EDITOR_OSX
        Build(BuildTarget.iOS, BuildTargetGroup.iOS, "OFFLINE_BUNDLE", "ios");
#else
        throw new NotSupportedException("[CIBuild] iOS 构建需在 macOS Runner 上执行（产出 Xcode 工程后再出 ipa）");
#endif
    }

    /// <summary>微信小游戏底包：WebGL + MINIGAME 宏（内购编译期关闭，仅广告变现）</summary>
    [MenuItem("Build/CI/WebGL (微信小游戏底包)", priority = 304)]
    public static void BuildWebgl()
    {
        Build(BuildTarget.WebGL, BuildTargetGroup.WebGL, "MINIGAME;OFFLINE_BUNDLE", "webgl");
    }

    /// <summary>-ciTarget 可选：win / steam / android / webgl / ios / all</summary>
    public static void BuildFromArgs()
    {
        string target = GetArg("-ciTarget") ?? "steam";
        switch (target)
        {
            case "win": BuildWindows(); return;
            case "steam": BuildSteam(); return;
            case "android": BuildAndroid(); return;
            case "webgl": BuildWebgl(); return;
            case "ios": BuildIos(); return;
            case "all": BuildWindows(); BuildSteam(); BuildAndroid(); BuildWebgl(); return;
            default: throw new ArgumentException($"[CIBuild] 未知 -ciTarget：{target}（可选 win/steam/android/webgl/ios/all）");
        }
    }

    // ---------------- 构建主流程 ----------------

    private static void Build(BuildTarget target, BuildTargetGroup group, string defines, string name)
    {
        string version = PlayerSettings.bundleVersion + "-" + GitShortHash();
        UnityEngine.Debug.Log($"[CIBuild] ===== {name}({target}) 开始，版本 {version} =====");

        // 1. 切平台（宏按 BuildTargetGroup 生效，先切再注）
        if (EditorUserBuildSettings.activeBuildTarget != target)
        {
            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(group, target))
                throw new Exception($"[CIBuild] 切换平台失败：{target}");
        }

        // 2. 平台宏：合并而非覆盖——项目既有宏（模板遗留如 LOCAL_LOGIN 等）原样保留，
        //    仅 CIBuild 自有宏（win↔steam 切换）先移除再并入，防残留
        string prev = PlayerSettings.GetScriptingDefineSymbolsForGroup(group);
        string merged = MergeDefines(prev, defines);
        PlayerSettings.SetScriptingDefineSymbolsForGroup(group, merged);
        UnityEngine.Debug.Log($"[CIBuild] 宏 [{group}]：{prev} → {merged}");

        // 构建主体 + finally 还原宏：无论成败，构建结束后剥离 CI 注入的宏，
        // ProjectSettings 回到项目原有状态（编辑器开发环境不受构建副作用影响；产物已编译完成，事后还原不影响结果）
        try
        {
            BuildContent(target, group, name, version);
        }
        finally
        {
            string current = PlayerSettings.GetScriptingDefineSymbolsForGroup(group);
            string restored = MergeDefines(current, string.Empty); // 剥离 OwnedDefines，等价于"删掉不属于本次目标的 CI 宏"
            if (restored != current)
            {
                PlayerSettings.SetScriptingDefineSymbolsForGroup(group, restored);
                UnityEngine.Debug.Log($"[CIBuild] 宏已还原 [{group}]：{current} → {restored}");
            }
        }
    }

    /// <summary>构建主体：HybridCLR 生成 → 拷 DLL → 烘焙校验 → YooAsset → BuildPlayer（宏由调用方注入/还原）</summary>
    private static void BuildContent(BuildTarget target, BuildTargetGroup group, string name, string version)
    {
        // 3. HybridCLR 全量生成（热更 dll 编译 + link.xml + AOT 裁剪 + 桥接函数）；
        //    未初始化则自动安装默认 il2cpp（一次性动作：git clone + 拷贝 Unity il2cpp，需网络）
        var hybridclrInstaller = new HybridCLR.Editor.Installer.InstallerController();
        if (!hybridclrInstaller.HasInstalledHybridCLR())
        {
            UnityEngine.Debug.Log("[CIBuild] HybridCLR 未初始化，自动安装中...");
            hybridclrInstaller.InstallDefaultHybridCLR();
            if (!hybridclrInstaller.HasInstalledHybridCLR())
                throw new Exception("[CIBuild] HybridCLR 自动安装失败（检查网络/git，或手动执行菜单 HybridCLR/Installer）");
        }
        PrebuildCommand.GenerateAll();

        // 4. 热更/AOT 元数据 dll 拷入 Assets，交 YooAsset 收集
        ProjectSetup.CopyHotUpdateDlls();

        // 5. UI 契约校验（对已提交 prefab；FAIL = LogError → 批处理退出码非 0）。
        //    CI 不重烘焙 prefab：Unity 每次烘焙生成的对象哈希都会变（会话级），
        //    自动重烘焙会导致每次构建后工作区出现无意义 diff。改布局代码后手动执行
        //    Build/UI/Bake UI Prefabs 并提交 prefab；忘烘时契约校验（缺节点 FAIL）会拦住。
        ValidateUIPrefabs.Validate();

        // 6. YooAsset SBP 构建（LZ4 + 资源内置，配合 OFFLINE_BUNDLE 离线模式；走 CDN 时改 HostPlayMode 并上传 Bundles/<target> 目录）
        // 同版本重打：先清旧资源包输出（YooAsset 拒绝覆盖已存在的版本目录，ErrorCode115）
        string packageOutputDir = Path.Combine(OutputRoot, "Bundles", target.ToString(), PackageName, version);
        if (Directory.Exists(packageOutputDir)) Directory.Delete(packageOutputDir, true);
        var parameters = new ScriptableBuildParameters
        {
            BuildOutputRoot = Path.Combine(OutputRoot, "Bundles"),
            BundledFileRoot = BundleBuilderHelper.GetStreamingAssetsRoot(),
            BuildPipeline = EBuildPipeline.ScriptableBuildPipeline.ToString(),
            BuildBundleType = (int)EBundleType.AssetBundle,
            BuildTarget = target,
            PackageName = PackageName,
            PackageVersion = version,
            FileNameStyle = EFileNameStyle.HashName,
            BundledCopyOption = EBundledCopyOption.ClearAndCopyAll,
            VerifyBuildingResult = true,
            CompressOption = ECompressOption.LZ4,
        };
        YooAsset.Editor.BuildResult bundleResult = new ScriptableBuildPipeline().Run(parameters, true);
        if (bundleResult == null || !bundleResult.Success)
            throw new Exception($"[CIBuild] YooAsset 资源包构建失败：{(bundleResult != null ? bundleResult.ErrorInfo : "null result")}");
        UnityEngine.Debug.Log($"[CIBuild] 资源包完成：{bundleResult.OutputPackageDirectory}");

        // 7. Player 构建（BuildPlayer 自带同步编译，OFFLINE_BUNDLE/MINIGAME 等宏随之生效）
        // HybridCLR strip 会临时置 buildScriptsOnly/createSolution=true（finally 还原，但历史会话可能残留）；
        // 不复位的话 BuildPlayer 只导出 VS 工程不打 exe（冒烟实测踩过）。此处显式复位 + 清旧产物。
        EditorUserBuildSettings.buildScriptsOnly = false;
#if UNITY_EDITOR_WIN
        UnityEditor.WindowsStandalone.UserBuildSettings.createSolution = false;
#endif
        string artifactDir = Path.Combine(OutputRoot, $"{name}_{version}");
        if (Directory.Exists(artifactDir)) Directory.Delete(artifactDir, true);
        var options = new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/Boot.unity" },
            locationPathName = target == BuildTarget.StandaloneWindows64
                ? Path.Combine(artifactDir, name + ".exe")
                : (target == BuildTarget.Android ? Path.Combine(artifactDir, name + ".apk") : artifactDir), // iOS=Xcode工程 / WebGL=目录
            target = target,
            targetGroup = group,
            options = BuildOptions.None,
        };
        PlayerBuildReport report = BuildPipeline.BuildPlayer(options);
        if (report.summary.result != PlayerBuildResult.Succeeded)
            throw new Exception($"[CIBuild] Player 构建失败：{report.summary.result}，用时 {report.summary.totalTime}，错误 {report.summary.totalErrors} 个");

        UnityEngine.Debug.Log($"[CIBuild] ===== {name} 构建成功 → {options.locationPathName}（资源版本 {version}） =====");
    }

    // ---------------- 工具 ----------------

    /// <summary>CIBuild 自有宏：切换目标时先移除（避免 STEAM 残留到裸包），再并入本次所需</summary>
    private static readonly string[] OwnedDefines = { "STEAM", "MINIGAME", "OFFLINE_BUNDLE" };

    private static string MergeDefines(string existing, string required)
    {
        var list = new System.Collections.Generic.List<string>();
        foreach (string d in existing.Split(';'))
        {
            string t = d.Trim();
            if (t.Length > 0 && Array.IndexOf(OwnedDefines, t) < 0 && !list.Contains(t)) list.Add(t);
        }
        foreach (string d in required.Split(';'))
        {
            string t = d.Trim();
            if (t.Length > 0 && !list.Contains(t)) list.Add(t);
        }
        return string.Join(";", list);
    }

    private static string GitShortHash()
    {
        try
        {
            var psi = new ProcessStartInfo("git", "rev-parse --short HEAD")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Directory.GetCurrentDirectory(),
            };
            using (var p = Process.Start(psi))
            {
                string hash = p.StandardOutput.ReadToEnd().Trim();
                return hash.Length > 0 ? hash : "nogit";
            }
        }
        catch
        {
            return "nogit";
        }
    }

    /// <summary>解析命令行参数：支持 “-ciTarget steam” 与 “-ciTarget=steam” 两种形式</summary>
    private static string GetArg(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == name && i + 1 < args.Length) return args[i + 1];
            if (args[i].StartsWith(name + "=")) return args[i].Substring(name.Length + 1);
        }
        return null;
    }
}
