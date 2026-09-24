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
            UIRoot.Instance.RefreshTop();
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
