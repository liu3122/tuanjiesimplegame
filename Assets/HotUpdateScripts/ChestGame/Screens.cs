using System.Threading.Tasks;
using System.Linq;
using cfg;
using Luban.SimpleJSON;
using UnityEngine;
using UnityEngine.UI;

namespace Game.HotUpdate.ChestGame
{
    /// <summary>屏幕集：登录 / 主界面 / 商城 / 背包 / 排行榜</summary>
    public static class Screens
    {
        // ---------------- 登录 ----------------

        public static void BuildLogin(Transform root)
        {
            var title = TDUIHelper.MakeText(root, new Vector2(0, -140), TextAnchor.MiddleCenter, 56);
            title.text = "宝 箱 商 人";
            TDUIHelper.SetSize(title.rectTransform, 1200, 90);
            var sub = TDUIHelper.MakeText(root, new Vector2(0, -240), TextAnchor.MiddleCenter, 22);
            sub.color = new Color(0.7f, 0.75f, 0.8f);
            sub.text = "连接服务器（cd Server && dotnet run --urls http://127.0.0.1:5188）";
            TDUIHelper.SetSize(sub.rectTransform, 1400, 40);

            var input = TDUIHelper.MakeInputField(root, new Vector2(0, -60), new Vector2(460, 64), "输入你的名字");
            input.contentType = InputField.ContentType.Standard;

            var btn = TDUIHelper.MakeButton(root, new Vector2(0, -180), new Vector2(360, 72), "登 录");
            btn.onClick.AddListener(async () =>
            {
                btn.interactable = false;
                ApiClient.PlayerName = string.IsNullOrEmpty(input.text) ? "玩家" + Random.Range(100, 999) : input.text;
                if (await ApiClient.LoginAsync())
                {
                    UIRoot.ToastStatic($"欢迎，{ApiClient.PlayerName}！");
                    BuildMain(UIRoot.Instance.CanvasTransform);
                }
                else
                {
                    btn.interactable = true;
                    UIRoot.ToastStatic("登录失败：服务器未启动？");
                }
            });
        }

        // ---------------- 主界面 ----------------

        public static void BuildMain(Transform root)
        {
            var ui = UIRoot.Instance;

            var title = TDUIHelper.MakeText(root, new Vector2(0, -120), TextAnchor.MiddleCenter, 44);
            title.text = "主 大 厅";
            TDUIHelper.SetSize(title.rectTransform, 1200, 70);

            var cards = new (string label, System.Action open)[]
            {
                ("商 城\n购买宝箱", () => BuildShop(UIRoot.Instance.CanvasTransform)),
                ("背 包\n开宝箱 / 挂拍", () => BuildBag(UIRoot.Instance.CanvasTransform)),
                ("拍卖行\n玩家交易", () => AuctionPanel.Open(UIRoot.Instance.CanvasTransform)),
                ("排行榜\n金币排行", () => BuildRank(UIRoot.Instance.CanvasTransform)),
            };
            for (int i = 0; i < cards.Length; i++)
            {
                int x = (i % 2) * 480 - 240;
                int y = -(i / 2) * 300 + 60;
                var b = TDUIHelper.MakeButton(root, new Vector2(x, y), new Vector2(440, 260), cards[i].label);
                var open = cards[i].open;
                b.GetComponentInChildren<Text>().fontSize = 34;
                b.onClick.AddListener(() => open());
            }
            ui.RefreshTop();
        }

        // ---------------- 通用：标题栏（返回主界面） ----------------

        public static void BuildHeader(Transform root, string name)
        {
            var t = TDUIHelper.MakeText(root, new Vector2(0, -40), TextAnchor.MiddleCenter, 38);
            t.text = name;
            TDUIHelper.SetSize(t.rectTransform, 1200, 60);
            var back = TDUIHelper.MakeButton(root, new Vector2(820, -44), new Vector2(180, 60), "返回大厅");
            back.onClick.AddListener(() => BuildMain(UIRoot.Instance.CanvasTransform));
        }

        // ---------------- 商城 ----------------

        public static void BuildShop(Transform root)
        {
            BuildHeader(root, "商 城");

            var g = GameConfig.Tables;
            int count = g.TbChest.DataList.Count;
            float spacing = 480;
            float startX = -(count - 1) * spacing / 2f;
            for (int i = 0; i < count; i++)
            {
                var c = g.TbChest.DataList[i];
                long ev = 0; int max = 0;
                foreach (var a in c.SlotAmounts) { ev += a; if (a > max) max = a; }
                ev /= c.SlotAmounts.Count;

                var card = new GameObject("chestCard");
                card.transform.SetParent(root, false);
                var img = card.AddComponent<Image>();
                img.color = new Color(0.14f, 0.17f, 0.23f, 0.97f);
                var rt = card.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(startX + i * spacing, -60);
                rt.sizeDelta = new Vector2(440, 560);

                var n = TDUIHelper.MakeText(card.transform, new Vector2(0, -40), TextAnchor.MiddleCenter, 38);
                n.text = $"<color=#{c.ColorHex}>{c.Name}</color>";
                TDUIHelper.SetSize(n.rectTransform, 400, 60);
                var d = TDUIHelper.MakeText(card.transform, new Vector2(0, -130), TextAnchor.UpperCenter, 24);
                d.color = new Color(0.78f, 0.82f, 0.86f);
                d.text = $"期望 {ev}\n最大 {max:N0}\n20 格金额池";
                TDUIHelper.SetSize(d.rectTransform, 400, 160);

                var buy = TDUIHelper.MakeButton(card.transform, new Vector2(0, -220), new Vector2(280, 76), $"购买 {c.Price:N0}");
                buy.onClick.AddListener(async () =>
                {
                    buy.interactable = false;
                    var r = await ApiClient.BuyChestAsync(c.Id);
                    buy.interactable = true;
                    if (r != null && r["ok"].AsBool)
                    {
                        UIRoot.ToastStatic($"已购买 {c.Name}，到背包查看");
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

        public static void BuildBag(Transform root)
        {
            BuildHeader(root, "背 包");
            var chests = OnlineState.Chests;
            if (chests == null || chests.Count == 0)
            {
                var empty = TDUIHelper.MakeText(root, new Vector2(0, 0), TextAnchor.MiddleCenter, 26);
                empty.color = new Color(0.6f, 0.65f, 0.7f);
                empty.text = "背包空空如也，去商城买一个宝箱吧";
                TDUIHelper.SetSize(empty.rectTransform, 900, 50);
                return;
            }
            float y = -140;
            foreach (JSONNode node in chests)
            {
                var chest = node;
                var config = GameConfig.Tables.TbChest.DataList.FirstOrDefault(x => x.Id == chest["configId"].AsInt);
                if (config == null) continue;

                var row = new GameObject("chestRow");
                row.transform.SetParent(root, false);
                var img = row.AddComponent<Image>();
                img.color = new Color(0.16f, 0.2f, 0.28f, 0.95f);
                var rt = row.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(0, y);
                rt.sizeDelta = new Vector2(1500, 96);

                bool listed = chest["state"].AsInt == 1;
                long ev = (long)chest["expectedValue"].AsDouble;
                var info = TDUIHelper.MakeText(row.transform, new Vector2(20, 0), TextAnchor.MiddleLeft, 24);
                string skillNote = listed ? "" : (chest["skillUsedByMe"].AsBool ? "（我已用）" : "");
                info.text = "<color=#" + config.ColorHex + ">" + config.Name + "</color>   "
                    + (listed ? "[拍卖中]  " : "") + "剩 " + chest["remainingCount"].AsInt + " 格 · EV " + ev.ToString("N0")
                    + " · 技能 " + chest["skillUsesRemaining"].AsInt + "/3" + skillNote;
                var infoRt = info.rectTransform;
                infoRt.anchorMin = infoRt.anchorMax = infoRt.pivot = new Vector2(0, 0.5f);
                infoRt.anchoredPosition = new Vector2(20, 0);
                TDUIHelper.SetSize(infoRt, 980, 90);

                if (!listed)
                {
                    var openBtn = TDUIHelper.MakeButton(row.transform, new Vector2(640, 0), new Vector2(200, 64), "开 宝 箱");
                    openBtn.onClick.AddListener(() =>
                    {
                        ChestBoard.Open(UIRoot.Instance.CanvasTransform, chest["uid"].AsLong);
                    });

                    var listBtn = TDUIHelper.MakeButton(row.transform, new Vector2(870, 0), new Vector2(180, 64), "挂 拍");
                    listBtn.onClick.AddListener(() => AuctionPanel.OpenListDialog(chest));
                }
                y -= 112;
            }
        }

        // ---------------- 排行榜 ----------------

        public static async void BuildRank(Transform root)
        {
            BuildHeader(root, "排行榜（金币）");
            var r = await ApiClient.GetRankAsync();
            if (r == null || !r["ok"].AsBool)
            {
                UIRoot.ToastStatic("获取排行失败");
                return;
            }
            float y = -140;
            int rank = 1;
            foreach (JSONNode row in r["data"])
            {
                var line = TDUIHelper.MakeText(root, new Vector2(0, y), TextAnchor.MiddleLeft, 26);
                bool isMe = row["isMe"].AsBool;
                line.text = "  第 " + rank + " 名    " + row["name"] + "    " + row["gold"].AsLong.ToString("N0");
                line.color = isMe ? new Color(1f, 0.85f, 0.3f) : Color.white;
                var rt = line.rectTransform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                TDUIHelper.SetSize(rt, 900, 50);
                y -= 64;
                rank++;
            }
        }

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
