using SimpleGameServer.Config;

namespace SimpleGameServer.Game
{
    /// <summary>
    /// 赛季服务（周赛季）：每周一检测上一周的现金周榜（gold_tx 净流入），
    /// 前 10 名自动发放金币奖励（直接入账 + season_reward 流水，state.lastSeasonReward 下发）。
    /// 启动与每 10 分钟各检查一次（覆盖停机跨周的场景），已发放的周幂等跳过。
    /// </summary>
    public class SeasonService : BackgroundService
    {
        /// <summary>周榜名次奖励（1~10 名）</summary>
        private static readonly int[] Rewards = { 400, 250, 150, 50, 50, 50, 50, 50, 50, 50 };

        private readonly GameStore _store;
        private readonly ILogger<SeasonService> _log;

        public SeasonService(GameStore store, ILogger<SeasonService> log)
        {
            _store = store;
            _log = log;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try { Tick(); }
                catch (Exception e) { _log.LogError(e, "[Season] tick failed"); }
                try { await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }

        private void Tick()
        {
            string curWeek = Db.WeekStartUtcIso();
            string prevWeek = DateTime.Parse(curWeek).ToUniversalTime().AddDays(-7).ToString("o");
            if (Db.SeasonPaid(prevWeek)) return;

            var board = Db.WeekGoldBoard(prevWeek, curWeek, Rewards.Length);
            if (board.Count == 0) return;

            var winners = new List<(long pid, string name, int rank, long amount)>();
            lock (_store.Gate)
            {
                for (int i = 0; i < board.Count; i++)
                {
                    var (pid, name, _) = board[i];
                    if (!_store.Players.TryGetValue(pid, out var p)) continue;
                    long amount = Rewards[i];
                    p.Gold += amount;
                    Db.SavePlayer(p);
                    Db.AddGoldTx(pid, amount, "season_reward", 0);
                    winners.Add((pid, name, i + 1, amount));
                }
            }
            Db.RecordSeasonRewards(prevWeek, winners);
            if (winners.Count > 0)
            {
                _log.LogInformation("赛季结算（{Week}）：发放 {Count} 人，合计 {Total}", prevWeek, winners.Count, winners.Sum(w => w.amount));
            }
        }
    }
}
