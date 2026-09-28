import asyncio, json, urllib.request, websockets

BASE = 'http://127.0.0.1:5188'

def post(path, token, body='{}'):
    req = urllib.request.Request(BASE + path, data=body.encode(),
        headers={'Content-Type': 'application/json', 'Authorization': 'Bearer ' + token})
    return json.load(urllib.request.urlopen(req))

def login(name):
    return post('/api/login', '', json.dumps({'name': name}))['token']

async def main():
    token = login('blind_tester')
    r = post('/api/rush/join', token, '{"roomId":3}')  # 盲抢新手房
    assert r['ok'], r
    ws = await websockets.connect(f'ws://127.0.0.1:5188/ws/rush?roomId=3&token={token}')
    claimed = asyncio.Event()

    async def pump():
        await asyncio.sleep(0.3)
        await ws.send(json.dumps({'type': 'claim', 'slot': 0}))

    async for raw in ws:
        e = json.loads(raw)
        t = e['type']
        if t == 'phase' and e['phase'] == 'rush':
            assert e.get('blind') is True, e
            assert e.get('amounts') is None, '盲抢房不得下发金额'
            print('PASS 盲抢开始：blind=true 且金额未下发')
            asyncio.create_task(pump())
        elif t == 'snapshot':
            assert e.get('blind') is True
        elif t == 'claimed':
            if e['slot'] == 0:
                print(f"  抢到第 1 格揭示金额: {e['amount']}")
                claimed.set()
                await ws.close()
                return

    await asyncio.wait_for(claimed.wait(), 30)
    print('ALL PASS')

asyncio.run(main())
