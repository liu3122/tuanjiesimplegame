using System.Linq;
using System.Threading.Tasks;
using cfg;
using Luban.SimpleJSON;
using UnityEngine;
using UnityEngine.UI;

namespace Game.HotUpdate.ChestGame
{
    /// <summary>
    /// 界面集（prefab Bind 模式）：UIManager 加载 prefab → 按命名约定 Find 控件 → 绑定逻辑。
    /// 静态框架在 prefab 里（BakeUIPrefabs 烘焙），动态列表仍代码生成。
    /// </summary>
    public static class Screens
    {
        // ---------------- 登录 ----------------

        public static async void ShowLogin()
        {
            var root = await UIManager.ShowAsync("LoginScreen");
            if (root == null) { UIRoot.ToastStatic("LoginScreen 加载失败"); return; }

            var input = UIManager.Find<InputField>(root.transform, "input_name");
            var btn = UIManager.Find<Button>(root.transform, "btn_login");
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(async () =>
            {
                btn.interactable = false;
                ApiClient.PlayerName = string.IsNullOrEmpty(input.text) ? "玩家" + Random.Range(100, 999) : input.text;
                if (await ApiClient.LoginAsync())
                {
                    UIRoot.ToastStatic($"欢迎，{ApiClient.PlayerName}！");
                    ShowMain(true);
                }
                else
                {
                    btn.interactable = true;
                    UIRoot.ToastStatic("登录失败：服务器未启动？");
                }
            });
        }

        // ---------------- 主大厅 ----------------

        public static async void ShowMain(bool unloadUnused = false)
        {
            UITheme.PreloadAtlas("main");
            var root = await UIManager.ShowAsync("MainScreen", unloadUnused);
            if (root == null) { UIRoot.ToastStatic("MainScreen 加载失败"); return; }

            Bind(root, "card_shop", () => ShowShop());
            Bind(root, "card_bag", () => ShowBag());
            Bind(root, "card_auction", () => AuctionPanel.Show());
            Bind(root, "card_rank", () => ShowRank());
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

        public static async void ShowShop()
        {
            UITheme.PreloadAtlas("shop");
            var root = await UIManager.ShowAsync("ShopScreen");
            if (root == null) return;

            var back = UIManager.Find<Button>(root.transform, "btn_back");
            back.onClick.RemoveAllListeners();
            back.onClick.AddListener(() => ShowMain(true)); // 退出商城，回收 shop 图集

            for (int i = 0; i < GameConfig.Tables.TbChest.DataList.Count; i++)
            {
                var c = GameConfig.Tables.TbChest.DataList[i];
                var card = root.transform.Find("card_" + i);
                if (card == null) continue;
                var buyBtn = card.Find("btn_buy") != null ? card.Find("btn_buy").GetComponent<Button>() : null;
                if (buyBtn == null) continue;
                var config = c;
                buyBtn.onClick.RemoveAllListeners();
                buyBtn.onClick.AddListener(async () =>
                {
                    buyBtn.interactable = false;
                    var r = await ApiClient.BuyChestAsync(config.Id);
                    buyBtn.interactable = true;
                    if (r != null && r["ok"].AsBool)
                    {
                        UIRoot.ToastStatic($"已购买 {config.Name}，到背包查看");
                        UIRoot.Instance.RefreshTop();
                    }
                    else
                    {
                        UIRoot.ToastStatic("购买失败：" + ErrText(r));
                    }
                });
            }
        }

        // ---------------- 背包 ----------------

        public static async void ShowBag()
        {
            UITheme.PreloadAtlas("open");
            var root = await UIManager.ShowAsync("BagScreen");
            if (root == null) return;

            var back = UIManager.Find<Button>(root.transform, "btn_back");
            back.onClick.RemoveAllListeners();
            back.onClick.AddListener(() => ShowMain(true));

            var empty = root.transform.Find("empty_hint") != null ? root.transform.Find("empty_hint").gameObject : null;
            var list = root.transform.Find("list");
            if (empty != null) empty.SetActive(false);
            foreach (Transform child in list) Object.Destroy(child.gameObject);

            var chests = OnlineState.Chests;
            if (chests == null || chests.Count == 0)
            {
                if (empty != null) empty.SetActive(true);
                return;
            }

            float y = 340;
            foreach (JSONNode node in chests)
            {
                var chest = node;
                var config = GameConfig.Tables.TbChest.DataList.FirstOrDefault(x => x.Id == chest["configId"].AsInt);
                if (config == null) continue;
                BuildBagRow(list, y, chest, config);
                y -= 112;
            }
        }

        private static void BuildBagRow(Transform list, float y, JSONNode chest, cfg.chest.Chest config)
        {
            bool listed = chest["state"].AsInt == 1;
            long ev = (long)chest["expectedValue"].AsDouble;
            string skillNote = listed ? "" : (chest["skillUsedByMe"].AsBool ? "（我已用）" : "");
            string info = "<color=#" + config.ColorHex + ">" + config.Name + "</color>   "
                + (listed ? "[拍卖中]  " : "") + "剩 " + chest["remainingCount"].AsInt + " 格 · EV " + ev.ToString("N0")
                + " · 技能 " + chest["skillUsesRemaining"].AsInt + "/3" + skillNote;

            var row = UITheme.CreatePanel(list, "panel_dark", new Vector2(0, y), new Vector2(1500, 96));
            var infoText = TDUIHelper.MakeText(row.transform, new Vector2(20, 0), TextAnchor.MiddleLeft, 24);
            infoText.text = info;
            var infoRt = infoText.rectTransform;
            infoRt.anchorMin = infoRt.anchorMax = infoRt.pivot = new Vector2(0, 0.5f);
            infoRt.anchoredPosition = new Vector2(20, 0);
            TDUIHelper.SetSize(infoRt, 980, 90);

            if (!listed)
            {
                long uid = chest["uid"].AsLong;
                var openBtn = UITheme.ThemedButton(row.transform, new Vector2(640, 0), new Vector2(200, 64), "开 宝 箱", "gold", 24);
                openBtn.onClick.AddListener(() => ChestBoard.Show(uid));

                var listBtn = UITheme.ThemedButton(row.transform, new Vector2(870, 0), new Vector2(180, 64), "挂 拍", "blue", 24);
                listBtn.onClick.AddListener(() => AuctionPanel.OpenListDialog(chest));
            }
        }

        // ---------------- 排行榜 ----------------

        public static async void ShowRank()
        {
            var root = await UIManager.ShowAsync("RankScreen");
            if (root == null) return;

            var back = UIManager.Find<Button>(root.transform, "btn_back");
            back.onClick.RemoveAllListeners();
            back.onClick.AddListener(() => ShowMain());

            var r = await ApiClient.GetRankAsync();
            if (r == null || !r["ok"].AsBool)
            {
                UIRoot.ToastStatic("获取排行失败");
                return;
            }
            var list = root.transform.Find("list");
            foreach (Transform child in list) Object.Destroy(child.gameObject);
            float y = -30;
            int rank = 1;
            foreach (JSONNode rowNode in r["data"])
            {
                var line = TDUIHelper.MakeText(list, new Vector2(0, y), TextAnchor.MiddleLeft, 26);
                bool isMe = rowNode["isMe"].AsBool;
                line.text = "  第 " + rank + " 名    " + rowNode["name"] + "    " + rowNode["gold"].AsLong.ToString("N0");
                line.color = isMe ? UITheme.GoldLight : (rank <= 3 ? UITheme.Gold : Color.white);
                var rt = line.rectTransform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
                TDUIHelper.SetSize(rt, 900, 50);
                y -= 64;
                rank++;
            }
        }

        // ---------------- 错误码转文案 ----------------

        public static string ErrText(JSONNode r)
        {
            if (r == null) return "网络错误";
            string err = r["error"].Value;
            if (err == "NOT_ENOUGH_GOLD") return "金币不足";
            if (err == "ERR_QUOTA") return "宝箱技能额度已耗尽";
            if (err == "ERR_PLAYER_USED") return "你对本宝箱已用过技能";
            if (err == "ERR_NO_TARGET") return "没有低于平均值的选项";
            if (err == "ALREADY_TOP") return "你已是最高出价";
            if (err == "OWN_LOT") return "不能竞拍自己的宝箱";
            if (err == "AUCTION_ENDED") return "拍卖已结束";
            if (string.IsNullOrEmpty(err)) return "未知错误";
            return err;
        }
    }
}
