using cfg;
using SimpleGameServer.Config;

namespace SimpleGameServer.Game
{
    /// <summary>内存仓储（单例，lock 保护）。接入数据库时以接口替换本实现。</summary>
    public class GameStore
    {
        public readonly object Gate = new();
        public readonly Dictionary<long, Player> Players = new();
        public readonly Dictionary<long, Chest> Chests = new();
        public readonly List<AuctionLot> Lots = new();
        public readonly Random Rng = new();

        private long _nextPlayerId = 1000;
        private long _nextChestUid = 1;
        private long _nextLotId = 1;
        private long _nextBotId = -100;

        public long AllocPlayerId() => _nextPlayerId++;
        public long AllocChestUid() => _nextChestUid++;
        public long AllocLotId() => _nextLotId++;
        public long AllocBotId() => _nextBotId--;

        public Player LoginOrRegister(string name)
        {
            lock (Gate)
            {
                var exist = Players.Values.FirstOrDefault(p => p.Name == name);
                if (exist != null)
                {
                    exist.Token = Guid.NewGuid().ToString("N");
                    Db.SavePlayer(exist);
                    return exist;
                }
                var player = new Player
                {
                    Id = AllocPlayerId(),
                    Name = name,
                    Token = Guid.NewGuid().ToString("N"),
                    Gold = GameTables.Tables.TbGlobal.ChestInitGold,
                };
                Players[player.Id] = player;
                Db.SavePlayer(player);
                Db.AddGoldTx(player.Id, player.Gold, "init", player.Id);
                return player;
            }
        }

        public Player Validate(string token)
        {
            if (string.IsNullOrEmpty(token)) return null;
            lock (Gate)
            {
                return Players.Values.FirstOrDefault(p => p.Token == token);
            }
        }

        private static readonly string[] RushBotNames = { "疾风剑豪", "手速之王", "夜行者", "闪电侠", "快枪手", "无影手" };

        /// <summary>创建抢宝房机器人（不持久化，房间结束即消失）</summary>
        public Player CreateRushBot()
        {
            lock (Gate)
            {
                return new Player
                {
                    Id = AllocBotId(),
                    Name = RushBotNames[Rng.Next(RushBotNames.Length)] + Rng.Next(10, 99),
                    Token = null,
                    Gold = 0,
                };
            }
        }

        /// <summary>启动恢复后修正自增 Id 游标</summary>
        public void RestoreIdCursors()
        {
            lock (Gate)
            {
                if (Players.Count > 0) _nextPlayerId = Players.Keys.Max() + 1;
                if (Chests.Count > 0) _nextChestUid = Chests.Keys.Max() + 1;
                if (Lots.Count > 0) _nextLotId = Lots.Select(l => l.LotId).Max() + 1;
                long minBot = Players.Keys.Where(k => k < 0).DefaultIfEmpty(0).Min();
                _nextBotId = Math.Min(_nextBotId, minBot - 1);
            }
        }

        /// <summary>确保机器人卖家存在并持有若干拍品</summary>
        public void EnsureBotLots()
        {
            lock (Gate)
            {
                if (Lots.Any(l => !l.Settled && l.SellerId < 0)) return;

                string[] botNames = { "风起云涌", "老铁666", "夜雨声烦", "搬砖达人", "宝箱猎人", "氪不改命" };
                foreach (var name in botNames.Take(3))
                {
                    var bot = Players.Values.FirstOrDefault(p => p.Name == name && p.Id < 0);
                    if (bot == null)
                    {
                        bot = new Player { Id = AllocBotId(), Name = name, Token = null };
                        Players[bot.Id] = bot;
                        Db.SavePlayer(bot);
                    }

                    for (int i = 0; i < 2; i++)
                    {
                        var config = GameTables.Tables.TbChest.DataList[Rng.Next(GameTables.Tables.TbChest.DataList.Count)];
                        var chest = new Chest(AllocChestUid(), config, Rng) { OwnerId = bot.Id };
                        int eliminates = Rng.Next(0, 8);
                        for (int e = 0; e < eliminates && chest.RemainingCount > 1; e++)
                        {
                            chest.Eliminate(Rng.Next(20));
                        }
                        chest.SkillUsesRemaining = Rng.Next(0, GameTables.Tables.TbGlobal.ChestMaxSkillUses + 1);
                        chest.State = ChestState.Listed;
                        Chests[chest.Uid] = chest;
                        Db.SaveChest(chest);

                        double ev = chest.ExpectedValue;
                        var lot = new AuctionLot
                        {
                            LotId = AllocLotId(),
                            ChestUid = chest.Uid,
                            SellerId = bot.Id,
                            SellerName = bot.Name,
                            StartPrice = Math.Max(1, (long)(ev * (0.7 + Rng.NextDouble() * 0.2))),
                            BuyoutPrice = (long)(ev * (1.10 + Rng.NextDouble() * 0.15)),
                            EndTimeUtc = DateTime.UtcNow.AddSeconds(GameTables.Tables.TbGlobal.AuctionDurationSec + Rng.Next(20)),
                        };
                        Lots.Add(lot);
                        Db.SaveLot(lot);
                    }
                }
            }
        }
    }
}
