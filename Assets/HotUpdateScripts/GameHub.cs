using Game.HotUpdate.ChestGame;
using UnityEngine;

namespace Game.HotUpdate
{
    /// <summary>
    /// 热更入口路由：挂载 UIRoot 并进入登录屏。
    /// 全部界面（登录/大厅/商城/背包/开箱/拍卖/排行）走服务端（SimpleGameServer）。
    /// </summary>
    public static class GameHub
    {
        private static GameObject _root;

        public static void Show()
        {
            Hide();
            _root = new GameObject("GameHub");
            var ui = UIRoot.Mount(_root.transform);

            var cam = Camera.main;
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.13f, 0.15f, 0.2f);
            }

            if (ApiClient.UseServer && !ApiClient.LoggedIn)
            {
                ui.Show(Screens.BuildLogin);
            }
            else
            {
                ui.Show(Screens.BuildMain);
            }
        }

        public static void Hide()
        {
            if (_root != null)
            {
                Object.Destroy(_root);
                _root = null;
            }
        }
    }
}
