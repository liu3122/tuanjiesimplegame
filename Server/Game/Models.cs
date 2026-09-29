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
        /// <summary>累计经验（等级门槛见 tb_level）</summary>
        public int Xp { get; set; }
        /// <summary>玩家等级（1 起）</summary>
        public int Level { get; set; } = 1;
        /// <summary>对决天梯分（Elo，初始 1000）</summary>
        public int DuelRating { get; set; } = 1000;
        /// <summary>地区码（CDN 头/客户端上报/GeoIP，见 RegionResolver）</summary>
        public string Region { get; set; } = "CN";
        /// <summary>拍卖出价预扣（lotId → 冻结金额）</summary>
        public Dictionary<long, long> Escrow { get; } = new();
        /// <summary>技能冷却（skillType → 解冻时刻 Unix 秒）。跨宝箱生效，防止反复刷同一强力技能</summary>
        public Dictionary<int, long> SkillCooldowns { get; } = new();
        /// <summary>开箱结算次数（内存权威，避免热路径 SQL COUNT）</summary>
        public int Opens { get; set; }
        /// <summary>最近赛季奖励缓存（state 下发用）</summary>
        public object LastSeasonReward { get; set; }
        // ---- 广告变现（激励视频）：服务端权威频控 ----
        /// <summary>广告位 → 上次使用 Unix 秒</summary>
        public Dictionary<string, long> AdLastUse { get; } = new();
        /// <summary>广告位 → 当日已用次数</summary>
        public Dictionary<string, int> AdDailyCount { get; } = new();
        /// <summary>广告计数日（yyyy-MM-dd，跨日清零）</summary>
        public string AdDailyDay { get; set; } = "";
        // ---- 每日任务（内存权威进度，跨日清零） ----
        /// <summary>任务类型 → 当日进度</summary>
        public Dictionary<string, int> QuestProgress { get; } = new();
        /// <summary>当日已领取的任务 Id</summary>
        public HashSet<string> QuestClaimed { get; } = new();
        /// <summary>任务计数日（yyyy-MM-dd）</summary>
        public string QuestDay { get; set; } = "";
        /// <summary>细粒度并发锁。全局加锁顺序约定：Room/Lot → Chest → Player（Player 内多锁按 Id 排序）</summary>
        public readonly object Sync = new();
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
        // ---- 扩展技能状态（均随宝箱转移） ----
        /// <summary>窥视结果：格号 → 已知金额（天眼）</summary>
        public Dictionary<int, int> PeekedSlots { get; } = new();
        /// <summary>探测标记格（罗盘：3 格中最大的一格）</summary>
        public HashSet<int> DetectedSlots { get; } = new();
        /// <summary>保底生效中（保险箱）</summary>
        public bool InsuranceActive { get; set; }
        /// <summary>收购价底价（天平锁价）；null=未锁定</summary>
        public long? PriceFloor { get; set; }
        /// <summary>减半剩余次数（税务官）</summary>
        public int TaxReductionCount { get; set; }
        /// <summary>已鉴定（鉴定师：拍卖时对买家展示剩余金额分布）</summary>
        public bool Identified { get; set; }
        // ---- 银行家报价（限时接受/拒绝） ----
        /// <summary>待处理报价金额；null=无</summary>
        public long? PendingOffer { get; set; }
        public DateTime? PendingOfferExpireUtc { get; set; }
        /// <summary>自上次报价以来的排除数（达到 N 触发报价）</summary>
        public int ElimsSinceOffer { get; set; }
        // ---- 特殊格（幸运/陷阱） ----
        public HashSet<int> LuckySlots { get; } = new();
        public HashSet<int> TrapSlots { get; } = new();
        // ---- 每日挑战 ----
        public bool IsDaily { get; set; }
        public string DailyDay { get; set; }
        /// <summary>教学箱（新玩家免费固定种子，客户端显示引导步骤）</summary>
        public bool IsTutorial { get; set; }
        /// <summary>细粒度并发锁（加锁顺序约定见 Player.Sync）</summary>
        public readonly object Sync = new();

        /// <summary>每日挑战种子（构造后由 GameService 设置）</summary>
        public int DailySeed { get; set; }
        public bool IsDailyShuffle { get; set; }

        public Chest(long uid, cfg.chest.Chest config, Random rng) : this(uid, config, rng, false, 0)
        {
        }

        public Chest(long uid, cfg.chest.Chest config, Random rng, bool isDaily, int dailySeed)
        {
            Uid = uid;
            ConfigId = config.Id;
            IsDaily = isDaily;
            DailySeed = dailySeed;
            IsDailyShuffle = isDaily;
            if (config.GenMode == 1 && !IsDailyShuffle && Config.GameTables.Tables.TbChestGen.GetOrDefault(config.Id) != null)
            {
                // 算法生成：稀有度模板 + 对数正态扰动 + EV 归一化（总池与固定池一致）
                Amounts = ChestFactory.Generate(config, rng);
            }
            else
            {
                Amounts = new int[config.SlotAmounts.Count];
                config.SlotAmounts.CopyTo(Amounts);
                // 每日挑战：日期作种子 → 全服同结构
                var shuffleRng = IsDailyShuffle ? new Random(DailySeed) : rng;
                for (int i = Amounts.Length - 1; i > 0; i--)
                {
                    int j = shuffleRng.Next(i + 1);
                    (Amounts[i], Amounts[j]) = (Amounts[j], Amounts[i]);
                }
            }
            // 特殊格标记（幸运/陷阱）
            if (config.Kind == 2 && config.SpecialCount > 0)
            {
                foreach (var i in Enumerable.Range(0, Amounts.Length).OrderBy(_ => rng.Next()).Take(config.SpecialCount))
                    LuckySlots.Add(i);
            }
            else if (config.Kind == 3 && config.SpecialCount > 0)
            {
                foreach (var i in Enumerable.Range(0, Amounts.Length).OrderBy(_ => rng.Next()).Take(config.SpecialCount))
                    TrapSlots.Add(i);
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

        /// <summary>打乱格位（金额池不变；镜像对决用：同池不同位，对手揭示不暴露我的格位）</summary>
        public void ShuffleAmounts(Random rng)
        {
            for (int i = Amounts.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (Amounts[i], Amounts[j]) = (Amounts[j], Amounts[i]);
            }
        }

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
                if (PriceBuffNext) offer *= 1.15;
                if (PriceFloor.HasValue) offer = Math.Max(offer, PriceFloor.Value); // 锁价：只涨不跌
                return offer;
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
                int cost = g.ChestEliminateBaseFee + g.ChestEliminateFeeGrowth * (EliminatedCount - g.ChestFreeEliminates);
                return cost;
            }
        }

        /// <summary>扣除税务减免后的实际排除费（消耗一次减半次数）</summary>
        public int ChargeEliminateCost()
        {
            int cost = NextEliminateCost;
            if (TaxReductionCount > 0 && cost > 0)
            {
                cost = cost / 2 + (cost % 2); // 向上取整减半
            }
            return cost;
        }

        /// <summary>排除指定格：返回被揭示金额；非法返回 -1</summary>
        /// <summary>排除；返回（揭示金额, 特殊格奖罚, 消息）。锁定时 err=OFFER_PENDING。</summary>
        public (int revealed, int special, string err) EliminateEx(int slotIndex, Player player)
        {
            if (PendingOffer != null) return (0, 0, "OFFER_PENDING");
            if (State != ChestState.Owned || Eliminated[slotIndex] || RemainingCount <= 1) return (0, 0, "BAD_SLOT");
            Eliminated[slotIndex] = true;
            EliminatedCount++;
            if (FreeEliminateBuff > 0) FreeEliminateBuff--;
            if (TaxReductionCount > 0) TaxReductionCount--;
            PeekedSlots.Remove(slotIndex);
            DetectedSlots.Remove(slotIndex);

            int special = 0;
            if (LuckySlots.Contains(slotIndex)) special = Config.GameTables.Tables.TbChest[ConfigId].SpecialValue;
            else if (TrapSlots.Contains(slotIndex)) special = -Config.GameTables.Tables.TbChest[ConfigId].SpecialValue;

            // 银行家报价触发
            ElimsSinceOffer++;
            return (Amounts[slotIndex], special, null);
        }

        /// <summary>尝试生成银行家报价（排除后调用；每 N 格触发，金额=收购价×随机浮动）</summary>
        public bool TryMakeOffer(Random rng)
        {
            var g = Config.GameTables.Tables.TbGlobal;
            if (PendingOffer != null || RemainingCount <= 1) return false;
            if (ElimsSinceOffer < g.OfferEveryNElims) return false;
            double jitter = 1.0 + (rng.NextDouble() * 2 - 1) * g.OfferJitter;
            PendingOffer = Math.Max(1, (long)Math.Round(BankerOffer * jitter));
            PendingOfferExpireUtc = DateTime.UtcNow.AddSeconds(g.OfferValidSec);
            ElimsSinceOffer = 0;
            return true;
        }

        /// <summary>过期视为无报价（懒判定）</summary>
        public void ExpireOfferIfNeeded()
        {
            if (PendingOffer != null && DateTime.UtcNow > PendingOfferExpireUtc)
            {
                PendingOffer = null;
                PendingOfferExpireUtc = null;
            }
        }

        public int Eliminate(int slotIndex)
        {
            var (revealed, _, err) = EliminateEx(slotIndex, null);
            return err == null ? revealed : -1;
        }

        /// <summary>使用技能（校验额度 + 按玩家限一次）。返回描述；失败返回 null</summary>
        public string UseSkill(Player player, int skillType, string skillName, int? slotIndex = null)
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
                case 4: // 窥视：偷看指定未排除格
                {
                    if (slotIndex == null || slotIndex < 0 || slotIndex >= Amounts.Length) return "ERR_BAD_SLOT";
                    if (Eliminated[slotIndex.Value]) return "ERR_SLOT_OPEN";
                    PeekedSlots[slotIndex.Value] = Amounts[slotIndex.Value];
                    SkillUsesRemaining--;
                    SkillUsedBy.Add(player.Id);
                    return $"【{skillName}】窥视第 {(slotIndex.Value + 1)} 格：{Amounts[slotIndex.Value]}";
                }
                case 5: // 探测：随机 3 个未排除格中标记最大的一格
                {
                    var pool = Enumerable.Range(0, Amounts.Length).Where(i => !Eliminated[i]).ToList();
                    if (pool.Count < 2) return "ERR_NO_TARGET";
                    var pick = pool.OrderBy(_ => Guid.NewGuid()).Take(3).ToList();
                    int best = pick.OrderByDescending(i => Amounts[i]).First();
                    foreach (var i in pick) DetectedSlots.Add(i);
                    SkillUsesRemaining--;
                    SkillUsedBy.Add(player.Id);
                    return $"【{skillName}】探测 {pick.Count} 格：第 {best + 1} 格为其中最大（已标记）";
                }
                case 6: // 保底
                    InsuranceActive = true;
                    SkillUsesRemaining--;
                    SkillUsedBy.Add(player.Id);
                    return $"【{skillName}】保底生效：系统结算所得不低于购买价 60%（{PaidPrice * 6 / 10}）";
                case 7: // 锁价
                {
                    long floor = Math.Max(1, (long)BankerOffer);
                    PriceFloor = floor;
                    SkillUsesRemaining--;
                    SkillUsedBy.Add(player.Id);
                    return $"【{skillName}】收购价已锁定底价 {floor}";
                }
                case 8: // 鉴定
                    Identified = true;
                    SkillUsesRemaining--;
                    SkillUsedBy.Add(player.Id);
                    return $"【{skillName}】已鉴定：挂拍后买家可见完整剩余金额分布";
                case 9: // 税务减免
                    TaxReductionCount += 5;
                    SkillUsesRemaining--;
                    SkillUsedBy.Add(player.Id);
                    return $"【{skillName}】接下来 5 次排除费用减半";
                default:
                    return null;
            }
        }

        /// <summary>系统结算（出售/开箱）所得，应用保底补差</summary>
        public long SettleIncome(long rawIncome)
        {
            if (!InsuranceActive) return rawIncome;
            long guarantee = PaidPrice * 6 / 10;
            return Math.Max(rawIncome, guarantee);
        }

        /// <summary>从数据库恢复扩展技能状态</summary>
        public void RestoreExtra(Dictionary<int, int> peeked, int[] detected, bool insurance, long? priceFloor, int taxCount, bool identified)
        {
            foreach (var kv in peeked) PeekedSlots[kv.Key] = kv.Value;
            foreach (var i in detected) DetectedSlots.Add(i);
            InsuranceActive = insurance;
            PriceFloor = priceFloor;
            TaxReductionCount = taxCount;
            Identified = identified;
        }

        public void RestoreMore(long? pendingOffer, DateTime? offerExpire, int elimsSinceOffer, int[] lucky, int[] trap, bool isDaily, string dailyDay)
        {
            PendingOffer = pendingOffer;
            PendingOfferExpireUtc = offerExpire;
            ElimsSinceOffer = elimsSinceOffer;
            foreach (var i in lucky) LuckySlots.Add(i);
            foreach (var i in trap) TrapSlots.Add(i);
            IsDaily = isDaily;
            DailyDay = dailyDay;
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
        /// <summary>细粒度并发锁（加锁顺序约定见 Player.Sync）</summary>
        public readonly object Sync = new();
    }
}
