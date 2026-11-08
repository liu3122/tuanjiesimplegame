using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Luban.SimpleJSON;
using UnityEngine;
using UnityEngine.Networking;

namespace Game.HotUpdate.ChestGame
{
    /// <summary>
    /// 服务端 API 客户端（热更侧）：钱包/开箱/技能/拍卖全部服务端权威。
    /// 服务端启动：cd Server && dotnet run --urls http://127.0.0.1:5188
    /// </summary>
    public static class ApiClient
    {
        public const bool UseServer = true;

        public const string BaseUrl = "http://127.0.0.1:5188";

        private static HttpClient _http;
        private static string _token;
        public static bool LoggedIn => _token != null;
        public static string Token => _token;
        public static long PlayerId { get; set; }

        public static string PlayerName { get; set; } = "Player" + UnityEngine.Random.Range(1000, 9999);

        private static HttpClient Http
        {
            get
            {
                if (_http == null)
                {
                    _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                }
                return _http;
            }
        }

        /// <summary>登录并保存 token，返回是否成功</summary>
        /// <summary>地区码上报（接 SDK 后替换为真实定位/玩家自选）</summary>
        public static string Region { get; set; } = "CN";

        public static async Task<bool> LoginAsync()
        {
            var r = await PostAsync("/api/login", $"{{\"name\":\"{PlayerName}\",\"region\":\"{Region}\"}}");
            if (r != null && r["ok"].AsBool)
            {
                _token = r["token"].Value;
                PlayerId = r["playerId"].AsLong;
                return true;
            }
            Debug.LogError(L10n.Get("[ApiClient] 登录失败：") + (r == null ? "no response" : r.ToString()));
            return false;
        }

        /// <summary>
        /// 平台身份登录（LoginBridge.LoginAsync 的服务端通道）：
        /// Guest/无身份 → 现行 /api/login 名称登录；
        /// 平台身份 → POST /api/auth/{platform}（服务端验签 Steam 票据/微信 code/Apple token 后发同一套 token，端点待服务端实现）。
        /// </summary>
        public static async Task<bool> LoginWithIdentityAsync(PlatformIdentity identity)
        {
            if (identity == null || identity.Platform == PlatformId.Guest)
            {
                return await LoginAsync();
            }

            var body = $"{{\"platform\":\"{identity.Platform}\",\"platformUserId\":\"{identity.PlatformUserId}\",\"payload\":\"{identity.AuthPayload}\",\"name\":\"{PlayerName}\",\"region\":\"{Region}\"}}";
            var r = await PostAsync($"/api/auth/{identity.Platform.ToString().ToLowerInvariant()}", body);
            if (r != null && r["ok"].AsBool)
            {
                _token = r["token"].Value;
                PlayerId = r["playerId"].AsLong;
                return true;
            }
            Debug.LogError(L10n.Get("[ApiClient] 平台登录失败：") + (r == null ? "no response" : r.ToString()));
            return false;
        }

        // ---------------- API 封装（返回 JSONNode，调用方按需取字段） ----------------

        public static Task<JSONNode> GetStateAsync() => GetAsync("/api/state");

        public static Task<JSONNode> SelectCharacterAsync(int characterId) =>
            PostAsync("/api/character", $"{{\"characterId\":{characterId}}}");

        public static Task<JSONNode> BuyChestAsync(int configId) =>
            PostAsync("/api/chest/buy", $"{{\"configId\":{configId}}}");

        public static Task<JSONNode> TutorialChestAsync() => PostAsync("/api/tutorial/chest", "{}");

        public static Task<JSONNode> AdRewardAsync(string placement) =>
            PostAsync("/api/ad/reward", $"{{\"placement\":\"{placement}\"}}");

        public static Task<JSONNode> BindReferralAsync(string code) =>
            PostAsync("/api/referral/bind", $"{{\"code\":\"{code}\"}}");

        public static Task<JSONNode> BuyItemAsync(int itemId, string arg = null) =>
            PostAsync("/api/item/buy", arg != null
                ? $"{{\"itemId\":{itemId},\"arg\":\"{arg}\"}}"
                : $"{{\"itemId\":{itemId}}}");

        public static Task<JSONNode> SignAsync(bool doubleReward = false) =>
            PostAsync("/api/sign", $"{{\"double\":{(doubleReward ? "true" : "false")}}}");

        public static Task<JSONNode> ClaimQuestAsync(string questId, bool doubleReward = false) =>
            PostAsync("/api/quest/claim", $"{{\"questId\":\"{questId}\",\"double\":{(doubleReward ? "true" : "false")}}}");

        public static Task<JSONNode> EliminateAsync(long chestUid, int slotIndex) =>
            PostAsync($"/api/chest/{chestUid}/eliminate", $"{{\"slotIndex\":{slotIndex}}}");

        public static Task<JSONNode> UseSkillAsync(long chestUid, int skillType, int? slotIndex = null) =>
            PostAsync($"/api/chest/{chestUid}/skill",
                slotIndex.HasValue
                    ? $"{{\"skillType\":{skillType},\"slotIndex\":{slotIndex.Value}}}"
                    : $"{{\"skillType\":{skillType}}}");

        public static Task<JSONNode> GetRankAsync(string board = "gold_total", string scope = "global") =>
            GetAsync($"/api/rank?board={board}&scope={scope}");

        public static Task<JSONNode> GetDuelRankAsync() => GetAsync("/api/duel/rank");

        public static Task<JSONNode> GetRushRoomsAsync() => GetAsync("/api/rush/rooms");

        public static Task<JSONNode> JoinRushAsync(int roomId) =>
            PostAsync("/api/rush/join", $"{{\"roomId\":{roomId}}}");

        public static Task<JSONNode> JoinDuelAsync(bool potMode = false) =>
            PostAsync("/api/duel/join", potMode ? "{\"mode\":\"pot\"}" : "{}");

        public static Task<JSONNode> GetShareContentAsync(string contentId = "default") =>
            GetAsync($"/api/share/content?contentId={contentId}");

        public static Task<JSONNode> ShareReportAsync(string channel, string contentId) =>
            PostAsync("/api/share/report", $"{{\"channel\":\"{channel}\",\"contentId\":\"{contentId}\"}}");

        public static Task<JSONNode> AcceptOfferAsync(long chestUid) =>
            PostAsync($"/api/chest/{chestUid}/offer/accept", "{}");

        public static Task<JSONNode> DeclineOfferAsync(long chestUid) =>
            PostAsync($"/api/chest/{chestUid}/offer/decline", "{}");

        public static Task<JSONNode> BuyDailyAsync() => PostAsync("/api/daily/buy", "{}");

        public static Task<JSONNode> GetDailyRankAsync() => GetAsync("/api/daily/rank");

        public static Task<JSONNode> SellAsync(long chestUid) =>
            PostAsync($"/api/chest/{chestUid}/sell", "{}");

        public static Task<JSONNode> GetAuctionsAsync() => GetAsync("/api/auction");

        public static Task<JSONNode> ListAuctionAsync(long chestUid, long startPrice, long buyoutPrice) =>
            PostAsync("/api/auction/list", $"{{\"chestUid\":{chestUid},\"startPrice\":{startPrice},\"buyoutPrice\":{buyoutPrice}}}");

        public static Task<JSONNode> BidAsync(long lotId) =>
            PostAsync($"/api/auction/{lotId}/bid", "{}");

        public static Task<JSONNode> BuyoutAsync(long lotId) =>
            PostAsync($"/api/auction/{lotId}/buyout", "{}");

        // ---------------- 基础请求 ----------------

        private static Task<JSONNode> GetAsync(string path)
        {
#if UNITY_WEBGL
            return SendAsync("GET", BaseUrl + path, null);
#else
            return SendAsync(new HttpRequestMessage(HttpMethod.Get, BaseUrl + path));
#endif
        }

        private static Task<JSONNode> PostAsync(string path, string jsonBody)
        {
#if UNITY_WEBGL
            return SendAsync("POST", BaseUrl + path, jsonBody);
#else
            var msg = new HttpRequestMessage(HttpMethod.Post, BaseUrl + path)
            {
                Content = new StringContent(jsonBody, Encoding.UTF8, "application/json"),
            };
            return SendAsync(msg);
#endif
        }

#if UNITY_WEBGL
        /// <summary>
        /// WebGL/微信小游戏：HttpClient 不可用（System.Net sockets 无 WASM 实现，await 挂死），
        /// 改走 UnityWebRequest（经 WX SDK 的 XHR 补丁，与 YooAsset 拉清单同通道）。
        /// </summary>
        private static async Task<JSONNode> SendAsync(string method, string url, string jsonBody)
        {
            try
            {
                using (var req = new UnityWebRequest(url, method)
                {
                    downloadHandler = new DownloadHandlerBuffer(),
                    timeout = 10,
                })
                {
                    if (jsonBody != null)
                    {
                        req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
                        req.SetRequestHeader("Content-Type", "application/json");
                    }
                    if (_token != null)
                        req.SetRequestHeader("Authorization", "Bearer " + _token);
                    var op = req.SendWebRequest();
                    while (!op.isDone)
                        await Task.Yield();
                    if (req.result != UnityWebRequest.Result.Success)
                    {
                        Debug.LogError($"[ApiClient] {method} {url} 失败：{req.error} {req.downloadHandler?.text}");
                        return null;
                    }
                    return JSON.Parse(req.downloadHandler.text);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[ApiClient] {method} {url} 异常：{e.Message}");
                return null;
            }
        }
#else
        private static async Task<JSONNode> SendAsync(HttpRequestMessage msg)
        {
            try
            {
                if (_token != null)
                {
                    msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
                }
                using (var resp = await Http.SendAsync(msg))
                {
                    string body = await resp.Content.ReadAsStringAsync();
                    return JSON.Parse(body);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[ApiClient] {msg.Method} {msg.RequestUri.PathAndQuery} 失败：{e.Message}");
                return null;
            }
        }
#endif
    }
}
