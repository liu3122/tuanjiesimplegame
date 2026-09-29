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

# 邀请人（老玩家）
inv = post('/api/login', body=json.dumps({'name': 'inviter_' + str(random.randint(10000, 99999))}))
inv_t, inv_id = inv['token'], inv['playerId']

# 新人
t = post('/api/login', body=json.dumps({'name': 'newbie_' + str(random.randint(10000, 99999))}))['token']
s = state(t)
assert s['referral']['myCode'] > 0 and s['referral']['bound'] is False, s['referral']

# 1) 非法绑定：自己 / 错码
r = post('/api/referral/bind', t, json.dumps({'code': str(inv_id + 999999)}))
assert not r['ok'] and r['error'] == 'BAD_CODE', r
print('PASS 无效邀请码拒绝')

# 2) 正常绑定 → 新人首箱结算 → 双方 +100
r = post('/api/referral/bind', t, json.dumps({'code': str(inv_id)}))
assert r['ok'] and r['data']['inviter'].startswith('inviter_'), r
g_new0 = state(t)['gold']; g_inv0 = state(inv_t)['gold']
uid = post('/api/tutorial/chest', t)['data']['chestUid']
post(f'/api/chest/{uid}/sell', t)  # 首箱结算（教学箱）
g_new1 = state(t)['gold']; g_inv1 = state(inv_t)['gold']
assert g_new1 - g_new0 >= 100, (g_new0, g_new1)  # 结算收入 + 100 邀请奖
assert g_inv1 == g_inv0 + 100, (g_inv0, g_inv1)
print(f'PASS 邀请双奖：新人 >=+100（含结算），邀请人 +100（{g_inv0}->{g_inv1}）')

# 3) 重复绑定拒绝 / 结算后不能再绑定
r = post('/api/referral/bind', t, json.dumps({'code': str(inv_id)}))
assert not r['ok'] and r['error'] == 'ALREADY_BOUND', r
print('PASS 重复绑定拒绝')

# 4) 邀请人视图计数
assert state(inv_t)['referral']['invited'] == 1
print('PASS 邀请计数 = 1')
print('ALL PASS')
