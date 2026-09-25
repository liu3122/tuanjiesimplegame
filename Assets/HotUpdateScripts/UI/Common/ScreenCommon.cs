using System.Linq;
using System.Threading.Tasks;
using cfg;
using Luban.SimpleJSON;
using UnityEngine;
using UnityEngine.UI;

namespace Game.HotUpdate.ChestGame
{
    /// <summary>Screens 模块拆分（partial）：Common</summary>
    public static partial class Screens
    {


        public static string ErrText(JSONNode r)
        {
            if (r == null) return L10n.Get("网络错误");
            string err = r["error"].Value;
            if (err == "NOT_ENOUGH_GOLD") return L10n.Get("金币不足");
            if (err == "ERR_QUOTA") return L10n.Get("宝箱技能额度已耗尽");
            if (err == "ERR_PLAYER_USED") return L10n.Get("你对本宝箱已用过技能");
            if (err == "ERR_NO_TARGET") return L10n.Get("没有低于平均值的选项");
            if (err == "ERR_COOLDOWN") return L10n.Get("技能冷却中，还剩") + " " + r["data"]["remainSec"].AsInt + "s";
            if (err == "FEATURE_DISABLED") return L10n.Get("该玩法已关闭");
            if (err == "ALREADY_TOP") return L10n.Get("你已是最高出价");
            if (err == "OWN_LOT") return L10n.Get("不能竞拍自己的宝箱");
            if (err == "AUCTION_ENDED") return L10n.Get("拍卖已结束");
            if (string.IsNullOrEmpty(err)) return L10n.Get("未知错误");
            return err;
        }
    }
}
