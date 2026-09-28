import json, urllib.request, urllib.error

BASE = 'http://127.0.0.1:5188'

def req(path, token='', body=None, admin=None):
    headers = {'Content-Type': 'application/json', 'Authorization': 'Bearer ' + token}
    if admin is not None: headers['X-Admin-Token'] = admin
    r = urllib.request.Request(BASE + path, data=(body or '{}').encode(), headers=headers)
    try:
        return json.load(urllib.request.urlopen(r))
    except urllib.error.HTTPError as e:
        return {'http': e.code, 'body': e.read().decode()}

def get(path, token='', admin=None):
    headers = {'Authorization': 'Bearer ' + token}
    if admin is not None: headers['X-Admin-Token'] = admin
    r = urllib.request.Request(BASE + path, headers=headers)
    try:
        return json.load(urllib.request.urlopen(r))
    except urllib.error.HTTPError as e:
        return {'http': e.code}

# 1) 经济报表：错误 token 拒绝，正确 token 返回结构完整
bad = get('/api/admin/economy', admin='wrong')
assert bad.get('http') == 403, bad
rep = get('/api/admin/economy', admin='dev-admin')
assert rep['ok'], rep
d = rep['data']
assert 'humanGold' in d and 'byReason' in d and 'daily' in d, d
reasons = {r['reason'] for r in d['byReason']}
print('PASS economy: humanGold=%d botGold=%d reasons=%s' % (d['humanGold'], d['botGold'], sorted(reasons)))
print('  人类日净流入:', [(x['day'], x['humanNet']) for x in d['daily']])

# 2) 教学箱：新玩家领取一次成功，重复领取被拒
t = req('/api/login', body=json.dumps({'name': 'rookie_' + str(__import__('random').randint(10000, 99999))}))['token']
r1 = req('/api/tutorial/chest', t)
assert r1['ok'], r1
uid = r1['data']['chestUid']
r2 = req('/api/tutorial/chest', t)
assert not r2['ok'] and r2['error'] == 'ALREADY_GRANTED', r2
state = get('/api/state', t)['data']
assert state['opensTotal'] == 0
chest = [c for c in state['chests'] if c['uid'] == uid][0]
assert chest['isTutorial'] is True
print('PASS tutorial: uid=%d granted once, isTutorial in state, opensTotal=0' % uid)
print('ALL PASS')
