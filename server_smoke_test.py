#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""SimpleGameServer 冒烟测试：登录→选角→买箱→排除→技能(含限用校验)→出售→拍卖→出价"""
import json, urllib.request

BASE = "http://127.0.0.1:5188"

def call(method, path, body=None, token=None):
    req = urllib.request.Request(BASE + path, method=method)
    req.add_header("Content-Type", "application/json")
    if token: req.add_header("Authorization", "Bearer " + token)
    data = json.dumps(body).encode() if body is not None else None
    try:
        with urllib.request.urlopen(req, data) as r:
            return json.loads(r.read().decode())
    except urllib.error.HTTPError as e:
        return json.loads(e.read().decode())

assert (r := call("POST", "/api/login", {"name": "tester"}))["ok"]
token = r["token"]; print("login OK, gold =", r["gold"])
assert call("POST", "/api/character", {"characterId": 1}, token)["ok"]
assert (r := call("POST", "/api/chest/buy", {"configId": 1}, token))["ok"]
uid = r["data"]["chestUid"]; print("buy chest", uid)
r = call("POST", f"/api/chest/{uid}/eliminate", {"slotIndex": 0}, token)
assert r["ok"]; print("eliminate ->", r["data"]["revealedAmount"])
r = call("POST", f"/api/chest/{uid}/skill", {"skillType": 1}, token)
assert r["ok"]; print("skill:", r["data"]["message"])
r = call("POST", f"/api/chest/{uid}/skill", {"skillType": 1}, token)
assert not r["ok"] and r["error"] == "ERR_PLAYER_USED"; print("skill quota OK:", r["error"])
assert (r := call("POST", f"/api/chest/{uid}/sell", {}, token))["ok"]; print("sold, income =", r["data"]["income"])
assert (r := call("GET", "/api/auction", token=token))["ok"] and r["data"]
lot = r["data"][0]; print(f"auction {len(r['data'])} lots, sample EV {lot['expectedValue']} skill {lot['skillUses']}/{lot['skillUsesMax']}")
assert (r := call("POST", f"/api/auction/{lot['lotId']}/bid", {}, token))["ok"]; print("bid OK ->", r["data"]["bid"])
r = call("POST", f"/api/chest/{uid}/skill", {"skillType": 9}, token)
assert not r["ok"] and r["error"] == "CHEST_NOT_FOUND" or r["error"] == "BAD_SKILL"; print("bad skillType rejected:", r["error"])
r = call("GET", "/api/rank", token=token)
assert r["ok"] and len(r["data"]) >= 5; print("rank OK, top:", r["data"][0]["name"], r["data"][0]["gold"])
print("ALL PASSED")
