using cfg;
using SimpleGameServer.Config;

namespace SimpleGameServer.Game
{
    /// <summary>
    /// 宝箱金额生成器（Rarity Template + EV Normalization）：
    /// 1. 按权重 Roll 每格稀有度档（白/绿/蓝/紫/金）
    /// 2. 档位基准金额 × 对数正态扰动（exp(jitter·N(0,1))，Box-Muller）
    /// 3. 整体归一化到固定池期望（总池 = 期望 × 20），保证庄家边际恒定、结构随机
    /// 参数配置于 TbChestGen；Chest.GenMode=1 时启用，否则用固定金额池洗牌。
    /// </summary>
    public static class ChestFactory
    {
        /// <summary>生成 20 格金额（已洗牌语义：生成顺序即随机）</summary>
        public static int[] Generate(cfg.chest.Chest config, Random rng)
        {
            var gen = GameTables.Tables.TbChestGen[config.Id];
            double[] weights = gen.Weights.Select(w => (double)w).ToArray();
            double weightSum = weights.Sum();
            double targetSum = config.SlotAmounts.Average() * config.SlotAmounts.Count; // 固定池总池为期望锚点

            var raw = new double[config.SlotAmounts.Count];
            for (int i = 0; i < raw.Length; i++)
            {
                int tier = RollTier(weights, weightSum, rng.NextDouble());
                double jittered = gen.Bases[tier] * Math.Exp(gen.Jitter * Normal(rng));
                raw[i] = Math.Max(1, jittered);
            }

            // EV 归一化：缩放到目标总池
            double scale = targetSum / raw.Sum();
            var amounts = raw.Select(v => Math.Max(1, (int)Math.Round(v * scale))).ToArray();

            // 取整误差修正：微调最大格使总和精确命中（保持期望严格一致）
            long diff = (long)targetSum - amounts.Sum();
            if (diff != 0)
            {
                int idx = Array.IndexOf(amounts, amounts.Max());
                amounts[idx] = Math.Max(1, amounts[idx] + (int)diff);
            }
            return amounts;
        }

        private static int RollTier(double[] weights, double sum, double roll)
        {
            double acc = 0;
            for (int i = 0; i < weights.Length; i++)
            {
                acc += weights[i] / sum;
                if (roll < acc) return i;
            }
            return weights.Length - 1;
        }

        /// <summary>Box-Muller 标准正态</summary>
        private static double Normal(Random rng)
        {
            double u1 = 1.0 - rng.NextDouble();
            double u2 = rng.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }
    }
}
