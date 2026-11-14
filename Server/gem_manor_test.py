# -*- coding: utf-8 -*-
"""软硬双币 + 宅邸改造 + 抢宝限次 回归（需服务端沙箱模式：--PaySandbox true，工作目录 Server/）。

覆盖：
- 双币：充值发钻石（金币不动）/ 兑换 1:10 / 超额拒 / 非法数额拒
- 宅邸：动工扣金币 + 二选一方案记录 / 区域称号（6 工程 → 小本商人）/ 金币不足拒 / BAD_CHOICE 拒
- 抢宝限次：sqlite 预插满次数 → RUSH_LIMIT；正常进房计数 +1（state.rushDaily 视图核对）
MANOR_MAX 需要把 manor_stage 拉满后重启服务端，由外部步骤验证（不在本脚本内重启）。
"""
import json
import sqlite3
import time
import urllib.request
from datetime import datetime, timezone

BASE = "http://127.0.0.1:5188"
DB = "simplegame.db"
TODAY = datetime.now(timezone.utc).strftime("%Y-%m-%d")


def call(method, path, body=None, token=None):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(BASE + path, data=data, method=method)
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


def sandbox_purchase(token, sku):
    """沙箱全链路购买：建单 → dev 收据验单，返回 (gems_delta_ok, granted)"""
    r = call("POST", "/api/pay/order", {"skuId": sku, "channel": "editor"}, token=token)
    assert r.get("ok"), f"建单失败: {r}"
    no = r["data"]["orderNo"]
    r = call("POST", "/api/pay/verify", {"orderNo": no, "receipt": f"dev:{no}:{sku}"}, token=token)
    assert r.get("ok"), f"验单失败: {r}"
    return r["data"]["gemsGranted"]


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

    name = f"gm_{int(time.time())}"
    r = call("POST", "/api/login", {"name": name, "region": "CN"})
    assert r.get("ok"), f"登录失败: {r}"
    token = r["token"]
    pid = r["playerId"]

    # ================= 双币 =================
    s = state(token)
    check("初始 gems=0 / manor.stage=0 / gemToGold=10",
          s["gems"] == 0 and s["manor"]["stage"] == 0 and s["gemToGold"] == 10)
    gold0 = s["gold"]

    granted = sandbox_purchase(token, "gold_small")
    s = state(token)
    check(f"充值发钻石 {granted}（金币不动）", s["gems"] == granted and s["gold"] == gold0)

    r = call("POST", "/api/gem/exchange", {"gems": 30}, token=token)
    s = state(token)
    check("兑换 30 钻 → +300 金", r.get("ok") and s["gems"] == granted - 30 and s["gold"] == gold0 + 300)
    r = call("POST", "/api/gem/exchange", {"gems": 99999}, token=token)
    check("超额兑换拒 NOT_ENOUGH_GEMS", not r.get("ok") and r.get("error") == "NOT_ENOUGH_GEMS")
    r = call("POST", "/api/gem/exchange", {"gems": 0}, token=token)
    check("0 钻兑换拒 BAD_GEMS", not r.get("ok") and r.get("error") == "BAD_GEMS")

    # ================= 宅邸改造 =================
    r = call("POST", "/api/manor/build", {"choice": 3}, token=token)
    check("choice=3 拒 BAD_CHOICE", not r.get("ok") and r.get("error") == "BAD_CHOICE")

    # 资金规划：现有 600 + 300 兑换金 = 900 金；工程 1(300)+2(410) 花得起，工程 3(550) 不够
    r = call("POST", "/api/manor/build", {"choice": 1}, token=token)
    check("工程1 动工 ok 扣 300", r.get("ok") and r["data"]["stage"] == 1)
    r = call("POST", "/api/manor/build", {"choice": 2}, token=token)
    check("工程2 动工 ok", r.get("ok") and r["data"]["stage"] == 2)
    s = state(token)
    check("方案选择已记录 {1:1, 2:2}", s["manor"]["choices"].get("1") == 1 and s["manor"]["choices"].get("2") == 2)

    # 按预算推进：工程 3、4 花得起，工程 5 按实际余额验证金币不足（费用取 next.cost 不硬编码）
    for step in (3, 4):
        s = state(token)
        cost = s["manor"]["next"]["cost"]
        r = call("POST", "/api/manor/build", {"choice": (step % 2) + 1}, token=token)
        check(f"工程{step} 动工 ok（扣 {cost}）", r.get("ok") and r["data"]["stage"] == step
              and state(token)["gold"] == s["gold"] - cost)
    s = state(token)
    cost5 = s["manor"]["next"]["cost"]
    check(f"余额 {s['gold']} < 工程费 {cost5}（预算耗尽）", s["gold"] < cost5)
    r = call("POST", "/api/manor/build", {"choice": 1}, token=token)
    check("金币不足拒 NOT_ENOUGH_GOLD", not r.get("ok") and r.get("error") == "NOT_ENOUGH_GOLD")

    # 充值 gold_mega(2600钻) 全兑换 → 完成剩余工程到 6 → 区域 1 称号
    sandbox_purchase(token, "gold_mega")
    gems_now = state(token)["gems"]
    r = call("POST", "/api/gem/exchange", {"gems": gems_now}, token=token)
    assert r.get("ok"), f"兑换失败: {r}"
    for i in range(2):
        r = call("POST", "/api/manor/build", {"choice": (i % 2) + 1}, token=token)
        assert r.get("ok"), f"工程{5 + i} 失败: {r}"
    s = state(token)
    check("区域1 完成 stage=6 且称号=小本商人", s["manor"]["stage"] == 6 and s["manor"]["title"] == "小本商人")
    check("下一区域工程在 next 视图", s["manor"]["next"]["areaName"] == "气派大厅")

    # ================= 抢宝限次 =================
    rname = name + "r"
    r = call("POST", "/api/login", {"name": rname, "region": "CN"})
    rtoken, rpid = r["token"], r["playerId"]

    # 限次为内存权威（防异步落库窗口被连发突破）：真实打满 5 次入场后拒绝
    # 每次等房间离开匹配期（填充4s+倒计时3s+余量），重进才计一次新入场
    for i in range(5):
        r = call("POST", "/api/rush/join", {"roomId": 1}, token=rtoken)
        assert r.get("ok"), f"第{i+1}次加入失败: {r}"
        time.sleep(10)
    r = call("POST", "/api/rush/join", {"roomId": 1}, token=rtoken)
    check("房1 满次拒 RUSH_LIMIT", not r.get("ok") and r.get("error") == "RUSH_LIMIT")
    r = call("POST", "/api/rush/join", {"roomId": 2}, token=rtoken)
    check("房2 正常加入", r.get("ok") is True)
    s = state(rtoken)
    rd = s["rushDaily"]
    check(f"限次视图 房1 used=5/5 房2 used=1/3",
          rd["1"]["used"] == 5 and rd["1"]["limit"] == 5 and rd["2"]["used"] == 1 and rd["2"]["limit"] == 3)

    print(f"\n结果: {passed} 通过, {failed} 失败")
    raise SystemExit(1 if failed else 0)


if __name__ == "__main__":
    main()
