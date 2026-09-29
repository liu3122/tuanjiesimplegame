using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.HotUpdate.ChestGame
{
    /// <summary>UGUI 代码构建工具（热更侧公共）</summary>
    public static class TDUIHelper
    {
        private static Font BuiltInFont => Resources.GetBuiltinResource<Font>("Arial.ttf");

        public static Color HexColor(string hex)
        {
            return ColorFromString(hex);
        }

        public static Color ColorFromString(string hex)
        {
            if (string.IsNullOrEmpty(hex) || hex.Length < 6) return Color.white;
            return new Color32(
                System.Convert.ToByte(hex.Substring(0, 2), 16),
                System.Convert.ToByte(hex.Substring(2, 2), 16),
                System.Convert.ToByte(hex.Substring(4, 2), 16),
                255);
        }

        public static Canvas CreateCanvas(Transform parent)
        {
            var canvasGo = new GameObject("UICanvas");
            canvasGo.transform.SetParent(parent, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920); // 竖版设计分辨率
            canvasGo.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        public static void EnsureEventSystem()
        {
            if (Object.FindObjectOfType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }

        /// <summary>创建锚定在父容器顶部的文本</summary>
        public static Text MakeText(Transform parent, Vector2 anchoredPos, TextAnchor align, int size)
        {
            var go = new GameObject("text");
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<Text>();
            text.font = BuiltInFont;
            text.fontSize = size;
            text.alignment = align;
            text.color = Color.white;
            text.supportRichText = true;
            var rt = text.rectTransform;
            bool left = align == TextAnchor.UpperLeft;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(left ? 0f : 0.5f, 1f);
            rt.anchoredPosition = anchoredPos;
            return text;
        }

        public static void SetSize(RectTransform rt, float w, float h)
        {
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, w);
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, h);
        }

        public static Button MakeButton(Transform parent, Vector2 anchoredPos, Vector2 size, string label)
        {
            var go = new GameObject("btn");
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.color = new Color(0.2f, 0.35f, 0.55f, 0.9f);
            var btn = go.AddComponent<Button>();
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;

            var textGo = new GameObject("label");
            textGo.transform.SetParent(go.transform, false);
            var text = textGo.AddComponent<Text>();
            text.font = BuiltInFont;
            text.fontSize = 26;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.supportRichText = true;
            text.text = label;
            var trt = text.rectTransform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;
            return btn;
        }

        public static InputField MakeInputField(Transform parent, Vector2 anchoredPos, Vector2 size, string placeholder)
        {
            var go = new GameObject("input");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.1f, 0.12f, 0.16f, 0.95f);
            var input = go.AddComponent<InputField>();
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;

            var phGo = new GameObject("placeholder");
            phGo.transform.SetParent(go.transform, false);
            var ph = phGo.AddComponent<Text>();
            ph.font = BuiltInFont; ph.fontSize = 24; ph.color = Color.gray;
            ph.alignment = TextAnchor.MiddleCenter; ph.text = placeholder;
            Fit(ph.rectTransform);

            var textGo = new GameObject("text");
            textGo.transform.SetParent(go.transform, false);
            var t = textGo.AddComponent<Text>();
            t.font = BuiltInFont; t.fontSize = 24; t.color = Color.white;
            t.alignment = TextAnchor.MiddleLeft;
            Fit(t.rectTransform);
            t.rectTransform.offsetMin = new Vector2(16, 0);

            input.placeholder = ph;
            input.textComponent = t;
            input.contentType = InputField.ContentType.IntegerNumber;
            return input;
        }

        private static void Fit(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
