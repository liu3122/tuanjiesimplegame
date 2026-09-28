import asyncio, json, urllib.request, websockets

BASE = 'http://127.0.0.1:5188'

def post(path, token, body='{}'):
    req = urllib.request.Request(BASE + path, data=body.encode(),
        headers={'Content-Type': 'application/json', 'Authorization': 'Bearer ' + token})
    return json.load(urllib.request.urlopen(req))

def login(name):
    return post('/api/login', '', json.dumps({'name': name}))['token']

async def run_duel(name, actions, disconnect_at=None, reconnect_after=None):
    """通用对决客户端：match 后按序执行动作；disconnect_at=动作数达到后断线"""
    token = login(name)
    r = post('/api/duel/join', token)
    assert r['ok'], r
    url = f'ws://127.0.0.1:5188/ws/duel?token={token}'
    ws = await websockets.connect(url)
    pending = list(actions)
    offer_active = False
    started = False
    result = {}

    async def pump():
        nonlocal ws, disconnect_at
        while pending:
            if offer_active:
                await ws.send(json.dumps({'type': 'decline'}))
                await asyncio.sleep(0.25)
                continue
            await ws.send(json.dumps(pending.pop(0)))
            await asyncio.sleep(0.35)
            if disconnect_at is not None and len(pending) == len(actions) - disconnect_at:
                old = ws
                await old.close()
                if reconnect_after is not None:
                    await asyncio.sleep(reconnect_after)
                    ws = await websockets.connect(url)  # 宽限期内重连
                    disconnect_at = None  # 只断一次，继续执行剩余动作
                else:
                    return

    while True:
        cur = ws
        try:
            raw = await cur.recv()
        except (websockets.exceptions.ConnectionClosed, websockets.exceptions.ConnectionClosedError):
            if reconnect_after is None:
                return result  # 断线不归：收不到结算
            if cur is ws:
                await asyncio.sleep(0.2)  # 等 pump 重连
                continue
            continue
        e = json.loads(raw)
        t = e['type']
        if t == 'phase' and e['phase'] == 'match' and not started:
            started = True
            asyncio.create_task(pump())
        elif t == 'me':
            offer_active = e['me']['pendingOffer'] is not None
        elif t == 'offerGone':
            offer_active = False
        elif t == 'settled':
            result = e
            try:
                await ws.close()
            except Exception:
                pass
            return result

async def scenario_reconnect():
    """断线 3 秒内重连：对局继续，双方正常结算"""
    a = asyncio.create_task(run_duel('grace_r1',
        [{'type': 'eliminate', 'slot': s} for s in (0, 1, 2)] + [{'type': 'sell'}],
        disconnect_at=2, reconnect_after=3.0))
    await asyncio.sleep(0.5)
    b = asyncio.create_task(run_duel('grace_r2',
        [{'type': 'eliminate', 'slot': s} for s in (5, 9)] + [{'type': 'sell'}]))
    ra, rb = await asyncio.wait_for(asyncio.gather(a, b), 60)
    assert ra and rb, 'both must settle'
    print(f'PASS 重连: r1 delta={ra["myDelta"]} r2 delta={rb["myDelta"]}')

async def scenario_expire():
    """断线不归：8 秒宽限后按收购价锁定，留守方收到结算"""
    a = asyncio.create_task(run_duel('grace_e1',
        [{'type': 'eliminate', 'slot': s} for s in (0, 1, 2)] + [{'type': 'sell'}]))
    await asyncio.sleep(0.5)
    b = asyncio.create_task(run_duel('grace_e2',
        [{'type': 'eliminate', 'slot': 7}], disconnect_at=1, reconnect_after=None))
    ra, rb = await asyncio.wait_for(asyncio.gather(a, b), 60)
    # e2 断线未归 → 宽限到期被锁定；e2 的 ws 已关收不到 settled（返回 {}），e1 必须结算
    assert ra, 'remaining player must settle'
    names = [x['name'] for x in ra['board']]
    assert 'grace_e1' in names and 'grace_e2' in names, ra['board']
    print(f'PASS 宽限到期: e1 结算正常 board={[(x["name"], x["score"]) for x in ra["board"]]}')

async def main():
    await scenario_reconnect()
    await asyncio.sleep(1)
    await scenario_expire()

asyncio.run(main())
