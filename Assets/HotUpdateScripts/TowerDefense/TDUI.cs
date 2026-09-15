using cfg;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.HotUpdate.TowerDefense
{
    /// <summary>塔防 UI：全部代码构建（UGUI），无 prefab 依赖</summary>
    public class TDUI : MonoBehaviour
    {
        private Text _statusText;
        private Text _tipText;
        private Button _waveButton;
        private Text _waveButtonText;
        private GameObject _resultPanel;
        private Text _resultText;
        private readonly System.Collections.Generic.List<Button> _towerButtons = new System.Collections.Generic.List<Button>();

        private float _tipTimer;

        private static Font BuiltInFont => Resources.GetBuiltinResource<Font>("Arial.ttf");

        private void Start()
        {
            EnsureEventSystem();
            BuildCanvas();
            RefreshAll();
        }

        private static void EnsureEventSystem()
        {
            if (Object.FindObjectOfType<EventSystem>() != null)
            {
                return;
            }
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }

        private void BuildCanvas()
        {
            var canvasGo = new GameObject("TDUICanvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            canvasGo.AddComponent<GraphicRaycaster>();

            // 左上：状态栏（金币/生命/波次/提示）
            _statusText = MakeText(canvasGo.transform, new Vector2(20, -20), TextAnchor.UpperLeft, 30);
            SetSize(_statusText.rectTransform, 800, 130);

            _tipText = MakeText(canvasGo.transform, new Vector2(0, -150), TextAnchor.MiddleCenter, 28);
            _tipText.color = new Color(1f, 0.85f, 0.3f);
            SetSize(_tipText.rectTransform, 1600, 50);

            // 底部：塔选择按钮（数据来自 TbTower —— 加新塔自动多一个按钮）
            float x = -420;
            foreach (var tower in GameConfig.Tables.TbTower.DataList)
            {
                var towerConfig = tower;
                var btn = MakeButton(canvasGo.transform, new Vector2(x, 480), new Vector2(240, 90), $"{towerConfig.Name}\n{towerConfig.Cost} 金币");
                btn.onClick.AddListener(() => TDGame.Instance.SelectTower(towerConfig.Id));
                _towerButtons.Add(btn);
                x += 270;
            }

            // 右下：出兵按钮
            _waveButton = MakeButton(canvasGo.transform, new Vector2(760, 480), new Vector2(300, 90), "出 兵 !");
            _waveButton.onClick.AddListener(() => TDGame.Instance.StartNextWave());
            _waveButtonText = _waveButton.GetComponentInChildren<Text>();

            // 中央：结算面板（默认隐藏）
            _resultPanel = new GameObject("ResultPanel");
            _resultPanel.transform.SetParent(canvasGo.transform, false);
            var rt = _resultPanel.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(700, 400);
            var img = _resultPanel.AddComponent<Image>();
            img.color = new Color(0, 0, 0, 0.75f);

            _resultText = MakeText(_resultPanel.transform, new Vector2(0, -60), TextAnchor.MiddleCenter, 64);
            SetSize(_resultText.rectTransform, 660, 120);

            var restartBtn = MakeButton(_resultPanel.transform, new Vector2(0, -220), new Vector2(280, 80), "再来一局");
            restartBtn.onClick.AddListener(() => TDGame.Instance.Restart());
            _resultPanel.SetActive(false);
        }

        /// <summary>创建锚定在父容器顶部的文本</summary>
        private static Text MakeText(Transform parent, Vector2 anchoredPos, TextAnchor align, int size)
        {
            var go = new GameObject("text");
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<Text>();
            text.font = BuiltInFont;
            text.fontSize = size;
            text.alignment = align;
            text.color = Color.white;
            var rt = text.rectTransform;
            bool left = align == TextAnchor.UpperLeft;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(left ? 0f : 0.5f, 1f);
            rt.anchoredPosition = anchoredPos;
            return text;
        }

        private static void SetSize(RectTransform rt, float w, float h)
        {
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, w);
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, h);
        }

        private static Button MakeButton(Transform parent, Vector2 anchoredPos, Vector2 size, string label)
        {
            var go = new GameObject($"btn");
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.color = new Color(0.2f, 0.35f, 0.55f, 0.9f);
            var btn = go.AddComponent<Button>();
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;

            // 按钮文本：铺满按钮
            var textGo = new GameObject("label");
            textGo.transform.SetParent(go.transform, false);
            var text = textGo.AddComponent<Text>();
            text.font = BuiltInFont;
            text.fontSize = 30;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.text = label;
            var trt = text.rectTransform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;
            return btn;
        }

        private void Update()
        {
            HandleClick();
            if (_tipTimer > 0)
            {
                _tipTimer -= Time.deltaTime;
                if (_tipTimer <= 0)
                {
                    _tipText.text = "";
                }
            }
        }

        /// <summary>点击场景中的塔位（点在 UGUI 上时不透射）</summary>
        private void HandleClick()
        {
            if (!Input.GetMouseButtonDown(0))
            {
                return;
            }
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                return;
            }
            Camera cam = Camera.main;
            if (cam == null)
            {
                return;
            }
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 100f))
            {
                var slot = hit.collider.GetComponentInParent<TDBuildSlot>();
                if (slot != null)
                {
                    TDGame.Instance.OnSlotClicked(slot);
                }
            }
        }

        public void ShowTip(string msg)
        {
            _tipText.text = msg;
            _tipTimer = 2.5f;
        }

        public void RefreshAll()
        {
            var game = TDGame.Instance;
            if (game == null)
            {
                return;
            }

            string waveInfo;
            if (game.State == TDGameState.Running)
            {
                waveInfo = $"第 {game.CurrentWave}/{game.TotalWaves} 波  进行中…";
            }
            else if (game.CurrentWave >= game.TotalWaves)
            {
                waveInfo = "全部波次已结束";
            }
            else
            {
                waveInfo = $"第 {game.CurrentWave + 1}/{game.TotalWaves} 波  {game.AutoWaveCountdown:F0}s 后自动出兵（可点按钮提前）";
            }
            string hint = game.SelectedTowerId == 0
                ? "   ▶ 先点下方按钮选塔，再点绿色地块建造"
                : "   ▶ 点击绿色地块建造";
            _statusText.text = $"金币 {game.Gold}    生命 {game.Life}    {waveInfo}{hint}";

            bool canWave = game.State == TDGameState.Ready && game.CurrentWave < game.TotalWaves;
            _waveButton.interactable = canWave;
            _waveButtonText.text = canWave ? "出 兵 !" : (game.CurrentWave >= game.TotalWaves ? "最终波" : "波次进行中…");

            bool playing = game.State == TDGameState.Ready || game.State == TDGameState.Running;
            for (int i = 0; i < _towerButtons.Count; i++)
            {
                var tower = GameConfig.Tables.TbTower.DataList[i];
                bool selected = game.SelectedTowerId == tower.Id;
                _towerButtons[i].image.color = selected
                    ? new Color(0.95f, 0.75f, 0.25f, 0.95f)
                    : new Color(0.2f, 0.35f, 0.55f, 0.9f);
                _towerButtons[i].interactable = playing;
            }
        }

        public void ShowResult(bool win)
        {
            _resultText.text = win ? "防线守住了！" : "防线被突破…";
            _resultText.color = win ? new Color(0.4f, 1f, 0.5f) : new Color(1f, 0.4f, 0.4f);
            _resultPanel.SetActive(true);
        }
    }
}
