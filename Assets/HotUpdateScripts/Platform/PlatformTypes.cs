namespace Game.HotUpdate.ChestGame
{
    /// <summary>
    /// 发布平台标识。多端共用一份热更代码，平台差异全部收敛在 Platform/ 目录：
    /// 客户端用编译宏（MINIGAME/STEAM 由 CI 按构建目标注入）+ Unity 内建宏（UNITY_IOS/UNITY_ANDROID）选择实现；
    /// 服务端按平台验签（/api/auth/{platform}）。
    /// 产品决策：小游戏端只做广告变现，不做内购（见 Docs/多平台构建指南.md）。
    /// </summary>
    public enum PlatformId
    {
        /// <summary>编辑器 / 未接平台 SDK：沿用 /api/login 名称登录</summary>
        Guest = 0,
        /// <summary>微信小游戏（WebGL 转换，MINIGAME 宏）。仅广告变现，无内购</summary>
        WeixinMinigame = 1,
        /// <summary>Steam（STEAM 宏，StandaloneWindows64 目标）</summary>
        Steam = 2,
        /// <summary>App Store（UNITY_IOS）</summary>
        Ios = 3,
        /// <summary>Google Play（UNITY_ANDROID）</summary>
        GooglePlay = 4,
    }

    /// <summary>平台身份：ILoginProvider 产出 → POST /api/auth/{platform} 换服务端 token</summary>
    public sealed class PlatformIdentity
    {
        public PlatformId Platform;
        /// <summary>平台侧用户唯一 Id（微信 openid / SteamID / Apple player id）</summary>
        public string PlatformUserId;
        /// <summary>登录凭证（Steam AuthSessionTicket / 微信 code / Apple IdentityToken），服务端验签用</summary>
        public string AuthPayload;
    }

    /// <summary>内购商品信息（价格字符串由平台 SDK 返回，本地化后直接展示）</summary>
    public sealed class PaySku
    {
        public string SkuId;
        public string Title;
        public string Price;
    }

    /// <summary>购买结果：Success 时 PlatformReceipt 交服务端 POST /api/pay/verify 验单后发货（防伪造）</summary>
    public sealed class PayResult
    {
        public bool Success;
        public string SkuId;
        /// <summary>服务端生成的订单号（发起购买时下发，回执带回防串单）</summary>
        public string OrderId;
        /// <summary>平台收据（Steam 微交易回执 / App Store receipt / Play purchaseToken）</summary>
        public string PlatformReceipt;
        public string Error;
    }
}
