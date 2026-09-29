using System.Linq;
using System.Threading.Tasks;
using cfg;
using Luban.SimpleJSON;
using UnityEngine;
using UnityEngine.UI;

namespace Game.HotUpdate.ChestGame
{
    /// <summary>Screens 模块拆分（partial）：Lobby</summary>
    public static partial class Screens
    {


        public static async void ShowMain(bool unloadUnused = false)
        {
            UITheme.PreloadAtlas("main");
            var root = await UIManager.ShowAsync("MainScreen", unloadUnused);
            if (root == null) { UIRoot.ToastStatic(L10n.Get("MainScreen 加载失败")); return; }

            Bind(root, "card_shop", () => ShowShop());
            Bind(root, "card_bag", () => ShowBag());
            Bind(root, "card_auction", () => AuctionPanel.Show());
            Bind(root, "card_rank", () => ShowRank());
            Bind(root, "card_daily", () => ShowDaily());
            Bind(root, "card_rush", () => RushPanel.Show());
            var langBtn = UITheme.ThemedButton(root.transform, new Vector2(860, -50), new Vector2(170, 56),
                L10n.IsZh ? "EN" : L10n.Get("中文"), "blue", 24);
            langBtn.onClick.AddListener(L10n.ToggleLanguage);
            // 对决入口（模式选择器：镜像对决/对赌房；开关关闭时隐藏）
            var duelBtn = UITheme.ThemedButton(root.transform, new Vector2(660, -50), new Vector2(170, 56),
                L10n.Get("对 决"), "gold", 24);
            duelBtn.name = "btn_duel_entry";
            duelBtn.onClick.AddListener(() => DuelPanel.ShowChooser());
            // 福利中心（激励视频；开关关闭时隐藏）
            var adBtn = UITheme.ThemedButton(root.transform, new Vector2(460, -50), new Vector2(170, 56),
                L10n.Get("福 利"), "green", 24);
            adBtn.name = "btn_ad_entry";
            adBtn.onClick.AddListener(() => ShowAdCenter());
            // 每日任务（开关关闭时隐藏）
            var questBtn = UITheme.ThemedButton(root.transform, new Vector2(260, -50), new Vector2(170, 56),
                L10n.Get("任 务"), "blue", 24);
            questBtn.name = "btn_quest_entry";
            questBtn.onClick.AddListener(() => ShowQuests());
            UIRoot.Instance.RefreshTop();

            ApplyFeatureGates(root, duelBtn.gameObject, adBtn.gameObject, questBtn.gameObject);
            _ = RefreshFeatureGates(root, duelBtn.gameObject, adBtn.gameObject, questBtn.gameObject);
            _ = MaybeShowTutorial();
        }

        /// <summary>新玩家（0 次开箱且无宝箱）：弹出教学箱领取</summary>
        private static async Task MaybeShowTutorial()
        {
            if (OnlineState.State == null && !await OnlineState.Refresh()) return;
            var data = OnlineState.State["data"];
            if (data == null || data["opensTotal"].AsInt > 0) return;
            if (data["chests"] != null && data["chests"].Count > 0) return; // 已在玩（买过箱）

            var panel = UITheme.CreatePanel(UIRoot.Instance.CanvasTransform, "panel_gold", Vector2.zero, new Vector2(660, 420));
            var rt = panel.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            var title = TDUIHelper.MakeText(panel.transform, new Vector2(0, -140), TextAnchor.MiddleCenter, 34);
            title.text = "<color=#F5D061>" + L10n.Get("欢迎来到宝箱商人！") + "</color>";
            TDUIHelper.SetSize(title.rectTransform, 620, 60);
            var desc = TDUIHelper.MakeText(panel.transform, new Vector2(0, -230), TextAnchor.UpperLeft, 24);
            desc.color = UITheme.TextSub;
            desc.text = L10n.Get("排除小额提高期望、银行家随时出价、卖或不卖由你抉择。\n先领一个免费教学宝箱（固定内容），三步学会核心玩法。");
            TDUIHelper.SetSize(desc.rectTransform, 600, 140);

            var take = UITheme.ThemedButton(panel.transform, new Vector2(0, -340), new Vector2(360, 72), L10n.Get("领取教学宝箱"), "green", 28);
            take.onClick.AddListener(async () =>
            {
                take.interactable = false;
                var r = await ApiClient.TutorialChestAsync();
                if (r == null || !r["ok"].AsBool)
                {
                    take.interactable = true;
                    UIRoot.ToastStatic(L10n.Get("领取失败：") + Screens.ErrText(r));
                    return;
                }
                Object.Destroy(panel);
                ChestBoard.Show(r["data"]["chestUid"].AsLong);
            });
        }

        /// <summary>玩法开关：隐藏关闭的入口（大厅卡片 + 对决/福利按钮）</summary>
        private static void ApplyFeatureGates(GameObject root, GameObject duelBtn, GameObject adBtn = null, GameObject questBtn = null)
        {
            if (root == null) return;
            if (OnlineState.State == null) return; // 无快照时默认显示
            SetCardActive(root, "card_rush", OnlineState.FeatureOn("rush"));
            SetCardActive(root, "card_daily", OnlineState.FeatureOn("daily"));
            SetCardActive(root, "card_auction", OnlineState.FeatureOn("auction"));
            if (duelBtn != null) duelBtn.SetActive(OnlineState.FeatureOn("duel") || OnlineState.FeatureOn("potduel"));
            if (adBtn != null) adBtn.SetActive(OnlineState.FeatureOn("ad"));
            if (questBtn != null) questBtn.SetActive(OnlineState.FeatureOn("quest"));
        }

        private static async Task RefreshFeatureGates(GameObject root, GameObject duelBtn, GameObject adBtn = null, GameObject questBtn = null)
        {
            if (await OnlineState.Refresh() && root != null) ApplyFeatureGates(root, duelBtn, adBtn, questBtn);
        }

        /// <summary>每日任务面板：进度 / 领取 / 看广告双倍领取</summary>
        private static async void ShowQuests()
        {
            if (!OnlineState.FeatureOn("quest"))
            {
                UIRoot.ToastStatic(L10n.Get("该玩法已关闭"));
                return;
            }
            await OnlineState.Refresh();
            var panel = UITheme.CreatePanel(UIRoot.Instance.CanvasTransform, "panel_gold", Vector2.zero, new Vector2(880, 700));
            var rt = panel.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);

            var title = TDUIHelper.MakeText(panel.transform, new Vector2(0, -26), TextAnchor.MiddleCenter, 32);
            title.text = "<color=#F5D061>" + L10n.Get("每日任务") + "</color> <color=#9AA3B2>" + L10n.Get("（每日轮换，看完广告可双倍领取）") + "</color>";
            TDUIHelper.SetSize(title.rectTransform, 840, 50);

            var quests = OnlineState.State != null ? OnlineState.State["data"]["quests"] : null;
            float y = -110;
            if (quests != null && !quests.IsNull)
            {
                foreach (JSONNode q in quests)
                {
                    var quest = q;
                    bool done = quest["progress"].AsInt >= quest["target"].AsInt;
                    bool claimed = quest["claimed"].AsBool;
                    var row = UITheme.CreatePanel(panel.transform, "panel_dark", new Vector2(0, y), new Vector2(820, 130));
                    var info = TDUIHelper.MakeText(row.transform, new Vector2(-20, 0), TextAnchor.MiddleLeft, 24);
                    var irt = info.rectTransform;
                    irt.anchorMin = irt.pivot = new Vector2(0, 0.5f);
                    irt.anchoredPosition = new Vector2(-380, 24);
                    TDUIHelper.SetSize(irt, 560, 44);
                    info.text = "<color=#F5D061>" + quest["name"].Value + "</color>   <color=#9AA3B2>+" + quest["rewardGold"].AsInt + " 金币 +" + quest["rewardXp"].AsInt + " 经验</color>";
                    var prog = TDUIHelper.MakeText(row.transform, new Vector2(-380, -28), TextAnchor.MiddleLeft, 22);
                    var prt = prog.rectTransform;
                    prt.anchorMin = prt.pivot = new Vector2(0, 0.5f);
                    TDUIHelper.SetSize(prt, 560, 40);
                    prog.color = done ? UITheme.Green : UITheme.TextSub;
                    prog.text = quest["progress"].AsInt + " / " + quest["target"].AsInt + (done ? " ✓" : "");

                    var claim = UITheme.ThemedButton(row.transform, new Vector2(280, 0), new Vector2(220, 56),
                        claimed ? L10n.Get("已领取") : L10n.Get("领 取"), claimed ? "blue" : "green", 24);
                    claim.interactable = done && !claimed;
                    claim.onClick.AddListener(async () =>
                    {
                        var r = await ApiClient.ClaimQuestAsync(quest["id"].Value);
                        if (r == null || !r["ok"].AsBool)
                        {
                            UIRoot.ToastStatic(L10n.Get("领取失败：") + Screens.ErrText(r));
                            return;
                        }
                        UIRoot.ToastStatic(r["data"]["message"].Value);
                        UIRoot.Instance.RefreshTop();
                        Object.Destroy(panel);
                        ShowQuests();
                    });
                    if (done && !claimed && OnlineState.FeatureOn("ad"))
                    {
                        var dbl = UITheme.ThemedButton(row.transform, new Vector2(140, -34), new Vector2(120, 44),
                            "<color=#F5D061>×2 " + L10n.Get("广告") + "</color>", "gold", 18);
                        dbl.onClick.AddListener(() =>
                        {
                            AdBridge.Show("quest_double", L10n.Get("双倍任务奖励"), async () =>
                            {
                                var r = await ApiClient.ClaimQuestAsync(quest["id"].Value, true);
                                if (r == null || !r["ok"].AsBool)
                                {
                                    UIRoot.ToastStatic(L10n.Get("领取失败：") + Screens.ErrText(r));
                                    return;
                                }
                                UIRoot.ToastStatic(r["data"]["message"].Value);
                                UIRoot.Instance.RefreshTop();
                                Object.Destroy(panel);
                                ShowQuests();
                            });
                        });
                    }
                    y -= 150;
                }
            }

            var close = UITheme.ThemedButton(panel.transform, new Vector2(0, y + 30), new Vector2(220, 54), L10n.Get("关 闭"), "red", 24);
            close.onClick.AddListener(() => Object.Destroy(panel));
        }

        /// <summary>福利中心：激励视频广告位列表（配置 + 服务端频控状态）</summary>
        private static async void ShowAdCenter()
        {
            if (!OnlineState.FeatureOn("ad"))
            {
                UIRoot.ToastStatic(L10n.Get("该玩法已关闭"));
                return;
            }
            await OnlineState.Refresh();
            var panel = UITheme.CreatePanel(UIRoot.Instance.CanvasTransform, "panel_gold", Vector2.zero, new Vector2(820, 640));
            var rt = panel.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);

            var title = TDUIHelper.MakeText(panel.transform, new Vector2(0, -26), TextAnchor.MiddleCenter, 32);
            title.text = "<color=#F5D061>" + L10n.Get("福利中心") + "</color>";
            TDUIHelper.SetSize(title.rectTransform, 780, 50);

            float y = -110;
            foreach (var ad in GameConfig.Tables.TbAd.DataList)
            {
                var cfg = ad;
                var row = UITheme.ThemedButton(panel.transform, new Vector2(0, y), new Vector2(740, 88),
                    "<color=#4DD0A5>" + cfg.Name + "</color> · " + cfg.Desc, "gold", 22);
                UITheme.SkinButton(row, "green");
                var label = row.GetComponentInChildren<Text>();
                label.alignment = TextAnchor.MiddleLeft;
                label.fontSize = 20;
                var state = OnlineState.State != null ? OnlineState.State["data"]["ads"][cfg.Id] : null;
                int remain = state != null ? state["remainToday"].AsInt : 0;
                int cd = state != null ? state["cooldownSec"].AsInt : 0;
                if (remain <= 0 || cd > 0)
                {
                    row.interactable = false;
                    label.text = cfg.Name + " · " + (remain <= 0 ? L10n.Get("今日已达上限") : L10n.Get("冷却中") + " " + cd + "s");
                }
                row.onClick.AddListener(() =>
                {
                    Object.Destroy(panel);
                    AdBridge.Show(cfg.Id, cfg.Name, async () =>
                    {
                        var r = await ApiClient.AdRewardAsync(cfg.Id);
                        if (r == null || !r["ok"].AsBool)
                        {
                            UIRoot.ToastStatic(L10n.Get("奖励失败：") + Screens.ErrText(r));
                            return;
                        }
                        UIRoot.ToastStatic(L10n.Get("广告奖励：") + r["data"]["message"].Value);
                        UIRoot.Instance.RefreshTop();
                        await OnlineState.Refresh();
                    });
                });
                y -= 100;
            }

            var close = UITheme.ThemedButton(panel.transform, new Vector2(0, y + 20), new Vector2(220, 54), L10n.Get("关 闭"), "red", 24);
            close.onClick.AddListener(() => Object.Destroy(panel));
        }

        private static void SetCardActive(GameObject root, string node, bool on)
        {
            var t = root.transform.Find(node);
            if (t != null) t.gameObject.SetActive(on);
        }

        private static void Bind(GameObject root, string node, System.Action open)
        {
            var t = root.transform.Find(node);
            if (t == null) return;
            var btn = t.GetComponent<Button>();
            if (btn == null) return;
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() => open());
        }

        // ---------------- 商城 ----------------
    }
}
