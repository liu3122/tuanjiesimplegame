using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Luban.SimpleJSON;
using UnityEngine;

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
        public static async Task<bool> LoginAsync()
        {
            var r = await PostAsync("/api/login", $"{{\"name\":\"{PlayerName}\"}}");
            if (r != null && r["ok"].AsBool)
            {
                _token = r["token"].Value;
                return true;
            }
            Debug.LogError("[ApiClient] 登录失败：" + (r == null ? "no response" : r.ToString()));
            return false;
        }

        // ---------------- API 封装（返回 JSONNode，调用方按需取字段） ----------------

        public static Task<JSONNode> GetStateAsync() => GetAsync("/api/state");

        public static Task<JSONNode> SelectCharacterAsync(int characterId) =>
            PostAsync("/api/character", $"{{\"characterId\":{characterId}}}");

        public static Task<JSONNode> BuyChestAsync(int configId) =>
            PostAsync("/api/chest/buy", $"{{\"configId\":{configId}}}");

        public static Task<JSONNode> EliminateAsync(long chestUid, int slotIndex) =>
            PostAsync($"/api/chest/{chestUid}/eliminate", $"{{\"slotIndex\":{slotIndex}}}");

        public static Task<JSONNode> UseSkillAsync(long chestUid, int skillType) =>
            PostAsync($"/api/chest/{chestUid}/skill", $"{{\"skillType\":{skillType}}}");

        public static Task<JSONNode> GetRankAsync() => GetAsync("/api/rank");

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

        private static async Task<JSONNode> GetAsync(string path)
        {
            return await SendAsync(new HttpRequestMessage(HttpMethod.Get, BaseUrl + path));
        }

        private static async Task<JSONNode> PostAsync(string path, string jsonBody)
        {
            var msg = new HttpRequestMessage(HttpMethod.Post, BaseUrl + path)
            {
                Content = new StringContent(jsonBody, Encoding.UTF8, "application/json"),
            };
            return await SendAsync(msg);
        }

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
    }
}
