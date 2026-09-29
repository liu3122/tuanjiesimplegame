using cfg;
using SimpleGameServer.Config;

namespace SimpleGameServer.Game
{
    /// <summary>
    /// 每日任务：任务池配置于 tb_quest.xlsx，按天轮换取 3 个（全服同任务，窗口式轮换）。
    /// 进度为内存权威计数（调用方持 player.Sync），事件埋点：
    /// chest 开箱结算 / eliminate 排除 / duel 对决完成 / duelwin 对决胜利 / rush 抢宝 / ad 广告 / auction 拍卖出价。
    /// </summary>
    public static class Quests
    {
        /// <summary>看广告双倍领取：每日最多次数（复用广告频控记账）</summary>
        public const int DoubleAdDailyLimit = 3;
        public const string DoubleAdPlacement = "quest_double";

        private static long DayIndex => DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 86400;

        /// <summary>今日任务（窗口轮换：dayIndex 起连续 3 个，全服一致，重启不变）</summary>
        public static List<cfg.quest.Quest> DailyQuests()
        {
            var list = GameTables.Tables.TbQuest.DataList;
            if (list.Count <= 3) return list.ToList();
            long d = DayIndex;
            var picked = new List<cfg.quest.Quest>();
            for (int i = 0; i < 3; i++) picked.Add(list[(int)((d + i) % list.Count)]);
            return picked;
        }

        /// <summary>跨日清零进度（调用方持 player.Sync）</summary>
        public static void RollDay(Player p)
        {
            string today = DateTime.UtcNow.ToString("yyyy-MM-dd");
            if (p.QuestDay != today)
            {
                p.QuestDay = today;
                p.QuestProgress.Clear();
                p.QuestClaimed.Clear();
                p.DailyExtra = 0;
            }
        }

        /// <summary>事件埋点：给今日同类型任务累计进度（调用方持 player.Sync）</summary>
        public static void Progress(Player p, string type, int n = 1)
        {
            RollDay(p);
            bool active = false;
            foreach (var q in DailyQuests())
            {
                if (q.Type == type) { active = true; break; }
            }
            if (!active) return;
            p.QuestProgress[type] = p.QuestProgress.GetValueOrDefault(type) + n;
        }
    }
}
