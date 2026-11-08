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

            var btn = UIManager.Find<Button>(root.transform, "btn_login");
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(async () =>
            {
                btn.interactable = false;
                ApiClient.PlayerName = string.IsNullOrEmpty(input.text) ? L10n.Get("玩家") + Random.Range(100, 999) : input.text;
                if (await LoginBridge.LoginAsync())
                {
                    UIRoot.ToastStatic($"欢迎，{ApiClient.PlayerName}！");
                    ShowMain(true);
                }
                else
                {
                    btn.interactable = true;
                    UIRoot.ToastStatic(L10n.Get("登录失败：服务器未启动？"));
                }
            });
        }

        // ---------------- 主大厅 ----------------
    }
}
