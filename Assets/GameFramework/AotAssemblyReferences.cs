using System;

/// <summary>
/// AOT 程序集保底引用（HybridCLR）：
/// 热更程序集（Game.HotUpdate 的 ApiClient）使用 System.Net.Http.HttpClient，
/// 但 AOT 侧若无任何代码引用该程序集，真机构建时它会被裁掉，
/// 导致 HybridCLR 方法桥生成报 "resolve AOT dll:System.Net.Http failed"。
/// 在此用 typeof 强引用，让 il2cpp 裁剪产物保留该程序集。
/// </summary>
internal static class AotAssemblyReferences
{
    internal static readonly Type[] Keep =
    {
        typeof(System.Net.Http.HttpClient),
    };
}
