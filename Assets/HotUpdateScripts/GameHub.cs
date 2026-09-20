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
            UIRoot.Mount(_root.transform);
            UITheme.ApplyCameraTheme();

            if (ApiClient.UseServer && !ApiClient.LoggedIn)
            {
                Screens.ShowLogin();
            }
            else
            {
                Screens.ShowMain();
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
