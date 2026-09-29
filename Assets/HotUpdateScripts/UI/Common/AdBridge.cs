using System;

namespace Game.HotUpdate.ChestGame
{
    /// <summary>
    /// 广告 SDK 接入层（与 ShareBridge 同款接口模式）：
    /// 前期仅激励视频变现。默认 EditorAdProvider 开发模式直接发奖；
    /// 接穿山甲/优量汇/AdMob：实现 IAdProvider 并启动时 AdBridge.SetProvider(...) 即可，
    /// 正式 SDK 的服务端回调校验（S2S）对接到 POST /api/ad/reward 前再加签。
    /// </summary>
    public interface IAdProvider
    {
        /// <summary>播放激励视频；onRewarded = 完整观看（服务端发奖）</summary>
        void Show(string placement, string placementName, Action onRewarded, Action onSkipped = null);
    }

    public static class AdBridge
    {
        private static IAdProvider _provider = new EditorAdProvider();

        public static void SetProvider(IAdProvider provider) => _provider = provider;

        public static void Show(string placement, string placementName, Action onRewarded, Action onSkipped = null) =>
            _provider.Show(placement, placementName, onRewarded, onSkipped);
    }

    /// <summary>开发模式：无 SDK，延迟 1.5s 模拟观看后直接发奖</summary>
    public class EditorAdProvider : IAdProvider
    {
        public void Show(string placement, string placementName, Action onRewarded, Action onSkipped = null)
        {
            UIRoot.ToastStatic("[开发模式] 播放广告：" + placementName + "…");
            var go = new UnityEngine.GameObject("[EditorAdTimer]");
            var timer = go.AddComponent<UnityEngine.MonoBehaviour>();
            // 简易延时：用协程宿主（DontDestroyOnLoad 防场景切换丢失）
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<AdDelayTimer>().StartDelayed(1.5f, () =>
            {
                UnityEngine.Object.Destroy(go);
                onRewarded?.Invoke();
            });
        }

        private class AdDelayTimer : UnityEngine.MonoBehaviour
        {
            private float _left;
            private Action _done;
            public void StartDelayed(float sec, Action done) { _left = sec; _done = done; }
            private void Update()
            {
                _left -= UnityEngine.Time.deltaTime;
                if (_left <= 0) { _done?.Invoke(); _done = null; }
            }
        }
    }
}
