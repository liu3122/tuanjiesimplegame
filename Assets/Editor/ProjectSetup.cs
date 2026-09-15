using System.IO;
using System.Linq;
using HybridCLR.Editor;
using HybridCLR.Editor.Commands;
using HybridCLR.Editor.Settings;
using UnityEditor;
using UnityEngine;
using YooAsset.Editor;

/// <summary>
/// 工程自动配置：
/// 1. 首次打开工程时自动生成 YooAsset 收集器配置（幂等，已存在则跳过）。
/// 2. 提供菜单：编译热更 dll 并拷贝到 Assets 下被 YooAsset 收集的目录。
/// </summary>
public static class ProjectSetup
{
    private const string PackageName = "DefaultPackage";
    private const string RawRoot = "Assets/AssetRaw";
    private const string HotUpdateDllTargetDir = "Assets/AssetRaw/HotUpdateDlls";
    private const string AOTMetadataTargetDir = "Assets/AssetRaw/AOTMetadataAssemblies";

    /// <summary>AOT 补充元数据程序集（与 GameApp 中列表保持一致）</summary>
    private static readonly string[] AOTMetadataAssemblies =
    {
        "mscorlib",
        "System",
        "System.Core",
        "netstandard",
    };

    /// <summary>批处理冒烟测试入口：初始化 → 打开 Boot 场景 → 进 Play（配合 -smoketest 参数）</summary>
    public static void SetupAndSmokeTest()
    {
        RunSetupNow();
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Boot.unity");
        EditorApplication.isPlaying = true;
    }

    /// <summary>命令行/菜单手动触发完整初始化（幂等）</summary>
    [MenuItem("Build/Run Project Setup", priority = 0)]
    public static void RunSetupNow()
    {
        CreateBootScene();
        CreateSamplePrefab();
        CreateCollectorSetting();
        AssetDatabase.SaveAssets();
    }

    [InitializeOnLoadMethod]
    private static void Setup()
    {
        // 延后一帧执行，避免与 YooAsset / HybridCLR 编辑器模块初始化竞争
        EditorApplication.delayCall += RunSetupNow;
    }

    /// <summary>创建 Boot 场景（已存在则跳过）并设为唯一构建场景</summary>
    private static void CreateBootScene()
    {
        const string scenePath = "Assets/Scenes/Boot.unity";
        if (File.Exists(scenePath))
        {
            return;
        }
        Directory.CreateDirectory("Assets/Scenes");

        var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
            UnityEditor.SceneManagement.NewSceneSetup.DefaultGameObjects,
            UnityEditor.SceneManagement.NewSceneMode.Single);
        var gameAppObject = new GameObject("GameApp");
        gameAppObject.AddComponent<GameApp>();
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, scenePath);

        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(scenePath, true) };
        Debug.Log($"[ProjectSetup] 已创建 Boot 场景：{scenePath}");
    }

    /// <summary>创建示例 prefab（挂热更脚本 Rotator，已存在则跳过）</summary>
    private static void CreateSamplePrefab()
    {
        const string prefabPath = "Assets/AssetRaw/Prefab/Cube.prefab";
        if (File.Exists(prefabPath))
        {
            return;
        }
        Directory.CreateDirectory("Assets/AssetRaw/Prefab");

        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "Cube";
        cube.transform.position = new Vector3(0, 0, 5);
        // 热更脚本通过反射挂载，保持 AOT 程序集对热更程序集零引用
        var rotatorType = System.Type.GetType("Game.HotUpdate.Rotator, Game.HotUpdate");
        if (rotatorType != null)
        {
            cube.AddComponent(rotatorType);
        }
        else
        {
            Debug.LogWarning("[ProjectSetup] 未找到热更脚本 Rotator（Game.HotUpdate 尚未编译？），prefab 未挂脚本");
        }
        PrefabUtility.SaveAsPrefabAsset(cube, prefabPath);
        Object.DestroyImmediate(cube);
        Debug.Log($"[ProjectSetup] 已创建示例 prefab：{prefabPath}");
    }

    /// <summary>生成 YooAsset 收集器配置（已存在则跳过）</summary>
    private static void CreateCollectorSetting()
    {
        if (BundleCollectorSettingData.HasSettingAsset())
        {
            return;
        }

        var package = BundleCollectorSettingData.CreatePackage(PackageName);
        package.EnableAddressable = true;
        package.AutoCollectShaders = true;
        package.IgnoreRuleName = "NormalIgnoreRule";

        // 配置表（Luban 生成的 json）
        var configGroup = BundleCollectorSettingData.CreateGroup(package, "ConfigBundle");
        AddCollector(configGroup, "Assets/AssetRaw/Config", ECollectorType.MainAssetCollector, nameof(PackDirectory));

        // 示例 prefab 等游戏资源
        var prefabGroup = BundleCollectorSettingData.CreateGroup(package, "PrefabBundle");
        AddCollector(prefabGroup, "Assets/AssetRaw/Prefab", ECollectorType.MainAssetCollector, nameof(PackSeparately));

        // 热更 dll（HybridCLR 编译产物，.bytes）
        var dllGroup = BundleCollectorSettingData.CreateGroup(package, "HotUpdateDllBundle");
        AddCollector(dllGroup, HotUpdateDllTargetDir, ECollectorType.MainAssetCollector, nameof(PackSeparately));

        // AOT 补充元数据 dll（IL2CPP 裁剪产物，.bytes）
        var aotGroup = BundleCollectorSettingData.CreateGroup(package, "AOTMetadataBundle");
        AddCollector(aotGroup, AOTMetadataTargetDir, ECollectorType.MainAssetCollector, nameof(PackSeparately));

        BundleCollectorSettingData.FixFile();
        BundleCollectorSettingData.SaveFile();
        Debug.Log($"[ProjectSetup] 已生成 YooAsset 收集器配置（Package: {PackageName}）");
    }

    private static void AddCollector(BundleCollectorGroup group, string collectPath, ECollectorType collectorType, string packRuleName)
    {
        string guid = AssetDatabase.AssetPathToGUID(collectPath);
        if (string.IsNullOrEmpty(guid))
        {
            // 目录尚不存在时补建（保证任意时机执行都能收集完整）
            Directory.CreateDirectory(collectPath);
            AssetDatabase.ImportAsset(collectPath);
            guid = AssetDatabase.AssetPathToGUID(collectPath);
        }
        if (string.IsNullOrEmpty(guid))
        {
            Debug.LogWarning($"[ProjectSetup] 目录不可用，跳过收集器：{collectPath}");
            return;
        }
        var collector = new BundleCollector
        {
            CollectPath = collectPath,
            CollectorGUID = guid,
            CollectorType = collectorType,
            PackRuleName = packRuleName,
            AddressRuleName = nameof(AddressByFileName),
            FilterRuleName = nameof(CollectAll),
        };
        BundleCollectorSettingData.CreateCollector(group, collector);
    }

    /// <summary>
    /// 编译热更 dll 并拷贝到 Assets 下（YooAsset 按 .bytes 收集），
    /// 编辑器模拟模式下即可走“真实热更流程”（从资源包加载 Game.HotUpdate）。
    /// </summary>
    [MenuItem("Build/HybridCLR/Copy HotUpdate Dlls", priority = 100)]
    public static void CopyHotUpdateDlls()
    {
        var target = EditorUserBuildSettings.activeBuildTarget;

        // 1. 编译热更 dll 到 HybridCLRData/HotUpdateDlls/<target>
        CompileDllCommand.CompileDll(target, EditorUserBuildSettings.development);

        // 2. 拷贝热更 dll
        string hotUpdateDllDir = SettingsUtil.GetHotUpdateDllsOutputDirByTarget(target);
        if (!Directory.Exists(hotUpdateDllDir))
        {
            Debug.LogError($"[ProjectSetup] 热更 dll 目录不存在：{hotUpdateDllDir}");
            return;
        }
        Directory.CreateDirectory(HotUpdateDllTargetDir);
        int copied = 0;
        foreach (string dllPath in Directory.GetFiles(hotUpdateDllDir, "*.dll"))
        {
            string dllName = Path.GetFileNameWithoutExtension(dllPath);
            // 只拷贝热更程序集（依赖的 AOT 程序集不进包，由补充元数据机制处理）
            string dest = $"{HotUpdateDllTargetDir}/{dllName}.bytes";
            File.Copy(dllPath, dest, true);
            copied++;
        }

        // 3. 拷贝 AOT 补充元数据 dll（若已生成）
        string aotDir = SettingsUtil.GetAssembliesPostIl2CppStripDir(target);
        int aotCopied = 0;
        if (Directory.Exists(aotDir))
        {
            Directory.CreateDirectory(AOTMetadataTargetDir);
            foreach (string assName in AOTMetadataAssemblies)
            {
                string src = $"{aotDir}/{assName}.dll";
                if (File.Exists(src))
                {
                    File.Copy(src, $"{AOTMetadataTargetDir}/{assName}.bytes", true);
                    aotCopied++;
                }
            }
        }

        AssetDatabase.Refresh();
        Debug.Log($"[ProjectSetup] 已拷贝 {copied} 个热更 dll 到 {HotUpdateDllTargetDir}，{aotCopied} 个 AOT 元数据 dll 到 {AOTMetadataTargetDir}");
        if (aotCopied == 0)
        {
            Debug.Log("[ProjectSetup] 提示：AOT 元数据未拷贝。真机构建时由 HybridCLR/Generate/All 自动生成，或先执行一次 Build Player。");
        }
    }
}
