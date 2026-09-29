using System.Linq;
using System.Threading.Tasks;
using cfg;
using Luban.SimpleJSON;
using UnityEngine;
using UnityEngine.UI;

namespace Game.HotUpdate.ChestGame
{
    /// <summary>Screens 模块拆分（partial）：Shop</summary>
    public static partial class Screens
    {


        public static async void ShowShop()
        {
            UITheme.PreloadAtlas("shop");
            var root = await UIManager.ShowAsync("ShopScreen");
            if (root == null) return;

            var back = UIManager.Find<Button>(root.transform, "btn_back");
            back.onClick.RemoveAllListeners();
            back.onClick.AddListener(() => ShowMain(true));

            var list = root.transform.Find("list");
            foreach (Transform child in list) Object.Destroy(child.gameObject);

            var chests = GameConfig.Tables.TbChest.DataList;
            for (int i = 0; i < chests.Count; i++)
            {
                var c = chests[i];
                var config = c;
                long ev = 0; int max = 0;
                foreach (var a in c.SlotAmounts) { ev += a; if (a > max) max = a; }
                ev /= c.SlotAmounts.Count;

                string kindNote = "";
                if (c.Kind == 1) kindNote = "\n双头奖箱";
                else if (c.Kind == 2) kindNote = "\n<color=#7ED957>幸运格排除 +" + c.SpecialValue + "</color>";
                else if (c.Kind == 3) kindNote = "\n<color=#E05D5D>陷阱格排除 -" + c.SpecialValue + "</color>";

                // 竖版：2 列 × 3 行
                int col = i % 2, row = i / 2;
                var card = UITheme.CreatePanel(list, "panel_dark", new Vector2(col * 480 - 240, 560 - row * 480), new Vector2(440, 430));
                var icoGo = new GameObject("chest_icon").AddComponent<Image>();
                icoGo.transform.SetParent(card.transform, false);
                var icon = UITheme.Load("chest_" + (c.Id == 1 ? "wood" : c.Id == 2 ? "silver" : "gold"));
                if (icon != null) icoGo.sprite = icon;
                var irt = icoGo.rectTransform;
                irt.anchorMin = irt.anchorMax = irt.pivot = new Vector2(0.5f, 1f);
                irt.anchoredPosition = new Vector2(0, -14);
                irt.sizeDelta = new Vector2(140, 140);

                var nameText = TDUIHelper.MakeText(card.transform, new Vector2(0, -140), TextAnchor.MiddleCenter, 28);
                nameText.text = "<color=#" + c.ColorHex + ">" + c.Name + "</color>";
                TDUIHelper.SetSize(nameText.rectTransform, 420, 46);
                var desc = TDUIHelper.MakeText(card.transform, new Vector2(0, -200), TextAnchor.UpperCenter, 20);
                desc.color = new Color(0.78f, 0.82f, 0.86f);
                desc.text = "期望 " + ev + " · 最大 " + max.ToString("N0") + kindNote + "\n20 格金额池";
                TDUIHelper.SetSize(desc.rectTransform, 410, 130);

                var buy = UITheme.ThemedButton(card.transform, new Vector2(0, 165), new Vector2(280, 72), "购买 " + c.Price.ToString("N0"), "green", 26);
                int minLv = MinLevelForChest(config.Id);
                if (OnlineState.Level < minLv)
                {
                    buy.interactable = false;
                    buy.GetComponentInChildren<Text>().text = "<color=#A8B4C2>Lv." + minLv + L10n.Get(" 解锁") + "</color>";
                }
                buy.onClick.AddListener(async () =>
                {
                    buy.interactable = false;
                    var r = await ApiClient.BuyChestAsync(config.Id);
                    buy.interactable = true;
                    if (r != null && r["ok"].AsBool)
                    {
                        UIRoot.ToastStatic("已购买 " + config.Name + L10n.Get("，到背包查看"));
                        UIRoot.Instance.RefreshTop();
                    }
                    else
                    {
                        UIRoot.ToastStatic(L10n.Get("购买失败：") + ErrText(r));
                    }
                });
            }

            BuildItemCards(list);
        }

        /// <summary>道具区（金币消耗品：冷却重置/补签/挑战券/改名）</summary>
        private static void BuildItemCards(Transform list)
        {
            if (!OnlineState.FeatureOn("itemshop")) return;
            var items = GameConfig.Tables.TbItem.DataList;
            if (items.Count == 0) return;
            float spacing = 250f;
            float startX = -(items.Count - 1) * spacing / 2f;
            string[] qualityHex = { "C8CDD6", "7ED957", "6EC6FF", "9B8EC4", "F5D061" };
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                var item = it;
                var card = UITheme.CreatePanel(list, "panel_dark", new Vector2(startX + i * spacing, -920), new Vector2(230, 300));
                var nameText = TDUIHelper.MakeText(card.transform, new Vector2(0, 22), TextAnchor.MiddleCenter, 22);
                nameText.text = "<color=#" + qualityHex[Mathf.Clamp((int)item.Quality, 0, 4)] + ">" + item.Name + "</color>";
                TDUIHelper.SetSize(nameText.rectTransform, 280, 40);
                var desc = TDUIHelper.MakeText(card.transform, new Vector2(0, -44), TextAnchor.UpperCenter, 17);
                desc.color = new Color(0.7f, 0.74f, 0.8f);
                desc.text = item.Desc;
                TDUIHelper.SetSize(desc.rectTransform, 210, 130);

                var buy = UITheme.ThemedButton(card.transform, new Vector2(0, 105), new Vector2(190, 56), item.Price.ToString("N0") + L10n.Get(" 金币"), "green", 20);
                buy.onClick.AddListener(async () =>
                {
                    if (item.Kind == "rename")
                    {
                        PromptRename(item);
                        return;
                    }
                    buy.interactable = false;
                    var r = await ApiClient.BuyItemAsync(item.Id);
                    buy.interactable = true;
                    if (r != null && r["ok"].AsBool)
                    {
                        UIRoot.ToastStatic(r["data"]["message"].Value);
                        UIRoot.Instance.RefreshTop();
                    }
                    else
                    {
                        UIRoot.ToastStatic(L10n.Get("购买失败：") + Screens.ErrText(r));
                    }
                });
            }
        }

        /// <summary>改名卡输入弹窗</summary>
        private static void PromptRename(cfg.item.Item item)
        {
            var panel = UITheme.CreatePanel(UIRoot.Instance.CanvasTransform, "panel_gold", Vector2.zero, new Vector2(560, 380));
            var rt = panel.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            var title = TDUIHelper.MakeText(panel.transform, new Vector2(0, -130), TextAnchor.MiddleCenter, 28);
            title.text = "<color=#F5D061>" + L10n.Get("输入新名字（16 字以内）") + "</color>";
            TDUIHelper.SetSize(title.rectTransform, 520, 44);
            var input = TDUIHelper.MakeInputField(panel.transform, new Vector2(0, -30), new Vector2(420, 60), ApiClient.PlayerName);
            var ok = UITheme.ThemedButton(panel.transform, new Vector2(0, -170), new Vector2(220, 60), item.Price.ToString("N0") + L10n.Get(" 金币改名"), "green", 24);
            ok.onClick.AddListener(async () =>
            {
                var name = input.text.Trim();
                if (name.Length < 1) { UIRoot.ToastStatic(L10n.Get("名字不能为空")); return; }
                var r = await ApiClient.BuyItemAsync(item.Id, name);
                if (r == null || !r["ok"].AsBool)
                {
                    UIRoot.ToastStatic(L10n.Get("购买失败：") + Screens.ErrText(r));
                    return;
                }
                ApiClient.PlayerName = name;
                UIRoot.ToastStatic(r["data"]["message"].Value);
                Object.Destroy(panel);
            });
            var cancel = UITheme.ThemedButton(panel.transform, new Vector2(190, -300), new Vector2(140, 48), L10n.Get("取 消"), "red", 22);
            cancel.onClick.AddListener(() => Object.Destroy(panel));
        }

        // ---------------- 每日挑战 ----------------
    }
}
