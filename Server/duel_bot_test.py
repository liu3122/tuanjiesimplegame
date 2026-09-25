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

async def main():
    token = login('duel_solo')
    gold_before = get_state(token)['gold']
    r = post('/api/duel/join', token)
    assert r['ok'], r
    ws = await websockets.connect(f'ws://127.0.0.1:5188/ws/duel?token={token}')
    pending = [{'type': 'eliminate', 'slot': s} for s in (2, 4, 6)] + [{'type': 'sell'}]
    offer_active = False
    started = False
    opp_name = None

    async def action_pump():
        nonlocal offer_active
        while pending:
            if offer_active:
                await ws.send(json.dumps({'type': 'decline'}))
                await asyncio.sleep(0.25)
                continue
            await ws.send(json.dumps(pending.pop(0)))
            await asyncio.sleep(0.4)

    async for raw in ws:
        e = json.loads(raw)
        t = e['type']
        if t == 'joined':
            print(f'  joined: {e["name"]} ({e["count"]}/{e["need"]})')
        elif t == 'snapshot':
            if e.get('opp'): opp_name = e['opp']['name']
            print(f'  snapshot phase={e["phase"]} opp={opp_name}')
        elif t == 'phase' and e['phase'] == 'match' and not started:
            started = True
            print('  MATCH: bot filled, match running')
            asyncio.create_task(action_pump())
        elif t == 'me':
            offer_active = e['me']['pendingOffer'] is not None
        elif t == 'offerGone':
            offer_active = False
        elif t == 'oppAction':
            print(f'  opp action: {e["kind"]} {e.get("amount", e.get("skillName", ""))}')
        elif t == 'locked':
            print(f'  locked: {e["name"]} score={e["score"]}')
        elif t == 'settled':
            gold_after = get_state(token)['gold']
            print(f'  SETTLED delta={e["myDelta"]} tie={e["tie"]}')
            print(f'  board={e["board"]}')
            print(f'  gold {gold_before} -> {gold_after} (diff {gold_after - gold_before}, expect {e["myDelta"]})')
            await ws.close()
            diff = gold_after - gold_before
            assert diff == e['myDelta'], f'gold diff mismatch: {diff} vs {e["myDelta"]}'
            print('PASS: solo+bot duel settled, gold consistent')
            return

asyncio.run(asyncio.wait_for(main(), 120) if False else main())
