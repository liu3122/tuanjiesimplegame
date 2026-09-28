using System.Linq;
using System.Threading.Tasks;
using cfg;
using Luban.SimpleJSON;
using UnityEngine;
using UnityEngine.UI;

namespace Game.HotUpdate.ChestGame
{
    /// <summary>Screens 模块拆分（partial）：Rank</summary>
    public static partial class Screens
    {


        public static async void ShowRank()
        {
            var root = await UIManager.ShowAsync("RankScreen");
            if (root == null) return;

            var back = UIManager.Find<Button>(root.transform, "btn_back");
            back.onClick.RemoveAllListeners();
            back.onClick.AddListener(() => ShowMain());

            var shareBtn = root.transform.Find("btn_share") != null ? root.transform.Find("btn_share").GetComponent<Button>() : null;
            if (shareBtn != null)
            {
                shareBtn.onClick.RemoveAllListeners();
                shareBtn.onClick.AddListener(() => ShareBridge.Share("rank"));
            }

            // 四榜 tab
            var boards = new (string node, string board, string label)[]
            {
                ("tab_gold_total", "gold_total", L10n.Get("现金总榜")),
                ("tab_gold_week", "gold_week", L10n.Get("现金周榜")),
                ("tab_opens_total", "opens_total", L10n.Get("开箱总榜")),
                ("tab_opens_week", "opens_week", L10n.Get("开箱周榜")),
            };
            foreach (var b in boards)
            {
                var node = root.transform.Find(b.node);
                if (node == null) continue;
                var btn = node.GetComponent<Button>();
                var board = b.board;
                var label = node.GetComponentInChildren<Text>();
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => { _board = board; _ = RefreshRank(root); });
            }

            var tabGlobalGo = root.transform.Find("tab_global");
            var tabRegionGo = root.transform.Find("tab_region");
            if (tabGlobalGo == null || tabRegionGo == null)
            {
                UIRoot.ToastStatic(L10n.Get("界面 prefab 过旧：请执行菜单 Build/UI/Bake UI Prefabs"));
            }
            else
            {
                tabGlobalGo.GetComponent<Button>().onClick.RemoveAllListeners();
                tabGlobalGo.GetComponent<Button>().onClick.AddListener(() => { _scope = "global"; _ = RefreshRank(root); });
                tabRegionGo.GetComponent<Button>().onClick.RemoveAllListeners();
                tabRegionGo.GetComponent<Button>().onClick.AddListener(() => { _scope = "region"; _ = RefreshRank(root); });
            }

            // 对决天梯（代码追加的第五个页签）
            var ladderBtn = UITheme.ThemedButton(root.transform, new Vector2(560, 420), new Vector2(180, 52),
                L10n.Get("对决天梯"), "gold", 20);
            UITheme.SkinButton(ladderBtn, "blue");
            ladderBtn.name = "tab_duel_ladder";
            ladderBtn.onClick.AddListener(() => { _board = "duel_ladder"; _ = RefreshRank(root); });

            await RefreshRank(root);
        }

        private static async System.Threading.Tasks.Task RefreshRank(GameObject root)
        {
            // tab 高亮
            foreach (Transform t in root.transform)
            {
                if (!t.name.StartsWith("tab_")) continue;
                var img = t.GetComponent<Image>();
                if (img == null) continue;
                bool active = (t.name == "tab_" + _board) || (t.name == "tab_" + _scope);
                img.color = active ? new Color(0.5f, 0.45f, 0.2f, 0.95f) : new Color(0.2f, 0.3f, 0.45f, 0.9f);
            }

            if (_board == "duel_ladder")
            {
                var rl = await ApiClient.GetDuelRankAsync();
                if (rl == null || !rl["ok"].AsBool) { UIRoot.ToastStatic(L10n.Get("获取排行失败")); return; }
                var subtitleGo0 = root.transform.Find("subtitle");
                if (subtitleGo0 != null) subtitleGo0.GetComponent<Text>().text = L10n.Get("对决天梯") + " · Elo · " + L10n.Get("我") + " " + rl["data"]["myRating"].AsInt;
                var list0 = root.transform.Find("list");
                foreach (Transform child in list0) Object.Destroy(child.gameObject);
                float y0 = 0;
                int rank0 = 1;
                foreach (JSONNode rowNode in rl["data"]["list"])
                {
                    var line = TDUIHelper.MakeText(list0, new Vector2(0, y0), TextAnchor.MiddleLeft, 26);
                    bool isMe = rowNode["isMe"].AsBool;
                    line.text = "  第 " + rank0 + " 名    " + rowNode["name"].Value + "    " + rowNode["value"].AsLong;
                    line.color = isMe ? UITheme.GoldLight : (rank0 <= 3 ? UITheme.Gold : Color.white);
                    var rt0 = line.rectTransform;
                    rt0.anchorMin = rt0.anchorMax = rt0.pivot = new Vector2(0.5f, 1f);
                    TDUIHelper.SetSize(rt0, 860, 46);
                    y0 -= 56;
                    rank0++;
                }
                return;
            }

            var r = await ApiClient.GetRankAsync(_board, _scope);
            if (r == null || !r["ok"].AsBool)
            {
                UIRoot.ToastStatic(L10n.Get("获取排行失败"));
                return;
            }
            string boardName = _board.Contains("opens") ? L10n.Get("开箱次数") : L10n.Get("金币");
            string scopeName = _scope == "region" ? L10n.Get("地区榜（") + r["data"]["myRegion"].Value + "）" : L10n.Get("全球榜");
            var subtitleGo = root.transform.Find("subtitle");
            if (subtitleGo != null) subtitleGo.GetComponent<Text>().text = boardName + " · " + scopeName + SeasonSuffix();

            var list = root.transform.Find("list");
            foreach (Transform child in list) Object.Destroy(child.gameObject);
            float y = 0;
            int rank = 1;
            foreach (JSONNode rowNode in r["data"]["list"])
            {
                var line = TDUIHelper.MakeText(list, new Vector2(0, y), TextAnchor.MiddleLeft, 26);
                bool isMe = rowNode["isMe"].AsBool;
                line.text = "  第 " + rank + " 名    " + rowNode["name"] + "    " + rowNode["value"].AsLong.ToString("N0");
                line.color = isMe ? UITheme.GoldLight : (rank <= 3 ? UITheme.Gold : Color.white);
                var rt = line.rectTransform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
                TDUIHelper.SetSize(rt, 860, 46);
                y -= 56;
                rank++;
            }
            if (r["data"]["list"].Count == 0)
            {
                var empty = TDUIHelper.MakeText(list, new Vector2(0, 0), TextAnchor.MiddleCenter, 24);
                empty.color = new Color(0.6f, 0.65f, 0.7f);
                empty.text = L10n.Get("暂无上榜数据");
                TDUIHelper.SetSize(empty.rectTransform, 860, 46);
            }
        }

        // ---------------- 错误码转文案 ----------------

        /// <summary>赛季信息行（当前周 + 我最近一次奖励）</summary>
        private static string SeasonSuffix()
        {
            var d = OnlineState.State != null ? OnlineState.State["data"] : null;
            if (d == null || d["seasonWeek"].IsNull) return "";
            string week = d["seasonWeek"].Value;
            string s = "\n<color=#9AA3B2>" + L10n.Get("赛季周") + " " + (week.Length >= 10 ? week.Substring(0, 10) : week) + "</color>";
            var last = d["lastSeasonReward"];
            if (last != null && !last.IsNull)
            {
                s += "<color=#F5D061> · " + L10n.Get("上赛季第") + " " + last["rank"].AsInt + L10n.Get("名 奖励") + " +" + last["amount"].AsLong.ToString("N0") + "</color>";
            }
            return s;
        }
    }
}
