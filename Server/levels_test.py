import json, random, urllib.request, urllib.error

BASE = 'http://127.0.0.1:5188'

def req(path, token='', body=None, method=None):
    headers = {'Content-Type': 'application/json', 'Authorization': 'Bearer ' + token}
    r = urllib.request.Request(BASE + path, data=(body or '{}').encode(), headers=headers)
    try:
        return json.load(urllib.request.urlopen(r))
    except urllib.error.HTTPError as e:
        return {'http': e.code}

def get(path, token=''):
    r = urllib.request.Request(BASE + path, headers={'Authorization': 'Bearer ' + token})
    return json.load(urllib.request.urlopen(r))

name = 'lv_tester_' + str(random.randint(10000, 99999))
t = req('/api/login', body=json.dumps({'name': name}))['token']
s = get('/api/state', t)['data']
assert s['level'] == 1 and s['xp'] == 0 and s['xpNext'] == 60, s
assert s['duelRating'] == 1000 and not s['seasonWeek'].startswith(' '), s
assert 'seasonWeek' in s and 'lastSeasonReward' in s, s
print('PASS state 字段: level=1 xp=0 xpNext=60 duelRating=1000 seasonWeek 存在')

# 教学箱 → 排除 1 格 → 出售 → +10xp
uid = req('/api/tutorial/chest', t)['data']['chestUid']
r = req(f'/api/chest/{uid}/eliminate', t, '{"slotIndex":0}')
assert r['ok'], r
r = req(f'/api/chest/{uid}/sell', t)
assert r['ok'], r
s = get('/api/state', t)['data']
assert s['xp'] == 10 and s['level'] == 1, s
print('PASS 开箱结算 +10xp')

# 等级门槛
r = req('/api/chest/buy', t, '{"configId":2}')
assert not r['ok'] and r['error'] == 'LEVEL_LOCKED' and r['data']['needLevel'] == 3, r
print('PASS 银宝箱等级锁定 (need L3)')

r = req('/api/chest/buy', t, '{"configId":1}')
assert r['ok'], r
r = req(f"/api/chest/{r['data']['chestUid']}/skill", t, '{"skillType":4}')
assert not r['ok'] and r['error'] == 'LEVEL_LOCKED' and r['data']['needLevel'] == 2, r
print('PASS 窥视技能等级锁定 (need L2)')

r = req('/api/duel/join', t, '{"mode":"pot"}')
assert not r['ok'] and r['error'] == 'LEVEL_LOCKED', r
r = req('/api/duel/join', t, '{"mode":"stake"}')
assert r['ok'], r
print('PASS 对赌房 L3 锁定 / 普通对决 L1 可用')

# 天梯榜接口
r = get('/api/duel/rank', t)
assert r['ok'] and 'myRating' in r['data'] and isinstance(r['data']['list'], list), r
print('PASS /api/duel/rank 接口')
print('ALL PASS')
