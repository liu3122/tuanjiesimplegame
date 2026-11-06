using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Game.HotUpdate.ChestGame
{
    /// <summary>
    /// 平台登录接入层（与 AdBridge/ShareBridge 同款接口模式）：
    /// 业务侧只调 LoginBridge.LoginAsync()。默认 GuestLoginProvider 走现行 /api/login 名称登录，
    /// 行为与改造前完全一致；接平台 SDK（Steamworks / 微信 / Sign in with Apple / Play Games）时
    /// 实现 ILoginProvider 并由 PlatformBootstrap 按编译宏切换，服务端配套 /api/auth/{platform} 验签发 token。
    /// </summary>
    public interface ILoginProvider
    {
        string Name { get; }

        /// <summary>平台 SDK 是否可用（未接 SDK 的桩返回 false，登录自动回退游客）</summary>
        bool IsAvailable { get; }

        /// <summary>获取平台身份（SDK 登录或静默续期），失败回调 onError</summary>
        void GetIdentity(Action<PlatformIdentity> onDone, Action<string> onError = null);
    }

    public static class LoginBridge
    {
        private static ILoginProvider _provider = new GuestLoginProvider();

        /// <summary>注册平台登录实现（PlatformBootstrap.Init 调用）</summary>
        public static void SetProvider(ILoginProvider provider)
        {
            if (provider != null) _provider = provider;
        }

        public static string ProviderName => _provider.Name;

        /// <summary>登录入口：平台身份可用则走 /api/auth/{platform}，否则回退游客名称登录</summary>
        public static async Task<bool> LoginAsync()
        {
            var identity = await GetIdentityAsync();
            return await ApiClient.LoginWithIdentityAsync(identity);
        }

        private static async Task<PlatformIdentity> GetIdentityAsync()
        {
            if (!_provider.IsAvailable)
            {
                Debug.Log($"[LoginBridge] {_provider.Name} SDK 未接入，回退游客登录");
                return null;
            }

            var tcs = new TaskCompletionSource<PlatformIdentity>();
            _provider.GetIdentity(
                id => tcs.TrySetResult(id),
                err =>
                {
                    Debug.LogError("[LoginBridge] 获取平台身份失败：" + err + "，回退游客登录");
                    tcs.TrySetResult(null);
                });
            return await tcs.Task;
        }
    }

    /// <summary>游客登录（默认）：身份 = 玩家输入名，走现行 /api/login</summary>
    public class GuestLoginProvider : ILoginProvider
    {
        public string Name => "guest";
        public bool IsAvailable => true;

        public void GetIdentity(Action<PlatformIdentity> onDone, Action<string> onError = null)
        {
            onDone?.Invoke(new PlatformIdentity { Platform = PlatformId.Guest, PlatformUserId = ApiClient.PlayerName });
        }
    }
}
