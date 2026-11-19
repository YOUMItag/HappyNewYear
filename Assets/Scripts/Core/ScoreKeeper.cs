using System;

namespace Game.Core
{
    /// <summary>
    /// 计分与星级（DOC-04 定稿）。
    ///
    /// 单组得分 = 100 × 规则倍率 × 连锁倍率
    ///   规则倍率：R1 ×5 / R2 ×3 / R3 ×1
    ///   连锁倍率：1 + 0.25 × (轮次 − 1)，上限 ×2.0（第 5 轮封顶）
    ///
    /// 为什么砍掉跨手 combo：实测每次合法交换都必然产生消除，combo 永远不会断，
    /// 等于恒定 ×3 倍，是个没有区分度的假机制。改成只看「同一次交换内的第几轮」。
    ///
    /// @trace MECH-012, MECH-013, MECH-014
    /// </summary>
    public sealed class ScoreKeeper
    {
        public const int BaseScore = 100;
        public const int LevelMoves = 60;
        public const double MaxChainFactor = 2.0;
        public const double ChainStep = 0.25;

        static readonly int[] RuleMultipliers = { 5, 3, 1 };

        /// <summary>
        /// 星级门槛，由 800 局批量试玩的分位数反推（见 Tools/Playtest）。
        ///
        /// 定位：**休闲向**——目标是「一半人拿到两星以上、四分之一人拿三星、几乎没人空手」。
        ///   目标分布（100 名玩家，排他口径）：0★ ≤5% ｜ 1★ ≥40% ｜ 2★ ≥25% ｜ 3★ ≥25%
        ///   实测分位（Random + Smart 各半的混合人群）：p05 ≈ 22,450 ｜ p50 ≈ 29,175 ｜ p75 ≈ 32,600
        /// 取整后实测落在 0★ 4% / 1★ 45% / 2★ 26% / 3★ 26%（两星及以上 52%），四项约束全部满足。
        ///
        /// 注意口径：这里的百分比是「恰好落在该档」的分布，不是「至少达到」的累计值。
        /// 累计口径下 1★ 达成率 96%、2★ 52%、3★ 26%。
        ///
        /// 历史：22,000/28,000/34,000（初期）→ 23,500/30,500/34,500（按 0★≤10% 收紧）→ 当前值（转休闲）。
        /// </summary>
        static readonly int[] StarThresholds = { 0, 22000, 29000, 32500 };

        readonly int[] _ruleCounts = new int[3];

        public ScoreKeeper()
        {
            Reset();
        }

        public int Score { get; private set; }
        public int MovesUsed { get; private set; }
        public int GroupsCleared { get; private set; }

        public int MovesLeft { get { return Math.Max(0, LevelMoves - MovesUsed); } }
        public bool IsFinished { get { return MovesUsed >= LevelMoves; } }

        public void Reset()
        {
            Score = 0;
            MovesUsed = 0;
            GroupsCleared = 0;
            for (int i = 0; i < _ruleCounts.Length; i++) _ruleCounts[i] = 0;
        }

        /// <summary>消耗一手。只有交换成立才调用，弹回不计步。</summary>
        public void ConsumeMove()
        {
            MovesUsed++;
        }

        /// <summary>连锁倍率：第 1 轮 ×1.00，第 2 轮 ×1.25，第 3 轮 ×1.50，第 5 轮起 ×2.00 封顶。</summary>
        public static double ChainFactor(int round)
        {
            if (round < 1) round = 1;
            double factor = 1.0 + ChainStep * (round - 1);
            return factor > MaxChainFactor ? MaxChainFactor : factor;
        }

        /// <summary>单组得分（不累加）。表现层用它算飘字上的数字，与 Award 保证同一套公式。</summary>
        public static int PointsFor(RuleKind rule, int round)
        {
            int multiplier = RuleMultipliers[(int)rule];
            double factor = ChainFactor(round);
            return (int)Math.Round(BaseScore * multiplier * factor, MidpointRounding.AwayFromZero);
        }

        /// <summary>结算一组消除，返回本次得分。round 为同一次交换内的第几轮（从 1 开始）。</summary>
        public int Award(RuleKind rule, int round)
        {
            int index = (int)rule;
            int points = PointsFor(rule, round);

            Score += points;
            GroupsCleared++;
            _ruleCounts[index]++;
            return points;
        }

        public int CountOf(RuleKind rule)
        {
            return _ruleCounts[(int)rule];
        }

        /// <summary>当前星级 0~3。</summary>
        public int Stars
        {
            get
            {
                int stars = 0;
                for (int i = 1; i < StarThresholds.Length; i++)
                {
                    if (Score >= StarThresholds[i]) stars = i;
                }
                return stars;
            }
        }

        public static int StarThreshold(int star)
        {
            if (star < 0) star = 0;
            if (star >= StarThresholds.Length) star = StarThresholds.Length - 1;
            return StarThresholds[star];
        }
    }
}
