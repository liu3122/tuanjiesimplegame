import asyncio, json, urllib.request, websockets

BASE = 'http://127.0.0.1:5188'

def post(path, token, body='{}'):
    req = urllib.request.Request(BASE + path, data=body.encode(),
        headers={'Content-Type': 'application/json', 'Authorization': 'Bearer ' + token})
    return json.load(urllib.request.urlopen(req))

def login(name):
    return post('/api/login', '', json.dumps({'name': name}))['token']

pools = {}
settled_count = 0

async def run_player(name, actions):
    global settled_count
    token = login(name)
    r = post('/api/duel/join', token)
    assert r['ok'], r
    ws = await websockets.connect(f'ws://127.0.0.1:5188/ws/duel?token={token}')
    pending = list(actions)
    offer_active = False
    started = False

    async def action_pump():
        nonlocal offer_active
        while pending:
            if offer_active:
                await ws.send(json.dumps({'type': 'decline'}))
                await asyncio.sleep(0.25)
                continue
            await ws.send(json.dumps(pending.pop(0)))
            await asyncio.sleep(0.35)

    async for raw in ws:
        e = json.loads(raw)
        t = e['type']
        if t == 'snapshot' and e.get('me'):
            pools[name] = e['me']['remainingSum']
            print(f'  [{name}] snapshot phase={e["phase"]} pool={pools[name]} opp={e["opp"]["name"]}')
        elif t == 'phase' and e['phase'] == 'match' and not started:
            started = True
            asyncio.create_task(action_pump())
        elif t == 'me':
            offer_active = e['me']['pendingOffer'] is not None
        elif t == 'offerGone':
            offer_active = False
        elif t == 'err':
            print(f'  [{name}] err: {e["code"]}')
        elif t == 'locked':
            print(f'  [{name}] locked: {e["name"]} score={e["score"]}')
        elif t == 'settled':
            settled_count += 1
            print(f'  [{name}] SETTLED myDelta={e["myDelta"]} tie={e["tie"]} board={e["board"]}')
            await ws.close()
            return

async def main():
    a = asyncio.create_task(run_player('duel_p1',
        [{'type': 'eliminate', 'slot': s} for s in (0, 1, 2, 3, 4, 5)] + [{'type': 'sell'}]))
    await asyncio.sleep(0.5)
    b = asyncio.create_task(run_player('duel_p2',
        [{'type': 'eliminate', 'slot': s} for s in (5, 9, 13)] + [{'type': 'sell'}]))
    await asyncio.wait_for(asyncio.gather(a, b), timeout=90)
    assert settled_count == 2, 'both players must receive settled'
    assert pools.get('duel_p1') == pools.get('duel_p2'), f'mirror pools differ: {pools}'
    print(f'PASS: mirror pool={pools["duel_p1"]}, both settled')

asyncio.run(main())
