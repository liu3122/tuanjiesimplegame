using System.Threading.Tasks;
using Luban.SimpleJSON;
using UnityEngine;
using UnityEngine.UI;

namespace Game.HotUpdate.ChestGame
{
    /// <summary>常驻 UI 层：Canvas、顶部金币、Toast。界面 prefab 由 UIManager 挂到本层下。</summary>
    public class UIRoot : MonoBehaviour
    {
        public static UIRoot Instance { get; private set; }

        private Canvas _canvas;
        private Text _topGold;
        private Text _topName;
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

            var coinGo = new GameObject("coinIcon");
            coinGo.transform.SetParent(canvas.transform, false);
            var coinImg = coinGo.AddComponent<Image>();
            coinImg.sprite = UITheme.Load("icon_coin");
            coinImg.raycastTarget = false;
            var crt = coinImg.rectTransform;
            crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(0, 1);
            crt.anchoredPosition = new Vector2(24, -18);
            crt.sizeDelta = new Vector2(40, 40);

            root._topGold = TDUIHelper.MakeText(canvas.transform, new Vector2(74, -38), TextAnchor.UpperLeft, 30);
            TDUIHelper.SetSize(root._topGold.rectTransform, 400, 46);
            root._topGold.color = UITheme.GoldLight;

            root._topName = TDUIHelper.MakeText(canvas.transform, new Vector2(-24, -38), TextAnchor.UpperRight, 26);
            TDUIHelper.SetSize(root._topName.rectTransform, 500, 42);
            root._topName.color = new Color(0.7f, 0.75f, 0.85f);
            root._topName.text = ApiClient.UseServer && ApiClient.LoggedIn ? ApiClient.PlayerName : "";

            root._toast = TDUIHelper.MakeText(canvas.transform, new Vector2(0, -680), TextAnchor.MiddleCenter, 28);
            root._toast.color = new Color(1f, 0.85f, 0.3f);
            TDUIHelper.SetSize(root._toast.rectTransform, 1600, 50);
            return root;
        }

        public Transform CanvasTransform => _canvas.transform;

        public async void RefreshTop()
        {
            if (await OnlineState.Refresh())
            {
                _topGold.text = OnlineState.Gold.ToString("N0");
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
