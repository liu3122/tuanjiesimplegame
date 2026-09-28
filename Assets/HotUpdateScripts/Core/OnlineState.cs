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

        /// <summary>玩法开关（服务端 featureFlags 下发；无数据/缺项默认开启）</summary>
        public static bool FeatureOn(string id)
        {
            var flags = State != null && State["data"] != null ? State["data"]["featureFlags"] : null;
            if (flags == null) return true;
            var v = flags[id];
            return v.IsNull || v.AsBool;
        }

        public static int Level => State != null && State["data"] != null ? Mathf.Max(1, State["data"]["level"].AsInt) : 1;
        public static int Xp => State != null && State["data"] != null ? State["data"]["xp"].AsInt : 0;
        public static int XpNext => State != null && State["data"] != null ? State["data"]["xpNext"].AsInt : 0;
        public static int DuelRating => State != null && State["data"] != null ? State["data"]["duelRating"].AsInt : 1000;

        /// <summary>上次快照等级（升级检测；0 = 首次不提示）</summary>
        private static int _lastLevel;

        private static void DetectLevelUp()
        {
            int lv = Level;
            if (_lastLevel > 0 && lv > _lastLevel)
            {
                var row = GameConfig.Tables.TbLevel.GetOrDefault(lv);
                string unlock = row != null && !string.IsNullOrEmpty(row.UnlockText) ? " · " + row.UnlockText : "";
                UIRoot.ToastStatic("<color=#F5D061>升级！Lv." + lv + unlock + "</color>");
            }
            _lastLevel = lv;
        }

        public static async Task<bool> Refresh()
        {
            var r = await ApiClient.GetStateAsync();
            if (r != null && r["ok"].AsBool)
            {
                State = r;
                DetectLevelUp();
                return true;
            }
            return false;
        }
    }
}
