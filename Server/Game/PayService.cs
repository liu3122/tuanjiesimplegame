using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using cfg;
using Microsoft.Extensions.Configuration;
using SimpleGameServer.Config;

namespace SimpleGameServer.Game
{
    /// <summary>
    /// 多平台内购（steam / ios / google；小游戏无内购：客户端编译期关闭支付路径，wechat 渠道在此再拒一次兜底）。
    /// 订单生命周期：CreateOrder(pending) → 客户端平台 SDK 拉起支付 → VerifyOrderAsync(平台验单 → paid + 发货)。
    /// 收据不可信客户端：验单/发货/流水全部服务端权威；paid 订单凭 receipt_hash 幂等（重放不重复发货）。
    /// 沙箱：PaySandbox=true（默认关）时接受编辑器 DevPayProvider 的 dev:{orderNo}:{skuId} 收据，本地全链路测试用。
    /// 真实商店验单（Steam 微交易 / Google Play Developer API / App Store Server API）纯 BCL 实现，
    /// 凭证从配置注入；上线前须用各店沙箱账号各过一轮联调（代码内 TODO 标注处）。
    /// </summary>
    public static class PayService
    {
        // ---------------- 配置（Program 启动时 LoadConfig 注入一次） ----------------

        public static bool Sandbox { get; private set; }
        public static string SteamAppId { get; private set; }
        public static string SteamKey { get; private set; }
        public static string GooglePackage { get; private set; }
        public static string GoogleClientEmail { get; private set; }
        public static string GooglePrivateKey { get; private set; }     // PKCS#8 PEM（服务账号私钥）
        public static string AppleBundleId { get; private set; }
        public static string AppleIssuer { get; private set; }          // App Store Connect Issuer ID
        public static string AppleKeyId { get; private set; }
        public static string ApplePrivateKey { get; private set; }      // PKCS#8 EC PEM（In-App Purchase 密钥）
        public static bool AppleUseSandbox { get; private set; }        // true = 沙箱环境 API 域名

        public static void LoadConfig(IConfiguration c)
        {
            Sandbox = bool.TryParse(c["PaySandbox"], out var sb) && sb;
            SteamAppId = c["PaySteamAppId"];
            SteamKey = c["PaySteamKey"];
            GooglePackage = c["PayGooglePackage"];
            GoogleClientEmail = c["PayGoogleClientEmail"];
            GooglePrivateKey = c["PayGooglePrivateKey"];
            AppleBundleId = c["PayAppleBundleId"];
            AppleIssuer = c["PayAppleIssuer"];
            AppleKeyId = c["PayAppleKeyId"];
            ApplePrivateKey = c["PayApplePrivateKey"];
            AppleUseSandbox = bool.TryParse(c["PayAppleUseSandbox"], out var asb) && asb;
        }

        // ---------------- 订单簿 ----------------

        /// <summary>全部订单（orderNo → order）。启动时从 pay_order 表恢复</summary>
        private static readonly ConcurrentDictionary<string, PayOrder> _orders = new();

        /// <summary>已支付收据哈希（跨订单重放防线：同一收据绝不允许付两单）</summary>
        private static readonly ConcurrentDictionary<string, string> _paidReceipts = new();

        /// <summary>Db.LoadPayOrders 回填：恢复全部订单（paid 订单的收据哈希同时回填重放防线）</summary>
        public static void Restore(PayOrder o)
        {
            _orders[o.OrderNo] = o;
            if (o.Status == PayOrderStatus.Paid && o.ReceiptHash != null)
                _paidReceipts[o.ReceiptHash] = o.OrderNo;
        }

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

        // ---------------- 对外业务（(ok, err, data) 签名与 GameService 一致） ----------------

        /// <summary>渠道合法性：steam/ios/google 常开；editor 仅沙箱；wechat（小游戏）产品决策不做内购</summary>
        private static string ChannelError(string channel)
        {
            if (string.IsNullOrEmpty(channel)) return "PAY_CHANNEL_REQUIRED";
            if (channel == "wechat" || channel == "weixinminigame") return "PAY_CHANNEL_DISABLED";
            if (channel == "editor") return Sandbox ? null : "PAY_CHANNEL_DISABLED";
            if (channel == "steam" || channel == "ios" || channel == "google") return null;
            return "PAY_CHANNEL_BAD";
        }

        private static cfg.recharge.Recharge FindSku(string skuId) =>
            GameTables.Tables.TbRecharge.DataList.FirstOrDefault(s => s.SkuId == skuId && s.Enabled);

        public static (bool ok, string err, object data) Skus(string channel)
        {
            var chErr = ChannelError(channel);
            if (chErr != null) return (false, chErr, null);
            var list = GameTables.Tables.TbRecharge.DataList
                .Where(s => s.Enabled)
                .OrderBy(s => s.Id)
                .Select(s => new { skuId = s.SkuId, gold = s.Gold, priceText = s.PriceText })
                .ToList();
            return (true, null, new { channel, skus = list });
        }

        public static (bool ok, string err, object data) CreateOrder(Player player, string skuId, string channel)
        {
            var chErr = ChannelError(channel);
            if (chErr != null) return (false, chErr, null);
            var sku = FindSku(skuId);
            if (sku == null) return (false, "SKU_NOT_FOUND", null);

            var order = new PayOrder
            {
                OrderNo = "PO" + Guid.NewGuid().ToString("N"),
                PlayerId = player.Id,
                SkuId = sku.SkuId,
                Channel = channel,
                Gold = sku.Gold,   // 建单时快照发货量，防后续改表串价
                Status = PayOrderStatus.Pending,
                CreatedUtc = DateTime.UtcNow,
            };
            _orders[order.OrderNo] = order;
            Db.SavePayOrder(order);
            // TODO(steam)：/api/auth/steam 落地拿到 steamid 后，真实 Steam 流程在此调
            //   ISteamMicroTxn/InitTxn(appid, orderid=OrderNo, steamid, 金额, sandbox)，客户端监听
            //   MicroTxnAuthorizationResponse → VerifyOrder；当前无 steamid，Steam 真机链路待登录端点接入。
            return (true, null, new { orderNo = order.OrderNo, skuId = sku.SkuId, gold = sku.Gold, priceText = sku.PriceText });
        }

        public static async Task<(bool ok, string err, object data)> VerifyOrderAsync(Player player, string orderNo, string receipt)
        {
            if (string.IsNullOrEmpty(orderNo) || !_orders.TryGetValue(orderNo, out var order))
                return (false, "ORDER_NOT_FOUND", null);
            if (order.PlayerId != player.Id) return (false, "ORDER_FORBIDDEN", null);
            if (string.IsNullOrEmpty(receipt) || receipt.Length > 4096) return (false, "BAD_RECEIPT", null);
            var hash = HashReceipt(receipt);

            // 1) 快速幂等：已 paid + 同收据 = 重复回调（客户端重试/掉线重发），直接返回成功不再发货
            lock (order.Sync)
            {
                if (order.Status == PayOrderStatus.Paid)
                    return order.ReceiptHash == hash
                        ? (true, null, new { gold = player.Gold, goldGranted = 0, alreadyPaid = true })
                        : (false, "PAY_REPLAY", null);
            }
            // 跨订单重放：这枚收据已为其他订单入账
            if (_paidReceipts.ContainsKey(hash)) return (false, "PAY_REPLAY", null);

            // 2) 平台验单（网络调用，锁外执行；并发双验时下方转换锁内再裁决一次）
            var (vOk, vErr, txnId) = await VerifyReceiptAsync(order, receipt);

            // 3) pending→paid 幂等转换 + 发货（锁序：Order → Player）
            lock (order.Sync)
            {
                if (order.Status == PayOrderStatus.Paid)
                    return order.ReceiptHash == hash
                        ? (true, null, new { gold = player.Gold, goldGranted = 0, alreadyPaid = true })
                        : (false, "PAY_REPLAY", null);
                if (order.Status == PayOrderStatus.Pending && !_paidReceipts.TryAdd(hash, order.OrderNo))
                    return (false, "PAY_REPLAY", null);

                if (!vOk)
                {
                    order.Status = PayOrderStatus.Failed;   // 验单失败留痕，修复后可重验（Failed 允许再来）
                    Db.SavePayOrder(order);
                    return (false, vErr, null);
                }

                order.Status = PayOrderStatus.Paid;
                order.ReceiptHash = hash;
                order.PlatformTxnId = txnId;
                order.PaidUtc = DateTime.UtcNow;
                lock (player.Sync)
                {
                    player.Gold += order.Gold;
                    Db.SavePlayer(player);
                }
                Db.SavePayOrder(order);
                Db.AddGoldTx(player.Id, order.Gold, "iap_purchase", order.RowId);
                return (true, null, new { gold = player.Gold, goldGranted = order.Gold });
            }
        }

        private static string HashReceipt(string receipt)
        {
            var sha = SHA256.HashData(Encoding.UTF8.GetBytes(receipt));
            return Convert.ToHexString(sha, 0, 16);   // 前 16 字节 hex，足够防碰撞且省列宽
        }

        // ---------------- 平台验单分发 ----------------

        private static async Task<(bool ok, string err, string txnId)> VerifyReceiptAsync(PayOrder order, string receipt)
        {
            if (Sandbox && order.Channel == "editor") return VerifyDevReceipt(order, receipt);
            switch (order.Channel)
            {
                case "steam": return await VerifySteamAsync(order, receipt);
                case "google": return await VerifyGoogleAsync(order, receipt);
                case "ios": return await VerifyAppleAsync(order, receipt);
                default: return (false, "PAY_CHANNEL_DISABLED", null);
            }
        }

        /// <summary>沙箱收据：dev:{orderNo}:{skuId}，三项绑定（收据只能验自己的单）</summary>
        private static (bool, string, string) VerifyDevReceipt(PayOrder order, string receipt)
        {
            var expect = $"dev:{order.OrderNo}:{order.SkuId}";
            return receipt == expect ? (true, null, "dev") : (false, "BAD_RECEIPT", null);
        }

        /// <summary>
        /// Steam 微交易验单。收据约定 = Steamworks SDK 回调带回的 orderid（数字字符串）。
        /// FinalizeTxn 幂等：重复调用同一 orderid 返回同一结果。
        /// 注意：真实闭环需要建单时先 InitTxn（见 CreateOrder 的 TODO(steam)），
        /// 未经 InitTxn 直接 Finalize 会被 Steam 拒绝（返回非 OK → PAY_VERIFY_FAILED），行为安全。
        /// </summary>
        private static async Task<(bool, string, string)> VerifySteamAsync(PayOrder order, string receipt)
        {
            if (string.IsNullOrEmpty(SteamAppId) || string.IsNullOrEmpty(SteamKey))
                return (false, "PAY_NOT_CONFIGURED", null);
            if (!long.TryParse(receipt, out _) && !receipt.StartsWith("steam:", StringComparison.Ordinal))
                return (false, "BAD_RECEIPT", null);   // orderid 是数字；steam: 前缀留给 InitTxn 对接期扩展
            try
            {
                var form = new Dictionary<string, string>
                {
                    ["key"] = SteamKey,
                    ["appid"] = SteamAppId,
                    ["orderid"] = receipt,
                };
                using var resp = await Http.PostAsync("https://api.steampowered.com/ISteamMicroTxn/FinalizeTxn/v1/", new FormUrlEncodedContent(form));
                var body = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("response", out var rs) ||
                    !rs.TryGetProperty("result", out var result))
                    return (false, "PAY_VERIFY_FAILED", null);
                return result.GetString() == "OK" ? (true, null, receipt) : (false, "PAY_VERIFY_FAILED", null);
            }
            catch (Exception)
            {
                return (false, "PAY_VERIFY_ERROR", null);   // Steam 侧网络异常：订单留 pending，客户端可重试
            }
        }

        /// <summary>
        /// Google Play 验单。收据 = purchaseToken（Unity IAP / Play Billing）。
        /// 服务账号 RS256 JWT 换 OAuth2 token → androidpublisher purchases.products.get 校验 purchaseState。
        /// 上线前用 license tester 过一轮沙箱联调（TODO 联调项）。
        /// </summary>
        private static async Task<(bool, string, string)> VerifyGoogleAsync(PayOrder order, string receipt)
        {
            if (string.IsNullOrEmpty(GooglePackage) || string.IsNullOrEmpty(GoogleClientEmail) || string.IsNullOrEmpty(GooglePrivateKey))
                return (false, "PAY_NOT_CONFIGURED", null);
            try
            {
                var token = await GetGoogleTokenAsync();
                var url = $"https://androidpublisher.googleapis.com/androidpublisher/v3/applications/{GooglePackage}" +
                          $"/purchases/products/{Uri.EscapeDataString(order.SkuId)}/tokens/{Uri.EscapeDataString(receipt)}";
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                using var resp = await Http.SendAsync(req);
                if ((int)resp.StatusCode == 404 || (int)resp.StatusCode == 401 || (int)resp.StatusCode == 403)
                    return (false, "PAY_VERIFY_FAILED", null);
                var body = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("purchaseState", out var state)) return (false, "PAY_VERIFY_FAILED", null);
                if (doc.RootElement.TryGetProperty("productId", out var pid) && pid.GetString() != order.SkuId)
                    return (false, "PAY_VERIFY_FAILED", null);
                // purchaseState：0 = Purchased，1 = Canceled
                return state.GetInt32() == 0 ? (true, null, receipt) : (false, "PAY_VERIFY_FAILED", null);
            }
            catch (Exception)
            {
                return (false, "PAY_VERIFY_ERROR", null);
            }
        }

        private static string _googleToken;
        private static long _googleTokenExpireUtc;

        /// <summary>服务账号 JWT（RS256，纯 BCL RSA）换 access token，缓存至到期前 60s</summary>
        private static async Task<string> GetGoogleTokenAsync()
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (_googleToken != null && now < _googleTokenExpireUtc - 60) return _googleToken;
            var header = B64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT" }));
            var payload = B64Url(JsonSerializer.SerializeToUtf8Bytes(new
            {
                iss = GoogleClientEmail,
                scope = "https://www.googleapis.com/auth/androidpublisher",
                aud = "https://oauth2.googleapis.com/token",
                iat = now,
                exp = now + 3600,
            }));
            using var rsa = RSA.Create();
            ImportPemFlexible(rsa, GooglePrivateKey);
            var sig = rsa.SignData(Encoding.UTF8.GetBytes($"{header}.{payload}"), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var assertion = $"{header}.{payload}.{B64Url(sig)}";

            using var resp = await Http.PostAsync("https://oauth2.googleapis.com/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                    ["assertion"] = assertion,
                }));
            var body = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            _googleToken = doc.RootElement.GetProperty("access_token").GetString();
            _googleTokenExpireUtc = now + (doc.RootElement.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3600);
            return _googleToken;
        }

        /// <summary>
        /// Apple 验单（App Store Server API）。收据 = StoreKit2 transactionId。
        /// ES256 JWT（纯 BCL ECDsa）→ GET /inApps/v1/transactions/{id} → 解码 signedTransactionInfo 校验 productId。
        /// TODO(联调)：signedTransactionInfo 的 JWS 签名链（x5c → Apple Root CA）校验上线前补上；
        ///   当前只解 payload 比对商品，配合生产环境域名白名单使用。
        /// </summary>
        private static async Task<(bool, string, string)> VerifyAppleAsync(PayOrder order, string receipt)
        {
            if (string.IsNullOrEmpty(AppleBundleId) || string.IsNullOrEmpty(AppleIssuer) ||
                string.IsNullOrEmpty(AppleKeyId) || string.IsNullOrEmpty(ApplePrivateKey))
                return (false, "PAY_NOT_CONFIGURED", null);
            if (string.IsNullOrEmpty(receipt) || receipt.Length > 128) return (false, "BAD_RECEIPT", null);
            try
            {
                var host = AppleUseSandbox ? "https://api.storekit-sandbox.itunes.apple.com" : "https://api.storekit.itunes.apple.com";
                using var req = new HttpRequestMessage(HttpMethod.Get, $"{host}/inApps/v1/transactions/{Uri.EscapeDataString(receipt)}");
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", MakeAppleJwt());
                using var resp = await Http.SendAsync(req);
                if (!resp.IsSuccessStatusCode) return (false, "PAY_VERIFY_FAILED", null);
                var body = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                var jws = doc.RootElement.TryGetProperty("signedTransactionInfo", out var s) ? s.GetString() : null;
                if (string.IsNullOrEmpty(jws)) return (false, "PAY_VERIFY_FAILED", null);
                using var txn = JsonDocument.Parse(Encoding.UTF8.GetString(B64UrlDecode(jws.Split('.')[1])));
                var root = txn.RootElement;
                if (root.TryGetProperty("productId", out var pid) && pid.GetString() != order.SkuId)
                    return (false, "PAY_VERIFY_FAILED", null);
                if (root.TryGetProperty("environment", out var env) && env.GetString() is string ev)
                {
                    var expect = AppleUseSandbox ? "Sandbox" : "Production";
                    if (ev != expect) return (false, "PAY_VERIFY_FAILED", null);
                }
                return (true, null, receipt);
            }
            catch (Exception)
            {
                return (false, "PAY_VERIFY_ERROR", null);
            }
        }

        /// <summary>App Store Server API 的 ES256 JWT（issuer/key/bundleId，5 分钟有效期）</summary>
        private static string MakeAppleJwt()
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var header = B64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "ES256", kid = AppleKeyId, typ = "JWT" }));
            var payload = B64Url(JsonSerializer.SerializeToUtf8Bytes(new
            {
                iss = AppleIssuer,
                iat = now,
                exp = now + 300,
                aud = "appstoreconnect-v1",
                bid = AppleBundleId,
            }));
            using var ecdsa = ECDsa.Create();
            ImportPemFlexible(ecdsa, ApplePrivateKey);
            var der = ecdsa.SignData(Encoding.UTF8.GetBytes($"{header}.{payload}"), HashAlgorithmName.SHA256);
            return $"{header}.{payload}.{B64Url(EcDerToJose(der))}";
        }

        // ---------------- 小工具 ----------------

        /// <summary>PKCS#8 PEM 导入（容忍 \n 转义与头尾空白；同时喂 RSA/EC 由密钥类型决定）</summary>
        private static void ImportPemFlexible(RSA rsa, string pem)
        {
            var norm = pem.Replace("\\n", "\n").Trim();
            rsa.ImportFromPem(norm);
        }

        private static void ImportPemFlexible(ECDsa ecdsa, string pem)
        {
            var norm = pem.Replace("\\n", "\n").Trim();
            ecdsa.ImportFromPem(norm);
        }

        /// <summary>DER ECDSA 签名（SEQUENCE{r,s}）→ JOSE ES256 原始格式 R(32)||S(32)</summary>
        private static byte[] EcDerToJose(byte[] der)
        {
            int i = (der[1] & 0x80) != 0 ? 2 + (der[1] & 0x7f) : 2;   // 跳过 SEQUENCE 头（长形长度）
            int rLen = der[i + 1];
            var r = new byte[rLen];
            Array.Copy(der, i + 2, r, 0, rLen);
            i += 2 + rLen;
            int sLen = der[i + 1];
            var s = new byte[sLen];
            Array.Copy(der, i + 2, s, 0, sLen);
            var raw = new byte[64];
            // INTEGER 可能带前导 0x00（>32 取尾 32）或不足 32（左补零）
            if (r.Length > 32) Array.Copy(r, r.Length - 32, raw, 0, 32);
            else Array.Copy(r, 0, raw, 32 - r.Length, r.Length);
            if (s.Length > 32) Array.Copy(s, s.Length - 32, raw, 32, 32);
            else Array.Copy(s, 0, raw, 64 - s.Length, s.Length);
            return raw;
        }

        private static string B64Url(byte[] bytes) =>
            Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static byte[] B64UrlDecode(string s)
        {
            s = s.Replace('-', '+').Replace('_', '/');
            return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
        }
    }
}
