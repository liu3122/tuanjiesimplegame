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
    req = urllib.request.Request(BASE + path, headers={'Authorization': 'Bearer ' + token})
    return json.load(urllib.request.urlopen(req))

t = post('/api/login', body=json.dumps({'name': 'ad_tester_' + str(random.randint(10000, 99999))}))['token']

# 1) state 含广告位状态
s = get('/api/state', t)['data']
assert 'ad_gold' in s['ads'] and s['ads']['ad_gold']['remainToday'] == 5, s['ads']
print('PASS state.ads 广告位状态（ad_gold 今日剩 5）')

# 2) 金币广告：首次成功 +50，二次立即被冷却拦截
g0 = s['gold']
r = post('/api/ad/reward', t, '{"placement":"ad_gold"}')
assert r['ok'] and r['data']['message'] == '获得 50 金币', r
g1 = get('/api/state', t)['data']['gold']
assert g1 == g0 + 50, (g0, g1)
r2 = post('/api/ad/reward', t, '{"placement":"ad_gold"}')
assert not r2['ok'] and r2['error'] == 'AD_COOLDOWN' and r2['data']['remainSec'] > 0, r2
print('PASS ad_gold +50 金币 / 冷却拦截')

# 3) 宝箱广告：领到木箱（uid 返回后 state chests 可见）
r = post('/api/ad/reward', t, '{"placement":"ad_chest"}')
assert r['ok'] and '宝箱' in r['data']['message'], r
print('PASS ad_chest 免费木箱')

# 4) 清冷却广告：无冷却时拒绝
r = post('/api/ad/reward', t, '{"placement":"ad_cooldown"}')
assert not r['ok'] and r2['error'] != 'OK', r
print(f'  ad_cooldown 无冷却时: {r.get("error")} (预期 AD_NO_COOLDOWN)')
assert r['error'] == 'AD_NO_COOLDOWN', r

# 5) 用一个技能制造冷却 → 清冷却成功
chests = [c for c in get('/api/state', t)['data']['chests'] if c['state'] == 0]
assert chests, '需要有可用宝箱（来自 ad_chest）'
uid = chests[0]['uid']
r = post(f'/api/chest/{uid}/skill', t, '{"skillType":2}')
assert r['ok'], r
s2 = get('/api/state', t)['data']
assert s2['skillCooldowns'].get('2', 0) > 0, s2['skillCooldowns']
r = post('/api/ad/reward', t, '{"placement":"ad_cooldown"}')
assert r['ok'] and r['data']['message'] == '技能冷却已全部清除', r
s3 = get('/api/state', t)['data']
assert '2' not in (s3['skillCooldowns'] or {}), s3['skillCooldowns']
assert s3['ads']['ad_cooldown']['remainToday'] == 0, '日限消耗'
print('PASS ad_cooldown：技能冷却清除 + 日限消耗')

# 6) 无效广告位
r = post('/api/ad/reward', t, '{"placement":"nope"}')
assert not r['ok'] and r['error'] == 'BAD_PLACEMENT', r
print('PASS BAD_PLACEMENT')
print('ALL PASS')
