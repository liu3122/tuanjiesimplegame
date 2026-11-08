using System.Collections;
using cfg;
using Luban.SimpleJSON;
using UnityEngine;
using YooAsset;

namespace Game.HotUpdate
{
    /// <summary>
    /// 热更入口（由 GameApp 反射调用，可随版本任意修改）。
    /// 流程：通过 YooAsset 异步加载 Luban 配置（WebGL/小游戏不支持同步加载）→ 进入主菜单。
    /// 注意：热更程序集不可引用 AOT 侧的 Assembly-CSharp，包名等常量在此侧自行维护。
    /// </summary>
    public static class HotUpdateEntry
    {
        /// <summary>资源包名（须与 GameApp.PackageName 一致）</summary>
        private const string PackageName = "DefaultPackage";

        public static IEnumerator StartGame()
        {
            Debug.Log("[HotUpdate] 热更代码执行中！");

            // 1. 通过 YooAsset 异步加载 Luban 生成的 json 配置（打进资源包，location = 文件名）
            ResourcePackage package = YooAssets.GetPackage(PackageName);
            var loader = new ConfigLoader(package);
            yield return loader.LoadAll();
            GameConfig.Tables = loader.Tables;

            Debug.Log($"[HotUpdate] 配置加载完成：道具 {GameConfig.Tables.TbItem.DataList.Count} 条，宝箱 {GameConfig.Tables.TbChest.DataList.Count} 档，角色 {GameConfig.Tables.TbCharacter.DataList.Count} 个");

            // 2. 平台引导：按编译宏（MINIGAME/STEAM/UNITY_IOS/UNITY_ANDROID）选定登录/内购 Bridge 实现
            ChestGame.PlatformBootstrap.Init();

            // 3. 进入主菜单（塔防 / 宝箱商人）
            GameHub.Show();
        }

        /// <summary>Luban Tables 异步加载器：逐个配置文件 LoadAssetAsync（WebGL 禁同步）</summary>
        private sealed class ConfigLoader
        {
            private readonly ResourcePackage _package;
            public Tables Tables { get; private set; }

            public ConfigLoader(ResourcePackage package) { _package = package; }

            public IEnumerator LoadAll()
            {
                var jsonMap = new System.Collections.Generic.Dictionary<string, JSONNode>();
                string[] files =
                {
                    "tbitem", "tbchest", "tbchestgen", "tbbankerfactor", "tbcharacter",
                    "tbrushroom", "tblevel", "tbtext", "tbfeature", "tbad",
                    "tbquest", "tbsign", "tbglobal",
                };
                foreach (string file in files)
                {
                    var handle = _package.LoadAssetAsync<TextAsset>(file);
                    yield return handle;
                    var textAsset = (TextAsset)handle.AssetObject;
                    if (textAsset != null)
                    {
                        jsonMap[file] = JSON.Parse(textAsset.text);
                        Debug.Log($"[HotUpdate] 配置 {file} 加载完成");
                    }
                    else
                    {
                        Debug.LogWarning($"[HotUpdate] 配置 {file} 不存在，跳过");
                    }
                    handle.Release();
                }

                Tables = new Tables(file =>
                {
                    JSONNode node;
                    return jsonMap.TryGetValue(file, out node) ? node : null;
                });
            }
        }
    }
}
