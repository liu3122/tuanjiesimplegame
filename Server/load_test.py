"""并发压测：N 个虚拟玩家（登录→买箱→排除→技能→状态查询），统计 RPS / 时延 / 错误。
用法：python load_test.py [players] [rounds]
"""
import json, random, sys, threading, time, urllib.request, urllib.error
from concurrent.futures import ThreadPoolExecutor

BASE = 'http://127.0.0.1:5188'
PLAYERS = int(sys.argv[1]) if len(sys.argv) > 1 else 50
ROUNDS = int(sys.argv[2]) if len(sys.argv) > 2 else 3

stats = {'requests': 0, 'errors': 0, 'latencies': [], 'lock': threading.Lock()}

def record(fn):
    t0 = time.perf_counter()
    try:
        r = fn()
        rejected = r is not None and r.get('ok') is False  # 玩法拒绝（冷却/报价锁定等）不算系统错误
        syserr = r is None
    except Exception as e:
        msg = type(e).__name__
        with stats['lock']:
            stats['errkinds'] = stats.get('errkinds', {})
            stats['errkinds'][msg] = stats['errkinds'].get(msg, 0) + 1
        r, rejected, syserr = None, False, True
    dt = (time.perf_counter() - t0) * 1000
    with stats['lock']:
        stats['requests'] += 1
        stats['latencies'].append(dt)
        if rejected: stats['rejects'] = stats.get('rejects', 0) + 1
        if syserr: stats['errors'] += 1
    return r

def call_get(path, token=''):
    def fn():
        req = urllib.request.Request(BASE + path, headers={'Authorization': 'Bearer ' + token})
        return json.load(urllib.request.urlopen(req, timeout=30))
    return fn

def call(path, token='', body=None):
    def fn():
        req = urllib.request.Request(BASE + path, data=(body or '{}').encode(),
            headers={'Content-Type': 'application/json', 'Authorization': 'Bearer ' + token})
        return json.load(urllib.request.urlopen(req, timeout=30))
    return fn

def worker(i):
    name = f'load_u{i}_{random.randint(1000,9999)}'
    t = record(call('/api/login', body=json.dumps({'name': name})))
    if not t or 'token' not in t:
        return
    token = t['token']
    for rnd in range(ROUNDS):
        r = record(call('/api/chest/buy', token, '{"configId":1}'))
        if not r or not r.get('ok'):
            continue
        uid = r['data']['chestUid']
        for slot in random.sample(range(20), 8):
            record(call(f'/api/chest/{uid}/eliminate', token, f'{{"slotIndex":{slot}}}'))
        record(call(f'/api/chest/{uid}/skill', token, '{"skillType":2}'))
        for _ in range(4):
            record(call_get('/api/state', token))

t0 = time.perf_counter()
with ThreadPoolExecutor(max_workers=PLAYERS) as pool:
    list(pool.map(worker, range(PLAYERS)))
wall = time.perf_counter() - t0

lat = sorted(stats['latencies'])
def pct(p): return lat[min(len(lat) - 1, int(len(lat) * p))] if lat else 0
print(f"players={PLAYERS} rounds={ROUNDS}")
print(f"requests={stats['requests']} rejects(玩法)={stats.get('rejects',0)} sysErrors={stats['errors']} wall={wall:.1f}s")
print('  errKinds:', stats.get('errkinds', {}))
print(f"RPS={stats['requests'] / wall:.0f}  p50={pct(0.5):.0f}ms  p95={pct(0.95):.0f}ms  max={lat[-1] if lat else 0:.0f}ms")
