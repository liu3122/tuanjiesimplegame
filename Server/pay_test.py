# -*- coding: utf-8 -*-
"""内购全链路回归（需服务端以沙箱模式启动：dotnet run --urls http://127.0.0.1:5188 --PaySandbox true）。

2026-11-11 软硬双币改造后：IAP 发钻石（gemsGranted / state.gems），金币不经充值直接变化。
覆盖：SKU 列表 / 渠道拦截 / 建单 → dev 收据验单 → 钻石到账（金币不动）/
幂等重放（同收据不再发货）/ 跨收据重放拒 / 他人订单拒 / 未知订单拒 / 错收据格式拒。
"""
import json
import time
import urllib.request

BASE = "http://127.0.0.1:5188"


def call(method, path, body=None, token=None):
    url = BASE + path
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(url, data=data, method=method)
    req.add_header("Content-Type", "application/json")
    if token:
        req.add_header("Authorization", "Bearer " + token)
    try:
        with urllib.request.urlopen(req, timeout=10) as r:
            return json.loads(r.read().decode())
    except urllib.error.HTTPError as e:
        return json.loads(e.read().decode() or "{}")


def state(token):
    r = call("GET", "/api/state", token=token)
    assert r.get("ok"), f"/api/state 失败: {r}"
    return r["data"]


def main():
    passed = failed = 0

    def check(name, cond):
        nonlocal passed, failed
        if cond:
            passed += 1
            print(f"  PASS {name}")
        else:
            failed += 1
            print(f"  FAIL {name}")

    name = f"pay_{int(time.time())}"
    r = call("POST", "/api/login", {"name": name, "region": "CN"})
    assert r.get("ok"), f"登录失败: {r}"
    token = r["token"]

    # 1) SKU 列表：editor 渠道（沙箱合法），字段为 gems
    r = call("GET", "/api/pay/skus?channel=editor", token=token)
    check("skus(editor) ok", r.get("ok") is True)
    skus = r.get("data", {}).get("skus", [])
    check("skus 含 gold_small(gems=60)", any(s["skuId"] == "gold_small" and s.get("gems") == 60 for s in skus))
    sku = next(s for s in skus if s["skuId"] == "gold_small")

    # 2) 渠道拦截：wechat 拒（小游戏无内购，服务端兜底）
    r = call("GET", "/api/pay/skus?channel=wechat", token=token)
    check("skus(wechat) 拒 PAY_CHANNEL_DISABLED", not r.get("ok") and r.get("error") == "PAY_CHANNEL_DISABLED")
    r = call("POST", "/api/pay/order", {"skuId": "gold_small", "channel": "hacker"}, token=token)
    check("order(未知渠道) 拒 PAY_CHANNEL_BAD", not r.get("ok") and r.get("error") == "PAY_CHANNEL_BAD")

    # 3) 建单 → dev 收据验单 → 钻石到账且金币不动
    s0 = state(token)
    gems0, gold0 = s0["gems"], s0["gold"]
    check("新玩家 gems=0", gems0 == 0)
    r = call("POST", "/api/pay/order", {"skuId": "gold_small", "channel": "editor"}, token=token)
    check("order 建单 ok", r.get("ok") and r["data"]["orderNo"].startswith("PO"))
    order_no = r["data"]["orderNo"]

    receipt = f"dev:{order_no}:gold_small"
    r = call("POST", "/api/pay/verify", {"orderNo": order_no, "receipt": receipt}, token=token)
    check("verify 首次 ok 且发钻石", r.get("ok") and r["data"]["gemsGranted"] == sku["gems"])
    s1 = state(token)
    check(f"钻石到账 {gems0}+{sku['gems']}=={s1['gems']}", s1["gems"] == gems0 + sku["gems"])
    check("金币不受充值影响", s1["gold"] == gold0)

    # 4) 幂等：同一收据重放 → ok 但不再发货
    r = call("POST", "/api/pay/verify", {"orderNo": order_no, "receipt": receipt}, token=token)
    check("verify 重放幂等 ok(alreadyPaid)", r.get("ok") and r["data"].get("alreadyPaid") is True)
    s2 = state(token)
    check("重放不重复发货", s2["gems"] == s1["gems"])

    # 5) paid 订单换收据 → 拒
    r = call("POST", "/api/pay/verify", {"orderNo": order_no, "receipt": "dev:" + order_no + ":gold_big"}, token=token)
    check("paid 订单换收据拒 PAY_REPLAY", not r.get("ok") and r.get("error") == "PAY_REPLAY")
    check("重放拒后钻石不变", state(token)["gems"] == s1["gems"])

    # 6) 跨订单收据重放：order1 的已支付收据拿去验 order2 → 跨订单重放防线拦截
    r = call("POST", "/api/pay/order", {"skuId": "gold_mid", "channel": "editor"}, token=token)
    order2 = r["data"]["orderNo"]
    r = call("POST", "/api/pay/verify", {"orderNo": order2, "receipt": receipt}, token=token)
    check("收据串单拒 PAY_REPLAY(跨订单已入账)", not r.get("ok") and r.get("error") == "PAY_REPLAY")
    check("串单拒后钻石不变", state(token)["gems"] == s1["gems"])

    # 7) 他人订单
    r2 = call("POST", "/api/login", {"name": name + "b", "region": "CN"})
    token_b = r2["token"]
    r = call("POST", "/api/pay/verify", {"orderNo": order2, "receipt": f"dev:{order2}:gold_mid"}, token=token_b)
    check("他人订单拒 ORDER_FORBIDDEN", not r.get("ok") and r.get("error") == "ORDER_FORBIDDEN")

    # 8) 未知订单 / 未知 SKU
    r = call("POST", "/api/pay/verify", {"orderNo": "POnotexist", "receipt": "x"}, token=token)
    check("未知订单拒 ORDER_NOT_FOUND", not r.get("ok") and r.get("error") == "ORDER_NOT_FOUND")
    r = call("POST", "/api/pay/order", {"skuId": "no_such", "channel": "editor"}, token=token)
    check("未知 SKU 拒 SKU_NOT_FOUND", not r.get("ok") and r.get("error") == "SKU_NOT_FOUND")

    # 9) 正常完成 order2（收据正确格式）
    r = call("POST", "/api/pay/verify", {"orderNo": order2, "receipt": f"dev:{order2}:gold_mid"}, token=token)
    check("order2 验单 ok(320 钻)", r.get("ok") and r["data"]["gemsGranted"] == 320)

    # 10) 未鉴权
    r = call("GET", "/api/pay/skus?channel=editor")
    check("未登录拒 UNAUTHORIZED", not r.get("ok") and r.get("error") == "UNAUTHORIZED")

    print(f"\n结果: {passed} 通过, {failed} 失败")
    raise SystemExit(1 if failed else 0)


if __name__ == "__main__":
    main()
