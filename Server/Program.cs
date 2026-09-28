using Microsoft.AspNetCore.SignalR;
using System.Net.WebSockets;
using System.Text;
using SimpleGameServer;
using SimpleGameServer.Config;
using SimpleGameServer.Game;
using SimpleGameServer.Hubs;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSignalR();
builder.Services.AddSingleton<GameStore>();
builder.Services.AddSingleton<AuctionEvents>();
builder.Services.AddSingleton<GameService>();
builder.Services.AddHostedService<AuctionBotService>();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyHeader().AllowAnyMethod().SetIsOriginAllowed(_ => true).AllowCredentials()));

var app = builder.Build();
app.UseCors();

// 配置表加载（与客户端共用 Assets/AssetRaw/Config）
GameTables.Load(app.Configuration["GameDataDir"] ?? "../Assets/AssetRaw/Config");

// SQLite 初始化 + 启动恢复（重启数据存活）
Db.Initialize(app.Configuration["GameDb"] ?? "simplegame.db");
var store0 = app.Services.GetRequiredService<GameStore>();
Db.LoadAll(store0);
store0.RestoreIdCursors();
Db.RefundOrphanStakes(store0); // 房间随进程消失：退还抢宝门票/对决入场金
app.Logger.LogInformation("Luban 配置加载完成：宝箱 {Chest} 档，角色 {Char} 个",
    GameTables.Tables.TbChest.DataList.Count, GameTables.Tables.TbCharacter.DataList.Count);

app.MapGet("/", () => Results.Json(new { name = "SimpleGameServer", api = "/api/*", hub = "/hubs/auction" }));

// ---------- 鉴权辅助 ----------
Player Auth(HttpRequest request)
{
    var token = request.Headers["Authorization"].ToString().Replace("Bearer ", "").Trim();
    return app.Services.GetRequiredService<GameStore>().Validate(token);
}

IResult Need(Player p) => p == null ? Results.Json(new { ok = false, error = "UNAUTHORIZED" }, statusCode: 401) : null;

// ---------- 账号 ----------
app.MapPost("/api/login", (LoginReq req, HttpContext ctx, GameStore store) =>
{
    if (string.IsNullOrWhiteSpace(req.Name)) return Results.Json(new { ok = false, error = "BAD_NAME" });
    var player = store.LoginOrRegister(req.Name.Trim());
    player.Region = RegionResolver.Resolve(ctx, req.Region);
    Db.SavePlayer(player);
    return Results.Json(new { ok = true, token = player.Token, playerId = player.Id, name = player.Name, gold = player.Gold, region = player.Region });
});

app.MapPost("/api/character", (CharacterReq req, HttpRequest http, GameStore store) =>
{
    var p = Auth(http);
    var deny = Need(p); if (deny != null) return deny;
    if (GameTables.Tables.TbCharacter.DataList.All(c => c.Id != req.CharacterId)) return Results.Json(new { ok = false, error = "BAD_CHARACTER" });
    lock (store.Gate) p.CharacterId = req.CharacterId;
    return Results.Json(new { ok = true });
});

// ---------- 宝箱 ----------
app.MapGet("/api/state", (HttpRequest http, GameService svc) =>
{
    var p = Auth(http);
    var deny = Need(p); if (deny != null) return deny;
    return Results.Json(new { ok = true, data = svc.ClientState(p) });
});

app.MapPost("/api/chest/buy", (BuyReq req, HttpRequest http, GameService svc) =>
{
    var p = Auth(http);
    var deny = Need(p); if (deny != null) return deny;
    var (ok, err, data) = svc.BuyChest(p, req.ConfigId);
    return Results.Json(new { ok, error = err, data });
});

// 新玩家教学箱（固定种子免费木箱，一次）
app.MapPost("/api/tutorial/chest", (HttpRequest http, GameService svc) =>
{
    var p = Auth(http);
    var deny = Need(p); if (deny != null) return deny;
    var (ok, err, data) = svc.TutorialChest(p);
    return Results.Json(new { ok, error = err, data });
});

// 经济报表（管理端）：X-Admin-Token 头校验，token 来自配置 AdminToken（默认 dev-admin）
app.MapGet("/api/admin/economy", (HttpRequest http) =>
{
    string token = http.Headers["X-Admin-Token"].ToString();
    string expect = app.Configuration["AdminToken"] ?? "dev-admin";
    if (token != expect) return Results.Json(new { ok = false, error = "FORBIDDEN" }, statusCode: 403);
    return Results.Json(new { ok = true, data = Db.EconomyReport() });
});

app.MapPost("/api/chest/{uid}/eliminate", (long uid, EliminateReq req, HttpRequest http, GameService svc) =>
{
    var p = Auth(http);
    var deny = Need(p); if (deny != null) return deny;
    var (ok, err, data) = svc.Eliminate(p, uid, req.SlotIndex);
    return Results.Json(new { ok, error = err, data });
});

app.MapPost("/api/chest/{uid}/skill", (long uid, SkillReq req, HttpRequest http, GameService svc) =>
{
    var p = Auth(http);
    var deny = Need(p); if (deny != null) return deny;
    var (ok, err, data) = svc.UseSkill(p, uid, req.SkillType, req.SlotIndex);
    return Results.Json(new { ok, error = err, data });
});

app.MapGet("/api/rank", (HttpRequest http, GameService svc, string board = "gold_total", string scope = "global") =>
{
    var p = Auth(http);
    var deny = Need(p); if (deny != null) return deny;
    var valid = new[] { "gold_total", "gold_week", "opens_total", "opens_week" };
    if (!valid.Contains(board)) return Results.Json(new { ok = false, error = "BAD_BOARD" });
    if (scope != "global" && scope != "region") scope = "global";
    return Results.Json(new { ok = true, data = svc.RankList(p, board, scope) });
});

// ---------- 抢宝房 ----------
app.MapGet("/api/rush/rooms", (HttpRequest http, GameService svc) =>
{
    var p = Auth(http);
    var deny = Need(p); if (deny != null) return deny;
    if (!Features.IsOn(Features.Rush)) return Results.Json(new { ok = false, error = "FEATURE_DISABLED" });
    return Results.Json(new { ok = true, data = svc.Rush.RoomList() });
});

app.MapPost("/api/rush/join", (RushJoinReq req, HttpRequest http, GameService svc, GameStore store) =>
{
    var p = Auth(http);
    var deny = Need(p); if (deny != null) return deny;
    if (!Features.IsOn(Features.Rush)) return Results.Json(new { ok = false, error = "FEATURE_DISABLED" });
    var (ok, err, room) = svc.Rush.Join(req.RoomId, p);
    return Results.Json(new { ok, error = err, data = room != null ? new { roomId = (int)room.RoomId } : null });
});

// ---------- 镜像对决 ----------
app.MapPost("/api/duel/join", (HttpRequest http, GameService svc) =>
{
    var p = Auth(http);
    var deny = Need(p); if (deny != null) return deny;
    if (!Features.IsOn(Features.Duel)) return Results.Json(new { ok = false, error = "FEATURE_DISABLED" });
    var (ok, err, room) = svc.Duel.Join(p);
    return Results.Json(new { ok, error = err, data = room != null ? new { duelId = room.DuelId } : null });
});

// 原生 WebSocket：/ws/rush?roomId=1&token=xxx 或 /ws/duel?token=xxx
app.UseWebSockets();
app.Use(async (context, next) =>
{
    if (context.Request.Path == "/ws/rush" && context.WebSockets.IsWebSocketRequest)
    {
        var token = context.Request.Query["token"].ToString();
        var store = context.RequestServices.GetRequiredService<GameStore>();
        var player = store.Validate(token);
        if (player == null) { context.Response.StatusCode = 401; return; }
        if (!Features.IsOn(Features.Rush)) { context.Response.StatusCode = 403; return; }
        if (!int.TryParse(context.Request.Query["roomId"], out int roomId)) { context.Response.StatusCode = 400; return; }

        var svc = context.RequestServices.GetRequiredService<GameService>();
        var room = svc.Rush.GetRoom(roomId);
        if (room == null) { context.Response.StatusCode = 404; return; }

        var ws = await context.WebSockets.AcceptWebSocketAsync();
        room.AttachSocket(player.Id, ws);
        var buffer = new byte[4096];
        try
        {
            while (ws.State == WebSocketState.Open)
            {
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close) break;
                string msg = Encoding.UTF8.GetString(buffer, 0, result.Count);
                var json = System.Text.Json.JsonDocument.Parse(msg).RootElement;
                if (json.GetProperty("type").GetString() == "claim")
                {
                    int slot = json.GetProperty("slot").GetInt32();
                    room.Claim(player.Id, slot);
                }
            }
        }
        catch { }
        finally
        {
            room.DetachSocket(player.Id);
        }
        return;
    }

    if (context.Request.Path == "/ws/duel" && context.WebSockets.IsWebSocketRequest)
    {
        var token = context.Request.Query["token"].ToString();
        var store = context.RequestServices.GetRequiredService<GameStore>();
        var player = store.Validate(token);
        if (player == null) { context.Response.StatusCode = 401; return; }
        if (!Features.IsOn(Features.Duel)) { context.Response.StatusCode = 403; return; }

        var svc = context.RequestServices.GetRequiredService<GameService>();
        var room = svc.Duel.FindRoomOf(player.Id);
        if (room == null) { context.Response.StatusCode = 404; return; }

        var ws = await context.WebSockets.AcceptWebSocketAsync();
        room.AttachSocket(player.Id, ws);
        var buffer = new byte[4096];
        try
        {
            while (ws.State == WebSocketState.Open)
            {
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close) break;
                string msg = Encoding.UTF8.GetString(buffer, 0, result.Count);
                var json = System.Text.Json.JsonDocument.Parse(msg).RootElement;
                switch (json.GetProperty("type").GetString())
                {
                    case "eliminate":
                        room.Eliminate(player.Id, json.GetProperty("slot").GetInt32());
                        break;
                    case "skill":
                    {
                        int skillType = json.GetProperty("skillType").GetInt32();
                        int? slotIndex = json.TryGetProperty("slotIndex", out var sv) && sv.ValueKind == System.Text.Json.JsonValueKind.Number ? sv.GetInt32() : null;
                        room.Skill(player.Id, skillType, slotIndex);
                        break;
                    }
                    case "sell": room.Sell(player.Id); break;
                    case "accept": room.AcceptOffer(player.Id); break;
                    case "decline": room.DeclineOffer(player.Id); break;
                }
            }
        }
        catch { }
        finally
        {
            room.DetachSocket(player.Id);
        }
        return;
    }

    await next();
});

// ---------- 分享 ----------
app.MapGet("/api/share/content", (HttpRequest http, GameService svc, string contentId = "default") =>
{
    var p = Auth(http);
    var deny = Need(p); if (deny != null) return deny;
    return Results.Json(new { ok = true, data = svc.ShareContent(p, contentId) });
});

app.MapPost("/api/share/report", (ShareReq req, HttpRequest http, GameService svc) =>
{
    var p = Auth(http);
    var deny = Need(p); if (deny != null) return deny;
    svc.ReportShare(p, req.Channel, req.ContentId);
    return Results.Json(new { ok = true });
});

app.MapPost("/api/chest/{uid}/sell", (long uid, HttpRequest http, GameService svc) =>
{
    var p = Auth(http);
    var deny = Need(p); if (deny != null) return deny;
    var (ok, err, data) = svc.SellToBanker(p, uid);
    return Results.Json(new { ok, error = err, data });
});

// ---------- 银行家报价 ----------
app.MapPost("/api/chest/{uid}/offer/accept", (long uid, HttpRequest http, GameService svc) =>
{
    var p = Auth(http);
    var deny = Need(p); if (deny != null) return deny;
    var (ok, err, data) = svc.AcceptOffer(p, uid);
    return Results.Json(new { ok, error = err, data });
});

app.MapPost("/api/chest/{uid}/offer/decline", (long uid, HttpRequest http, GameService svc) =>
{
    var p = Auth(http);
    var deny = Need(p); if (deny != null) return deny;
    var (ok, err, data) = svc.DeclineOffer(p, uid);
    return Results.Json(new { ok, error = err, data });
});

// ---------- 每日挑战 ----------
app.MapPost("/api/daily/buy", (HttpRequest http, GameService svc) =>
{
    var p = Auth(http);
    var deny = Need(p); if (deny != null) return deny;
    if (!Features.IsOn(Features.Daily)) return Results.Json(new { ok = false, error = "FEATURE_DISABLED" });
    var (ok, err, data) = svc.BuyDailyChest(p);
    return Results.Json(new { ok, error = err, data });
});

app.MapGet("/api/daily/rank", (HttpRequest http, GameService svc) =>
{
    var p = Auth(http);
    var deny = Need(p); if (deny != null) return deny;
    if (!Features.IsOn(Features.Daily)) return Results.Json(new { ok = false, error = "FEATURE_DISABLED" });
    return Results.Json(new { ok = true, data = svc.DailyRank(p) });
});

// ---------- 拍卖 ----------
app.MapGet("/api/auction", (HttpRequest http, GameService svc, GameStore store) =>
{
    var p = Auth(http);
    if (!Features.IsOn(Features.Auction)) return Results.Json(new { ok = false, error = "FEATURE_DISABLED" });
    store.EnsureBotLots();
    return Results.Json(new { ok = true, data = svc.AuctionList(p) });
});

app.MapPost("/api/auction/list", (ListReq req, HttpRequest http, GameService svc) =>
{
    var p = Auth(http);
    var deny = Need(p); if (deny != null) return deny;
    if (!Features.IsOn(Features.Auction)) return Results.Json(new { ok = false, error = "FEATURE_DISABLED" });
    var (ok, err, data) = svc.ListChest(p, req.ChestUid, req.StartPrice, req.BuyoutPrice);
    return Results.Json(new { ok, error = err, data });
});

app.MapPost("/api/auction/{lotId}/bid", (long lotId, HttpRequest http, GameService svc) =>
{
    var p = Auth(http);
    var deny = Need(p); if (deny != null) return deny;
    if (!Features.IsOn(Features.Auction)) return Results.Json(new { ok = false, error = "FEATURE_DISABLED" });
    var (ok, err, data) = svc.Bid(p, lotId);
    return Results.Json(new { ok, error = err, data });
});

app.MapPost("/api/auction/{lotId}/buyout", (long lotId, HttpRequest http, GameService svc) =>
{
    var p = Auth(http);
    var deny = Need(p); if (deny != null) return deny;
    if (!Features.IsOn(Features.Auction)) return Results.Json(new { ok = false, error = "FEATURE_DISABLED" });
    var (ok, err, data) = svc.Buyout(p, lotId);
    return Results.Json(new { ok, error = err, data });
});

app.MapHub<AuctionHub>("/hubs/auction");
app.Run();

// ---------- 请求模型 ----------
public record LoginReq(string Name, string Region = null);
public record ShareReq(string Channel, string ContentId = "");
public record RushJoinReq(int RoomId);
public record CharacterReq(int CharacterId);
public record BuyReq(int ConfigId);
public record EliminateReq(int SlotIndex);
public record SkillReq(int SkillType, int? SlotIndex = null);
public record ListReq(long ChestUid, long StartPrice, long BuyoutPrice);

namespace SimpleGameServer.Game
{
    /// <summary>后台服务：机器人竞价 + 到期结算 + SignalR 推送桥</summary>
    public class AuctionBotService : BackgroundService
    {
        private readonly GameService _svc;
        private readonly AuctionEvents _events;
        private readonly IHubContext<Hubs.AuctionHub> _hub;

        public AuctionBotService(GameService svc, AuctionEvents events, IHubContext<Hubs.AuctionHub> hub)
        {
            _svc = svc;
            _events = events;
            _hub = hub;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _events.OnLotChanged = async lot =>
            {
                await _hub.Clients.All.SendAsync("LotChanged", new { lotId = lot.LotId, currentBid = lot.CurrentBid, currentBidder = lot.CurrentBidderName }, stoppingToken);
            };
            _events.OnLotSettled = async lot =>
            {
                await _hub.Clients.All.SendAsync("LotSettled", new { lotId = lot.LotId, price = lot.CurrentBid, winner = lot.CurrentBidderName }, stoppingToken);
            };

            while (!stoppingToken.IsCancellationRequested)
            {
                _svc.BotTick();
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
        }
    }
}

namespace SimpleGameServer
{
    /// <summary>地区解析（优先级：CDN 头 > 客户端上报 > GeoIP 扩展点 > 默认）</summary>
    public static class RegionResolver
    {
        public static string Resolve(HttpContext ctx, string clientHint)
        {
            // 1) 反代/CDN 注入的国家码（部署到 CloudFlare/腾讯云 CDN 后自动生效）
            var cf = ctx.Request.Headers["CF-IPCountry"].FirstOrDefault();
            if (!string.IsNullOrEmpty(cf)) return cf.ToString().ToUpperInvariant();
            var xc = ctx.Request.Headers["X-Country-Code"].FirstOrDefault();
            if (!string.IsNullOrEmpty(xc)) return xc.ToString().ToUpperInvariant();
            // 2) 客户端上报（SDK 定位/玩家自选）
            if (!string.IsNullOrEmpty(clientHint)) return clientHint.ToUpperInvariant();
            // 3) TODO: MaxMind GeoLite2 等离线 IP 库：按 ctx.Connection.RemoteIpAddress 解析
            //    dotnet add package MaxMind.GeoIP2 && using var reader = new DatabaseReader("GeoLite2-Country.mmdb")
            return "CN";
    }
}

}
