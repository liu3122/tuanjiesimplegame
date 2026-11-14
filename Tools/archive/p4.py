# -*- coding: utf-8 -*-
"""客户端 p4：Screens（商城动态卡/大厅第五卡/每日挑战面板）+ 烘焙脚本改造"""
import io

# ============ Screens.cs ============
p = r'G:\SimpleProject\Assets\HotUpdateScripts\ChestGame\Screens.cs'
s = io.open(p, encoding='utf-8').read()

# 1) 大厅：第五卡（每日挑战）
old = '''            Bind(root, "card_shop", () => ShowShop());
            Bind(root, "card_bag", () => ShowBag());
            Bind(root, "card_auction", () => AuctionPanel.Show());
            Bind(root, "card_rank", () => ShowRank());'''
new = '''            Bind(root, "card_shop", () => ShowShop());
            Bind(root, "card_bag", () => ShowBag());
            Bind(root, "card_auction", () => AuctionPanel.Show());
            Bind(root, "card_rank", () => ShowRank());
            Bind(root, "card_daily", () => ShowDaily());'''
assert old in s
s = s.replace(old, new)

# 2) 商城：动态卡片（配置驱动，支持 6+ 箱）
old2 = '''        public static async void ShowShop()
        {
            UITheme.PreloadAtlas("shop");
            var root = await UIManager.ShowAsync("ShopScreen");
            if (root == null) return;

            var back = UIManager.Find<Button>(root, "btn_back");
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
        }'''
new2 = '''        public static async void ShowShop()
        {
            UITheme.PreloadAtlas("shop");
            var root = await UIManager.ShowAsync("ShopScreen");
            if (root == null) return;

            var back = UIManager.Find<Button>(root, "btn_back");
            back.onClick.RemoveAllListeners();
            back.onClick.AddListener(() => ShowMain(true));

            var list = root.transform.Find("list");
            foreach (Transform child in list) Object.Destroy(child.gameObject);

            var chests = GameConfig.Tables.TbChest.DataList;
            float spacing = 470f;
            float startX = -(chests.Count - 1) * spacing / 2f;
            for (int i = 0; i < chests.Count; i++)
            {
                var c = chests[i];
                var config = c;
                long ev = 0; int max = 0;
                foreach (var a in c.SlotAmounts) { ev += a; if (a > max) max = a; }
                ev /= c.SlotAmounts.Count;

                string kindNote = "";
                if (c.Kind == 1) kindNote = "\\n<color=#D4885A>双头奖箱</color>";
                else if (c.Kind == 2) kindNote = "\\n<color=#7ED957>幸运格排除 +" + c.SpecialValue + "</color>";
                else if (c.Kind == 3) kindNote = "\\n<color=#E05D5D>陷阱格排除 -" + c.SpecialValue + "</color>";

                var card = UITheme.CreatePanel(list, "panel_dark", new Vector2(startX + i * spacing, -40), new Vector2(440, 560));
                var icoGo = new GameObject("chest_icon").AddComponent<Image>();
                icoGo.transform.SetParent(card.transform, false);
                var icon = UITheme.Load("chest_" + (c.Id <= 3 ? (c.Id == 1 ? "wood" : c.Id == 2 ? "silver" : "gold") : "gold"));
                if (icon != null) icoGo.sprite = icon;
                var irt = icoGo.rectTransform;
                irt.anchorMin = irt.anchorMax = irt.pivot = new Vector2(0.5f, 1f);
                irt.anchoredPosition = new Vector2(0, -14);
                irt.sizeDelta = new Vector2(140, 140);

                var name = TDUIHelper.MakeText(card.transform, new Vector2(0, -160), TextAnchor.MiddleCenter, 30);
                name.text = "<color=#" + c.ColorHex + ">" + c.Name + "</color>";
                TDUIHelper.SetSize(name.rectTransform, 420, 46);
                var desc = TDUIHelper.MakeText(card.transform, new Vector2(0, -250), TextAnchor.UpperCenter, 22);
                desc.color = new Color(0.78f, 0.82f, 0.86f);
                desc.text = "期望 " + ev + " · 最大 " + max.ToString("N0") + kindNote + "\\n20 格金额池";
                TDUIHelper.SetSize(desc.rectTransform, 410, 150);

                var buy = UITheme.ThemedButton(card.transform, new Vector2(0, -230 + 430), new Vector2(280, 76), "购买 " + c.Price.ToString("N0"), "green", 28);
                buy.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, 200);
                buy.onClick.AddListener(async () =>
                {
                    buy.interactable = false;
                    var r = await ApiClient.BuyChestAsync(config.Id);
                    buy.interactable = true;
                    if (r != null && r["ok"].AsBool)
                    {
                        UIRoot.ToastStatic("已购买 " + config.Name + "，到背包查看");
                        UIRoot.Instance.RefreshTop();
                    }
                    else
                    {
                        UIRoot.ToastStatic("购买失败：" + ErrText(r));
                    }
                });
            }
        }

        // ---------------- 每日挑战 ----------------

        public static async void ShowDaily()
        {
            var root = await UIManager.ShowAsync("DailyScreen");
            if (root == null) return;

            var back = UIManager.Find<Button>(root, "btn_back");
            back.onClick.RemoveAllListeners();
            back.onClick.AddListener(() => ShowMain());

            var cost = GameConfig.Tables.TbGlobal.DailyTicketCost;
            var info = UIManager.Find<Text>(root, "info");
            info.text = "全服同一个宝箱（结构完全相同）\\n比谁最终卖得更高\\n\\n门票 " + cost + " 金币 · 每天 " + GameConfig.Tables.TbGlobal.DailyAttemptsPerDay + " 次\\n成绩 = 出售/开箱最终所得";

            var buy = UIManager.Find<Button>(root, "btn_buy");
            var buyText = buy.GetComponentInChildren<Text>();
            buy.onClick.RemoveAllListeners();
            buy.onClick.AddListener(async () =>
            {
                buy.interactable = false;
                var r = await ApiClient.BuyDailyAsync();
                buy.interactable = true;
                if (r == null || !r["ok"].AsBool)
                {
                    UIRoot.ToastStatic("购买失败：" + ErrText(r));
                    return;
                }
                UIRoot.ToastStatic("挑战箱已放入背包（剩余 " + r["data"]["attemptsLeft"].AsInt + " 次）");
                UIRoot.Instance.RefreshTop();
                await OnlineState.Refresh();
                ChestBoard.Show(r["data"]["chestUid"].AsLong);
            });

            var list = root.transform.Find("list");
            var r2 = await ApiClient.GetDailyRankAsync();
            if (r2 == null || !r2["ok"].AsBool) return;
            foreach (Transform child in list) Object.Destroy(child.gameObject);
            var mine = r2["data"]["mine"];
            var header = UIManager.Find<Text>(root, "my_best");
            header.text = mine != null && !mine.IsNull
                ? "我的今日最好成绩：" + mine.AsLong.ToString("N0") + "（剩余次数 " + r2["data"]["attemptsLeft"].AsInt + "）"
                : "今天还没挑战（剩余次数 " + r2["data"]["attemptsLeft"].AsInt + "）";
            float y = 0;
            int rank = 1;
            foreach (JSONNode rowNode in r2["data"]["list"])
            {
                var line = TDUIHelper.MakeText(list, new Vector2(0, y), TextAnchor.MiddleLeft, 26);
                bool isMe = rowNode["isMe"].AsBool;
                line.text = "  第 " + rank + " 名    " + rowNode["name"] + "    " + rowNode["score"].AsLong.ToString("N0");
                line.color = isMe ? UITheme.GoldLight : (rank <= 3 ? UITheme.Gold : Color.white);
                var rt = line.rectTransform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
                TDUIHelper.SetSize(rt, 700, 46);
                y -= 56;
                rank++;
            }
        }'''
assert old2 in s
s = s.replace(old2, new2)
io.open(p, 'w', encoding='utf-8', newline='\n').write(s)
print('Screens ok')

# ============ 烘焙脚本 ============
p3 = r'G:\SimpleProject\Assets\Editor\BakeUIPrefabs.cs'
s3 = io.open(p3, encoding='utf-8').read()

# BakeMain：五卡（3+2 布局）
old3 = '''        string[,] cards = { { "icon_shop", "card_shop", "商 城" }, { "icon_bag", "card_bag", "背 包" }, { "icon_auction", "card_auction", "拍卖行" }, { "icon_rank", "card_rank", "排行榜" } };
        for (int i = 0; i < 4; i++)
        {
            int x = (i % 2) * 480 - 240;
            int y = -(i / 2) * 300 + 60;'''
new3 = '''        string[,] cards = { { "icon_shop", "card_shop", "商 城" }, { "icon_bag", "card_bag", "背 包" }, { "icon_auction", "card_auction", "拍卖行" }, { "icon_rank", "card_rank", "排行榜" }, { "icon_coin", "card_daily", "每日挑战" } };
        for (int i = 0; i < 5; i++)
        {
            int x = (i % 3) * 480 - 480;
            int y = -(i / 3) * 300 + 130;'''
assert old3 in s3
s3 = s3.replace(old3, new3)

# BakeShop：改为容器（卡片运行时生成）
old4 = s3[s3.index('    private static void BakeShop()'):s3.index('    private static void BakeBag()')]
new4 = '''    private static void BakeShop()
    {
        var root = Root("ShopScreen");
        Header(root, "商 城");
        var list = new GameObject("list");
        list.transform.SetParent(root.transform, false);
        var lrt = list.AddComponent<RectTransform>();
        lrt.anchorMin = lrt.anchorMax = lrt.pivot = new Vector2(0.5f, 0.5f);
        lrt.anchoredPosition = Vector2.zero;
        lrt.sizeDelta = new Vector2(1900, 700);
        Save(root);
    }

'''
s3 = s3.replace(old4, new4)

# BakeDaily：新增
old5 = '''    private static void BakeRank()'''
new5 = '''    private static void BakeDaily()
    {
        var root = Root("DailyScreen");
        Header(root, "每日挑战");
        var panel = Panel(root.transform, "info_panel", new Vector2(-380, 0), new Vector2(700, 560), "panel_gold");
        var info = MakeText(panel.transform, "info", new Vector2(0, -30), 640, 320, "", 26, TextAnchor.UpperLeft);
        info.color = Color.white;
        Button(panel.transform, "btn_buy", new Vector2(0, -220), new Vector2(380, 80), "购买挑战箱", "green", 28);
        var rankPanel = Panel(root.transform, "rank_panel", new Vector2(440, 0), new Vector2(800, 560), "panel_dark");
        var hb = MakeText(rankPanel.transform, "my_best", new Vector2(0, -30), 740, 40, "", 24, TextAnchor.UpperLeft);
        hb.color = UIThemeGold();
        var list = new GameObject("list");
        list.transform.SetParent(rankPanel.transform, false);
        var lrt = list.AddComponent<RectTransform>();
        lrt.anchorMin = lrt.anchorMax = lrt.pivot = new Vector2(0.5f, 1f);
        lrt.anchoredPosition = new Vector2(0, -90);
        lrt.sizeDelta = new Vector2(740, 440);
        Save(root);
    }

    private static Color UIThemeGold() => new Color(0.96f, 0.82f, 0.35f);

    private static void BakeRank()'''
assert old5 in s3
s3 = s3.replace(old5, new5)

# 注册 BakeDaily
s3 = s3.replace('BakeRank();\n        BakeAuction();', 'BakeRank();\n        BakeAuction();\n        BakeDaily();')
# BakeRank 面板皮肤（Panel 函数引用检查：BakeRank 原来没有 Panel 用法——ok）
io.open(p3, 'w', encoding='utf-8', newline='\n').write(s3)
print('Bake ok')
