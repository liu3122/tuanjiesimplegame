import asyncio, json, urllib.request, websockets

BASE = 'http://127.0.0.1:5188'

def post(path, token, body='{}'):
    req = urllib.request.Request(BASE + path, data=body.encode(),
        headers={'Content-Type': 'application/json', 'Authorization': 'Bearer ' + token})
    return json.load(urllib.request.urlopen(req))

def get_state(token):
    req = urllib.request.Request(BASE + '/api/state', headers={'Authorization': 'Bearer ' + token})
    return json.load(urllib.request.urlopen(req))['data']

def login(name):
    return post('/api/login', '', json.dumps({'name': name}))['token']

async def run_player(name, actions):
    token = login(name)
    r0 = get_state(token)
    r = post('/api/duel/join', token)
    assert r['ok'], r
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
        if t == 'phase' and e['phase'] == 'match' and not started:
            started = True
            asyncio.create_task(pump())
        elif t == 'me':
            state['offer'] = e['me']['pendingOffer'] is not None
        elif t == 'offerGone':
            state['offer'] = False
        elif t == 'settled':
            r1 = get_state(token)
            await ws.close()
            return {'before': r0, 'after': r1, 'settled': e}
    return None

async def main():
    a = asyncio.create_task(run_player('elo_a',
        [{'type': 'eliminate', 'slot': s} for s in (0, 1, 2, 3, 4, 5)] + [{'type': 'sell'}]))
    await asyncio.sleep(0.5)
    b = asyncio.create_task(run_player('elo_b',
        [{'type': 'eliminate', 'slot': s} for s in (5, 9)] + [{'type': 'sell'}]))
    ra, rb = await asyncio.wait_for(asyncio.gather(a, b), 90)
    assert ra and rb

    for name, r in (('elo_a', ra), ('elo_b', rb)):
        before, after = r['before'], r['after']
        assert before['duelRating'] == 1000
        print(f"  {name}: rating {before['duelRating']} -> {after['duelRating']}  xp {before['xp']} -> {after['xp']}")

    if not ra['settled']['tie']:
        winner, loser = (ra, rb) if ra['settled']['myDelta'] > 0 else (rb, ra)
        assert winner['after']['duelRating'] > 1000, 'Elo 赢家应涨分'
        assert loser['after']['duelRating'] < 1000, 'Elo 输家应掉分'
        assert winner['after']['xp'] - winner['before']['xp'] >= 15, '赢家经验 +15'
        print('PASS: 双真人 Elo 一升一降，赢家 +15xp')
    else:
        assert ra['after']['xp'] - ra['before']['xp'] == 5
        print('PASS: 平局双方 +5xp，rating 微调')

asyncio.run(main())
