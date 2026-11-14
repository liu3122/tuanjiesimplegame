using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using WeChatWASM;

/// <summary>
/// 微信小游戏导出胶水（CI/批处理入口）：
/// 配置 MiniGameConfig → WXConvertCore.DoExport() 全流程（构建 WeixinMiniGame + 转换），
/// 生成小游戏工程于 Build/wx/minigame，并自动注入 DOM shim（见 InjectDomShim）。
/// 打开方式：微信开发者工具 CLI —— "D:\Program Files (x86)\Tencent\微信web开发者工具\cli.bat" open --project &lt;工程绝对路径&gt;
/// AppID 来源优先级：命令行 -wxAppid xxx &gt; 环境变量 WX_APPID &gt; 上一导出配置（touristappid 兜底）。
/// </summary>
public static class WXMinigameExport
{
    private const string ShimMarker = "[CI 注入]";

    [MenuItem("Build/CI/WX 导出小游戏工程", priority = 306)]
    public static void Run()
    {
        CIBuild.EnsureHybridCLRInstalled(); // SDK 的 DoExport 直调 HybridCLR 命令，无安装守卫，先确保初始化
        var conf = UnityUtil.GetEditorConf();
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;

        // AppID：参数/环境变量可覆盖（CI 免改码换号）；touristappid 仅在开发者工具支持测试号的场景可用
        string appid = GetArg("-wxAppid") ?? Environment.GetEnvironmentVariable("WX_APPID");
        if (!string.IsNullOrEmpty(appid))
        {
            conf.ProjectConf.Appid = appid;
        }
        else if (string.IsNullOrEmpty(conf.ProjectConf.Appid))
        {
            conf.ProjectConf.Appid = "touristappid";
        }

        conf.ProjectConf.relativeDST = "Build/wx";
        conf.ProjectConf.DST = Path.GetFullPath(Path.Combine(projectRoot, "Build", "wx"));
        // 首包资源加载方式：1=小游戏包内（data-package 分包），0=CDN（需配置 DATA_CDN）。
        // 开发期无 CDN，走包内分包加载（总包 ~16MB 远低于 30MB 上限）；上线要热更资源时改回 0+CDN。
        conf.ProjectConf.assetLoadType = 1;
        EditorUtility.SetDirty(conf);
        AssetDatabase.SaveAssets();
        Debug.Log($"[WXMinigameExport] appid={conf.ProjectConf.Appid} DST={conf.ProjectConf.DST}");

        var err = WXConvertCore.DoExport();
        Debug.Log($"[WXMinigameExport] DoExport → {err}");
        if (err != WXConvertCore.WXExportError.SUCCEED)
            throw new Exception($"[WXMinigameExport] 微信小游戏导出失败：{err}");

        InjectDomShim(conf.ProjectConf.DST);
        InjectWsBridge(conf.ProjectConf.DST);
        PatchDataCdn(conf.ProjectConf.DST, GetCdnUrl());
        CopyStreamingAssets(conf.ProjectConf.DST);
        Debug.Log($"[WXMinigameExport] 完成：{Path.Combine(conf.ProjectConf.DST, "minigame")}（appid={conf.ProjectConf.Appid}）");
    }

    /// <summary>
    /// 给生成工程的 game.js 注入 document.querySelector shim：
    /// 团结引擎 WASM 框架的 onResize/画布尺寸查询路径会调用 document.querySelector，
    /// weapp-adapter 已实现但部分执行作用域拿不到（实测 VM 作用域报 is not a function）。
    /// 幂等（带标记防重复注入）；导出会重新生成 game.js，故每次导出后都注入。
    /// </summary>
    private static void InjectDomShim(string dst)
    {
        string gameJs = Path.Combine(dst, "minigame", "game.js");
        if (!File.Exists(gameJs))
        {
            Debug.LogWarning($"[WXMinigameExport] 未找到 {gameJs}，跳过 shim 注入");
            return;
        }
        string content = File.ReadAllText(gameJs);
        if (content.Contains(ShimMarker))
        {
            Debug.Log("[WXMinigameExport] shim 已存在，跳过");
            return;
        }
        string shim =
            "// " + ShimMarker + " document.querySelector shim：团结引擎框架 onResize/尺寸查询路径调用，weapp-adapter 部分作用域未覆盖\n" +
            "(function(){if(typeof document!=='undefined'&&typeof document.querySelector!=='function'){" +
            "document.querySelector=function(s){if(s==='#canvas'||s==='canvas'){return (typeof GameGlobal!=='undefined'&&GameGlobal.canvas)||(typeof window!=='undefined'&&window.canvas)||null;}return null;};" +
            "document.querySelectorAll=function(){return [];};}})();\n";
        File.WriteAllText(gameJs, shim + content);
        Debug.Log("[WXMinigameExport] DOM shim 已注入 game.js");
    }

    private const string WsBridgeMarker = "[CI 注入 WSBridge]";

    /// <summary>
    /// 给生成工程注入 WebSocket 桥（对决/抢宝房在小游戏上的通信通道）：
    /// C# 热更侧 WxBridgeWsConnection 经 WX.CallJSFunction 调 GameGlobal.WSBridge.{setCallback,connect,send,close}；
    /// JS 事件经 GameGlobal.Module.dynCall_vii 回调 C# —— 对应 JsCallbackBridge.OnJsEvent 预留槽位
    /// void(int,IntPtr)（eventId=连接号，payload=["open|msg|err|close",文本]）。
    /// 与 DOM shim 同款幂等注入；导出会重新生成 game.js，故每次导出后都注入。
    /// 生效前提：底包含 ReversePInvoke 槽位（HybridCLR Generate/All，见 JsCallbackBridge 注释）。
    /// </summary>
    private static void InjectWsBridge(string dst)
    {
        string gameJs = Path.Combine(dst, "minigame", "game.js");
        if (!File.Exists(gameJs))
        {
            Debug.LogWarning($"[WXMinigameExport] 未找到 {gameJs}，跳过 WSBridge 注入");
            return;
        }
        string content = File.ReadAllText(gameJs);
        if (content.Contains(WsBridgeMarker))
        {
            Debug.Log("[WXMinigameExport] WSBridge 已存在，跳过");
            return;
        }
        string bridge =
@"// " + WsBridgeMarker + @" WebSocket 桥：wx.connectSocket ←→ C# WxBridgeWsConnection（热更侧 Platform/WsConnection.cs）
(function () {
  var sockets = {}, cbPtr = 0;
  function M() { return GameGlobal.Module; }
  function callVii(ptr, a, b) {
    var m = M();
    if (typeof m.dynCall_vii === 'function') return m.dynCall_vii(ptr, a, b);
    try { // 兜底：直接查 wasm 函数表（TCPSocket 桥同款机制的降级路径）
      var fn = m.getWasmTableEntry ? m.getWasmTableEntry(ptr) : (m.wasmTable || m.asm).get(ptr);
      if (fn) return fn(a, b);
    } catch (e) {}
    console.error('[WSBridge] dynCall_vii 通道不可用，事件丢失');
  }
  function emit(id, kind, text) {
    if (!cbPtr) return;
    var m = M();
    var json = JSON.stringify([kind, text || '']);
    var len = m.lengthBytesUTF8(json) + 1;
    var ptr = m._malloc(len);
    m.stringToUTF8(json, ptr, len);
    callVii(cbPtr, id, ptr);
    m._free(ptr); // C# 回调内同步消费完指针才返回，此处可安全释放
  }
  function install() {
    if (typeof GameGlobal === 'undefined' || typeof wx === 'undefined' || GameGlobal.WSBridge) return false;
    GameGlobal.WSBridge = {
      setCallback: function (ptr) { cbPtr = ptr; },
      connect: function (id, url) {
        try {
          var task = wx.connectSocket({ url: url });
          sockets[id] = task;
          task.onOpen(function () { emit(id, 'open', ''); });
          task.onMessage(function (res) { emit(id, 'msg', typeof res.data === 'string' ? res.data : ''); });
          task.onError(function (err) { emit(id, 'err', (err && err.errMsg) || ''); });
          task.onClose(function () { emit(id, 'close', ''); delete sockets[id]; });
        } catch (e) { emit(id, 'err', String(e && e.message || e)); emit(id, 'close', ''); }
      },
      send: function (id, text) { var s = sockets[id]; if (s) { try { s.send({ data: text }); } catch (e) {} } },
      close: function (id) { var s = sockets[id]; if (s) { try { s.close({}); } catch (e) {} } delete sockets[id]; },
    };
    return true;
  }
  if (!install()) { // GameGlobal/wx 若晚于本脚本就绪（适配器加载顺序差异），短轮询兜底
    var n = 0, t = setInterval(function () { if (install() || ++n > 100) clearInterval(t); }, 100);
  }
})();
";
        File.WriteAllText(gameJs, bridge + content);
        Debug.Log("[WXMinigameExport] WSBridge 已注入 game.js");
    }

    /// <summary>
    /// 资源 CDN 前缀。WXAssetBundle 缓存层（WXAssetBundle.jslib 的 url2path）按 DATA_CDN 前缀把
    /// 远端 bundle URL 映射为 USER_DATA_PATH/__GAME_FILE_CACHE/ 本地缓存路径；转换器在
    /// assetLoadType=1（首包数据走包内分包）时不回填 DATA_CDN，为空时映射串损坏，
    /// C# 侧回退 LoadFromFile(原始URL) → "Unable to open archive file: http://..."。
    /// 取值须与 GameApp.RemoteHostURL 同前缀；真机预览时用 WX_CDN=http://<局域网IP>/CDN 覆盖。
    /// </summary>
    private static string GetCdnUrl()
    {
        return (Environment.GetEnvironmentVariable("WX_CDN") ?? "http://127.0.0.1/CDN").TrimEnd('/');
    }

    /// <summary>
    /// 补写生成工程 game.js 的 DATA_CDN（managerConfig.DATA_CDN → 同步到 GameGlobal.DATA_CDN）。
    /// 只替换空占位，SDK 已写入正确值时跳过；导出每次重新生成 game.js，故每次导出后都执行。
    /// </summary>
    private static void PatchDataCdn(string dst, string cdn)
    {
        string gameJs = Path.Combine(dst, "minigame", "game.js");
        if (!File.Exists(gameJs))
        {
            Debug.LogWarning($"[WXMinigameExport] 未找到 {gameJs}，跳过 DATA_CDN 补写");
            return;
        }
        string content = File.ReadAllText(gameJs);
        string replacement = $"DATA_CDN: '{cdn}'";
        if (content.Contains(replacement))
        {
            Debug.Log("[WXMinigameExport] DATA_CDN 已是目标值，跳过");
            return;
        }
        const string placeholder = "DATA_CDN: ''";
        if (!content.Contains(placeholder))
        {
            Debug.LogWarning("[WXMinigameExport] game.js 未找到 DATA_CDN 空占位（转换模板已变？），请人工确认");
            return;
        }
        File.WriteAllText(gameJs, content.Replace(placeholder, replacement));
        Debug.Log($"[WXMinigameExport] DATA_CDN 已补写 → {cdn}");
    }

    /// <summary>
    /// 补拷 StreamingAssets 到小游戏工程根：WebServerFileSystem 以 https://game.weixin.qq.com（虚拟域名=包根）
    /// 为基址请求 StreamingAssets/yoo/...，SDK 转换在分包资源模式下不拷贝该目录 → 404。
    /// 幂等（存在则跳过，重建时覆盖）。
    /// </summary>
    private static void CopyStreamingAssets(string dst)
    {
        string src = Path.Combine(dst, "webgl", "StreamingAssets");
        string to = Path.Combine(dst, "minigame", "StreamingAssets");
        if (!Directory.Exists(src))
        {
            Debug.LogWarning($"[WXMinigameExport] 未找到 {src}，跳过 StreamingAssets 补拷");
            return;
        }
        if (Directory.Exists(to)) Directory.Delete(to, true);
        CopyDir(src, to);
        Debug.Log($"[WXMinigameExport] StreamingAssets 已补拷 → {to}");
    }

    private static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (string f in Directory.GetFiles(src))
            File.Copy(f, Path.Combine(dst, Path.GetFileName(f)));
        foreach (string d in Directory.GetDirectories(src))
            CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
    }

    private static string GetArg(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == name && i + 1 < args.Length) return args[i + 1];
            if (args[i].StartsWith(name + "=")) return args[i].Substring(name.Length + 1);
        }
        return null;
    }
}
