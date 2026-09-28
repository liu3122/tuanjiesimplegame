using System.Linq;
using System.Threading.Tasks;
using cfg;
using Luban.SimpleJSON;
using UnityEngine;
using UnityEngine.UI;

namespace Game.HotUpdate.ChestGame
{
    /// <summary>Screens 模块拆分（partial）：Lobby</summary>
    public static partial class Screens
    {


        public static async void ShowMain(bool unloadUnused = false)
        {
            UITheme.PreloadAtlas("main");
            var root = await UIManager.ShowAsync("MainScreen", unloadUnused);
            if (root == null) { UIRoot.ToastStatic(L10n.Get("MainScreen 加载失败")); return; }

            Bind(root, "card_shop", () => ShowShop());
            Bind(root, "card_bag", () => ShowBag());
            Bind(root, "card_auction", () => AuctionPanel.Show());
            Bind(root, "card_rank", () => ShowRank());
            Bind(root, "card_daily", () => ShowDaily());
            Bind(root, "card_rush", () => RushPanel.Show());
            var langBtn = UITheme.ThemedButton(root.transform, new Vector2(860, -50), new Vector2(170, 56),
                L10n.IsZh ? "EN" : L10n.Get("中文"), "blue", 24);
            langBtn.onClick.AddListener(L10n.ToggleLanguage);
            // 对决入口（模式选择器：镜像对决/对赌房；开关关闭时隐藏）
            var duelBtn = UITheme.ThemedButton(root.transform, new Vector2(660, -50), new Vector2(170, 56),
                L10n.Get("对 决"), "gold", 24);
            duelBtn.name = "btn_duel_entry";
            duelBtn.onClick.AddListener(() => DuelPanel.ShowChooser());
            UIRoot.Instance.RefreshTop();

            ApplyFeatureGates(root, duelBtn.gameObject);
            _ = RefreshFeatureGates(root, duelBtn.gameObject);
            _ = MaybeShowTutorial();
        }

        /// <summary>新玩家（0 次开箱且无宝箱）：弹出教学箱领取</summary>
        private static async Task MaybeShowTutorial()
        {
            if (OnlineState.State == null && !await OnlineState.Refresh()) return;
            var data = OnlineState.State["data"];
            if (data == null || data["opensTotal"].AsInt > 0) return;
            if (data["chests"] != null && data["chests"].Count > 0) return; // 已在玩（买过箱）

            var panel = UITheme.CreatePanel(UIRoot.Instance.CanvasTransform, "panel_gold", Vector2.zero, new Vector2(660, 420));
            var rt = panel.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            var title = TDUIHelper.MakeText(panel.transform, new Vector2(0, -140), TextAnchor.MiddleCenter, 34);
            title.text = "<color=#F5D061>" + L10n.Get("欢迎来到宝箱商人！") + "</color>";
            TDUIHelper.SetSize(title.rectTransform, 620, 60);
            var desc = TDUIHelper.MakeText(panel.transform, new Vector2(0, -230), TextAnchor.UpperLeft, 24);
            desc.color = UITheme.TextSub;
            desc.text = L10n.Get("排除小额提高期望、银行家随时出价、卖或不卖由你抉择。\n先领一个免费教学宝箱（固定内容），三步学会核心玩法。");
            TDUIHelper.SetSize(desc.rectTransform, 600, 140);

            var take = UITheme.ThemedButton(panel.transform, new Vector2(0, -340), new Vector2(360, 72), L10n.Get("领取教学宝箱"), "green", 28);
            take.onClick.AddListener(async () =>
            {
                take.interactable = false;
                var r = await ApiClient.TutorialChestAsync();
                if (r == null || !r["ok"].AsBool)
                {
                    take.interactable = true;
                    UIRoot.ToastStatic(L10n.Get("领取失败：") + Screens.ErrText(r));
                    return;
                }
                Object.Destroy(panel);
                ChestBoard.Show(r["data"]["chestUid"].AsLong);
            });
        }

        /// <summary>玩法开关：隐藏关闭的入口（大厅卡片 + 对决按钮）</summary>
        private static void ApplyFeatureGates(GameObject root, GameObject duelBtn)
        {
            if (root == null) return;
            if (OnlineState.State == null) return; // 无快照时默认显示
            SetCardActive(root, "card_rush", OnlineState.FeatureOn("rush"));
            SetCardActive(root, "card_daily", OnlineState.FeatureOn("daily"));
            SetCardActive(root, "card_auction", OnlineState.FeatureOn("auction"));
            if (duelBtn != null) duelBtn.SetActive(OnlineState.FeatureOn("duel") || OnlineState.FeatureOn("potduel"));
        }

        private static async Task RefreshFeatureGates(GameObject root, GameObject duelBtn)
        {
            if (await OnlineState.Refresh() && root != null) ApplyFeatureGates(root, duelBtn);
        }

        private static void SetCardActive(GameObject root, string node, bool on)
        {
            var t = root.transform.Find(node);
            if (t != null) t.gameObject.SetActive(on);
        }

        private static void Bind(GameObject root, string node, System.Action open)
        {
            var t = root.transform.Find(node);
            if (t == null) return;
            var btn = t.GetComponent<Button>();
            if (btn == null) return;
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() => open());
        }

        // ---------------- 商城 ----------------
    }
}
