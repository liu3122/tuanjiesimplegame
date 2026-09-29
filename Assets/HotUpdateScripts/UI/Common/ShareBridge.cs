using System;
using UnityEngine;

namespace Game.HotUpdate.ChestGame
{
    /// <summary>
    /// 分享接口层：业务侧只调 ShareBridge.Share，具体平台由 Provider 决定。
    /// 默认 ClipboardShareProvider（复制文案到剪贴板）；接入微信/抖音 SDK 时实现 IShareProvider
    /// 并在启动处调用 ShareBridge.SetProvider(new WechatShareProvider()) 即完成切换。
    /// </summary>
    public interface IShareProvider
    {
        string Name { get; }
        /// <summary>发起分享。SDK 版：调平台 API；完成后回调 onDone(success)。</summary>
        void Share(string title, string text, string url, Action<bool> onDone);

        /// <summary>分享图片战报（本地 PNG 路径）。SDK 版：微信/抖音图片消息分享。</summary>
        void ShareImage(string imagePath, string title, string text, Action<bool> onDone);
    }

    /// <summary>默认实现：复制到系统剪贴板（编辑器/无 SDK 环境可用）</summary>
    public class ClipboardShareProvider : IShareProvider
    {
        public string Name => "clipboard";

        public void Share(string title, string text, string url, Action<bool> onDone)
        {
#if UNITY_EDITOR || UNITY_STANDALONE
            GUIUtility.systemCopyBuffer = title + "\n" + text + " " + url;
            onDone?.Invoke(true);
#else
            // 移动端剪贴板可接原生插件；无 SDK 时退化为提示
            onDone?.Invoke(false);
#endif
        }

        public void ShareImage(string imagePath, string title, string text, Action<bool> onDone)
        {
#if UNITY_EDITOR || UNITY_STANDALONE
            GUIUtility.systemCopyBuffer = title + " " + text + " [图] " + imagePath;
            Debug.Log("[ShareImage] 战报已生成: " + imagePath);
            onDone?.Invoke(true);
#else
            onDone?.Invoke(false);
#endif
        }
    }

    public static class ShareBridge
    {
        private static IShareProvider _provider = new ClipboardShareProvider();

        /// <summary>注册平台分享实现（接 SDK 时在启动处调用一次）</summary>
        public static void SetProvider(IShareProvider provider)
        {
            if (provider != null) _provider = provider;
        }

        public static string ProviderName => _provider.Name;

        /// <summary>业务侧入口：分享（自动上报分享行为）</summary>
        public static async void Share(string contentId)
        {
            var content = await ApiClient.GetShareContentAsync(contentId);
            if (content == null || !content["ok"].AsBool)
            {
                UIRoot.ToastStatic(L10n.Get("获取分享内容失败"));
                return;
            }
            var data = content["data"];
            _provider.Share(data["title"].Value, data["text"].Value, data["url"].Value, success =>
            {
                UIRoot.ToastStatic(success
                    ? (ProviderName == "clipboard" ? L10n.Get("已复制分享文案") : L10n.Get("分享成功"))
                    : L10n.Get("分享未完成"));
            });
            _ = ApiClient.ShareReportAsync(ProviderName, contentId);
        }
    }
}
