using System.Linq;
using System.Text;
using cfg;
using UnityEngine;

namespace Game.HotUpdate.ChestGame
{
    /// <summary>
    /// 概率/金额公示面板（合规要求：随机抽取规则透明）。
    /// 数据来自本机加载的同一份配置表（与服务端权威算法 tb_chest/tb_chestgen/tb_rushroom 一致）：
    /// - GenMode 0 固定池：20 格金额固定，仅格位洗牌，无抽取概率
    /// - GenMode 1 算法生成：每格按权重 Roll 稀有度档，基准金额 × 对数正态扰动，整体 EV 归一化（总池恒定）
    /// - 抢宝房：固定金字塔结构奖池，总池 = 门票 × 人数 × 倍率
    /// 文本锚点/排版复刻 ShowQuests 的既有模式（已验证渲染正确）。
    /// </summary>
    public static class DisclosurePanel
    {
        private static readonly string[] TierNames = { "白", "绿", "蓝", "紫", "金" };

        public static void Show()
        {
            var panel = UITheme.CreatePanel(UIRoot.Instance.CanvasTransform, "panel_gold", Vector2.zero, new Vector2(760, 1160));
            var rt = panel.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);

            var title = TDUIHelper.MakeText(panel.transform, new Vector2(0, -26), TextAnchor.MiddleCenter, 32);
            title.text = "<color=#F5D061>" + L10n.Get("概率与金额公示") + "</color>";
            TDUIHelper.SetSize(title.rectTransform, 880, 50);

            var hint = TDUIHelper.MakeText(panel.transform, new Vector2(0, -80), TextAnchor.MiddleCenter, 20);
            hint.color = UITheme.TextSub;
            // 货币声明按平台区分（合规口径）：小游戏纯广告无内购；内购平台（Steam/iOS/Google）金币可购买
            hint.text = PlatformBootstrap.Current == PlatformId.WeixinMinigame
                ? L10n.Get("全部随机结果由服务器生成；金币为游戏内虚拟货币，无法用现金购买或兑换现金、实物")
                : L10n.Get("全部随机结果由服务器生成；金币为游戏内虚拟货币，可通过应用内购买获得，不可兑换现金、实物");
            TDUIHelper.SetSize(hint.rectTransform, 720, 40);

            int count = GameConfig.Tables.TbChest.DataList.Count;
            float y = -140;   // 宝箱行中心（首行）
            float step = 124;
            foreach (var chest in GameConfig.Tables.TbChest.DataList)
            {
                var row = UITheme.CreatePanel(panel.transform, "panel_dark", new Vector2(0, y), new Vector2(700, 114));

                var name = TDUIHelper.MakeText(row.transform, new Vector2(0, -8), TextAnchor.MiddleCenter, 24);
                TDUIHelper.SetSize(name.rectTransform, 660, 34);
                name.text = "<color=#F5D061>" + chest.Name + "</color> · " + L10n.Get("售价") + " " + chest.Price.ToString("N0");

                var detail = TDUIHelper.MakeText(row.transform, new Vector2(-20, 0), TextAnchor.MiddleLeft, 19);
                var drt = detail.rectTransform;
                drt.anchorMin = drt.pivot = new Vector2(0, 0.5f);
                drt.anchoredPosition = new Vector2(-330, -14);
                TDUIHelper.SetSize(drt, 660, 62);
                detail.color = UITheme.TextSub;
                detail.text = Describe(chest);
                y -= step;
            }

            // 抢宝房：固定金字塔结构（无抽取概率）
            float afterChests = -140 - count * step;   // 末行底缘附近的下一可用 y
            var rushHeader = TDUIHelper.MakeText(panel.transform, new Vector2(0, afterChests + 20), TextAnchor.MiddleCenter, 24);
            TDUIHelper.SetSize(rushHeader.rectTransform, 700, 34);
            rushHeader.text = "<color=#F5D061>" + L10n.Get("抢宝房奖池规则") + "</color>";

            var rushRow = UITheme.CreatePanel(panel.transform, "panel_dark", new Vector2(0, afterChests - 60), new Vector2(700, 96));
            var rushDetail = TDUIHelper.MakeText(rushRow.transform, new Vector2(-20, 0), TextAnchor.MiddleLeft, 19);
            var rrt = rushDetail.rectTransform;
            rrt.anchorMin = rrt.pivot = new Vector2(0, 0.5f);
            rrt.anchoredPosition = new Vector2(-330, 0);
            TDUIHelper.SetSize(rrt, 660, 80);
            rushDetail.color = UITheme.TextSub;
            var scales = string.Join("、", GameConfig.Tables.TbRushRoom.DataList.Select(r => r.Name + " ×" + r.PoolScale));
            rushDetail.text = L10n.Get("奖池为固定金字塔结构（20 档比例恒定，仅格位随机），总奖池 = 门票 × 人数 × 倍率：") + scales +
                (GameConfig.Tables.TbRushRoom.DataList.Any(r => r.Blind)
                    ? "\n" + L10n.Get("盲抢房：格位金额领取后才显示，对所有人（含机器人）同样隐藏")
                    : "");

            var close = UITheme.ThemedButton(panel.transform, new Vector2(0, afterChests - 170), new Vector2(220, 54), L10n.Get("关 闭"), "red", 24);
            close.onClick.AddListener(() => Object.Destroy(panel));

            var age = TDUIHelper.MakeText(panel.transform, new Vector2(0, -1120), TextAnchor.MiddleCenter, 20);
            TDUIHelper.SetSize(age.rectTransform, 720, 32);
            age.color = UITheme.TextSub;
            age.text = L10n.Get("适龄提示：本游戏适合 8 周岁以上用户，请在监护人指导下适度游戏");
        }

        /// <summary>按 GenMode 生成公示文案（与 Server/Game/ChestFactory.cs 算法口径一致）</summary>
        private static string Describe(cfg.chest.Chest chest)
        {
            if (chest.GenMode == 0)
            {
                // 固定池：聚合同额格子 → "10×6、20×5…"
                var sb = new StringBuilder();
                foreach (var g in chest.SlotAmounts.GroupBy(a => a).OrderBy(g => g.Key))
                    sb.Append(g.Key).Append("×").Append(g.Count()).Append("、");
                return L10n.Get("固定金额池（无抽取概率，仅格位随机洗牌）：") + sb.ToString(0, sb.Length - 1);
            }

            var gen = GameConfig.Tables.TbChestGen.GetOrDefault(chest.Id);
            if (gen == null) return L10n.Get("固定金额池（无抽取概率，仅格位随机洗牌）：") + "—";
            float total = gen.Weights.Sum();
            var s = new StringBuilder();
            int tiers = Mathf.Min(gen.Weights.Count, TierNames.Length);
            for (int i = 0; i < tiers; i++)
            {
                float pct = gen.Weights[i] / total * 100f;
                if (i > 0) s.Append("｜");
                s.Append(TierNames[i]).Append(Trim(pct)).Append("%·基准").Append(gen.Bases[i].ToString("N0"));
            }
            return L10n.Get("每格按概率抽取稀有度档：") + s + "\n" +
                   L10n.Get("单格金额在基准上按对数正态随机浮动（幅度约 ±") + Trim(gen.Jitter * 100f) + "%），" +
                   L10n.Get("整体归一化后总池恒定，与固定池总和一致（期望稳定）");
        }

        private static string Trim(float v) => v.ToString(v % 1f < 0.05f ? "F0" : "F1");
    }
}
