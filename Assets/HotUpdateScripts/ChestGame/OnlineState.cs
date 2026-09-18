using System.Threading.Tasks;
using Luban.SimpleJSON;
using UnityEngine;
using UnityEngine.UI;

namespace Game.HotUpdate.ChestGame
{
    /// <summary>在线状态缓存（/api/state 的最新快照）与全屏导航</summary>
    public static class OnlineState
    {
        public static JSONNode State { get; private set; }
        public static long Gold => State != null && State["data"] != null ? State["data"]["gold"].AsLong : 0;
        public static JSONNode Chests => State != null && State["data"] != null ? State["data"]["chests"] : null;

        public static async Task<bool> Refresh()
        {
            var r = await ApiClient.GetStateAsync();
            if (r != null && r["ok"].AsBool)
            {
                State = r;
                return true;
            }
            return false;
        }
    }

    /// <summary>屏幕导航 + 顶部信息条 + Toast</summary>
    public class UIRoot : MonoBehaviour
    {
        public static UIRoot Instance { get; private set; }

        private Canvas _canvas;
        private Text _topGold;
        private Text _toast;
        private float _toastTimer;
        private GameObject _screen;

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public static UIRoot Mount(Transform parent)
        {
            if (Instance != null) return Instance;
            var go = new GameObject("UIRoot");
            go.transform.SetParent(parent, false);
            var root = go.AddComponent<UIRoot>();
            var canvas = TDUIHelper.CreateCanvas(go.transform);
            root._canvas = canvas;
            TDUIHelper.EnsureEventSystem();

            root._topGold = TDUIHelper.MakeText(canvas.transform, new Vector2(24, -16), TextAnchor.UpperLeft, 32);
            TDUIHelper.SetSize(root._topGold.rectTransform, 700, 50);

            root._toast = TDUIHelper.MakeText(canvas.transform, new Vector2(0, -680), TextAnchor.MiddleCenter, 28);
            root._toast.color = new Color(1f, 0.85f, 0.3f);
            TDUIHelper.SetSize(root._toast.rectTransform, 1600, 50);
            return root;
        }

        public Transform CanvasTransform => _canvas.transform;

        /// <summary>切换全屏（销毁旧屏，挂新屏）</summary>
        public void Show(System.Action<Transform> builder)
        {
            if (_screen != null) Destroy(_screen);
            _screen = new GameObject("screen");
            _screen.transform.SetParent(_canvas.transform, false);
            builder(_screen.transform);
            RefreshTop();
        }

        public async void RefreshTop()
        {
            if (await OnlineState.Refresh())
            {
                _topGold.text = $"金币 {OnlineState.Gold:N0}";
            }
        }

        public void Toast(string msg)
        {
            _toast.text = msg;
            _toastTimer = 3f;
        }

        public static void ToastStatic(string msg) => Instance?.Toast(msg);

        private void Update()
        {
            if (_toastTimer > 0)
            {
                _toastTimer -= Time.deltaTime;
                if (_toastTimer <= 0) _toast.text = "";
            }
        }
    }
}
