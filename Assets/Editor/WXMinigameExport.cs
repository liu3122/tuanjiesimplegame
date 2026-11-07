using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using WeChatWASM;

/// <summary>
/// 微信小游戏导出胶水（CI/批处理入口）：
/// 配置 MiniGameConfig（测试号 touristappid，导出根 Build/wx）→ WXConvertCore.DoExport()
/// 全流程（构建 WeixinMiniGame + 转换），生成小游戏工程于 Build/wx/minigame。
/// 打开方式：微信开发者工具 CLI —— "D:\Program Files (x86)\Tencent\微信web开发者工具\cli.bat" open --project &lt;工程绝对路径&gt;
/// 正式 AppID 替换：改 Appid 字段即可（或走 MP 后台配置）。
/// </summary>
public static class WXMinigameExport
{
    [MenuItem("Build/CI/WX 导出小游戏工程", priority = 306)]
    public static void Run()
    {
        var conf = UnityUtil.GetEditorConf();
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        conf.ProjectConf.Appid = "touristappid";
        conf.ProjectConf.relativeDST = "Build/wx";
        conf.ProjectConf.DST = Path.GetFullPath(Path.Combine(projectRoot, "Build", "wx"));
        EditorUtility.SetDirty(conf);
        AssetDatabase.SaveAssets();
        Debug.Log($"[WXMinigameExport] appid={conf.ProjectConf.Appid} DST={conf.ProjectConf.DST}");

        var err = WXConvertCore.DoExport();
        Debug.Log($"[WXMinigameExport] DoExport → {err}");
        if (err != WXConvertCore.WXExportError.SUCCEED)
            throw new Exception($"[WXMinigameExport] 微信小游戏导出失败：{err}");
        Debug.Log("[WXMinigameExport] 小游戏工程已生成，用微信开发者工具打开 Build/wx/minigame");
    }
}
