using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Luban.SimpleJSON;
using UnityEngine;
using UnityEngine.UI;

namespace Game.HotUpdate.ChestGame
{
    /// <summary>
    /// 开宝箱界面（prefab Bind 模式）：ChestBoardScreen 加载后挂本组件。
    /// 20 格排除 / 系统出售与终局开箱 / 技能三选一（额度与"每玩家一次"由服务端裁决）。
    /// </summary>
    public class ChestBoard : MonoBehaviour
    {
        private long _uid;
        private JSONNode _chest;
        private readonly GameObject[] _slots = new GameObject[20];
        private Text _info;
        private Button _sellButton;
        private Text _sellText;
        private Button _skillsButton;
        private Text _skillHint;
        private bool _busy;
        /// <summary>窥视选格模式：点技能后，下一次点格触发窥视而非排除</summary>
        private bool _peekTargeting;
        /// <summary>报价面板（pendingOffer 存在时显示）</summary>
        private GameObject _offerPanel;
        private Text _offerCountdown;
        private float _offerExpireAt = -1f;
        /// <summary>EV 滚动显示</summary>
        private Text _evText;
        private double _evShown;
        private double _evTarget;
        /// <summary>技能面板中冷却中的行（倒计时结束自动解锁）</summary>
        private readonly List<CdRow> _cooldownRows = new List<CdRow>();
        private float _nextCdRefresh;
        /// <summary>教学箱引导：0=排除 1=费用与出售 2=报价抉择（isTutorial 箱显示）</summary>
        private Text _tutBanner;
        private int _tutStep;

        /// <summary>技能面板冷却行</summary>
        private class CdRow
        {
            public Button Btn;
            public Text Label;
            public string BaseText;
            public float EndAt;
        }

        public static async void Show(long uid)
        {
            UITheme.PreloadAtlas("open");
            var root = await UIManager.ShowAsync("ChestBoardScreen");
            if (root == null) { UIRoot.ToastStatic(L10n.Get("界面加载失败")); return; }

            var board = root.GetComponent<ChestBoard>();
            if (board == null) board = root.AddComponent<ChestBoard>();
            board._uid = uid;
            await board.Bind(root);
        }

        private async Task Bind(GameObject root)
        {
            var back = UIManager.Find<Button>(root.transform, "btn_back");
            back.onClick.RemoveAllListeners();
            back.onClick.AddListener(() => Screens.ShowBag());

            for (int i = 0; i < 20; i++)
            {
                int idx = i;
                var slot = root.transform.Find("slot_" + i);
                if (slot == null) continue;
                _slots[i] = slot.gameObject;
                var btn = slot.GetComponent<Button>();
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => OnEliminate(idx));
            }

            var panel = root.transform.Find("info_panel");
            _info = UIManager.Find<Text>(panel, "info");
            _evText = TDUIHelper.MakeText(panel, new Vector2(0, -155), TextAnchor.UpperLeft, 24);
            TDUIHelper.SetSize(_evText.rectTransform, 400, 32);
            _evShown = 0; _evTarget = 0;
            _sellButton = UIManager.Find<Button>(panel, "btn_sell");
            _sellText = UIManager.Find<Text>(panel, "btn_sell/label");
            _sellButton.onClick.RemoveAllListeners();
            _sellButton.onClick.AddListener(OnSell);
            _skillHint = UIManager.Find<Text>(panel, "skill_hint");
            _skillsButton = UIManager.Find<Button>(panel, "btn_skills");
            _skillsButton.onClick.RemoveAllListeners();
            _skillsButton.onClick.AddListener(OpenSkillPanel);

            await RefreshData();
            if (_chest == null)
            {
                UIRoot.ToastStatic(L10n.Get("宝箱不存在"));
                Screens.ShowBag();
                return;
            }
            Redraw();
        }

        private bool IsDaily => _chest != null && _chest["isDaily"].AsBool;

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
                var accept = UITheme.ThemedButton(_offerPanel.transform, new Vector2(-140, -320), new Vector2(240, 72), L10n.Get("接 受"), "green", 30);
                accept.onClick.AddListener(async () =>
                {
                    var r = await ApiClient.AcceptOfferAsync(_uid);
                    if (r == null || !r["ok"].AsBool) { UIRoot.ToastStatic(L10n.Get("接受失败：") + Screens.ErrText(r)); return; }
                    UIRoot.ToastStatic("成交！获得 " + r["data"]["income"].AsLong.ToString("N0"));
                    UIRoot.Instance.RefreshTop();
                    Screens.ShowBag();
                });
                var decline = UITheme.ThemedButton(_offerPanel.transform, new Vector2(140, -320), new Vector2(240, 72), L10n.Get("拒 绝"), "red", 30);
                decline.onClick.AddListener(async () =>
                {
                    _offerExpireAt = -1f;
                    var r = await ApiClient.DeclineOfferAsync(_uid);
                    if (r != null && r["ok"].AsBool) UIRoot.ToastStatic(L10n.Get("已拒绝，继续排除！"));
                    await RefreshData();
                    Redraw();
                });
            }
            _offerExpireAt = Time.time + Mathf.Max(1f, _chest["offerExpireInSec"].AsFloat);
            if (_tutStep < 2) _tutStep = 2; // 教学推进：首个报价出现
            RefreshTutorial();
        }

        private async Task RefreshData()
        {
            await OnlineState.Refresh();
            _chest = null;
            if (OnlineState.Chests != null)
            {
                foreach (JSONNode c in OnlineState.Chests)
                {
                    if (c["uid"].AsLong == _uid) { _chest = c; break; }
                }
            }
        }

        private void Redraw()
        {
            if (_chest == null) return;
            var config = GameConfig.Tables.TbChest.DataList.FirstOrDefault(x => x.Id == _chest["configId"].AsInt);
            var lockedSprite = UITheme.Load("slot_locked");
            var openSprite = UITheme.Load("slot_open");

            for (int i = 0; i < 20; i++)
            {
                if (_slots[i] == null) continue;
                var text = _slots[i].GetComponentInChildren<Text>();
                var img = _slots[i].GetComponent<Image>();
                bool eliminated = _chest["eliminated"][i].AsBool;
                JSONNode amount = _chest["revealedAmounts"][i];
                if (eliminated)
                {
                    text.text = amount.IsNull ? "?" : amount.AsInt.ToString("N0");
                    text.fontSize = 24;
                    text.color = new Color(0.55f, 0.58f, 0.62f);
                    if (openSprite != null) img.sprite = openSprite;
                }
                else
                {
                    var peekedNode = _chest["peekedSlots"];
                    var detectedNode = _chest["detectedSlots"];
                    bool peeked = peekedNode != null && !peekedNode[i.ToString()].IsNull;
                    bool detected = detectedNode != null && detectedNode[i.ToString()].AsBool;
                    if (peeked)
                    {
                        text.text = peekedNode[i.ToString()].AsInt.ToString("N0");
                        text.fontSize = 24;
                        text.color = UITheme.GoldLight;
                    }
                    else if (detected)
                    {
                        text.text = "\u25B2";
                        text.fontSize = 36;
                        text.color = UITheme.Blue;
                    }
                    else
                    {
                        string prefix = "";
                        if (_chest["luckySlots"] != null && _chest["luckySlots"][i.ToString()].AsBool) prefix = "\u2605";
                        else if (_chest["trapSlots"] != null && _chest["trapSlots"][i.ToString()].AsBool) prefix = "\u2620";
                        text.text = string.IsNullOrEmpty(prefix) ? "?" : prefix;
                        text.fontSize = string.IsNullOrEmpty(prefix) ? 40 : 34;
                        text.color = string.IsNullOrEmpty(prefix) ? Color.white
                            : (_chest["luckySlots"] != null && _chest["luckySlots"][i.ToString()].AsBool ? UITheme.Green : UITheme.Red);
                    }
                    if (lockedSprite != null) img.sprite = lockedSprite;
                }
            }

            int remaining = _chest["remainingCount"].AsInt;
            long offer = _chest["bankerOffer"].AsLong;
            int cost = _chest["nextEliminateCost"].AsInt;
            long sum = _chest["remainingSum"].AsLong;
            double ev = _chest["expectedValue"].AsDouble;
            int freeBuff = _chest["freeEliminateBuff"].AsInt;
            bool priceBuff = _chest["priceBuffNext"].AsBool;
            string buff = freeBuff > 0 ? "\n免排 ×" + freeBuff : "";
            if (priceBuff) buff += "\n抬价 +15% 已就绪";
            if (_chest["insuranceActive"].AsBool) buff += "\n保底已生效";
            if (_chest["taxReductionCount"].AsInt > 0) buff += "\n减税 ×" + _chest["taxReductionCount"].AsInt;
            if (_chest["identified"].AsBool) buff += "\n已鉴定";

            if (remaining <= 1)
            {
                long final = 0;
                for (int i = 0; i < 20; i++)
                {
                    if (!_chest["eliminated"][i].AsBool && !_chest["revealedAmounts"][i].IsNull)
                        final = _chest["revealedAmounts"][i].AsInt;
                }
                _info.text = "<b>" + config.Name + "</b>\n\n终局！最后一格\n\n开箱可得 " + final.ToString("N0");
                _sellText.text = L10n.Get("开 箱（") + final.ToString("N0") + "）";
            }
            else
            {
                string kindTag = "";
                int kind = _chest["kind"].AsInt;
                if (kind == 2) kindTag = "\n<color=#7ED957>幸运箱：★ 格排除 +" + GameConfig.Tables.TbChest[_chest["configId"].AsInt].SpecialValue + "</color>";
                else if (kind == 3) kindTag = "\n<color=#E05D5D>诅咒箱：☠ 格排除 -" + GameConfig.Tables.TbChest[_chest["configId"].AsInt].SpecialValue + "</color>";
                _info.text = "<b>" + config.Name + (IsDaily ? L10n.Get("（每日挑战）") : "") + "</b>\n剩余 " + remaining + " 格\n\n剩余总和 " + sum.ToString("N0")
                    + "\n" + "下次排除 " + (cost == 0 ? L10n.Get("免费") : cost + " 金币") + buff + kindTag;
                _evTarget = ev;
                _sellText.text = "出售给系统 " + offer.ToString("N0");
            }

            // 银行家报价面板
            RefreshOfferPanel();

            int quota = _chest["skillUsesRemaining"].AsInt;
            bool iUsed = _chest["skillUsedByMe"].AsBool;
            int quotaMax = GameConfig.Tables.TbGlobal.ChestMaxSkillUses;
            _skillHint.text = quota <= 0
                ? L10n.Get("技能额度已耗尽")
                : "技能额度 " + quota + "/" + quotaMax + " · " + (iUsed ? L10n.Get("我已用过") : L10n.Get("我可任选一个技能（限一次）"));
            if (_skillsButton != null) _skillsButton.interactable = quota > 0 && !iUsed && remaining > 1 && !_busy;

            RefreshTutorial();
        }

        /// <summary>教学箱引导横幅：三步推进（排除 → 费用/出售 → 报价抉择）</summary>
        private void RefreshTutorial()
        {
            if (_chest == null || !_chest["isTutorial"].AsBool) return;
            if (_tutBanner == null)
            {
                _tutBanner = TDUIHelper.MakeText(transform, new Vector2(0, 390), TextAnchor.MiddleCenter, 26);
                _tutBanner.color = UITheme.GoldLight;
                TDUIHelper.SetSize(_tutBanner.rectTransform, 1200, 60);
            }
            string[] steps =
            {
                L10n.Get("教学 1/3：点击任意未排除的格子——注意「剩余总和」与「期望 V」的变化"),
                L10n.Get("教学 2/3：排除小额会提高期望值，但排除费会递增；随时可点【出售给系统】落袋为安"),
                L10n.Get("教学 3/3：银行家出价了！接受 = 立即到手，拒绝 = 继续排除博更高——卖或不卖，这是本游戏的核心抉择"),
            };
            _tutBanner.text = _tutStep < steps.Length ? steps[_tutStep] : L10n.Get("教学完成：去商城买真正的宝箱吧！");
        }

        private async void OnEliminate(int index)
        {
            if (_busy || _chest == null) return;
            if (_peekTargeting)
            {
                _peekTargeting = false;
                if (_chest["eliminated"][index].AsBool) { UIRoot.ToastStatic(L10n.Get("该格已排除，请选未排除的格")); return; }
                OnSkill(4, index);
                return;
            }
            if (_chest["pendingOffer"] != null && !_chest["pendingOffer"].IsNull)
            {
                UIRoot.ToastStatic(L10n.Get("银行家报价中，请先做出决定！"));
                return;
            }
            if (_chest["eliminated"][index].AsBool || _chest["remainingCount"].AsInt <= 1)
            {
                UIRoot.ToastStatic(_chest["remainingCount"].AsInt <= 1 ? L10n.Get("只剩最后一格，点右侧开箱") : L10n.Get("该格已排除"));
                return;
            }
            _busy = true;
            var r = await ApiClient.EliminateAsync(_uid, index);
            _busy = false;
            if (r == null || !r["ok"].AsBool)
            {
                UIRoot.ToastStatic(L10n.Get("排除失败：") + Screens.ErrText(r));
                return;
            }
            int revealed = r["data"]["revealedAmount"].AsInt;
            string specialMsg = r["data"]["specialMsg"].Value;
            double evBefore = _chest["expectedValue"].AsDouble;
            if (!string.IsNullOrEmpty(specialMsg)) UIRoot.ToastStatic(specialMsg);
            UIRoot.ToastStatic(L10n.Get("排除揭示：") + revealed.ToString("N0"));
            if (_tutStep == 0) _tutStep = 1; // 教学推进：已排除过一格
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
        }

        private void Update()
        {
            // EV 数字滚动
            if (_evText != null && _evTarget > 0)
            {
                if (System.Math.Abs(_evShown - _evTarget) > 0.5)
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
            // 技能冷却倒计时（面板打开期间逐秒刷新，到点自动解锁）
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
            UIRoot.ToastStatic(L10n.Get("报价已超时，继续！"));
            await RefreshData();
            Redraw();
        }

        private async void OnSkill(int skillType, int? slotIndex = null)
        {
            if (_busy) return;
            _busy = true;
            var r = await ApiClient.UseSkillAsync(_uid, skillType, slotIndex);
            _busy = false;
            if (r == null || !r["ok"].AsBool)
            {
                UIRoot.ToastStatic(L10n.Get("技能失败：") + Screens.ErrText(r));
                return;
            }
            UIRoot.ToastStatic(r["data"]["message"].Value);
            await RefreshData();
            Redraw();
        }

        /// <summary>技能面板：全部技能（来自 TbCharacter 配置）+ 描述 + 剩余额度</summary>
        private void OpenSkillPanel()
        {
            if (_chest == null || _chest["skillUsesRemaining"].AsInt <= 0 || _chest["skillUsedByMe"].AsBool)
            {
                UIRoot.ToastStatic(_chest != null && _chest["skillUsedByMe"].AsBool ? L10n.Get("你对本宝箱已用过技能") : L10n.Get("技能额度已耗尽"));
                return;
            }
            int quotaMax = GameConfig.Tables.TbGlobal.ChestMaxSkillUses;

            var panel = UITheme.CreatePanel(UIRoot.Instance.CanvasTransform, "panel_gold", Vector2.zero, new Vector2(900, 880));
            var rt = panel.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);

            var title = TDUIHelper.MakeText(panel.transform, new Vector2(0, -26), TextAnchor.MiddleCenter, 32);
            title.text = "选择技能（额度 " + _chest["skillUsesRemaining"].AsInt + "/" + quotaMax + " · 我限用一次 · 用后进入冷却）";
            TDUIHelper.SetSize(title.rectTransform, 860, 50);

            float y = -110;
            _cooldownRows.Clear();
            foreach (var c in GameConfig.Tables.TbCharacter.DataList)
            {
                var skill = c;
                string cdTag = skill.SkillCooldownSec > 0 ? " · CD " + skill.SkillCooldownSec + "s" : "";
                string baseText = "<color=#" + c.ColorHex + ">" + c.SkillName + "</color> · " + c.SkillDesc + cdTag;
                var row = UITheme.ThemedButton(panel.transform, new Vector2(0, y), new Vector2(840, 62), baseText, "gold", 20);
                UITheme.SkinButton(row, "blue");
                var label = row.GetComponentInChildren<Text>();
                label.alignment = TextAnchor.MiddleLeft;
                label.fontSize = 19;
                int remain = OnlineState.SkillCooldownRemain(skill.SkillType);
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
                    Object.Destroy(panel);
                    if (skill.SkillType == 4)
                    {
                        _peekTargeting = true;
                        UIRoot.ToastStatic(L10n.Get("窥视：点击一个未排除的格子查看金额"));
                    }
                    else
                    {
                        OnSkill(skill.SkillType);
                    }
                });
                y -= 72;
            }

            var closeBtn = UITheme.ThemedButton(panel.transform, new Vector2(0, y + 10), new Vector2(240, 56), L10n.Get("取 消"), "red", 24);
            closeBtn.onClick.AddListener(() => Object.Destroy(panel));
        }

        private async void OnSell()
        {
            if (_busy || _chest == null) return;
            _busy = true;
            var r = await ApiClient.SellAsync(_uid);
            _busy = false;
            if (r == null || !r["ok"].AsBool)
            {
                UIRoot.ToastStatic(L10n.Get("出售失败：") + Screens.ErrText(r));
                return;
            }
            long income = r["data"]["income"].AsLong;
            UIRoot.ToastStatic(r["data"]["final"].AsBool
                ? "开箱获得 " + income.ToString("N0") + " 金币！"
                : "已出售给系统，获得 " + income.ToString("N0"));
            UIRoot.Instance.RefreshTop();
            Screens.ShowBag();
        }
    }
}
