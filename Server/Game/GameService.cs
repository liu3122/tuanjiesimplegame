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
        }

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
                int cost = chest.NextEliminateCost;
                if (player.Gold < cost)
                {
                    return (false, "NOT_ENOUGH_GOLD", null);
                }
                player.Gold -= cost;
                int revealed = chest.Eliminate(slotIndex);
                Db.SaveChest(chest);
                Db.SavePlayer(player);
                if (cost > 0) Db.AddGoldTx(player.Id, -cost, "eliminate_fee", chest.Uid);
                return (true, null, new { revealedAmount = revealed, cost, gold = player.Gold });
            }
        }

        public (bool ok, string err, object data) UseSkill(Player player, long chestUid, int skillType)
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
                string result = chest.UseSkill(player, character.SkillType, character.SkillName);
                if (result == null || result.StartsWith("ERR"))
                {
                    return (false, result ?? "SKILL_FAILED", null);
                }
                Db.SaveChest(chest);
                return (true, null, new { message = result, skillUsesRemaining = chest.SkillUsesRemaining, freeEliminateBuff = chest.FreeEliminateBuff });
            }
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
                    income = chest.OpenFinal();
                }
                else
                {
                    income = Math.Max(1, (long)Math.Round(chest.BankerOffer));
                    chest.State = ChestState.Finished;
                }
                player.Gold += income;
                chest.OwnerId = 0; // 系统回收
                Db.SaveChest(chest);
                Db.SavePlayer(player);
                Db.AddGoldTx(player.Id, income, "sell_banker", chest.Uid);
                return (true, null, new { income, gold = player.Gold, final = chest.EliminatedCount >= 20 });
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
                // 买家：价款已预扣
                if (_store.Players.TryGetValue(lot.CurrentBidderId, out var buyer))
                {
                    buyer.Escrow.Remove(lot.LotId);
                    Db.SavePlayer(buyer);
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
                    }).ToList();

                return new
                {
                    gold = player.Gold,
                    escrow = player.Escrow.Values.Sum(),
                    characterId = player.CharacterId,
                    chests,
                };
            }
        }

        /// <summary>金币排行榜：真人按余额排序，混入机器人展示数据</summary>
        public object RankList(Player viewer)
        {
            lock (_store.Gate)
            {
                var fake = new[]
                {
                    (name: "沉舟侧畔", gold: 98_000L), (name: "点石成金", gold: 45_300L),
                    (name: "欧皇本皇", gold: 31_800L), (name: "白嫖大师", gold: 12_400L),
                    (name: "稳中求胜", gold: 6_900L),
                };
                var rows = fake.Select(f => new { name = f.name, gold = f.gold, isMe = false })
                    .Concat(_store.Players.Values.Where(p => p.Id > 0)
                        .Select(p => new { name = p.Name, gold = p.Gold, isMe = p != null && p.Id == viewer?.Id }))
                    .OrderByDescending(r => r.gold)
                    .Take(10)
                    .Select((r, i) => new { rank = i + 1, r.name, r.gold, r.isMe })
                    .ToList();
                return rows;
            }
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
