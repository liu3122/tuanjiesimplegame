using cfg;
using SimpleGameServer.Config;

namespace SimpleGameServer.Game
{
    /// <summary>
    /// 核心玩法逻辑（服务端权威）：购买 / 排除 / 技能 / 出售 / 拍卖。
    /// 所有结果以 (ok, error, data) 形式返回，路由层转 JSON。
    /// </summary>
    public class GameService
    {
        private readonly GameStore _store;
        private readonly AuctionEvents _events;

        public GameService(GameStore store, AuctionEvents events)
        {
            _store = store;
            _events = events;
            Rush = new RushManager(store, this);
            Duel = new DuelManager(store);
        }

        public RushManager Rush { get; }

        public DuelManager Duel { get; }

        // ---------------- 宝箱 ----------------

        public (bool ok, string err, object data) BuyChest(Player player, int configId)
        {
            var config = GameTables.Tables.TbChest.DataList.FirstOrDefault(c => c.Id == configId);
            if (config == null)
            {
                return (false, "CONFIG_NOT_FOUND", null);
            }
            int minLv = Levels.MinLevelForChest(configId);
            if (player.Level < minLv)
            {
                return (false, "LEVEL_LOCKED", new { needLevel = minLv });
            }
            lock (player.Sync)
            {
                if (player.Gold < config.Price)
                {
                    return (false, "NOT_ENOUGH_GOLD", null);
                }
                player.Gold -= config.Price;
                var chest = new Chest(_store.AllocChestUid(), config, _store.Rng) { OwnerId = player.Id, PaidPrice = config.Price };
                _store.Chests[chest.Uid] = chest;
                Db.SaveChest(chest);
                Db.SavePlayer(player);
                Db.AddGoldTx(player.Id, -config.Price, "buy_chest", chest.Uid);
                return (true, null, new { chestUid = chest.Uid, gold = player.Gold });
            }
        }

        public (bool ok, string err, object data) Eliminate(Player player, long chestUid, int slotIndex)
        {
            if (!_store.Chests.TryGetValue(chestUid, out var chest) || chest.OwnerId != player.Id)
            {
                return (false, "CHEST_NOT_FOUND", null);
            }
            lock (chest.Sync)
            {
                if (chest.State != ChestState.Owned || chest.RemainingCount <= 1 || slotIndex < 0 || slotIndex >= chest.Amounts.Length || chest.Eliminated[slotIndex])
                {
                    return (false, "BAD_SLOT", null);
                }
                chest.ExpireOfferIfNeeded();
                int cost = chest.ChargeEliminateCost();
                lock (player.Sync)
                {
                    if (player.Gold < cost)
                    {
                        return (false, "NOT_ENOUGH_GOLD", null);
                    }
                }
                var (revealed, special, err) = chest.EliminateEx(slotIndex, player);
                if (err != null)
                {
                    return (false, err, null);
                }
                string specialMsg = null;
                lock (player.Sync)
                {
                    player.Gold -= cost; // 成功后才扣费（失败不扣，修复旧版先扣后退的窗口）
                    if (special > 0)
                    {
                        player.Gold += special;
                        specialMsg = "幸运格 +" + special;
                    }
                    else if (special < 0)
                    {
                        player.Gold = Math.Max(0, player.Gold + special);
                        specialMsg = "陷阱格 " + special;
                    }
                }
                if (special > 0) Db.AddGoldTx(player.Id, special, "lucky_slot", chest.Uid);
                else if (special < 0) Db.AddGoldTx(player.Id, special, "trap_slot", chest.Uid);
                bool offerMade = chest.TryMakeOffer(_store.Rng);
                Db.SaveChest(chest);
                Db.SavePlayer(player);
                if (cost > 0) Db.AddGoldTx(player.Id, -cost, "eliminate_fee", chest.Uid);
                return (true, null, new
                {
                    revealedAmount = revealed,
                    cost,
                    special,
                    specialMsg,
                    offerMade,
                    pendingOffer = chest.PendingOffer,
                    offerExpireInSec = chest.PendingOfferExpireUtc != null
                        ? Math.Max(0, (int)(chest.PendingOfferExpireUtc.Value - DateTime.UtcNow).TotalSeconds) : 0,
                    gold = player.Gold,
                });
            }
        }

        public (bool ok, string err, object data) UseSkill(Player player, long chestUid, int skillType, int? slotIndex = null)
        {
            if (!_store.Chests.TryGetValue(chestUid, out var chest) || chest.OwnerId != player.Id)
            {
                return (false, "CHEST_NOT_FOUND", null);
            }
            var character = GameTables.Tables.TbCharacter.DataList.FirstOrDefault(c => c.SkillType == skillType);
            if (character == null)
            {
                return (false, "BAD_SKILL", null);
            }
            int skillMinLv = Levels.MinLevelForSkill(skillType);
            if (player.Level < skillMinLv)
            {
                return (false, "LEVEL_LOCKED", new { needLevel = skillMinLv });
            }
            lock (chest.Sync)
            {
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                lock (player.Sync)
                {
                    if (player.SkillCooldowns.TryGetValue(character.SkillType, out var until) && now < until)
                    {
                        return (false, "ERR_COOLDOWN", new { remainSec = (int)(until - now) });
                    }
                }
                string result = chest.UseSkill(player, character.SkillType, character.SkillName, slotIndex);
                if (result == null || result.StartsWith("ERR"))
                {
                    return (false, result ?? "SKILL_FAILED", null);
                }
                lock (player.Sync)
                {
                    if (character.SkillCooldownSec > 0)
                    {
                        player.SkillCooldowns[character.SkillType] = now + character.SkillCooldownSec;
                    }
                }
                Db.SaveChest(chest);
                Db.SavePlayer(player);
                return (true, null, new
                {
                    message = result,
                    skillUsesRemaining = chest.SkillUsesRemaining,
                    freeEliminateBuff = chest.FreeEliminateBuff,
                    peekedSlots = chest.PeekedSlots.ToDictionary(kv => kv.Key, kv => kv.Value),
                    detectedSlots = chest.DetectedSlots.ToList(),
                    skillCooldowns = SkillCooldownView(player),
                });
            }
        }

        /// <summary>玩家技能冷却视图：skillType → 剩余秒（仅 >0 的）</summary>
        internal static Dictionary<int, int> SkillCooldownView(Player player)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var view = new Dictionary<int, int>();
            foreach (var kv in player.SkillCooldowns)
            {
                int remain = (int)(kv.Value - now);
                if (remain > 0) view[kv.Key] = remain;
            }
            return view;
        }

        public (bool ok, string err, object data) SellToBanker(Player player, long chestUid)
        {
            if (!_store.Chests.TryGetValue(chestUid, out var chest) || chest.OwnerId != player.Id)
            {
                return (false, "CHEST_NOT_FOUND", null);
            }
            lock (chest.Sync)
            {
                if (chest.State != ChestState.Owned)
                {
                    return (false, "NOT_OWNED", null);
                }
                long income;
                if (chest.RemainingCount <= 1)
                {
                    income = chest.SettleIncome(chest.OpenFinal());
                }
                else
                {
                    income = chest.SettleIncome(Math.Max(1, (long)Math.Round(chest.BankerOffer)));
                    chest.State = ChestState.Finished;
                }
                chest.OwnerId = 0; // 系统回收
                lock (player.Sync)
                {
                    player.Gold += income;
                    Levels.AddXp(player, Levels.XpChestSettle); // 开箱结算经验
                    player.Opens++;
                }
                Db.SaveChest(chest);
                Db.SavePlayer(player);
                Db.AddGoldTx(player.Id, income, "sell_banker", chest.Uid);
                Db.AddGoldTx(player.Id, 0, "chest_settled", chest.Uid); // 开箱完成计数（出售/终局开箱）
                RecordDailyIfNeed(player, chest, income);
                return (true, null, new { income, gold = player.Gold, final = chest.EliminatedCount >= chest.Amounts.Length });
            }
        }

        // ---------------- 银行家报价 ----------------

        public (bool ok, string err, object data) AcceptOffer(Player player, long chestUid)
        {
            if (!_store.Chests.TryGetValue(chestUid, out var chest) || chest.OwnerId != player.Id)
                return (false, "CHEST_NOT_FOUND", null);
            lock (chest.Sync)
            {
                chest.ExpireOfferIfNeeded();
                if (chest.PendingOffer == null) return (false, "NO_OFFER", null);
                long income = chest.SettleIncome(chest.PendingOffer.Value);
                chest.PendingOffer = null;
                chest.PendingOfferExpireUtc = null;
                chest.State = ChestState.Finished;
                chest.OwnerId = 0;
                lock (player.Sync)
                {
                    player.Gold += income;
                    Levels.AddXp(player, Levels.XpChestSettle); // 开箱结算经验
                    player.Opens++;
                }
                Db.SaveChest(chest);
                Db.SavePlayer(player);
                Db.AddGoldTx(player.Id, income, "accept_offer", chest.Uid);
                Db.AddGoldTx(player.Id, 0, "chest_settled", chest.Uid); // 开箱完成计数（接受报价）
                RecordDailyIfNeed(player, chest, income);
                return (true, null, new { income, gold = player.Gold });
            }
        }

        public (bool ok, string err, object data) DeclineOffer(Player player, long chestUid)
        {
            if (!_store.Chests.TryGetValue(chestUid, out var chest) || chest.OwnerId != player.Id)
                return (false, "CHEST_NOT_FOUND", null);
            lock (chest.Sync)
            {
                chest.ExpireOfferIfNeeded();
                if (chest.PendingOffer == null) return (false, "NO_OFFER", null);
                chest.PendingOffer = null;
                chest.PendingOfferExpireUtc = null;
                Db.SaveChest(chest);
                return (true, null, new { declined = true });
            }
        }

        // ---------------- 每日挑战 ----------------

        private static string TodayDay() => DateTime.UtcNow.ToString("yyyy-MM-dd");

        public (bool ok, string err, object data) BuyDailyChest(Player player)
        {
            var g = GameTables.Tables.TbGlobal;
            lock (player.Sync)
            {
                if (player.Gold < g.DailyTicketCost) return (false, "NOT_ENOUGH_GOLD", null);
                string day = TodayDay();
                int used = Db.DailyAttemptsUsed(day, player.Id);
                if (used >= g.DailyAttemptsPerDay) return (false, "DAILY_LIMIT", null);

                player.Gold -= g.DailyTicketCost;
                Db.AddGoldTx(player.Id, -g.DailyTicketCost, "daily_ticket", player.Id);

                var config = GameTables.Tables.TbChest[g.DailyChestId];
                int seed = day.GetHashCode(); // 全服同构种子
                var chest = new Chest(_store.AllocChestUid(), config, new Random(seed), true, seed)
                {
                    OwnerId = player.Id,
                    PaidPrice = g.DailyTicketCost,
                    DailyDay = day,
                };
                _store.Chests[chest.Uid] = chest;
                Db.SaveChest(chest);
                Db.SavePlayer(player);
                Db.RecordDailyAttempt(day, player.Id);
                return (true, null, new { chestUid = chest.Uid, attemptsLeft = g.DailyAttemptsPerDay - used - 1, gold = player.Gold });
            }
        }

        /// <summary>结算时若为挑战箱，记录当日最好成绩</summary>
        private void RecordDailyIfNeed(Player player, Chest chest, long income)
        {
            if (!chest.IsDaily || string.IsNullOrEmpty(chest.DailyDay)) return;
            Db.RecordDailyResult(chest.DailyDay, player.Id, player.Name, income);
        }

        /// <summary>新玩家教学箱：固定种子免费木箱（一次），客户端按 isTutorial 显示引导步骤</summary>
        public (bool ok, string err, object data) TutorialChest(Player player)
        {
            lock (player.Sync)
            {
                if (player.Opens > 0) return (false, "NOT_ROOKIE", null);
                if (_store.Chests.Values.Any(c => c.OwnerId == player.Id && c.IsTutorial)) return (false, "ALREADY_GRANTED", null);
                var config = GameTables.Tables.TbChest[1];
                var chest = new Chest(_store.AllocChestUid(), config, new Random(20260928)) { OwnerId = player.Id, PaidPrice = 0 };
                chest.IsTutorial = true;
                _store.Chests[chest.Uid] = chest;
                Db.SaveChest(chest);
                return (true, null, new { chestUid = chest.Uid });
            }
        }

        // ---------------- 广告变现（激励视频，服务端权威频控） ----------------

        /// <summary>跨日清零广告计数</summary>
        private static void RollAdDay(Player p)
        {
            string today = DateTime.UtcNow.ToString("yyyy-MM-dd");
            if (p.AdDailyDay != today)
            {
                p.AdDailyDay = today;
                p.AdDailyCount.Clear();
            }
        }

        /// <summary>观看完成发奖：校验开关/日限/冷却 → 金币或宝箱或清技能冷却</summary>
        public (bool ok, string err, object data) AdReward(Player player, string placementId)
        {
            lock (player.Sync)
            {
                if (!Features.IsOn(Features.Ad)) return (false, "FEATURE_DISABLED", null);
                var cfg = GameTables.Tables.TbAd.GetOrDefault(placementId);
                if (cfg == null) return (false, "BAD_PLACEMENT", null);
                RollAdDay(player);
                if (player.AdDailyCount.GetValueOrDefault(placementId) >= cfg.DailyLimit)
                {
                    return (false, "AD_LIMIT", new { remainToday = 0 });
                }
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                if (player.AdLastUse.TryGetValue(placementId, out var last) && now - last < cfg.CooldownSec)
                {
                    return (false, "AD_COOLDOWN", new { remainSec = (int)(cfg.CooldownSec - (now - last)) });
                }

                string message;
                if (cfg.RewardGold == 0 && cfg.RewardChestId == 0)
                {
                    // 清除全部技能冷却（ad_cooldown）
                    if (player.SkillCooldowns.Count == 0) return (false, "AD_NO_COOLDOWN", null);
                    player.SkillCooldowns.Clear();
                    message = "技能冷却已全部清除";
                }
                else if (cfg.RewardChestId > 0)
                {
                    int minLv = Levels.MinLevelForChest(cfg.RewardChestId);
                    if (player.Level < minLv) return (false, "LEVEL_LOCKED", new { needLevel = minLv });
                    var config = GameTables.Tables.TbChest[cfg.RewardChestId];
                    var chest = new Chest(_store.AllocChestUid(), config, _store.Rng) { OwnerId = player.Id, PaidPrice = 0 };
                    _store.Chests[chest.Uid] = chest;
                    Db.SaveChest(chest);
                    Db.AddGoldTx(player.Id, 0, "ad_chest", chest.Uid);
                    message = "获得 " + config.Name;
                }
                else
                {
                    player.Gold += cfg.RewardGold;
                    Db.AddGoldTx(player.Id, cfg.RewardGold, "ad_reward", 0);
                    message = "获得 " + cfg.RewardGold + " 金币";
                }
                player.AdLastUse[placementId] = now;
                player.AdDailyCount[placementId] = player.AdDailyCount.GetValueOrDefault(placementId) + 1;
                Db.SavePlayer(player);
                return (true, null, new { message, gold = player.Gold, ads = AdStatusView(player) });
            }
        }

        /// <summary>广告位状态视图：今日剩余次数 + 冷却秒（需持 player.Sync）</summary>
        internal static object AdStatusView(Player p)
        {
            RollAdDay(p);
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return GameTables.Tables.TbAd.DataList.ToDictionary(
                c => c.Id,
                c => new
                {
                    remainToday = Math.Max(0, c.DailyLimit - p.AdDailyCount.GetValueOrDefault(c.Id)),
                    cooldownSec = p.AdLastUse.TryGetValue(c.Id, out var last)
                        ? Math.Max(0, (int)(c.CooldownSec - (now - last))) : 0,
                });
        }

        public object DailyRank(Player viewer)
        {
            {
                string day = TodayDay();
                var rows = Db.DailyRank(day);
                var list = rows.Select((r, i) => new
                {
                    rank = i + 1,
                    name = r.name,
                    score = r.score,
                    isMe = r.playerId == viewer.Id,
                }).ToList();
                long? mine = Db.MyDailyBest(day, viewer.Id);
                return new { day, attemptsLeft = Math.Max(0, GameTables.Tables.TbGlobal.DailyAttemptsPerDay - Db.DailyAttemptsUsed(day, viewer.Id)), mine, list };
            }
        }

        // ---------------- 拍卖 ----------------

        public (bool ok, string err, object data) ListChest(Player player, long chestUid, long startPrice, long buyoutPrice)
        {
            if (!_store.Chests.TryGetValue(chestUid, out var chest) || chest.OwnerId != player.Id)
            {
                return (false, "CHEST_NOT_FOUND", null);
            }
            lock (chest.Sync)
            {
                if (chest.State != ChestState.Owned || chest.RemainingCount <= 1)
                {
                    return (false, "NOT_OWNED", null);
                }
                if (startPrice <= 0) return (false, "BAD_PRICE", null);
                if (buyoutPrice != 0 && buyoutPrice <= startPrice) return (false, "BAD_BUYOUT", null);

                chest.State = ChestState.Listed;
                var lot = new AuctionLot
                {
                    LotId = _store.AllocLotId(),
                    ChestUid = chest.Uid,
                    SellerId = player.Id,
                    SellerName = player.Name,
                    StartPrice = startPrice,
                    BuyoutPrice = buyoutPrice,
                    EndTimeUtc = DateTime.UtcNow.AddSeconds(GameTables.Tables.TbGlobal.AuctionDurationSec),
                };
                _store.Lots[lot.LotId] = lot;
                Db.SaveChest(chest);
                Db.SaveLot(lot);
                _ = _events.LotChanged(lot);
                return (true, null, new { lotId = lot.LotId });
            }
        }

        private static long MinStep(AuctionLot lot)
        {
            long baseBid = lot.CurrentBid > 0 ? lot.CurrentBid : lot.StartPrice;
            return Math.Max(1, (long)(baseBid * GameTables.Tables.TbGlobal.AuctionBidStepPct));
        }

        /// <summary>出价（服务端计算最低加价，预扣/退回由服务端管理）。锁序：Lot → Player（按 Id 排序）</summary>
        public (bool ok, string err, object data) Bid(Player player, long lotId)
        {
            if (!_store.Lots.TryGetValue(lotId, out var lot) || lot.Settled) return (false, "LOT_NOT_FOUND", null);
            lock (lot.Sync)
            {
                if (lot.SellerId == player.Id) return (false, "OWN_LOT", null);
                if (DateTime.UtcNow > lot.EndTimeUtc) return (false, "AUCTION_ENDED", null);
                if (lot.CurrentBidderId == player.Id) return (false, "ALREADY_TOP", null);

                long next = (lot.CurrentBid > 0 ? lot.CurrentBid : lot.StartPrice) + MinStep(lot);
                bool asBuyout = lot.BuyoutPrice > 0 && next >= lot.BuyoutPrice;
                if (asBuyout) next = lot.BuyoutPrice;

                // 退被超价者的预扣（与出价者排序加锁，避免死锁）
                Player loser = lot.CurrentBidderId > 0 && _store.Players.TryGetValue(lot.CurrentBidderId, out var l) ? l : null;
                var parties = new[] { player, loser }.Where(p => p != null).OrderBy(p => p.Id).ToList();
                foreach (var p in parties)
                {
                    lock (p.Sync)
                    {
                        if (p == player)
                        {
                            if (player.Gold + player.Escrow.GetValueOrDefault(lot.LotId) < next)
                            {
                                return (false, "NOT_ENOUGH_GOLD", null);
                            }
                            player.Gold += player.Escrow.GetValueOrDefault(lot.LotId);
                            player.Escrow.Remove(lot.LotId);
                            player.Gold -= next;
                            player.Escrow[lot.LotId] = next;
                        }
                        else
                        {
                            loser.Gold += loser.Escrow.GetValueOrDefault(lot.LotId);
                            loser.Escrow.Remove(lot.LotId);
                            Db.SavePlayer(loser);
                        }
                    }
                }

                lot.CurrentBid = next;
                lot.CurrentBidderId = player.Id;
                lot.CurrentBidderName = player.Name;
                lot.EndTimeUtc = lot.EndTimeUtc > DateTime.UtcNow.AddSeconds(12) ? lot.EndTimeUtc : DateTime.UtcNow.AddSeconds(12);
                Db.SavePlayer(player);
                Db.SaveLot(lot);
                _ = _events.LotChanged(lot);

                if (asBuyout)
                {
                    Settle(lot);
                }
                return (true, null, new { bid = next, buyout = asBuyout });
            }
        }

        public (bool ok, string err, object data) Buyout(Player player, long lotId)
        {
            if (!_store.Lots.TryGetValue(lotId, out var lot) || lot.Settled) return (false, "LOT_NOT_FOUND", null);
            lock (lot.Sync)
            {
                if (lot.SellerId == player.Id) return (false, "OWN_LOT", null);
                if (lot.BuyoutPrice <= 0) return (false, "NO_BUYOUT", null);
                if (DateTime.UtcNow > lot.EndTimeUtc) return (false, "AUCTION_ENDED", null);

                long price = lot.BuyoutPrice;
                lock (player.Sync)
                {
                    if (player.Gold + player.Escrow.GetValueOrDefault(lot.LotId) < price)
                    {
                        return (false, "NOT_ENOUGH_GOLD", null);
                    }
                    player.Gold += player.Escrow.GetValueOrDefault(lot.LotId);
                    player.Escrow.Remove(lot.LotId);
                    player.Gold -= price;
                    player.Escrow[lot.LotId] = price;
                }

                lot.CurrentBid = price;
                lot.CurrentBidderId = player.Id;
                lot.CurrentBidderName = player.Name;
                Db.SavePlayer(player);
                Db.SaveLot(lot);
                Settle(lot);
                return (true, null, new { buyout = price });
            }
        }

        /// <summary>到期/一口价结算（需在 lot.Sync 内调用）。锁序：Lot → Player(排序) → Chest</summary>
        public void Settle(AuctionLot lot)
        {
            if (lot.Settled) return;
            lot.Settled = true;
            var g = GameTables.Tables.TbGlobal;

            if (lot.CurrentBid > 0 && lot.CurrentBidderId != 0)
            {
                long sellerIncome = (long)Math.Round(lot.CurrentBid * (1f - g.AuctionFeeRate));
                // 玩家阶段：买家解预扣 + 卖家入账（按 Id 排序加锁）
                var buyer = _store.Players.TryGetValue(lot.CurrentBidderId, out var b) ? b : null;
                var seller = _store.Players.TryGetValue(lot.SellerId, out var s) ? s : null;
                foreach (var p in new[] { buyer, seller }.Where(p => p != null).OrderBy(p => p.Id))
                {
                    lock (p.Sync)
                    {
                        if (p == buyer)
                        {
                            buyer.Escrow.Remove(lot.LotId);
                        }
                        else
                        {
                            seller.Gold += sellerIncome;
                        }
                        Db.SavePlayer(p);
                    }
                }
                if (buyer != null) Db.AddGoldTx(buyer.Id, -lot.CurrentBid, "auction_buy", lot.LotId);
                if (seller != null) Db.AddGoldTx(seller.Id, sellerIncome, "auction_sell", lot.LotId);
                // 宝箱阶段：易主（不持 Player 锁进 Chest，遵守全局锁序）
                if (_store.Chests.TryGetValue(lot.ChestUid, out var chest))
                {
                    lock (chest.Sync)
                    {
                        chest.OwnerId = lot.CurrentBidderId;
                        chest.State = ChestState.Owned;
                        Db.SaveChest(chest);
                    }
                }
            }
            else
            {
                // 流拍退回
                if (_store.Chests.TryGetValue(lot.ChestUid, out var chest))
                {
                    lock (chest.Sync)
                    {
                        chest.State = ChestState.Owned;
                        Db.SaveChest(chest);
                    }
                }
            }
            Db.SaveLot(lot);
            _ = _events.LotSettled(lot);
        }

        /// <summary>机器人竞价 + 到期结算（由后台服务周期调用）</summary>
        public void BotTick()
        {
            var bots = _store.Players.Values.Where(p => p.Id < 0 && p.Token == null).ToList();
            if (bots.Count == 0) return;

            foreach (var lot in _store.Lots.Values.Where(l => !l.Settled).ToList())
            {
                lock (lot.Sync)
                {
                    if (lot.Settled) continue;
                    if (DateTime.UtcNow > lot.EndTimeUtc)
                    {
                        Settle(lot);
                        continue;
                    }
                    if ((lot.EndTimeUtc - DateTime.UtcNow).TotalSeconds < 5) continue;
                    if (_store.Rng.NextDouble() > 0.55) continue;

                    long next = (lot.CurrentBid > 0 ? lot.CurrentBid : lot.StartPrice) + MinStep(lot);
                    if (lot.BuyoutPrice > 0 && next >= lot.BuyoutPrice) continue;

                    if (!_store.Chests.TryGetValue(lot.ChestUid, out var chest)) continue;
                    double ev;
                    lock (chest.Sync) ev = chest.ExpectedValue;
                    double tolerance = 1.0 + _store.Rng.NextDouble() * 0.18;
                    double skillPremium = 1.0 + chest.SkillUsesRemaining * 0.05;
                    if (chest.Identified) skillPremium += 0.04; // 鉴定拍品信息透明，买家愿意多付
                    if (next > ev * tolerance * skillPremium) continue;

                    var bot = bots[_store.Rng.Next(bots.Count)];
                    // 退被超价玩家
                    if (lot.CurrentBidderId > 0 && _store.Players.TryGetValue(lot.CurrentBidderId, out var loser))
                    {
                        lock (loser.Sync)
                        {
                            loser.Gold += loser.Escrow.GetValueOrDefault(lot.LotId);
                            loser.Escrow.Remove(lot.LotId);
                            Db.SavePlayer(loser);
                        }
                    }
                    lot.CurrentBid = next;
                    lot.CurrentBidderId = bot.Id;
                    lot.CurrentBidderName = bot.Name;
                    Db.SaveLot(lot);
                    _ = _events.LotChanged(lot);
                }
            }
        }

        // ---------------- 查询 ----------------

        /// <summary>客户端状态视图：未揭示格金额绝不下发（玩家标量快照后逐箱加锁，无嵌套锁序风险）</summary>
        public object ClientState(Player player)
        {
            long gold; long escrowSum; int characterId, level, xp, xpNext, duelRating, opens;
            Dictionary<int, int> cooldowns; object lastSeasonReward;
            lock (player.Sync)
            {
                gold = player.Gold;
                escrowSum = player.Escrow.Values.Sum();
                characterId = player.CharacterId;
                level = player.Level; xp = player.Xp; xpNext = Levels.XpNext(player.Level, player.Xp);
                duelRating = player.DuelRating; opens = player.Opens;
                cooldowns = SkillCooldownView(player);
                lastSeasonReward = player.LastSeasonReward;
            }

            var chests = _store.Chests.Values
                .Where(c => c.OwnerId == player.Id)
                .OrderBy(c => c.Uid)
                .Select(c =>
                {
                    lock (c.Sync)
                    {
                        return new
                        {
                            uid = c.Uid,
                            configId = c.ConfigId,
                            state = (int)c.State,
                            eliminated = c.Eliminated,
                            revealedAmounts = c.Eliminated.Select((e, i) => e ? (int?)c.Amounts[i] : (int?)null).ToArray(),
                            remainingCount = c.RemainingCount,
                            remainingSum = c.RemainingSum,
                            expectedValue = Math.Round(c.ExpectedValue, 1),
                            bankerOffer = (long)Math.Round(c.BankerOffer),
                            nextEliminateCost = c.NextEliminateCost,
                            skillUsesRemaining = c.SkillUsesRemaining,
                            skillUsedByMe = c.SkillUsedBy.Contains(player.Id),
                            freeEliminateBuff = c.FreeEliminateBuff,
                            priceBuffNext = c.PriceBuffNext,
                            peekedSlots = c.PeekedSlots.ToDictionary(kv => kv.Key, kv => kv.Value),
                            detectedSlots = c.DetectedSlots.ToList(),
                            insuranceActive = c.InsuranceActive,
                            taxReductionCount = c.TaxReductionCount,
                            identified = c.Identified,
                            luckySlots = c.LuckySlots.ToList(),
                            trapSlots = c.TrapSlots.ToList(),
                            kind = Config.GameTables.Tables.TbChest[c.ConfigId].Kind,
                            isDaily = c.IsDaily,
                            isTutorial = c.IsTutorial,
                            pendingOffer = c.PendingOffer,
                            offerExpireInSec = c.PendingOfferExpireUtc != null
                                ? Math.Max(0, (int)(c.PendingOfferExpireUtc.Value - DateTime.UtcNow).TotalSeconds) : 0,
                        };
                    }
                })
                .ToList();

            return new
            {
                gold,
                escrow = escrowSum,
                characterId,
                opensTotal = opens,
                level,
                xp,
                xpNext,
                duelRating,
                seasonWeek = Db.WeekStartUtcIso(),
                lastSeasonReward,
                skillCooldowns = cooldowns,
                featureFlags = Features.View(),
                ads = AdStatusView(player),
                chests,
            };
        }

        /// <summary>四榜查询：gold_total/gold_week/opens_total/opens_week × 全球(global)/本地区(region)</summary>
        public object RankList(Player viewer, string board, string scope)
        {
            string region = scope == "region" ? (viewer?.Region ?? "CN") : null;
            if (viewer != null && !string.IsNullOrEmpty(region))
            {
                Db.SavePlayer(viewer); // 确保地区已持久化
            }
            var rows = Db.QueryBoard(board, region);
            return new
            {
                board,
                scope,
                myRegion = viewer?.Region ?? "CN",
                list = rows.Select((r, i) => new
                {
                    rank = i + 1,
                    name = r.name,
                    value = r.value,
                    isMe = viewer != null && r.playerId == viewer.Id,
                }).ToList(),
            };
        }

        /// <summary>对决天梯榜（Elo）</summary>
        public object DuelRankList(Player viewer)
        {
            var rows = Db.DuelRatingBoard(20);
            return new
            {
                myRating = viewer.DuelRating,
                list = rows.Select((r, i) => new
                {
                    rank = i + 1,
                    name = r.name,
                    value = r.value,
                    isMe = r.playerId == viewer.Id,
                }).ToList(),
            };
        }

        // ---------------- 分享 ----------------

        /// <summary>生成分享文案（服务端组装数据，实际分享由客户端 SDK 执行）</summary>
        public object ShareContent(Player viewer, string contentId)
        {
            string title, text;
            switch (contentId)
            {
                case "daily":
                {
                    string day = DateTime.UtcNow.ToString("yyyy-MM-dd");
                    long? best = Db.MyDailyBest(day, viewer.Id);
                    title = "宝箱商人·每日挑战";
                    text = best.HasValue
                        ? $"我在今日挑战拿到了 {best.Value:N0} 分，你能超过我吗？"
                        : "今日挑战开始了，全服同一个宝箱，来比比谁卖得更高！";
                    break;
                }
                case "rank":
                {
                    var entry = Db.QueryBoard("gold_total", null, 20);
                    var mine = entry.FirstOrDefault(r => r.playerId == viewer.Id);
                    title = "宝箱商人·富豪榜";
                    text = mine.value > 0
                        ? $"我在富豪榜第 {entry.IndexOf(mine) + 1} 名，身家 {mine.value:N0}，快来追我！"
                        : "宝箱商人开张了，来和我一较高下！";
                    break;
                }
                default:
                    title = "宝箱商人";
                    text = "排除、博弈、拍卖，来开你的宝箱！";
                    break;
            }
            return new { title, text, url = "https://your.game.link/invite?" + viewer.Id };
        }

        public void ReportShare(Player viewer, string channel, string contentId)
        {
            Db.RecordShare(viewer.Id, channel, contentId);
        }

        public object AuctionList(Player viewer)
        {
            var g = GameTables.Tables.TbGlobal;
            return _store.Lots.Values.Where(l => !l.Settled).Select(l =>
            {
                lock (l.Sync)
                {
                    Chest c = _store.Chests.TryGetValue(l.ChestUid, out var chest) ? chest : null;
                    int skillUses; bool identified; long ev; int remaining;
                    List<int> remainingAmounts;
                    if (c != null)
                    {
                        lock (c.Sync)
                        {
                            skillUses = c.SkillUsesRemaining;
                            identified = c.Identified;
                            ev = (long)Math.Round(c.ExpectedValue);
                            remaining = c.RemainingCount;
                            remainingAmounts = identified
                                ? Enumerable.Range(0, c.Amounts.Length).Where(i => !c.Eliminated[i]).Select(i => c.Amounts[i]).OrderByDescending(a => a).ToList()
                                : null;
                        }
                    }
                    else
                    {
                        skillUses = 0; identified = false; ev = 0; remaining = 0; remainingAmounts = null;
                    }
                    return new
                    {
                        lotId = l.LotId,
                        chestUid = l.ChestUid,
                        configId = c?.ConfigId ?? 0,
                        remainingCount = remaining,
                        expectedValue = ev,
                        skillUses = skillUses,
                        skillUsesMax = g.ChestMaxSkillUses,
                        seller = l.SellerName,
                        isMine = l.SellerId == viewer?.Id,
                        identified,
                        remainingAmounts,
                        startPrice = l.StartPrice,
                        buyoutPrice = l.BuyoutPrice,
                        currentBid = l.CurrentBid,
                        currentBidder = l.CurrentBidderName,
                        iAmTop = l.CurrentBidderId == viewer?.Id,
                        endInSec = Math.Max(0, (int)(l.EndTimeUtc - DateTime.UtcNow).TotalSeconds),
                    };
                }
            }).ToList();
        }
    }

    /// <summary>拍卖事件推送（SignalR hub 桥）</summary>
    public class AuctionEvents
    {
        public Func<AuctionLot, Task> OnLotChanged = _ => Task.CompletedTask;
        public Func<AuctionLot, Task> OnLotSettled = _ => Task.CompletedTask;

        public Task LotChanged(AuctionLot lot) => OnLotChanged(lot);
        public Task LotSettled(AuctionLot lot) => OnLotSettled(lot);
    }
}
