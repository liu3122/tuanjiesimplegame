using cfg;
using Luban.SimpleJSON;
using UnityEngine;
using YooAsset;

namespace Game.HotUpdate
{
    /// <summary>
    /// 热更入口（由 GameApp 反射调用，可随版本任意修改）。
    /// 流程：通过 YooAsset 加载 Luban 配置 → 进入主菜单（角色选择 → 宝箱商人玩法）。
    /// 注意：热更程序集不可引用 AOT 侧的 Assembly-CSharp，包名等常量在此侧自行维护。
    /// </summary>
    public static class HotUpdateEntry
    {
        /// <summary>资源包名（须与 GameApp.PackageName 一致）</summary>
        private const string PackageName = "DefaultPackage";

        public static void StartGame()
        {
            Debug.Log("[HotUpdate] 热更代码执行中！");

            // 1. 通过 YooAsset 加载 Luban 生成的 json 配置（打进资源包，location = 文件名）
            ResourcePackage package = YooAssets.GetPackage(PackageName);
            var tables = new Tables(file =>
            {
                var handle = package.LoadAssetSync<TextAsset>(file);
                var textAsset = (TextAsset)handle.AssetObject;
                var node = JSON.Parse(textAsset.text);
                handle.Release();
                return node;
            });
            GameConfig.Tables = tables;

            Debug.Log($"[HotUpdate] 配置加载完成：道具 {tables.TbItem.DataList.Count} 条，宝箱 {tables.TbChest.DataList.Count} 档，角色 {tables.TbCharacter.DataList.Count} 个");

            // 2. 进入主菜单（塔防 / 宝箱商人）
            GameHub.Show();
        }
    }
}
