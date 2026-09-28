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
            float spacing = 470f;
            float startX = -(chests.Count - 1) * spacing / 2f;
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

                var card = UITheme.CreatePanel(list, "panel_dark", new Vector2(startX + i * spacing, -20), new Vector2(440, 560));
                var icoGo = new GameObject("chest_icon").AddComponent<Image>();
                icoGo.transform.SetParent(card.transform, false);
                var icon = UITheme.Load("chest_" + (c.Id == 1 ? "wood" : c.Id == 2 ? "silver" : "gold"));
                if (icon != null) icoGo.sprite = icon;
                var irt = icoGo.rectTransform;
                irt.anchorMin = irt.anchorMax = irt.pivot = new Vector2(0.5f, 1f);
                irt.anchoredPosition = new Vector2(0, -14);
                irt.sizeDelta = new Vector2(140, 140);

                var nameText = TDUIHelper.MakeText(card.transform, new Vector2(0, -160), TextAnchor.MiddleCenter, 30);
                nameText.text = "<color=#" + c.ColorHex + ">" + c.Name + "</color>";
                TDUIHelper.SetSize(nameText.rectTransform, 420, 46);
                var desc = TDUIHelper.MakeText(card.transform, new Vector2(0, -250), TextAnchor.UpperCenter, 22);
                desc.color = new Color(0.78f, 0.82f, 0.86f);
                desc.text = "期望 " + ev + " · 最大 " + max.ToString("N0") + kindNote + "\n20 格金额池";
                TDUIHelper.SetSize(desc.rectTransform, 410, 160);

                var buy = UITheme.ThemedButton(card.transform, new Vector2(0, 200), new Vector2(280, 76), "购买 " + c.Price.ToString("N0"), "green", 28);
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
        }

        // ---------------- 每日挑战 ----------------
    }
}
