using System.Linq;
using System.Threading.Tasks;
using Luban.SimpleJSON;
using UnityEngine;
using UnityEngine.UI;

namespace Game.HotUpdate.ChestGame
{
    /// <summary>
    /// 宅邸改造面板（梦幻家园式金币指数回收）：6 区域 36 工程线性链，每工程二选一装饰方案，
    /// 区域完成给称号（UIRoot 顶栏名字前缀展示）。进度/选择服务端权威（state.manor），工程表本地 TbManor。
    /// v1 全代码 UI（区域卡片+工程进度），房间两态立绘留美术接入位（见美术需求总览）。
    /// 动工成功 → Destroy 重开刷新（ShowSign 模式）。
    /// </summary>
    public static class ManorPanel
    {
        public static async void Show()
        {
            if (!OnlineState.FeatureOn("manor"))
            {
                UIRoot.ToastStatic(L10n.Get("该玩法已关闭"));
                return;
            }
            await OnlineState.Refresh();
            var st = OnlineState.State != null && OnlineState.State["data"] != null ? OnlineState.State["data"]["manor"] : null;
            if (st == null || st.IsNull)
            {
                UIRoot.ToastStatic(L10n.Get("服务暂不可用，请稍后再试"));
                return;
            }
            int stage = st["stage"].AsInt;
            int total = st["total"].AsInt;
            var choices = st["choices"];

            var panel = UITheme.CreatePanel(UIRoot.Instance.CanvasTransform, "panel_gold", Vector2.zero, new Vector2(760, 1160));
            var rt = panel.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);

            var title = TDUIHelper.MakeText(panel.transform, new Vector2(0, -26), TextAnchor.MiddleCenter, 32);
            title.text = "<color=#F5D061>" + L10n.Get("宅 邸") + "</color>";
            TDUIHelper.SetSize(title.rectTransform, 720, 50);

            string curTitle = !st["title"].IsNull && !string.IsNullOrEmpty(st["title"].Value) ? st["title"].Value : null;
            var progress = TDUIHelper.MakeText(panel.transform, new Vector2(0, -76), TextAnchor.MiddleCenter, 22);
            progress.color = UITheme.TextSub;
            progress.text = (curTitle != null ? "<color=#F5D061>『" + L10n.Get(curTitle) + "』</color> · " : "")
                + L10n.Get("改造进度") + " " + stage + "/" + total;
            TDUIHelper.SetSize(progress.rectTransform, 700, 40);

            var rows = GameConfig.Tables.TbManor.DataList;
            var next = stage < total ? rows.First(m => m.Id == stage + 1) : null;

            float y = -116;   // 下一区域行顶部
            foreach (var g in rows.GroupBy(m => m.Area).OrderBy(g => g.Key))
            {
                string areaName = g.First().AreaName;
                int done = g.Count(m => m.Id <= stage);
                if (done >= g.Count())
                {
                    // 已完工区域：收起行（✓ + 区域称号）
                    var row = UITheme.CreatePanel(panel.transform, "panel_dark", new Vector2(0, y - 42), new Vector2(700, 84));
                    var txt = TDUIHelper.MakeText(row.transform, new Vector2(0, -22), TextAnchor.MiddleCenter, 22);
                    TDUIHelper.SetSize(txt.rectTransform, 660, 40);
                    string areaTitle = g.Last().Title;
                    txt.text = "<color=#7ED957>✓ " + L10n.Get(areaName) + "</color>" +
                               (string.IsNullOrEmpty(areaTitle) ? "" : " · 『" + L10n.Get(areaTitle) + "』");
                    y -= 100;
                }
                else if (next != null && g.Key == next.Area)
                {
                    y = BuildCurrentArea(panel, y, g, next, stage, choices, curTitle);
                }
                else
                {
                    // 未解锁区域：灰色
                    var row = UITheme.CreatePanel(panel.transform, "panel_dark", new Vector2(0, y - 42), new Vector2(700, 84));
                    var txt = TDUIHelper.MakeText(row.transform, new Vector2(0, -22), TextAnchor.MiddleCenter, 22);
                    TDUIHelper.SetSize(txt.rectTransform, 660, 40);
                    txt.color = new Color(0.62f, 0.66f, 0.72f);
                    txt.text = L10n.Get(areaName) + " · " + L10n.Get("未解锁");
                    y -= 100;
                }
            }

            var close = UITheme.ThemedButton(panel.transform, new Vector2(0, -1096), new Vector2(220, 54), L10n.Get("关 闭"), "red", 24);
            close.onClick.AddListener(() => Object.Destroy(panel));
        }

        /// <summary>当前区域展开卡：进度头 + 下一工程（费用/描述/二选一方案/动工）+ 最近已完成工程。返回下一可用 y</summary>
        private static float BuildCurrentArea(GameObject panel, float y, System.Collections.Generic.IEnumerable<cfg.manor.Manor> areaRows,
            cfg.manor.Manor next, int stage, JSONNode choices, string prevTitle)
        {
            var list = areaRows.OrderBy(m => m.Id).ToList();
            string areaName = list[0].AreaName;
            int done = list.Count(m => m.Id <= stage);

            var card = UITheme.CreatePanel(panel.transform, "panel_dark", new Vector2(0, y - 170), new Vector2(700, 340));

            var head = TDUIHelper.MakeText(card.transform, new Vector2(0, -14), TextAnchor.MiddleCenter, 24);
            TDUIHelper.SetSize(head.rectTransform, 660, 40);
            head.text = "<color=#F5D061>" + L10n.Get(areaName) + "</color> · " + done + "/" + list.Count;

            var pname = TDUIHelper.MakeText(card.transform, new Vector2(0, -58), TextAnchor.MiddleCenter, 24);
            TDUIHelper.SetSize(pname.rectTransform, 660, 40);
            pname.text = next.Name + " <color=#F5D061>(" + next.Cost.ToString("N0") + " " + L10n.Get("金币") + ")</color>";

            var pdesc = TDUIHelper.MakeText(card.transform, new Vector2(0, -96), TextAnchor.MiddleCenter, 18);
            TDUIHelper.SetSize(pdesc.rectTransform, 660, 30);
            pdesc.color = UITheme.TextSub;
            pdesc.text = next.Desc;

            // 最近 2 个已完成工程（小字：工程名 → 所选方案）
            var doneList = list.Where(m => m.Id <= stage).ToList();
            if (doneList.Count > 0)
            {
                var recent = doneList.Skip(Mathf.Max(0, doneList.Count - 2)).ToList();
                var hist = TDUIHelper.MakeText(card.transform, new Vector2(0, -122), TextAnchor.MiddleCenter, 16);
                TDUIHelper.SetSize(hist.rectTransform, 660, 20 * recent.Count + 8);
                hist.color = new Color(0.55f, 0.6f, 0.66f);
                hist.text = string.Join("\n", recent.Select(m =>
                    m.Name + " → " + (choices[m.Id.ToString()].AsInt == 2 ? m.OptionB : m.OptionA)));
            }

            // 二选一装饰方案（选中变绿）
            int[] sel = { 0 };
            var optA = UITheme.ThemedButton(card.transform, new Vector2(-172, -226), new Vector2(320, 52),
                "A · " + next.OptionA, "blue", 19);
            var optB = UITheme.ThemedButton(card.transform, new Vector2(172, -226), new Vector2(320, 52),
                "B · " + next.OptionB, "blue", 19);
            optA.onClick.AddListener(() => { sel[0] = 1; UITheme.SkinButton(optA, "green"); UITheme.SkinButton(optB, "blue"); });
            optB.onClick.AddListener(() => { sel[0] = 2; UITheme.SkinButton(optB, "green"); UITheme.SkinButton(optA, "blue"); });

            var build = UITheme.ThemedButton(card.transform, new Vector2(0, -288), new Vector2(420, 56),
                L10n.Get("动 工") + " · " + next.Cost.ToString("N0"), "gold", 24);
            build.onClick.AddListener(async () =>
            {
                if (sel[0] == 0)
                {
                    UIRoot.ToastStatic(L10n.Get("请先选择装饰方案"));
                    return;
                }
                build.interactable = false;
                var r = await ApiClient.ManorBuildAsync(sel[0]);
                if (r != null && r["ok"].AsBool)
                {
                    string newTitle = r["data"]["title"].Value;
                    if (!string.IsNullOrEmpty(newTitle) && newTitle != prevTitle)
                        UIRoot.ToastStatic("<color=#F5D061>" + L10n.Get("改造完成") + "：" + r["data"]["message"].Value +
                            " · " + L10n.Get("获得称号") + "『" + L10n.Get(newTitle) + "』</color>");
                    else
                        UIRoot.ToastStatic(L10n.Get("改造完成") + "：" + r["data"]["message"].Value);
                    UIRoot.Instance.RefreshTop();
                    Object.Destroy(panel);
                    Show();   // 重开刷新进度
                }
                else
                {
                    build.interactable = true;
                    UIRoot.ToastStatic(L10n.Get("动工失败：") + Screens.ErrText(r));
                }
            });
            return y - 360;
        }
    }
}
