using System;
using System.Collections.Concurrent;
using System.Text;
using System.Threading;
using UnityEngine;

namespace Game.HotUpdate.ChestGame
{
    /// <summary>
    /// WebSocket 连接抽象（对决/抢宝房共用）：
    /// - 编辑器/原生平台：NativeWsConnection（System.Net ClientWebSocket）；
    /// - 微信小游戏（UNITY_WEBGL）：WxBridgeWsConnection —— WASM 无 System.Net sockets 实现，
    ///   直用 ClientWebSocket 会 PlatformNotSupportedException（对决/抢宝 100% 连不上）。
    ///   走 WX.CallJSFunction → 导出工程注入的 WSBridge（wx.connectSocket），
    ///   JS 事件经 JsCallbackBridge 预留槽位 void(int,IntPtr) 回调（见该文件铁律）。
    /// </summary>
    public interface IWsConnection
    {
        bool Connected { get; }

        /// <summary>连接。onMessage/onClosed 可能在任意线程回调（调用方自行入队、主线程 Pump）</summary>
        void Connect(string url, Action<string> onMessage, Action onClosed);

        /// <summary>发送文本（实现内部保证串行与静默失败安全）</summary>
        bool Send(string text);

        void Close();
    }

    public static class WsConnectionFactory
    {
        public static IWsConnection Create()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return new WxBridgeWsConnection();
#else
            return new NativeWsConnection();
#endif
        }

        /// <summary>http(s) API 地址 → ws(s) 连接地址（原 Replace("http","ws") 会误伤路径里含 "http" 的串）</summary>
        public static string ToWsUrl(string baseUrl, string pathAndQuery)
        {
            var b = baseUrl.TrimEnd('/');
            string scheme = b.StartsWith("https", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws";
            int idx = b.IndexOf("://", StringComparison.Ordinal);
            if (idx > 0) b = b[(idx + 3)..];
            return $"{scheme}://{b}{pathAndQuery}";
        }
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    /// <summary>
    /// 小游戏 WS 连接：C#→JS 走 WX.CallJSFunction（WXWASMSDK 注入的 GameGlobal.WSBridge，见
    /// Assets/Editor/WXMinigameExport.cs InjectWsBridge）；JS→C# 走 JsCallbackBridge.OnJsEvent
    /// 预留槽位（void(int,IntPtr)：eventId=连接号，payload=["open|msg|err|close", 文本]）。
    /// 激活条件：下次导出底包（HybridCLR Generate/All 固化槽位）+ 注入的 wsbridge.js 随包。
    /// </summary>
    public sealed class WxBridgeWsConnection : IWsConnection
    {
        // 连接注册表：JS eventId → 连接实例（OnJsEvent 是全局单指针，按 id 分发）
        private static readonly ConcurrentDictionary<int, WxBridgeWsConnection> _active = new();
        private static int _nextId;
        private static bool _callbackRegistered;

        private readonly int _id = Interlocked.Increment(ref _nextId);
        private Action<string> _onMessage;
        private Action _onClosed;

        public bool Connected { get; private set; }
        private int _closed;

        public void Connect(string url, Action<string> onMessage, Action onClosed)
        {
            _onMessage = onMessage;
            _onClosed = onClosed;
            RegisterCallbackOnce();
            _active[_id] = this;
            // JS: WSBridge.connect(id, url) → wx.connectSocket；事件回到 OnJsEvent
            WeChatWASM.WX.CallJSFunction("WSBridge", "connect", _id, url);
        }

        /// <summary>静态持有委托：GetFunctionPointerForDelegate 的委托实例被 GC 回收会使函数指针失效</summary>
        private static JsCallbackBridge.JsEventCallback _rootKeepAlive;

        private static void RegisterCallbackOnce()
        {
            if (_callbackRegistered) return;
            try
            {
                // 铁律（见 JsCallbackBridge）：反向回调指针只能取自预留了 ReversePInvoke 槽位的方法本身，
                // 订阅方经其静态事件 JsEvent 分发（eventId=连接号），不得另开签名或另取指针
                JsCallbackBridge.JsEvent += OnJsEvent;
                _rootKeepAlive = new JsCallbackBridge.JsEventCallback(JsCallbackBridge.OnJsEvent);
                var ptr = System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(_rootKeepAlive);
                WeChatWASM.WX.CallJSFunction("WSBridge", "setCallback", ptr.ToInt32());
                _callbackRegistered = true; // 成功才置位：失败（如底包无槽位）时下次连接仍会重试
            }
            catch (Exception e)
            {
                Debug.LogError("[WsBridge] 注册 JS 回调指针失败（底包未含 ReversePInvoke 槽位？需重导底包）：" + e.Message);
            }
        }

        /// <summary>JS 事件分发（eventId=连接号；payload=["open|msg|err|close", 文本]）</summary>
        private static void OnJsEvent(int eventId, IntPtr payloadPtr)
        {
            if (!_active.TryGetValue(eventId, out var conn)) return;
            try
            {
                var node = Luban.SimpleJSON.JSON.Parse(ReadUtf8(payloadPtr));
                if (node == null || node.Count < 1) return;
                switch (node[0].Value)
                {
                    case "open":
                        conn.Connected = true;
                        break;
                    case "msg":
                        conn._onMessage?.Invoke(node.Count > 1 ? node[1].Value : "");
                        break;
                    case "err":
                        Debug.LogWarning("[WsBridge] socket 错误：" + (node.Count > 1 ? node[1].Value : ""));
                        break;
                    case "close":
                        conn.Connected = false;
                        _active.TryRemove(eventId, out _);
                        conn._onClosed?.Invoke();
                        break;
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[WsBridge] 事件处理异常：" + e.Message);
            }
        }

        /// <summary>IntPtr → UTF8 字符串（手写解码：Marshal.PtrToStringUTF8 属 BCL 冷门方法，AOT 可能被裁）</summary>
        private static string ReadUtf8(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero) return "";
            int len = 0;
            while (System.Runtime.InteropServices.Marshal.ReadByte(ptr, len) != 0) len++;
            var bytes = new byte[len];
            for (int i = 0; i < len; i++) bytes[i] = System.Runtime.InteropServices.Marshal.ReadByte(ptr, i);
            return Encoding.UTF8.GetString(bytes);
        }

        public bool Send(string text)
        {
            if (!Connected) return false;
            WeChatWASM.WX.CallJSFunction("WSBridge", "send", _id, text);
            return true;
        }

        public void Close()
        {
            if (System.Threading.Interlocked.Exchange(ref _closed, 1) == 1) return; // 幂等
            Connected = false;
            _active.TryRemove(_id, out _);
            WeChatWASM.WX.CallJSFunction("WSBridge", "close", _id);
            _onClosed?.Invoke(); // 与 Native 实现语义对称
        }
    }

#else
    /// <summary>原生/编辑器 WS 连接（ClientWebSocket）：修复了旧实现的三个坑——Send 并发异常、大消息分帧截断、Close 不 await</summary>
    public sealed class NativeWsConnection : IWsConnection
    {
        private System.Net.WebSockets.ClientWebSocket _ws;
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private Action<string> _onMessage;
        private Action _onClosed;
        private int _closed;

        public bool Connected => _ws?.State == System.Net.WebSockets.WebSocketState.Open;

        public async void Connect(string url, Action<string> onMessage, Action onClosed)
        {
            _onMessage = onMessage;
            _onClosed = onClosed;
            try
            {
                _ws = new System.Net.WebSockets.ClientWebSocket();
                await _ws.ConnectAsync(new Uri(url), CancellationToken.None);
                _ = ReceiveLoop();
            }
            catch (Exception e)
            {
                Debug.LogError("[NativeWs] 连接失败：" + url + " " + e.Message);
                _onClosed?.Invoke();
            }
        }

        private async System.Threading.Tasks.Task ReceiveLoop()
        {
            var buffer = new byte[8192];
            var msg = new System.Collections.Generic.List<byte>();
            try
            {
                while (_ws.State == System.Net.WebSockets.WebSocketState.Open)
                {
                    var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Close) break;
                    // 按分帧拼包：超过 8K 的 JSON（snapshot 类）旧实现直接截断解析失败
                    msg.AddRange(new ArraySegment<byte>(buffer, 0, result.Count));
                    if (!result.EndOfMessage) continue;
                    if (msg.Count > 0)
                    {
                        _onMessage?.Invoke(Encoding.UTF8.GetString(msg.ToArray()));
                        msg.Clear();
                    }
                }
            }
            catch { }
            FinishClose();
        }

        public bool Send(string text)
        {
            if (!Connected) return false;
            var bytes = Encoding.UTF8.GetBytes(text);
            // ClientWebSocket 同时只允许一个在途 Send：快速连点会 InvalidOperationException 丢消息 → 串行化
            _ = SendLocked(bytes);
            return true;
        }

        private async System.Threading.Tasks.Task SendLocked(byte[] bytes)
        {
            await _sendLock.WaitAsync();
            try
            {
                if (Connected)
                    await _ws.SendAsync(new ArraySegment<byte>(bytes), System.Net.WebSockets.WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch { }
            finally { _sendLock.Release(); }
        }

        public void Close()
        {
            FinishClose();
            try { _ws?.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); } catch { }
        }

        private void FinishClose()
        {
            if (Interlocked.Exchange(ref _closed, 1) == 1) return;
            _onClosed?.Invoke();
        }
    }
#endif
}
