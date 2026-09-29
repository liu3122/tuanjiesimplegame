using System.Collections;
using Luban.SimpleJSON;
using UnityEngine;
using UnityEngine.UI;

namespace Game.HotUpdate.ChestGame
{
    /// <summary>
    /// 图片战报：全屏遮罩 + 居中战报卡（大数字高光），「保存战报图」截屏生成 PNG 走 ShareBridge 分享。
    /// 美术接入后：底图换分享模板 Sprite（见美术清单）；当前为程序配色版。
    /// </summary>
    public static class BattleReport
    {
        public static void Show(string title, string[] lines, string shareText, System.Action onClose = null)
        {
            var dim = UITheme.CreatePanel(UIRoot.Instance.CanvasTransform, "panel_dark", Vector2.zero, new Vector2(4000, 2400));
            dim.name = "report_dim";
            var dimRt = dim.GetComponent<RectTransform>();
            dimRt.anchorMin = dimRt.anchorMax = dimRt.pivot = new Vector2(0.5f, 0.5f);

            var card = UITheme.CreatePanel(dim.transform, "panel_gold", Vector2.zero, new Vector2(760, 900));
            var rt = card.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);

            var brand = TDUIHelper.MakeText(card.transform, new Vector2(0, -60), TextAnchor.MiddleCenter, 24);
            brand.color = UITheme.TextSub;
            brand.text = "宝箱商人";
            TDUIHelper.SetSize(brand.rectTransform, 700, 40);

            var titleText = TDUIHelper.MakeText(card.transform, new Vector2(0, -140), TextAnchor.MiddleCenter, 44);
            titleText.text = "<color=#F5D061>" + title + "</color>";
            TDUIHelper.SetSize(titleText.rectTransform, 700, 70);

            float y = -260;
            foreach (var line in lines)
            {
                var t = TDUIHelper.MakeText(card.transform, new Vector2(0, y), TextAnchor.MiddleCenter, 34);
                t.text = line;
                t.color = Color.white;
                TDUIHelper.SetSize(t.rectTransform, 700, 60);
                y -= 76;
            }

            var share = UITheme.ThemedButton(card.transform, new Vector2(0, -740), new Vector2(420, 76), "保存战报图 · 分享", "green", 28);
            share.onClick.AddListener(() =>
            {
                string path = Application.persistentDataPath + "/battle_report_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";
                ScreenCapture.CaptureScreenshot(path);
                UIRoot.ToastStatic("战报图生成中…");
                share.GetComponentInParent<MonoBehaviour>()?.StartCoroutine(DelayedShare(path, shareText));
            });
            var close = UITheme.ThemedButton(card.transform, new Vector2(0, -840), new Vector2(200, 44), "关 闭", "red", 20);
            close.onClick.AddListener(() =>
            {
                Object.Destroy(dim);
                onClose?.Invoke();
            });
        }

        private static IEnumerator DelayedShare(string path, string shareText)
        {
            yield return new WaitForSeconds(0.6f); // 等截屏落盘
            ShareBridgeImage(path, "宝箱商人战报", shareText);
        }

        private static void ShareBridgeImage(string path, string title, string text)
        {
            // 经由 ShareBridge 的图片分享（默认剪贴板实现；接 SDK 后自动走平台图片消息）
            GUIUtility.systemCopyBuffer = title + " " + text + " [图] " + path;
            UIRoot.ToastStatic("战报已保存：" + path);
        }

        /// <summary>开箱战报（卖出/终局开箱）</summary>
        public static void ShowChestSettle(long income, bool final, string chestName, System.Action onClose)
        {
            string title = final ? "终局开箱！" : "落袋为安";
            Show(title, new[]
            {
                chestName,
                "<color=#F5D061>+" + income.ToString("N0") + " 金币</color>",
                "排除博弈 · 卖或不卖由我",
            }, "我在宝箱商人" + (final ? "赌到最后一格开箱" : "精准落袋") + "，得了 " + income.ToString("N0") + " 金币！", onClose);
        }

        /// <summary>对决战报</summary>
        public static void ShowDuel(bool win, bool tie, long delta, long myScore, long oppScore, string oppName, bool potMode)
        {
            string title = tie ? "势均力敌" : win ? (potMode ? "赢家通吃！" : "对决胜利！") : "棋差一招";
            Show(title, new[]
            {
                (potMode ? "对赌房" : "镜像对决") + " · " + (tie ? "平局" : (win ? "WIN" : "LOSE")),
                "我 " + myScore.ToString("N0") + "  vs  " + oppScore.ToString("N0") + " " + oppName,
                "<color=" + (delta >= 0 ? "#4DD0A5" : "#E05D5D") + ">" + (delta >= 0 ? "+" : "") + delta.ToString("N0") + " 金币</color>",
            }, "我在宝箱商人" + (potMode ? "对赌房" : "镜像对决") + (tie ? "和对手战平" : (win ? "战胜了 " + oppName : "惜败给 " + oppName)) + "，来和我开同一个宝箱！");
        }
    }
}
