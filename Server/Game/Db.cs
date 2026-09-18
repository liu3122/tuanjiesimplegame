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
  character_id INTEGER NOT NULL DEFAULT 1, escrow TEXT NOT NULL DEFAULT '{}'
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
);");
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
            Exec(conn, @"INSERT INTO player(id,name,token,gold,character_id,escrow) VALUES($id,$n,$t,$g,$c,$e)
ON CONFLICT(id) DO UPDATE SET token=$t, gold=$g, character_id=$c, escrow=$e",
                ("$id", p.Id), ("$n", p.Name), ("$t", p.Token as object ?? DBNull.Value),
                ("$g", p.Gold), ("$c", p.CharacterId), ("$e", JsonSerializer.Serialize(p.Escrow, JsonOpts)));
        }, "SavePlayer");

        // ---------------- Chest ----------------

        public static void SaveChest(Chest c) => Try(conn =>
        {
            Exec(conn, @"INSERT INTO chest(uid,config_id,owner_id,state,amounts,eliminated,skill_uses,skill_used_by,free_buff,price_buff,paid_price)
VALUES($u,$cfg,$o,$s,$a,$e,$sk,$sub,$fb,$pb,$pp)
ON CONFLICT(uid) DO UPDATE SET owner_id=$o, state=$s, eliminated=$e, skill_uses=$sk,
skill_used_by=$sub, free_buff=$fb, price_buff=$pb",
                ("$u", c.Uid), ("$cfg", c.ConfigId), ("$o", c.OwnerId), ("$s", (int)c.State),
                ("$a", JsonSerializer.Serialize(c.Amounts, JsonOpts)),
                ("$e", JsonSerializer.Serialize(c.Eliminated, JsonOpts)),
                ("$sk", c.SkillUsesRemaining),
                ("$sub", JsonSerializer.Serialize(c.SkillUsedBy, JsonOpts)),
                ("$fb", c.FreeEliminateBuff), ("$pb", c.PriceBuffNext ? 1 : 0), ("$pp", c.PaidPrice));
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

        // ---------------- 启动恢复 ----------------

        public static void LoadAll(GameStore store)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = "SELECT id,name,token,gold,character_id,escrow FROM player";
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
                    };
                    foreach (var (lotId, amount) in JsonSerializer.Deserialize<Dictionary<long, long>>(r.GetString(5), JsonOpts) ?? new Dictionary<long, long>())
                    {
                        p.Escrow[lotId] = amount;
                    }
                    store.Players[p.Id] = p;
                }
            }

            cmd.CommandText = "SELECT uid,config_id,owner_id,state,amounts,eliminated,skill_uses,skill_used_by,free_buff,price_buff,paid_price FROM chest";
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
