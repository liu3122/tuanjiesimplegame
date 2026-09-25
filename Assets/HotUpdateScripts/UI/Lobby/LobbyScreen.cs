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
            // 镜像对决入口（纯代码按钮；开关关闭时隐藏）
            var duelBtn = UITheme.ThemedButton(root.transform, new Vector2(660, -50), new Vector2(170, 56),
                L10n.Get("对 决"), "gold", 24);
            duelBtn.name = "btn_duel_entry";
            duelBtn.onClick.AddListener(() => DuelPanel.Show());
            UIRoot.Instance.RefreshTop();

            ApplyFeatureGates(root, duelBtn.gameObject);
            _ = RefreshFeatureGates(root, duelBtn.gameObject);
        }

        /// <summary>玩法开关：隐藏关闭的入口（大厅卡片 + 对决按钮）</summary>
        private static void ApplyFeatureGates(GameObject root, GameObject duelBtn)
        {
            if (root == null) return;
            if (OnlineState.State == null) return; // 无快照时默认显示
            SetCardActive(root, "card_rush", OnlineState.FeatureOn("rush"));
            SetCardActive(root, "card_daily", OnlineState.FeatureOn("daily"));
            SetCardActive(root, "card_auction", OnlineState.FeatureOn("auction"));
            if (duelBtn != null) duelBtn.SetActive(OnlineState.FeatureOn("duel"));
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
