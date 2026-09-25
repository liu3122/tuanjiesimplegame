using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Game.HotUpdate.ChestGame
{
    /// <summary>
    /// 「秘宝暗金」UI 主题：加载 Assets/AssetRaw/UI 下的 Sprite 并提供组件皮肤 API。
    /// 贴图可整体替换为美术成品（保持文件名与九宫格参数，见 UI_THEME.md），代码零改动。
    /// </summary>
    public static class UITheme
    {
        // ---- 色板（与 UI_THEME.md 一致）----
        public static readonly Color Bg = FromHex("12151D");
        public static readonly Color Panel = FromHex("1C2230");
        public static readonly Color Gold = FromHex("E8C35A");
        public static readonly Color GoldLight = FromHex("F5D061");
        public static readonly Color TextSub = FromHex("9AA3B2");
        public static readonly Color Green = FromHex("4DD0A5");
        public static readonly Color Red = FromHex("E05D5D");
        public static readonly Color Blue = FromHex("6EC6FF");

        private static readonly Dictionary<string, Sprite> _cache = new Dictionary<string, Sprite>();
        private static bool _applied;

        /// <summary>项目根下的资源相对路径（AssetRaw/UI）</summary>
        private const string ResDir = "Assets/AssetRaw/UI";

        // ---------------- 图集拆分（spritePackingTag 同名打组） ----------------
        // common 全局常驻；界面图集随界面使用，界面关闭可整体卸载（Resources.UnloadUnusedAssets 回收）。
        private static readonly Dictionary<string, string> _atlasOf = new Dictionary<string, string>
        {
            { "panel_gold", "common" }, { "panel_dark", "common" },
            { "btn_gold", "common" }, { "btn_green", "common" }, { "btn_red", "common" }, { "btn_blue", "common" },
            { "icon_coin", "common" },
            { "icon_shop", "main" }, { "icon_bag", "main" }, { "icon_auction", "main" }, { "icon_rank", "main" },
            { "chest_wood", "shop" }, { "chest_silver", "shop" }, { "chest_gold", "shop" },
            { "slot_locked", "open" }, { "slot_open", "open" },
            { "skill_insight", "open" }, { "skill_price", "open" }, { "skill_free", "open" },
        };

        /// <summary>素材所属图集名（未登记的归 common）</summary>
        public static string AtlasOf(string name)
        {
            string atlas;
            return _atlasOf.TryGetValue(name, out atlas) ? atlas : "common";
        }

        /// <summary>预加载某界面的整组图集（进入界面时可调用，减少首帧卡顿）</summary>
        public static void PreloadAtlas(string atlas)
        {
            foreach (var kv in _atlasOf)
            {
                if (kv.Value == atlas) Load(kv.Key);
            }
        }

        /// <summary>当前已加载的图集及 sprite 数（调试用）</summary>
        public static string LoadedAtlasStats()
        {
            var byAtlas = new Dictionary<string, int>();
            foreach (var kv in _cache)
            {
                if (kv.Value == null) continue;
                string a = AtlasOf(kv.Key);
                int n;
                byAtlas.TryGetValue(a, out n);
                byAtlas[a] = n + 1;
            }
            var sb = new System.Text.StringBuilder("[UITheme] 已加载图集: ");
            foreach (var kv in byAtlas) sb.Append(kv.Key).Append('(').Append(kv.Value).Append(") ");
            return sb.ToString();
        }

        public static Color FromHex(string hex)
        {
            if (string.IsNullOrEmpty(hex) || hex.Length < 6) return Color.white;
            return new Color32(
                System.Convert.ToByte(hex.Substring(0, 2), 16),
                System.Convert.ToByte(hex.Substring(2, 2), 16),
                System.Convert.ToByte(hex.Substring(4, 2), 16),
                255);
        }

        /// <summary>按文件名（不含扩展名）取 Sprite；编辑器下直接读 PNG，真机走 AssetRaw 收集</summary>
        public static Sprite Load(string name)
        {
            if (_cache.TryGetValue(name, out var s) && s != null) return s;
#if UNITY_EDITOR
            string path = $"{ResDir}/{name}.png";
            var tex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex != null)
            {
                s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect,
                    new Vector4(32, 32, 32, 32));
                s.name = name;
                _cache[name] = s;
                return s;
            }
#endif
            Debug.LogWarning($"[UITheme] Sprite 不存在：{name}（{ResDir}/{name}.png）");
            return null;
        }

        /// <summary>相机背景设为主题底色</summary>
        public static void ApplyCameraTheme()
        {
            var cam = Camera.main;
            if (cam == null) return;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Bg;
        }

        // ---------------- 组件皮肤 ----------------

        /// <summary>创建九宫格面板对象（panel_gold / panel_dark）</summary>
        public static GameObject CreatePanel(Transform parent, string spriteName, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(spriteName);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.type = Image.Type.Sliced;
            var sp = Load(spriteName);
            if (sp != null) img.sprite = sp;
            else img.color = Panel;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return go;
        }

        /// <summary>主题化按钮：skin = gold/green/red/blue</summary>
        public static Button ThemedButton(Transform parent, Vector2 pos, Vector2 size, string label, string skin = "gold", int fontSize = 26)
        {
            var go = new GameObject("btn_" + skin);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.type = Image.Type.Sliced;
            var sp = Load("btn_" + skin);
            if (sp != null) img.sprite = sp;
            var btn = go.AddComponent<Button>();
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            var textGo = new GameObject("label");
            textGo.transform.SetParent(go.transform, false);
            var text = textGo.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = skin == "gold" ? FromHex("2A230F") : Color.white;
            text.supportRichText = true;
            text.text = label;
            var trt = text.rectTransform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;
            return btn;
        }

        /// <summary>图标 + 文本的入口卡片按钮</summary>
        public static Button IconCard(Transform parent, Vector2 pos, Vector2 size, string iconName, string label)
        {
            var btn = ThemedButton(parent, pos, size, label, "dark", 30);
            var sp = Load(iconName);
            if (sp != null)
            {
                var iconGo = new GameObject("icon");
                iconGo.transform.SetParent(btn.transform, false);
                var img = iconGo.AddComponent<Image>();
                img.sprite = sp;
                img.raycastTarget = false;
                var irt = img.rectTransform;
                irt.anchorMin = irt.anchorMax = irt.pivot = new Vector2(0.5f, 1f);
                irt.anchoredPosition = new Vector2(0, -18);
                irt.sizeDelta = new Vector2(size.y * 0.45f, size.y * 0.45f);
                // 下移文本
                var labelT = btn.GetComponentInChildren<Text>();
                var lrt = labelT.rectTransform;
                lrt.anchorMin = new Vector2(0, 0);
                lrt.anchorMax = new Vector2(1, 0.42f);
                lrt.offsetMin = Vector2.zero;
                lrt.offsetMax = Vector2.zero;
            }
            return btn;
        }

        /// <summary>给既有 Image 换主题面板皮肤（迁移期兼容）</summary>
        public static void SkinPanel(Image img, bool gold = false)
        {
            img.type = Image.Type.Sliced;
            var sp = Load(gold ? "panel_gold" : "panel_dark");
            if (sp != null) img.sprite = sp;
        }

        public static void SkinButton(Button btn, string skin)
        {
            var img = btn.GetComponent<Image>();
            img.type = Image.Type.Sliced;
            var sp = Load("btn_" + skin);
            if (sp != null) img.sprite = sp;
        }
    }
}
