using System.Linq;
using System.Threading.Tasks;
using Luban.SimpleJSON;
using UnityEngine;
using UnityEngine.UI;

namespace Game.HotUpdate.ChestGame
{
    /// <summary>
    /// 金币充值面板（真钱内购，仅 PayBridge.IsAvailable 平台显示入口：Steam/iOS/Google；小游戏无内购）。
    /// 流程（收据不可信客户端）：GET /api/pay/skus 列档 → 点击购买 POST /api/pay/order 建单拿 orderNo
    /// → PayBridge.PurchaseAsync 拉起平台支付 → POST /api/pay/verify 服务端向商店验单后发货到账。
    /// 价格文案优先用平台 SDK 本地化价（QuerySku），未接 SDK 时回落服务端 tb_recharge 的参考价。
    /// </summary>
    public static class RechargePanel
    {
        public static async void Show()
        {
            var panel = UITheme.CreatePanel(UIRoot.Instance.CanvasTransform, "panel_gold", Vector2.zero, new Vector2(760, 780));
            var rt = panel.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);

            var title = TDUIHelper.MakeText(panel.transform, new Vector2(0, -26), TextAnchor.MiddleCenter, 32);
            title.text = "<color=#F5D061>" + L10n.Get("充值钻石") + "</color>";
            TDUIHelper.SetSize(title.rectTransform, 720, 50);

            var hint = TDUIHelper.MakeText(panel.transform, new Vector2(0, -80), TextAnchor.MiddleCenter, 20);
            hint.color = UITheme.TextSub;
            hint.text = L10n.Get("钻石为游戏内付费虚拟货币，可通过应用内购买获得，仅用于兑换金币，不可兑换现金、实物");
            TDUIHelper.SetSize(hint.rectTransform, 720, 40);

            var loading = TDUIHelper.MakeText(panel.transform, new Vector2(0, -220), TextAnchor.MiddleCenter, 24);
            loading.text = L10n.Get("充值服务暂不可用，请稍后再试");

            var close = UITheme.ThemedButton(panel.transform, new Vector2(0, -690), new Vector2(220, 54), L10n.Get("关 闭"), "red", 24);
            close.onClick.AddListener(() => Object.Destroy(panel));

            // SKU 列表（服务端下发：skuId / gold / priceText 参考价）
            var r = await ApiClient.PaySkusAsync();
            if (panel == null) return;   // 等待期间面板已关闭
            if (r == null || !r["ok"].AsBool)
            {
                loading.text = "<color=#E05D5D>" + L10n.Get("充值服务暂不可用，请稍后再试") + "</color>";
                Debug.LogWarning("[RechargePanel] skus 拉取失败: " + (r == null ? "no response" : r.ToString()));
                return;
            }

            var skus = r["data"]["skus"];
            if (skus == null || skus.Count == 0)
            {
                loading.text = L10n.Get("充值服务暂不可用，请稍后再试");
                return;
            }
            Object.Destroy(loading.gameObject);

            // 平台本地化价（未接 SDK 为空 → 回落服务端参考价）
            var skuIds = new string[skus.Count];
            for (int i = 0; i < skus.Count; i++) skuIds[i] = skus[i]["skuId"].Value;
            var storePrices = await PayBridge.QuerySkuAsync(skuIds);
            if (panel == null) return; // await 期间面板已关/切屏：不再挂子节点（防 MissingReference）

            float y = -140;
            float step = 124;
            for (int i = 0; i < skus.Count; i++)
            {
                string skuId = skus[i]["skuId"].Value;
                long gems = skus[i]["gems"].AsLong;
                string priceText = skus[i]["priceText"].Value;
                var store = storePrices.FirstOrDefault(s => s.SkuId == skuId);
                if (store != null && !string.IsNullOrEmpty(store.Price)) priceText = store.Price;

                var row = UITheme.CreatePanel(panel.transform, "panel_dark", new Vector2(0, y), new Vector2(700, 110));
                var goldText = TDUIHelper.MakeText(row.transform, new Vector2(-20, 0), TextAnchor.MiddleLeft, 26);
                var grt = goldText.rectTransform;
                grt.anchorMin = grt.pivot = new Vector2(0, 0.5f);
                grt.anchoredPosition = new Vector2(-320, 0);
                TDUIHelper.SetSize(grt, 400, 40);
                goldText.text = "<color=#6EC6FF>◆ " + L10n.Get("钻石") + " ×" + gems.ToString("N0") + "</color>";

                var buy = UITheme.ThemedButton(row.transform, new Vector2(230, 0), new Vector2(200, 64), priceText, "green", 26);
                buy.onClick.AddListener(async () =>
                {
                    buy.interactable = false;
                    await PurchaseFlowAsync(skuId, gems);
                    if (buy != null) buy.interactable = true;
                });
                y -= step;
            }

            // 钻石 → 金币单向兑换行（软硬双币闸门；余额为 0 时置灰）
            await OnlineState.Refresh();
            int rate = OnlineState.State != null && OnlineState.State["data"] != null
                ? OnlineState.State["data"]["gemToGold"].AsInt : 10;
            var exRow = UITheme.CreatePanel(panel.transform, "panel_dark", new Vector2(0, -606), new Vector2(700, 84));
            var exText = TDUIHelper.MakeText(exRow.transform, new Vector2(-150, -24), TextAnchor.MiddleCenter, 20);
            TDUIHelper.SetSize(exText.rectTransform, 400, 36);
            exText.color = UITheme.TextSub;
            exText.text = "◆ " + L10n.Get("钻石") + " ×1 = " + rate + " " + L10n.Get("金币");
            var exBtn = UITheme.ThemedButton(exRow.transform, new Vector2(215, -14), new Vector2(230, 56),
                L10n.Get("兑换全部钻石"), "blue", 18);
            exBtn.interactable = OnlineState.Gems > 0;
            exBtn.onClick.AddListener(async () =>
            {
                exBtn.interactable = false;
                var v = await ApiClient.ExchangeGemsAsync(OnlineState.Gems);
                if (v != null && v["ok"].AsBool)
                {
                    UIRoot.ToastStatic(L10n.Get("全部钻石已兑换为金币") + " +" + v["data"]["goldGained"].AsLong.ToString("N0"));
                    UIRoot.Instance.RefreshTop();
                    Object.Destroy(panel);
                }
                else
                {
                    exBtn.interactable = OnlineState.Gems > 0;
                    UIRoot.ToastStatic(L10n.Get("兑换失败：") + Screens.ErrText(v));
                }
            });
        }

        /// <summary>建单 → 平台支付 → 服务端验单发货（钻石）</summary>
        private static async Task PurchaseFlowAsync(string skuId, long gems)
        {
            // 1) 建单（服务端返回 orderNo，回执带回防串单）
            var order = await ApiClient.CreatePayOrderAsync(skuId);
            if (order == null || !order["ok"].AsBool)
            {
                UIRoot.ToastStatic(L10n.Get("购买失败：") + Screens.ErrText(order));
                return;
            }
            string orderNo = order["data"]["orderNo"].Value;

            // 2) 平台支付（小游戏编译期到不了这里；未接 SDK 的平台桩直接失败）
            var result = await PayBridge.PurchaseAsync(skuId, orderNo);
            if (!result.Success)
            {
                UIRoot.ToastStatic(L10n.Get("支付未完成") + (string.IsNullOrEmpty(result.Error) ? "" : "：" + result.Error));
                return;
            }

            // 3) 服务端验单发货（幂等：掉线重试/重复回调不会重复加金）
            var verify = await ApiClient.VerifyPayAsync(result.OrderId ?? orderNo, result.PlatformReceipt);
            if (verify != null && verify["ok"].AsBool)
            {
                long granted = verify["data"]["gemsGranted"].AsLong;
                UIRoot.ToastStatic(L10n.Get("钻石已到账") + " ◆+" + granted.ToString("N0"));
                UIRoot.Instance.RefreshTop();
            }
            else
            {
                UIRoot.ToastStatic(L10n.Get("购买失败：") + Screens.ErrText(verify));
            }
        }
    }
}
