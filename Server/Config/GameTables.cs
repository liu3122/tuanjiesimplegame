using cfg;
using Luban.SimpleJSON;

namespace SimpleGameServer.Config
{
    /// <summary>
    /// 服务端配置表：与客户端共用同一份 Luban 产物（Assets/AssetRaw/Config/*.json），
    /// 保证两端数值永远一致（gen.bat / gen_server.bat 从同一 xlsx 生成）。
    /// </summary>
    public static class GameTables
    {
        public static Tables Tables { get; private set; }
        public static string DataDir { get; set; } = "../Assets/AssetRaw/Config";

        public static void Load(string dataDir = null)
        {
            if (dataDir != null) DataDir = dataDir;
            Tables = new Tables(file =>
            {
                string path = Path.Combine(DataDir, file + ".json");
                return JSON.Parse(File.ReadAllText(path));
            });
        }
    }
}
