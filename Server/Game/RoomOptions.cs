namespace SimpleGameServer.Game
{
    /// <summary>房间运行时选项：默认取 tbglobal，可被启动参数覆盖（联调/测试用，如 --FriendRoomTimeoutSec 8）</summary>
    public static class RoomOptions
    {
        /// <summary>私人房不满员解散超时（秒）</summary>
        public static int PrivateTimeoutSec = 300;

        public static int ResolvePrivateTimeoutSec()
        {
            var g = Config.GameTables.Tables.TbGlobal;
            return PrivateTimeoutSec > 0 ? PrivateTimeoutSec : g.FriendRoomTimeoutSec;
        }
    }
}
