using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Game.Core;

namespace Playtest
{
    /// <summary>
    /// 离线批量试玩：不开 Unity，直接跑核心逻辑，几十秒出几百局的统计。
    ///
    /// 两种打法对比，用来区分「难度来自运气」还是「来自技巧」：
    ///   Random —— 每次随机挑一个合法步，相当于新手乱打
    ///   Smart  —— 每个候选步都模拟一遍，优先挑能出直系三代（R1）的，相当于会玩的玩家
    ///
    /// 两者分差越大，说明技巧空间越大；分差越小，说明结果主要由运气决定。
    /// </summary>
    internal static class Program
    {
        const int CellCount = BoardModel.CellCount;

        internal enum Strategy
        {
            Random,
            Smart
        }

        internal sealed class GameResult
        {
            public int Score;
            public int Stars;
            public int Shuffles;
            public int Groups;
            public int Moves;
            public int MaxRound;
            public readonly int[] Rules = new int[3];
            /// <summary>每手连锁层数的分布：下标 = 这一手消了几轮（1..7，7 表示 7 轮及以上）。
            /// 用「手数」而非「轮次出现次数」作口径，否则各档加起来会超过 100%。</summary>
            public readonly int[] MoveRoundHist = new int[8];
        }

        static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; }
            catch (IOException) { }

            int games = GetInt(args, "--games", 40);
            int cap = GetInt(args, "--cap", 2);
            int seed = GetInt(args, "--seed", 20261118);

            // 星级门槛做成参数，方便不改代码试多组。默认取工程里当前写死的值。
            int t1 = GetInt(args, "--t1", ScoreKeeper.StarThreshold(1));
            int t2 = GetInt(args, "--t2", ScoreKeeper.StarThreshold(2));
            int t3 = GetInt(args, "--t3", ScoreKeeper.StarThreshold(3));
            var thresholds = new[] { 0, t1, t2, t3 };

            string jsonPath = GetString(args, "--data", Path.Combine("..", "..", "Assets", "Data", "FamilyTree.json"));
            if (!File.Exists(jsonPath))
            {
                Console.WriteLine("[FATAL] 找不到族谱文件：" + Path.GetFullPath(jsonPath));
                return 2;
            }

            List<FamilyPerson> persons = FamilyDatabase.LoadFromJson(File.ReadAllText(jsonPath, Encoding.UTF8));
            var graph = new FamilyGraph(persons);

            Console.WriteLine("=== 过年啦！离线批量试玩 ===");
            Console.WriteLine("族谱：" + graph.Count + " 人 / " + graph.TitleCount + " 种称谓 / 三元组 "
                + graph.TripleCount + "（R1 " + graph.R1Count + " + R2 " + graph.R2Count + " + R3 " + graph.R3Count + "）");
            Console.WriteLine("配置：每档 " + games + " 局，每局 60 手，同名上限 " + cap + "，随机种子 " + seed);
            Console.WriteLine("门槛：1★ " + t1 + " ｜ 2★ " + t2 + " ｜ 3★ " + t3);
            Console.WriteLine();

            List<GameResult> randomRuns = RunBatch(graph, Strategy.Random, games, cap, seed);
            List<GameResult> smartRuns = RunBatch(graph, Strategy.Smart, games, cap, seed + 1);

            PrintStrategy("Random（新手乱打）", randomRuns, thresholds);
            PrintStrategy("Smart（优先直系三代）", smartRuns, thresholds);
            PrintComparison(randomRuns, smartRuns, thresholds);
            PrintBlend(randomRuns, smartRuns, thresholds);

            return 0;
        }

        // ═══════════════════════ 混合人群 ═══════════════════════

        /// <summary>
        /// 定门槛用的「100 个玩家」模型：一半是乱打的新手，一半是会挑直系三代的老手。
        /// 真实玩家池大概就在这两者之间，用它取分位数，比只看其中一种更稳。
        /// </summary>
        static void PrintBlend(List<GameResult> randomRuns, List<GameResult> smartRuns, int[] thresholds)
        {
            var scores = new List<int>(randomRuns.Count + smartRuns.Count);
            for (int i = 0; i < randomRuns.Count; i++) scores.Add(randomRuns[i].Score);
            for (int i = 0; i < smartRuns.Count; i++) scores.Add(smartRuns[i].Score);
            scores.Sort();

            int n = scores.Count;
            Console.WriteLine("── 混合人群（Random + Smart 各半，共 " + n + " 局）──");
            Console.WriteLine("  分位   p05 " + Percentile(scores, 0.05)
                + " ｜ p10 " + Percentile(scores, 0.10)
                + " ｜ p25 " + Percentile(scores, 0.25)
                + " ｜ p50 " + Percentile(scores, 0.50)
                + " ｜ p60 " + Percentile(scores, 0.60)
                + " ｜ p75 " + Percentile(scores, 0.75)
                + " ｜ p85 " + Percentile(scores, 0.85)
                + " ｜ p90 " + Percentile(scores, 0.90)
                + " ｜ p95 " + Percentile(scores, 0.95));

            int[] bins = Bins(scores, thresholds);
            Console.WriteLine("  星级  0★ " + Pct(bins[0], n) + " ｜ 1★ " + Pct(bins[1], n)
                + " ｜ 2★ " + Pct(bins[2], n) + " ｜ 3★ " + Pct(bins[3], n));

            Console.WriteLine("  目标  0★ ≤5% ｜ 1★ ≥40% ｜ 2★ ≥25% ｜ 3★ ≥25%（休闲向，两星以上过半）");
            Console.WriteLine("  按目标分位反推的门槛：1★ = p05 = " + Percentile(scores, 0.05)
                + " ｜ 2★ = p50 = " + Percentile(scores, 0.50)
                + " ｜ 3★ = p75 = " + Percentile(scores, 0.75));
            Console.WriteLine();
        }

        static int[] Bins(List<int> sortedScores, int[] thresholds)
        {
            var bins = new int[4];
            for (int i = 0; i < sortedScores.Count; i++)
            {
                int s = sortedScores[i];
                int star = 0;
                for (int k = 1; k < thresholds.Length; k++) if (s >= thresholds[k]) star = k;
                bins[star]++;
            }
            return bins;
        }

        // ═══════════════════════ 批量对局 ═══════════════════════

        static List<GameResult> RunBatch(FamilyGraph graph, Strategy strategy, int games, int cap, int seed)
        {
            var results = new List<GameResult>(games);

            for (int i = 0; i < games; i++)
            {
                var rng = new Random(seed + i);
                results.Add(PlayOne(graph, strategy, cap, rng));
            }
            return results;
        }

        static GameResult PlayOne(FamilyGraph graph, Strategy strategy, int cap, Random rng)
        {
            var result = new GameResult();
            var detector = new MatchDetector(graph);
            var validator = new MoveValidator(detector);

            int[] roster = new int[graph.Count];
            for (int i = 0; i < roster.Length; i++) roster[i] = i;
            var pool = new CellPool(graph, rng, roster, cap);
            var generator = new BoardGenerator(graph, detector, pool, rng, 3, 400);
            var resolver = new BoardResolver(detector, pool);
            var score = new ScoreKeeper();

            var moves = new List<int>(24);
            var matches = new List<MatchGroup>(24);

            BoardModel board = generator.Build();
            int guard = 0;

            while (!score.IsFinished && guard++ < 3000)
            {
                // 上一轮结算可能留下现成三连（正常不会，兜底）
                if (detector.HasMatch(board))
                {
                    ApplyRounds(resolver.Resolve(board), score, result);
                    continue;
                }

                AutoMoveFinder.Collect(board, detector, moves);
                if (moves.Count == 0)
                {
                    result.Shuffles++;
                    board = generator.Build();
                    continue;
                }

                int key = strategy == Strategy.Random
                    ? moves[rng.Next(moves.Count)]
                    : PickBestMove(board, detector, moves, matches);

                int a = key / CellCount;
                int b = key % CellCount;

                if (!validator.TrySwap(board, a, b)) continue;   // 理论上不会发生

                score.ConsumeMove();
                result.Moves++;

                var rounds = resolver.Resolve(board);
                ApplyRounds(rounds, score, result);

                int bucket = rounds.Count > 7 ? 7 : rounds.Count;
                if (bucket >= 1) result.MoveRoundHist[bucket]++;
            }

            result.Score = score.Score;
            result.Stars = score.Stars;
            return result;
        }

        static void ApplyRounds(
            List<BoardResolver.RoundResult> rounds, ScoreKeeper score, GameResult result)
        {
            for (int i = 0; i < rounds.Count; i++)
            {
                BoardResolver.RoundResult round = rounds[i];
                if (round.Round > result.MaxRound) result.MaxRound = round.Round;

                for (int k = 0; k < round.GroupCount; k++)
                {
                    MatchGroup group = round.Groups[k];
                    score.Award(group.Rule, round.Round);
                    result.Groups++;
                    result.Rules[(int)group.Rule]++;
                }
            }
        }

        /// <summary>Smart 打法：对每个候选步模拟一次，按「出 R1 &gt; 出 R2 &gt; 消除组数多」打分。</summary>
        static int PickBestMove(BoardModel board, MatchDetector detector, List<int> moves, List<MatchGroup> matches)
        {
            int bestKey = moves[0];
            int bestValue = int.MinValue;

            for (int i = 0; i < moves.Count; i++)
            {
                int key = moves[i];
                int a = key / CellCount;
                int b = key % CellCount;

                Swap(board, a, b);
                detector.FindMatches(board, matches);

                bool hasR1 = false;
                bool hasR2 = false;
                for (int k = 0; k < matches.Count; k++)
                {
                    if (matches[k].Rule == RuleKind.R1) hasR1 = true;
                    else if (matches[k].Rule == RuleKind.R2) hasR2 = true;
                }
                Swap(board, a, b);

                int value = (hasR1 ? 100 : 0) + (hasR2 ? 30 : 0) + matches.Count;
                if (value > bestValue)
                {
                    bestValue = value;
                    bestKey = key;
                }
            }
            return bestKey;
        }

        static void Swap(BoardModel board, int a, int b)
        {
            int t = board[a];
            board[a] = board[b];
            board[b] = t;
        }

        // ═══════════════════════ 报告 ═══════════════════════

        static void PrintStrategy(string label, List<GameResult> runs, int[] thresholds)
        {
            int n = runs.Count;
            var scores = new List<int>(n);
            var shuffles = new List<int>(n);
            int totalGroups = 0, totalMoves = 0, totalRules = 0;
            var rules = new int[3];
            var rounds = new int[8];

            for (int i = 0; i < n; i++)
            {
                GameResult r = runs[i];
                scores.Add(r.Score);
                shuffles.Add(r.Shuffles);
                totalGroups += r.Groups;
                totalMoves += r.Moves;
                for (int k = 0; k < 3; k++) { rules[k] += r.Rules[k]; totalRules += r.Rules[k]; }
                for (int k = 0; k < 8; k++) rounds[k] += r.MoveRoundHist[k];
            }

            scores.Sort();
            shuffles.Sort();

            int[] bins = Bins(scores, thresholds);

            Console.WriteLine("── " + label + " ──");
            Console.WriteLine("  分数      最低 " + scores[0]
                + " ｜ p10 " + Percentile(scores, 0.10)
                + " ｜ 中位 " + Percentile(scores, 0.50)
                + " ｜ p60 " + Percentile(scores, 0.60)
                + " ｜ p85 " + Percentile(scores, 0.85)
                + " ｜ p90 " + Percentile(scores, 0.90)
                + " ｜ 最高 " + scores[n - 1]
                + " ｜ 均值 " + (Sum(scores) / n));
            Console.WriteLine("  星级      0★ " + Pct(bins[0], n) + "（" + bins[0] + "）"
                + " ｜ 1★ " + Pct(bins[1], n) + "（" + bins[1] + "）"
                + " ｜ 2★ " + Pct(bins[2], n) + "（" + bins[2] + "）"
                + " ｜ 3★ " + Pct(bins[3], n) + "（" + bins[3] + "）");
            Console.WriteLine("  洗牌      平均 " + Avg(shuffles, n) + " 次/局 ｜ 最多 " + shuffles[n - 1] + " 次"
                + " ｜ 每 " + (totalMoves == 0 ? "-" : (totalMoves / Math.Max(1.0, Sum(shuffles))).ToString("F1")) + " 手一次");
            Console.WriteLine("  消除      平均 " + AvgInt(totalGroups, n) + " 组/局 ｜ "
                + (totalMoves == 0 ? "-" : ((double)totalGroups / totalMoves).ToString("F2")) + " 组/手");
            Console.WriteLine("  规则占比  R1 " + Pct(rules[0], totalRules)
                + " ｜ R2 " + Pct(rules[1], totalRules)
                + " ｜ R3 " + Pct(rules[2], totalRules));
            Console.WriteLine("  连锁层数  " + RoundHistText(rounds));
            Console.WriteLine();
        }

        static void PrintComparison(List<GameResult> randomRuns, List<GameResult> smartRuns, int[] thresholds)
        {
            var a = new List<int>(); var b = new List<int>();
            for (int i = 0; i < randomRuns.Count; i++) a.Add(randomRuns[i].Score);
            for (int i = 0; i < smartRuns.Count; i++) b.Add(smartRuns[i].Score);
            a.Sort(); b.Sort();

            int medA = Percentile(a, 0.50);
            int medB = Percentile(b, 0.50);
            double gap = medA == 0 ? 0 : (medB - medA) * 100.0 / medA;

            Console.WriteLine("── 技巧空间 ──");
            Console.WriteLine("  中位分：Random " + medA + " → Smart " + medB
                + "（" + (gap >= 0 ? "+" : "") + gap.ToString("F1") + "%）");

            if (gap < 10)
                Console.WriteLine("  判定：技巧带来的收益 < 10%，结果基本由运气决定，需要加强 R1 的分数权重或机会占比");
            else if (gap < 25)
                Console.WriteLine("  判定：技巧收益 10~25%，有区分度但偏弱");
            else
                Console.WriteLine("  判定：技巧收益 > 25%，会玩与乱打差距明显，难度曲线健康");

            Console.WriteLine();
            Console.WriteLine("── 各档达到 1★ / 2★ / 3★ 的比例 ──");
            Console.WriteLine("  Random " + Reach(randomRuns, thresholds)
                + "\n  Smart  " + Reach(smartRuns, thresholds));
        }

        static string Reach(List<GameResult> runs, int[] thresholds)
        {
            int n = runs.Count;
            var reach = new int[4];
            for (int i = 0; i < n; i++)
            {
                int s = runs[i].Score;
                for (int k = 1; k < thresholds.Length; k++)
                    if (s >= thresholds[k]) reach[k]++;
            }
            return "达到 1★ " + Pct(reach[1], n) + " ｜ 2★ " + Pct(reach[2], n) + " ｜ 3★ " + Pct(reach[3], n);
        }

        /// <summary>每手连锁层数的排他分布（各档相加 = 100%）。</summary>
        static string RoundHistText(int[] rounds)
        {
            int total = 0;
            for (int k = 1; k < 8; k++) total += rounds[k];
            if (total == 0) return "-";

            int longTail = 0;
            for (int k = 4; k < 8; k++) longTail += rounds[k];

            var sb = new StringBuilder();
            sb.Append("消 1 层 ").Append(Pct(rounds[1], total));
            sb.Append(" ｜ 2 层 ").Append(Pct(rounds[2], total));
            sb.Append(" ｜ 3 层 ").Append(Pct(rounds[3], total));
            sb.Append(" ｜ 4 层及以上 ").Append(Pct(longTail, total));
            return sb.ToString();
        }

        // ═══════════════════════ 小工具 ═══════════════════════

        static int Percentile(List<int> sorted, double p)
        {
            if (sorted.Count == 0) return 0;
            int index = (int)Math.Round((sorted.Count - 1) * p);
            if (index < 0) index = 0;
            if (index >= sorted.Count) index = sorted.Count - 1;
            return sorted[index];
        }

        static int Sum(List<int> values)
        {
            int total = 0;
            for (int i = 0; i < values.Count; i++) total += values[i];
            return total;
        }

        static string Avg(List<int> values, int n)
        {
            return n == 0 ? "-" : ((double)Sum(values) / n).ToString("F1");
        }

        static string AvgInt(int total, int n)
        {
            return n == 0 ? "-" : ((double)total / n).ToString("F1");
        }

        static string Pct(int part, int total)
        {
            if (total <= 0) return "-";
            return (part * 100.0 / total).ToString("F0") + "%";
        }

        static int GetInt(string[] args, string name, int fallback)
        {
            string raw = GetString(args, name, null);
            int value;
            return int.TryParse(raw, out value) ? value : fallback;
        }

        static string GetString(string[] args, string name, string fallback)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name) return args[i + 1];
            }
            return fallback;
        }
    }
}
