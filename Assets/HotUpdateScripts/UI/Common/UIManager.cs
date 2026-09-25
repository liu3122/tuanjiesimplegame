using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using YooAsset;

namespace Game.HotUpdate.ChestGame
{
    /// <summary>
    /// 界面管理器：prefab 由 YooAsset 加载（Assets/AssetRaw/Prefab/UI/ 下收集），
    /// 单屏切换；关闭即销毁实例，可联动 Resources.UnloadUnusedAssets 回收界面专属图集。
    /// 控件绑定约定：prefab 内节点命名即代码 Find 的键（如 btn_login / input_name）。
    /// </summary>
    public static class UIManager
    {
        /// <summary>资源包名（须与 GameApp.PackageName 一致）</summary>
        private const string PackageName = "DefaultPackage";

        /// <summary>当前屏实例（单屏模式：切换时自动关闭旧屏）</summary>
        public static GameObject Current { get; private set; }
        public static string CurrentName { get; private set; }

        private static readonly Dictionary<string, AssetHandle> _handles = new Dictionary<string, AssetHandle>();

        /// <summary>加载并显示界面（自动关闭上一屏）。返回根节点；失败返回 null。</summary>
        public static async Task<GameObject> ShowAsync(string name, bool unloadUnusedOnSwitch = false)
        {
            if (CurrentName == name && Current != null)
            {
                return Current;
            }
            CloseCurrent(unloadUnusedOnSwitch);

            ResourcePackage package = YooAssets.GetPackage(PackageName);
            if (!package.IsLocationValid(name))
            {
                Debug.LogError($"[UIManager] 界面不在资源包中：{name}（先执行烘焙菜单生成 prefab）");
                return null;
            }

            AssetHandle handle = package.LoadAssetAsync<GameObject>(name);
            await handle;
            if (handle.Status != EOperationStatus.Succeeded)
            {
                Debug.LogError($"[UIManager] 界面加载失败：{name} -> {handle.Error}");
                return null;
            }

            var root = handle.InstantiateSync();
            root.name = name;
            root.transform.SetParent(UIRoot.Instance.CanvasTransform, false);
            L10n.LocalizeTransform(root.transform); // prefab 静态文本自动本地化
            _handles[name] = handle;
            Current = root;
            CurrentName = name;
            Debug.Log($"[UIManager] Show {name}");
            return root;
        }

        /// <summary>关闭当前屏。unloadUnused=true 时回收无引用资源（含界面专属图集）。</summary>
        public static void CloseCurrent(bool unloadUnused = false)
        {
            if (Current != null)
            {
                Debug.Log($"[UIManager] Close {CurrentName}");
                Object.Destroy(Current);
                Current = null;
            }
            if (CurrentName != null && _handles.TryGetValue(CurrentName, out var h))
            {
                h.Release();
                _handles.Remove(CurrentName);
            }
            CurrentName = null;
            if (unloadUnused)
            {
                Resources.UnloadUnusedAssets();
                Debug.Log(L10n.Get("[UIManager] 已回收无引用资源（界面图集随引用释放）"));
            }
        }

        /// <summary>按命名查找控件（绑定约定的核心）</summary>
        public static T Find<T>(Transform root, string path) where T : Component
        {
            var t = root.Find(path);
            if (t == null)
            {
                Debug.LogError($"[UIManager] 控件缺失：{root.name}/{path}");
                return null;
            }
            return t.GetComponent<T>();
        }
    }
}
