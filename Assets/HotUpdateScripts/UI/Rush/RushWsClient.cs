using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Threading;
using System.Text;
using Luban.SimpleJSON;
using UnityEngine;

namespace Game.HotUpdate.ChestGame
{
    /// <summary>
    /// 抢宝房 WebSocket 客户端：连接 /ws/rush，事件进队列，
    /// 由主线程 Pump 派发（Unity API 只能在主线程调用）。
    /// </summary>
    public class RushWsClient
    {
        private ClientWebSocket _ws;
        private readonly ConcurrentQueue<string> _inbox  = new System.Collections.Concurrent.ConcurrentQueue<string>();
        public bool Connected { get; private set; }

        public async void Connect(int roomId)
        {
            try
            {
                _ws = new ClientWebSocket();
                string url = $"{ApiClient.BaseUrl.Replace("http", "ws")}/ws/rush?roomId={roomId}&token={ApiClient.Token}";
                await _ws.ConnectAsync(new System.Uri(url), CancellationToken.None);
                Connected = true;
                Debug.Log("[RushWs] 已连接房间 " + roomId);
                _ = ReceiveLoop();
            }
            catch (System.Exception e)
            {
                Debug.LogError(L10n.Get("[RushWs] 连接失败：") + e.Message);
                Connected = false;
            }
        }

        private async System.Threading.Tasks.Task ReceiveLoop()
        {
            var buffer = new byte[8192];
            try
            {
                while (_ws.State == WebSocketState.Open)
                {
                    var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close) break;
                    _inbox.Enqueue(Encoding.UTF8.GetString(buffer, 0, result.Count));
                }
            }
            catch { }
            Connected = false;
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
        public void Claim(int slot)
        {
            if (!Connected || _ws == null) return;
            var msg = $"{{\"type\":\"claim\",\"slot\":{slot}}}";
            _ = _ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(msg)), WebSocketMessageType.Text, true, CancellationToken.None);
        }

        public void Close()
        {
            try { _ws?.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); } catch { }
            Connected = false;
        }
    }
}
