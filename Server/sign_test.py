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

t = post('/api/login', body=json.dumps({'name': 'sign_a_' + str(random.randint(10000, 99999))}))['token']
s = state(t)
assert s['sign']['todaySigned'] is False and s['sign']['cycleDay'] == 1 and s['sign']['todayReward'] == 30, s['sign']
assert s['sign']['cycleRewards'] == [30, 40, 50, 60, 80, 100, 150]
print('PASS 未签状态：本轮第 1 天 / 今日 30 / 7 档配置下发')

g0 = s['gold']
r = post('/api/sign', t, '{"double":false}')
assert r['ok'] and '本轮第 1 天' in r['data']['message'], r
assert state(t)['gold'] == g0 + 30
r2 = post('/api/sign', t, '{"double":false}')
assert not r2['ok'] and r2['error'] == 'SIGN_DONE', r2
print(f'PASS 首签 +30（{g0}->{g0+30}），当日重复拒绝')

# 双倍签到（另一玩家）
t2 = post('/api/login', body=json.dumps({'name': 'sign_b_' + str(random.randint(10000, 99999))}))['token']
g1 = state(t2)['gold']
r = post('/api/sign', t2, '{"double":true}')
assert r['ok'] and '广告双倍' in r['data']['message'], r
assert state(t2)['gold'] == g1 + 30 * 2, (g1, state(t2)['gold'])
r2 = post('/api/sign', t2, '{"double":true}')
assert r2['error'] == 'SIGN_DONE'
print(f'PASS 广告双倍签到：+{30*2}，重复拒绝')

# state 视图刷新
sv = state(t)['sign']
assert sv['todaySigned'] is True and sv['streak'] == 1 and sv['total'] == 1, sv
print('PASS state.sign 视图（streak/total/todaySigned）')
print('ALL PASS')
