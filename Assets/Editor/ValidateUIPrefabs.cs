using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI Prefab 绑定契约校验（手摆 prefab 时代的保险丝）。
/// 契约 = 运行时代码 UIManager.Find / Transform.Find 的节点路径 + 组件类型；本文件的契约表是唯一权威。
/// 用法：
///   菜单 Build/UI/Validate UI Prefabs —— 逐 prefab 校验节点与组件，缺失即报错（FAIL 会在控制台留 Error）；
///     同时反扫热更源码里的 Find 字面量，契约表未收录的新键给出警告（防止表过期）。
///   菜单 Build/UI/Export UI Contract —— 导出契约文档 Docs/UIContract.md（给摆界面的人当对账单）。
/// 新增界面/改节点名：先改这里的契约表，再动 prefab，跑一次校验。
/// </summary>
public static class ValidateUIPrefabs
{
    private const string PrefabDir = "Assets/AssetRaw/Prefab/UI";
    private const string HotUpdateDir = "Assets/HotUpdateScripts";
    private const string DocPath = "Docs/UIContract.md";

    private class Need
    {
        public string Path;
        public string Type;      // Button / Text / InputField / Transform
        public bool Optional;    // 代码里判空的节点（缺了只提示不报错）
    }

    // ---------------- 契约表（唯一权威） ----------------

    private static Dictionary<string, List<Need>> Contract()
    {
        var c = new Dictionary<string, List<Need>>();

        void Add(string screen, string path, string type, bool optional = false)
        {
            if (!c.TryGetValue(screen, out var list)) c[screen] = list = new List<Need>();
            list.Add(new Need { Path = path, Type = type, Optional = optional });
        }

        // 登录
        Add("LoginScreen", "input_name", "InputField");
        Add("LoginScreen", "btn_login", "Button");

        // 主大厅（卡片）
        Add("MainScreen", "card_shop", "Button");
        Add("MainScreen", "card_bag", "Button");
        Add("MainScreen", "card_auction", "Button");
        Add("MainScreen", "card_rank", "Button");
        Add("MainScreen", "card_daily", "Button");
        Add("MainScreen", "card_rush", "Button");

        // 商城 / 背包
        Add("ShopScreen", "btn_back", "Button");
        Add("ShopScreen", "list", "Transform");
        Add("BagScreen", "btn_back", "Button");
        Add("BagScreen", "list", "Transform");
        Add("BagScreen", "empty_hint", "Text", optional: true);

        // 开宝箱
        Add("ChestBoardScreen", "btn_back", "Button");
        for (int i = 0; i < 20; i++) Add("ChestBoardScreen", "slot_" + i, "Transform");
        Add("ChestBoardScreen", "info_panel", "Transform");
        Add("ChestBoardScreen", "info_panel/info", "Text");
        Add("ChestBoardScreen", "info_panel/btn_sell", "Button");
        Add("ChestBoardScreen", "info_panel/btn_sell/label", "Text");
        Add("ChestBoardScreen", "info_panel/skill_hint", "Text");
        Add("ChestBoardScreen", "info_panel/btn_skills", "Button");

        // 抢宝房
        Add("RushScreen", "btn_back", "Button");
        Add("RushScreen", "room_list", "Transform");
        Add("RushScreen", "game_area", "Transform");
        Add("RushScreen", "game_area/title", "Text");
        Add("RushScreen", "game_area/status", "Text");
        Add("RushScreen", "game_area/feed", "Text");
        for (int i = 0; i < 20; i++) Add("RushScreen", "game_area/slot_" + i, "Transform");

        // 排行榜
        Add("RankScreen", "btn_back", "Button");
        Add("RankScreen", "subtitle", "Text");
        Add("RankScreen", "list", "Transform");
        Add("RankScreen", "tab_gold_total", "Button");
        Add("RankScreen", "tab_gold_week", "Button");
        Add("RankScreen", "tab_opens_total", "Button");
        Add("RankScreen", "tab_opens_week", "Button");
        Add("RankScreen", "tab_global", "Button");
        Add("RankScreen", "tab_region", "Button");
        Add("RankScreen", "btn_share", "Button", optional: true);

        // 每日挑战
        Add("DailyScreen", "btn_back", "Button");
        Add("DailyScreen", "info_panel/info", "Text");
        Add("DailyScreen", "info_panel/btn_buy", "Button");
        Add("DailyScreen", "rank_panel/list", "Transform");
        Add("DailyScreen", "rank_panel/my_best", "Text");

        // 拍卖行
        Add("AuctionScreen", "btn_back", "Button");
        Add("AuctionScreen", "list_bg/list", "Transform");
        Add("AuctionScreen", "detail_panel/detail", "Text");
        Add("AuctionScreen", "detail_panel/btn_bid", "Button");
        Add("AuctionScreen", "detail_panel/btn_bid/label", "Text");
        Add("AuctionScreen", "detail_panel/btn_buyout", "Button");
        Add("AuctionScreen", "detail_panel/btn_buyout/label", "Text");

        return c;
    }

    // ---------------- 校验 ----------------

    [MenuItem("Build/UI/Validate UI Prefabs", priority = 201)]
    public static void Validate()
    {
        var contract = Contract();
        int errors = 0, checkedNodes = 0, optionalMissing = 0;
        var sb = new StringBuilder();

        foreach (var kv in contract)
        {
            string prefabPath = $"{PrefabDir}/{kv.Key}.prefab";
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (go == null)
            {
                sb.AppendLine($"[FAIL] 缺少 prefab：{prefabPath}");
                errors++;
                continue;
            }
            foreach (var need in kv.Value)
            {
                checkedNodes++;
                var t = go.transform.Find(need.Path);
                if (t == null)
                {
                    if (need.Optional)
                    {
                        sb.AppendLine($"[warn] {kv.Key}: 可选节点缺失 [{need.Path}]（代码已判空，功能会退化）");
                        optionalMissing++;
                    }
                    else
                    {
                        sb.AppendLine($"[FAIL] {kv.Key}: 缺少节点 [{need.Path}]（类型 {need.Type}）——手摆时节点名被改动？");
                        errors++;
                    }
                    continue;
                }
                if (!HasComponent(t, need.Type))
                {
                    sb.AppendLine($"[FAIL] {kv.Key}: 节点 [{need.Path}] 缺少组件 {need.Type}");
                    errors++;
                }
            }
        }

        // 反扫源码 Find 字面量：契约表过期预警
        var known = new HashSet<string>();
        foreach (var kv in contract)
        {
            foreach (var need in kv.Value) known.Add(need.Path);
        }
        foreach (var key in ScanCodeFindLiterals())
        {
            if (!known.Contains(key))
            {
                sb.AppendLine($"[warn] 代码发现契约表未收录的 Find 键：\"{key}\"（新增界面/节点？请更新 ValidateUIPrefabs 契约表）");
            }
        }

        string verdict = errors == 0 ? "PASS ✅" : "FAIL ❌";
        string report = $"[UIValidate] 契约节点 {checkedNodes} 个：错误 {errors}，可选缺失 {optionalMissing} → {verdict}\n{sb}";
        if (errors == 0) Debug.Log(report);
        else Debug.LogError(report);
        EditorUtility.DisplayDialog("UI Prefab 契约校验",
            errors == 0
                ? $"校验通过：{checkedNodes} 个契约节点全部就位。\n（可选缺失 {optionalMissing}，详见控制台）"
                : $"发现 {errors} 处问题，手摆 prefab 与代码契约不一致！\n详见控制台日志。", "确定");
    }

    private static bool HasComponent(Transform t, string type)
    {
        switch (type)
        {
            case "Button": return t.GetComponent<Button>() != null;
            case "Text": return t.GetComponent<Text>() != null;
            case "InputField": return t.GetComponent<InputField>() != null;
            default: return true; // Transform 天然存在
        }
    }

    /// <summary>扫描热更源码里的 Find 字面量（拼接前缀如 "slot_" + i 以末尾下划线识别并跳过）</summary>
    private static HashSet<string> ScanCodeFindLiterals()
    {
        var found = new HashSet<string>();
        var rx = new Regex("(?:UIManager\\.Find<[^>]+>\\([^,]+,|\\.Find\\(\\s*)\"([^\"]+)\"");
        foreach (var file in Directory.GetFiles(HotUpdateDir, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}Gen{Path.DirectorySeparatorChar}")) continue;
            string code;
            try { code = File.ReadAllText(file); } catch { continue; }
            foreach (Match m in rx.Matches(code))
            {
                string key = m.Groups[1].Value;
                if (key.EndsWith("_") || key.Length == 0) continue; // 拼接前缀（slot_ + i）
                found.Add(key);
            }
        }
        return found;
    }

    // ---------------- 契约文档导出 ----------------

    [MenuItem("Build/UI/Export UI Contract", priority = 202)]
    public static void ExportDoc()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# UI Prefab 绑定契约（由 ValidateUIPrefabs 导出，勿手改）");
        sb.AppendLine();
        sb.AppendLine("> 手摆/修改 prefab 时**必须保留**下列节点（路径相对界面根，含组件类型）。");
        sb.AppendLine("> 改名 = 运行时控件找不到；改完后跑一次 `Build/UI/Validate UI Prefabs`。");
        sb.AppendLine();
        foreach (var kv in Contract())
        {
            sb.AppendLine($"## {kv.Key}");
            sb.AppendLine();
            sb.AppendLine("| 节点路径 | 组件 | 必需 |");
            sb.AppendLine("|---|---|---|");
            foreach (var n in kv.Value)
            {
                sb.AppendLine($"| `{n.Path}` | {n.Type} | {(n.Optional ? "可选（代码判空）" : "必需")} |");
            }
            sb.AppendLine();
        }
        Directory.CreateDirectory(Path.GetDirectoryName(DocPath));
        File.WriteAllText(DocPath, sb.ToString());
        Debug.Log($"[UIContract] 契约文档已导出 -> {DocPath}");
        EditorUtility.DisplayDialog("契约导出", $"已写入 {DocPath}", "确定");
    }
}
