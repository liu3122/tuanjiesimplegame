using System;
using System.Collections.Concurrent;
using System.Text;
using Luban.SimpleJSON;
using UnityEngine;

namespace Game.HotUpdate.ChestGame
{
    /// <summary>
    /// 镜像对决 WebSocket 客户端：连接 /ws/duel，事件进队列，
    /// 由主线程 Pump 派发（Unity API 只能在主线程调用）。
    /// 连接层见 Platform/WsConnection.cs（编辑器/原生 ClientWebSocket；小游戏 WSBridge JS 桥）。
    /// </summary>
    public class DuelWsClient
    {
        private IWsConnection _ws;
        private readonly ConcurrentQueue<string> _inbox = new ConcurrentQueue<string>();
        public bool Connected => _ws?.Connected ?? false;

        public void Connect()
        {
            _ws = WsConnectionFactory.Create();
            string url = WsConnectionFactory.ToWsUrl(ApiClient.BaseUrl, "/ws/duel?token=" + ApiClient.Token);
            _ws.Connect(url,
                msg => _inbox.Enqueue(msg),
                () => _inbox.Enqueue("{\"type\":\"__disconnected\"}")); // 断线通知面板（切后台 socket 必断）
            Debug.Log("[DuelWs] 连接对决房间 /ws/duel（URL 含鉴权 token，不打日志）");
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

        private void Send(string msg) => _ws?.Send(msg);

        public void Close() => _ws?.Close();
    }
}
