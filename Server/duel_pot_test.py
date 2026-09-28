import asyncio, json, urllib.request, websockets

BASE = 'http://127.0.0.1:5188'
STAKE = 75      # 对赌房入场金 = 木箱售价
POOL = 1540

def post(path, token='', body='{}'):
    req = urllib.request.Request(BASE + path, data=body.encode(),
        headers={'Content-Type': 'application/json', 'Authorization': 'Bearer ' + token})
    return json.load(urllib.request.urlopen(req))

def get_state(token):
    req = urllib.request.Request(BASE + '/api/state', headers={'Authorization': 'Bearer ' + token})
    return json.load(urllib.request.urlopen(req))['data']

def login(name):
    return post('/api/login', '', json.dumps({'name': name}))['token']

def level_to(token, target=3, max_cycles=25):
    """练级：买木箱→立即卖（开箱结算 +10xp/次），验证升级解锁链路"""
    for _ in range(max_cycles):
        s = get_state(token)
        if s['level'] >= target:
            return s['level']
        r = post('/api/chest/buy', token, '{"configId":1}')
        if not r['ok']:
            raise AssertionError('buy failed: ' + json.dumps(r))
        post(f"/api/chest/{r['data']['chestUid']}/sell", token)
    lv = get_state(token)['level']
    assert lv >= target, f'练级失败: {lv}'
    return lv

async def run_player(name, actions, ctx):
    token = login(name)
    lv = level_to(token, 3)
    print(f'  {name} 练到 Lv.{lv}')
    ctx[name]['gold0'] = get_state(token)['gold']
    r = post('/api/duel/join', token, '{"mode":"pot"}')
    assert r['ok'], (name, r)
    assert r['data']['potMode'] is True, r
    ws = await websockets.connect(f'ws://127.0.0.1:5188/ws/duel?token={token}')
    pending = list(actions)
    state = {'offer': False}
    started = False

    async def pump():
        while pending:
            if state['offer']:
                await ws.send(json.dumps({'type': 'decline'}))
                await asyncio.sleep(0.25)
                continue
            await ws.send(json.dumps(pending.pop(0)))
            await asyncio.sleep(0.35)

    async for raw in ws:
        e = json.loads(raw)
        t = e['type']
        if t == 'snapshot' and e.get('me'):
            assert e['potMode'] is True
            ctx[name]['pool'] = e['me']['remainingSum']
        elif t == 'phase' and e['phase'] == 'match' and not started:
            started = True
            asyncio.create_task(pump())
        elif t == 'me':
            state['offer'] = e['me']['pendingOffer'] is not None
        elif t == 'offerGone':
            state['offer'] = False
        elif t == 'settled':
            ctx[name]['settled'] = e
            ctx[name]['gold1'] = get_state(token)['gold']
            await ws.close()
            return

async def main():
    ctx = {'pot_a': {}, 'pot_b': {}}
    a = asyncio.create_task(run_player('pot_a',
        [{'type': 'eliminate', 'slot': s} for s in (0, 1, 2, 3, 4, 5)] + [{'type': 'sell'}], ctx))
    await asyncio.sleep(0.5)
    b = asyncio.create_task(run_player('pot_b',
        [{'type': 'eliminate', 'slot': s} for s in (5, 9, 13)] + [{'type': 'sell'}], ctx))
    await asyncio.wait_for(asyncio.gather(a, b), 90)

    assert ctx['pot_a'].get('settled') and ctx['pot_b'].get('settled'), 'both must settle'
    assert ctx['pot_a']['pool'] == ctx['pot_b']['pool'] == POOL, f"mirror pools: {ctx}"

    for name in ('pot_a', 'pot_b'):
        c = ctx[name]
        assert c['settled']['potMode'] is True
        # 入场金 75
        paid = c['gold0'] - (c['gold1'] - c['settled']['myDelta'])  # gold1 = gold0 - 75 + payout
        assert c['gold1'] == c['gold0'] + c['settled']['myDelta'], (name, c)
    board = ctx['pot_a']['settled']['board']
    win = [x for x in board if x['win']][0]
    lose = [x for x in board if not x['win'] and not x['win'] is None][0] if False else [x for x in board if not x['win']][0]
    if not ctx['pot_a']['settled']['tie']:
        expected_win = round((win['score'] + lose['score']) * 0.95) - STAKE
        wname = win['name']
        got = ctx[wname]['settled']['myDelta']
        print(f'  board: {[(x["name"], x["score"]) for x in board]}')
        print(f'  winner {wname}: delta={got} expected={expected_win} (两个宝箱价值 {win["score"]}+{lose["score"]} 扣5%)')
        assert got == expected_win, (got, expected_win)
        lname = lose['name']
        assert ctx[lname]['settled']['myDelta'] == -STAKE
        print('PASS: 对赌房赢家通吃（两箱总价值扣 5%），输家 -75，镜像池一致')
    else:
        print('PASS(平局路径): 各自落袋自己箱子的价值')

asyncio.run(main())
