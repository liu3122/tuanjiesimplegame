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
            lock (_store.Gate)
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
            lock (_store.Gate)
            {
                if (!_store.Chests.TryGetValue(chestUid, out var chest) || chest.OwnerId != player.Id)
                {
                    return (false, "CHEST_NOT_FOUND", null);
                }
                if (chest.State != ChestState.Owned || chest.RemainingCount <= 1 || slotIndex < 0 || slotIndex >= 20 || chest.Eliminated[slotIndex])
                {
                    return (false, "BAD_SLOT", null);
                }
                chest.ExpireOfferIfNeeded();
                int cost = chest.ChargeEliminateCost();
                if (player.Gold < cost)
                {
                    return (false, "NOT_ENOUGH_GOLD", null);
                }
                player.Gold -= cost;
                var (revealed, special, err) = chest.EliminateEx(slotIndex, player);
                if (err != null)
                {
                    return (false, err, null);
                }
                string specialMsg = null;
                if (special > 0)
                {
                    player.Gold += special;
                    Db.AddGoldTx(player.Id, special, "lucky_slot", chest.Uid);
                    specialMsg = "幸运格 +" + special;
                }
                else if (special < 0)
                {
                    player.Gold = Math.Max(0, player.Gold + special);
                    Db.AddGoldTx(player.Id, special, "trap_slot", chest.Uid);
                    specialMsg = "陷阱格 " + special;
                }
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
            lock (_store.Gate)
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
                // 技能冷却（服务端权威，跨宝箱）：同一技能用过后需等待
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                if (player.SkillCooldowns.TryGetValue(character.SkillType, out var until) && now < until)
                {
                    return (false, "ERR_COOLDOWN", new { remainSec = (int)(until - now) });
                }
                string result = chest.UseSkill(player, character.SkillType, character.SkillName, slotIndex);
                if (result == null || result.StartsWith("ERR"))
                {
                    return (false, result ?? "SKILL_FAILED", null);
                }
                if (character.SkillCooldownSec > 0)
                {
                    player.SkillCooldowns[character.SkillType] = now + character.SkillCooldownSec;
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
            lock (_store.Gate)
            {
                if (!_store.Chests.TryGetValue(chestUid, out var chest) || chest.OwnerId != player.Id)
                {
                    return (false, "CHEST_NOT_FOUND", null);
                }
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
                player.Gold += income;
                chest.OwnerId = 0; // 系统回收
                Levels.AddXp(player, Levels.XpChestSettle); // 开箱结算经验
                Db.SaveChest(chest);
                Db.SavePlayer(player);
                Db.AddGoldTx(player.Id, income, "sell_banker", chest.Uid);
                Db.AddGoldTx(player.Id, 0, "chest_settled", chest.Uid); // 开箱完成计数（出售/终局开箱）
                RecordDailyIfNeed(player, chest, income);
                return (true, null, new { income, gold = player.Gold, final = chest.EliminatedCount >= 20 });
            }
        }

        // ---------------- 银行家报价 ----------------

        public (bool ok, string err, object data) AcceptOffer(Player player, long chestUid)
        {
            lock (_store.Gate)
            {
                if (!_store.Chests.TryGetValue(chestUid, out var chest) || chest.OwnerId != player.Id)
                    return (false, "CHEST_NOT_FOUND", null);
                chest.ExpireOfferIfNeeded();
                if (chest.PendingOffer == null) return (false, "NO_OFFER", null);
                long income = chest.SettleIncome(chest.PendingOffer.Value);
                chest.PendingOffer = null;
                chest.PendingOfferExpireUtc = null;
                chest.State = ChestState.Finished;
                player.Gold += income;
                chest.OwnerId = 0;
                Levels.AddXp(player, Levels.XpChestSettle); // 开箱结算经验
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
            lock (_store.Gate)
            {
                if (!_store.Chests.TryGetValue(chestUid, out var chest) || chest.OwnerId != player.Id)
                    return (false, "CHEST_NOT_FOUND", null);
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
            lock (_store.Gate)
            {
                var g = GameTables.Tables.TbGlobal;
                if (player.Gold < g.DailyTicketCost) return (false, "NOT_ENOUGH_GOLD", null);
                string day = TodayDay();
                int used = Db.DailyAttemptsUsed(day, player.Id);
                if (used >= g.DailyAttemptsPerDay) return (false, "DAILY_LIMIT", null);

                player.Gold -= g.DailyTicketCost;
                Db.AddGoldTx(player.Id, -g.DailyTicketCost, "daily_ticket", player.Id);

                var config = GameTables.Tables.TbChest[g.DailyChestId];
                int seed = day.GetHashCode(); // 全服同构种子
                var chest = new Chest(_store.AllocChestUid(), config, _store.Rng, true, seed)
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
            lock (_store.Gate)
            {
                if (Db.OpensCount(player.Id) > 0) return (false, "NOT_ROOKIE", null);
                if (_store.Chests.Values.Any(c => c.OwnerId == player.Id && c.IsTutorial)) return (false, "ALREADY_GRANTED", null);
                var config = GameTables.Tables.TbChest[1];
                var chest = new Chest(_store.AllocChestUid(), config, new Random(20260928)) { OwnerId = player.Id, PaidPrice = 0 };
                chest.IsTutorial = true;
                _store.Chests[chest.Uid] = chest;
                Db.SaveChest(chest);
                return (true, null, new { chestUid = chest.Uid });
            }
        }

        public object DailyRank(Player viewer)
        {
            lock (_store.Gate)
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
            lock (_store.Gate)
            {
                if (!_store.Chests.TryGetValue(chestUid, out var chest) || chest.OwnerId != player.Id)
                {
                    return (false, "CHEST_NOT_FOUND", null);
                }
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
                _store.Lots.Add(lot);
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

        /// <summary>出价（服务端计算最低加价，预扣/退回由服务端管理）</summary>
        public (bool ok, string err, object data) Bid(Player player, long lotId)
        {
            lock (_store.Gate)
            {
                var lot = _store.Lots.FirstOrDefault(l => l.LotId == lotId && !l.Settled);
                if (lot == null) return (false, "LOT_NOT_FOUND", null);
                if (lot.SellerId == player.Id) return (false, "OWN_LOT", null);
                if (DateTime.UtcNow > lot.EndTimeUtc) return (false, "AUCTION_ENDED", null);
                if (lot.CurrentBidderId == player.Id) return (false, "ALREADY_TOP", null);

                long next = (lot.CurrentBid > 0 ? lot.CurrentBid : lot.StartPrice) + MinStep(lot);
                bool asBuyout = lot.BuyoutPrice > 0 && next >= lot.BuyoutPrice;
                if (asBuyout) next = lot.BuyoutPrice;

                if (player.Gold + player.Escrow.GetValueOrDefault(lot.LotId) < next)
                {
                    return (false, "NOT_ENOUGH_GOLD", null);
                }
                // 退自己旧预扣、预扣新价
                player.Gold += player.Escrow.GetValueOrDefault(lot.LotId);
                player.Escrow.Remove(lot.LotId);
                player.Gold -= next;
                player.Escrow[lot.LotId] = next;

                // 退被超价者的预扣
                if (lot.CurrentBidderId > 0 && _store.Players.TryGetValue(lot.CurrentBidderId, out var loser))
                {
                    loser.Gold += loser.Escrow.GetValueOrDefault(lot.LotId);
                    loser.Escrow.Remove(lot.LotId);
                    Db.SavePlayer(loser);
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
            lock (_store.Gate)
            {
                var lot = _store.Lots.FirstOrDefault(l => l.LotId == lotId && !l.Settled);
                if (lot == null) return (false, "LOT_NOT_FOUND", null);
                if (lot.SellerId == player.Id) return (false, "OWN_LOT", null);
                if (lot.BuyoutPrice <= 0) return (false, "NO_BUYOUT", null);
                if (DateTime.UtcNow > lot.EndTimeUtc) return (false, "AUCTION_ENDED", null);

                long price = lot.BuyoutPrice;
                if (player.Gold + player.Escrow.GetValueOrDefault(lot.LotId) < price)
                {
                    return (false, "NOT_ENOUGH_GOLD", null);
                }
                player.Gold += player.Escrow.GetValueOrDefault(lot.LotId);
                player.Escrow.Remove(lot.LotId);
                player.Gold -= price;
                player.Escrow[lot.LotId] = price;

                lot.CurrentBid = price;
                lot.CurrentBidderId = player.Id;
                lot.CurrentBidderName = player.Name;
                Db.SavePlayer(player);
                Db.SaveLot(lot);
                Settle(lot);
                return (true, null, new { buyout = price });
            }
        }

        /// <summary>到期/一口价结算（需在 lock 内调用）</summary>
        public void Settle(AuctionLot lot)
        {
            if (lot.Settled) return;
            lot.Settled = true;
            var g = GameTables.Tables.TbGlobal;

            if (lot.CurrentBid > 0 && lot.CurrentBidderId != 0)
            {
                long sellerIncome = (long)Math.Round(lot.CurrentBid * (1f - g.AuctionFeeRate));
                // 买家：价款已预扣（补记支出流水，经济报表对账用）
                if (_store.Players.TryGetValue(lot.CurrentBidderId, out var buyer))
                {
                    buyer.Escrow.Remove(lot.LotId);
                    Db.SavePlayer(buyer);
                    Db.AddGoldTx(lot.CurrentBidderId, -lot.CurrentBid, "auction_buy", lot.LotId);
                }
                // 卖家入账（扣抽成）
                if (_store.Players.TryGetValue(lot.SellerId, out var seller))
                {
                    seller.Gold += sellerIncome;
                    Db.SavePlayer(seller);
                    Db.AddGoldTx(seller.Id, sellerIncome, "auction_sell", lot.LotId);
                }
                // 宝箱易主：新玩家技能可用（额度不重置，SkillUsedBy 含买家则其不可再用）
                if (_store.Chests.TryGetValue(lot.ChestUid, out var chest))
                {
                    chest.OwnerId = lot.CurrentBidderId;
                    chest.State = ChestState.Owned;
                    Db.SaveChest(chest);
                }
            }
            else
            {
                // 流拍退回
                if (_store.Chests.TryGetValue(lot.ChestUid, out var chest))
                {
                    chest.State = ChestState.Owned;
                    Db.SaveChest(chest);
                }
            }
            Db.SaveLot(lot);
            _ = _events.LotSettled(lot);
        }

        /// <summary>机器人竞价 + 到期结算（由后台服务周期调用）</summary>
        public void BotTick()
        {
            lock (_store.Gate)
            {
                var bots = _store.Players.Values.Where(p => p.Id < 0 && p.Token == null).ToList();
                if (bots.Count == 0) return;

                foreach (var lot in _store.Lots.Where(l => !l.Settled).ToList())
                {
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
                    double tolerance = 1.0 + _store.Rng.NextDouble() * 0.18;
                    double skillPremium = 1.0 + chest.SkillUsesRemaining * 0.05;
                    if (chest.Identified) skillPremium += 0.04; // 鉴定拍品信息透明，买家愿意多付
                    if (next > chest.ExpectedValue * tolerance * skillPremium) continue;

                    var bot = bots[_store.Rng.Next(bots.Count)];
                    // 退被超价玩家
                    if (lot.CurrentBidderId > 0 && _store.Players.TryGetValue(lot.CurrentBidderId, out var loser))
                    {
                        loser.Gold += loser.Escrow.GetValueOrDefault(lot.LotId);
                        loser.Escrow.Remove(lot.LotId);
                        Db.SavePlayer(loser);
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

        /// <summary>客户端状态视图：未揭示格金额绝不下发</summary>
        public object ClientState(Player player)
        {
            lock (_store.Gate)
            {
                var chests = _store.Chests.Values
                    .Where(c => c.OwnerId == player.Id)
                    .OrderBy(c => c.Uid)
                    .Select(c => new
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
                    }).ToList();

                return new
                {
                    gold = player.Gold,
                    escrow = player.Escrow.Values.Sum(),
                    characterId = player.CharacterId,
                    opensTotal = Db.OpensCount(player.Id),
                    level = player.Level,
                    xp = player.Xp,
                    xpNext = Levels.XpNext(player.Level, player.Xp),
                    duelRating = player.DuelRating,
                    seasonWeek = Db.WeekStartUtcIso(),
                    lastSeasonReward = Db.MySeasonReward(player.Id),
                    skillCooldowns = SkillCooldownView(player),
                    featureFlags = Features.View(),
                    chests,
                };
            }
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
            lock (_store.Gate)
            {
                var g = GameTables.Tables.TbGlobal;
                return _store.Lots.Where(l => !l.Settled).Select(l =>
                {
                    _store.Chests.TryGetValue(l.ChestUid, out var c);
                    return new
                    {
                        lotId = l.LotId,
                        chestUid = l.ChestUid,
                        configId = c?.ConfigId ?? 0,
                        remainingCount = c?.RemainingCount ?? 0,
                        expectedValue = c != null ? (long)Math.Round(c.ExpectedValue) : 0,
                        skillUses = c?.SkillUsesRemaining ?? 0,
                        skillUsesMax = g.ChestMaxSkillUses,
                        seller = l.SellerName,
                        isMine = l.SellerId == viewer?.Id,
                        identified = c?.Identified ?? false,
                        remainingAmounts = (c != null && c.Identified)
                            ? Enumerable.Range(0, c.Amounts.Length).Where(i => !c.Eliminated[i]).Select(i => c.Amounts[i]).OrderByDescending(a => a).ToList()
                            : null,
                        startPrice = l.StartPrice,
                        buyoutPrice = l.BuyoutPrice,
                        currentBid = l.CurrentBid,
                        currentBidder = l.CurrentBidderName,
                        iAmTop = l.CurrentBidderId == viewer?.Id,
                        endInSec = Math.Max(0, (int)(l.EndTimeUtc - DateTime.UtcNow).TotalSeconds),
                    };
                }).ToList();
            }
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
