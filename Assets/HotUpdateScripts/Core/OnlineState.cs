using System.Threading.Tasks;
using Luban.SimpleJSON;
using UnityEngine;
using UnityEngine.UI;

namespace Game.HotUpdate.ChestGame
{
    /// <summary>在线状态缓存（/api/state 的最新快照）与全屏导航</summary>
    public static class OnlineState
    {
        public static JSONNode State { get; private set; }
        public static long Gold => State != null && State["data"] != null ? State["data"]["gold"].AsLong : 0;
        public static JSONNode Chests => State != null && State["data"] != null ? State["data"]["chests"] : null;
        /// <summary>技能冷却快照（skillType → 剩余秒；服务端权威下发）</summary>
        public static JSONNode SkillCooldowns => State != null && State["data"] != null ? State["data"]["skillCooldowns"] : null;

        /// <summary>指定技能的冷却剩余秒（0 = 可用）</summary>
        public static int SkillCooldownRemain(int skillType)
        {
            var cds = SkillCooldowns;
            return cds != null ? Mathf.Max(0, cds[skillType.ToString()].AsInt) : 0;
        }

        public static async Task<bool> Refresh()
        {
            var r = await ApiClient.GetStateAsync();
            if (r != null && r["ok"].AsBool)
            {
                State = r;
                return true;
            }
            return false;
        }
    }
}
