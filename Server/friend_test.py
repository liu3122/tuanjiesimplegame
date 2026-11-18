# -*- coding: utf-8 -*-
"""好友系统回归测试：关系链 + 好友私人房（对决/抢宝）+ 邀请推送 + 超时解散退款。

启动（注意超时覆盖，用于解散用例）：
  cd Server && ASPNETCORE_ENVIRONMENT=Development dotnet bin/Debug/net8.0/Server.dll \
      --urls http://127.0.0.1:5188 --PaySandbox true --FriendRoomTimeoutSec 8
  python friend_test.py
"""
import asyncio
import json
import time
import urllib.request
import urllib.error

import websockets

BASE = 'http://127.0.0.1:5188'
_SUF = str(int(time.time()) % 10**6)

passed = failed = 0


def check(name, cond, detail=''):
    global passed, failed
    if cond:
        passed += 1
        print(f'PASS {name}')
    else:
        failed += 1
        print(f'FAIL {name}  {detail}')


def post(path, body=None, token=''):
    req = urllib.request.Request(BASE + path, data=json.dumps(body or {}).encode(),
                                 headers={'Content-Type': 'application/json',
                                          'Authorization': 'Bearer ' + token})
    try:
        return json.load(urllib.request.urlopen(req))
    except urllib.error.HTTPError as e:
        return {'http': e.code, **json.loads(e.read() or b'{}')}


def get(path, token=''):
    req = urllib.request.Request(BASE + path, headers={'Authorization': 'Bearer ' + token})
    return json.load(urllib.request.urlopen(req))


SECRETS = {}


def login(name):
    body = {'name': name}
    if name in SECRETS:
        body['secret'] = SECRETS[name]
    r = post('/api/login', body)
    assert r.get('ok'), r
    SECRETS[name] = r['secret']
    return r['token']


def state(token):
    return get('/api/state', token)['data']


def fpost(path, body, token):
    return post(path, body, token)


# ================= 1) 好友关系链 =================
ta = login('fa_' + _SUF)
tb = login('fb_' + _SUF)
tc = login('fc_' + _SUF)
sa = state(ta)
a_id = get('/api/friend/list', ta)['data']['myCode']
b_id = get('/api/friend/list', tb)['data']['myCode']
c_id = get('/api/friend/list', tc)['data']['myCode']

r = fpost('/api/friend/request', {'code': str(a_id)}, ta)
check('加自己拒 FRIEND_SELF', not r.get('ok') and r.get('error') == 'FRIEND_SELF', r)

r = fpost('/api/friend/request', {'code': '-1'}, ta)
check('非法码拒 FRIEND_NOT_FOUND', not r.get('ok') and r.get('error') == 'FRIEND_NOT_FOUND', r)

r = fpost('/api/friend/request', {'code': str(b_id)}, ta)
check('A 申请 B 成功', r.get('ok'), r)
r = fpost('/api/friend/request', {'code': str(b_id)}, ta)
check('重复申请拒 ALREADY_REQUESTED', not r.get('ok') and r.get('error') == 'ALREADY_REQUESTED', r)

lst = get('/api/friend/list', tb)['data']
check('B 收到申请', any(q['id'] == a_id for q in lst['requests']), lst['requests'])

r = fpost('/api/friend/accept', {'fromId': a_id}, tb)
check('B 同意申请', r.get('ok'), r)
la = get('/api/friend/list', ta)['data']
lb = get('/api/friend/list', tb)['data']
check('双方好友列表互通', any(f['id'] == b_id for f in la['friends']) and any(f['id'] == a_id for f in lb['friends']),
      (la['friends'], lb['friends']))

r = fpost('/api/friend/request', {'code': str(b_id)}, ta)
check('已是好友拒 FRIEND_EXISTS', not r.get('ok') and r.get('error') == 'FRIEND_EXISTS', r)

# 互发申请 → 自动成为好友
r = fpost('/api/friend/request', {'code': str(c_id)}, ta)
check('A 申请 C 成功', r.get('ok'), r)
r = fpost('/api/friend/request', {'code': str(a_id)}, tc)
check('C 反向申请自动成为好友', r.get('ok'), r)
la = get('/api/friend/list', ta)['data']
lc = get('/api/friend/list', tc)['data']
check('A-C 互为好友（免二次确认）', any(f['id'] == c_id for f in la['friends']) and any(f['id'] == a_id for f in lc['friends']))

r = fpost('/api/friend/remove', {'fromId': c_id}, ta)
check('A 删除 C', r.get('ok'), r)
la = get('/api/friend/list', ta)['data']
check('删除后列表无 C', not any(f['id'] == c_id for f in la['friends']))

# 邀请推送仅好友
r = fpost('/api/friend/invite', {'toId': c_id, 'code': '123456', 'mode': 'duel'}, ta)
check('非好友推送邀请拒', not r.get('ok'), r)

# ================= 2) 好友私人对决房 =================
elo_a0 = state(ta)['duelRating']
xp_a0 = state(ta)['xp']
gold_a0 = state(ta)['gold']

r = fpost('/api/duel/join', {'mode': 'friend'}, ta)
code = r.get('data', {}).get('roomCode')
check('创建好友对决房返回房码', r.get('ok') and code and len(code) == 6, r)

r = fpost('/api/duel/join', {'mode': 'friend', 'code': '000000'}, tb)
check('错码拒 ROOM_NOT_FOUND', not r.get('ok') and r.get('error') == 'ROOM_NOT_FOUND', r)

# 在局防重入：B 已无处可去，先验证 A 在局时不能进公共房
r = fpost('/api/duel/join', {}, ta)
check('在局中再进公共房拒 ROOM_RUNNING', not r.get('ok') and r.get('error') == 'ROOM_RUNNING', r)

# 房间邀请推送（读即消费）
r = fpost('/api/friend/invite', {'toId': b_id, 'code': code, 'mode': 'duel'}, ta)
check('房主向好友推送邀请', r.get('ok'), r)
inbox = get('/api/friend/inbox', tb)['data']
check('B 的 inbox 收到邀请', inbox.get('invite') and inbox['invite']['code'] == code, inbox)
inbox2 = get('/api/friend/inbox', tb)['data']
check('邀请读即消费（再读无）', not inbox2.get('invite'), inbox2)

r = fpost('/api/duel/join', {'mode': 'friend', 'code': code}, tb)
check('B 凭码加入好友房', r.get('ok'), r)


async def duel_private_flow():
    """双 WS 打完整局：断言私人局结算（金币照常、Elo/XP 不动）"""
    ws_a = await websockets.connect(f'ws://127.0.0.1:5188/ws/duel?token={ta}')
    snap = json.loads(await asyncio.wait_for(ws_a.recv(), 10))
    check('私人房快照带房码', snap.get('roomCode') == code, snap.get('roomCode'))
    ws_b = await websockets.connect(f'ws://127.0.0.1:5188/ws/duel?token={tb}')
    got_match = {'a': False, 'b': False}

    async def play(ws, tag):
        async for raw in ws:
            e = json.loads(raw)
            t = e['type']
            if t == 'phase' and e['phase'] == 'match' and not got_match[tag]:
                got_match[tag] = True
                await ws.send(json.dumps({'type': 'sell'}))  # 双方立即锁定 → 结算
            elif t == 'settled':
                return e
        return None

    ra, rb = await asyncio.wait_for(asyncio.gather(play(ws_a, 'a'), play(ws_b, 'b')), 60)
    await ws_a.close()
    await ws_b.close()
    return ra


settled = asyncio.run(duel_private_flow())
check('私人房正常结算', settled is not None)
sa2 = state(ta)
check('私人局 Elo 不变（防刷）', sa2['duelRating'] == elo_a0, (elo_a0, sa2['duelRating']))
check('私人局 XP 不变（防刷）', sa2['xp'] == xp_a0, (xp_a0, sa2['xp']))
gold_a1 = sa2['gold']
check('私人局金币照常结算（赢+90/输-100/平局0）', gold_a1 - gold_a0 in (90, -100, 0), (gold_a0, gold_a1))

# 结算后可再加入（在局锁释放）
r = fpost('/api/duel/join', {'mode': 'friend'}, ta)
code2 = r.get('data', {}).get('roomCode')
check('结算后可再建好友房', r.get('ok') and code2, r)

# ================= 3) 私人房超时解散退款 =================
gold_before = state(ta)['gold']  # 注意：上面建房已扣 100
td = login('fd_' + _SUF)


async def dissolve_flow():
    ws = await websockets.connect(f'ws://127.0.0.1:5188/ws/duel?token={ta}')
    async for raw in ws:
        e = json.loads(raw)
        if e['type'] == 'closed':
            await ws.close()
            return e
    return None


closed = asyncio.run(asyncio.wait_for(dissolve_flow(), 30))
check('超时解散收到 closed', closed is not None and closed.get('reason') == 'timeout', closed)
gold_after = state(ta)['gold']
check('解散退回入场费', gold_after == gold_before + 100, (gold_before, gold_after))

# ================= 4) 好友私人抢宝房 =================
gold_r0 = state(ta)['gold']
r = fpost('/api/rush/private', {'roomId': 1}, ta)
rcode = r.get('data', {}).get('roomCode')
check('创建好友抢宝房返回房码', r.get('ok') and rcode and len(rcode) == 6, r)
r = fpost('/api/rush/join', {'code': rcode}, tb)
check('好友凭码加入抢宝房', r.get('ok'), r)


async def rush_private_flow():
    ws_a = await websockets.connect(f'ws://127.0.0.1:5188/ws/rush?roomId={rcode}&token={ta}')
    snap = json.loads(await asyncio.wait_for(ws_a.recv(), 10))
    check('抢宝私人房快照带房码', snap.get('roomCode') == rcode, snap.get('roomCode'))
    check('房主标识 isHost', snap.get('isHost') is True, snap.get('isHost'))
    ws_b = await websockets.connect(f'ws://127.0.0.1:5188/ws/rush?roomId={rcode}&token={tb}')
    snap_b = json.loads(await asyncio.wait_for(ws_b.recv(), 10))
    check('非房主 isHost=false', snap_b.get('isHost') is False, snap_b.get('isHost'))
    # 房主提前开局（2 人不足 6 人满员）
    await ws_a.send(json.dumps({'type': 'start'}))
    phase_rush = False
    settled_evt = None
    while True:
        raw = await asyncio.wait_for(ws_a.recv(), 40)
        e = json.loads(raw)
        t = e['type']
        if t == 'phase' and e['phase'] == 'rush':
            phase_rush = True
            check('房主 start 提前开局（进入抢宝阶段）', True)
            # 两人交替抢满 20 格 → 提前结算（不等 30s）
            for slot in range(20):
                await (ws_a if slot % 2 == 0 else ws_b).send(json.dumps({'type': 'claim', 'slot': slot}))
        elif t == 'settled':
            settled_evt = e
            break
        elif t == 'err':
            print('  [rush err]', e)
    await ws_a.close()
    await ws_b.close()
    return settled_evt


rs = asyncio.run(rush_private_flow())
check('好友抢宝房结算（2 人房，奖池=门票×2×倍率）', rs is not None and len(rs['board']) == 2, rs)

print(f'\n结果: {passed} 通过, {failed} 失败')
raise SystemExit(1 if failed else 0)
