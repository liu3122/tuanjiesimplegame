using System.Collections.Concurrent;
using SimpleGameServer.Config;

namespace SimpleGameServer.Game
{
    /// <summary>挂起的好友邀请（好友房房主推送，TTL 内有效，读即消费）</summary>
    public record FriendInvite(string Code, string Mode, long FromId, string FromName, DateTime At);

    /// <summary>
    /// 好友系统（服务端权威）：好友码互加 / 申请-同意 / 私人房邀请推送。
    /// 设计要点：
    /// - 关系存独立表 friend_link（对称双行：A→B 与 B→A 各一行，列友扫描 O(1)），申请存 friend_req；
    /// - 变更全部走单一 _gate 锁（低频社交操作，彻底规避双人 Player 锁序问题）；
    /// - 邀请推送内存易失（进程重启丢失可接受，重新点邀请即可），读即消费防弹窗反复；
    /// - 防骚扰：每日发出请求数上限（tbglobal.FriendReqDailyCap）、好友上限（tbglobal.FriendMax）、收件箱上限 20。
    /// </summary>
    public class FriendService
    {
        private const int PendingInboxCap = 20;
        private static readonly TimeSpan InviteTtl = TimeSpan.FromSeconds(120);

        private readonly GameStore _store;
        private readonly object _gate = new();

        /// <summary>玩家 → 好友集合（对称双写：加好友时两边都加）</summary>
        private readonly ConcurrentDictionary<long, HashSet<long>> _friends = new();
        /// <summary>收件人 → 待处理申请（from 集合）</summary>
        private readonly ConcurrentDictionary<long, HashSet<long>> _pendingTo = new();
        /// <summary>收件人 → 房间邀请（读即消费）</summary>
        private readonly ConcurrentDictionary<long, FriendInvite> _invites = new();
        /// <summary>每日发出请求数（防骚扰；日界用 UTC 日串）</summary>
        private readonly ConcurrentDictionary<long, int> _reqSent = new();
        private string _reqSentDay = "";

        public FriendService(GameStore store)
        {
            _store = store;
        }

        // ---------------- 启动恢复 ----------------

        public void LoadLink(long a, long b)
        {
            _friends.GetOrAdd(a, _ => new HashSet<long>()).Add(b);
        }

        public void LoadReq(long from, long to)
        {
            _pendingTo.GetOrAdd(to, _ => new HashSet<long>()).Add(from);
        }

        // ---------------- 关系操作 ----------------

        public (bool ok, string err, object data) Request(Player from, string code)
        {
            if (!long.TryParse((code ?? "").Trim(), out var target) || target <= 0)
                return (false, "FRIEND_NOT_FOUND", null);
            lock (_gate)
            {
                if (target == from.Id) return (false, "FRIEND_SELF", null);
                if (!_store.Players.TryGetValue(target, out var to) || to.Id < 0)
                    return (false, "FRIEND_NOT_FOUND", null); // 不存在或机器人（Id<0）不可加
                var mine = _friends.GetOrAdd(from.Id, _ => new HashSet<long>());
                if (mine.Contains(target)) return (false, "FRIEND_EXISTS", null);

                // 对方已向我发过申请 → 直接互相成为好友（免二次确认）
                var inbox = _pendingTo.GetOrAdd(from.Id, _ => new HashSet<long>());
                if (inbox.Contains(target))
                {
                    return AcceptInternal(from, target);
                }

                if (_pendingTo.GetOrAdd(target, _ => new HashSet<long>()).Contains(from.Id))
                    return (false, "ALREADY_REQUESTED", null);
                var g = GameTables.Tables.TbGlobal;
                if (mine.Count >= g.FriendMax) return (false, "FRIEND_LIMIT", null);
                if (_friends.GetOrAdd(target, _ => new HashSet<long>()).Count >= g.FriendMax)
                    return (false, "FRIEND_LIMIT", null); // 对方已满
                var inbox2 = _pendingTo.GetOrAdd(target, _ => new HashSet<long>());
                if (inbox2.Count >= PendingInboxCap) return (false, "REQ_LIMIT", null);

                RollReqDay();
                if (_reqSent.GetValueOrDefault(from.Id) >= g.FriendReqDailyCap)
                    return (false, "REQ_LIMIT", null);
                _reqSent[from.Id] = _reqSent.GetValueOrDefault(from.Id) + 1;

                inbox2.Add(from.Id);
                Db.AddFriendReq(from.Id, target);
                return (true, null, new { target = to.Name });
            }
        }

        public (bool ok, string err, object data) Accept(Player me, long fromId)
        {
            lock (_gate)
            {
                return AcceptInternal(me, fromId);
            }
        }

        private (bool ok, string err, object data) AcceptInternal(Player me, long fromId)
        {
            var inbox = _pendingTo.GetOrAdd(me.Id, _ => new HashSet<long>());
            if (!inbox.Contains(fromId)) return (false, "REQ_NOT_FOUND", null);
            var g = GameTables.Tables.TbGlobal;
            var mine = _friends.GetOrAdd(me.Id, _ => new HashSet<long>());
            var theirs = _friends.GetOrAdd(fromId, _ => new HashSet<long>());
            if (mine.Count >= g.FriendMax || theirs.Count >= g.FriendMax)
                return (false, "FRIEND_LIMIT", null);

            inbox.Remove(fromId);
            _pendingTo.TryGetValue(fromId, out var reverse); // 清掉可能存在的反向申请残留
            reverse?.Remove(me.Id);
            mine.Add(fromId);
            theirs.Add(me.Id);
            Db.DeleteFriendReq(fromId, me.Id);
            Db.DeleteFriendReq(me.Id, fromId);
            Db.AddFriendLink(me.Id, fromId);
            return (true, null, new { friendId = fromId });
        }

        public (bool ok, string err, object data) Reject(Player me, long fromId)
        {
            lock (_gate)
            {
                var inbox = _pendingTo.GetOrAdd(me.Id, _ => new HashSet<long>());
                if (!inbox.Remove(fromId)) return (false, "REQ_NOT_FOUND", null);
                Db.DeleteFriendReq(fromId, me.Id);
                return (true, null, new { rejected = true });
            }
        }

        public (bool ok, string err, object data) Remove(Player me, long friendId)
        {
            lock (_gate)
            {
                var mine = _friends.GetOrAdd(me.Id, _ => new HashSet<long>());
                if (!mine.Remove(friendId)) return (false, "FRIEND_NOT_FOUND", null);
                _friends.GetOrAdd(friendId, _ => new HashSet<long>()).Remove(me.Id);
                _invites.TryRemove(me.Id, out _); // 顺带清掉来自对方的挂起邀请
                Db.RemoveFriendLink(me.Id, friendId);
                return (true, null, new { removed = true });
            }
        }

        public bool AreFriends(long a, long b)
        {
            return _friends.TryGetValue(a, out var set) && set.Contains(b);
        }

        // ---------------- 邀请推送 ----------------

        /// <summary>房主向好友推送私人房邀请（仅好友可推，防陌生人骚扰）</summary>
        public (bool ok, string err) Invite(Player from, long toId, string code, string mode)
        {
            lock (_gate)
            {
                if (!AreFriends(from.Id, toId)) return (false, "FRIEND_NOT_FOUND");
                if (!_store.Players.ContainsKey(toId)) return (false, "FRIEND_NOT_FOUND");
                _invites[toId] = new FriendInvite(code, mode, from.Id, from.Name, DateTime.UtcNow);
                return (true, null);
            }
        }

        /// <summary>大厅轻量轮询：申请数 + 房间邀请（读即消费，TTL 120s）</summary>
        public object Inbox(Player me)
        {
            lock (_gate)
            {
                int reqCount = _pendingTo.GetOrAdd(me.Id, _ => new HashSet<long>()).Count;
                object invite = null;
                if (_invites.TryGetValue(me.Id, out var inv))
                {
                    _invites.TryRemove(me.Id, out _); // 读即消费：客户端本次轮询负责展示
                    if (DateTime.UtcNow - inv.At <= InviteTtl)
                    {
                        invite = new { code = inv.Code, mode = inv.Mode, fromId = inv.FromId, fromName = inv.FromName };
                    }
                }
                return new { reqCount, invite };
            }
        }

        // ---------------- 视图 ----------------

        /// <summary>好友列表 + 收到的申请（快照读，名字/等级/天梯分实时取）</summary>
        public object List(Player me)
        {
            lock (_gate)
            {
                var friends = new List<object>();
                foreach (var fid in _friends.GetOrAdd(me.Id, _ => new HashSet<long>()))
                {
                    if (!_store.Players.TryGetValue(fid, out var p)) continue;
                    lock (p.Sync) // 只取标量快照，持锁极短
                    {
                        friends.Add(new { id = p.Id, name = p.Name, level = p.Level, rating = p.DuelRating, online = false });
                    }
                }
                var requests = new List<object>();
                foreach (var fromId in _pendingTo.GetOrAdd(me.Id, _ => new HashSet<long>()))
                {
                    if (!_store.Players.TryGetValue(fromId, out var p)) continue;
                    requests.Add(new { id = p.Id, name = p.Name });
                }
                return new { myCode = me.Id, friends, requests };
            }
        }

        private void RollReqDay()
        {
            string today = DateTime.UtcNow.ToString("yyyy-MM-dd");
            if (_reqSentDay != today)
            {
                _reqSentDay = today;
                _reqSent.Clear();
            }
        }
    }
}
