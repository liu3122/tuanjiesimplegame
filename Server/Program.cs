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
builder.Services.AddSingleton<FriendService>();
builder.Services.AddHostedService<AuctionBotService>();
builder.Services.AddHostedService<SeasonService>();
builder.Services.AddHostedService<DbWriterService>();
// CORS：WebGL/小游戏浏览器运行时需要放开来源；不使用 cookie 凭证（鉴权走 Bearer 头），不放大攻击面
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyHeader().AllowAnyMethod().SetIsOriginAllowed(_ => true)));

var app = builder.Build();
app.UseCors();

// 配置表加载（与客户端共用 Assets/AssetRaw/Config）
GameTables.Load(app.Configuration["GameDataDir"] ?? "../Assets/AssetRaw/Config");

// SQLite 初始化 + 启动恢复（重启数据存活）
Db.Initialize(app.Configuration["GameDb"] ?? "simplegame.db");
var store0 = app.Services.GetRequiredService<GameStore>();
Db.LoadAll(store0);
store0.RestoreIdCursors();
Db.LoadOpenCounts(store0);      // 开箱次数内存化（热路径免 SQL COUNT）
Db.LoadSeasonRewards(store0);   // 赛季奖励缓存内存化
Db.RefundOrphanStakes(store0);  // 房间随进程消失：退还抢宝门票/对决入场费
PayService.LoadConfig(app.Configuration);  // 内购：沙箱开关 + 各店凭证（配置键见 PayService）
Db.LoadPayOrders(PayService.Restore);      // 恢复订单簿（paid 订单防重启后收据重放）
// 好友系统：关系/申请恢复 + 私人房超时覆盖（联调/测试用 --FriendRoomTimeoutSec 8）
var friends = app.Services.GetRequiredService<FriendService>();
Db.LoadFriends(friends.LoadLink, friends.LoadReq);
if (int.TryParse(app.Configuration["FriendRoomTimeoutSec"], out var friendTimeout))
{
    RoomOptions.PrivateTimeoutSec = friendTimeout;
    app.Logger.LogInformation("私人房解散超时覆盖：{Sec}s", friendTimeout);
}
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
    if (string.IsNullOrWhiteSpace(req?.Name)) return Results.Json(new { ok = false, error = "BAD_NAME" });
    var nameErr = SensitiveFilter.ValidateName(req.Name);   // 敏感词/合法性：服务端权威拦截
    if (nameErr != null) return Results.Json(new { ok = false, error = nameErr });
    var (player, err) = store.LoginOrRegister(req.Name.Trim(), req.Secret);
    if (player == null) return Results.Json(new { ok = false, error = err }); // WRONG_SECRET：知道昵称不再等于拿到账号
    player.Region = RegionResolver.Resolve(ctx, req.Region);
    Db.SavePlayer(player);
    return Results.Json(new { ok = true, token = player.Token, secret = player.Secret, playerId = player.Id, name = player.Name, gold = player.Gold, region = player.Region });
});

app.MapPost("/api/character", (CharacterReq req, HttpRequest http, GameStore store) =>
{
    var p = Auth(http);
    var deny = Need(p); if (deny != null) return deny;
    if (GameTables.Tables.TbCharacter.DataList.All(c => c.Id != (req?.CharacterId ?? 0))) return Results.Json(new { ok = false, error = "BAD_CHARACTER" });
    lock (p.Sync) p.CharacterId = req.CharacterId;
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
    var (ok, err, data) = svc.BuyChest(p, req?.ConfigId ?? 0);
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

// 广告发奖（激励视频：客户端 SDK 播放完成回调后调用；频控在服务端）
app.MapPost("/api/referral/bind", (ReferralReq req, HttpRequest http, GameService svc) =>
{
    var p = Auth(http);
    var deny = Need(p); if (deny != null) return deny;
    var (ok, err, data) = svc.BindReferral(p, req?.Code);
    return Results.Json(new { ok, error = err, data });
});

app.MapPost("/api/item/buy", (ItemReq req, HttpRequest http, GameService svc) =>
{
    var p = Auth(http);
    var deny = Need(p); if (deny != null) return deny;
    var (ok, err, data) = svc.BuyItem(p, req?.ItemId ?? 0, req?.Arg);
    return Results.Json(new { ok, error = err, data });
});

app.MapPost("/api/sign", (SignReq req, HttpRequest http, GameService svc) =>
{
    var p = Auth(http);
    var deny = Need(p); if (deny != null) return deny;
    var (ok, err, data) = svc.SignIn(p, req?.Double == true);
    return Results.Json(new { ok, error = err, data });
});

app.MapPost("/api/quest/claim", (QuestReq req, HttpRequest http, GameService svc) =>
{
    var p = Auth(http);
    var deny = Need(p); if (deny != null) return deny;
    var (ok, err, data) = svc.ClaimQuest(p, req?.QuestId, req?.Double == true);
    return Results.Json(new { ok, error = err, data });
});

app.MapPost("/api/ad/reward", (AdReq req, HttpRequest http, GameService svc) =>
{
    var p = Auth(http);
    var deny = Need(p); if (deny != null) return deny;
    var (ok, err, data) = svc.AdReward(p, req?.Placement);
    return Results.Json(new { ok, error = err, data });
});

// ---------- 内购（steam/ios/google；小游戏无内购：编译期无支付路径，wechat 渠道此处兜底拒绝） ----------
app.MapGet("/api/pay/skus", (HttpRequest http, string channel = "editor") =>
{
    var p = Auth(http);
    var deny = Need(p); if (deny != null) return deny;
    if (!Features.IsOn(Features.Iap)) return Results.Json(new { ok = false, error = "FEATURE_DISABLED" });
    var (ok, err, data) = PayService.Skus(channel);
    return Results.Json(new { ok, error = err, data });
});

app.MapPost("/api/pay/order", (PayOrderReq req, HttpRequest http) =>
{
    var p = Auth(http);
    var deny = Need(p); if (deny != null) return deny;
    if (!Features.IsOn(Features.Iap)) return Results.Json(new { ok = false, error = "FEATURE_DISABLED" });
    var (ok, err, data) = PayService.CreateOrder(p, req?.SkuId, req?.Channel);
    return Results.Json(new { ok, error = err, data });
});

app.MapPost("/api/pay/verify", async (PayVerifyReq req, HttpRequest http) =>
{
    var p = Auth(http);
    var deny = Need(p); if (deny != null) return deny;
    if (!Features.IsOn(Features.Iap)) return Results.Json(new { ok = false, error = "FEATURE_DISABLED" });
    var (ok, err, data) = await PayService.VerifyOrderAsync(p, req?.OrderNo, req?.Receipt);
    return Results.Json(new { ok, error = err, data });
});

// ---------- 钻石兑换（软硬双币单向闸门：钻石 → 金币） ----------
app.MapPost("/api/gem/exchange", (GemExchangeReq req, HttpRequest http, GameService svc) =>
{
    var p = Auth(http);
    var deny = Need(p); if (deny != null) return deny;
    var (ok, err, data) = svc.ExchangeGems(p, req?.Gems ?? 0);
    return Results.Json(new { ok, error = err, data });
});

// ---------- 宅邸改造（金币指数回收，梦幻家园式工程链） ----------
app.MapPost("/api/manor/build", (ManorBuildReq req, HttpRequest http, GameService svc) =>
{
    var p = Auth(http);
    var deny = Need(p); if (deny != null) return deny;
    if (!Features.IsOn(Features.Manor)) return Results.Json(new { ok = false, error = "FEATURE_DISABLED" });
    var (ok, err, data) = svc.ManorBuild(p, req?.Choice ?? 0);
    return Results.Json(new { ok, error = err, data });
});

// 经济报表（管理端）：X-Admin-Token 头校验，token 来自配置 AdminToken。
// 未配置时：Development 沿用 dev-admin（本地联调），生产环境直接 404 关闭（防止忘配即裸奔）
app.MapGet("/api/admin/economy", (HttpRequest http) =>
{
    string expect = app.Configuration["AdminToken"]
        ?? (app.Environment.IsDevelopment() ? "dev-admin" : null);
    if (expect == null) return Results.NotFound();
    string token = http.Headers["X-Admin-Token"].ToString();
    if (token != expect) return Results.Json(new { ok = false, error = "FORBIDDEN" }, statusCode: 403);
    return Results.Json(new { ok = true, data = Db.EconomyReport() });
});

app.MapPost("/api/chest/{uid}/eliminate", (long uid, EliminateReq req, HttpRequest http, GameService svc) =>
{
    var p = Auth(http);
    var deny = Need(p); if (deny != null) return deny;
    var (ok, err, data) = svc.Eliminate(p, uid, req?.SlotIndex ?? -1);
    return Results.Json(new { ok, error = err, data });
});

app.MapPost("/api/chest/{uid}/skill", (long uid, SkillReq req, HttpRequest http, GameService svc) =>
{
    var p = Auth(http);
    var deny = Need(p); if (deny != null) return deny;
    var (ok, err, data) = svc.UseSkill(p, uid, req?.SkillType ?? 0, req?.SlotIndex);
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

// 对决天梯榜（Elo）
app.MapGet("/api/duel/rank", (HttpRequest http, GameService svc) =>
{
    var p = Auth(http);
    var deny = Need(p); if (deny != null) return deny;
    return Results.Json(new { ok = true, data = svc.DuelRankList(p) });
});

// ---------- 好友系统 ----------
app.MapPost("/api/friend/request", (FriendReq req, HttpRequest http, FriendService svc) =>
{
    var p = Auth(http);
    var deny = Need(p); if (deny != null) return deny;
    if (!Features.IsOn(Features.Friend)) return Results.Json(new { ok = false, error = "FEATURE_DISABLED" });
    var (ok, err, data) = svc.Request(p, req?.Code);
    return Results.Json(new { ok, error = err, data });
});

app.MapPost("/api/friend/accept", (FriendTargetReq req, HttpRequest http, FriendService svc) =>
{
    var p = Auth(http);
    var deny = Need(p); if (deny != null) return deny;
    if (!Features.IsOn(Features.Friend)) return Results.Json(new { ok = false, error = "FEATURE_DISABLED" });
    var (ok, err, data) = svc.Accept(p, req?.FromId ?? 0);
    return Results.Json(new { ok, error = err, data });
});

app.MapPost("/api/friend/reject", (FriendTargetReq req, HttpRequest http, FriendService svc) =>
{
    var p = Auth(http);
    var deny = Need(p); if (deny != null) return deny;
    if (!Features.IsOn(Features.Friend)) return Results.Json(new { ok = false, error = "FEATURE_DISABLED" });
    var (ok, err, data) = svc.Reject(p, req?.FromId ?? 0);
    return Results.Json(new { ok, error = err, data });
});

app.MapPost("/api/friend/remove", (FriendTargetReq req, HttpRequest http, FriendService svc) =>
{
    var p = Auth(http);
    var deny = Need(p); if (deny != null) return deny;
    if (!Features.IsOn(Features.Friend)) return Results.Json(new { ok = false, error = "FEATURE_DISABLED" });
    var (ok, err, data) = svc.Remove(p, req?.FromId ?? 0);
    return Results.Json(new { ok, error = err, data });
});

app.MapGet("/api/friend/list", (HttpRequest http, FriendService svc) =>
{
    var p = Auth(http);
    var deny = Need(p); if (deny != null) return deny;
    if (!Features.IsOn(Features.Friend)) return Results.Json(new { ok = false, error = "FEATURE_DISABLED" });
    return Results.Json(new { ok = true, data = svc.List(p) });
});

/// <summary>大厅轻量轮询：申请数 + 房间邀请（读即消费）</summary>
app.MapGet("/api/friend/inbox", (HttpRequest http, FriendService svc) =>
{
    var p = Auth(http);
    var deny = Need(p); if (deny != null) return deny;
    if (!Features.IsOn(Features.Friend)) return Results.Json(new { ok = false, error = "FEATURE_DISABLED" });
    return Results.Json(new { ok = true, data = svc.Inbox(p) });
});

/// <summary>房主推送私人房邀请（仅好友）</summary>
app.MapPost("/api/friend/invite", (FriendInviteReq req, HttpRequest http, FriendService svc) =>
{
    var p = Auth(http);
    var deny = Need(p); if (deny != null) return deny;
    if (!Features.IsOn(Features.Friend)) return Results.Json(new { ok = false, error = "FEATURE_DISABLED" });
    var (ok, err) = svc.Invite(p, req?.ToId ?? 0, req?.Code, string.IsNullOrEmpty(req?.Mode) ? "duel" : req.Mode);
    return Results.Json(new { ok, error = err });
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
    // 带 Code = 凭码加入好友私人房；否则按档位进公共房
    if (!string.IsNullOrEmpty(req?.Code))
    {
        var (pok, perr, proom) = svc.Rush.JoinPrivate(req.Code, p);
        return Results.Json(new { ok = pok, error = perr, data = proom != null ? new { roomCode = proom.RoomCode } : null });
    }
    var (ok, err, room) = svc.Rush.Join(req?.RoomId ?? 0, p);
    return Results.Json(new { ok, error = err, data = room != null ? new { roomId = (int)room.RoomId } : null });
});

/// <summary>创建好友私人抢宝房（档位由房主选择），返回房间码</summary>
app.MapPost("/api/rush/private", (RushJoinReq req, HttpRequest http, GameService svc) =>
{
    var p = Auth(http);
    var deny = Need(p); if (deny != null) return deny;
    if (!Features.IsOn(Features.Rush) || !Features.IsOn(Features.Friend)) return Results.Json(new { ok = false, error = "FEATURE_DISABLED" });
    var (ok, err, room, code) = svc.Rush.CreatePrivate(p, req?.RoomId ?? 0);
    return Results.Json(new { ok, error = err, data = code != null ? new { roomCode = code } : null });
});

// ---------- 镜像对决 / 竞宝房 ----------
app.MapPost("/api/duel/join", (DuelJoinReq req, HttpRequest http, GameService svc) =>
{
    var p = Auth(http);
    var deny = Need(p); if (deny != null) return deny;
    bool potMode = string.Equals(req?.Mode, "pot", StringComparison.OrdinalIgnoreCase) || (req?.Pot == true); // friend 模式带 pot=true = 好友竞宝房
    bool friendMode = string.Equals(req?.Mode, "friend", StringComparison.OrdinalIgnoreCase);
    if (!Features.IsOn(potMode ? Features.PotDuel : Features.Duel)) return Results.Json(new { ok = false, error = "FEATURE_DISABLED" });
    if (friendMode)
    {
        // 好友私人房：无 Code=建房（返回房码）；有 Code=凭码加入
        if (!Features.IsOn(Features.Friend)) return Results.Json(new { ok = false, error = "FEATURE_DISABLED" });
        if (string.IsNullOrEmpty(req.Code))
        {
            var (cok, cerr, croom, code) = svc.Duel.CreatePrivate(p, potMode);
            return Results.Json(new { ok = cok, error = cerr, data = code != null ? new { roomCode = code, duelId = croom!.DuelId, potMode = croom.PotMode } : null });
        }
        var (jok, jerr, jroom) = svc.Duel.JoinPrivate(req.Code, p);
        return Results.Json(new { ok = jok, error = jerr, data = jroom != null ? new { roomCode = jroom.RoomCode, duelId = jroom.DuelId, potMode = jroom.PotMode } : null });
    }
    var (ok, err, room) = svc.Duel.Join(p, potMode);
    return Results.Json(new { ok, error = err, data = room != null ? new { duelId = room.DuelId, potMode = room.PotMode } : null });
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
        string roomIdRaw = context.Request.Query["roomId"].ToString();

        var svc = context.RequestServices.GetRequiredService<GameService>();
        // roomId 兼容两种取值：公共档位 Id（1~4）或好友私人房 6 位房间码（100000+，数字可整型解析，须先查档位再回退码）
        RushRoom? room = null;
        if (int.TryParse(roomIdRaw, out int tierId))
        {
            room = svc.Rush.GetRoom(tierId);
        }
        room ??= svc.Rush.GetPrivate(roomIdRaw);
        if (room == null) { context.Response.StatusCode = 404; return; }
        if (!room.HasPlayer(player.Id)) { context.Response.StatusCode = 403; return; } // 未付门票不入流（防白嫖金额分布广播）

        var ws = await context.WebSockets.AcceptWebSocketAsync();
        room.AttachSocket(player.Id, ws);
        try
        {
            while (ws.State == WebSocketState.Open)
            {
                var msg = await ReceiveWsMessage(ws, context.RequestAborted);
                if (msg == null) break;
                if (!TryParseWsJson(msg, out var json)) continue; // 单条坏消息不断连（旧实现解析异常直接踢连接）
                switch (json.GetProperty("type").GetString())
                {
                    case "claim":
                        int slot = json.GetProperty("slot").GetInt32();
                        room.Claim(player.Id, slot);
                        break;
                    case "start": // 好友房房主提前开局（不足满员）
                        room.TryStart(player.Id);
                        break;
                }
            }
        }
        catch { }
        finally
        {
            room.DetachSocket(player.Id, ws);
        }
        return;
    }

    if (context.Request.Path == "/ws/duel" && context.WebSockets.IsWebSocketRequest)
    {
        var token = context.Request.Query["token"].ToString();
        var store = context.RequestServices.GetRequiredService<GameStore>();
        var player = store.Validate(token);
        if (player == null) { context.Response.StatusCode = 401; return; }

        var svc = context.RequestServices.GetRequiredService<GameService>();
        var room = svc.Duel.FindRoomOf(player.Id);
        if (room == null) { context.Response.StatusCode = 404; return; }
        if (!Features.IsOn(room.PotMode ? Features.PotDuel : Features.Duel)) { context.Response.StatusCode = 403; return; }

        var ws = await context.WebSockets.AcceptWebSocketAsync();
        room.AttachSocket(player.Id, ws);
        try
        {
            while (ws.State == WebSocketState.Open)
            {
                var msg = await ReceiveWsMessage(ws, context.RequestAborted);
                if (msg == null) break;
                if (!TryParseWsJson(msg, out var json)) continue;
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
            room.DetachSocket(player.Id, ws);
        }
        return;
    }

    await next();
});

/// <summary>收一条完整 WS 消息：拼分帧（endOfMessage=false 的 continuation），上限 64KB，Close/连接中止返回 null</summary>
static async Task<byte[]?> ReceiveWsMessage(WebSocket ws, CancellationToken ct)
{
    using var buf = new MemoryStream();
    var chunk = new byte[8192];
    while (true)
    {
        var result = await ws.ReceiveAsync(new ArraySegment<byte>(chunk), ct);
        if (result.MessageType == WebSocketMessageType.Close) return null;
        buf.Write(chunk, 0, result.Count);
        if (buf.Length > 64 * 1024) return null; // 超限：按断连处理（防恶意大消息耗内存）
        if (result.EndOfMessage) return buf.ToArray();
    }
}

static bool TryParseWsJson(byte[] msg, out System.Text.Json.JsonElement json)
{
    try
    {
        json = System.Text.Json.JsonDocument.Parse(msg).RootElement;
        return true;
    }
    catch (System.Text.Json.JsonException)
    {
        json = default;
        return false;
    }
}

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
    svc.ReportShare(p, req?.Channel ?? "", req?.ContentId ?? "");
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
    var deny = Need(p); if (deny != null) return deny; // 此前全文件唯一漏鉴权端点（拍卖行含玩家昵称/出价分布）
    if (!Features.IsOn(Features.Auction)) return Results.Json(new { ok = false, error = "FEATURE_DISABLED" });
    store.EnsureBotLots();
    return Results.Json(new { ok = true, data = svc.AuctionList(p) });
});

app.MapPost("/api/auction/list", (ListReq req, HttpRequest http, GameService svc) =>
{
    var p = Auth(http);
    var deny = Need(p); if (deny != null) return deny;
    if (!Features.IsOn(Features.Auction)) return Results.Json(new { ok = false, error = "FEATURE_DISABLED" });
    var (ok, err, data) = svc.ListChest(p, req?.ChestUid ?? 0, req?.StartPrice ?? 0, req?.BuyoutPrice ?? 0);
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
public record LoginReq(string Name, string Secret = null, string Region = null);
public record ShareReq(string Channel, string ContentId = "");
public record RushJoinReq(int RoomId, string Code = null);
public record DuelJoinReq(string Mode = "stake", string Code = null, bool Pot = false);
public record FriendReq(string Code);
public record FriendTargetReq(long? FromId);
public record FriendInviteReq(long? ToId, string Code, string Mode = "duel");
public record AdReq(string Placement);
public record QuestReq(string QuestId, bool Double = false);
public record SignReq(bool Double = false);
public record ItemReq(int ItemId, string Arg = null);
public record ReferralReq(string Code);
public record CharacterReq(int CharacterId);
public record BuyReq(int ConfigId);
public record EliminateReq(int SlotIndex);
public record SkillReq(int SkillType, int? SlotIndex = null);
public record ListReq(long ChestUid, long StartPrice, long BuyoutPrice);
public record PayOrderReq(string SkuId, string Channel);
public record PayVerifyReq(string OrderNo, string Receipt);
public record GemExchangeReq(long Gems);
public record ManorBuildReq(int Choice);

namespace SimpleGameServer.Game
{
    /// <summary>后台服务：机器人竞价 + 到期结算 + SignalR 推送桥</summary>
    /// <summary>Db 后台单写者宿主：启动写循环；停机时取消并等循环排空剩余写（保证落盘再退出）</summary>
    public class DbWriterService : BackgroundService
    {
        // Task.Run 不带 token：若启动即取消，带 token 会跳过委托导致停机排空逻辑不执行（队列余写丢失）
        protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.Run(() => Db.RunWriterLoop(stoppingToken));

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            await base.StopAsync(cancellationToken); // 取消 token → RunWriterLoop 排空后返回
        }
    }

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
                try { await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken); }
                catch (OperationCanceledException) { break; } // 停机取消：勿让 OCE 冒泡拖垮宿主
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
