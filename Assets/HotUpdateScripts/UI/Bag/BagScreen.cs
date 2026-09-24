using System.Linq;
using System.Threading.Tasks;
using cfg;
using Luban.SimpleJSON;
using UnityEngine;
using UnityEngine.UI;

namespace Game.HotUpdate.ChestGame
{
    /// <summary>Screens 模块拆分（partial）：Bag</summary>
    public static partial class Screens
    {


        public static async void ShowBag()
        {
            UITheme.PreloadAtlas("open");
            var root = await UIManager.ShowAsync("BagScreen");
            if (root == null) return;

            var back = UIManager.Find<Button>(root.transform, "btn_back");
            back.onClick.RemoveAllListeners();
            back.onClick.AddListener(() => ShowMain(true));

            var empty = root.transform.Find("empty_hint") != null ? root.transform.Find("empty_hint").gameObject : null;
            var list = root.transform.Find("list");
            if (empty != null) empty.SetActive(false);
            foreach (Transform child in list) Object.Destroy(child.gameObject);

            var chests = OnlineState.Chests;
            if (chests == null || chests.Count == 0)
            {
                if (empty != null) empty.SetActive(true);
                return;
            }

            float y = 340;
            foreach (JSONNode node in chests)
            {
                var chest = node;
                var config = GameConfig.Tables.TbChest.DataList.FirstOrDefault(x => x.Id == chest["configId"].AsInt);
                if (config == null) continue;
                BuildBagRow(list, y, chest, config);
                y -= 112;
            }
        }

        private static void BuildBagRow(Transform list, float y, JSONNode chest, cfg.chest.Chest config)
        {
            bool listed = chest["state"].AsInt == 1;
            long ev = (long)chest["expectedValue"].AsDouble;
            string skillNote = listed ? "" : (chest["skillUsedByMe"].AsBool ? L10n.Get("（我已用）") : "");
            string info = "<color=#" + config.ColorHex + ">" + config.Name + "</color>   "
                + (listed ? "[拍卖中]  " : "") + "剩 " + chest["remainingCount"].AsInt + " 格 · EV " + ev.ToString("N0")
                + " · 技能 " + chest["skillUsesRemaining"].AsInt + "/3" + skillNote;

            var row = UITheme.CreatePanel(list, "panel_dark", new Vector2(0, y), new Vector2(1500, 96));
            var infoText = TDUIHelper.MakeText(row.transform, new Vector2(20, 0), TextAnchor.MiddleLeft, 24);
            infoText.text = info;
            var infoRt = infoText.rectTransform;
            infoRt.anchorMin = infoRt.anchorMax = infoRt.pivot = new Vector2(0, 0.5f);
            infoRt.anchoredPosition = new Vector2(20, 0);
            TDUIHelper.SetSize(infoRt, 980, 90);

            if (!listed)
            {
                long uid = chest["uid"].AsLong;
                var openBtn = UITheme.ThemedButton(row.transform, new Vector2(640, 0), new Vector2(200, 64), L10n.Get("开 宝 箱"), "gold", 24);
                openBtn.onClick.AddListener(() => ChestBoard.Show(uid));

                var listBtn = UITheme.ThemedButton(row.transform, new Vector2(870, 0), new Vector2(180, 64), L10n.Get("挂 拍"), "blue", 24);
                listBtn.onClick.AddListener(() => AuctionPanel.OpenListDialog(chest));
            }
        }

        // ---------------- 排行榜（四榜 × 全球/地区） ----------------

        private static string _board = "gold_total";
        private static string _scope = "global";
    }
}
