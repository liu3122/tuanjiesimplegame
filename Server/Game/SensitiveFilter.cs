using System.Text;

namespace SimpleGameServer.Game;

/// <summary>
/// 昵称敏感词过滤（服务端权威）。
/// 词表为基线版：送审/运营期应替换或补充出版单位下发的官方屏蔽字库（Txt 文件热载亦可）。
/// 覆盖入口：/api/login 昵称注册、道具改名卡；其他玩家可见文本（拍卖行、战报）均由系统生成，无自由文本。
/// </summary>
public static class SensitiveFilter
{
    // 基线词表：涉政泛化词 / 低俗辱骂 / 暴恐 / 毒品 / 赌博 / 平台高危词
    // 说明：涉政人名等高敏词不硬编码在源码里，接入出版方字库后统一生效（见上注释）。
    private static readonly string[] BaseWords =
    {
        // 低俗辱骂
        "傻逼", "煞笔", "妈的", "他妈", "你妈", "草泥马", "尼玛", "狗日", "婊", "屌", "操你", "滚蛋",
        "sb", "nmb", "cnm", "fuck", "shit", "bitch", "dick", "pussy", "asshole",
        // 赌博
        "赌博", "赌场", "博彩", "下注", "押注", "开奖", "时时彩", "六合彩", "赌资", "外围", "庄家抽水",
        // 毒品
        "毒品", "大麻", "冰毒", "海洛因", "吸毒", "贩毒", "摇头丸",
        // 暴恐
        "恐怖分子", "恐怖袭击", "炸弹", "爆炸物", "砍人", "自杀袭击", "圣战",
        // 违法/平台高危
        "代练", "代充", "外挂", "刷金", "金币交易", "人民币交易", "私服", "破解版",
        "诈骗", "洗钱", "传销", "枪支", "买枪",
        // 涉政泛化
        "反动", "法轮", "台独", "藏独", "疆独",
    };

    private static readonly HashSet<string> _words = new(BaseWords, StringComparer.OrdinalIgnoreCase);

    /// <summary>昵称合法性校验：通过返回 null，否则返回错误码（SENSITIVE_NAME / BAD_NAME）</summary>
    public static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "BAD_NAME";
        name = name.Trim();
        if (name.Length < 1 || name.Length > 16) return "BAD_NAME";

        // 字符白名单：中文/字母/数字/下划线（禁 emoji、控制符、特殊符号——防变形绕过与显示异常）
        foreach (var ch in name)
        {
            bool ok = char.IsLetter(ch) || char.IsDigit(ch) || ch == '_' ||
                      (ch >= 0x4E00 && ch <= 0x9FFF);   // CJK 统一表意
            if (!ok) return "BAD_NAME";
        }

        // 归一化后匹配：去空白、全角转半角、小写——防 "s b"、"ＳＢ" 这类插空/全角绕过
        if (_words.Contains(Normalize(name))) return "SENSITIVE_NAME";
        foreach (var w in _words)
            if (Normalize(name).Contains(Normalize(w))) return "SENSITIVE_NAME";
        return null;
    }

    /// <summary>任意文本敏感词检测（含未归一化原文，双保险）</summary>
    public static bool ContainsSensitive(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        var n = Normalize(text);
        foreach (var w in _words)
            if (n.Contains(Normalize(w)) || text.Contains(w, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static string Normalize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (char.IsWhiteSpace(ch)) continue;                              // 去空白
            char c = ch;
            if (c >= 0xFF01 && c <= 0xFF5E) c = (char)(c - 0xFEE0);           // 全角→半角
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}
