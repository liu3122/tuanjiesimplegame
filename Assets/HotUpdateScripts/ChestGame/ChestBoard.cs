using System.Linq;
using System.Threading.Tasks;
using Luban.SimpleJSON;
using UnityEngine;
using UnityEngine.UI;

namespace Game.HotUpdate.ChestGame
{
    /// <summary>
    /// 开宝箱界面：20 格面板（点击排除）、系统出售/终局开箱、
    /// 技能三选一（洞察/抬价/免排 —— 开箱时自选，额度与"每玩家一次"由服务端裁决）。
    /// </summary>
    public class ChestBoard : MonoBehaviour
    {
        private long _uid;
        private JSONNode _chest;
        private readonly GameObject[] _slots = new GameObject[20];
        private Text _info;
        private Button _sellButton;
        private Text _sellText;
        private readonly Button[] _skillButtons = new Button[3];
        private readonly Text[] _skillTexts = new Text[3];
        private Text _skillHint;
        private bool _busy;

        public static void Open(Transform root, long uid)
        {
            UIRoot.Instance.Show(t =>
            {
                var board = new GameObject("ChestBoard").AddComponent<ChestBoard>();
                board.transform.SetParent(t, false);
                board._uid = uid;
                board.Build(t);
            });
        }

        private async void Build(Transform root)
        {
            await RefreshData();
            if (_chest == null)
            {
                UIRoot.ToastStatic("宝箱不存在");
                Screens.BuildBag(UIRoot.Instance.CanvasTransform);
                return;
            }

            Screens.BuildHeader(root, "开 宝 箱");

            // 左侧 20 格
            const float cw = 150, ch = 110, gap = 12;
            for (int i = 0; i < 20; i++)
            {
                int idx = i;
                var cell = TDUIHelper.MakeButton(root, Vector2.zero, new Vector2(cw, ch), "?");
                int col = i % 5, row = i / 5;
                cell.GetComponent<RectTransform>().anchoredPosition = new Vector2(-500 + col * (cw + gap), 130 - row * (ch + gap));
                cell.onClick.AddListener(() => OnEliminate(idx));
                _slots[i] = cell.gameObject;
            }

            // 右侧信息 + 操作
            var panel = new GameObject("info");
            panel.transform.SetParent(root, false);
            var img = panel.AddComponent<Image>();
            img.color = new Color(0.12f, 0.15f, 0.22f, 0.95f);
            var rt = panel.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(480, -20);
            rt.sizeDelta = new Vector2(440, 620);

            _info = TDUIHelper.MakeText(panel.transform, new Vector2(0, -30), TextAnchor.UpperLeft, 24);
            TDUIHelper.SetSize(_info.rectTransform, 400, 240);

            _sellButton = TDUIHelper.MakeButton(panel.transform, new Vector2(0, -170), new Vector2(380, 80), "");
            _sellButton.onClick.AddListener(OnSell);
            _sellText = _sellButton.GetComponentInChildren<Text>();

            _skillHint = TDUIHelper.MakeText(panel.transform, new Vector2(0, -240), TextAnchor.MiddleCenter, 20);
            _skillHint.color = new Color(0.75f, 0.8f, 0.85f);
            TDUIHelper.SetSize(_skillHint.rectTransform, 400, 30);

            // 技能三选一（开箱时选择，来自 TbCharacter 配置）
            for (int i = 0; i < 3; i++)
            {
                int skillType = i + 1;
                var c = GameConfig.Tables.TbCharacter.DataList.FirstOrDefault(x => x.SkillType == skillType);
                if (c == null) continue;
                var btn = TDUIHelper.MakeButton(panel.transform, new Vector2(0, -290 - i * 84), new Vector2(380, 74), $"{c.SkillName} · {c.SkillDesc}");
                btn.GetComponentInChildren<Text>().fontSize = 20;
                btn.onClick.AddListener(() => OnSkill(skillType));
                _skillButtons[i] = btn;
                _skillTexts[i] = btn.GetComponentInChildren<Text>();
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
            var baseColor = TDUIHelper.ColorFromString(config.ColorHex) * 0.35f + new Color(0.1f, 0.12f, 0.16f);

            for (int i = 0; i < 20; i++)
            {
                var text = _slots[i].GetComponentInChildren<Text>();
                var img = _slots[i].GetComponent<Image>();
                bool eliminated = _chest["eliminated"][i].AsBool;
                JSONNode amount = _chest["revealedAmounts"][i];
                if (eliminated)
                {
                    text.text = amount.IsNull ? "?" : amount.AsInt.ToString("N0");
                    text.fontSize = 24;
                    text.color = new Color(0.55f, 0.58f, 0.62f);
                    img.color = new Color(0.1f, 0.12f, 0.15f, 0.6f);
                }
                else
                {
                    text.text = "?";
                    text.fontSize = 40;
                    text.color = Color.white;
                    img.color = baseColor;
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
            _skillHint.text = quota <= 0
                ? "技能额度已耗尽"
                : "技能额度 " + quota + "/3 · " + (iUsed ? "我已用过" : "我可选一个技能（限一次）");
            for (int i = 0; i < 3; i++)
            {
                if (_skillButtons[i] != null) _skillButtons[i].interactable = quota > 0 && !iUsed && remaining > 1 && !_busy;
            }
        }

        private async void OnEliminate(int index)
        {
            if (_busy || _chest == null) return;
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

        private async void OnSkill(int skillType)
        {
            if (_busy) return;
            _busy = true;
            var r = await ApiClient.UseSkillAsync(_uid, skillType);
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
            Screens.BuildBag(UIRoot.Instance.CanvasTransform);
        }
    }
}
