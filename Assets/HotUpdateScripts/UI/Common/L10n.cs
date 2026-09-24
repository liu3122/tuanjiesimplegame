using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Game.HotUpdate.ChestGame
{
    /// <summary>
    /// 多语言：Key = 中文原文（中文环境零查找直接返回；英文环境查 TbText）。
    /// 静态 prefab 文本由 LocalizeTransform 自动翻译；动态拼接文案用 Get()。
    /// </summary>
    public static class L10n
    {
        public static string Lang
        {
            get => PlayerPrefs.GetString("l10n.lang", "zh");
            set
            {
                PlayerPrefs.SetString("l10n.lang", value);
                PlayerPrefs.Save();
            }
        }

        public static bool IsZh => Lang != "en";

        /// <summary>取译文：中文即 key（回退原文）；en 查表</summary>
        public static string Get(string key)
        {
            if (key == null || IsZh) return key;
            var item = GameConfig.Tables.TbText.GetOrDefault(key);
            if (item != null && !string.IsNullOrEmpty(item.En)) return item.En;
            return key;
        }

        /// <summary>翻译一行文本（保留 rich 标签：剥标签查表命中则套回同标签）</summary>
        public static string TranslateLine(string line)
        {
            if (string.IsNullOrEmpty(line) || IsZh) return line;
            // 整串命中
            var direct = GameConfig.Tables.TbText.GetOrDefault(line);
            if (direct != null && !string.IsNullOrEmpty(direct.En)) return direct.En;
            // rich 标签包裹：取最长可命中前缀/中缀翻译太复杂——仅处理「前缀标签 + 文本」形态
            int close = line.LastIndexOf('>');
            if (close >= 0 && close < line.Length - 1)
            {
                string prefix = line.Substring(0, close + 1);
                string rest = line.Substring(close + 1).TrimEnd('<', '/');
                var hit = GameConfig.Tables.TbText.GetOrDefault(rest);
                if (hit != null && !string.IsNullOrEmpty(hit.En)) return prefix + hit.En;
            }
            return line;
        }

        /// <summary>遍历翻译一个界面下所有 Text（prefab 静态文本的自动本地化）</summary>
        public static void LocalizeTransform(Transform root)
        {
            if (IsZh) return;
            foreach (var t in root.GetComponentsInChildren<Text>(true))
            {
                t.text = TranslateLine(t.text);
            }
        }

        /// <summary>切换语言并重载当前界面（简版：回到大厅）</summary>
        public static void ToggleLanguage()
        {
            Lang = IsZh ? "en" : "zh";
            UIRoot.ToastStatic(IsZh ? "已切换到中文" : "Switched to English");
            Screens.ShowMain();
        }
    }
}
