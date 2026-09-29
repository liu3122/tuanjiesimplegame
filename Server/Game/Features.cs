using cfg;
using SimpleGameServer.Config;

namespace SimpleGameServer.Game
{
    /// <summary>
    /// 玩法开关（tb_feature.xlsx 配置，改完重新生成即生效，服务重启后应用）：
    /// 服务端拦截对应路由/WebSocket，/api/state 下发 featureFlags 供客户端隐藏入口；
    /// 表里缺行 = 默认开启（删行即恢复）。
    /// </summary>
    public static class Features
    {
        public const string Rush = "rush";
        public const string Auction = "auction";
        public const string Daily = "daily";
        public const string Duel = "duel";
        public const string PotDuel = "potduel";
        public const string Ad = "ad";
        public const string Quest = "quest";
        public const string Sign = "sign";
        public const string ItemShop = "itemshop";

        public static bool IsOn(string id)
        {
            var row = GameTables.Tables.TbFeature.GetOrDefault(id);
            return row == null || row.Enabled;
        }

        /// <summary>全量开关视图（下发客户端）</summary>
        public static Dictionary<string, bool> View()
        {
            return GameTables.Tables.TbFeature.DataList.ToDictionary(f => f.Id, f => f.Enabled);
        }
    }
}
