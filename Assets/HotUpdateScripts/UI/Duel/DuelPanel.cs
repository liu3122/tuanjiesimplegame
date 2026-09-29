using System.Collections.Generic;
using System.Linq;
using Luban.SimpleJSON;
using UnityEngine;
using UnityEngine.UI;

namespace Game.HotUpdate.ChestGame
{
    /// <summary>
    /// 镜像对决界面（纯代码构建，无需 prefab）：加入 → 3 秒倒计时 → 并行开镜像宝箱。
    /// 我的 20 格面板 + 对手动态（同池不同位：对手揭示金额只影响"池面"推断）+ 技能/卖出/报价博弈。
    /// 中途退出按当前收购价锁定分数（逃跑惩罚），由服务端裁决。
    /// </summary>
    public class DuelPanel : MonoBehaviour
    {
        private DuelWsClient _ws;
        private JSONNode _me;
        private JSONNode _opp;
        private string _phase = "matching";
        private bool _finished;
        /// <summary>对赌房模式：赢家独得两个宝箱的结算总价值</summary>
        private bool _potMode;
        private readonly GameObject[] _slots = new GameObject[20];
        private readonly bool[] _oppEliminated = new bool[20];
        private readonly int[] _oppAmounts = new int[20];
        private Text _status;
        private Text _timer;
        private Text _oppText;
        private Text _meInfo;
        private Text _feed;
        private readonly List<string> _feedLines = new List<string>();
        private bool _peekTargeting;
        /// <summary>报价弹窗</summary>
        private GameObject _offerPanel;
        private Text _offerCountdown;
        private float _offerExpireAt = -1f;
        /// <summary>报价已过期/拒绝（同一份报价不再弹窗；新报价由 offerMade 重置）</summary>
        private bool _offerHidden;
        /// <summary>技能面板冷却行</summary>
        private readonly List<CdRow> _cooldownRows = new List<CdRow>();
        private float _nextCdRefresh;

        private class CdRow
        {
            public Button Btn;
            public Text Label;
            public string BaseText;
            public float EndAt;
        }

        /// <summary>模式选择器：镜像对决（固定奖池）/ 对赌房（赢家通吃）</summary>
        public static void ShowChooser()
        {
            var panel = UITheme.CreatePanel(UIRoot.Instance.CanvasTransform, "panel_gold", Vector2.zero, new Vector2(820, 560));
            var rt = panel.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            var title = TDUIHelper.MakeText(panel.transform, new Vector2(0, -230), TextAnchor.MiddleCenter, 34);
            title.text = "<color=#F5D061>" + L10n.Get("选择对决模式") + "</color>";
            TDUIHelper.SetSize(title.rectTransform, 780, 60);

            if (OnlineState.FeatureOn("duel"))
            {
                var a = UITheme.ThemedButton(panel.transform, new Vector2(0, -90), new Vector2(700, 120),
                    "<color=#6EC6FF>镜像对决</color> · 入场金 100\n<color=#9AA3B2>高分者赢固定奖池（入场金×2 扣 5% 抽成）</color>", "blue", 24);
                UITheme.SkinButton(a, "blue");
                a.onClick.AddListener(() => { Object.Destroy(panel); Show(false); });
            }
            if (OnlineState.FeatureOn("potduel"))
            {
                var b = UITheme.ThemedButton(panel.transform, new Vector2(0, -240), new Vector2(700, 120),
                    "<color=#E05D5D>对赌房</color> · 入场金 = 宝箱售价\n<color=#9AA3B2>同箱对开，赢家独得两个宝箱的全部价值（扣 5%）</color>", "gold", 24);
                if (OnlineState.Level < Screens.PotDuelMinLevel)
                {
                    b.interactable = false;
                    b.GetComponentInChildren<Text>().text =
                        "<color=#A8B4C2>对赌房 · Lv." + Screens.PotDuelMinLevel + L10n.Get(" 解锁") + "</color>\n<color=#77808C>赢家独得两个宝箱的全部价值（扣 5%）</color>";
                }
                b.onClick.AddListener(() => { Object.Destroy(panel); Show(true); });
            }
            var close = UITheme.ThemedButton(panel.transform, new Vector2(0, -440), new Vector2(200, 52), L10n.Get("取 消"), "red", 22);
            close.onClick.AddListener(() => Object.Destroy(panel));
        }

        public static async void Show(bool potMode = false)
        {
            string flag = potMode ? "potduel" : "duel";
            if (!OnlineState.FeatureOn(flag))
            {
                UIRoot.ToastStatic(L10n.Get("该玩法已关闭"));
                return;
            }
            UIRoot.ToastStatic(L10n.Get("正在匹配对手…"));
            var r = await ApiClient.JoinDuelAsync(potMode);
            if (r == null || !r["ok"].AsBool)
            {
                UIRoot.ToastStatic(L10n.Get("加入失败：") + Screens.ErrText(r));
                return;
            }
            var panel = UITheme.CreatePanel(UIRoot.Instance.CanvasTransform, "panel_dark", Vector2.zero, new Vector2(1040, 1560));
            var rt = panel.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            var self = panel.AddComponent<DuelPanel>();
            self._potMode = potMode;
            self.Bind(panel);
        }

        private void Bind(GameObject root)
        {
            var title = TDUIHelper.MakeText(root.transform, new Vector2(0, -40), TextAnchor.MiddleCenter, 34);
            title.text = _potMode
                ? "<color=#E05D5D>" + L10n.Get("对赌房 · 赢家通吃") + "</color>"
                : "<color=#F5D061>镜像对决 · 1v1</color>";
            TDUIHelper.SetSize(title.rectTransform, 1240, 50);
            _timer = TDUIHelper.MakeText(root.transform, new Vector2(0, -92), TextAnchor.MiddleCenter, 26);
            _timer.color = UITheme.TextSub;
            TDUIHelper.SetSize(_timer.rectTransform, 1000, 40);
            _status = TDUIHelper.MakeText(root.transform, new Vector2(0, -128), TextAnchor.MiddleCenter, 28);
            TDUIHelper.SetSize(_status.rectTransform, 1000, 44);

            // 我的棋盘（5×4）
            for (int i = 0; i < 20; i++)
            {
                int idx = i;
                int col = i % 5, row = i / 5;
                var slot = MakeSlot(root.transform, new Vector2(-256 + col * 128, 300 - row * 118), new Vector2(112, 96));
                _slots[i] = slot;
                slot.GetComponent<Button>().onClick.AddListener(() => OnSlot(idx));
            }

            _meInfo = TDUIHelper.MakeText(root.transform, new Vector2(0, -1040), TextAnchor.UpperLeft, 22);
            _meInfo.color = UITheme.TextSub;
            TDUIHelper.SetSize(_meInfo.rectTransform, 960, 90);

            // 右列：对手面板 + 动态
            var oppPanel = UITheme.CreatePanel(root.transform, "panel_card", new Vector2(0, 500), new Vector2(940, 230));
            var oppTitle = TDUIHelper.MakeText(oppPanel.transform, new Vector2(0, -20), TextAnchor.MiddleCenter, 24);
            oppTitle.text = "<color=#E05D5D>对手</color>";
            _oppText = TDUIHelper.MakeText(oppPanel.transform, new Vector2(0, -66), TextAnchor.UpperLeft, 22);
            TDUIHelper.SetSize(_oppText.rectTransform, 900, 140);
            _feed = TDUIHelper.MakeText(root.transform, new Vector2(0, -1140), TextAnchor.UpperLeft, 20);
            _feed.color = UITheme.TextSub;
            TDUIHelper.SetSize(_feed.rectTransform, 960, 480);

            // 底部操作
            var skillBtn = UITheme.ThemedButton(root.transform, new Vector2(-230, -690), new Vector2(280, 64), L10n.Get("技 能"), "blue", 26);
            skillBtn.onClick.AddListener(OpenSkillPanel);
            var sellBtn = UITheme.ThemedButton(root.transform, new Vector2(230, -690), new Vector2(280, 64), L10n.Get("卖给银行·锁定"), "green", 24);
            sellBtn.onClick.AddListener(() => _ws?.Sell());
            var back = UITheme.ThemedButton(root.transform, new Vector2(410, -730), new Vector2(170, 56), L10n.Get("离 开"), "red", 20);
            back.onClick.AddListener(() => { _ws?.Close(); Screens.ShowMain(); });

            _status.text = L10n.Get("等待对手中…");
            _ws = new DuelWsClient();
            _ws.Connect();
        }

        private static GameObject MakeSlot(Transform parent, Vector2 pos, Vector2 size)
        {
            var go = new GameObject("slot", typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            go.GetComponent<Image>().sprite = UITheme.Load("slot_locked");
            var txt = TDUIHelper.MakeText(go.transform, Vector2.zero, TextAnchor.MiddleCenter, 30);
            TDUIHelper.SetSize(txt.rectTransform, size.x, size.y);
            txt.text = "?";
            return go;
        }

        private bool CanAct => _me != null && !_finished && _phase == "match";

        private void OnSlot(int index)
        {
            if (!CanAct) return;
            if (_peekTargeting)
            {
                _peekTargeting = false;
                if (_me["eliminated"][index].AsBool) { UIRoot.ToastStatic(L10n.Get("该格已排除，请选未排除的格")); return; }
                _ws.Skill(4, index);
                return;
            }
            if (_me["pendingOffer"] != null && !_me["pendingOffer"].IsNull)
            {
                UIRoot.ToastStatic(L10n.Get("银行家报价中，请先做出决定！"));
                return;
            }
            if (_me["eliminated"][index].AsBool) return;
            if (_me["remainingCount"].AsInt <= 1) return;
            _ws.Eliminate(index);
        }

        private void Update()
        {
            _ws?.Pump(OnEvent);
            if (_offerPanel != null && _offerCountdown != null && _offerExpireAt > 0)
            {
                float left = _offerExpireAt - Time.time;
                if (left > 0) _offerCountdown.text = Mathf.Ceil(left).ToString("F0") + " " + L10n.Get("秒");
            }
            // 技能面板冷却倒计时
            if (_cooldownRows.Count > 0 && Time.time >= _nextCdRefresh)
            {
                _nextCdRefresh = Time.time + 1f;
                for (int i = _cooldownRows.Count - 1; i >= 0; i--)
                {
                    var row = _cooldownRows[i];
                    if (row.Btn == null) { _cooldownRows.RemoveAt(i); continue; }
                    int remain = Mathf.CeilToInt(row.EndAt - Time.time);
                    if (remain <= 0)
                    {
                        row.Btn.interactable = true;
                        row.Label.text = row.BaseText;
                        _cooldownRows.RemoveAt(i);
                    }
                    else
                    {
                        row.Label.text = row.BaseText + " · <color=#A8B4C2>" + L10n.Get("冷却") + " " + remain + "s</color>";
                    }
                }
            }
        }

        private void OnEvent(JSONNode e)
        {
            switch (e["type"].Value)
            {
                case "snapshot":
                    _phase = e["phase"].Value;
                    _me = e["me"].IsNull ? null : e["me"];
                    if (!e["opp"].IsNull) _opp = e["opp"];
                    _offerHidden = false;
                    Redraw();
                    break;
                case "joined":
                    PushFeed(e["name"].Value + " " + L10n.Get("进入（") + e["count"].AsInt + "/" + e["need"].AsInt + "）");
                    break;
                case "phase":
                    _phase = e["phase"].Value;
                    if (_phase == "countdown") _status.text = "<color=#E05D5D>" + L10n.Get("准备！") + "</color>";
                    else if (_phase == "match")
                    {
                        _status.text = "<color=#4DD0A5>" + L10n.Get("对决开始！随时可卖给银行锁定分数") + "</color>";
                        PushFeed(L10n.Get("对手与你共享同一金额池（格位不同），留意对手排除的金额！"));
                    }
                    break;
                case "countdown":
                {
                    int sec = e["remainSec"].AsInt;
                    if (sec > 0) _status.text = "<color=#E05D5D>" + sec + "</color>";
                    break;
                }
                case "remainSec":
                    _timer.text = L10n.Get("剩余") + " <color=#F5D061>" + e["remainSec"].AsInt + "</color> s · " + L10n.Get("超时按收购价锁定");
                    break;
                case "me":
                    _me = e["me"];
                    if (e["offerMade"].AsBool) _offerHidden = false;
                    if (!e["message"].IsNull) UIRoot.ToastStatic(e["message"].Value);
                    Redraw();
                    break;
                case "oppAction":
                {
                    string kind = e["kind"].Value;
                    if (kind == "eliminate")
                    {
                        int slot = e["slot"].AsInt;
                        _oppEliminated[slot] = true;
                        _oppAmounts[slot] = e["amount"].AsInt;
                        PushFeed(L10n.Get("对手排除第") + " " + (slot + 1) + " " + L10n.Get("格") + " → <color=#F5D061>" + e["amount"].AsInt.ToString("N0") + "</color>");
                    }
                    else if (kind == "skill")
                    {
                        PushFeed(L10n.Get("对手使用了") + "【" + e["skillName"].Value + "】");
                    }
                    RefreshOppView();
                    break;
                }
                case "offerGone":
                    _offerHidden = true;
                    if (_offerPanel != null) { Destroy(_offerPanel); _offerPanel = null; _offerExpireAt = -1f; }
                    break;
                case "locked":
                    PushFeed("<color=#F5D061>" + e["name"].Value + "</color> " + L10n.Get("锁定分数") + " " + e["score"].AsLong.ToString("N0"));
                    if (e["isBot"].AsBool == false && e["name"].Value == ApiClient.PlayerName)
                    {
                        _finished = true;
                        _status.text = L10n.Get("已锁定，等待对手…");
                    }
                    RefreshOppView();
                    break;
                case "settled":
                    OnSettled(e);
                    break;
                case "err":
                {
                    string code = e["code"].Value;
                    if (code == "ERR_COOLDOWN") UIRoot.ToastStatic(L10n.Get("技能冷却中，还剩") + " " + e["remainSec"].AsInt + "s");
                    else UIRoot.ToastStatic(code);
                    break;
                }
            }
        }

        private void OnSettled(JSONNode e)
        {
            _finished = true;
            _ws?.Close();
            var sb = new System.Text.StringBuilder();
            foreach (JSONNode row in e["board"])
            {
                string tag = row["win"].AsBool ? " <color=#4DD0A5>WIN</color>" : "";
                sb.Append(row["name"].Value).Append("  ").Append(row["score"].AsLong.ToString("N0")).Append(tag).Append('\n');
            }
            long delta = e["myDelta"].AsLong;
            bool tie = e["tie"].AsBool;
            _status.text = tie
                ? (_potMode
                    ? "<color=#F5D061>" + L10n.Get("平局！各自落袋自己宝箱的价值") + "</color>"
                    : "<color=#F5D061>" + L10n.Get("平局！入场金已退回") + "</color>")
                : (delta > 0 ? "<color=#4DD0A5>" + L10n.Get("胜利！净赚") + " +" + delta.ToString("N0") + "</color>"
                             : "<color=#E05D5D>" + L10n.Get("惜败") + " " + delta.ToString("N0") + "</color>");
            PushFeed("—— " + L10n.Get("结算") + " ——\n" + sb.ToString());
            UIRoot.Instance.RefreshTop();
            long myScore = 0; long oppScore = 0; string oppName = "?";
            foreach (JSONNode row in e["board"])
            {
                if (row["name"].Value == ApiClient.PlayerName) myScore = row["score"].AsLong;
                else { oppScore = row["score"].AsLong; oppName = row["name"].Value; }
            }
            var shareRow = UITheme.ThemedButton(transform, new Vector2(0, -620), new Vector2(280, 56), "分享战报", "gold", 24);
            shareRow.onClick.AddListener(() => BattleReport.ShowDuel(delta > 0, tie, delta, myScore, oppScore, oppName, _potMode));
        }

        private void Redraw()
        {
            if (_me == null) return;
            var lockedSprite = UITheme.Load("slot_locked");
            var openSprite = UITheme.Load("slot_open");
            for (int i = 0; i < 20; i++)
            {
                var text = _slots[i].GetComponentInChildren<Text>();
                var img = _slots[i].GetComponent<Image>();
                bool eliminated = _me["eliminated"][i].AsBool;
                if (eliminated)
                {
                    JSONNode amount = _me["revealedAmounts"][i];
                    text.text = amount.IsNull ? "?" : amount.AsInt.ToString("N0");
                    text.fontSize = 22;
                    text.color = new Color(0.55f, 0.58f, 0.62f);
                    if (openSprite != null) img.sprite = openSprite;
                    _slots[i].GetComponent<Button>().interactable = false;
                }
                else
                {
                    var peeked = _me["peekedSlots"];
                    var detected = _me["detectedSlots"];
                    if (peeked != null && !peeked[i.ToString()].IsNull)
                    {
                        text.text = peeked[i.ToString()].AsInt.ToString("N0");
                        text.fontSize = 22;
                        text.color = UITheme.GoldLight;
                    }
                    else if (detected != null && detected[i.ToString()].AsBool)
                    {
                        text.text = "\u25B2";
                        text.fontSize = 32;
                        text.color = UITheme.Blue;
                    }
                    else
                    {
                        text.text = "?";
                        text.fontSize = 30;
                        text.color = Color.white;
                    }
                    if (lockedSprite != null) img.sprite = lockedSprite;
                    _slots[i].GetComponent<Button>().interactable = CanAct;
                }
            }

            string buff = "";
            if (_me["priceBuffNext"].AsBool) buff += "\n抬价 +15% 已就绪";
            if (_me["insuranceActive"].AsBool) buff += "\n保底已生效";
            _meInfo.text = L10n.Get("剩余") + " " + _me["remainingCount"].AsInt + " " + L10n.Get("格") + " · " + L10n.Get("总和") + " " + _me["remainingSum"].AsLong.ToString("N0")
                + "\nEV " + _me["expectedValue"].AsDouble.ToString("F1") + " · " + L10n.Get("银行收购") + " " + _me["bankerOffer"].AsLong.ToString("N0") + buff;
            RefreshOppView();
            RefreshOfferPanel();
        }

        private void RefreshOppView()
        {
            if (_oppText == null) return;
            if (_opp != null && !_opp.IsNull)
            {
                int elims = 0;
                foreach (bool b in _oppEliminated) if (b) elims++;
                _oppText.text = "<color=#F5D061>" + _opp["name"].Value + "</color>" + (_opp["isBot"].AsBool ? "（AI）" : "")
                    + "\n" + L10n.Get("已排除") + " " + elims + " " + L10n.Get("格") + " · " + L10n.Get("剩余") + " " + (20 - elims) + " " + L10n.Get("格")
                    + "\n" + (_opp["finished"].AsBool
                        ? L10n.Get("已锁定") + "：<color=#F5D061>" + (_opp["score"].IsNull ? "?" : _opp["score"].AsLong.ToString("N0")) + "</color>"
                        : L10n.Get("对决中…"));
            }
            else
            {
                _oppText.text = L10n.Get("等待对手…");
            }
        }

        /// <summary>银行家报价弹窗（pendingOffer 存在且未隐藏时显示）</summary>
        private void RefreshOfferPanel()
        {
            bool hasOffer = !_offerHidden && _me != null && _me["pendingOffer"] != null && !_me["pendingOffer"].IsNull;
            if (!hasOffer)
            {
                if (_offerPanel != null) { Destroy(_offerPanel); _offerPanel = null; _offerExpireAt = -1f; }
                return;
            }
            if (_offerPanel == null)
            {
                _offerPanel = UITheme.CreatePanel(UIRoot.Instance.CanvasTransform, "panel_gold", Vector2.zero, new Vector2(560, 380));
                var rt = _offerPanel.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                var title = TDUIHelper.MakeText(_offerPanel.transform, new Vector2(0, -26), TextAnchor.MiddleCenter, 28);
                title.text = "<color=#F5D061>" + L10n.Get("银行家出价") + "</color>";
                TDUIHelper.SetSize(title.rectTransform, 520, 44);
                var amount = TDUIHelper.MakeText(_offerPanel.transform, new Vector2(0, -100), TextAnchor.MiddleCenter, 52);
                amount.text = _me["pendingOffer"].AsLong.ToString("N0");
                amount.color = UITheme.GoldLight;
                TDUIHelper.SetSize(amount.rectTransform, 520, 80);
                _offerCountdown = TDUIHelper.MakeText(_offerPanel.transform, new Vector2(0, -190), TextAnchor.MiddleCenter, 28);
                _offerCountdown.color = UITheme.Red;
                TDUIHelper.SetSize(_offerCountdown.rectTransform, 520, 40);
                var accept = UITheme.ThemedButton(_offerPanel.transform, new Vector2(-130, -290), new Vector2(220, 64), L10n.Get("接受·锁定"), "green", 26);
                accept.onClick.AddListener(() => { _offerExpireAt = -1f; _ws.Accept(); });
                var decline = UITheme.ThemedButton(_offerPanel.transform, new Vector2(130, -290), new Vector2(220, 64), L10n.Get("拒 绝"), "red", 26);
                decline.onClick.AddListener(() => { _offerExpireAt = -1f; _ws.Decline(); });
            }
            _offerExpireAt = Time.time + Mathf.Max(1f, _me["offerExpireInSec"].AsFloat);
        }

        /// <summary>技能面板：全部技能（额度/冷却沿用服务端裁决）</summary>
        private void OpenSkillPanel()
        {
            if (!CanAct) { UIRoot.ToastStatic(L10n.Get("当前不能使用技能")); return; }
            if (_me["skillUsedByMe"].AsBool) { UIRoot.ToastStatic(L10n.Get("你对本宝箱已用过技能")); return; }

            var panel = UITheme.CreatePanel(UIRoot.Instance.CanvasTransform, "panel_gold", Vector2.zero, new Vector2(680, 1120));
            var rt = panel.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);

            var title = TDUIHelper.MakeText(panel.transform, new Vector2(0, -26), TextAnchor.MiddleCenter, 32);
            title.text = L10n.Get("选择技能（限一次 · 用后进入冷却）");
            TDUIHelper.SetSize(title.rectTransform, 640, 50);

            float y = -110;
            _cooldownRows.Clear();
            foreach (var c in GameConfig.Tables.TbCharacter.DataList)
            {
                var skill = c;
                string cdTag = skill.SkillCooldownSec > 0 ? " · CD " + skill.SkillCooldownSec + "s" : "";
                string baseText = "<color=#" + c.ColorHex + ">" + c.SkillName + "</color> · " + c.SkillDesc + cdTag;
                var row = UITheme.ThemedButton(panel.transform, new Vector2(0, y), new Vector2(620, 92), baseText, "gold", 20);
                UITheme.SkinButton(row, "blue");
                var label = row.GetComponentInChildren<Text>();
                label.alignment = TextAnchor.MiddleLeft;
                label.fontSize = 17;
                var cds = _me["skillCooldowns"];
                int remain = cds != null ? Mathf.Max(0, cds[skill.SkillType.ToString()].AsInt) : 0;
                int minLv = Screens.MinLevelForSkill(skill.SkillType);
                if (OnlineState.Level < minLv)
                {
                    row.interactable = false;
                    label.text = baseText + " · <color=#A8B4C2>Lv." + minLv + "</color>";
                }
                else if (remain > 0)
                {
                    row.interactable = false;
                    _cooldownRows.Add(new CdRow { Btn = row, Label = label, BaseText = baseText, EndAt = Time.time + remain });
                    label.text = baseText + " · <color=#A8B4C2>" + L10n.Get("冷却") + " " + remain + "s</color>";
                }
                row.onClick.AddListener(() =>
                {
                    Destroy(panel);
                    if (skill.SkillType == 4)
                    {
                        _peekTargeting = true;
                        UIRoot.ToastStatic(L10n.Get("窥视：点击一个未排除的格子查看金额"));
                    }
                    else
                    {
                        _ws.Skill(skill.SkillType);
                    }
                });
                y -= 102;
            }

            var closeBtn = UITheme.ThemedButton(panel.transform, new Vector2(0, y + 10), new Vector2(240, 56), L10n.Get("取 消"), "red", 24);
            closeBtn.onClick.AddListener(() => Destroy(panel));
        }

        private void PushFeed(string line)
        {
            _feedLines.Add(line);
            if (_feedLines.Count > 11) _feedLines.RemoveAt(0);
            _feed.text = string.Join("\n", _feedLines.ToArray());
        }

        private void OnDestroy()
        {
            _ws?.Close();
        }
    }
}
