using Microsoft.AspNetCore.SignalR;

namespace SimpleGameServer.Hubs
{
    /// <summary>拍卖行实时推送：竞价变化 / 结算结果。客户端连接 /hubs/auction 订阅。</summary>
    public class AuctionHub : Hub
    {
    }
}
