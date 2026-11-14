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
        /// <summary>名字 → 玩家 Id 索引：O(1) 登录查找 + 改名唯一性的原子裁决（替代全表扫描）</summary>
        private readonly ConcurrentDictionary<string, long> _nameIndex = new(StringComparer.Ordinal);
        /// <summary>邀请人 → 已绑定新人数（ReferralView 用，避免 /api/state 全服扫描）</summary>
        public readonly ConcurrentDictionary<long, int> InviteCount = new();
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

        /// <summary>
        /// 登录/注册（凭昵称 + secret）：
        /// - 新玩家：注册并生成 secret 随响应下发，客户端持久保存，之后登录必须携带；
        /// - 老玩家（已绑定 secret）：不匹配返回 WRONG_SECRET —— 堵住"知道昵称即可接管账号"；
        /// - 存量账号（secret 为 null，如本次升级前的库）：首次登录绑定当时携带（或新生成）的 secret。
        /// 注意：上线生产必须接平台身份（微信 code2session / Steam 票据），secret 是过渡防线。
        /// </summary>
        public (Player player, string err) LoginOrRegister(string name, string secret)
        {
            lock (_loginLock)
            {
                if (_nameIndex.TryGetValue(name, out var existId) && Players.TryGetValue(existId, out var exist))
                {
                    if (exist.Id < 0) return (null, "NAME_TAKEN"); // 系统保留名（机器人）：不可登录/注册
                    lock (exist.Sync)
                    {
                        if (exist.Secret != null && !FixedTimeEquals(exist.Secret, secret))
                        {
                            return (null, "WRONG_SECRET");
                        }
                        if (exist.Secret == null)
                        {
                            exist.Secret = string.IsNullOrEmpty(secret) ? Guid.NewGuid().ToString("N") : secret; // 存量账号首登绑定
                        }
                        if (exist.Token != null) _tokenIndex.TryRemove(exist.Token, out _);
                        exist.Token = Guid.NewGuid().ToString("N");
                        Db.SavePlayer(exist);
                    }
                    _tokenIndex[exist.Token] = exist.Id;
                    return (exist, null);
                }
                if (!string.IsNullOrEmpty(secret) && _nameIndex.ContainsKey(name)) return (null, "WRONG_SECRET"); // 并发兜底
                var player = new Player
                {
                    Id = AllocPlayerId(),
                    Name = name,
                    Token = Guid.NewGuid().ToString("N"),
                    Secret = string.IsNullOrEmpty(secret) ? Guid.NewGuid().ToString("N") : secret,
                    Gold = GameTables.Tables.TbGlobal.ChestInitGold,
                };
                Players[player.Id] = player;
                _nameIndex[name] = player.Id;
                _tokenIndex[player.Token] = player.Id;
                Db.SavePlayer(player);
                Db.AddGoldTx(player.Id, player.Gold, "init", player.Id);
                return (player, null);
            }
        }

        /// <summary>恒时字符串比较（防时序侧信道逐位泄漏 secret）</summary>
        private static bool FixedTimeEquals(string a, string b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        /// <summary>改名原子裁决：新名未被他人占用则取得所有权并释放旧名（ ConcurrentDictionary 原子操作，无嵌套锁）</summary>
        public bool TryAcquireName(string newName, long ownerId)
        {
            if (_nameIndex.TryGetValue(newName, out var holder) && holder != ownerId) return false;
            if (_nameIndex.AddOrUpdate(newName, ownerId, (_, old) => old == ownerId ? ownerId : old) != ownerId) return false;
            return true;
        }

        /// <summary>改名成功后释放旧名（仅当仍归属本人时移除）</summary>
        public void ReleaseName(string oldName, long ownerId)
        {
            if (_nameIndex.TryGetValue(oldName, out var holder) && holder == ownerId)
            {
                _nameIndex.TryRemove(new KeyValuePair<string, long>(oldName, ownerId));
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

        /// <summary>启动恢复后修正自增 Id 游标 + 重建名字/邀请索引</summary>
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
                _nameIndex[p.Name] = p.Id;                         // 登录 O(1) 查找 + 改名唯一性裁决（机器人名同样占位；登录侧按 Id<0 拒绝接管）
                if (p.InviterId > 0) InviteCount.AddOrUpdate(p.InviterId, 1, (_, c) => c + 1);
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
                        _nameIndex[name] = bot.Id; // 持久机器人名也占位（保留名，防真人重名注册撞唯一索引丢写）
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
