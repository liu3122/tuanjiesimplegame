import json, random, urllib.request, urllib.error

BASE = 'http://127.0.0.1:5188'

def post(path, token='', body='{}'):
    req = urllib.request.Request(BASE + path, data=body.encode(),
        headers={'Content-Type': 'application/json', 'Authorization': 'Bearer ' + token})
    try:
        return json.load(urllib.request.urlopen(req))
    except urllib.error.HTTPError as e:
        return {'http': e.code}

def get(path, token=''):
    return json.load(urllib.request.urlopen(urllib.request.Request(BASE + path, headers={'Authorization': 'Bearer ' + token})))

def state(t): return get('/api/state', t)['data']

name = 'item_t_' + str(random.randint(10000, 99999))
t = post('/api/login', body=json.dumps({'name': name}))['token']

# 1) 冷却重置券：用技能产生冷却 → 买券清除
uid = post('/api/tutorial/chest', t)['data']['chestUid']
post(f'/api/chest/{uid}/skill', t, '{"skillType":2}')
s = state(t)
assert s['skillCooldowns'].get('2', 0) > 0, s['skillCooldowns']
g0 = s['gold']
r = post('/api/item/buy', t, json.dumps({'itemId': 2001}))
assert r['ok'] and r['data']['message'] == '技能冷却已全部清除', r
s = state(t)
assert not s['skillCooldowns'] and s['gold'] == g0 - 200, (g0, s['gold'])
print(f'PASS 冷却重置券：-200 金（{g0}->{s["gold"]}）冷却清空')

# 重复买（无冷却）→ ITEM_NO_EFFECT，不扣钱
r = post('/api/item/buy', t, json.dumps({'itemId': 2001}))
assert not r['ok'] and r['error'] == 'ITEM_NO_EFFECT'
assert state(t)['gold'] == g0 - 200
print('PASS 无效果拒绝且不扣费')

# 2) 挑战券：次数 +1（daily rank attemptsLeft）
before = post('/api/daily/buy', t)  # 先用一次（教学箱也算？不：daily buy 单独）
left_before = get('/api/daily/rank', t)['data']['attemptsLeft']
r = post('/api/item/buy', t, json.dumps({'itemId': 2003}))
assert r['ok'], r
left_after = get('/api/daily/rank', t)['data']['attemptsLeft']
assert left_after == left_before + 1, (left_before, left_after)
print(f'PASS 挑战券：挑战次数 {left_before} -> {left_after}')

# 3) 改名卡：改名成功 + 重名拒绝
new_name = 'renamed_' + str(random.randint(10000, 99999))
g1 = state(t)['gold']
r = post('/api/item/buy', t, json.dumps({'itemId': 2004, 'arg': new_name}))
assert r['ok'] and r['data']['name'] == new_name, r
assert state(t)['gold'] == g1 - 500
t2 = post('/api/login', body=json.dumps({'name': 'item_t2_' + str(random.randint(10000, 99999))}))['token']
r = post('/api/item/buy', t2, json.dumps({'itemId': 2004, 'arg': new_name}))
assert not r['ok'] and r['error'] == 'NAME_TAKEN', r
print(f'PASS 改名卡：-500 金改名 {new_name[:14]}…，重名被拒')

# 4) 无效道具 + 金币不足路径
r = post('/api/item/buy', t, json.dumps({'itemId': 9999}))
assert not r['ok'] and r['error'] == 'BAD_ITEM'
print('PASS 无效道具拒绝')

# 5) 经济报表可见 item_buy（增量比对：本测试共花费 200+150+500）
import urllib.request as u
def item_buy_delta():
    req = u.Request(BASE + '/api/admin/economy', headers={'X-Admin-Token': 'dev-admin'})
    rep = json.load(u.urlopen(req))['data']
    row = [r for r in rep['byReason'] if r['reason'] == 'item_buy']
    return row[0]['humanDelta'] if row else 0
before = item_buy_delta()
g_pre = state(t)['gold']
r = post('/api/item/buy', t, json.dumps({'itemId': 2003}))  # 再买一张挑战券验证增量
assert r['ok'], r
after = item_buy_delta()
assert after - before == -150, (before, after)
print(f"PASS 经济报表 item_buy 增量: {after - before}")
print('ALL PASS')
