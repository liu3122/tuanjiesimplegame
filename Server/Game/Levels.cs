using cfg;
using SimpleGameServer.Config;

namespace SimpleGameServer.Game
{
    /// <summary>
    /// 玩家等级（tb_level 配置：累计经验达 XpNeed 升级）与解锁门槛。
    /// 经验来源：开箱结算 +10、对决胜利 +15（平局 +5）、抢宝参与 +3。
    /// </summary>
    public static class Levels
    {
        public const int XpChestSettle = 10;
        public const int XpDuelWin = 15;
        public const int XpDuelTie = 5;
        public const int XpRushJoin = 3;

        /// <summary>对赌房解锁等级</summary>
        public const int PotDuelMinLevel = 3;

        public static int MaxLevel
        {
            get { var l = 1; foreach (var row in GameTables.Tables.TbLevel.DataList) l = Math.Max(l, row.Id); return l; }
        }

        public static int LevelFromXp(int xp)
        {
            int level = 1;
            foreach (var row in GameTables.Tables.TbLevel.DataList)
            {
                if (xp >= row.XpNeed) level = Math.Max(level, row.Id);
            }
            return Math.Min(level, MaxLevel);
        }

        public static int XpForLevel(int level)
        {
            var row = GameTables.Tables.TbLevel.GetOrDefault(level);
            return row?.XpNeed ?? int.MaxValue;
        }

        /// <summary>升到下一级还需经验（满级返回 0）</summary>
        public static int XpNext(int level, int xp) => level >= MaxLevel ? 0 : Math.Max(0, XpForLevel(level + 1) - xp);

        /// <summary>加经验并升级（返回 true = 升级了）</summary>
        public static bool AddXp(Player p, int amount)
        {
            p.Xp += amount;
            int newLevel = LevelFromXp(p.Xp);
            if (newLevel > p.Level)
            {
                p.Level = newLevel;
                return true;
            }
            return false;
        }

        /// <summary>宝箱档位解锁等级（木/双头奖 1，银/幸运 3，金/诅咒 5）</summary>
        public static int MinLevelForChest(int configId) => configId switch { 2 or 5 => 3, 3 or 6 => 5, _ => 1 };

        /// <summary>技能解锁等级（窥视 2、探测 4、保底 6，其余 1）</summary>
        public static int MinLevelForSkill(int skillType) => skillType switch { 4 => 2, 5 => 4, 6 => 6, _ => 1 };
    }

    /// <summary>Elo 天梯分（标准 Elo，K=32；对机器人小分值防刷）</summary>
    public static class Elo
    {
        public const int K = 32;
        public const int StartRating = 1000;

        public static double Expected(int ra, int rb) => 1.0 / (1.0 + Math.Pow(10, (rb - ra) / 400.0));

        public static int NewRating(int ra, int rb, double score) => (int)Math.Round(ra + K * (score - Expected(ra, rb)));
    }
}
