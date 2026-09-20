using Microsoft.AspNetCore.SignalR;
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
app.MapPost("/api/login", (LoginReq req, GameStore store) =>
{
    if (string.IsNullOrWhiteSpace(req.Name)) return Results.Json(new { ok = false, error = "BAD_NAME" });
    var player = store.LoginOrRegister(req.Name.Trim());
    return Results.Json(new { ok = true, token = player.Token, playerId = player.Id, name = player.Name, gold = player.Gold });
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

app.MapGet("/api/rank", (HttpRequest http, GameService svc) =>
{
    var p = Auth(http);
    var deny = Need(p); if (deny != null) return deny;
    return Results.Json(new { ok = true, data = svc.RankList(p) });
});

app.MapPost("/api/chest/{uid}/sell", (long uid, HttpRequest http, GameService svc) =>
{
    var p = Auth(http);
    var deny = Need(p); if (deny != null) return deny;
    var (ok, err, data) = svc.SellToBanker(p, uid);
    return Results.Json(new { ok, error = err, data });
});

// ---------- 拍卖 ----------
app.MapGet("/api/auction", (HttpRequest http, GameService svc, GameStore store) =>
{
    var p = Auth(http);
    store.EnsureBotLots();
    return Results.Json(new { ok = true, data = svc.AuctionList(p) });
});

app.MapPost("/api/auction/list", (ListReq req, HttpRequest http, GameService svc) =>
{
    var p = Auth(http);
    var deny = Need(p); if (deny != null) return deny;
    var (ok, err, data) = svc.ListChest(p, req.ChestUid, req.StartPrice, req.BuyoutPrice);
    return Results.Json(new { ok, error = err, data });
});

app.MapPost("/api/auction/{lotId}/bid", (long lotId, HttpRequest http, GameService svc) =>
{
    var p = Auth(http);
    var deny = Need(p); if (deny != null) return deny;
    var (ok, err, data) = svc.Bid(p, lotId);
    return Results.Json(new { ok, error = err, data });
});

app.MapPost("/api/auction/{lotId}/buyout", (long lotId, HttpRequest http, GameService svc) =>
{
    var p = Auth(http);
    var deny = Need(p); if (deny != null) return deny;
    var (ok, err, data) = svc.Buyout(p, lotId);
    return Results.Json(new { ok, error = err, data });
});

app.MapHub<AuctionHub>("/hubs/auction");
app.Run();

// ---------- 请求模型 ----------
public record LoginReq(string Name);
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
