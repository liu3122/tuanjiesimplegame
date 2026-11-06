using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Game.HotUpdate.ChestGame
{
    /// <summary>
    /// 内购接入层。业务侧调 PayBridge.PurchaseAsync(skuId)，成功后拿 PlatformReceipt
    /// 调 POST /api/pay/verify，由服务端向对应平台验单后发货（金币/道具入库），收据不可信客户端。
    /// 产品决策：小游戏端不做内购 —— MINIGAME 目标编译期即固定 UnavailablePayProvider（Steam/iOS/Google 才接）。
    /// 接 SDK 时实现 IPayProvider（Steamworks 微交易 / Unity IAP）并由 PlatformBootstrap 切换，接口不变。
    /// </summary>
    public interface IPayProvider
    {
        string Name { get; }

        /// <summary>当前平台是否开放内购（UI 依此隐藏/展示付费入口）</summary>
        bool IsAvailable { get; }

        /// <summary>查询商品本地化信息（价格字符串直接展示）</summary>
        void QuerySku(string[] skuIds, Action<List<PaySku>> onDone);

        /// <summary>发起购买，结果回调（含平台收据）</summary>
        void Purchase(string skuId, Action<PayResult> onDone);

        /// <summary>恢复购买（iOS 必需：换机/重装找回非消耗品）</summary>
        void Restore(Action<bool> onDone);
    }

    public static class PayBridge
    {
        private static IPayProvider _provider = new UnavailablePayProvider("editor");

        /// <summary>注册内购实现（PlatformBootstrap.Init 调用）</summary>
        public static void SetProvider(IPayProvider provider)
        {
            if (provider != null) _provider = provider;
        }

        public static string ProviderName => _provider.Name;

        /// <summary>当前平台是否可付费（UI 用它决定入口显隐）</summary>
        public static bool IsAvailable => _provider.IsAvailable;

        /// <summary>查询商品信息（SDK 未接时返回空列表）</summary>
        public static async Task<List<PaySku>> QuerySkuAsync(string[] skuIds)
        {
            if (!_provider.IsAvailable) return new List<PaySku>();
            var tcs = new TaskCompletionSource<List<PaySku>>();
            _provider.QuerySku(skuIds, list => tcs.TrySetResult(list ?? new List<PaySku>()));
            return await tcs.Task;
        }

        /// <summary>购买入口：平台不可用时直接返回失败（UI 提示"当前平台未开放支付"）</summary>
        public static async Task<PayResult> PurchaseAsync(string skuId)
        {
            if (!_provider.IsAvailable)
            {
                Debug.Log($"[PayBridge] 当前平台（{_provider.Name}）未开放内购，忽略购买 {skuId}");
                return new PayResult { Success = false, SkuId = skuId, Error = "pay_unavailable" };
            }

            var tcs = new TaskCompletionSource<PayResult>();
            _provider.Purchase(skuId, r => tcs.TrySetResult(r ?? new PayResult { Success = false, SkuId = skuId, Error = "null_result" }));
            return await tcs.Task;
        }

        /// <summary>恢复购买（iOS 设置页用）</summary>
        public static void Restore(Action<bool> onDone)
        {
            if (!_provider.IsAvailable) { onDone?.Invoke(false); return; }
            _provider.Restore(onDone);
        }
    }

    /// <summary>不可用实现：未接 SDK 或平台无内购（小游戏/编辑器）时占位，调用直接失败</summary>
    public class UnavailablePayProvider : IPayProvider
    {
        private readonly string _name;
        public UnavailablePayProvider(string name) { _name = name; }

        public string Name => _name;
        public bool IsAvailable => false;

        public void QuerySku(string[] skuIds, Action<List<PaySku>> onDone) { onDone?.Invoke(new List<PaySku>()); }
        public void Purchase(string skuId, Action<PayResult> onDone)
        {
            onDone?.Invoke(new PayResult { Success = false, SkuId = skuId, Error = "pay_unavailable" });
        }
        public void Restore(Action<bool> onDone) { onDone?.Invoke(false); }
    }
}
