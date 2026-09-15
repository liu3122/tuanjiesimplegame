using System.IO;
using UnityEditor;
using UnityEngine;
using YooAsset;
using YooAsset.Editor;

/// <summary>
/// 编辑器模拟构建辅助：为 EditorSimulateMode 生成虚拟资源包，返回 packageRoot。
/// </summary>
public static class EditorSimulateBuildHelper
{
    public static string BuildSimulatePackage(string packageName)
    {
        string outputRoot = Path.Combine(EditorPathUtility.GetProjectPath(), "Bundles", "EditorSimulate");

        var buildParameters = new EditorSimulateBuildParameters();
        buildParameters.BuildOutputRoot = outputRoot;
        buildParameters.BundledFileRoot = BundleBuilderHelper.GetStreamingAssetsRoot();
        buildParameters.BuildPipeline = EBuildPipeline.EditorSimulateBuildPipeline.ToString();
        buildParameters.BuildBundleType = (int)EBundleType.VirtualAssetBundle;
        buildParameters.BuildTarget = EditorUserBuildSettings.activeBuildTarget;
        buildParameters.PackageName = packageName;
        buildParameters.PackageVersion = "SimulateVersion";
        buildParameters.FileNameStyle = EFileNameStyle.HashName;
        buildParameters.BundledCopyOption = EBundledCopyOption.None;
        buildParameters.BundledCopyParams = string.Empty;
        buildParameters.ClearBuildCacheFiles = true;
        buildParameters.UseAssetDependencyDB = true;

        var pipeline = new EditorSimulateBuildPipeline();
        BuildResult result = pipeline.Run(buildParameters, true);
        if (result.Success)
        {
            return result.OutputPackageDirectory;
        }
        Debug.LogError($"[EditorSimulateBuildHelper] 模拟构建失败：{result.ErrorInfo}");
        return null;
    }
}
