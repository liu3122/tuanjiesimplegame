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
        public int WaitingCount { get { lock (_gate) return _players.Count(p => p.Key > 0); } }

        public RushRoom(GameStore store, RushManager manager, cfg.rush.RushRoom config)
        {
            _store = store;
            _manager = manager;
            _config = config;
        }

        // ---------------- 加入与连接 ----------------

        public (bool ok, string err) Join(Player player)
        {
            lock (_gate)
            {
                if (_phase != Phase.Matching) return (false, "ROOM_RUNNING");
                if (_players.ContainsKey(player.Id)) return (true, null); // 重复加入幂等
                if (_players.Count >= _config.PlayerCount) return (false, "ROOM_FULL");
                if (player.Gold < _config.Ticket) return (false, "NOT_ENOUGH_GOLD");

                player.Gold -= _config.Ticket;
                Db.SavePlayer(player);
                Db.AddGoldTx(player.Id, -_config.Ticket, "rush_ticket", _config.Id);
                Db.RecordStake(player.Id, "rush", _config.Id, _config.Ticket); // 重启退款凭据
                _players[player.Id] = player;
                Broadcast(new { type = "joined", name = player.Name, count = _players.Count, need = _config.PlayerCount });
                return (true, null);
            }
        }

        public void AttachSocket(long playerId, WebSocket socket)
        {
            lock (_gate)
            {
                _sockets[playerId] = socket;
            }
            // 发送当前状态快照
            SendTo(playerId, Snapshot());
        }

        public void DetachSocket(long playerId)
        {
            lock (_gate)
            {
                _sockets.Remove(playerId);
            }
        }

        // ---------------- 机器人与调度 ----------------

        /// <summary>开始匹配流程：等待真人 → 机器人补满 → 倒计时 → 抢 → 结算</summary>
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
                            if (DateTime.UtcNow >= _phaseEndUtc || _players.Count >= _config.PlayerCount)
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
                Levels.AddXp(p, Levels.XpRushJoin);
                Db.SavePlayer(p);
                long earn = _earnings.GetValueOrDefault(p.Id);
                if (earn > 0)
                {
                    p.Gold += earn;
                    Db.SavePlayer(p);
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

        // ---------------- 通信 ----------------

        private object Snapshot()
        {
            return new
            {
                type = "snapshot",
                roomId = _config.Id,
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

    /// <summary>抢宝房管理器</summary>
    public class RushManager
    {
        private readonly GameStore _store;
        private readonly GameService _svc;
        private readonly ConcurrentDictionary<int, RushRoom> _rooms = new();
        private readonly ConcurrentDictionary<int, byte> _starting = new();

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

        public (bool ok, string err, RushRoom? room) Join(int roomId, Player player)
        {
            var config = GameTables.Tables.TbRushRoom.GetOrDefault(roomId);
            if (config == null) return (false, "ROOM_NOT_FOUND", null);

            lock (this)
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
            // 房间结束：若该档位无等待中房间，移除（下次 join 新建）
            if (_rooms.TryGetValue((int)room.RoomId, out var cur) && cur == room)
            {
                _rooms.TryRemove((int)room.RoomId, out _);
            }
        }
    }
}
