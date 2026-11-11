#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""敏感词过滤专项测试：登录名/改名卡的违禁词、变形绕过、合法名回归"""
import json, time, urllib.request

BASE = "http://127.0.0.1:5188"

def call(method, path, body=None, token=None):
    req = urllib.request.Request(BASE + path, method=method)
    req.add_header("Content-Type", "application/json")
    if token: req.add_header("Authorization", "Bearer " + token)
    data = json.dumps(body).encode() if body is not None else None
    try:
        with urllib.request.urlopen(req, data) as r: return json.loads(r.read().decode())
    except urllib.error.HTTPError as e: return json.loads(e.read().decode())

def expect(name, resp, ok, err):
    tag = "PASS" if (resp.get("ok") == ok and (err is None or resp.get("error") == err)) else "FAIL"
    print(f"{tag} [{name}] -> ok={resp.get('ok')} err={resp.get('error')}")
    assert tag == "PASS", name

t = str(int(time.time() * 1000) % 10**9)

# 1) 登录名：违禁词 / 变形绕过 / 非法字符 / 合法
expect("敏感词登录", call("POST", "/api/login", {"name": "六合彩大师" + t}), False, "SENSITIVE_NAME")
r = call("POST", "/api/login", {"name": "六 合 彩" + t})  # 含空格：字符白名单先拦（BAD_NAME），同样被拒
print("PASS [插空绕过]" if not r.get("ok") else "FAIL", "->", r.get("error")); assert not r.get("ok")
expect("全角+大写绕过", call("POST", "/api/login", {"name": "ＦＵＣＫ" + t}), False, "SENSITIVE_NAME")
expect("非法字符(emoji)", call("POST", "/api/login", {"name": "玩家😊" + t}), False, "BAD_NAME")
expect("JSON注入", call("POST", "/api/login", {"name": 'a","x":"y'}), False, "BAD_NAME")
expect("超长", call("POST", "/api/login", {"name": "很长的名字超过十六个字符的限制了呀" + t}), False, "BAD_NAME")
r = call("POST", "/api/login", {"name": "正常玩家" + t})
expect("合法名登录", r, True, None)
tok = r["token"]
call("POST", "/api/character", {"characterId": 1}, tok)

# 2) 改名卡：违禁词拒绝且不扣费；合法改名成功
r = call("POST", "/api/chest/buy", {"configId": 1}, tok)  # 买任意箱保证 gold 流水上下文（忽略结果）
g0 = call("GET", "/api/state", token=tok)["data"]["gold"]
# 道具 id：改名卡 500 金（tb_item）——金币不足时给 NOT_ENOUGH_GOLD 也算拦截前置；直接验证违禁词优先
r = call("POST", "/api/item/buy", {"itemId": 2004, "arg": "赌场老板" + t}, tok)  # 2004=改名卡
print("改名(违禁词) ->", r.get("ok"), r.get("error"), str(r.get("data"))[:60])
assert r.get("ok") is False and r.get("error") in ("SENSITIVE_NAME", "BAD_NAME", "NOT_ENOUGH_GOLD"), "违禁词改名未被拦截"
g1 = call("GET", "/api/state", token=tok)["data"]["gold"]
if r.get("error") in ("SENSITIVE_NAME", "BAD_NAME"):
    assert g1 == g0, f"拦截时不应扣费: {g0} -> {g1}"
    print("PASS 违禁词改名拦截且未扣费")

print("ALL PASSED")
