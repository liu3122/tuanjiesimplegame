using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using cfg;
using SimpleGameServer.Config;

namespace SimpleGameServer.Game
{
    /// <summary>
    /// 抢宝房（红包玩法）：门票入房 → 倒计时 → 实时抢 20 格 → 结算。
    /// 参数配置于 TbRushRoom（房间档位），机器人自动补位。
    /// 通信：原生 WebSocket（/ws/rush），JSON 事件双向推送。
    /// </summary>
    public class RushRoom
    {
        private enum Phase { Matching, Countdown, Rush, Settled }

        private readonly GameStore _store;
        private readonly RushManager _manager;
        private readonly cfg.rush.RushRoom _config;
        private Phase _phase = Phase.Matching;
        private readonly Dictionary<long, Player> _players = new();
        private readonly Dictionary<long, WebSocket> _sockets = new();
        private readonly object _gate = new();
        private readonly Random _rng = new();

        private int[] _amounts = Array.Empty<int>();
        private readonly long[] _slotOwner = new long[20];
        private readonly int[] _slotAmount = new int[20];
        private readonly Dictionary<long, long> _earnings = new();
        private DateTime _phaseEndUtc;
        private Timer? _timer;

        public long RoomId => _config.Id;
        /// <summary>好友私人房房间码（6 位；null=公共档位房）。凭码即入，不补机器人，房主可提前开局</summary>
        public string RoomCode { get; }
        public bool IsPrivate => RoomCode != null;
        private readonly long _hostId;
        public int WaitingCount { get { lock (_gate) return _players.Count(p => p.Key > 0); } }

        public RushRoom(GameStore store, RushManager manager, cfg.rush.RushRoom config)
        {
            _store = store;
            _manager = manager;
            _config = config;
        }

        public RushRoom(GameStore store, RushManager manager, cfg.rush.RushRoom config, string roomCode, long hostId)
        {
            _store = store;
            _manager = manager;
            _config = config;
            RoomCode = roomCode;
            _hostId = hostId;
        }

        // ---------------- 加入与连接 ----------------

        public (bool ok, string err) Join(Player player)
        {
            lock (_gate)
            {
                if (_phase != Phase.Matching) return (false, "ROOM_RUNNING");
                if (_players.ContainsKey(player.Id)) return (true, null); // 重复加入幂等
                if (_players.Count >= _config.PlayerCount) return (false, "ROOM_FULL");
                string day = DateTime.UtcNow.ToString("yyyy-MM-dd");
                lock (player.Sync)
                {
                    // 每日限次（内存权威）：RTP>100% 的投放型玩法必须封顶（PoolScale>1 系统倒贴），防无限通胀；
                    // 旧实现查 SQLite 而记账走异步合批，写未 flush 前快速连发可突破 DailyLimit；幂等重进不重复计数
                    if (_config.DailyLimit > 0)
                    {
                        Quests.RollDay(player);
                        if (player.RushUsed.GetValueOrDefault(_config.Id) >= _config.DailyLimit)
                        {
                            return (false, "RUSH_LIMIT");
                        }
                    }
                    if (player.Gold < _config.Ticket) return (false, "NOT_ENOUGH_GOLD");
                    player.Gold -= _config.Ticket;
                    if (_config.DailyLimit > 0) player.RushUsed[_config.Id] = player.RushUsed.GetValueOrDefault(_config.Id) + 1;
                }
                Db.SavePlayer(player);
                Db.AddGoldTx(player.Id, -_config.Ticket, "rush_ticket", _config.Id);
                Db.RecordStake(player.Id, "rush", _config.Id, _config.Ticket); // 重启退款凭据
                if (_config.DailyLimit > 0) Db.RecordRushAttempt(day, player.Id, _config.Id); // 对账审计
                _players[player.Id] = player;
                Broadcast(new { type = "joined", name = player.Name, count = _players.Count, need = _config.PlayerCount });
                return (true, null);
            }
        }

        /// <summary>是否房内成员（WS 连接前的成员资格校验，防未付门票白嫖广播）</summary>
        public bool HasPlayer(long playerId)
        {
            lock (_gate) return _players.ContainsKey(playerId);
        }

        public void AttachSocket(long playerId, WebSocket socket)
        {
            lock (_gate)
            {
                _sockets[playerId] = socket;
            }
            // 发送当前状态快照
            SendTo(playerId, Snapshot(playerId));
        }

        /// <summary>连接断开。只清理"仍是这条连接"的槽位——玩家重连后旧连接的 finally 不许把新连接踢掉</summary>
        public void DetachSocket(long playerId, WebSocket socket)
        {
            lock (_gate)
            {
                if (_sockets.TryGetValue(playerId, out var cur) && cur != socket) return; // 已被新连接覆盖
                _sockets.Remove(playerId);
            }
        }

        // ---------------- 机器人与调度 ----------------

        /// <summary>开始匹配流程：公共房=等待真人→机器人补满；好友房=等真人满员/房主开局，超时解散</summary>
        public void StartLifecycle()
        {
            var g = GameTables.Tables.TbGlobal;
            _phaseEndUtc = DateTime.UtcNow.AddSeconds(IsPrivate
                ? RoomOptions.ResolvePrivateTimeoutSec()
                : g.RushBotFillDelaySec);
            _timer = new Timer(_ => Tick(), null, 500, 250);
        }

        /// <summary>好友房房主提前开局（不足满员也开：奖池=门票×实际人数×倍率）；至少 2 人才有对抗</summary>
        public (bool ok, string err) TryStart(long playerId)
        {
            lock (_gate)
            {
                if (!IsPrivate || _phase != Phase.Matching) return (false, "NOT_MATCHING");
                if (playerId != _hostId) return (false, "NOT_HOST");
                if (_players.Count(p => p.Key > 0) < 2) return (false, "ROOM_NEED_TWO");
                BeginCountdown();
                return (true, null);
            }
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
                            if (IsPrivate)
                            {
                                // 好友房：不补机器人；满员自动开局，不足员由房主 TryStart，超时未满解散退款
                                if (_players.Count >= _config.PlayerCount)
                                {
                                    BeginCountdown();
                                }
                                else if (DateTime.UtcNow >= _phaseEndUtc)
                                {
                                    Dissolve();
                                }
                            }
                            else if (DateTime.UtcNow >= _phaseEndUtc || _players.Count >= _config.PlayerCount)
                            {
                                // 机器人补位
                                while (_players.Count < _config.PlayerCount)
                                {
                                    var bot = _store.CreateRushBot(); // GameStore 提供
                                    _players[bot.Id] = bot;
                                    Broadcast(new { type = "joined", name = bot.Name, count = _players.Count, need = _config.PlayerCount });
                                }
                                BeginCountdown();
                            }
                            break;

                        case Phase.Countdown:
                            Broadcast(new { type = "countdown", remainSec = (int)Math.Ceiling((_phaseEndUtc - DateTime.UtcNow).TotalSeconds) });
                            if (DateTime.UtcNow >= _phaseEndUtc) BeginRush();
                            break;

                        case Phase.Rush:
                            var remain = (int)Math.Ceiling((_phaseEndUtc - DateTime.UtcNow).TotalSeconds);
                            Broadcast(new { type = "remainSec", remainSec = remain });
                            BotClaims();
                            if (DateTime.UtcNow >= _phaseEndUtc || _slotOwner.All(o => o != 0)) Settle();
                            break;
                    }
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("[RushRoom] tick error: " + e.Message);
            }
        }

        private void BeginCountdown()
        {
            _phase = Phase.Countdown;
            _phaseEndUtc = DateTime.UtcNow.AddSeconds(_config.CountdownSec);
            Broadcast(new { type = "phase", phase = "countdown", countdownSec = _config.CountdownSec });
        }

        private void BeginRush()
        {
            _amounts = BuildPool(_players.Count);
            _phase = Phase.Rush;
            _phaseEndUtc = DateTime.UtcNow.AddSeconds(_config.DurationSec);
            // 盲抢房不下发金额（抢到才揭示），机器人同样不可见 → 纯概率/推理博弈
            Broadcast(new { type = "phase", phase = "rush", durationSec = _config.DurationSec,
                amounts = _config.Blind ? null : PublicAmounts(), blind = _config.Blind });
        }

        /// <summary>生成奖池：门票 × 人数 × 倍率，金字塔分布</summary>
        private int[] BuildPool(int playerCount)
        {
            long total = (long)Math.Round(_config.Ticket * playerCount * _config.PoolScale);
            // 金字塔：8 小 5 中 3 中大 2 高 2 大奖
            var weights = new[] { 4, 4, 4, 4, 4, 4, 4, 4, 8, 8, 8, 8, 8, 14, 14, 14, 24, 24, 50, 100 };
            double wsum = weights.Sum();
            var pool = weights.Select(w => (int)Math.Max(1, (long)Math.Round(total * w / wsum))).ToArray();
            // 洗牌
            for (int i = pool.Length - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }
            // 取整误差修正
            long diff = total - pool.Sum();
            pool[Array.IndexOf(pool, pool.Max())] += (int)diff;
            return pool;
        }

        /// <summary>抢格（玩家/机器人均走此入口）</summary>
        public (bool ok, string err, int amount) Claim(long playerId, int slot)
        {
            lock (_gate)
            {
                if (_phase != Phase.Rush) return (false, "NOT_RUSHING", 0);
                if (slot < 0 || slot >= 20) return (false, "BAD_SLOT", 0);
                if (_slotOwner[slot] != 0) return (false, "SLOT_TAKEN", 0);
                if (!_players.ContainsKey(playerId)) return (false, "NOT_IN_ROOM", 0);

                _slotOwner[slot] = playerId;
                _slotAmount[slot] = _amounts[slot];
                _earnings[playerId] = _earnings.GetValueOrDefault(playerId) + _amounts[slot];

                var who = _players[playerId];
                Broadcast(new { type = "claimed", slot, playerId, name = who.Name, amount = _amounts[slot] });
                if (_slotOwner.All(o => o != 0)) Settle();
                return (true, null, _amounts[slot]);
            }
        }

        private void BotClaims()
        {
            var g = GameTables.Tables.TbGlobal;
            var bots = _players.Values.Where(p => p.Id < 0).ToList();
            foreach (var bot in bots)
            {
                // 每 tick 以概率决定是否抢（间隔 500~1400ms 的平均速率）
                double rate = 1000.0 / ((g.RushBotClaimMinMs + g.RushBotClaimMaxMs) / 2.0);
                if (_rng.NextDouble() > rate * 0.25) continue; // tick=250ms
                int slot = ChooseBotSlot();
                if (slot >= 0) Claim(bot.Id, slot);
            }
        }

        private int ChooseBotSlot()
        {
            var free = Enumerable.Range(0, 20).Where(i => _slotOwner[i] == 0).ToList();
            if (free.Count == 0) return -1;
            // 盲抢房机器人不许"看得见"金额（公平）；明抢房 35% 挑当前已知最大，制造压力
            if (!_config.Blind && _rng.NextDouble() < 0.35)
            {
                return free.OrderByDescending(i => _amounts[i]).First();
            }
            return free[_rng.Next(free.Count)];
        }

        private void Settle()
        {
            if (_phase == Phase.Settled) return; // 幂等守卫（Claim 填满与 Tick 超时可能并发触发）
            _phase = Phase.Settled;
            // 真人入账 + 参与经验 + 清除质押（正常结算，不再需要重启退款）
            foreach (var p in _players.Values.Where(p => p.Id > 0))
            {
                Db.ClearStake(p.Id, "rush", _config.Id);
                long earn = _earnings.GetValueOrDefault(p.Id);
                lock (p.Sync)
                {
                    Levels.AddXp(p, Levels.XpRushJoin);
                    Quests.Progress(p, "rush");
                    if (earn > 0) p.Gold += earn;
                    Db.SavePlayer(p);
                }
                if (earn > 0)
                {
                    Db.AddGoldTx(p.Id, earn, "rush_win", _config.Id);
                }
            }
            var board = _players.Values
                .Select(p => new { name = p.Name, earned = _earnings.GetValueOrDefault(p.Id) })
                .OrderByDescending(x => x.earned).ToList();
            Broadcast(new { type = "settled", board });
            _timer?.Dispose();
            _manager.OnRoomFinished(this);
        }

        /// <summary>私人房解散（等待超时未开局）：逐真人退门票 + 清质押 + 广播 closed（调用方持 _gate）</summary>
        private void Dissolve()
        {
            if (_phase == Phase.Settled) return;
            _phase = Phase.Settled;
            foreach (var p in _players.Values.Where(p => p.Id > 0).ToList())
            {
                lock (p.Sync)
                {
                    p.Gold += _config.Ticket;
                    Db.SavePlayer(p);
                }
                Db.ClearStake(p.Id, "rush", _config.Id);
                Db.AddGoldTx(p.Id, _config.Ticket, "friendroom_refund", _config.Id);
            }
            Broadcast(new { type = "closed", reason = "timeout" });
            _timer?.Dispose();
            _manager.OnRoomFinished(this);
        }

        // ---------------- 通信 ----------------

        private object Snapshot(long forPid)
        {
            return new
            {
                type = "snapshot",
                roomId = _config.Id,
                roomCode = RoomCode, // 私人房展示房码；公共房为 null
                isHost = IsPrivate && forPid == _hostId,
                phase = _phase.ToString().ToLowerInvariant(),
                players = _players.Values.Select(p => new { name = p.Name, isBot = p.Id < 0 }).ToList(),
                countdownSec = _phase == Phase.Countdown ? (int)Math.Ceiling((_phaseEndUtc - DateTime.UtcNow).TotalSeconds) : 0,
                remainSec = _phase == Phase.Rush ? (int)Math.Ceiling((_phaseEndUtc - DateTime.UtcNow).TotalSeconds) : 0,
                blind = _config.Blind,
                amounts = _phase == Phase.Rush && !_config.Blind ? PublicAmounts() : null,
                slots = _slotOwner.Select((o, i) => o != 0 ? new { slot = i, playerId = o, amount = _slotAmount[i] } : null).Where(x => x != null).ToList(),
            };
        }

        /// <summary>抢宝阶段金额对房内玩家公开（都开抢了，知道金额才有博弈）</summary>
        private int[] PublicAmounts() => _amounts;

        private void Broadcast(object msg)
        {
            var json = JsonSerializer.Serialize(msg);
            foreach (var kv in _sockets.ToList())
            {
                _ = SendRaw(kv.Value, json);
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

    /// <summary>抢宝房管理器：公共档位房 + 好友私人房（凭码）</summary>
    public class RushManager
    {
        private readonly GameStore _store;
        private readonly GameService _svc;
        private readonly ConcurrentDictionary<int, RushRoom> _rooms = new();
        private readonly ConcurrentDictionary<int, byte> _starting = new();
        /// <summary>好友私人房：房间码 → 房间</summary>
        private readonly ConcurrentDictionary<string, RushRoom> _private = new();
        /// <summary>Join 串行锁（lock(this) 可被外部锁死，改私有对象）</summary>
        private readonly object _gate = new();

        public RushManager(GameStore store, GameService svc)
        {
            _store = store;
            _svc = svc;
        }

        public List<object> RoomList()
        {
            return GameTables.Tables.TbRushRoom.DataList.Select(c =>
            {
                _rooms.TryGetValue(c.Id, out var room);
                return new
                {
                    id = c.Id,
                    name = c.Name,
                    ticket = c.Ticket,
                    playerCount = c.PlayerCount,
                    durationSec = c.DurationSec,
                    waiting = room?.WaitingCount ?? 0,
                    colorHex = c.ColorHex,
                };
            }).Cast<object>().ToList();
        }

        public RushRoom? GetRoom(int roomId)
        {
            _rooms.TryGetValue(roomId, out var room);
            return room;
        }

        public RushRoom? GetPrivate(string code)
        {
            _private.TryGetValue((code ?? "").Trim(), out var room);
            return room;
        }


        /// <summary>创建好友私人抢宝房（档位由房主选择）：返回 6 位房间码</summary>
        public (bool ok, string err, RushRoom? room, string? code) CreatePrivate(Player player, int configId)
        {
            var config = GameTables.Tables.TbRushRoom.GetOrDefault(configId);
            if (config == null) return (false, "ROOM_NOT_FOUND", null, null);
            lock (_gate)
            {
                string code;
                do
                {
                    code = Random.Shared.Next(100000, 1000000).ToString();
                } while (_private.ContainsKey(code));
                var fresh = new RushRoom(_store, this, config, code, player.Id);
                var (ok, err) = fresh.Join(player);
                if (!ok) return (ok, err, null, null);
                _private[code] = fresh;
                _ = Task.Run(() => fresh.StartLifecycle()); // 私人房不进 _rooms，无公共档位替换竞态
                return (true, null, fresh, code);
            }
        }

        /// <summary>凭码加入好友私人抢宝房（码即门票）</summary>
        public (bool ok, string err, RushRoom? room) JoinPrivate(string code, Player player)
        {
            if (!_private.TryGetValue((code ?? "").Trim(), out var room))
                return (false, "ROOM_NOT_FOUND", null);
            var (ok, err) = room.Join(player); // 幂等重进不重复扣费/计数
            return (ok, err, ok ? room : null);
        }

        public (bool ok, string err, RushRoom? room) Join(int roomId, Player player)
        {
            var config = GameTables.Tables.TbRushRoom.GetOrDefault(roomId);
            if (config == null) return (false, "ROOM_NOT_FOUND", null);

            lock (_gate)
            {
                if (_rooms.TryGetValue(roomId, out var existing))
                {
                    var (ok, err) = existing.Join(player);
                    if (ok) return (true, null, existing);
                    if (err != "ROOM_RUNNING") return (ok, err, null);
                    // 房间进行中：不并房，直接为新玩家开新房替换（旧房生命周期自行结束）
                }
                var fresh = new RushRoom(_store, this, config);
                var (ok2, err2) = fresh.Join(player);
                if (!ok2) return (ok2, err2, null);
                _rooms[roomId] = fresh;
                _ = Task.Run(() =>
                {
                    _starting[roomId] = 1;
                    try { fresh.StartLifecycle(); } finally { _starting.TryRemove(roomId, out _); }
                });
                return (true, null, fresh);
            }
        }

        public void OnRoomFinished(RushRoom room)
        {
            // 房间结束：若该档位无等待中房间，移除（下次 join 新建）；私人房按房码移除（无锁，Settle 在 room 锁内回调）
            if (room.RoomCode != null)
            {
                _private.TryRemove(room.RoomCode, out _);
                return;
            }
            if (_rooms.TryGetValue((int)room.RoomId, out var cur) && cur == room)
            {
                _rooms.TryRemove((int)room.RoomId, out _);
            }
        }
    }
}
