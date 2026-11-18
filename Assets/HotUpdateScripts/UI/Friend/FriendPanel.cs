using System.Threading.Tasks;
using Luban.SimpleJSON;
using UnityEngine;
using UnityEngine.UI;

namespace Game.HotUpdate.ChestGame
{
    /// <summary>
    /// 好友面板（纯代码模态弹窗，照 ShowSign 重建刷新模式）：
    /// 页签 好友/申请；好友行可邀请对战（创建私人房+推送邀请）/移除；
    /// 底部：加好友 / 创建好友房（对决/竞宝/抢宝）/ 输房间码加入。
    /// 列表暂无滚动容器（项目无 ScrollRect 先例），超 12 位截断显示（早期够用，量大后升级）。
    /// </summary>
    public static class FriendPanel
    {
        private const int MaxRows = 12;

        public static async void Show(string tab = "friends")
        {
            if (!OnlineState.FeatureOn("friend"))
            {
                UIRoot.ToastStatic(L10n.Get("该玩法已关闭"));
                return;
            }
            var r = await ApiClient.FriendListAsync();
            if (r == null || !r["ok"].AsBool)
            {
                UIRoot.ToastStatic(L10n.Get("获取好友失败：") + Screens.ErrText(r));
                return;
            }
            var data = r["data"];

            var panel = UITheme.CreatePanel(UIRoot.Instance.CanvasTransform, "panel_gold", Vector2.zero, new Vector2(800, 1180));
            var rt = panel.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);

            var title = TDUIHelper.MakeText(panel.transform, new Vector2(0, -26), TextAnchor.MiddleCenter, 32);
            title.text = "<color=#F5D061>" + L10n.Get("好 友") + "</color>";
            TDUIHelper.SetSize(title.rectTransform, 760, 50);

            var codeText = TDUIHelper.MakeText(panel.transform, new Vector2(-100, -92), TextAnchor.MiddleCenter, 26);
            codeText.text = L10n.Get("我的好友码") + " <color=#F5D061>" + data["myCode"].AsLong + "</color>";
            TDUIHelper.SetSize(codeText.rectTransform, 500, 44);
            var copy = UITheme.ThemedButton(panel.transform, new Vector2(250, -92), new Vector2(160, 48), L10n.Get("复制"), "blue", 20);
            copy.onClick.AddListener(() =>
            {
                GUIUtility.systemCopyBuffer = L10n.Get("来和我一起玩宝箱商人！加我好友码：") + data["myCode"].AsLong;
                UIRoot.ToastStatic(L10n.Get("已复制（小游戏端请截图分享房码）"));
            });

            // 页签
            var friends = data["friends"];
            var requests = data["requests"];
            var tabFriend = UITheme.ThemedButton(panel.transform, new Vector2(-180, -158), new Vector2(320, 56),
                L10n.Get("好友") + " (" + friends.Count + ")", tab == "friends" ? "gold" : "blue", 24);
            var tabReq = UITheme.ThemedButton(panel.transform, new Vector2(180, -158), new Vector2(320, 56),
                L10n.Get("申请") + " (" + requests.Count + ")", tab == "requests" ? "gold" : "blue", 24);
            string curTab = tab;
            tabFriend.onClick.AddListener(() => { if (curTab != "friends") { Object.Destroy(panel); Show("friends"); } });
            tabReq.onClick.AddListener(() => { if (curTab != "requests") { Object.Destroy(panel); Show("requests"); } });

            float y = -250;
            if (tab == "requests")
            {
                if (requests.Count == 0)
                {
                    var empty = TDUIHelper.MakeText(panel.transform, new Vector2(0, y), TextAnchor.MiddleCenter, 24);
                    empty.color = UITheme.TextSub;
                    empty.text = L10n.Get("暂无好友申请");
                    TDUIHelper.SetSize(empty.rectTransform, 700, 50);
                    y -= 70;
                }
                for (int i = 0; i < requests.Count && i < MaxRows; i++)
                {
                    var req = requests[i];
                    var row = UITheme.CreatePanel(panel.transform, "panel_dark", new Vector2(0, y), new Vector2(720, 76));
                    var name = TDUIHelper.MakeText(row.transform, new Vector2(-220, 0), TextAnchor.MiddleLeft, 26);
                    name.text = req["name"].Value;
                    TDUIHelper.SetSize(name.rectTransform, 380, 44);
                    var ok = UITheme.ThemedButton(row.transform, new Vector2(50, 0), new Vector2(150, 52), L10n.Get("同 意"), "green", 22);
                    ok.onClick.AddListener(async () =>
                    {
                        var rr = await ApiClient.FriendAcceptAsync(req["id"].AsLong);
                        if (rr == null || !rr["ok"].AsBool || panel == null) { if (panel != null) UIRoot.ToastStatic(L10n.Get("操作失败：") + Screens.ErrText(rr)); return; }
                        UIRoot.ToastStatic(L10n.Get("已添加好友"));
                        Object.Destroy(panel);
                        Show("friends");
                    });
                    var no = UITheme.ThemedButton(row.transform, new Vector2(230, 0), new Vector2(150, 52), L10n.Get("拒 绝"), "red", 22);
                    no.onClick.AddListener(async () =>
                    {
                        var rr = await ApiClient.FriendRejectAsync(req["id"].AsLong);
                        if (rr == null || !rr["ok"].AsBool || panel == null) { if (panel != null) UIRoot.ToastStatic(L10n.Get("操作失败：") + Screens.ErrText(rr)); return; }
                        Object.Destroy(panel);
                        Show("requests");
                    });
                    y -= 88;
                }
                if (requests.Count > MaxRows)
                {
                    var more = TDUIHelper.MakeText(panel.transform, new Vector2(0, y), TextAnchor.MiddleCenter, 20);
                    more.color = UITheme.TextSub;
                    more.text = "… " + (requests.Count - MaxRows) + L10n.Get(" 条未显示");
                    y -= 40;
                }
            }
            else
            {
                if (friends.Count == 0)
                {
                    var empty = TDUIHelper.MakeText(panel.transform, new Vector2(0, y), TextAnchor.MiddleCenter, 24);
                    empty.color = UITheme.TextSub;
                    empty.text = L10n.Get("还没有好友——把你的好友码发给朋友吧");
                    TDUIHelper.SetSize(empty.rectTransform, 700, 50);
                    y -= 70;
                }
                for (int i = 0; i < friends.Count && i < MaxRows; i++)
                {
                    var f = friends[i];
                    var row = UITheme.CreatePanel(panel.transform, "panel_dark", new Vector2(0, y), new Vector2(720, 76));
                    var name = TDUIHelper.MakeText(row.transform, new Vector2(-20, 0), TextAnchor.MiddleLeft, 26);
                    name.text = "<color=#F5D061>" + f["name"].Value + "</color>  <color=#9AA3B2>Lv." + f["level"].AsInt + " · " + f["rating"].AsInt + "</color>";
                    TDUIHelper.SetSize(name.rectTransform, 420, 44);
                    var invite = UITheme.ThemedButton(row.transform, new Vector2(140, 0), new Vector2(190, 52), L10n.Get("邀请对战"), "green", 20);
                    invite.onClick.AddListener(() => _ = CreateDuelAndInviteAsync(panel, f["id"].AsLong, false));
                    var rm = UITheme.ThemedButton(row.transform, new Vector2(320, 0), new Vector2(120, 52), L10n.Get("移除"), "red", 20);
                    rm.onClick.AddListener(async () =>
                    {
                        var rr = await ApiClient.FriendRemoveAsync(f["id"].AsLong);
                        if (rr == null || !rr["ok"].AsBool || panel == null) { if (panel != null) UIRoot.ToastStatic(L10n.Get("操作失败：") + Screens.ErrText(rr)); return; }
                        Object.Destroy(panel);
                        Show("friends");
                    });
                    y -= 88;
                }
                if (friends.Count > MaxRows)
                {
                    var more = TDUIHelper.MakeText(panel.transform, new Vector2(0, y), TextAnchor.MiddleCenter, 20);
                    more.color = UITheme.TextSub;
                    more.text = "… " + (friends.Count - MaxRows) + L10n.Get(" 位未显示");
                    y -= 40;
                }
            }

            // ---- 好友房区（创建 / 凭码加入） ----
            y -= 16;
            var roomTitle = TDUIHelper.MakeText(panel.transform, new Vector2(0, y), TextAnchor.MiddleCenter, 24);
            roomTitle.color = UITheme.Blue;
            roomTitle.text = "—— " + L10n.Get("好友房（凭房间码加入，无机器人）") + " ——";
            TDUIHelper.SetSize(roomTitle.rectTransform, 740, 40);
            y -= 66;
            var createDuel = UITheme.ThemedButton(panel.transform, new Vector2(-250, y), new Vector2(220, 60), L10n.Get("开对决房"), "green", 22);
            createDuel.onClick.AddListener(() => _ = CreateDuelAndInviteAsync(panel, 0, false));
            var createPot = UITheme.ThemedButton(panel.transform, new Vector2(0, y), new Vector2(220, 60), L10n.Get("开竞宝房"), "gold", 22);
            createPot.onClick.AddListener(() => _ = CreateDuelAndInviteAsync(panel, 0, true));
            var createRush = UITheme.ThemedButton(panel.transform, new Vector2(250, y), new Vector2(220, 60), L10n.Get("开抢宝房"), "blue", 22);
            createRush.onClick.AddListener(async () =>
            {
                var rr = await ApiClient.CreatePrivateRushAsync(1); // 新手档（100 门票）
                if (rr == null || !rr["ok"].AsBool || panel == null) { if (panel != null) UIRoot.ToastStatic(L10n.Get("创建失败：") + Screens.ErrText(rr)); return; }
                string code = rr["data"]["roomCode"].Value;
                Object.Destroy(panel);
                RushPanel.ShowPrivate(code);
            });
            y -= 84;
            var input = TDUIHelper.MakeInputField(panel.transform, new Vector2(-130, y), new Vector2(340, 56), L10n.Get("房间码"));
            var joinDuel = UITheme.ThemedButton(panel.transform, new Vector2(80, y), new Vector2(180, 56), L10n.Get("入对决房"), "green", 22);
            joinDuel.onClick.AddListener(async () =>
            {
                var code = input.text.Trim();
                if (code.Length != 6) { UIRoot.ToastStatic(L10n.Get("请输入 6 位房间码")); return; }
                var rr = await ApiClient.JoinPrivateDuelAsync(code);
                if (rr == null || !rr["ok"].AsBool || panel == null) { if (panel != null) UIRoot.ToastStatic(L10n.Get("加入失败：") + Screens.ErrText(rr)); return; }
                Object.Destroy(panel);
                DuelPanel.ShowPrivate(code, false);
            });
            var joinRush = UITheme.ThemedButton(panel.transform, new Vector2(280, y), new Vector2(180, 56), L10n.Get("入抢宝房"), "blue", 22);
            joinRush.onClick.AddListener(async () =>
            {
                var code = input.text.Trim();
                if (code.Length != 6) { UIRoot.ToastStatic(L10n.Get("请输入 6 位房间码")); return; }
                var rr = await ApiClient.JoinPrivateRushAsync(code);
                if (rr == null || !rr["ok"].AsBool || panel == null) { if (panel != null) UIRoot.ToastStatic(L10n.Get("加入失败：") + Screens.ErrText(rr)); return; }
                Object.Destroy(panel);
                RushPanel.ShowPrivate(code);
            });
            y -= 90;
            var fInput = TDUIHelper.MakeInputField(panel.transform, new Vector2(-130, y), new Vector2(340, 56), L10n.Get("好友码"));
            var add = UITheme.ThemedButton(panel.transform, new Vector2(180, y), new Vector2(280, 56), L10n.Get("加 好 友"), "gold", 22);
            add.onClick.AddListener(async () =>
            {
                var rr = await ApiClient.FriendRequestAsync(fInput.text.Trim());
                if (rr == null || !rr["ok"].AsBool || panel == null) { if (panel != null) UIRoot.ToastStatic(L10n.Get("申请失败：") + Screens.ErrText(rr)); return; }
                UIRoot.ToastStatic(L10n.Get("已发送申请（若对方也申请你，将直接成为好友）"));
                Object.Destroy(panel);
                Show(curTab);
            });

            var close = UITheme.ThemedButton(panel.transform, new Vector2(0, y - 90), new Vector2(200, 52), L10n.Get("关 闭"), "red", 24);
            close.onClick.AddListener(() => Object.Destroy(panel));
        }

        /// <summary>创建好友对决房；inviteTo>0 时顺带向该好友推送邀请，然后进入房间等待</summary>
        private static async Task CreateDuelAndInviteAsync(GameObject panel, long inviteTo, bool potMode)
        {
            var r = await ApiClient.CreatePrivateDuelAsync(potMode);
            if (r == null || !r["ok"].AsBool || panel == null)
            {
                if (panel != null) UIRoot.ToastStatic(L10n.Get("创建失败：") + Screens.ErrText(r));
                return;
            }
            string code = r["data"]["roomCode"].Value;
            if (inviteTo > 0)
            {
                var ir = await ApiClient.FriendInviteAsync(inviteTo, code, potMode ? "pot" : "duel");
                if (ir != null && ir["ok"].AsBool) UIRoot.ToastStatic(L10n.Get("已推送邀请，等好友进来开打！"));
            }
            Object.Destroy(panel);
            DuelPanel.ShowPrivate(code, potMode);
        }

        /// <summary>房间内"邀请好友"选择器（DuelPanel 等待期调用）：点好友即推送当前房间邀请</summary>
        public static async void PickFriendToInvite(string roomCode, string mode)
        {
            var r = await ApiClient.FriendListAsync();
            if (r == null || !r["ok"].AsBool)
            {
                UIRoot.ToastStatic(L10n.Get("获取好友失败"));
                return;
            }
            var friends = r["data"]["friends"];
            if (friends.Count == 0)
            {
                UIRoot.ToastStatic(L10n.Get("还没有好友——把房间码 ") + roomCode + L10n.Get(" 发给朋友即可加入"));
                return;
            }
            var panel = UITheme.CreatePanel(UIRoot.Instance.CanvasTransform, "panel_gold", Vector2.zero, new Vector2(620, 700));
            var rt = panel.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            var title = TDUIHelper.MakeText(panel.transform, new Vector2(0, -26), TextAnchor.MiddleCenter, 30);
            title.text = "<color=#F5D061>" + L10n.Get("邀请好友进入房间") + " " + roomCode + "</color>";
            TDUIHelper.SetSize(title.rectTransform, 580, 50);
            float y = -110;
            for (int i = 0; i < friends.Count && i < 5; i++)
            {
                var f = friends[i];
                var row = UITheme.ThemedButton(panel.transform, new Vector2(0, y), new Vector2(520, 76),
                    "<color=#F5D061>" + f["name"].Value + "</color>  Lv." + f["level"].AsInt, "green", 24);
                row.onClick.AddListener(async () =>
                {
                    var ir = await ApiClient.FriendInviteAsync(f["id"].AsLong, roomCode, mode);
                    if (ir == null || !ir["ok"].AsBool) { UIRoot.ToastStatic(L10n.Get("邀请失败：") + Screens.ErrText(ir)); return; }
                    UIRoot.ToastStatic(L10n.Get("已邀请") + " " + f["name"].Value);
                    Object.Destroy(panel);
                });
                y -= 92;
            }
            var close = UITheme.ThemedButton(panel.transform, new Vector2(0, y), new Vector2(200, 52), L10n.Get("关 闭"), "red", 24);
            close.onClick.AddListener(() => Object.Destroy(panel));
        }
    }
}

namespace Game.HotUpdate.ChestGame
{
    /// <summary>
    /// 大厅好友收件箱轮询（挂在大厅 root 上随屏销毁）：
    /// 每 15s 拉轻量 /api/friend/inbox——申请数→好友按钮红点；房间邀请→模态弹窗（服务端读即消费）。
    /// 失败静默（网络抖动不打扰）；好友开关关闭时不轮询。
    /// </summary>
    public class FriendInboxPoller : MonoBehaviour
    {
        /// <summary>静默标志：已入房后不再弹邀请，Screens.ShowMain 复位（跨屏静态，随大厅重进解除）</summary>
        public static bool Muted;

        /// <summary>回到大厅时解除静默（Screens.ShowMain 调用）</summary>
        public static void Unmute() => Muted = false;

        private Button _btn;
        private Text _label;
        private string _baseText;
        private float _timer = 2f; // 进大厅 2s 后首拉
        private const float Interval = 15f;
        private bool _popupShowing;

        public void Bind(Button btn)
        {
            _btn = btn;
            _label = btn != null ? btn.GetComponentInChildren<Text>() : null;
            if (_label != null) _baseText = _label.text;
        }

        private void Update()
        {
            if (!OnlineState.FeatureOn("friend")) return;
            _timer += Time.unscaledDeltaTime;
            if (_timer < Interval) return;
            _timer = 0f;
            _ = PollAsync();
        }

        private async Task PollAsync()
        {
            var r = await ApiClient.FriendInboxAsync();
            if (this == null || r == null || !r["ok"].AsBool) return; // 失败静默
            var data = r["data"];
            if (_label != null)
            {
                int req = data["reqCount"].AsInt;
                string want = req > 0 ? _baseText + " <color=#E05D5D>●</color>" : _baseText;
                if (_label.text != want) _label.text = want;
            }
            var invite = data["invite"];
            if (invite != null && !invite.IsNull && !_popupShowing && !Muted && UIManager.CurrentName == "MainScreen")
            {
                ShowInvitePopup(invite);
            }
        }

        /// <summary>好友房间邀请弹窗：XX 邀请你加入好友房 123456 [加入][忽略]</summary>
        private void ShowInvitePopup(JSONNode invite)
        {
            _popupShowing = true;
            string code = invite["code"].Value;
            string mode = invite["mode"].Value;
            string from = invite["fromName"].Value;
            bool isRush = mode == "rush";

            var panel = UITheme.CreatePanel(UIRoot.Instance.CanvasTransform, "panel_gold", Vector2.zero, new Vector2(640, 420));
            var rt = panel.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            var title = TDUIHelper.MakeText(panel.transform, new Vector2(0, -40), TextAnchor.MiddleCenter, 30);
            title.text = "<color=#F5D061>" + L10n.Get("好友邀请") + "</color>";
            var desc = TDUIHelper.MakeText(panel.transform, new Vector2(0, -130), TextAnchor.MiddleCenter, 26);
            desc.text = "<color=#F5D061>" + from + "</color> " + L10n.Get("邀请你加入好友")
                + (isRush ? L10n.Get("抢宝房") : (mode == "pot" ? L10n.Get("竞宝房") : L10n.Get("对决房")))
                + "\n" + L10n.Get("房间码") + " <color=#F5D061>" + code + "</color>";
            TDUIHelper.SetSize(desc.rectTransform, 580, 90);

            var join = UITheme.ThemedButton(panel.transform, new Vector2(-130, -300), new Vector2(220, 64), L10n.Get("加 入"), "green", 26);
            join.onClick.AddListener(async () =>
            {
                join.interactable = false;
                if (isRush)
                {
                    var r = await ApiClient.JoinPrivateRushAsync(code);
                    if (r == null || !r["ok"].AsBool) { join.interactable = true; UIRoot.ToastStatic(L10n.Get("加入失败：") + Screens.ErrText(r)); return; }
                    Muted = true; // 已入房：对局中静默邀请弹窗，回大厅（ShowMain→Unmute）再恢复
                    Object.Destroy(panel);
                    RushPanel.ShowPrivate(code);
                }
                else
                {
                    var r = await ApiClient.JoinPrivateDuelAsync(code);
                    if (r == null || !r["ok"].AsBool) { join.interactable = true; UIRoot.ToastStatic(L10n.Get("加入失败：") + Screens.ErrText(r)); return; }
                    Muted = true;
                    Object.Destroy(panel);
                    DuelPanel.ShowPrivate(code, mode == "pot");
                }
            });
            var ignore = UITheme.ThemedButton(panel.transform, new Vector2(130, -300), new Vector2(220, 64), L10n.Get("忽 略"), "red", 26);
            ignore.onClick.AddListener(() =>
            {
                _popupShowing = false;
                Object.Destroy(panel);
            });
        }
    }
}
