import json, random, time, urllib.request, urllib.error

BASE = 'http://127.0.0.1:5188'

def post(path, token='', body='{}'):
    req = urllib.request.Request(BASE + path, data=body.encode(),
        headers={'Content-Type': 'application/json', 'Authorization': 'Bearer ' + token})
    try:
        return json.load(urllib.request.urlopen(req))
    except urllib.error.HTTPError as e:
        return {'http': e.code}

def get(path, token=''):
    req = urllib.request.Request(BASE + path, headers={'Authorization': 'Bearer ' + token})
    return json.load(urllib.request.urlopen(req))

def state(t):
    return get('/api/state', t)['data']

# 今日任务轮换（与服务端同算法：utc 天索引窗口取 3）
import datetime
QUESTS = [('q_chest2','chest',2,60,20),('q_elim15','eliminate',15,40,15),('q_duel1','duel',1,80,25),
          ('q_rush1','rush',1,40,15),('q_ad1','ad',1,30,10),('q_auction1','auction',1,50,15),('q_duelwin','duelwin',1,100,30)]
day_index = int(time.time()) // 86400
expected_ids = [QUESTS[(day_index + i) % len(QUESTS)][0] for i in range(3)]

t = post('/api/login', body=json.dumps({'name': 'quest_tester_' + str(random.randint(10000, 99999))}))['token']

# 1) 轮换确定性
qs = state(t)['quests']
got_ids = [q['id'] for q in qs]
assert got_ids == expected_ids, (got_ids, expected_ids)
print(f'PASS 今日任务轮换确定: {got_ids}')

# 2) 进度埋点：开箱 → chest 任务 +1；排除 → eliminate +1
if any(q['id'] == 'q_chest2' for q in qs):
    uid = post('/api/tutorial/chest', t)['data']['chestUid'] if state(t)['opensTotal'] == 0 else None
    if uid is None:
        uid = post('/api/chest/buy', t, '{"configId":1}')['data']['chestUid']
    post(f'/api/chest/{uid}/eliminate', t, '{"slotIndex":0}')
    post(f'/api/chest/{uid}/sell', t)
    qs2 = state(t)['quests']
    chest_q = [q for q in qs2 if q['id'] == 'q_chest2'][0]
    assert chest_q['progress'] == 1, chest_q
    print('PASS 开箱任务进度 +1')

# 3) 未完成不可领取
chest_q = [q for q in state(t)['quests'] if q['id'] == 'q_chest2'][0]
if chest_q['progress'] < chest_q['target']:
    r = post('/api/quest/claim', t, json.dumps({'questId': 'q_chest2'}))
    assert not r['ok'] and r['error'] == 'QUEST_NOT_READY', r
    print('PASS 未完成拒绝领取')

# 4) 广告任务可完成并双倍领取
if any(q['id'] == 'q_ad1' for q in state(t)['quests']):
    g0 = state(t)['gold']
    r = post('/api/ad/reward', t, '{"placement":"ad_gold"}')
    assert r['ok'], r
    r = post('/api/quest/claim', t, json.dumps({'questId': 'q_ad1', 'double': True}))
    assert r['ok'] and '广告双倍' in r['data']['message'], r
    g1 = state(t)['gold']
    assert g1 == g0 + 50 + 30 * 2, (g0, g1)  # 广告 50 + 任务 30×2
    r2 = post('/api/quest/claim', t, json.dumps({'questId': 'q_ad1'}))
    assert not r2['ok'] and r2['error'] == 'QUEST_CLAIMED', r2
    print(f'PASS 广告任务双倍领取（{g0}->{g1}）+ 重复领取拒绝')

# 5) 无效任务
r = post('/api/quest/claim', t, json.dumps({'questId': 'q_duelwin'}))
assert not r['ok'] and r['error'] in ('QUEST_INVALID', 'QUEST_NOT_READY'), r
print('PASS 无效/未选任务校验')
print('ALL PASS')
