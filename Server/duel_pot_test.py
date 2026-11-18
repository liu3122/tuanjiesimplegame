import asyncio, json, urllib.request, websockets
import time
_SUF = str(int(time.time()) % 10**6)

BASE = 'http://127.0.0.1:5188'
POOL = 1540
# 竞宝房经济口径（与服务端/客户端同公式）：
# 入场费 = 奖池 1540 × PotDuelEntryPct 5% = 77；赔付封顶 = 双方入场 154 × CapK 3 = 462；
# 排除免费额度 5 次后收费（基础 5 递增）；无勇气加成；平局也扣 5% 抽成
STAKE = 77
CAP = STAKE * 2 * 3

def post(path, token='', body='{}'):
    req = urllib.request.Request(BASE + path, data=body.encode(),
        headers={'Content-Type': 'application/json', 'Authorization': 'Bearer ' + token})
    return json.load(urllib.request.urlopen(req))

def get_state(token):
    req = urllib.request.Request(BASE + '/api/state', headers={'Authorization': 'Bearer ' + token})
    return json.load(urllib.request.urlopen(req))['data']

def login(name):
    r = post('/api/login', '', json.dumps({'name': name}))
    assert 'token' in r, r
    tok = post('/api/login', '', json.dumps({'name': name, 'secret': r['secret']}))['token']
    return tok

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
            assert e['me']['nextEliminateCost'] == 0, '免费额度内排除费应为 0'
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
    na, nb = 'pot_a_' + _SUF, 'pot_b_' + _SUF
    ctx = {na: {}, nb: {}}
    # 双方各排除 ≤5 格（保持在免费额度内，金额断言不混入排除费）
    a = asyncio.create_task(run_player(na,
        [{'type': 'eliminate', 'slot': s} for s in (0, 1, 2, 3, 4)] + [{'type': 'sell'}], ctx))
    await asyncio.sleep(0.5)
    b = asyncio.create_task(run_player(nb,
        [{'type': 'eliminate', 'slot': s} for s in (5, 9, 13)] + [{'type': 'sell'}], ctx))
    await asyncio.wait_for(asyncio.gather(a, b), 90)

    assert ctx[na].get('settled') and ctx[nb].get('settled'), 'both must settle'
    assert ctx[na]['pool'] == ctx[nb]['pool'] == POOL, f"mirror pools: {ctx}"

    for name in (na, nb):
        c = ctx[name]
        assert c['settled']['potMode'] is True
        assert c['gold1'] == c['gold0'] + c['settled']['myDelta'], (name, c)
    board = ctx[na]['settled']['board']
    if not ctx[na]['settled']['tie']:
        win = [x for x in board if x['win']][0]
        lose = [x for x in board if not x['win']][0]
        expected_win = min(round((win['score'] + lose['score']) * 0.95), CAP) - STAKE
        wname = win['name']
        got = ctx[wname]['settled']['myDelta']
        print(f'  board: {[(x["name"], x["score"]) for x in board]}')
        print(f'  winner {wname}: delta={got} expected={expected_win} (双方得分 {win["score"]}+{lose["score"]} 扣5%，封顶 {CAP}，入场 {STAKE})')
        assert got == expected_win, (got, expected_win)
        lname = lose['name']
        assert ctx[lname]['settled']['myDelta'] == -STAKE
        # 根治验证：合谋双方合计净收益必须为负（赔付 ≤ 双方得分和×0.95 ≤ 免费额度内可达上限，< 2×入场）
        pair_net = ctx[na]['settled']['myDelta'] + ctx[nb]['settled']['myDelta']
        assert pair_net < 0, f'合谋净收益应为负（庄家边际）: {pair_net}'
        print(f'PASS: 竞宝赢家得双方得分和（扣5%封顶{CAP}），输家 -{STAKE}，合谋对净收益 {pair_net}（负，刷金根治）')
    else:
        # 平局：各得自己得分×0.95（补了抽成）
        for name in (na, nb):
            score = [x for x in ctx[na]['settled']['board'] if x['name'] == name][0]['score']
            assert ctx[name]['settled']['myDelta'] == round(score * 0.95) - STAKE, ctx[name]
        print('PASS(平局路径): 各得自己得分×0.95（含抽成），合谋收益为负')

asyncio.run(main())
