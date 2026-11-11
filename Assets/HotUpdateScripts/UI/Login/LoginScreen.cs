using System.Linq;
using System.Threading.Tasks;
using cfg;
using Luban.SimpleJSON;
using UnityEngine;
using UnityEngine.UI;

namespace Game.HotUpdate.ChestGame
{
    /// <summary>Screens 模块拆分（partial）：Login</summary>
    public static partial class Screens
    {


        public static async void ShowLogin()
        {
            var root = await UIManager.ShowAsync("LoginScreen");
            if (root == null) { UIRoot.ToastStatic(L10n.Get("LoginScreen 加载失败")); return; }

            var input = UIManager.Find<InputField>(root.transform, "input_name");
#if UNITY_WEBGL
            // 微信小游戏：WASM 无键盘事件，InputField 手动接 wx.showKeyboard
            WXKeyboard.Bind(input);
#endif
            var langBtn = UITheme.ThemedButton(root.transform, new Vector2(410, -50), new Vector2(170, 56),
                L10n.IsZh ? "EN" : L10n.Get("中文"), "blue", 24);
            langBtn.onClick.AddListener(L10n.ToggleLanguage);

            // 适龄提示（合规：登录页常驻展示）
            var age = TDUIHelper.MakeText(root.transform, Vector2.zero, TextAnchor.MiddleCenter, 20);
            var art = age.rectTransform;
            art.anchorMin = art.anchorMax = art.pivot = new Vector2(0.5f, 0f);
            art.anchoredPosition = new Vector2(0, 28);
            TDUIHelper.SetSize(art, 900, 32);
            age.color = UITheme.TextSub;
            age.text = L10n.Get("适龄提示：本游戏适合 8 周岁以上用户，请在监护人指导下适度游戏");

            // 用户协议与隐私政策（合规：登录页可查看摘要）
            var policy = TDUIHelper.MakeText(root.transform, Vector2.zero, TextAnchor.MiddleCenter, 20);
            var prt = policy.rectTransform;
            prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0f);
            prt.anchoredPosition = new Vector2(0, 6);
            TDUIHelper.SetSize(prt, 900, 30);
            policy.color = UITheme.Blue;
            policy.text = L10n.Get("《用户协议与隐私政策》");
            var policyBox = policy.gameObject.AddComponent<Button>();
            policyBox.targetGraphic = policy;
            policyBox.onClick.AddListener(ShowPolicy);

            var btn = UIManager.Find<Button>(root.transform, "btn_login");
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(async () =>
            {
                btn.interactable = false;
                ApiClient.PlayerName = string.IsNullOrEmpty(input.text.Trim()) ? L10n.Get("玩家") + Random.Range(100, 999) : input.text.Trim();
                // 客户端预校验（权威校验在服务端）：长度 + 字符白名单，防 JSON 注入与非法字符
                if (!NameValid(ApiClient.PlayerName))
                {
                    btn.interactable = true;
                    UIRoot.ToastStatic(L10n.Get("昵称不合法：1~16位中文/字母/数字"));
                    return;
                }
                if (await LoginBridge.LoginAsync())
                {
                    UIRoot.ToastStatic($"欢迎，{ApiClient.PlayerName}！");
                    ShowMain(true);
                }
                else
                {
                    btn.interactable = true;
                    UIRoot.ToastStatic(ApiClient.LastLoginError != null
                        ? Screens.ErrText(ApiClient.LastLoginError)
                        : L10n.Get("登录失败：服务器未启动？"));
                }
            });
        }

        // ---------------- 主大厅 ----------------

        /// <summary>用户协议与隐私政策摘要弹窗（完整版以小游戏平台备案公示为准）</summary>
        private static void ShowPolicy()
        {
            var panel = UITheme.CreatePanel(UIRoot.Instance.CanvasTransform, "panel_gold", Vector2.zero, new Vector2(760, 900));
            var rt = panel.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);

            var title = TDUIHelper.MakeText(panel.transform, new Vector2(0, -30), TextAnchor.MiddleCenter, 30);
            title.text = "<color=#F5D061>" + L10n.Get("用户协议与隐私政策（摘要）") + "</color>";
            TDUIHelper.SetSize(title.rectTransform, 720, 46);

            var body = TDUIHelper.MakeText(panel.transform, new Vector2(40, -100), TextAnchor.UpperLeft, 21);
            TDUIHelper.SetSize(body.rectTransform, 680, 680);
            body.color = UITheme.TextSub;
            body.text = L10n.Get(
                "1. 本游戏为休闲策略类小游戏，登录仅需昵称；昵称会展示给其他玩家，请勿包含个人敏感信息。\n\n" +
                "2. 游戏进度、金币与对局数据存储于游戏服务器，仅用于玩法结算与排行榜展示。\n\n" +
                "3. 我们不收集设备标识、通讯录、精确定位等个人信息；地区展示来自网络接入商归属地。\n\n" +
                "4. 未成年用户请在监护人指导下适度游戏。如需注销账号或删除数据，请联系客服（联系方式见小游戏主页）。\n\n" +
                "5. 完整版本以小游戏平台备案公示的《用户隐私保护指引》为准。");

            var close = UITheme.ThemedButton(panel.transform, new Vector2(0, -830), new Vector2(220, 54), L10n.Get("关 闭"), "red", 24);
            close.onClick.AddListener(() => Object.Destroy(panel));
        }

        /// <summary>昵称客户端预校验（与服务端 SensitiveFilter 规则一致：1~16 位中文/字母/数字/下划线）</summary>
        private static bool NameValid(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length < 1 || name.Length > 16) return false;
            foreach (var ch in name)
            {
                bool ok = char.IsLetter(ch) || char.IsDigit(ch) || ch == '_' || (ch >= 0x4E00 && ch <= 0x9FFF);
                if (!ok) return false;
            }
            return true;
        }
    }
}
