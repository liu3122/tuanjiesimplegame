"""异步压测（aiohttp + keep-alive 连接复用）：测服务端真实容量。
用法：python async_load_test.py [players] [rounds]
"""
import asyncio, json, random, sys, time
import aiohttp

BASE = 'http://127.0.0.1:5188'
PLAYERS = int(sys.argv[1]) if len(sys.argv) > 1 else 100
ROUNDS = int(sys.argv[2]) if len(sys.argv) > 2 else 5

stats = {'requests': 0, 'rejects': 0, 'errors': 0, 'latencies': []}
t_start = [0.0]

async def call(session, method, path, token='', body=None):
    headers = {'Authorization': 'Bearer ' + token} if token else {}
    t0 = time.perf_counter()
    try:
        async with session.request(method, BASE + path, json=body, headers=headers,
                                   timeout=aiohttp.ClientTimeout(total=30)) as resp:
            data = await resp.json()
            dt = (time.perf_counter() - t0) * 1000
            stats['latencies'].append(dt)
            stats['requests'] += 1
            if data.get('ok') is False:
                stats['rejects'] += 1
            return data
    except Exception:
        stats['requests'] += 1
        stats['errors'] += 1
        return None

async def worker(session, i):
    name = f'aload_u{i}_{random.randint(1000, 9999)}'
    t = await call(session, 'POST', '/api/login', body={'name': name})
    if not t or 'token' not in t:
        return
    token = t['token']
    for rnd in range(ROUNDS):
        r = await call(session, 'POST', '/api/chest/buy', token, {'configId': 1})
        if not r or not r.get('ok'):
            continue
        uid = r['data']['chestUid']
        for slot in random.sample(range(20), 8):
            await call(session, 'POST', f'/api/chest/{uid}/eliminate', token, {'slotIndex': slot})
        await call(session, 'POST', f'/api/chest/{uid}/skill', token, {'skillType': 2})
        for _ in range(4):
            await call(session, 'GET', '/api/state', token)

async def main():
    conn = aiohttp.TCPConnector(limit=0)  # 不限连接数（keep-alive 复用）
    async with aiohttp.ClientSession(connector=conn) as session:
        t_start[0] = time.perf_counter()
        await asyncio.gather(*(worker(session, i) for i in range(PLAYERS)))
    wall = time.perf_counter() - t_start[0]
    lat = sorted(stats['latencies'])
    def pct(p): return lat[min(len(lat) - 1, int(len(lat) * p))] if lat else 0
    print(f"players={PLAYERS} rounds={ROUNDS}")
    print(f"requests={stats['requests']} rejects(玩法)={stats['rejects']} sysErrors={stats['errors']} wall={wall:.1f}s")
    print(f"RPS={stats['requests'] / wall:.0f}  p50={pct(0.5):.1f}ms  p95={pct(0.95):.1f}ms  max={lat[-1] if lat else 0:.0f}ms")

asyncio.run(main())
