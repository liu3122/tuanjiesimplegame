using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using cfg;
using SimpleGameServer.Config;

namespace SimpleGameServer.Game
{
    /// <summary>
    /// 镜像对决（1v1 即时对抗）：两名玩家各开一个「同金额池、不同洗牌位」的镜像宝箱，
    /// 并行排除 / 用技能（沿用技能冷却，跨玩法共享）/ 应对银行家报价，随时卖给银行锁定分数；
    /// 双方锁定或超时后比分数，高分者赢走奖池（入场金×2 扣抽成），平分则各退入场金。
    /// 对手排除的格号与金额公开（同池不同位 → 只泄露「池面」不泄露格位，信息战核心）。
    /// 通信：原生 WebSocket（/ws/duel），JSON 事件双向推送；对手不足时机器人补位。
    /// 对决内排除免费（FreeEliminateBuff 预置），专注时机博弈；保底技能因 PaidPrice=0 无效。
    /// </summary>
    public class DuelRoom
    {
        private enum Phase { Matching, Countdown, Match, Settled }

        private readonly GameStore _store;
        private readonly DuelManager _manager;
        private readonly object _gate = new();
        private readonly Random _rng = new();

        private Phase _phase = Phase.Matching;
        private readonly Dictionary<long, Player> _players = new();
        private readonly Dictionary<long, WebSocket> _sockets = new();
        private readonly Dictionary<long, Chest> _chests = new();
        private readonly Dictionary<long, long> _scores = new();
        private readonly HashSet<long> _finished = new();
        private readonly Dictionary<long, double> _botTargets = new();
        /// <summary>断线宽限（重连窗口；过期未归按收购价锁定）</summary>
        private readonly Dictionary<long, DateTime> _grace = new();
        private DateTime _phaseEndUtc;
        private Timer? _timer;

        public int DuelId { get; }
        /// <summary>对赌房模式：入场金=宝箱售价，赢家独得两个宝箱的结算总价值（扣抽成）；平局各得自己箱子的价值</summary>
        public bool PotMode { get; }

        public DuelRoom(GameStore store, DuelManager manager, int duelId, bool potMode = false)
        {
            _store = store;
            _manager = manager;
            DuelId = duelId;
            PotMode = potMode;
        }

        /// <summary>入场金：普通对决=DuelStake；对赌房=对决宝箱售价</summary>
        private long Stake => PotMode
            ? GameTables.Tables.TbChest[GameTables.Tables.TbGlobal.DuelChestId].Price
            : GameTables.Tables.TbGlobal.DuelStake;

        public int WaitingCount { get { lock (_gate) return _players.Count(p => p.Key > 0); } }
        public bool Joinable { get { lock (_gate) return _phase == Phase.Matching && _players.Count < 2; } }
        public bool HasPlayer(long pid) { lock (_gate) return _players.ContainsKey(pid); }

        // ---------------- 加入与连接 ----------------

        public (bool ok, string err) Join(Player player)
        {
            lock (_gate)
            {
                if (_phase != Phase.Matching) return (false, "ROOM_RUNNING");
                if (_players.ContainsKey(player.Id)) return (true, null); // 幂等
                if (_players.Count >= 2) return (false, "ROOM_FULL");
                long stake = Stake;
                lock (player.Sync)
                {
                    if (player.Gold < stake) return (false, "NOT_ENOUGH_GOLD");
                    player.Gold -= stake;
                }
                Db.SavePlayer(player);
                Db.AddGoldTx(player.Id, -stake, "duel_stake", DuelId);
                Db.RecordStake(player.Id, "duel", DuelId, (int)stake); // 重启退款凭据
                _players[player.Id] = player;
                Broadcast(new { type = "joined", name = player.Name, count = _players.Count, need = 2 });
                return (true, null);
            }
        }

        public void AttachSocket(long playerId, WebSocket socket)
        {
            lock (_gate)
            {
                _sockets[playerId] = socket;
                _grace.Remove(playerId); // 宽限期内重连：恢复对局
            }
            SendTo(playerId, PersonalSnapshot(playerId));
        }

        public void DetachSocket(long playerId)
        {
            lock (_gate)
            {
                _sockets.Remove(playerId);
                // 对局中断线：给 8 秒重连宽限，过期未归按当前收购价锁定
                if (_phase == Phase.Match && playerId > 0 && !_finished.Contains(playerId) && _chests.ContainsKey(playerId))
                {
                    _grace[playerId] = DateTime.UtcNow.AddSeconds(8);
                }
            }
        }

        // ---------------- 生命周期 ----------------

        public void StartLifecycle()
        {
            var g = GameTables.Tables.TbGlobal;
            _phaseEndUtc = DateTime.UtcNow.AddSeconds(g.RushBotFillDelaySec);
            _timer = new Timer(_ => Tick(), null, 500, 250);
        }

        private void Tick()
        {
            try
            {
                lock (_gate)
                {
                    switch (_phase)
                    {
                        case Phase.Matching:
                            if (DateTime.UtcNow >= _phaseEndUtc || _players.Count >= 2)
                            {
                                while (_players.Count < 2)
                                {
                                    var bot = _store.CreateRushBot();
                                    _players[bot.Id] = bot;
                                    Broadcast(new { type = "joined", name = bot.Name, count = _players.Count, need = 2 });
                                }
                                BeginCountdown();
                            }
                            break;

                        case Phase.Countdown:
                            Broadcast(new { type = "countdown", remainSec = (int)Math.Ceiling((_phaseEndUtc - DateTime.UtcNow).TotalSeconds) });
                            if (DateTime.UtcNow >= _phaseEndUtc) BeginMatch();
                            break;

                        case Phase.Match:
                            // 断线宽限到期：按收购价锁定
                            foreach (var pid in _grace.Where(kv => DateTime.UtcNow >= kv.Value).Select(kv => kv.Key).ToList())
                            {
                                _grace.Remove(pid);
                                if (!_finished.Contains(pid) && _chests.ContainsKey(pid)) LockScore(pid);
                            }
                            // 报价过期推送
                            foreach (var (pid, chest) in _chests)
                            {
                                if (pid > 0 && chest.PendingOffer != null)
                                {
                                    chest.ExpireOfferIfNeeded();
                                    if (chest.PendingOffer == null) SendTo(pid, new { type = "offerGone" });
                                }
                            }
                            Broadcast(new { type = "remainSec", remainSec = (int)Math.Ceiling((_phaseEndUtc - DateTime.UtcNow).TotalSeconds) });
                            BotAct();
                            if (DateTime.UtcNow >= _phaseEndUtc && _phase == Phase.Match)
                            {
                                foreach (var pid in _players.Keys.Where(id => !_finished.Contains(id)).ToList()) LockScore(pid);
                            }
                            break;
                    }
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("[DuelRoom] tick error: " + e.Message);
            }
        }

        private void BeginCountdown()
        {
            _phase = Phase.Countdown;
            _phaseEndUtc = DateTime.UtcNow.AddSeconds(3);
            Broadcast(new { type = "phase", phase = "countdown", countdownSec = 3 });
        }

        private void BeginMatch()
        {
            var g = GameTables.Tables.TbGlobal;
            var config = GameTables.Tables.TbChest[g.DuelChestId];
            int poolSeed = _rng.Next();
            foreach (var pid in _players.Keys.ToList())
            {
                // 同种子构造 → 金额池完全一致；再各自独立洗位 → 镜像但格位不同
                var chest = new Chest(_store.AllocChestUid(), config, new Random(poolSeed))
                {
                    OwnerId = pid,
                    PaidPrice = Stake, // 保底技能兜底 60% 入场金（对决内有效）
                    FreeEliminateBuff = 99,  // 对决内排除免费（节奏优先）
                };
                chest.ShuffleAmounts(new Random(_rng.Next()));
                _chests[pid] = chest;
                if (pid < 0) _botTargets[pid] = (double)Stake * (1.0 + _rng.NextDouble() * 1.2);
            }
            _phase = Phase.Match;
            _phaseEndUtc = DateTime.UtcNow.AddSeconds(g.DuelMatchSec);
            Broadcast(new { type = "phase", phase = "match", matchSec = g.DuelMatchSec });
            foreach (var pid in _players.Keys) SendTo(pid, PersonalSnapshot(pid));
        }

        // ---------------- 玩家操作（WS 入口，均为幂等失败返回 err 事件） ----------------

        public (bool ok, string err) Eliminate(long pid, int slot)
        {
            lock (_gate)
            {
                if (!CanAct(pid, out var chest, out var err)) { SendErr(pid, err); return (false, err); }
                if (slot < 0 || slot >= chest.Amounts.Length) { SendErr(pid, "BAD_SLOT"); return (false, "BAD_SLOT"); }
                chest.ExpireOfferIfNeeded();
                var (revealed, special, err2) = chest.EliminateEx(slot, _players[pid]);
                if (err2 != null) { SendErr(pid, err2); return (false, err2); }
                if (special != 0)
                {
                    var p = _players[pid];
                    p.Gold = Math.Max(0, p.Gold + special);
                    Db.SavePlayer(p);
                    Db.AddGoldTx(pid, special, special > 0 ? "lucky_slot" : "trap_slot", DuelId);
                }
                bool offerMade = chest.TryMakeOffer(_rng);
                BroadcastExcept(pid, new { type = "oppAction", kind = "eliminate", slot, amount = revealed });
                SendTo(pid, new { type = "me", me = ChestView(chest, pid), offerMade });
                CheckAutoEnd();
                return (true, null);
            }
        }

        public (bool ok, string err) Skill(long pid, int skillType, int? slotIndex)
        {
            lock (_gate)
            {
                if (!CanAct(pid, out var chest, out var err)) { SendErr(pid, err); return (false, err); }
                var character = GameTables.Tables.TbCharacter.DataList.FirstOrDefault(c => c.SkillType == skillType);
                if (character == null) { SendErr(pid, "BAD_SKILL"); return (false, "BAD_SKILL"); }
                var player = _players[pid];
                int skillMinLv = Levels.MinLevelForSkill(skillType);
                if (player.Level < skillMinLv)
                {
                    SendTo(pid, new { type = "err", code = "LEVEL_LOCKED", needLevel = skillMinLv });
                    return (false, "LEVEL_LOCKED");
                }
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                lock (player.Sync)
                {
                    if (player.SkillCooldowns.TryGetValue(skillType, out var until) && now < until)
                    {
                        SendTo(pid, new { type = "err", code = "ERR_COOLDOWN", remainSec = (int)(until - now) });
                        return (false, "ERR_COOLDOWN");
                    }
                }
                string result = chest.UseSkill(player, skillType, character.SkillName, slotIndex);
                if (result == null || result.StartsWith("ERR")) { SendErr(pid, result ?? "SKILL_FAILED"); return (false, result ?? "SKILL_FAILED"); }
                if (character.SkillCooldownSec > 0)
                {
                    lock (player.Sync)
                    {
                        player.SkillCooldowns[skillType] = now + character.SkillCooldownSec;
                    }
                    Db.SavePlayer(player);
                }
                BroadcastExcept(pid, new { type = "oppAction", kind = "skill", skillName = character.SkillName });
                SendTo(pid, new { type = "me", me = ChestView(chest, pid), message = result });
                CheckAutoEnd();
                return (true, null);
            }
        }

        public (bool ok, string err) Sell(long pid)
        {
            lock (_gate)
            {
                if (!CanAct(pid, out var chest, out var err)) { SendErr(pid, err); return (false, err); }
                chest.ExpireOfferIfNeeded();
                long score = chest.RemainingCount <= 1
                    // 勇气加成：撑到最后一格开箱，得分 ×1.2（鼓励风险路线，破解"到价就卖"单一策略）
                    ? (long)Math.Round(chest.SettleIncome(chest.OpenFinal()) * 1.2)
                    : chest.SettleIncome(Math.Max(1, (long)Math.Round(chest.BankerOffer)));
                chest.State = ChestState.Finished;
                LockScore(pid, score);
                return (true, null);
            }
        }

        public (bool ok, string err) AcceptOffer(long pid)
        {
            lock (_gate)
            {
                if (!CanAct(pid, out var chest, out var err)) { SendErr(pid, err); return (false, err); }
                chest.ExpireOfferIfNeeded();
                if (chest.PendingOffer == null) { SendErr(pid, "NO_OFFER"); return (false, "NO_OFFER"); }
                long score = chest.SettleIncome(chest.PendingOffer.Value);
                chest.PendingOffer = null;
                chest.PendingOfferExpireUtc = null;
                chest.State = ChestState.Finished;
                LockScore(pid, score);
                return (true, null);
            }
        }

        public (bool ok, string err) DeclineOffer(long pid)
        {
            lock (_gate)
            {
                if (!_chests.TryGetValue(pid, out var chest) || _phase != Phase.Match)
                {
                    SendErr(pid, "NOT_MATCHING");
                    return (false, "NOT_MATCHING");
                }
                chest.ExpireOfferIfNeeded();
                if (chest.PendingOffer == null) { SendErr(pid, "NO_OFFER"); return (false, "NO_OFFER"); }
                chest.PendingOffer = null;
                chest.PendingOfferExpireUtc = null;
                SendTo(pid, new { type = "offerGone" });
                return (true, null);
            }
        }

        private bool CanAct(long pid, out Chest chest, out string err)
        {
            chest = null;
            if (_phase != Phase.Match || !_chests.TryGetValue(pid, out chest) || _finished.Contains(pid))
            {
                err = _phase == Phase.Settled ? "DUEL_SETTLED" : "NOT_MATCHING";
                return false;
            }
            err = null;
            return true;
        }

        /// <summary>锁定分数（卖/接受报价/断线/超时）；调用方持 _gate</summary>
        private void LockScore(long pid, long? score = null)
        {
            var chest = _chests[pid];
            long s = score ?? chest.SettleIncome(Math.Max(1, (long)Math.Round(chest.BankerOffer)));
            _scores[pid] = s;
            _finished.Add(pid);
            Broadcast(new { type = "locked", name = _players[pid].Name, score = s, isBot = pid < 0 });
            CheckAutoEnd();
        }

        private void CheckAutoEnd()
        {
            if (_phase == Phase.Match && _finished.Count >= _players.Count) Settle();
        }

        // ---------------- 机器人 ----------------

        private void BotAct()
        {
            double stake = Stake;
            foreach (var bot in _players.Values.Where(p => p.Id < 0).ToList())
            {
                if (_finished.Contains(bot.Id)) continue;
                if (!_chests.TryGetValue(bot.Id, out var chest)) continue;
                chest.ExpireOfferIfNeeded();
                double target = _botTargets.GetValueOrDefault(bot.Id, stake * 1.5);

                if (chest.PendingOffer != null)
                {
                    if (chest.PendingOffer.Value >= target) _ = AcceptOffer(bot.Id);
                    else _ = DeclineOffer(bot.Id);
                    continue;
                }
                if (_rng.NextDouble() > 0.25) continue; // 约 1 秒一步
                long offerNow = (long)Math.Round(chest.BankerOffer);
                if (offerNow >= target || chest.RemainingCount <= 3)
                {
                    _ = Sell(bot.Id);
                    continue;
                }
                if (_rng.NextDouble() < 0.12 && chest.SkillUsesRemaining > 0 && !chest.SkillUsedBy.Contains(bot.Id) && !bot.SkillCooldowns.ContainsKey(1))
                {
                    _ = Skill(bot.Id, 1, null); // 偶尔洞察
                    continue;
                }
                var free = Enumerable.Range(0, chest.Amounts.Length).Where(i => !chest.Eliminated[i]).ToList();
                if (free.Count > 1) _ = Eliminate(bot.Id, free[_rng.Next(free.Count)]);
            }
        }

        // ---------------- 结算 ----------------

        private void Settle()
        {
            if (_phase == Phase.Settled) return;
            _phase = Phase.Settled;
            var g = GameTables.Tables.TbGlobal;
            long stake = Stake;
            var ids = _players.Keys.ToList();
            long s0 = _scores.GetValueOrDefault(ids[0]);
            long s1 = _scores.GetValueOrDefault(ids[1]);
            long winner = s0 > s1 ? ids[0] : s1 > s0 ? ids[1] : 0; // 0 = 平局

            // 对赌房：赢家独得两个宝箱的结算总价值（扣抽成）；平局各得自己箱子的价值
            // 普通对决：赢家得固定奖池（入场金×2 扣抽成）；平局退入场金
            long potPayout(long pid) => PotMode
                ? (winner == 0 ? _scores.GetValueOrDefault(pid)
                    : (pid == winner ? (long)Math.Round((s0 + s1) * (1 - g.AuctionFeeRate)) : 0))
                : (winner == 0 ? stake : (pid == winner ? (long)Math.Round(stake * 2 * (1 - g.AuctionFeeRate)) : 0));

            // 真人入账（按 Id 排序加锁，遵守全局锁序）
            foreach (var pid in ids.Where(id => id > 0).OrderBy(id => id))
            {
                long payout = potPayout(pid);
                lock (_players[pid].Sync)
                {
                    if (payout > 0) _players[pid].Gold += payout;
                }
                if (payout > 0)
                {
                    Db.AddGoldTx(pid, payout, PotMode ? "potduel_win" : "duel_win", DuelId);
                }
            }

            var board = ids.Select(id => new
            {
                name = _players[id].Name,
                score = _scores.GetValueOrDefault(id),
                isBot = id < 0,
                win = winner == id,
            }).OrderByDescending(b => b.score).ToList();

            // 天梯 Elo + 经验（真人）：双真人标准 Elo；对机器人小分值防刷
            var humans = ids.Where(id => id > 0).OrderBy(id => id).ToList();
            if (humans.Count == 2)
            {
                var pa = _players[humans[0]];
                var pb = _players[humans[1]];
                double sa = winner == 0 ? 0.5 : (winner == pa.Id ? 1 : 0);
                lock (pa.Sync)
                {
                    lock (pb.Sync)
                    {
                        int ra = pa.DuelRating, rb = pb.DuelRating;
                        pa.DuelRating = Math.Max(100, Elo.NewRating(ra, rb, sa));
                        pb.DuelRating = Math.Max(100, Elo.NewRating(rb, ra, 1 - sa));
                    }
                }
            }
            else if (humans.Count == 1)
            {
                var h = _players[humans[0]];
                lock (h.Sync)
                {
                    h.DuelRating = Math.Max(100, h.DuelRating + (winner == humans[0] ? 4 : (winner == 0 ? 0 : -2)));
                }
            }
            foreach (var pid in humans)
            {
                var p = _players[pid];
                lock (p.Sync)
                {
                    Levels.AddXp(p, winner == 0 ? Levels.XpDuelTie : (pid == winner ? Levels.XpDuelWin : 0));
                    Db.SavePlayer(p);
                }
            }

            foreach (var pid in ids)
            {
                long myDelta = potPayout(pid) - stake;
                SendTo(pid, new { type = "settled", board, myDelta, tie = winner == 0, potMode = PotMode });
                if (pid > 0) Db.ClearStake(pid, "duel", DuelId); // 正常结算，清除重启退款凭据
            }
            _grace.Clear();
            _timer?.Dispose();
            _manager.OnRoomFinished(this);
        }

        // ---------------- 视图与通信 ----------------

        private object PersonalSnapshot(long pid)
        {
            lock (_gate)
            {
                var opp = _players.Values.FirstOrDefault(p => p.Id != pid);
                Chest? oppChest = opp != null ? _chests.GetValueOrDefault(opp.Id) : null;
                return new
                {
                    type = "snapshot",
                    duelId = DuelId,
                    potMode = PotMode,
                    phase = _phase.ToString().ToLowerInvariant(),
                    players = _players.Values.Select(p => new { name = p.Name, isBot = p.Id < 0 }).ToList(),
                    countdownSec = _phase == Phase.Countdown ? (int)Math.Ceiling((_phaseEndUtc - DateTime.UtcNow).TotalSeconds) : 0,
                    remainSec = _phase == Phase.Match ? (int)Math.Ceiling((_phaseEndUtc - DateTime.UtcNow).TotalSeconds) : 0,
                    me = _chests.TryGetValue(pid, out var mine) ? ChestView(mine, pid) : null,
                    opp = opp != null ? new
                    {
                        name = opp.Name,
                        isBot = opp.Id < 0,
                        eliminatedCount = oppChest?.EliminatedCount ?? 0,
                        remainingCount = oppChest?.RemainingCount ?? 0,
                        finished = _finished.Contains(opp.Id),
                        score = _finished.Contains(opp.Id) ? (long?)_scores.GetValueOrDefault(opp.Id) : null,
                    } : null,
                };
            }
        }

        /// <summary>我的宝箱视图（金额揭示按本箱进度；绝不泄露对手箱）</summary>
        private object ChestView(Chest c, long pid)
        {
            Dictionary<int, int> cds;
            var pl = _players[pid];
            lock (pl.Sync) cds = GameService.SkillCooldownView(pl);
            return new
            {
                eliminated = c.Eliminated,
                revealedAmounts = c.Eliminated.Select((e, i) => e ? (int?)c.Amounts[i] : (int?)null).ToArray(),
                remainingCount = c.RemainingCount,
                remainingSum = c.RemainingSum,
                expectedValue = Math.Round(c.ExpectedValue, 1),
                bankerOffer = (long)Math.Round(c.BankerOffer),
                skillUsesRemaining = c.SkillUsesRemaining,
                skillUsedByMe = c.SkillUsedBy.Contains(pid),
                skillCooldowns = cds,
                freeEliminateBuff = c.FreeEliminateBuff,
                peekedSlots = c.PeekedSlots.ToDictionary(kv => kv.Key, kv => kv.Value),
                detectedSlots = c.DetectedSlots.ToList(),
                insuranceActive = c.InsuranceActive,
                taxReductionCount = c.TaxReductionCount,
                priceBuffNext = c.PriceBuffNext,
                pendingOffer = c.PendingOffer,
                offerExpireInSec = c.PendingOfferExpireUtc != null
                    ? Math.Max(0, (int)(c.PendingOfferExpireUtc.Value - DateTime.UtcNow).TotalSeconds) : 0,
            };
        }

        private void SendErr(long pid, string code) => SendTo(pid, new { type = "err", code });

        private void Broadcast(object msg)
        {
            var json = JsonSerializer.Serialize(msg);
            foreach (var kv in _sockets.ToList())
            {
                _ = SendRaw(kv.Value, json);
            }
        }

        private void BroadcastExcept(long pid, object msg)
        {
            var json = JsonSerializer.Serialize(msg);
            foreach (var kv in _sockets.ToList())
            {
                if (kv.Key != pid) _ = SendRaw(kv.Value, json);
            }
        }

        private void SendTo(long playerId, object msg)
        {
            if (_sockets.TryGetValue(playerId, out var ws))
            {
                _ = SendRaw(ws, JsonSerializer.Serialize(msg));
            }
        }

        private static async Task SendRaw(WebSocket ws, string json)
        {
            try
            {
                if (ws.State == WebSocketState.Open)
                {
                    await ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(json)), WebSocketMessageType.Text, true, CancellationToken.None);
                }
            }
            catch { /* 连接断开由 DetachSocket 清理 */ }
        }
    }

    /// <summary>镜像对决管理器：普通/对赌双队列（1v1），结束即回收</summary>
    public class DuelManager
    {
        private readonly GameStore _store;
        private readonly object _gate = new();
        private DuelRoom? _current;
        private DuelRoom? _currentPot;
        private int _nextDuelId = 1;

        public DuelManager(GameStore store)
        {
            _store = store;
        }

        public (bool ok, string err, DuelRoom? room) Join(Player player, bool potMode = false)
        {
            lock (_gate)
            {
                if (potMode && player.Level < Levels.PotDuelMinLevel)
                {
                    return (false, "LEVEL_LOCKED", null);
                }
                var current = potMode ? _currentPot : _current;
                if (current != null && current.Joinable)
                {
                    var (ok, err) = current.Join(player);
                    if (ok) return (true, null, current);
                    if (err != "ROOM_RUNNING" && err != "ROOM_FULL") return (ok, err, null);
                }
                var fresh = new DuelRoom(_store, this, _nextDuelId++, potMode);
                var (ok2, err2) = fresh.Join(player);
                if (!ok2) return (ok2, err2, null);
                if (potMode) _currentPot = fresh; else _current = fresh;
                _ = Task.Run(() => fresh.StartLifecycle());
                return (true, null, fresh);
            }
        }

        public DuelRoom? FindRoomOf(long playerId)
        {
            lock (_gate)
            {
                if (_current != null && _current.HasPlayer(playerId)) return _current;
                if (_currentPot != null && _currentPot.HasPlayer(playerId)) return _currentPot;
                return null;
            }
        }

        public void OnRoomFinished(DuelRoom room)
        {
            lock (_gate)
            {
                if (_current == room) _current = null;
                if (_currentPot == room) _currentPot = null;
            }
        }
    }
}
