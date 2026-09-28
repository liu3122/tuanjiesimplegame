import asyncio, json, urllib.request, websockets

BASE = 'http://127.0.0.1:5188'
POOL = 1540  # 木箱固定池总和

def post(path, token, body='{}'):
    req = urllib.request.Request(BASE + path, data=body.encode(),
        headers={'Content-Type': 'application/json', 'Authorization': 'Bearer ' + token})
    return json.load(urllib.request.urlopen(req))

def login(name):
    return post('/api/login', '', json.dumps({'name': name}))['token']

async def main():
    token = login('duel_courage')
    r = post('/api/duel/join', token)
    assert r['ok'], r
    ws = await websockets.connect(f'ws://127.0.0.1:5188/ws/duel?token={token}')
    state = {'eliminated': 0, 'offer': False}

    async def pump():
        # 排到只剩 1 格（报价全拒），然后卖出 → 吃最后一格勇气加成
        while state['eliminated'] < 19:
            if state['offer']:
                await ws.send(json.dumps({'type': 'decline'}))
                await asyncio.sleep(0.2)
                continue
            await ws.send(json.dumps({'type': 'eliminate', 'slot': state['eliminated']}))
            await asyncio.sleep(0.25)
        await ws.send(json.dumps({'type': 'sell'}))

    started = False
    async for raw in ws:
        e = json.loads(raw)
        t = e['type']
        if t == 'phase' and e['phase'] == 'match' and not started:
            started = True
            asyncio.create_task(pump())
        elif t == 'me' and e.get('me'):
            me = e['me']
            state['eliminated'] = 20 - me['remainingCount']
            state['offer'] = me['pendingOffer'] is not None
            if me['remainingCount'] <= 1:
                revealed_sum = sum(a for a in me['revealedAmounts'] if a is not None)
                state['final'] = POOL - revealed_sum
        elif t == 'offerGone':
            state['offer'] = False
        elif t == 'settled':
            my = [b for b in e['board'] if b['name'] == 'duel_courage'][0]
            expected = round(state['final'] * 1.2)
            print(f'  final={state["final"]} score={my["score"]} expected(×1.2)={expected} win={my["win"]}')
            assert my['score'] == expected, f'courage bonus mismatch: {my["score"]} vs {expected}'
            print('PASS: 勇气加成生效（最后一格开箱 ×1.2）')
            await ws.close()
            return

asyncio.run(asyncio.wait_for(main(), 110) if False else main())
