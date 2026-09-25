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
    /// 镜像对决 WebSocket 客户端：连接 /ws/duel，事件进队列，
    /// 由主线程 Pump 派发（Unity API 只能在主线程调用）。
    /// </summary>
    public class DuelWsClient
    {
        private ClientWebSocket _ws;
        private readonly ConcurrentQueue<string> _inbox = new ConcurrentQueue<string>();
        public bool Connected { get; private set; }

        public async void Connect()
        {
            try
            {
                _ws = new ClientWebSocket();
                string url = $"{ApiClient.BaseUrl.Replace("http", "ws")}/ws/duel?token={ApiClient.Token}";
                await _ws.ConnectAsync(new System.Uri(url), CancellationToken.None);
                Connected = true;
                Debug.Log("[DuelWs] 已连接对决房间");
                _ = ReceiveLoop();
            }
            catch (System.Exception e)
            {
                Debug.LogError(L10n.Get("[DuelWs] 连接失败：") + e.Message);
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

        public void Eliminate(int slot) => Send("{\"type\":\"eliminate\",\"slot\":" + slot + "}");

        public void Skill(int skillType, int? slotIndex = null) => Send(slotIndex.HasValue
            ? "{\"type\":\"skill\",\"skillType\":" + skillType + ",\"slotIndex\":" + slotIndex.Value + "}"
            : "{\"type\":\"skill\",\"skillType\":" + skillType + "}");

        public void Sell() => Send("{\"type\":\"sell\"}");
        public void Accept() => Send("{\"type\":\"accept\"}");
        public void Decline() => Send("{\"type\":\"decline\"}");

        private void Send(string msg)
        {
            if (!Connected || _ws == null) return;
            _ = _ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(msg)), WebSocketMessageType.Text, true, CancellationToken.None);
        }

        public void Close()
        {
            try { _ws?.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); } catch { }
            Connected = false;
        }
    }
}
