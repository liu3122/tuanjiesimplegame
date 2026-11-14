using System;
using System.Collections.Concurrent;
using Luban.SimpleJSON;
using UnityEngine;

namespace Game.HotUpdate.ChestGame
{
    /// <summary>
    /// 抢宝房 WebSocket 客户端：连接 /ws/rush，事件进队列，
    /// 由主线程 Pump 派发（Unity API 只能在主线程调用）。
    /// 连接层见 Platform/WsConnection.cs（编辑器/原生 ClientWebSocket；小游戏 WSBridge JS 桥）。
    /// </summary>
    public class RushWsClient
    {
        private IWsConnection _ws;
        private readonly ConcurrentQueue<string> _inbox = new();
        public bool Connected => _ws?.Connected ?? false;

        public void Connect(int roomId)
        {
            _ws = WsConnectionFactory.Create();
            string url = WsConnectionFactory.ToWsUrl(ApiClient.BaseUrl, $"/ws/rush?roomId={roomId}&token={ApiClient.Token}");
            _ws.Connect(url,
                msg => _inbox.Enqueue(msg),
                () => _inbox.Enqueue("{\"type\":\"__disconnected\"}")); // 断线通知面板（切后台 socket 必断）
            Debug.Log($"[RushWs] 连接房间 {roomId}（URL 含鉴权 token，不打日志）");
        }

        /// <summary>主线程每帧调用：派发收到的事件</summary>
        public void Pump(System.Action<JSONNode> handler)
        {
            while (_inbox.TryDequeue(out var json))
            {
                var node = JSON.Parse(json);
                if (node != null) handler(node);
            }
        }

        /// <summary>抢格</summary>
        public void Claim(int slot) => _ws?.Send($"{{\"type\":\"claim\",\"slot\":{slot}}}");

        public void Close() => _ws?.Close();
    }
}
