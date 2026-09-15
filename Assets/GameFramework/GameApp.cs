using System;
using System.Collections;
using System.Reflection;
using HybridCLR;
using UnityEngine;
using YooAsset;

/// <summary>
/// 游戏启动引导（AOT 部分，不参与热更新）。
/// 流程：YooAsset 初始化 → 获取清单 →（真机）下载资源 → AOT 补充元数据 → 加载热更程序集 → 反射进入热更入口。
/// 挂载在 Boot 场景的 GameApp 物体上。
/// </summary>
public class GameApp : MonoBehaviour
{
    /// <summary>YooAsset 资源包名（与收集器配置中的 PackageName 一致）</summary>
    public const string PackageName = "DefaultPackage";

    /// <summary>热更程序集名（与 HybridCLRSettings 中 hotUpdateAssemblies 一致）</summary>
    public const string HotUpdateAssemblyName = "Game.HotUpdate";

    /// <summary>热更入口（热更程序集内），必须提供 public static void StartGame()</summary>
    private const string EntryTypeName = "Game.HotUpdate.HotUpdateEntry";
    private const string EntryMethodName = "StartGame";

    /// <summary>
    /// 真机运行模式。编辑器下始终使用 EditorSimulateMode。
    /// - OfflinePlayMode：内置资源模式，资源全部打进包体，无需资源服务器。
    /// - HostPlayMode：联机模式，通过 DefaultRemoteService 从远端下载更新资源。
    /// </summary>
    public static EPlayMode DevicePlayMode = EPlayMode.HostPlayMode;

    /// <summary>HostPlayMode 模式下的资源服务器地址（按环境修改，或接入你自己的 CDN）</summary>
    public const string RemoteHostURL = "http://127.0.0.1/CDN";

    /// <summary>需要补充元数据的 AOT 程序集（真机模式生效，按需增删）</summary>
    private static readonly string[] AOTMetadataAssemblies =
    {
        "mscorlib",
        "System",
        "System.Core",
        "netstandard",
    };

    private ResourcePackage _package;

    private IEnumerator Start()
    {
        Debug.Log($"[GameApp] Unity {Application.unityVersion} / EditorSimulate={Application.isEditor}");

#if UNITY_EDITOR
        // 批处理冒烟测试：-smoketest 参数时挂载自动驱动器
        if (SmokeDriver.IsSmokeTestRequested())
        {
            gameObject.AddComponent<SmokeDriver>();
        }
#endif

        // 1. 初始化 YooAsset
        YooAssets.Initialize();
        _package = YooAssets.CreatePackage(PackageName);
        yield return InitYooPackage();

        // 2. 加载清单
        yield return LoadManifest();

        // 3. 真机：下载资源 + AOT 补充元数据
#if !UNITY_EDITOR
        if (DevicePlayMode == EPlayMode.HostPlayMode)
        {
            yield return DownloadPatch();
        }
        LoadAOTMetadata();
#endif

        // 4. 加载热更程序集并进入热更入口
        Assembly hotUpdateAssembly = LoadHotUpdateAssembly();
        EnterHotUpdate(hotUpdateAssembly);
    }

    /// <summary>初始化资源包（按运行模式选择文件系统）</summary>
    private IEnumerator InitYooPackage()
    {
        InitializePackageOptions options;
#if UNITY_EDITOR
        // 编辑器模拟构建辅助类位于 Assembly-CSharp-Editor，AOT 侧通过反射调用
        var helperType = System.Type.GetType("EditorSimulateBuildHelper, Assembly-CSharp-Editor");
        string packageRoot = (string)helperType
            ?.GetMethod("BuildSimulatePackage", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            ?.Invoke(null, new object[] { PackageName });
        var simulateOptions = new EditorSimulateModeOptions
        {
            EditorFileSystemParameters = FileSystemParameters.CreateDefaultEditorFileSystemParameters(packageRoot),
        };
        options = simulateOptions;
#else
        if (DevicePlayMode == EPlayMode.OfflinePlayMode)
        {
            options = new OfflinePlayModeOptions
            {
                BuiltinFileSystemParameters = FileSystemParameters.CreateDefaultBuiltinFileSystemParameters(),
            };
        }
        else
        {
            options = new HostPlayModeOptions
            {
                BuiltinFileSystemParameters = FileSystemParameters.CreateDefaultBuiltinFileSystemParameters(),
                CacheFileSystemParameters = FileSystemParameters.CreateDefaultSandboxFileSystemParameters(
                    new DefaultRemoteService(RemoteHostURL)),
            };
        }
#endif
        var initOp = _package.InitializePackageAsync(options);
        yield return initOp;
        if (initOp.Status != EOperationStatus.Succeeded)
        {
            Debug.LogError($"[GameApp] YooAsset 初始化失败：{initOp.Error}");
            yield break;
        }
        Debug.Log("[GameApp] YooAsset 初始化完成");
    }

    /// <summary>请求资源版本并加载清单</summary>
    private IEnumerator LoadManifest()
    {
        var versionOp = _package.RequestPackageVersionAsync();
        yield return versionOp;
        if (versionOp.Status != EOperationStatus.Succeeded)
        {
            Debug.LogError($"[GameApp] 请求资源版本失败：{versionOp.Error}");
            yield break;
        }
        Debug.Log($"[GameApp] 资源版本：{versionOp.PackageVersion}");

        var manifestOp = _package.LoadPackageManifestAsync(new LoadPackageManifestOptions(versionOp.PackageVersion, 60));
        yield return manifestOp;
        if (manifestOp.Status != EOperationStatus.Succeeded)
        {
            Debug.LogError($"[GameApp] 加载资源清单失败：{manifestOp.Error}");
            yield break;
        }
    }

    /// <summary>下载远端资源（HostPlayMode）</summary>
    private IEnumerator DownloadPatch()
    {
        var downloader = _package.CreateResourceDownloader(new ResourceDownloaderOptions(10, 3));
        if (downloader.TotalDownloadCount == 0)
        {
            Debug.Log("[GameApp] 没有需要下载的资源");
            yield break;
        }

        int totalCount = downloader.TotalDownloadCount;
        long totalBytes = downloader.TotalDownloadBytes;
        Debug.Log($"[GameApp] 开始下载 {totalCount} 个文件，共 {totalBytes / 1024} KB");

        downloader.StartDownload();
        yield return downloader;
        if (downloader.Status != EOperationStatus.Succeeded)
        {
            Debug.LogError($"[GameApp] 资源下载失败：{downloader.Error}");
            yield break;
        }
        Debug.Log("[GameApp] 资源下载完成");
    }

    /// <summary>
    /// 为 AOT 程序集补充元数据，使热更代码可以调用 AOT 泛型。
    /// 说明：补充元数据 dll 为 IL2CPP 裁剪后的 AOT dll（HybridCLRData/AssembliesPostIl2CppStrip），
    /// 由 Editor 脚本拷贝到 Assets/AssetRaw/AOTMetadataAssemblies 下被 YooAsset 收集。
    /// </summary>
    private void LoadAOTMetadata()
    {
        foreach (string assName in AOTMetadataAssemblies)
        {
            if (!_package.IsLocationValid(assName))
            {
                Debug.LogWarning($"[GameApp] 未收集 AOT 补充元数据 {assName}，跳过（需执行菜单 Build/HybridCLR/Copy HotUpdate Dlls）");
                continue;
            }

            var handle = _package.LoadAssetSync<TextAsset>(assName);
            var textAsset = handle.AssetObject as TextAsset;
            if (textAsset == null)
            {
                Debug.LogError($"[GameApp] AOT 元数据加载失败：{assName}");
                handle.Release();
                continue;
            }

            var imageMode = HomologousImageMode.SuperSet;
            LoadImageErrorCode err = RuntimeApi.LoadMetadataForAOTAssembly(textAsset.bytes, imageMode);
            Debug.Log($"[GameApp] LoadMetadataForAOTAssembly {assName} → {err}");
            handle.Release();
        }
    }

    /// <summary>
    /// 加载热更程序集。
    /// 优先从 YooAsset 加载 HybridCLR 编译的热更 dll；
    /// 编辑器下若未执行“拷贝热更 DLL”，回退使用 Unity 已编译进域的程序集。
    /// </summary>
    private Assembly LoadHotUpdateAssembly()
    {
        if (_package.IsLocationValid(HotUpdateAssemblyName))
        {
            var handle = _package.LoadAssetSync<TextAsset>(HotUpdateAssemblyName);
            var textAsset = (TextAsset)handle.AssetObject;
            Debug.Log($"[GameApp] 加载热更程序集 {HotUpdateAssemblyName}（{(textAsset.bytes.Length / 1024)} KB）");
            return Assembly.Load(textAsset.bytes);
        }

#if UNITY_EDITOR
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.GetName().Name == HotUpdateAssemblyName)
            {
                Debug.Log("[GameApp] 使用编辑器域内已编译的热更程序集（如需测试真实热更流程，请执行菜单 Build/HybridCLR/Copy HotUpdate Dlls）");
                return assembly;
            }
        }
#endif
        throw new Exception($"[GameApp] 热更程序集 {HotUpdateAssemblyName} 不存在！真机请先构建资源包（含热更 dll）");
    }

    /// <summary>反射进入热更入口</summary>
    private void EnterHotUpdate(Assembly hotUpdateAssembly)
    {
        Type entryType = hotUpdateAssembly.GetType(EntryTypeName);
        if (entryType == null)
        {
            Debug.LogError($"[GameApp] 未找到热更入口类型 {EntryTypeName}");
            return;
        }
        MethodInfo method = entryType.GetMethod(EntryMethodName, BindingFlags.Public | BindingFlags.Static);
        if (method == null)
        {
            Debug.LogError($"[GameApp] 热更入口缺少 public static void {EntryMethodName}()");
            return;
        }
        Debug.Log("[GameApp] >>> 进入热更世界 <<<");
        method.Invoke(null, null);
    }
}
