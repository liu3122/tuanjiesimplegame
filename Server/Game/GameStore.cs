using System.Collections.Concurrent;
using cfg;
using SimpleGameServer.Config;

namespace SimpleGameServer.Game
{
    /// <summary>
    /// 内存仓储（高并发）：Players/Chests/Lots 均为并发字典；令牌索引 O(1) 鉴权；
    /// 自增 Id 用 Interlocked。业务锁按实体拆分（Player.Sync/Chest.Sync，加锁顺序 Room/Lot → Chest → Player）。
    /// </summary>
    public class GameStore
    {
        public readonly ConcurrentDictionary<long, Player> Players = new();
        public readonly ConcurrentDictionary<long, Chest> Chests = new();
        public readonly ConcurrentDictionary<long, AuctionLot> Lots = new();
        private readonly ConcurrentDictionary<string, long> _tokenIndex = new();
        private readonly object _loginLock = new();
        private readonly object _botLotsLock = new();
        /// <summary>赛季结算外层锁（内部逐玩家 Sync 加锁）</summary>
        public readonly object SeasonGate = new();
        /// <summary>线程安全随机源（多请求并行下共享 Random 会损坏内部状态）</summary>
        public readonly Random Rng = new SafeRandom();

        private long _nextPlayerId = 1000;
        private long _nextChestUid = 1;
        private long _nextLotId = 1;
        private long _nextBotId = -100;

        public long AllocPlayerId() => Interlocked.Increment(ref _nextPlayerId) - 1;
        public long AllocChestUid() => Interlocked.Increment(ref _nextChestUid) - 1;
        public long AllocLotId() => Interlocked.Increment(ref _nextLotId) - 1;
        public long AllocBotId() => Interlocked.Decrement(ref _nextBotId) + 1;

        public Player LoginOrRegister(string name)
        {
            lock (_loginLock)
            {
                var exist = Players.Values.FirstOrDefault(p => p.Name == name);
                if (exist != null)
                {
                    lock (exist.Sync)
                    {
                        if (exist.Token != null) _tokenIndex.TryRemove(exist.Token, out _);
                        exist.Token = Guid.NewGuid().ToString("N");
                        Db.SavePlayer(exist);
                    }
                    _tokenIndex[exist.Token] = exist.Id;
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
                _tokenIndex[player.Token] = player.Id;
                Db.SavePlayer(player);
                Db.AddGoldTx(player.Id, player.Gold, "init", player.Id);
                return player;
            }
        }

        /// <summary>鉴权：令牌索引 O(1)（热路径，无锁读并发字典）</summary>
        public Player Validate(string token)
        {
            if (string.IsNullOrEmpty(token) || !_tokenIndex.TryGetValue(token, out var id)) return null;
            return Players.TryGetValue(id, out var p) ? p : null;
        }

        private static readonly string[] RushBotNames = { "疾风剑豪", "手速之王", "夜行者", "闪电侠", "快枪手", "无影手" };

        /// <summary>创建抢宝房机器人（不持久化，房间结束即消失）</summary>
        public Player CreateRushBot()
        {
            lock (Rng) // Random 非线程安全
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
            if (Players.Count > 0) _nextPlayerId = Players.Keys.Max() + 1;
            if (Chests.Count > 0) _nextChestUid = Chests.Keys.Max() + 1;
            if (Lots.Count > 0) _nextLotId = Lots.Keys.Max() + 1;
            long minBot = Players.Keys.Where(k => k < 0).DefaultIfEmpty(0).Min();
            _nextBotId = Math.Min(_nextBotId, minBot - 1);
            foreach (var p in Players.Values)
            {
                if (p.Token != null) _tokenIndex[p.Token] = p.Id; // 重启后旧 token 仍可鉴权
            }
        }

        /// <summary>确保机器人卖家存在并持有若干拍品</summary>
        public void EnsureBotLots()
        {
            lock (_botLotsLock)
            {
                if (Lots.Values.Any(l => !l.Settled && l.SellerId < 0)) return;

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
                        cfg.chest.Chest config;
                        lock (Rng) config = GameTables.Tables.TbChest.DataList[Rng.Next(GameTables.Tables.TbChest.DataList.Count)];
                        var chest = new Chest(AllocChestUid(), config, Rng) { OwnerId = bot.Id };
                        int eliminates;
                        lock (Rng) eliminates = Rng.Next(0, 8);
                        for (int e = 0; e < eliminates && chest.RemainingCount > 1; e++)
                        {
                            int slot;
                            lock (Rng) slot = Rng.Next(20);
                            chest.Eliminate(slot);
                        }
                        lock (Rng) chest.SkillUsesRemaining = Rng.Next(0, GameTables.Tables.TbGlobal.ChestMaxSkillUses + 1);
                        chest.State = ChestState.Listed;
                        Chests[chest.Uid] = chest;
                        Db.SaveChest(chest);

                        double ev = chest.ExpectedValue;
                        long lotId = AllocLotId();
                        var lot = new AuctionLot
                        {
                            LotId = lotId,
                            ChestUid = chest.Uid,
                            SellerId = bot.Id,
                            SellerName = bot.Name,
                            StartPrice = Math.Max(1, (long)(ev * (0.7 + SafeNextDouble() * 0.2))),
                            BuyoutPrice = (long)(ev * (1.10 + SafeNextDouble() * 0.15)),
                            EndTimeUtc = DateTime.UtcNow.AddSeconds(GameTables.Tables.TbGlobal.AuctionDurationSec + SafeNextDouble() * 20),
                        };
                        Lots[lotId] = lot;
                        Db.SaveLot(lot);
                    }
                }
            }
        }

        /// <summary>线程安全的随机数（Random 实例非线程安全，统一走此入口）</summary>
        public double SafeNextDouble()
        {
            lock (Rng) return Rng.NextDouble();
        }
    }

    /// <summary>线程安全 Random（覆盖虚方法加锁；作为 Random 透明替换）</summary>
    public class SafeRandom : Random
    {
        private readonly object _gate = new();
        public override int Next() { lock (_gate) return base.Next(); }
        public override int Next(int maxValue) { lock (_gate) return base.Next(maxValue); }
        public override int Next(int minValue, int maxValue) { lock (_gate) return base.Next(minValue, maxValue); }
        public override double NextDouble() { lock (_gate) return base.NextDouble(); }
    }
}
