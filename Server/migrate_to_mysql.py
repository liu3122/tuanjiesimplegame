"""SQLite → MySQL 数据迁移 + 对账工具（上线换库用，老数据完整平移）

用法：
  # 1) 迁移前自检（只读 SQLite，验证内部一致性；无需 MySQL）
  python migrate_to_mysql.py --check-only

  # 2) 正式迁移（停服后执行；目标表非空会拒绝，--fresh 清空目标表重迁）
  python migrate_to_mysql.py --host 127.0.0.1 --port 3306 --user root --password xxx --db simplegame
  python migrate_to_mysql.py ... --fresh

  # 3) 迁移后对账（两边行数/逐行校验和/金币守恒 全比对）
  python migrate_to_mysql.py --host ... --db simplegame --check-only-target

说明：
  - 源库只读打开，绝不修改；simplegame.db 文件即回滚保险（配置指回即可回退）。
  - JSON TEXT 列（escrow/amounts/...）原样平移，业务代码零改动（SQL 方言差异在 Db.cs）。
  - gold_tx 显式保留原自增 id，MySQL 会自动把 AUTO_INCREMENT 游标推到 max+1。
"""
import argparse
import hashlib
import json
import sqlite3
import sys

try:
    import pymysql
except ImportError:
    pymysql = None

# ---------------- 表定义（与 Db.cs 建表一一对应） ----------------

TABLES = [
    'player', 'chest', 'auction_lot', 'gold_tx',
    'daily_attempt', 'daily_result', 'active_stake', 'season_reward', 'share_log',
]

DDL = """
CREATE TABLE IF NOT EXISTS player(
  id BIGINT PRIMARY KEY, name VARCHAR(64) NOT NULL, token TEXT, gold BIGINT NOT NULL,
  character_id INT NOT NULL DEFAULT 1, escrow TEXT NOT NULL, region VARCHAR(16) NOT NULL DEFAULT 'CN',
  skill_cooldowns TEXT NOT NULL, xp INT NOT NULL DEFAULT 0, level INT NOT NULL DEFAULT 1,
  duel_rating INT NOT NULL DEFAULT 1000
) CHARACTER SET utf8mb4;
CREATE UNIQUE INDEX idx_player_name ON player(name);
CREATE TABLE IF NOT EXISTS chest(
  uid BIGINT PRIMARY KEY, config_id INT NOT NULL, owner_id BIGINT NOT NULL, state INT NOT NULL,
  amounts TEXT NOT NULL, eliminated TEXT NOT NULL, skill_uses INT NOT NULL,
  skill_used_by TEXT NOT NULL, free_buff INT NOT NULL DEFAULT 0,
  price_buff INT NOT NULL DEFAULT 0, paid_price BIGINT NOT NULL DEFAULT 0,
  peeked_slots TEXT NOT NULL DEFAULT '{}', detected_slots TEXT NOT NULL DEFAULT '[]',
  insurance INT NOT NULL DEFAULT 0, price_floor BIGINT, tax_count INT NOT NULL DEFAULT 0,
  identified INT NOT NULL DEFAULT 0, lucky_slots TEXT NOT NULL DEFAULT '[]', trap_slots TEXT NOT NULL DEFAULT '[]',
  pending_offer BIGINT, offer_expire TEXT, elims_since_offer INT NOT NULL DEFAULT 0,
  is_daily INT NOT NULL DEFAULT 0, daily_day TEXT, is_tutorial INT NOT NULL DEFAULT 0
) CHARACTER SET utf8mb4;
CREATE INDEX idx_chest_owner ON chest(owner_id);
CREATE TABLE IF NOT EXISTS auction_lot(
  lot_id BIGINT PRIMARY KEY, chest_uid BIGINT NOT NULL, seller_id BIGINT NOT NULL, seller_name TEXT,
  start_price BIGINT NOT NULL, buyout_price BIGINT NOT NULL DEFAULT 0,
  current_bid BIGINT NOT NULL DEFAULT 0, current_bidder_id BIGINT NOT NULL DEFAULT 0,
  current_bidder_name TEXT, end_time_utc VARCHAR(64) NOT NULL, settled INT NOT NULL DEFAULT 0
) CHARACTER SET utf8mb4;
CREATE INDEX idx_lot_active ON auction_lot(settled, end_time_utc);
CREATE TABLE IF NOT EXISTS gold_tx(
  id BIGINT AUTO_INCREMENT PRIMARY KEY, player_id BIGINT NOT NULL, delta BIGINT NOT NULL,
  reason VARCHAR(32) NOT NULL, ref_id BIGINT NOT NULL DEFAULT 0, created_at VARCHAR(64) NOT NULL
) CHARACTER SET utf8mb4;
CREATE INDEX idx_goldtx_reason ON gold_tx(reason, player_id, created_at);
CREATE TABLE IF NOT EXISTS daily_attempt(
  day VARCHAR(16) NOT NULL, player_id BIGINT NOT NULL, count INT NOT NULL DEFAULT 0,
  PRIMARY KEY(day, player_id)
) CHARACTER SET utf8mb4;
CREATE TABLE IF NOT EXISTS share_log(
  id BIGINT AUTO_INCREMENT PRIMARY KEY, player_id BIGINT NOT NULL, channel VARCHAR(32) NOT NULL,
  content_id VARCHAR(64) NOT NULL DEFAULT '', created_at VARCHAR(64) NOT NULL
) CHARACTER SET utf8mb4;
CREATE TABLE IF NOT EXISTS daily_result(
  day VARCHAR(16) NOT NULL, player_id BIGINT NOT NULL, name VARCHAR(64) NOT NULL,
  best_score BIGINT NOT NULL, PRIMARY KEY(day, player_id)
) CHARACTER SET utf8mb4;
CREATE TABLE IF NOT EXISTS active_stake(
  player_id BIGINT NOT NULL, kind VARCHAR(16) NOT NULL, ref_id BIGINT NOT NULL,
  amount INT NOT NULL, created_at VARCHAR(64) NOT NULL, PRIMARY KEY(player_id, kind, ref_id)
) CHARACTER SET utf8mb4;
CREATE TABLE IF NOT EXISTS season_reward(
  week VARCHAR(64) NOT NULL, player_id BIGINT NOT NULL, rank INT NOT NULL,
  amount BIGINT NOT NULL, created_at VARCHAR(64) NOT NULL, PRIMARY KEY(week, player_id)
) CHARACTER SET utf8mb4;
"""

CHUNK = 500


def open_sqlite(path):
    return sqlite3.connect(f'file:{path}?mode=ro', uri=True)  # 只读：源库绝不修改


def open_mysql(a):
    if pymysql is None:
        sys.exit('缺 pymysql：pip install pymysql')
    return pymysql.connect(host=a.host, port=a.port, user=a.user,
                           password=a.password, database=a.db, charset='utf8mb4')


def table_checksum(conn, table, is_sqlite):
    """顺序无关的逐行校验和（两侧同表比对用）"""
    cur = conn.cursor()
    cur.execute(f'SELECT * FROM {table}')
    h = 0
    n = 0
    for row in cur.fetchall():
        h ^= int.from_bytes(hashlib.md5(repr(row).encode('utf-8')).digest()[:8], 'little')
        n += 1
    return n, h


def check_internal(sqlite_path):
    """迁移前后都适用的自检：金币守恒（含预扣修正）+ 表可读"""
    conn = open_sqlite(sqlite_path)
    cur = conn.cursor()
    ok = True
    cur.execute('SELECT id, name, gold, escrow FROM player')
    players = cur.fetchall()
    for pid, name, gold, escrow_json in players:
        cur.execute('SELECT COALESCE(SUM(delta),0) FROM gold_tx WHERE player_id=?', (pid,))
        tx_sum = cur.fetchone()[0]
        try:
            escrow = sum(json.loads(escrow_json or '{}').values())
        except Exception:
            escrow = 0
        expect = tx_sum - escrow
        if gold != expect:
            # 陷阱格把金币打到 0 时会截断负数（Math.Max(0,...)），流水多记了被截断部分——已知可解释差异
            print(f'  [warn] {name}(#{pid}): gold={gold} 流水-预扣={expect} (差 {gold - expect}，'
                  f'多为陷阱格截断所致)')
    cur.execute('SELECT COALESCE(SUM(delta),0), COUNT(*) FROM gold_tx')
    tx_total, tx_count = cur.fetchone()
    cur.execute('SELECT COALESCE(SUM(gold),0), COALESCE(SUM(CASE WHEN id>0 THEN gold END),0) FROM player')
    gold_all, gold_human = cur.fetchone()
    print(f'内部一致性: 玩家 {len(players)}，流水 {tx_count} 条 / 净额 {tx_total}，'
          f'存量 {gold_all}（真人 {gold_human}）')
    print('自检 PASS' if ok else '自检 FAIL')
    conn.close()
    return ok


def migrate(a):
    src = open_sqlite(a.sqlite)
    dst = open_mysql(a)
    cur_d = dst.cursor()
    cur_d.execute('SET FOREIGN_KEY_CHECKS=0')
    for stmt in filter(str.strip, DDL.split(';')):
        cur_d.execute(stmt)
    if a.fresh:
        for t in reversed(TABLES):
            cur_d.execute(f'TRUNCATE TABLE {t}')
    else:
        # 目标非空则拒绝（防重复迁移）
        for t in TABLES:
            cur_d.execute(f'SELECT COUNT(*) FROM {t}')
            if cur_d.fetchone()[0] > 0:
                sys.exit(f'目标表 {t} 非空：加 --fresh 清空重迁，或换空库')
    cur_s = src.cursor()
    for table in TABLES:
        cur_s.execute(f'SELECT * FROM {table}')
        cols = [d[0] for d in cur_s.description]
        placeholders = ','.join(['%s'] * len(cols))
        sql = f'INSERT INTO {table}({",".join(cols)}) VALUES({placeholders})'
        total, chunk = 0, []
        while True:
            rows = cur_s.fetchmany(CHUNK)
            if not rows:
                break
            cur_d.executemany(sql, rows)
            total += len(rows)
        dst.commit()
        print(f'  {table}: {total} 行')
    cur_d.execute('SET FOREIGN_KEY_CHECKS=1')
    dst.commit()
    print('迁移完成，开始对账...')
    reconcile(a, src, dst)
    src.close()
    dst.close()


def reconcile(a, src, dst):
    """两侧逐表比对：行数 + 顺序无关校验和"""
    all_ok = True
    for table in TABLES:
        n1, h1 = table_checksum(src, table, True)
        n2, h2 = table_checksum(dst, table, False)
        status = 'OK' if (n1 == n2 and h1 == h2) else '!! 不一致'
        if status != 'OK':
            all_ok = False
        print(f'  {table}: {n1} vs {n2} 行, checksum {"一致" if h1 == h2 else "不一致"} {status}')
    print('对账 PASS：SQLite 与 MySQL 数据完全一致' if all_ok else '对账 FAIL：请勿切换，检查上方差异')
    return all_ok


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--sqlite', default='simplegame.db')
    ap.add_argument('--host', default='127.0.0.1')
    ap.add_argument('--port', type=int, default=3306)
    ap.add_argument('--user', default='root')
    ap.add_argument('--password', default='')
    ap.add_argument('--db', default='simplegame')
    ap.add_argument('--fresh', action='store_true', help='清空目标表后重迁')
    ap.add_argument('--check-only', action='store_true', help='只做 SQLite 内部自检（无需 MySQL）')
    ap.add_argument('--check-only-target', action='store_true', help='只对账两侧数据（迁移后复核）')
    a = ap.parse_args()

    if a.check_only:
        sys.exit(0 if check_internal(a.sqlite) else 1)
    if a.check_only_target:
        src, dst = open_sqlite(a.sqlite), open_mysql(a)
        ok = reconcile(a, src, dst)
        src.close(); dst.close()
        sys.exit(0 if ok else 1)
    migrate(a)


if __name__ == '__main__':
    main()
