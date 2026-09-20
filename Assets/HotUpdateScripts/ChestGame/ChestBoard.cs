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

        public static async void Show(long uid)
        {
            UITheme.PreloadAtlas("open");
            var root = await UIManager.ShowAsync("ChestBoardScreen");
            if (root == null) { UIRoot.ToastStatic("界面加载失败"); return; }

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
                UIRoot.ToastStatic("宝箱不存在");
                Screens.ShowBag();
                return;
            }
            Redraw();
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
                        text.text = "?";
                        text.fontSize = 40;
                        text.color = Color.white;
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
                _sellText.text = "开 箱（" + final.ToString("N0") + "）";
            }
            else
            {
                _info.text = "<b>" + config.Name + "</b>\n剩余 " + remaining + " 格\n\n剩余总和 " + sum.ToString("N0")
                    + "\n期望 V " + ev.ToString("F1") + "\n\n下次排除 " + (cost == 0 ? "免费" : cost + " 金币") + buff;
                _sellText.text = "出售给系统 " + offer.ToString("N0");
            }

            int quota = _chest["skillUsesRemaining"].AsInt;
            bool iUsed = _chest["skillUsedByMe"].AsBool;
            int quotaMax = GameConfig.Tables.TbGlobal.ChestMaxSkillUses;
            _skillHint.text = quota <= 0
                ? "技能额度已耗尽"
                : "技能额度 " + quota + "/" + quotaMax + " · " + (iUsed ? "我已用过" : "我可任选一个技能（限一次）");
            if (_skillsButton != null) _skillsButton.interactable = quota > 0 && !iUsed && remaining > 1 && !_busy;
        }

        private async void OnEliminate(int index)
        {
            if (_busy || _chest == null) return;
            if (_peekTargeting)
            {
                _peekTargeting = false;
                if (_chest["eliminated"][index].AsBool) { UIRoot.ToastStatic("该格已排除，请选未排除的格"); return; }
                OnSkill(4, index);
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
            UIRoot.ToastStatic("排除揭示：" + r["data"]["revealedAmount"].AsInt.ToString("N0"));
            UIRoot.Instance.RefreshTop();
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
                UIRoot.ToastStatic("技能失败：" + Screens.ErrText(r));
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
                UIRoot.ToastStatic(_chest != null && _chest["skillUsedByMe"].AsBool ? "你对本宝箱已用过技能" : "技能额度已耗尽");
                return;
            }
            int quotaMax = GameConfig.Tables.TbGlobal.ChestMaxSkillUses;

            var panel = UITheme.CreatePanel(UIRoot.Instance.CanvasTransform, "panel_gold", Vector2.zero, new Vector2(900, 880));
            var rt = panel.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);

            var title = TDUIHelper.MakeText(panel.transform, new Vector2(0, -26), TextAnchor.MiddleCenter, 32);
            title.text = "选择技能（额度 " + _chest["skillUsesRemaining"].AsInt + "/" + quotaMax + " · 我限用一次）";
            TDUIHelper.SetSize(title.rectTransform, 860, 50);

            float y = -110;
            foreach (var c in GameConfig.Tables.TbCharacter.DataList)
            {
                var skill = c;
                var row = UITheme.ThemedButton(panel.transform, new Vector2(0, y), new Vector2(840, 62),
                    "<color=#" + c.ColorHex + ">" + c.SkillName + "</color> · " + c.SkillDesc, "gold", 20);
                UITheme.SkinButton(row, "blue");
                var label = row.GetComponentInChildren<Text>();
                label.alignment = TextAnchor.MiddleLeft;
                label.fontSize = 19;
                row.onClick.AddListener(() =>
                {
                    Object.Destroy(panel);
                    if (skill.SkillType == 4)
                    {
                        _peekTargeting = true;
                        UIRoot.ToastStatic("窥视：点击一个未排除的格子查看金额");
                    }
                    else
                    {
                        OnSkill(skill.SkillType);
                    }
                });
                y -= 72;
            }

            var closeBtn = UITheme.ThemedButton(panel.transform, new Vector2(0, y + 10), new Vector2(240, 56), "取 消", "red", 24);
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
                UIRoot.ToastStatic("出售失败：" + Screens.ErrText(r));
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
