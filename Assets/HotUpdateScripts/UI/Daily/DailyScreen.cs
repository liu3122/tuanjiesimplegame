using System.Linq;
using System.Threading.Tasks;
using cfg;
using Luban.SimpleJSON;
using UnityEngine;
using UnityEngine.UI;

namespace Game.HotUpdate.ChestGame
{
    /// <summary>Screens 模块拆分（partial）：Daily</summary>
    public static partial class Screens
    {


        public static async void ShowDaily()
        {
            var root = await UIManager.ShowAsync("DailyScreen");
            if (root == null) return;

            var back = UIManager.Find<Button>(root.transform, "btn_back");
            back.onClick.RemoveAllListeners();
            back.onClick.AddListener(() => ShowMain());

            var cost = GameConfig.Tables.TbGlobal.DailyTicketCost;
            var info = UIManager.Find<Text>(root.transform, "info_panel/info");
            info.text = "全服同一个宝箱（结构完全相同）\n比谁最终卖得更高\n\n门票 " + cost + " 金币\n每天 " + GameConfig.Tables.TbGlobal.DailyAttemptsPerDay + " 次\n成绩 = 出售/开箱最终所得";

            var buy = UIManager.Find<Button>(root.transform, "info_panel/btn_buy");
            buy.onClick.RemoveAllListeners();
            buy.onClick.AddListener(async () =>
            {
                buy.interactable = false;
                var r = await ApiClient.BuyDailyAsync();
                buy.interactable = true;
                if (r == null || !r["ok"].AsBool)
                {
                    UIRoot.ToastStatic(L10n.Get("购买失败：") + ErrText(r));
                    return;
                }
                UIRoot.ToastStatic("挑战箱已放入背包（今日剩余 " + r["data"]["attemptsLeft"].AsInt + " 次）");
                UIRoot.Instance.RefreshTop();
                await OnlineState.Refresh();
                ChestBoard.Show(r["data"]["chestUid"].AsLong);
            });

            var r2 = await ApiClient.GetDailyRankAsync();
            if (r2 == null || !r2["ok"].AsBool) return;
            var list = root.transform.Find("rank_panel/list");
            foreach (Transform child in list) Object.Destroy(child.gameObject);
            var mine = r2["data"]["mine"];
            var header = UIManager.Find<Text>(root.transform, "rank_panel/my_best");
            header.text = (mine != null && !mine.IsNull ? L10n.Get("我的最好：") + mine.AsLong.ToString("N0") : L10n.Get("今天还没挑战"))
                + "（剩余 " + r2["data"]["attemptsLeft"].AsInt + " 次）";
            float y = 0;
            int rank = 1;
            foreach (JSONNode rowNode in r2["data"]["list"])
            {
                var line = TDUIHelper.MakeText(list, new Vector2(0, y), TextAnchor.MiddleLeft, 26);
                bool isMe = rowNode["isMe"].AsBool;
                line.text = "  第 " + rank + " 名    " + rowNode["name"] + "    " + rowNode["score"].AsLong.ToString("N0");
                line.color = isMe ? UITheme.GoldLight : (rank <= 3 ? UITheme.Gold : Color.white);
                var rt = line.rectTransform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
                TDUIHelper.SetSize(rt, 700, 46);
                y -= 56;
                rank++;
            }
        }

        // ---------------- 背包 ----------------
    }
}
