using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Data.Sqlite;
using SimpleGameServer.Config;

namespace SimpleGameServer.Game
{
    /// <summary>
    /// SQLite 持久层（WAL 模式，短连接）。
    /// 内存模型仍为运行时权威，本层在每个写路径同步落库并支持启动恢复；
    /// 嵌套结构（金额数组/揭示状态/技能记录/预扣）以 JSON 列存储。
    /// 上线换 MySQL：SQL 基本兼容，替换本实现即可。
    /// </summary>
    public static class Db
    {
        private static string _connString;
        private static readonly ILogger _log = SimpleGameServer.Logging.SimpleLog.Logger;

        private static readonly JsonSerializerOptions JsonOpts = new() { IncludeFields = true };

        public static void Initialize(string dbPath)
        {
            _connString = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();
            using var conn = Open();
            Exec(conn, @"
CREATE TABLE IF NOT EXISTS player(
  id INTEGER PRIMARY KEY, name TEXT NOT NULL, token TEXT, gold INTEGER NOT NULL,
  character_id INTEGER NOT NULL DEFAULT 1, escrow TEXT NOT NULL DEFAULT '{}',
  region TEXT NOT NULL DEFAULT 'CN'
);
CREATE UNIQUE INDEX IF NOT EXISTS idx_player_name ON player(name);
CREATE TABLE IF NOT EXISTS chest(
  uid INTEGER PRIMARY KEY, config_id INTEGER NOT NULL, owner_id INTEGER NOT NULL, state INTEGER NOT NULL,
  amounts TEXT NOT NULL, eliminated TEXT NOT NULL, skill_uses INTEGER NOT NULL,
  skill_used_by TEXT NOT NULL, free_buff INTEGER NOT NULL DEFAULT 0,
  price_buff INTEGER NOT NULL DEFAULT 0, paid_price INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS idx_chest_owner ON chest(owner_id);
CREATE TABLE IF NOT EXISTS auction_lot(
  lot_id INTEGER PRIMARY KEY, chest_uid INTEGER NOT NULL, seller_id INTEGER NOT NULL, seller_name TEXT,
  start_price INTEGER NOT NULL, buyout_price INTEGER NOT NULL DEFAULT 0,
  current_bid INTEGER NOT NULL DEFAULT 0, current_bidder_id INTEGER NOT NULL DEFAULT 0,
  current_bidder_name TEXT, end_time_utc TEXT NOT NULL, settled INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS idx_lot_active ON auction_lot(settled, end_time_utc);
CREATE TABLE IF NOT EXISTS gold_tx(
  id INTEGER PRIMARY KEY AUTOINCREMENT, player_id INTEGER NOT NULL, delta INTEGER NOT NULL,
  reason TEXT NOT NULL, ref_id INTEGER NOT NULL DEFAULT 0, created_at TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS daily_attempt(
  day TEXT NOT NULL, player_id INTEGER NOT NULL, count INTEGER NOT NULL DEFAULT 0,
  PRIMARY KEY(day, player_id)
);
CREATE TABLE IF NOT EXISTS share_log(
  id INTEGER PRIMARY KEY AUTOINCREMENT, player_id INTEGER NOT NULL, channel TEXT NOT NULL,
  content_id TEXT NOT NULL DEFAULT '', created_at TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS daily_result(
  day TEXT NOT NULL, player_id INTEGER NOT NULL, name TEXT NOT NULL, best_score INTEGER NOT NULL,
  PRIMARY KEY(day, player_id)
);
CREATE TABLE IF NOT EXISTS active_stake(
  player_id INTEGER NOT NULL, kind TEXT NOT NULL, ref_id INTEGER NOT NULL,
  amount INTEGER NOT NULL, created_at TEXT NOT NULL,
  PRIMARY KEY(player_id, kind, ref_id)
);
CREATE TABLE IF NOT EXISTS season_reward(
  week TEXT NOT NULL, player_id INTEGER NOT NULL, rank INTEGER NOT NULL, amount INTEGER NOT NULL,
  created_at TEXT NOT NULL, PRIMARY KEY(week, player_id)
);");
            try { Exec(conn, "ALTER TABLE player ADD COLUMN region TEXT NOT NULL DEFAULT 'CN'"); } catch (Exception) { }
            try { Exec(conn, "ALTER TABLE player ADD COLUMN skill_cooldowns TEXT NOT NULL DEFAULT '{}'"); } catch (Exception) { }
            foreach (var col in new[] { "xp INTEGER NOT NULL DEFAULT 0", "level INTEGER NOT NULL DEFAULT 1", "duel_rating INTEGER NOT NULL DEFAULT 1000" })
            {
                try { Exec(conn, "ALTER TABLE player ADD COLUMN " + col); } catch (Exception) { }
            }
            try { Exec(conn, "CREATE INDEX IF NOT EXISTS idx_goldtx_reason ON gold_tx(reason, player_id, created_at)"); } catch (Exception) { }
            foreach (var col in new[] { "lucky_slots TEXT NOT NULL DEFAULT '[]'", "trap_slots TEXT NOT NULL DEFAULT '[]'",
                                        "pending_offer INTEGER", "offer_expire TEXT", "elims_since_offer INTEGER NOT NULL DEFAULT 0",
                                        "is_daily INTEGER NOT NULL DEFAULT 0", "daily_day TEXT" })
            {
                try { Exec(conn, "ALTER TABLE chest ADD COLUMN " + col); }
                catch (Exception) { }
            }
            // 旧库升级：追加新列（已存在则忽略）
            foreach (var col in new[] { "peeked_slots TEXT NOT NULL DEFAULT '{}'", "detected_slots TEXT NOT NULL DEFAULT '[]'",
                                        "insurance INTEGER NOT NULL DEFAULT 0", "price_floor INTEGER", "tax_count INTEGER NOT NULL DEFAULT 0",
                                        "identified INTEGER NOT NULL DEFAULT 0", "is_tutorial INTEGER NOT NULL DEFAULT 0" })
            {
                try { Exec(conn, "ALTER TABLE chest ADD COLUMN " + col); }
                catch (Exception) { /* 列已存在 */ }
            }
            _log.LogInformation("SQLite 就绪：{Db}", dbPath);
        }

        private static SqliteConnection Open()
        {
            var conn = new SqliteConnection(_connString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
            cmd.ExecuteNonQuery();
            return conn;
        }

        private static void Exec(SqliteConnection conn, string sql, params (string, object)[] args)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            foreach (var (k, v) in args)
            {
                cmd.Parameters.AddWithValue(k, v ?? DBNull.Value);
            }
            cmd.ExecuteNonQuery();
        }

        private static void Try(Action<SqliteConnection> action, string what)
        {
            try
            {
                using var conn = Open();
                action(conn);
            }
            catch (Exception e)
            {
                _log.LogError(e, "SQLite 写入失败（{What}），内存状态未受影响", what);
            }
        }

        // ---------------- Player ----------------

        public static void SavePlayer(Player p) => Try(conn =>
        {
            Exec(conn, @"INSERT INTO player(id,name,token,gold,character_id,escrow,skill_cooldowns,xp,level,duel_rating) VALUES($id,$n,$t,$g,$c,$e,$cd,$xp,$lv,$dr)
ON CONFLICT(id) DO UPDATE SET token=$t, gold=$g, character_id=$c, escrow=$e, region=$r, skill_cooldowns=$cd, xp=$xp, level=$lv, duel_rating=$dr",
                ("$id", p.Id), ("$n", p.Name), ("$t", p.Token as object ?? DBNull.Value),
                ("$g", p.Gold), ("$c", p.CharacterId), ("$e", JsonSerializer.Serialize(p.Escrow, JsonOpts)),
                ("$cd", JsonSerializer.Serialize(p.SkillCooldowns, JsonOpts)),
                ("$xp", p.Xp), ("$lv", p.Level), ("$dr", p.DuelRating),
                ("$r", p.Region ?? "CN"));
        }, "SavePlayer");

        // ---------------- Chest ----------------

        public static void SaveChest(Chest c) => Try(conn =>
        {
            Exec(conn, @"INSERT INTO chest(uid,config_id,owner_id,state,amounts,eliminated,skill_uses,skill_used_by,free_buff,price_buff,paid_price,
peeked_slots,detected_slots,insurance,price_floor,tax_count,identified,
lucky_slots,trap_slots,pending_offer,offer_expire,elims_since_offer,is_daily,daily_day,is_tutorial)
VALUES($u,$cfg,$o,$s,$a,$e,$sk,$sub,$fb,$pb,$pp,$pk,$dt,$ins,$pf,$tc,$id,$lk,$tp,$po,$ox,$eo,$dd,$dy,$it)
ON CONFLICT(uid) DO UPDATE SET owner_id=$o, state=$s, eliminated=$e, skill_uses=$sk,
skill_used_by=$sub, free_buff=$fb, price_buff=$pb,
peeked_slots=$pk, detected_slots=$dt, insurance=$ins, price_floor=$pf, tax_count=$tc, identified=$id,
lucky_slots=$lk, trap_slots=$tp, pending_offer=$po, offer_expire=$ox, elims_since_offer=$eo, is_daily=$dd, daily_day=$dy, is_tutorial=$it",
                ("$u", c.Uid), ("$cfg", c.ConfigId), ("$o", c.OwnerId), ("$s", (int)c.State),
                ("$a", JsonSerializer.Serialize(c.Amounts, JsonOpts)),
                ("$e", JsonSerializer.Serialize(c.Eliminated, JsonOpts)),
                ("$sk", c.SkillUsesRemaining),
                ("$sub", JsonSerializer.Serialize(c.SkillUsedBy, JsonOpts)),
                ("$fb", c.FreeEliminateBuff), ("$pb", c.PriceBuffNext ? 1 : 0), ("$pp", c.PaidPrice),
                ("$pk", JsonSerializer.Serialize(c.PeekedSlots, JsonOpts)),
                ("$dt", JsonSerializer.Serialize(c.DetectedSlots, JsonOpts)),
                ("$ins", c.InsuranceActive ? 1 : 0),
                ("$pf", c.PriceFloor.HasValue ? (object)c.PriceFloor.Value : DBNull.Value),
                ("$tc", c.TaxReductionCount), ("$id", c.Identified ? 1 : 0),
                ("$lk", JsonSerializer.Serialize(c.LuckySlots, JsonOpts)),
                ("$tp", JsonSerializer.Serialize(c.TrapSlots, JsonOpts)),
                ("$po", c.PendingOffer.HasValue ? (object)c.PendingOffer.Value : DBNull.Value),
                ("$ox", c.PendingOfferExpireUtc.HasValue ? (object)c.PendingOfferExpireUtc.Value.ToString("o") : DBNull.Value),
                ("$eo", c.ElimsSinceOffer),
                ("$dd", c.IsDaily ? 1 : 0), ("$dy", c.DailyDay as object ?? DBNull.Value),
                ("$it", c.IsTutorial ? 1 : 0));
        }, "SaveChest");

        // ---------------- AuctionLot ----------------

        public static void SaveLot(AuctionLot l) => Try(conn =>
        {
            Exec(conn, @"INSERT INTO auction_lot(lot_id,chest_uid,seller_id,seller_name,start_price,buyout_price,current_bid,current_bidder_id,current_bidder_name,end_time_utc,settled)
VALUES($l,$c,$s,$sn,$sp,$bp,$cb,$cbi,$cbn,$et,$st)
ON CONFLICT(lot_id) DO UPDATE SET current_bid=$cb, current_bidder_id=$cbi,
current_bidder_name=$cbn, end_time_utc=$et, settled=$st",
                ("$l", l.LotId), ("$c", l.ChestUid), ("$s", l.SellerId), ("$sn", l.SellerName),
                ("$sp", l.StartPrice), ("$bp", l.BuyoutPrice), ("$cb", l.CurrentBid),
                ("$cbi", l.CurrentBidderId), ("$cbn", l.CurrentBidderName as object ?? DBNull.Value),
                ("$et", l.EndTimeUtc.ToString("o")), ("$st", l.Settled ? 1 : 0));
        }, "SaveLot");

        // ---------------- 流水 ----------------

        public static void AddGoldTx(long playerId, long delta, string reason, long refId) => Try(conn =>
        {
            Exec(conn, "INSERT INTO gold_tx(player_id,delta,reason,ref_id,created_at) VALUES($p,$d,$r,$ref,$t)",
                ("$p", playerId), ("$d", delta), ("$r", reason), ("$ref", refId),
                ("$t", DateTime.UtcNow.ToString("o")));
        }, "AddGoldTx");

        /// <summary>玩家已开箱结算次数（教学引导用：0 = 新玩家）</summary>
        public static int OpensCount(long playerId)
        {
            var v = QueryScalar("SELECT COUNT(*) FROM gold_tx WHERE reason='chest_settled' AND player_id=$p", ("$p", playerId));
            return v is long l ? (int)l : 0;
        }

        // ---------------- 在场质押（重启退款） ----------------

        /// <summary>进房记录质押（抢宝门票/对决入场金）；服务端异常重启后据此退款</summary>
        public static void RecordStake(long playerId, string kind, long refId, int amount) => Try(conn => Exec(conn, @"
INSERT OR REPLACE INTO active_stake(player_id,kind,ref_id,amount,created_at) VALUES($p,$k,$r,$a,$t)",
            ("$p", playerId), ("$k", kind), ("$r", refId), ("$a", amount), ("$t", DateTime.UtcNow.ToString("o"))), "RecordStake");

        /// <summary>正常结算后清除质押记录</summary>
        public static void ClearStake(long playerId, string kind, long refId) => Try(conn => Exec(conn,
            "DELETE FROM active_stake WHERE player_id=$p AND kind=$k AND ref_id=$r",
            ("$p", playerId), ("$k", kind), ("$r", refId)), "ClearStake");

        /// <summary>启动恢复：房间随进程消失，孤立质押退款（按玩家合并）</summary>
        public static void RefundOrphanStakes(GameStore store)
        {
            try
            {
                using var conn = Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT player_id, SUM(amount) FROM active_stake GROUP BY player_id";
                var refunds = new List<(long pid, long amount)>();
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read()) refunds.Add((r.GetInt64(0), r.GetInt64(1)));
                }
                foreach (var (pid, amount) in refunds)
                {
                    if (store.Players.TryGetValue(pid, out var p) && amount > 0)
                    {
                        p.Gold += amount;
                        Db.SavePlayer(p);
                        Db.AddGoldTx(pid, amount, "stake_refund", 0);
                        _log.LogInformation("质押退款：玩家 {Pid} +{Amount}", pid, amount);
                    }
                }
                if (refunds.Count > 0)
                {
                    using var del = conn.CreateCommand();
                    del.CommandText = "DELETE FROM active_stake";
                    del.ExecuteNonQuery();
                }
            }
            catch (Exception e) { _log.LogError(e, "RefundOrphanStakes"); }
        }

        // ---------------- 经济报表（管理端） ----------------

        /// <summary>按 reason 聚合流水（区分真人/机器人），加上近 7 日真人净流入与存量</summary>
        public static object EconomyReport()
        {
            var byReason = new List<object>();
            try
            {
                using var conn = Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
SELECT reason,
  SUM(CASE WHEN player_id>0 THEN delta ELSE 0 END) AS human_delta,
  SUM(CASE WHEN player_id<0 THEN delta ELSE 0 END) AS bot_delta,
  COUNT(*) AS cnt
FROM gold_tx GROUP BY reason ORDER BY 2";
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        byReason.Add(new { reason = r.GetString(0), humanDelta = r.GetInt64(1), botDelta = r.GetInt64(2), count = r.GetInt64(3) });
                    }
                }

                var daily = new List<object>();
                using var cmd2 = conn.CreateCommand();
                cmd2.CommandText = @"
SELECT substr(created_at,1,10) AS day, SUM(CASE WHEN player_id>0 THEN delta ELSE 0 END)
FROM gold_tx WHERE player_id>0 AND created_at >= $ws GROUP BY day ORDER BY day";
                cmd2.Parameters.AddWithValue("$ws", DateTime.UtcNow.AddDays(-7).ToString("o"));
                using (var r = cmd2.ExecuteReader())
                {
                    while (r.Read()) daily.Add(new { day = r.GetString(0), humanNet = r.GetInt64(1) });
                }

                long humanGold = 0, botGold = 0, humanCount = 0;
                using var cmd3 = conn.CreateCommand();
                cmd3.CommandText = "SELECT COALESCE(SUM(CASE WHEN id>0 THEN gold END),0), COALESCE(SUM(CASE WHEN id<0 THEN gold END),0), COUNT(CASE WHEN id>0 THEN 1 END) FROM player";
                using (var r = cmd3.ExecuteReader())
                {
                    if (r.Read()) { humanGold = r.GetInt64(0); botGold = r.GetInt64(1); humanCount = r.GetInt64(2); }
                }
                return new { humanGold, botGold, humanCount, byReason, daily };
            }
            catch (Exception e)
            {
                _log.LogError(e, "EconomyReport");
                return new { error = "REPORT_FAILED" };
            }
        }

        // ---------------- 每日挑战 ----------------

        public static int DailyAttemptsUsed(string day, long playerId)
        {
            var v = QueryScalar("SELECT COALESCE(count,0) FROM daily_attempt WHERE day=$d AND player_id=$p", ("$d", day), ("$p", playerId));
            return v is long l ? (int)l : (v is int i ? i : 0);
        }

        public static void RecordDailyAttempt(string day, long playerId) => Try(conn => Exec(conn, @"
INSERT INTO daily_attempt(day,player_id,count) VALUES($d,$p,1)
ON CONFLICT(day,player_id) DO UPDATE SET count=count+1", ("$d", day), ("$p", playerId)), "RecordDailyAttempt");

        public static void RecordDailyResult(string day, long playerId, string name, long score) => Try(conn => Exec(conn, @"
INSERT INTO daily_result(day,player_id,name,best_score) VALUES($d,$p,$n,$s)
ON CONFLICT(day,player_id) DO UPDATE SET best_score=MAX(best_score,$s)", ("$d", day), ("$p", playerId), ("$n", name), ("$s", score)), "RecordDailyResult");

        public static List<(long playerId, string name, long score)> DailyRank(string day)
        {
            var list = new List<(long, string, long)>();
            try
            {
                using var conn = Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT player_id,name,best_score FROM daily_result WHERE day=$d ORDER BY best_score DESC LIMIT 20";
                cmd.Parameters.AddWithValue("$d", day);
                using var r = cmd.ExecuteReader();
                while (r.Read()) list.Add((r.GetInt64(0), r.GetString(1), r.GetInt64(2)));
            }
            catch (Exception e) { _log.LogError(e, "DailyRank"); }
            return list;
        }

        public static long? MyDailyBest(string day, long playerId)
        {
            var v = QueryScalar("SELECT best_score FROM daily_result WHERE day=$d AND player_id=$p", ("$d", day), ("$p", playerId));
            return v as long?;
        }

        private static object QueryScalar(string sql, params (string, object)[] args)
        {
            try
            {
                using var conn = Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                foreach (var (k, v) in args) cmd.Parameters.AddWithValue(k, v ?? DBNull.Value);
                return cmd.ExecuteScalar();
            }
            catch (Exception e) { _log.LogError(e, "QueryScalar {Sql}", sql); return null; }
        }

        // ---------------- 排行榜（四榜 × 全球/地区） ----------------

        /// <summary>ISO 周一 0 点 UTC（字符串比较用）</summary>
        public static string WeekStartUtcIso()
        {
            var now = DateTime.UtcNow;
            int diff = ((int)now.DayOfWeek + 6) % 7;
            return now.Date.AddDays(-diff).ToString("o");
        }

        // ---------------- 赛季（周） ----------------

        /// <summary>某周奖励是否已发放</summary>
        public static bool SeasonPaid(string week)
        {
            var v = QueryScalar("SELECT COUNT(*) FROM season_reward WHERE week=$w", ("$w", week));
            return v is long l && l > 0;
        }

        /// <summary>时间区间内的真人金币净流入榜（赛季奖励依据）</summary>
        public static List<(long playerId, string name, long value)> WeekGoldBoard(string fromIso, string toIso, int limit = 10)
        {
            var list = new List<(long, string, long)>();
            try
            {
                using var conn = Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"SELECT p.id, p.name, COALESCE(SUM(t.delta),0)
FROM gold_tx t JOIN player p ON p.id = t.player_id
WHERE t.created_at >= $f AND t.created_at < $to AND t.player_id > 0
GROUP BY t.player_id ORDER BY 3 DESC LIMIT $lim";
                cmd.Parameters.AddWithValue("$f", fromIso);
                cmd.Parameters.AddWithValue("$to", toIso);
                cmd.Parameters.AddWithValue("$lim", limit);
                using var r = cmd.ExecuteReader();
                while (r.Read()) list.Add((r.GetInt64(0), r.GetString(1), r.GetInt64(2)));
            }
            catch (Exception e) { _log.LogError(e, "WeekGoldBoard"); }
            return list;
        }

        public static void RecordSeasonRewards(string week, List<(long pid, string name, int rank, long amount)> winners) =>
            Try(conn =>
            {
                foreach (var w in winners)
                {
                    Exec(conn, "INSERT OR IGNORE INTO season_reward(week,player_id,rank,amount,created_at) VALUES($w,$p,$r,$a,$t)",
                        ("$w", week), ("$p", w.pid), ("$r", w.rank), ("$a", w.amount), ("$t", DateTime.UtcNow.ToString("o")));
                }
            }, "RecordSeasonRewards");

        /// <summary>我最近一次赛季奖励（无则 null）</summary>
        public static object MySeasonReward(long playerId)
        {
            try
            {
                using var conn = Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT week,rank,amount FROM season_reward WHERE player_id=$p ORDER BY week DESC LIMIT 1";
                cmd.Parameters.AddWithValue("$p", playerId);
                using var r = cmd.ExecuteReader();
                if (r.Read()) return new { week = r.GetString(0), rank = r.GetInt32(1), amount = r.GetInt64(2) };
                return null;
            }
            catch { return null; }
        }

        /// <summary>对决天梯榜（Elo 降序）</summary>
        public static List<(long playerId, string name, long value)> DuelRatingBoard(int limit = 20)
        {
            var list = new List<(long, string, long)>();
            try
            {
                using var conn = Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT id,name,duel_rating FROM player WHERE id>0 AND duel_rating > $min ORDER BY duel_rating DESC, id LIMIT $lim";
                cmd.Parameters.AddWithValue("$min", Elo.StartRating); // 只显示打过天梯的（>1000 初始）
                cmd.Parameters.AddWithValue("$lim", limit);
                using var r = cmd.ExecuteReader();
                while (r.Read()) list.Add((r.GetInt64(0), r.GetString(1), r.GetInt64(2)));
            }
            catch (Exception e) { _log.LogError(e, "DuelRatingBoard"); }
            return list;
        }

        public static List<(long playerId, string name, long value)> QueryBoard(string board, string region, int limit = 20)
        {
            string weekStart = WeekStartUtcIso();
            string sql = board switch
            {
                "gold_total" => "SELECT p.id, p.name, p.gold FROM player p WHERE p.id > 0 {REGION} ORDER BY p.gold DESC LIMIT $lim",
                "gold_week" => "SELECT p.id, p.name, COALESCE(SUM(t.delta),0) FROM gold_tx t JOIN player p ON p.id=t.player_id WHERE t.created_at >= $ws {REGION} GROUP BY t.player_id ORDER BY 3 DESC LIMIT $lim",
                "opens_total" => "SELECT p.id, p.name, COUNT(*) FROM gold_tx t JOIN player p ON p.id=t.player_id WHERE t.reason='chest_settled' {REGION} GROUP BY t.player_id ORDER BY 3 DESC LIMIT $lim",
                "opens_week" => "SELECT p.id, p.name, COUNT(*) FROM gold_tx t JOIN player p ON p.id=t.player_id WHERE t.reason='chest_settled' AND t.created_at >= $ws {REGION} GROUP BY t.player_id ORDER BY 3 DESC LIMIT $lim",
                _ => null,
            };
            if (sql == null) return new();
            string regionFilter = string.IsNullOrEmpty(region) ? "" : " AND p.region = $rg";
            sql = sql.Replace("{REGION}", regionFilter);

            var list = new List<(long, string, long)>();
            try
            {
                using var conn = Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("$lim", limit);
                cmd.Parameters.AddWithValue("$ws", weekStart);
                if (!string.IsNullOrEmpty(region)) cmd.Parameters.AddWithValue("$rg", region);
                using var r = cmd.ExecuteReader();
                while (r.Read()) list.Add((r.GetInt64(0), r.GetString(1), r.GetInt64(2)));
            }
            catch (Exception e) { _log.LogError(e, "QueryBoard {Board}", board); }
            return list;
        }

        /// <summary>我在某榜的名次与值（名次 = 榜上比我大的行数 + 1）</summary>
        public static (int rank, long value)? MyBoardEntry(long playerId, string board, string region)
        {
            try
            {
                string weekStart = WeekStartUtcIso();
                var conn0 = Open();
                conn0.Dispose();
                using var conn = Open();
                using var cmd = conn.CreateCommand();
                string rg = string.IsNullOrEmpty(region) ? "" : " AND p.region = $rg";
                switch (board)
                {
                    case "gold_total":
                        cmd.CommandText = $"SELECT (SELECT COUNT(*)+1 FROM player p2 WHERE p2.id>0 AND p2.gold > (SELECT gold FROM player WHERE id=$p) {rg.Replace("p.region", "p2.region")} ), (SELECT gold FROM player WHERE id=$p) FROM player WHERE id=$p";
                        break;
                    default:
                        return null; // 周榜/开箱榜我的名次走榜内匹配（简化：仅当上榜时显示）
                }
                cmd.Parameters.AddWithValue("$p", playerId);
                if (!string.IsNullOrEmpty(region)) cmd.Parameters.AddWithValue("$rg", region);
                using var r = cmd.ExecuteReader();
                if (r.Read()) return ((int)r.GetInt64(0), r.GetInt64(1));
                return null;
            }
            catch { return null; }
        }

        // ---------------- 分享 ----------------

        public static void RecordShare(long playerId, string channel, string contentId) => Try(conn => Exec(conn,
            "INSERT INTO share_log(player_id, channel, content_id, created_at) VALUES($p,$c,$cid,$t)",
            ("$p", playerId), ("$c", channel ?? "unknown"), ("$cid", contentId ?? ""), ("$t", DateTime.UtcNow.ToString("o"))), "RecordShare");

        // ---------------- 启动恢复 ----------------

        public static void LoadAll(GameStore store)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = "SELECT id,name,token,gold,character_id,escrow,region,skill_cooldowns,xp,level,duel_rating FROM player";
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    var p = new Player
                    {
                        Id = r.GetInt64(0),
                        Name = r.GetString(1),
                        Token = r.IsDBNull(2) ? null : r.GetString(2),
                        Gold = r.GetInt64(3),
                        CharacterId = r.GetInt32(4),
                        Region = r.IsDBNull(6) ? "CN" : r.GetString(6),
                        Xp = r.IsDBNull(8) ? 0 : r.GetInt32(8),
                        Level = r.IsDBNull(9) ? 1 : r.GetInt32(9),
                        DuelRating = r.IsDBNull(10) ? Elo.StartRating : r.GetInt32(10),
                    };
                    foreach (var (lotId, amount) in JsonSerializer.Deserialize<Dictionary<long, long>>(r.GetString(5), JsonOpts) ?? new Dictionary<long, long>())
                    {
                        p.Escrow[lotId] = amount;
                    }
                    if (!r.IsDBNull(7))
                    {
                        foreach (var (skillType, until) in JsonSerializer.Deserialize<Dictionary<int, long>>(r.GetString(7), JsonOpts) ?? new Dictionary<int, long>())
                        {
                            p.SkillCooldowns[skillType] = until;
                        }
                    }
                    store.Players[p.Id] = p;
                }
            }

            cmd.CommandText = "SELECT uid,config_id,owner_id,state,amounts,eliminated,skill_uses,skill_used_by,free_buff,price_buff,paid_price,peeked_slots,detected_slots,insurance,price_floor,tax_count,identified,lucky_slots,trap_slots,pending_offer,offer_expire,elims_since_offer,is_daily,daily_day,is_tutorial FROM chest";
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    var chest = Chest.Restore(
                        r.GetInt64(0), r.GetInt32(1), r.GetInt64(2), (ChestState)r.GetInt32(3),
                        JsonSerializer.Deserialize<int[]>(r.GetString(4), JsonOpts),
                        JsonSerializer.Deserialize<bool[]>(r.GetString(5), JsonOpts),
                        r.GetInt32(6),
                        JsonSerializer.Deserialize<long[]>(r.GetString(7), JsonOpts),
                        r.GetInt32(8), r.GetInt32(9) == 1, r.GetInt64(10));
                    chest.RestoreExtra(
                        JsonSerializer.Deserialize<Dictionary<int, int>>(r.GetString(11), JsonOpts),
                        JsonSerializer.Deserialize<int[]>(r.GetString(12), JsonOpts),
                        r.GetInt32(13) == 1,
                        r.IsDBNull(14) ? (long?)null : r.GetInt64(14),
                        r.GetInt32(15), r.GetInt32(16) == 1);
                    // SELECT 列序: 17=lucky 18=trap 19=pending 20=expire 21=elims 22=is_daily 23=daily_day
                    chest.RestoreMore(
                        r.IsDBNull(19) ? (long?)null : r.GetInt64(19),
                        r.IsDBNull(20) ? (DateTime?)null : DateTime.Parse(r.GetString(20)).ToUniversalTime(),
                        r.GetInt32(21),
                        JsonSerializer.Deserialize<int[]>(r.GetString(17) ?? "[]", JsonOpts),
                        JsonSerializer.Deserialize<int[]>(r.GetString(18) ?? "[]", JsonOpts),
                        r.GetInt32(22) == 1,
                        r.IsDBNull(23) ? null : r.GetString(23));
                    chest.IsTutorial = r.GetInt32(24) == 1;
                    store.Chests[chest.Uid] = chest;
                }
            }

            cmd.CommandText = "SELECT lot_id,chest_uid,seller_id,seller_name,start_price,buyout_price,current_bid,current_bidder_id,current_bidder_name,end_time_utc,settled FROM auction_lot WHERE settled=0";
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    store.Lots.Add(new AuctionLot
                    {
                        LotId = r.GetInt64(0),
                        ChestUid = r.GetInt64(1),
                        SellerId = r.GetInt64(2),
                        SellerName = r.GetString(3),
                        StartPrice = r.GetInt64(4),
                        BuyoutPrice = r.GetInt64(5),
                        CurrentBid = r.GetInt64(6),
                        CurrentBidderId = r.GetInt64(7),
                        CurrentBidderName = r.IsDBNull(8) ? null : r.GetString(8),
                        EndTimeUtc = DateTime.Parse(r.GetString(9)).ToUniversalTime(),
                        Settled = r.GetInt32(10) == 1,
                    });
                }
            }

            _log.LogInformation("恢复完成：玩家 {P}，宝箱 {C}，进行中拍品 {L}", store.Players.Count, store.Chests.Count, store.Lots.Count);
        }
    }
}

namespace SimpleGameServer.Logging
{
    /// <summary>简单日志入口（避免依赖注入环境外取 logger）</summary>
    public static class SimpleLog
    {
        public static readonly ILogger Logger = Microsoft.Extensions.Logging.LoggerFactory.Create(b => b.AddConsole()).CreateLogger("Db");
    }
}
