using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI 界面烘焙（竖版 1080×1920）：把界面框架按命名约定构建并保存为 prefab（Assets/AssetRaw/Prefab/UI/）。
/// 运行时由热更侧 UIManager 经 YooAsset 按名字加载，控件节点名 = 代码 Find 的键。
/// 布局调整后重新执行菜单 Build/UI/Bake UI Prefabs。
/// </summary>
public static class BakeUIPrefabs
{
    private const string OutDir = "Assets/AssetRaw/Prefab/UI";

    private static Sprite Sprite(string name)
    {
        var sp = AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/AssetRaw/UI/{name}.png");
        return sp;
    }

    private static GameObject Root(string name)
    {
        var go = new GameObject(name);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        // 全局装饰背景（深蓝渐变 + 金色角标）
        var bgGo = new GameObject("bg");
        bgGo.transform.SetParent(go.transform, false);
        var bgImg = bgGo.AddComponent<Image>();
        bgImg.sprite = Sprite("bg_main");
        var brt = bgImg.rectTransform;
        brt.anchorMin = Vector2.zero;
        brt.anchorMax = Vector2.one;
        brt.offsetMin = Vector2.zero;
        brt.offsetMax = Vector2.zero;
        return go;
    }

    private static T Node<T>(Transform parent, string name, Vector2 pos, Vector2 size) where T : Component
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var img = go.AddComponent<Image>();
        img.raycastTarget = typeof(T) == typeof(Image) || typeof(T) == typeof(Button);
        return go.GetComponent<T>() ?? go.AddComponent<T>();
    }

    private static Text Text(Transform parent, string name, Vector2 pos, Vector2 size, string content, int fontSize, TextAnchor align = TextAnchor.MiddleCenter)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<Text>();
        t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        t.fontSize = fontSize;
        t.alignment = align;
        t.supportRichText = true;
        t.color = Color.white;
        t.text = content;
        var rt = t.rectTransform;
        bool left = align == TextAnchor.UpperLeft;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(left ? 0f : 0.5f, 1f);
        rt.anchoredPosition = pos;
        return t;
    }

    private static void SetSize(RectTransform rt, float w, float h)
    {
        rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, w);
        rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, h);
    }

    private static Text MakeText(Transform parent, string name, Vector2 pos, float w, float h, string content, int fontSize, TextAnchor align = TextAnchor.MiddleCenter)
    {
        var t = Text(parent, name, pos, Vector2.zero, content, fontSize, align);
        SetSize(t.rectTransform, w, h);
        return t;
    }

    private static Button Button(Transform parent, string name, Vector2 pos, Vector2 size, string label, string skin, int fontSize = 26)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.type = Image.Type.Sliced;
        img.sprite = Sprite("btn_" + skin);
        var btn = go.AddComponent<Button>();
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var t = MakeText(go.transform, "label", Vector2.zero, size.x, size.y, label, fontSize);
        t.rectTransform.anchorMin = Vector2.zero;
        t.rectTransform.anchorMax = Vector2.one;
        t.rectTransform.offsetMin = Vector2.zero;
        t.rectTransform.offsetMax = Vector2.zero;
        return btn;
    }

    private static GameObject Panel(Transform parent, string name, Vector2 pos, Vector2 size, string skin)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.type = Image.Type.Sliced;
        img.sprite = Sprite(skin);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return go;
    }

    /// <summary>各界面通用的顶部导航条：金色标题 + 金线 + 右上角醒目返回按钮</summary>
    private static void Header(GameObject root, string title)
    {
        Panel(root.transform, "topbar", new Vector2(0, -34), new Vector2(1080, 96), "panel_dark");
        MakeText(root.transform, "title", new Vector2(0, -14), 700, 64, title, 44).color = new Color(0.96f, 0.82f, 0.35f);
        var lineGo = new GameObject("goldline");
        lineGo.transform.SetParent(root.transform, false);
        var lineImg = lineGo.AddComponent<Image>();
        lineImg.sprite = Sprite("panel_gold");
        lineImg.type = Image.Type.Sliced;
        lineImg.color = new Color(1f, 1f, 1f, 0.5f);
        var lrt = lineImg.GetComponent<RectTransform>();
        lrt.anchorMin = lrt.anchorMax = lrt.pivot = new Vector2(0.5f, 1f);
        lrt.anchoredPosition = new Vector2(0, -84);
        lrt.sizeDelta = new Vector2(1040, 8);
        Button(root.transform, "btn_back", new Vector2(410, -48), new Vector2(200, 64), "\u25C0 返回", "red", 22);
    }

    [MenuItem("Build/UI/Bake UI Prefabs", priority = 200)]
    public static void BakeAll()
    {
        // 防误覆盖：手摆 prefab 之后此菜单会整体重置 UI，必须显式确认
        if (!EditorUtility.DisplayDialog("重新烘焙全部 UI Prefab",
                "将用代码默认布局覆盖 AssetRaw/Prefab/UI 下全部 prefab。\n\n如果已经手摆过 prefab，这会丢掉全部手工修改！\n（手摆流程请改用 Build/UI/Validate UI Prefabs 校验）",
                "覆盖烘焙", "取消"))
        {
            return;
        }
        Directory.CreateDirectory(OutDir);
        BakeLogin();
        BakeMain();
        BakeShop();
        BakeBag();
        BakeChestBoard();
        BakeAuction();
        BakeRank();
        BakeDaily();
        BakeRush();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[BakeUI] 全部界面 prefab 已生成 -> {OutDir}");
    }

    private static void Save(GameObject go)
    {
        string path = $"{OutDir}/{go.name}.prefab";
        PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        Debug.Log("[BakeUI] " + path);
    }

    // ---------------- 各界面（竖版布局：宽 ≤1040，纵向堆叠） ----------------

    private static void BakeLogin()
    {
        var root = Root("LoginScreen");
        MakeText(root.transform, "title", new Vector2(0, -120), 900, 110, "<color=#F5D061>宝 箱 商 人</color>", 64);
        MakeText(root.transform, "sub", new Vector2(0, -240), 900, 40, "排除 · 博弈 · 拍卖 · 抢宝", 24).color = new Color(0.62f, 0.68f, 0.78f);

        var heroGo = new GameObject("hero");
        heroGo.transform.SetParent(root.transform, false);
        var heroImg = heroGo.AddComponent<Image>();
        heroImg.sprite = Sprite("chest_hero");
        var hrt = heroImg.rectTransform;
        hrt.anchorMin = hrt.anchorMax = hrt.pivot = new Vector2(0.5f, 0.5f);
        hrt.anchoredPosition = new Vector2(0, 430);
        hrt.sizeDelta = new Vector2(400, 400);

        var inputGo = new GameObject("input_name");
        inputGo.transform.SetParent(root.transform, false);
        var img = inputGo.AddComponent<Image>();
        img.color = new Color(0.1f, 0.12f, 0.16f, 0.95f);
        var input = inputGo.AddComponent<InputField>();
        var irt = inputGo.GetComponent<RectTransform>();
        irt.anchorMin = irt.anchorMax = irt.pivot = new Vector2(0.5f, 0.5f);
        irt.anchoredPosition = new Vector2(0, -110);
        irt.sizeDelta = new Vector2(480, 66);
        var phGo = new GameObject("placeholder");
        phGo.transform.SetParent(inputGo.transform, false);
        var ph = phGo.AddComponent<Text>();
        ph.font = Resources.GetBuiltinResource<Font>("Arial.ttf"); ph.fontSize = 24; ph.color = Color.gray;
        ph.alignment = TextAnchor.MiddleCenter; ph.text = "输入你的名字，开始冒险";
        ph.rectTransform.anchorMin = Vector2.zero; ph.rectTransform.anchorMax = Vector2.one;
        ph.rectTransform.offsetMin = Vector2.zero; ph.rectTransform.offsetMax = Vector2.zero;
        var textGo = new GameObject("text");
        textGo.transform.SetParent(inputGo.transform, false);
        var txt = textGo.AddComponent<Text>();
        txt.font = Resources.GetBuiltinResource<Font>("Arial.ttf"); txt.fontSize = 26; txt.color = Color.white;
        txt.alignment = TextAnchor.MiddleLeft;
        txt.rectTransform.anchorMin = Vector2.zero; txt.rectTransform.anchorMax = Vector2.one;
        txt.rectTransform.offsetMin = new Vector2(18, 0); txt.rectTransform.offsetMax = Vector2.zero;
        input.placeholder = ph;
        input.textComponent = txt;
        input.contentType = InputField.ContentType.Standard;

        Button(root.transform, "btn_login", new Vector2(0, -240), new Vector2(400, 76), "开 始 冒 险", "green", 32);
        MakeText(root.transform, "hint", new Vector2(0, -320), 1000, 36, "先启动服务器：cd Server && dotnet run --urls http://127.0.0.1:5188", 18).color = new Color(0.45f, 0.5f, 0.58f);
        Save(root);
    }

    private static void BakeMain()
    {
        var root = Root("MainScreen");
        MakeText(root.transform, "title", new Vector2(0, -120), 900, 70, "<color=#F5D061>主 大 厅</color>", 44);

        string[,] cards = { { "icon_shop", "card_shop", "商 城" }, { "icon_bag", "card_bag", "背 包" }, { "icon_auction", "card_auction", "拍卖行" }, { "icon_rank", "card_rank", "排行榜" }, { "icon_coin", "card_daily", "每日挑战" }, { "chest_gold", "card_rush", "抢宝房" } };
        for (int i = 0; i < 6; i++)
        {
            // 竖版 2 列 × 3 行
            int x = (i % 2) * 480 - 240;
            int y = 340 - (i / 2) * 320;
            var card = Panel(root.transform, cards[i, 1], new Vector2(x, y), new Vector2(440, 260), "panel_card");
            var iconGo = new GameObject("icon");
            iconGo.transform.SetParent(card.transform, false);
            var icon = iconGo.AddComponent<Image>();
            icon.sprite = Sprite(cards[i, 0]);
            var irt = icon.rectTransform;
            irt.anchorMin = irt.anchorMax = irt.pivot = new Vector2(0.5f, 1f);
            irt.anchoredPosition = new Vector2(0, -18);
            irt.sizeDelta = new Vector2(116, 116);
            MakeText(card.transform, "label", new Vector2(0, -170), 420, 60, cards[i, 2], 34);
            card.AddComponent<Button>();
        }
        Save(root);
    }

    private static void BakeShop()
    {
        var root = Root("ShopScreen");
        Header(root, "商 城");
        var list = new GameObject("list");
        list.transform.SetParent(root.transform, false);
        var lrt = list.AddComponent<RectTransform>();
        lrt.anchorMin = lrt.anchorMax = lrt.pivot = new Vector2(0.5f, 0.5f);
        lrt.anchoredPosition = new Vector2(0, -40);
        lrt.sizeDelta = new Vector2(1040, 1780);
        Save(root);
    }

    private static void BakeBag()
    {
        var root = Root("BagScreen");
        Header(root, "背 包");
        MakeText(root.transform, "empty_hint", new Vector2(0, 0), 900, 50, "背包空空如也，去商城买一个宝箱吧", 26).color = new Color(0.6f, 0.65f, 0.7f);
        var list = new GameObject("list");
        list.transform.SetParent(root.transform, false);
        var lrt = list.AddComponent<RectTransform>();
        lrt.anchorMin = lrt.anchorMax = lrt.pivot = new Vector2(0.5f, 0.5f);
        lrt.anchoredPosition = Vector2.zero;
        lrt.sizeDelta = new Vector2(1000, 1700);
        Save(root);
    }

    private static void BakeChestBoard()
    {
        var root = Root("ChestBoardScreen");
        Header(root, "开 宝 箱");

        // 20 格棋盘（上方）：5×4，格子 150×110
        const float cw = 150, ch = 110, gap = 12;
        for (int i = 0; i < 20; i++)
        {
            int col = i % 5, row = i / 5;
            var cellGo = new GameObject("slot_" + i);
            cellGo.transform.SetParent(root.transform, false);
            var img = cellGo.AddComponent<Image>();
            img.type = Image.Type.Sliced;
            img.sprite = Sprite("slot_locked");
            var btn = cellGo.AddComponent<Button>();
            var rt = cellGo.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(-405 + col * (cw + gap), 430 - row * (ch + gap));
            rt.sizeDelta = new Vector2(cw, ch);
            var t = MakeText(cellGo.transform, "label", Vector2.zero, cw, ch, "?", 40);
            t.rectTransform.anchorMin = Vector2.zero;
            t.rectTransform.anchorMax = Vector2.one;
            t.rectTransform.offsetMin = Vector2.zero;
            t.rectTransform.offsetMax = Vector2.zero;
        }

        // 信息面板（棋盘下方，竖版）
        var panel = Panel(root.transform, "info_panel", new Vector2(0, -350), new Vector2(960, 560), "panel_gold");
        MakeText(panel.transform, "info", new Vector2(0, -30), 880, 250, "", 24, TextAnchor.UpperLeft);
        Button(panel.transform, "btn_sell", new Vector2(0, -320), new Vector2(420, 80), "", "red", 26);
        MakeText(panel.transform, "skill_hint", new Vector2(0, -380), 880, 30, "", 20).color = new Color(0.75f, 0.8f, 0.85f);
        Button(panel.transform, "btn_skills", new Vector2(0, -450), new Vector2(420, 84), "技 能", "blue", 28);
        Save(root);
    }

    private static void BakeRush()
    {
        var root = Root("RushScreen");
        Header(root, "抢 宝 房");

        var roomList = new GameObject("room_list");
        roomList.transform.SetParent(root.transform, false);
        var rl = roomList.AddComponent<RectTransform>();
        rl.anchorMin = rl.anchorMax = rl.pivot = new Vector2(0.5f, 0.5f);
        rl.anchoredPosition = new Vector2(0, -60);
        rl.sizeDelta = new Vector2(1000, 1700);

        var game = new GameObject("game_area");
        game.transform.SetParent(root.transform, false);
        var ga = game.AddComponent<RectTransform>();
        ga.anchorMin = Vector2.zero;
        ga.anchorMax = Vector2.one;
        ga.offsetMin = Vector2.zero;
        ga.offsetMax = Vector2.zero;
        MakeText(game.transform, "title", new Vector2(0, -110), 1000, 60, "", 38);
        MakeText(game.transform, "status", new Vector2(0, -180), 1000, 50, "", 30).color = new Color(0.9f, 0.9f, 0.95f);
        MakeText(game.transform, "feed", new Vector2(30, -640), 940, 620, "", 24, TextAnchor.UpperLeft).color = new Color(0.8f, 0.85f, 0.9f);

        const float cw = 150, ch = 110, gap = 12;
        for (int i = 0; i < 20; i++)
        {
            int col = i % 5, row = i / 5;
            var cellGo = new GameObject("slot_" + i);
            cellGo.transform.SetParent(game.transform, false);
            var img = cellGo.AddComponent<Image>();
            img.type = Image.Type.Sliced;
            img.sprite = Sprite("slot_locked");
            cellGo.AddComponent<Button>();
            var rt = cellGo.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(-324 + col * (cw + gap), 300 - row * (ch + gap));
            rt.sizeDelta = new Vector2(cw, ch);
            var t = MakeText(cellGo.transform, "label", Vector2.zero, cw, ch, "?", 34);
            t.rectTransform.anchorMin = Vector2.zero;
            t.rectTransform.anchorMax = Vector2.one;
            t.rectTransform.offsetMin = Vector2.zero;
            t.rectTransform.offsetMax = Vector2.zero;
        }
        Save(root);
    }

    private static void BakeDaily()
    {
        var root = Root("DailyScreen");
        Header(root, "每日挑战");
        // 竖版上下堆叠：信息卡（上）+ 日榜（下）
        var panel = Panel(root.transform, "info_panel", new Vector2(0, 560), new Vector2(960, 420), "panel_gold");
        var info = MakeText(panel.transform, "info", new Vector2(0, -30), 880, 220, "", 26, TextAnchor.UpperLeft);
        info.color = Color.white;
        Button(panel.transform, "btn_buy", new Vector2(0, -300), new Vector2(380, 80), "购买挑战箱", "green", 28);
        var rankPanel = Panel(root.transform, "rank_panel", new Vector2(0, -420), new Vector2(1000, 940), "panel_dark");
        var hb = MakeText(rankPanel.transform, "my_best", new Vector2(0, -30), 940, 40, "", 24, TextAnchor.UpperLeft);
        hb.color = new Color(0.96f, 0.82f, 0.35f);
        var list = new GameObject("list");
        list.transform.SetParent(rankPanel.transform, false);
        var lrt = list.AddComponent<RectTransform>();
        lrt.anchorMin = lrt.anchorMax = lrt.pivot = new Vector2(0.5f, 1f);
        lrt.anchoredPosition = new Vector2(0, -90);
        lrt.sizeDelta = new Vector2(940, 800);
        Save(root);
    }

    private static void BakeRank()
    {
        var root = Root("RankScreen");
        Header(root, "排行榜");
        MakeText(root.transform, "subtitle", new Vector2(0, -78), 1000, 36, "", 26).color = new Color(0.85f, 0.88f, 0.92f);

        // 竖版：四榜 2×2，其下全球/地区/分享一行，列表占满剩余
        string[] boards = { "tab_gold_total|现金总榜", "tab_gold_week|现金周榜", "tab_opens_total|开箱总榜", "tab_opens_week|开箱周榜" };
        for (int i = 0; i < boards.Length; i++)
        {
            var parts = boards[i].Split('|');
            float x = (i % 2) * 280 - 140;
            float y = -150 - (i / 2) * 68;
            Button(root.transform, parts[0], new Vector2(x, y), new Vector2(260, 56), parts[1], "blue", 20);
        }

        Button(root.transform, "tab_global", new Vector2(-300, -296), new Vector2(220, 56), "全球榜", "green", 22);
        Button(root.transform, "tab_region", new Vector2(-60, -296), new Vector2(220, 56), "我的地区", "green", 22);
        Button(root.transform, "btn_share", new Vector2(330, -296), new Vector2(200, 56), "分 享", "gold", 22);

        var list = new GameObject("list");
        list.transform.SetParent(root.transform, false);
        var lrt = list.AddComponent<RectTransform>();
        lrt.anchorMin = lrt.anchorMax = lrt.pivot = new Vector2(0.5f, 1f);
        lrt.anchoredPosition = new Vector2(0, -350);
        lrt.sizeDelta = new Vector2(1000, 1300);
        Save(root);
    }

    private static void BakeAuction()
    {
        var root = Root("AuctionScreen");
        Header(root, "拍 卖 行");

        // 竖版上下堆叠：拍品列表（上，占大）+ 详情/出价（下）
        var list = Panel(root.transform, "list_bg", new Vector2(0, 330), new Vector2(1000, 900), "panel_dark");
        var listGo = new GameObject("list");
        listGo.transform.SetParent(list.transform, false);
        var lrt = listGo.AddComponent<RectTransform>();
        lrt.anchorMin = lrt.anchorMax = lrt.pivot = new Vector2(0.5f, 0.5f);
        lrt.anchoredPosition = Vector2.zero;
        lrt.sizeDelta = new Vector2(980, 860);

        var detail = Panel(root.transform, "detail_panel", new Vector2(0, -580), new Vector2(1000, 700), "panel_gold");
        MakeText(detail.transform, "detail", new Vector2(0, -30), 900, 430, "", 24, TextAnchor.UpperLeft);
        Button(detail.transform, "btn_bid", new Vector2(0, -510), new Vector2(480, 80), "出 价", "green", 28);
        Button(detail.transform, "btn_buyout", new Vector2(0, -600), new Vector2(480, 70), "一口价", "gold", 26);
        Save(root);
    }
}
