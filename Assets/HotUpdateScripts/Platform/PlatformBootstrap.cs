using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.HotUpdate.ChestGame
{
    /// <summary>
    /// 平台引导：按编译宏选出当前平台的登录/内购实现（HotUpdateEntry.StartGame 调用一次）。
    /// 宏由 CI 注入（见 Assets/Editor/CIBuild.cs）：小游戏=MINIGAME、Steam=STEAM；iOS/Android 用内建宏。
    /// 各平台桩均 IsAvailable=false：登录自动回退游客，付费入口隐藏 —— 接真实 SDK 时替换对应桩即可。
    /// </summary>
    public static class PlatformBootstrap
    {
        /// <summary>当前发布平台（编译期确定）</summary>
        public static PlatformId Current
        {
            get
            {
#if MINIGAME
                return PlatformId.WeixinMinigame;
#elif STEAM
                return PlatformId.Steam;
#elif UNITY_IOS
                return PlatformId.Ios;
#elif UNITY_ANDROID
                return PlatformId.GooglePlay;
#else
                return PlatformId.Guest;
#endif
            }
        }

        public static void Init()
        {
            LoginBridge.SetProvider(CreateLogin());
            PayBridge.SetProvider(CreatePay());
            Debug.Log($"[PlatformBootstrap] 平台={Current} 登录={LoginBridge.ProviderName} 内购={(PayBridge.IsAvailable ? PayBridge.ProviderName : "关闭")}");
        }

        /// <summary>当前平台对应的服务端支付渠道名（wechat 服务端直接拒：小游戏无内购，兜底）</summary>
        public static string PayChannel
        {
            get
            {
                switch (Current)
                {
                    case PlatformId.WeixinMinigame: return "wechat";
                    case PlatformId.Steam: return "steam";
                    case PlatformId.Ios: return "ios";
                    case PlatformId.GooglePlay: return "google";
                    default: return "editor";
                }
            }
        }

        private static ILoginProvider CreateLogin()
        {
            switch (Current)
            {
                case PlatformId.WeixinMinigame: return new WeixinLoginStub();
                case PlatformId.Steam: return new SteamLoginStub();
                case PlatformId.Ios: return new IosLoginStub();
                case PlatformId.GooglePlay: return new GoogleLoginStub();
                default: return new GuestLoginProvider();
            }
        }

        private static IPayProvider CreatePay()
        {
            switch (Current)
            {
                // 产品决策：小游戏不做内购，编译期关闭（MINIGAME 包不含任何支付路径）
                case PlatformId.WeixinMinigame: return new UnavailablePayProvider("minigame-no-iap");
                case PlatformId.Steam: return new SteamPayStub();
                case PlatformId.Ios: return new IosPayStub();
                case PlatformId.GooglePlay: return new GooglePayStub();
                default:
#if UNITY_EDITOR
                    return new DevPayProvider();   // 编辑器模拟支付：全链路联调（服务端须开 --PaySandbox）
#else
                    return new UnavailablePayProvider("editor");
#endif
            }
        }
    }

    // ---------------- 各平台桩实现（接 SDK 时替换为实现类，接口不变） ----------------

    /// <summary>微信小游戏登录桩。正式实现：wx.login → code，交服务端 /api/auth/weixinminigame 换 openid 绑号</summary>
    public class WeixinLoginStub : ILoginProvider
    {
        public string Name => "weixin";
        public bool IsAvailable => false;
        public void GetIdentity(Action<PlatformIdentity> onDone, Action<string> onError = null)
        {
            onError?.Invoke("微信 SDK 未接入（wx.login code → 服务端 code2session）");
        }
    }

    /// <summary>Steam 登录桩。正式实现：Steamworks.NET GetAuthSessionTicket → 服务端 VAC/网页 API 验票</summary>
    public class SteamLoginStub : ILoginProvider
    {
        public string Name => "steam";
        public bool IsAvailable => false;
        public void GetIdentity(Action<PlatformIdentity> onDone, Action<string> onError = null)
        {
            onError?.Invoke("Steamworks.NET 未接入（GetAuthSessionTicket → /api/auth/steam）");
        }
    }

    /// <summary>Apple 登录桩。正式实现：Sign in with Apple IdentityToken → 服务端验签</summary>
    public class IosLoginStub : ILoginProvider
    {
        public string Name => "apple";
        public bool IsAvailable => false;
        public void GetIdentity(Action<PlatformIdentity> onDone, Action<string> onError = null)
        {
            onError?.Invoke("Sign in with Apple 未接入");
        }
    }

    /// <summary>Google 登录桩。正式实现：Play Games Services / Google ID token → 服务端验签</summary>
    public class GoogleLoginStub : ILoginProvider
    {
        public string Name => "google";
        public bool IsAvailable => false;
        public void GetIdentity(Action<PlatformIdentity> onDone, Action<string> onError = null)
        {
            onError?.Invoke("Google Play Games SDK 未接入");
        }
    }

    /// <summary>Steam 内购桩。正式实现：Steamworks 微交易发起 + MicroTxnAuthorizationResponse 回执 → /api/pay/verify</summary>
    public class SteamPayStub : IPayProvider
    {
        public string Name => "steam";
        public bool IsAvailable => false;
        public void QuerySku(string[] skuIds, Action<List<PaySku>> onDone) { onDone?.Invoke(new List<PaySku>()); }
        public void Purchase(string skuId, string orderNo, Action<PayResult> onDone)
        {
            onDone?.Invoke(new PayResult { Success = false, SkuId = skuId, OrderId = orderNo, Error = "Steamworks 微交易未接入" });
        }
        public void Restore(Action<bool> onDone) { onDone?.Invoke(false); }
    }

    /// <summary>iOS 内购桩。正式实现：Unity IAP（StoreKit2），receipt → /api/pay/verify 验单发货</summary>
    public class IosPayStub : IPayProvider
    {
        public string Name => "iap-ios";
        public bool IsAvailable => false;
        public void QuerySku(string[] skuIds, Action<List<PaySku>> onDone) { onDone?.Invoke(new List<PaySku>()); }
        public void Purchase(string skuId, string orderNo, Action<PayResult> onDone)
        {
            onDone?.Invoke(new PayResult { Success = false, SkuId = skuId, OrderId = orderNo, Error = "Unity IAP 未接入" });
        }
        public void Restore(Action<bool> onDone) { onDone?.Invoke(false); }
    }

    /// <summary>Google Play 内购桩。正式实现：Unity IAP（Play Billing4），purchaseToken → /api/pay/verify</summary>
    public class GooglePayStub : IPayProvider
    {
        public string Name => "iap-google";
        public bool IsAvailable => false;
        public void QuerySku(string[] skuIds, Action<List<PaySku>> onDone) { onDone?.Invoke(new List<PaySku>()); }
        public void Purchase(string skuId, string orderNo, Action<PayResult> onDone)
        {
            onDone?.Invoke(new PayResult { Success = false, SkuId = skuId, OrderId = orderNo, Error = "Unity IAP 未接入" });
        }
        public void Restore(Action<bool> onDone) { onDone?.Invoke(false); }
    }

#if UNITY_EDITOR
    /// <summary>
    /// 编辑器模拟支付（仅 UNITY_EDITOR 编入）：跳过真实商店直接回 dev 收据，
    /// 服务端开 --PaySandbox true 后可走完 建单→支付→验单→发货 全链路。
    /// 收据 dev:{orderNo}:{skuId} 与订单强绑定，服务端沙箱外一律拒绝。
    /// </summary>
    public class DevPayProvider : IPayProvider
    {
        public string Name => "editor-dev";
        public bool IsAvailable => true;
        public void QuerySku(string[] skuIds, Action<List<PaySku>> onDone) { onDone?.Invoke(new List<PaySku>()); }
        public void Purchase(string skuId, string orderNo, Action<PayResult> onDone)
        {
            Debug.Log($"[DevPayProvider] 模拟支付 {skuId}（订单 {orderNo}）——服务端需 --PaySandbox true");
            onDone?.Invoke(new PayResult
            {
                Success = true,
                SkuId = skuId,
                OrderId = orderNo,
                PlatformReceipt = $"dev:{orderNo}:{skuId}",
            });
        }
        public void Restore(Action<bool> onDone) { onDone?.Invoke(true); }
    }
#endif
}
