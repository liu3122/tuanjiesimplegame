# -*- coding: utf-8 -*-
"""客户端 p3：ApiClient 新接口 + ChestBoard（报价/动画/特殊格）"""
import io

# ============ ApiClient ============
p = r'G:\SimpleProject\Assets\HotUpdateScripts\ChestGame\ApiClient.cs'
s = io.open(p, encoding='utf-8').read()
old = '''        public static Task<JSONNode> GetRankAsync() => GetAsync("/api/rank");'''
new = '''        public static Task<JSONNode> GetRankAsync() => GetAsync("/api/rank");

        public static Task<JSONNode> AcceptOfferAsync(long chestUid) =>
            PostAsync($"/api/chest/{chestUid}/offer/accept", "{}");

        public static Task<JSONNode> DeclineOfferAsync(long chestUid) =>
            PostAsync($"/api/chest/{chestUid}/offer/decline", "{}");

        public static Task<JSONNode> BuyDailyAsync() => PostAsync("/api/daily/buy", "{}");

        public static Task<JSONNode> GetDailyRankAsync() => GetAsync("/api/daily/rank");'''
assert old in s
s = s.replace(old, new)
io.open(p, 'w', encoding='utf-8', newline='\n').write(s)
print('ApiClient ok')

# ============ ChestBoard ============
p2 = r'G:\SimpleProject\Assets\HotUpdateScripts\ChestGame\ChestBoard.cs'
s2 = io.open(p2, encoding='utf-8').read()

# 字段：报价面板 + 上次 EV
old2 = '''        /// <summary>窥视选格模式：点技能后，下一次点格触发窥视而非排除</summary>
        private bool _peekTargeting;'''
new2 = '''        /// <summary>窥视选格模式：点技能后，下一次点格触发窥视而非排除</summary>
        private bool _peekTargeting;
        /// <summary>报价面板（pendingOffer 存在时显示）</summary>
        private GameObject _offerPanel;
        private Text _offerCountdown;
        private float _offerExpireAt = -1f;
        /// <summary>EV 滚动显示</summary>
        private Text _evText;
        private double _evShown;
        private double _evTarget;'''
assert old2 in s2
s2 = s2.replace(old2, new2)

# OnEliminate：报价锁 + 动画/震屏/特殊格反馈
old3 = '''            if (_chest["eliminated"][index].AsBool || _chest["remainingCount"].AsInt <= 1)
            {
                UIRoot.ToastStatic(_chest["remainingCount"].AsInt <= 1 ? "只剩最后一格，点右侧开箱" : "该格已排除");
                return;
            }
            _busy = true;
            var r = await ApiClient.EliminateAsync(_uid, index);
            _busy = false;
            if (r == null || !r["ok"].AsBool)
            {
                UIRoot.ToastStatic("排除失败：" + Screens.ErrText(r));
                return;
            }
            UIRoot.ToastStatic("排除揭示：" + r["data"]["revealedAmount"].AsInt.ToString("N0"));
            UIRoot.Instance.RefreshTop();
            await RefreshData();
            Redraw();
        }'''
new3 = '''            if (_chest["pendingOffer"] != null && !_chest["pendingOffer"].IsNull)
            {
                UIRoot.ToastStatic("银行家报价中，请先做出决定！");
                return;
            }
            if (_chest["eliminated"][index].AsBool || _chest["remainingCount"].AsInt <= 1)
            {
                UIRoot.ToastStatic(_chest["remainingCount"].AsInt <= 1 ? "只剩最后一格，点右侧开箱" : "该格已排除");
                return;
            }
            _busy = true;
            var r = await ApiClient.EliminateAsync(_uid, index);
            _busy = false;
            if (r == null || !r["ok"].AsBool)
            {
                UIRoot.ToastStatic("排除失败：" + Screens.ErrText(r));
                return;
            }
            int revealed = r["data"]["revealedAmount"].AsInt;
            string specialMsg = r["data"]["specialMsg"].Value;
            double evBefore = _chest["expectedValue"].AsDouble;
            if (!string.IsNullOrEmpty(specialMsg)) UIRoot.ToastStatic(specialMsg);
            UIRoot.ToastStatic("排除揭示：" + revealed.ToString("N0"));
            UIRoot.Instance.RefreshTop();
            // 表现层：开格弹跳 + 大额震屏
            StartCoroutine(PopSlot(index));
            if (revealed >= evBefore * 2) StartCoroutine(ShakeCamera());
            await RefreshData();
            Redraw();
        }

        /// <summary>开格弹跳动画</summary>
        private System.Collections.IEnumerator PopSlot(int index)
        {
            var tr = _slots[index].transform;
            float t = 0;
            while (t < 0.25f)
            {
                t += Time.deltaTime;
                float k = 1f + 0.35f * Mathf.Sin(Mathf.Clamp01(t / 0.25f) * Mathf.PI);
                tr.localScale = Vector3.one * k;
                yield return null;
            }
            tr.localScale = Vector3.one;
        }

        /// <summary>大额排除震屏（0.3s）</summary>
        private System.Collections.IEnumerator ShakeCamera()
        {
            var cam = Camera.main;
            if (cam == null) yield break;
            Vector3 origin = cam.transform.position;
            float t = 0;
            while (t < 0.3f)
            {
                t += Time.deltaTime;
                cam.transform.position = origin + new Vector3(Random.Range(-0.3f, 0.3f), Random.Range(-0.3f, 0.3f), 0);
                yield return null;
            }
            cam.transform.position = origin;
        }'''
assert old3 in s2
s2 = s2.replace(old3, new3)

# Update：EV 滚动 + 报价倒计时
old4 = '''        private async void OnSkill(int skillType, int? slotIndex = null)'''
new4 = '''        private void Update()
        {
            // EV 数字滚动
            if (_evText != null && _evTarget > 0)
            {
                if (Math.Abs(_evShown - _evTarget) > 0.5)
                {
                    _evShown = _evShown + (_evTarget - _evShown) * Mathf.Min(1f, Time.deltaTime * 6f);
                    _evText.text = "期望 V " + _evShown.ToString("F1");
                }
                else if (_evShown != _evTarget)
                {
                    _evShown = _evTarget;
                    _evText.text = "期望 V " + _evTarget.ToString("F1");
                }
            }
            // 报价倒计时
            if (_offerPanel != null && _offerCountdown != null && _offerExpireAt > 0)
            {
                float left = _offerExpireAt - Time.time;
                if (left <= 0)
                {
                    _ = AutoDecline();
                }
                else
                {
                    _offerCountdown.text = Mathf.Ceil(left).ToString("F0") + " 秒";
                }
            }
        }

        private async System.Threading.Tasks.Task AutoDecline()
        {
            _offerExpireAt = -1f;
            var r = await ApiClient.DeclineOfferAsync(_uid);
            UIRoot.ToastStatic("报价已超时，继续！");
            await RefreshData();
            Redraw();
        }

        private async void OnSkill(int skillType, int? slotIndex = null)'''
assert old4 in s2
s2 = s2.replace(old4, new4)

# Redraw：特殊格标记 + EV tween + 报价面板
old5 = '''                    else
                    {
                        text.text = "?";
                        text.fontSize = 40;
                        text.color = Color.white;
                    }
                    if (lockedSprite != null) img.sprite = lockedSprite;'''
new5 = '''                    else
                    {
                        string prefix = "";
                        if (_chest["luckySlots"] != null && _chest["luckySlots"][i.ToString()].AsBool) prefix = "\\u2605";
                        else if (_chest["trapSlots"] != null && _chest["trapSlots"][i.ToString()].AsBool) prefix = "\\u2620";
                        text.text = string.IsNullOrEmpty(prefix) ? "?" : prefix;
                        text.fontSize = string.IsNullOrEmpty(prefix) ? 40 : 34;
                        text.color = string.IsNullOrEmpty(prefix) ? Color.white
                            : (_chest["luckySlots"] != null && _chest["luckySlots"][i.ToString()].AsBool ? UITheme.Green : UITheme.Red);
                    }
                    if (lockedSprite != null) img.sprite = lockedSprite;'''
assert old5 in s2
s2 = s2.replace(old5, new5)

# info 文本改用独立 EV 引用（tween）：把 EV 行替换为占位节点不好做——直接在 info 生成后拆出 EV 文本：
old6 = '''            else
            {
                _info.text = "<b>" + config.Name + "</b>\\n剩余 " + remaining + " 格\\n\\n剩余总和 " + sum.ToString("N0")
                    + "\\n期望 V " + ev.ToString("F1") + "\\n\\n下次排除 " + (cost == 0 ? "免费" : cost + " 金币") + buff;
                _sellText.text = "出售给系统 " + offer.ToString("N0");
            }'''
new6 = '''            else
            {
                string kindTag = "";
                int kind = _chest["kind"].AsInt;
                if (kind == 2) kindTag = "\\n<color=#7ED957>幸运箱：★ 格排除 +" + GameConfig.Tables.TbChest[_chest["configId"].AsInt].SpecialValue + "</color>";
                else if (kind == 3) kindTag = "\\n<color=#E05D5D>诅咒箱：☠ 格排除 -" + GameConfig.Tables.TbChest[_chest["configId"].AsInt].SpecialValue + "</color>";
                _info.text = "<b>" + config.Name + (IsDaily ? "（每日挑战）" : "") + "</b>\\n剩余 " + remaining + " 格\\n\\n剩余总和 " + sum.ToString("N0")
                    + "\\n" + "下次排除 " + (cost == 0 ? "免费" : cost + " 金币") + buff + kindTag;
                _evTarget = ev;
                _sellText.text = "出售给系统 " + offer.ToString("N0");
            }'''
assert old6 in s2
s2 = s2.replace(old6, new6)

# info 里 EV 单独 text：Bind 时创建 _evText（挂在 info 后面），Redraw 末尾更新
old7 = '''            _info = UIManager.Find<Text>(panel, "info");'''
new7 = '''            _info = UIManager.Find<Text>(panel, "info");
            _evText = TDUIHelper.MakeText(panel, new Vector2(0, -155), TextAnchor.UpperLeft, 24);
            TDUIHelper.SetSize(_evText.rectTransform, 400, 32);
            _evShown = 0; _evTarget = 0;'''
assert old7 in s2
s2 = s2.replace(old7, new7)

# Redraw 末尾：报价面板显示/隐藏
old8 = '''            int quota = _chest["skillUsesRemaining"].AsInt;'''
new8 = '''            // 银行家报价面板
            RefreshOfferPanel();

            int quota = _chest["skillUsesRemaining"].AsInt;'''
assert old8 in s2
s2 = s2.replace(old8, new8)

# IsDaily 属性 + RefreshOfferPanel 方法
old9 = '''        private async Task RefreshData()'''
new9 = '''        private bool IsDaily => _chest != null && _chest["isDaily"].AsBool;

        /// <summary>银行家报价面板：pendingOffer 存在时弹出（金额+倒计时+接受/拒绝）</summary>
        private void RefreshOfferPanel()
        {
            bool hasOffer = _chest["pendingOffer"] != null && !_chest["pendingOffer"].IsNull;
            if (!hasOffer)
            {
                if (_offerPanel != null) { Object.Destroy(_offerPanel); _offerPanel = null; _offerExpireAt = -1f; }
                return;
            }
            if (_offerPanel == null)
            {
                _offerPanel = UITheme.CreatePanel(UIRoot.Instance.CanvasTransform, "panel_gold", new Vector2(-200, 0), new Vector2(620, 420));
                var title = TDUIHelper.MakeText(_offerPanel.transform, new Vector2(0, -30), TextAnchor.MiddleCenter, 30);
                title.text = "<color=#F5D061>银行家出价</color>";
                TDUIHelper.SetSize(title.rectTransform, 580, 44);
                var amount = TDUIHelper.MakeText(_offerPanel.transform, new Vector2(0, -110), TextAnchor.MiddleCenter, 56);
                amount.text = _chest["pendingOffer"].AsLong.ToString("N0");
                amount.color = UITheme.GoldLight;
                TDUIHelper.SetSize(amount.rectTransform, 580, 90);
                _offerCountdown = TDUIHelper.MakeText(_offerPanel.transform, new Vector2(0, -210), TextAnchor.MiddleCenter, 30);
                _offerCountdown.color = UITheme.Red;
                TDUIHelper.SetSize(_offerCountdown.rectTransform, 580, 40);
                var accept = UITheme.ThemedButton(_offerPanel.transform, new Vector2(-140, -320), new Vector2(240, 72), "接 受", "green", 30);
                accept.onClick.AddListener(async () =>
                {
                    var r = await ApiClient.AcceptOfferAsync(_uid);
                    if (r == null || !r["ok"].AsBool) { UIRoot.ToastStatic("接受失败：" + Screens.ErrText(r)); return; }
                    UIRoot.ToastStatic("成交！获得 " + r["data"]["income"].AsLong.ToString("N0"));
                    UIRoot.Instance.RefreshTop();
                    Screens.ShowBag();
                });
                var decline = UITheme.ThemedButton(_offerPanel.transform, new Vector2(140, -320), new Vector2(240, 72), "拒 绝", "red", 30);
                decline.onClick.AddListener(async () =>
                {
                    _offerExpireAt = -1f;
                    var r = await ApiClient.DeclineOfferAsync(_uid);
                    if (r != null && r["ok"].AsBool) UIRoot.ToastStatic("已拒绝，继续排除！");
                    await RefreshData();
                    Redraw();
                });
            }
            _offerExpireAt = Time.time + Mathf.Max(1f, _chest["offerExpireInSec"].AsFloat);
        }

        private async Task RefreshData()'''
assert old9 in s2
s2 = s2.replace(old9, new9)
io.open(p2, 'w', encoding='utf-8', newline='\n').write(s2)
print('ChestBoard ok')
