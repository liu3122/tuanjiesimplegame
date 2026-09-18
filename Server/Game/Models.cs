using cfg;
using cfg.chest;

namespace SimpleGameServer.Game
{
    /// <summary>玩家（含机器人：Id < 0）</summary>
    public class Player
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public string Token { get; set; }
        public long Gold { get; set; }
        public int CharacterId { get; set; } = 1;
        /// <summary>拍卖出价预扣（lotId → 冻结金额）</summary>
        public Dictionary<long, long> Escrow { get; } = new();
    }

    /// <summary>
    /// 服务端权威的宝箱实体：20 格金额只有服务端知道全貌，
    /// 未揭示格的金额绝不下发客户端。
    /// </summary>
    public class Chest
    {
        public long Uid { get; }
        public int ConfigId { get; }
        public long OwnerId { get; set; }             // 持有者（0 = 已被系统回收）
        public int[] Amounts { get; private set; }     // 洗牌后的 20 格金额（仅服务端可见）
        public bool[] Eliminated { get; private set; }
        public int EliminatedCount { get; set; }
        public ChestState State { get; set; }
        public long PaidPrice { get; set; }

        /// <summary>技能额度（共享池：最多 N 个玩家各用一次）</summary>
        public int SkillUsesRemaining { get; set; }
        /// <summary>已对本宝箱使用过技能的玩家集合（按玩家限一次的权威记录）</summary>
        public HashSet<long> SkillUsedBy { get; } = new();
        /// <summary>免费排除剩余次数（赌徒技能，随宝箱转移）</summary>
        public int FreeEliminateBuff { get; set; }
        /// <summary>下次系统收购价 +15%（银行家技能，随宝箱转移）</summary>
        public bool PriceBuffNext { get; set; }

        public Chest(long uid, cfg.chest.Chest config, Random rng)
        {
            Uid = uid;
            ConfigId = config.Id;
            Amounts = new int[config.SlotAmounts.Count];
            config.SlotAmounts.CopyTo(Amounts);
            for (int i = Amounts.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (Amounts[i], Amounts[j]) = (Amounts[j], Amounts[i]);
            }
            Eliminated = new bool[Amounts.Length];
            State = ChestState.Owned;
            SkillUsesRemaining = Config.GameTables.Tables.TbGlobal.ChestMaxSkillUses;
        }

        /// <summary>从数据库恢复实例（绕过洗牌构造）</summary>
        public static Chest Restore(long uid, int configId, long ownerId, ChestState state,
            int[] amounts, bool[] eliminated, int skillUses, long[] skillUsedBy,
            int freeBuff, bool priceBuff, long paidPrice)
        {
            var c = new Chest(uid, Config.GameTables.Tables.TbChest.DataList.First(x => x.Id == configId), Random.Shared)
            {
                OwnerId = ownerId,
                State = state,
                PaidPrice = paidPrice,
            };
            c.Amounts = amounts;
            c.Eliminated = eliminated;
            c.EliminatedCount = eliminated.Count(e => e);
            c.SkillUsesRemaining = skillUses;
            c.FreeEliminateBuff = freeBuff;
            c.PriceBuffNext = priceBuff;
            foreach (var pid in skillUsedBy) c.SkillUsedBy.Add(pid);
            return c;
        }

        public int RemainingCount => Amounts.Length - EliminatedCount;

        public long RemainingSum
        {
            get
            {
                long sum = 0;
                for (int i = 0; i < Amounts.Length; i++)
                {
                    if (!Eliminated[i]) sum += Amounts[i];
                }
                return sum;
            }
        }

        public double ExpectedValue => (double)RemainingSum / RemainingCount;

        public double BankerOffer
        {
            get
            {
                double offer = ExpectedValue * Config.GameTables.Tables.TbBankerFactor[EliminatedCount].Factor;
                return PriceBuffNext ? offer * 1.15 : offer;
            }
        }

        public int NextEliminateCost
        {
            get
            {
                if (FreeEliminateBuff > 0) return 0;
                var g = Config.GameTables.Tables.TbGlobal;
                if (EliminatedCount < g.ChestFreeEliminates) return 0;
                if (RemainingCount <= 1) return 0;
                return g.ChestEliminateBaseFee + g.ChestEliminateFeeGrowth * (EliminatedCount - g.ChestFreeEliminates);
            }
        }

        /// <summary>排除指定格：返回被揭示金额；非法返回 -1</summary>
        public int Eliminate(int slotIndex)
        {
            if (State != ChestState.Owned || Eliminated[slotIndex] || RemainingCount <= 1) return -1;
            Eliminated[slotIndex] = true;
            EliminatedCount++;
            if (FreeEliminateBuff > 0) FreeEliminateBuff--;
            return Amounts[slotIndex];
        }

        /// <summary>使用技能（校验额度 + 按玩家限一次）。返回描述；失败返回 null</summary>
        public string UseSkill(Player player, int skillType, string skillName)
        {
            if (State != ChestState.Owned || RemainingCount <= 1) return null;
            if (SkillUsesRemaining <= 0) return "ERR_QUOTA";
            if (SkillUsedBy.Contains(player.Id)) return "ERR_PLAYER_USED";

            switch (skillType)
            {
                case 1:
                {
                    int best = -1;
                    double ev = ExpectedValue;
                    for (int i = 0; i < Amounts.Length; i++)
                    {
                        if (Eliminated[i]) continue;
                        if (Amounts[i] < ev && (best < 0 || Amounts[i] < Amounts[best])) best = i;
                    }
                    if (best < 0) return "ERR_NO_TARGET";
                    int revealed = Amounts[best];
                    Eliminated[best] = true;
                    EliminatedCount++;
                    SkillUsesRemaining--;
                    SkillUsedBy.Add(player.Id);
                    return $"【{skillName}】洞察排除 {revealed}（低于均值 {ev:F1}）";
                }
                case 2:
                    PriceBuffNext = true;
                    SkillUsesRemaining--;
                    SkillUsedBy.Add(player.Id);
                    return $"【{skillName}】下次收购价已抬升 15%";
                case 3:
                    FreeEliminateBuff += 3;
                    SkillUsesRemaining--;
                    SkillUsedBy.Add(player.Id);
                    return $"【{skillName}】接下来 3 次排除免费";
                default:
                    return null;
            }
        }

        /// <summary>终局开箱：返回最终金额</summary>
        public long OpenFinal()
        {
            for (int i = 0; i < Amounts.Length; i++)
            {
                if (!Eliminated[i])
                {
                    Eliminated[i] = true;
                    EliminatedCount++;
                    State = ChestState.Finished;
                    return Amounts[i];
                }
            }
            return 0;
        }
    }

    public enum ChestState
    {
        Owned = 0,
        Listed = 1,
        Finished = 2,
    }

    /// <summary>拍卖拍品</summary>
    public class AuctionLot
    {
        public long LotId { get; set; }
        public long ChestUid { get; set; }
        public long SellerId { get; set; }
        public string SellerName { get; set; }
        public long StartPrice { get; set; }
        public long BuyoutPrice { get; set; }        // 0 = 无一口价
        public DateTime EndTimeUtc { get; set; }
        public long CurrentBid { get; set; }         // 0 = 无出价
        public long CurrentBidderId { get; set; }    // 0 = 无
        public string CurrentBidderName { get; set; }
        public bool Settled { get; set; }
    }
}
