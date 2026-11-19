using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Game.Core;

namespace LogicTests
{
    /// <summary>
    /// M1 逻辑层自测。不打开 Unity，直接用 dotnet 编译 Assets/Scripts/Core 并跑真实数据。
    /// 验收基线全部来自 DOC-05《终检报告》的实测值。
    /// </summary>
    static class Program
    {
        const int Rows = BoardModel.Rows;
        const int Cols = BoardModel.Cols;

        static int _pass;
        static int _fail;
        static readonly List<string> _notes = new List<string>();

        static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; }
            catch (IOException) { /* 重定向到文件时可能失败，忽略 */ }

            string jsonPath = args.Length > 0
                ? args[0]
                : Path.Combine("..", "..", "Assets", "Data", "FamilyTree.json");

            if (!File.Exists(jsonPath))
            {
                Console.WriteLine("[FATAL] 找不到族谱文件: " + Path.GetFullPath(jsonPath));
                return 2;
            }

            string json = File.ReadAllText(jsonPath, Encoding.UTF8);
            List<FamilyPerson> persons = FamilyDatabase.LoadFromJson(json);
            FamilyGraph graph = new FamilyGraph(persons);

            Console.WriteLine("=== M1 逻辑层自测 ===");
            Console.WriteLine("数据源: " + Path.GetFullPath(jsonPath));
            Console.WriteLine();

            T1_DataIntegrity(graph, persons);
            T2_TripleCounts(graph);
            T3_FamilyDistribution(graph);
            T4_Generation(graph);
            T5_Gameplay(graph);

            Console.WriteLine();
            Console.WriteLine("────────────────────────────────");
            Console.WriteLine("通过 " + _pass + " 项，失败 " + _fail + " 项");
            if (_notes.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("需要留意：");
                for (int i = 0; i < _notes.Count; i++) Console.WriteLine("  · " + _notes[i]);
            }
            Console.WriteLine("────────────────────────────────");
            return _fail == 0 ? 0 : 1;
        }

        // ═══════════════════════ T1 数据完整性 ═══════════════════════

        static void T1_DataIntegrity(FamilyGraph g, List<FamilyPerson> persons)
        {
            Section("T1 数据完整性");

            Check("人数 = 47", g.Count == 47, "实测 " + g.Count);
            Check("称谓种类 = 35", g.TitleCount == 35, "实测 " + g.TitleCount);

            var badRefs = new List<string>();
            for (int i = 0; i < persons.Count; i++)
            {
                FamilyPerson p = persons[i];
                if (!string.IsNullOrEmpty(p.FatherId) && g.IndexOf(p.FatherId) < 0)
                    badRefs.Add(p.Title + " 的父亲 " + p.FatherId + " 不存在");
                if (!string.IsNullOrEmpty(p.MotherId) && g.IndexOf(p.MotherId) < 0)
                    badRefs.Add(p.Title + " 的母亲 " + p.MotherId + " 不存在");
                if (!string.IsNullOrEmpty(p.SpouseId) && g.IndexOf(p.SpouseId) < 0)
                    badRefs.Add(p.Title + " 的配偶 " + p.SpouseId + " 不存在");
            }
            Check("父母/配偶引用全部合法", badRefs.Count == 0, Join(badRefs));

            Check("无血缘环", !HasCycle(g), "检测到父子/母子成环");

            var used = new HashSet<int>();
            for (int i = 0; i < g.Triples.Count; i++)
            {
                int[] t = g.Triples[i];
                used.Add(t[0]);
                used.Add(t[1]);
                used.Add(t[2]);
            }
            var dead = new List<string>();
            for (int i = 0; i < g.Count; i++)
            {
                if (!used.Contains(i)) dead.Add(g.Title(i) + "(" + g.Get(i).Id + ")");
            }
            Check("无死块（每人至少能进一条组合）", dead.Count == 0, Join(dead));
        }

        static bool HasCycle(FamilyGraph g)
        {
            var state = new byte[g.Count];
            for (int i = 0; i < g.Count; i++)
            {
                if (state[i] == 0 && VisitParent(g, i, state)) return true;
            }
            return false;
        }

        static bool VisitParent(FamilyGraph g, int index, byte[] state)
        {
            if (state[index] == 1) return true;
            if (state[index] == 2) return false;
            state[index] = 1;

            int father = g.FatherOf(index);
            if (father >= 0 && VisitParent(g, father, state)) return true;
            int mother = g.MotherOf(index);
            if (mother >= 0 && mother != father && VisitParent(g, mother, state)) return true;

            state[index] = 2;
            return false;
        }

        // ═══════════════════════ T2 三元组数量 ═══════════════════════

        static void T2_TripleCounts(FamilyGraph g)
        {
            Section("T2 三元组数量（对标 DOC-05）");

            Check("R1 直系三代 = 64", g.R1Count == 64, "实测 " + g.R1Count);
            Check("R2 同代姻亲 = 11", g.R2Count == 11, "实测 " + g.R2Count);
            Check("R3 同类三消 = 460", g.R3Count == 460, "实测 " + g.R3Count);
            Check("合计 = 535", g.TripleCount == 535, "实测 " + g.TripleCount);

            Console.WriteLine("    明细: R1 " + g.R1Count + " / R2 " + g.R2Count + " / R3 " + g.R3Count
                              + " = " + g.TripleCount);
        }

        // ═══════════════════════ T3 家庭划分 ═══════════════════════

        static void T3_FamilyDistribution(FamilyGraph g)
        {
            Section("T3 家庭划分（方块副标题显示的就是这个）");

            var counts = new Dictionary<string, int>();
            for (int i = 0; i < g.Count; i++)
            {
                string fam = g.FamilyTitle(i);
                int c;
                counts.TryGetValue(fam, out c);
                counts[fam] = c + 1;
            }

            Check("家庭数 = 10", counts.Count == 10, "实测 " + counts.Count);

            var expected = new Dictionary<string, int>
            {
                { "爷爷家", 9 }, { "外公家", 7 }, { "爸爸家", 7 },
                { "伯父家", 4 }, { "叔父家", 4 }, { "姑妈家", 4 }, { "舅舅家", 4 }, { "姨妈家", 4 },
                { "哥哥家", 2 }, { "姐姐家", 2 }
            };

            var problems = new List<string>();
            foreach (KeyValuePair<string, int> pair in expected)
            {
                int actual;
                counts.TryGetValue(pair.Key, out actual);
                if (actual != pair.Value) problems.Add(pair.Key + " 期望 " + pair.Value + " 实测 " + actual);
            }
            Check("各家庭人数与终检一致", problems.Count == 0, Join(problems));

            var keys = new List<string>(counts.Keys);
            keys.Sort();
            for (int i = 0; i < keys.Count; i++)
            {
                Console.WriteLine("    " + keys[i] + " " + counts[keys[i]] + " 人");
            }
        }

        // ═══════════════════════ T4 开局生成 ═══════════════════════

        static void T4_Generation(FamilyGraph g)
        {
            Section("T4 开局生成 300 次");

            var rng = new Random(20261118);
            var detector = new MatchDetector(g);
            int[] roster = AllIndices(g);
            var pool = new CellPool(g, rng, roster, 2);
            var generator = new BoardGenerator(g, detector, pool, rng, 3, 400);

            Console.WriteLine("    同名上限 " + pool.Cap + "，可容纳 " + pool.CapacitySlots + " 格（盘面 64 格）");
            if (pool.CapacitySlots < BoardModel.CellCount)
            {
                _notes.Add("同名上限 " + pool.Cap + " × 可用称谓 " + (pool.CapacitySlots / pool.Cap)
                           + " 只有 " + pool.CapacitySlots + " 格 < 64，约束装不满盘面，会退化为纯随机");
            }

            const int rounds = 300;
            int withMatch = 0;
            int tooFewMoves = 0;
            int failed = 0;
            var attempts = new List<int>(rounds);
            var moveCounts = new List<int>(rounds);

            for (int i = 0; i < rounds; i++)
            {
                try
                {
                    BoardModel board = generator.Build();
                    attempts.Add(generator.LastAttempts);
                    if (detector.HasMatch(board)) withMatch++;

                    int moves = detector.CountMoves(board);
                    moveCounts.Add(moves);
                    if (moves < 3) tooFewMoves++;
                }
                catch (Exception e)
                {
                    failed++;
                    if (failed == 1) Console.WriteLine("    首次失败: " + e.Message);
                }
            }

            Check("生成失败 0 次", failed == 0, "失败 " + failed + " 次");
            Check("自带现成三连 0 次", withMatch == 0, "出现 " + withMatch + " 次");
            Check("可走步数 < 3 的盘面 0 个", tooFewMoves == 0, "出现 " + tooFewMoves + " 个");

            if (attempts.Count > 0)
            {
                int minAttempts = int.MaxValue;
                int maxAttempts = int.MinValue;
                for (int i = 0; i < attempts.Count; i++)
                {
                    if (attempts[i] >= 0 && attempts[i] < minAttempts) minAttempts = attempts[i];
                    if (attempts[i] > maxAttempts) maxAttempts = attempts[i];
                }
                Console.WriteLine("    尝试次数: 最少 " + minAttempts + " / 中位 " + Median(attempts)
                                  + " / 最多 " + maxAttempts + "（终检基线 中位 1、最多 5）");
            }

            if (moveCounts.Count > 0)
            {
                int minMoves = int.MaxValue;
                for (int i = 0; i < moveCounts.Count; i++) if (moveCounts[i] < minMoves) minMoves = moveCounts[i];
                Console.WriteLine("    开局面可走步数: 最少 " + minMoves + " / 中位 " + Median(moveCounts)
                                  + "（终检基线 最少 3、中位 9）");
            }
        }

        // ═══════════════════════ T5~T7 对局 ═══════════════════════

        sealed class GameStat
        {
            public int Shuffles;
            public int Groups;
            public int Moves;
            public int Score;
            public int Stars;
            public int MaxRound;
            public int NoMatchStates;
            public readonly int[] Rules = new int[3];
        }

        static void T5_Gameplay(FamilyGraph g)
        {
            Section("T5~T7 对局模拟（20 局 × 60 手，随机打法）");

            var rng = new Random(777);
            var detector = new MatchDetector(g);
            int[] roster = AllIndices(g);
            var pool = new CellPool(g, rng, roster, 2);
            var generator = new BoardGenerator(g, detector, pool, rng, 3, 400);
            var resolver = new BoardResolver(detector, pool);
            var validator = new MoveValidator(detector);

            const int games = 20;
            var stats = new List<GameStat>(games);
            var scores = new List<int>(games);
            var starCounts = new int[4];

            for (int i = 0; i < games; i++)
            {
                GameStat stat = PlayOne(generator, detector, resolver, validator, rng);
                stats.Add(stat);
                scores.Add(stat.Score);
                if (stat.Stars >= 0 && stat.Stars <= 3) starCounts[stat.Stars]++;
            }

            int totalShuffles = 0, totalGroups = 0, totalMoves = 0, totalNoMatch = 0;
            var rules = new int[3];
            for (int i = 0; i < stats.Count; i++)
            {
                totalShuffles += stats[i].Shuffles;
                totalGroups += stats[i].Groups;
                totalMoves += stats[i].Moves;
                totalNoMatch += stats[i].NoMatchStates;
                for (int k = 0; k < 3; k++) rules[k] += stats[i].Rules[k];
            }

            double shufflePerGame = (double)totalShuffles / games;
            double groupsPerMove = totalMoves == 0 ? 0 : (double)totalGroups / totalMoves;
            double ruleSum = rules[0] + rules[1] + rules[2];
            double p1 = ruleSum == 0 ? 0 : rules[0] / ruleSum * 100;
            double p2 = ruleSum == 0 ? 0 : rules[1] / ruleSum * 100;
            double p3 = ruleSum == 0 ? 0 : rules[2] / ruleSum * 100;

            Console.WriteLine("    洗牌 " + shufflePerGame.ToString("F1") + " 次/局（终检基线 2.5）");
            Console.WriteLine("    每手消除 " + groupsPerMove.ToString("F2") + " 组（终检基线 1.43）");
            Console.WriteLine("    规则占比 R1 " + p1.ToString("F0") + "% / R2 " + p2.ToString("F0")
                              + "% / R3 " + p3.ToString("F0") + "%（终检基线 46 / 13 / 41）");

            int minScore = int.MaxValue, maxScore = int.MinValue, sumScore = 0;
            for (int i = 0; i < scores.Count; i++)
            {
                if (scores[i] < minScore) minScore = scores[i];
                if (scores[i] > maxScore) maxScore = scores[i];
                sumScore += scores[i];
            }
            Console.WriteLine("    得分: 最低 " + minScore + " / 中位 " + Median(scores)
                              + " / 最高 " + maxScore + " / 均值 " + (sumScore / games)
                              + "（终检基线 中位 27150）");
            Console.WriteLine("    星级分布: 0★ " + starCounts[0] + " / 1★ " + starCounts[1]
                              + " / 2★ " + starCounts[2] + " / 3★ " + starCounts[3]);

            // 与基线比对，允许 ±15%（随机模拟的正常波动）
            Check("洗牌次数在基线 ±15% 内", Within(shufflePerGame, 2.5, 0.15),
                "实测 " + shufflePerGame.ToString("F1") + "，基线 2.5");
            Check("每手消除组数在基线 ±15% 内", Within(groupsPerMove, 1.43, 0.15),
                "实测 " + groupsPerMove.ToString("F2") + "，基线 1.43");
            Check("R1 占比在基线 ±15% 内", Within(p1, 46, 0.15),
                "实测 " + p1.ToString("F0") + "%，基线 46%");
            Check("得分中位在基线 ±15% 内", Within(Median(scores), 27150, 0.15),
                "实测 " + Median(scores) + "，基线 27150");

            if (totalNoMatch == 0)
            {
                _notes.Add("对局中没有记录到「无现成三连」的状态，统计口径可能有问题");
            }
        }

        static GameStat PlayOne(
            BoardGenerator generator,
            MatchDetector detector,
            BoardResolver resolver,
            MoveValidator validator,
            Random rng)
        {
            var stat = new GameStat();
            var score = new ScoreKeeper();
            BoardModel board = generator.Build();
            var moves = new List<int>(24);

            int guard = 0;
            while (!score.IsFinished && guard++ < 5000)
            {
                if (detector.HasMatch(board))
                {
                    // 正常情况下不会走到这里（每次交换后都结算干净了），兜底防止死循环
                    ApplyRounds(resolver.Resolve(board), score, stat);
                    continue;
                }

                stat.NoMatchStates++;
                moves.Clear();
                CollectValidMoves(board, detector, moves);

                if (moves.Count == 0)
                {
                    stat.Shuffles++;
                    board = generator.Build();
                    continue;
                }

                int key = moves[rng.Next(moves.Count)];
                int a = key / BoardModel.CellCount;
                int b = key % BoardModel.CellCount;

                if (!validator.TrySwap(board, a, b))
                {
                    // 理论上不会发生：候选集合里的每一对都验证过
                    continue;
                }

                score.ConsumeMove();
                stat.Moves++;
                ApplyRounds(resolver.Resolve(board), score, stat);
            }

            stat.Score = score.Score;
            stat.Stars = score.Stars;
            return stat;
        }

        static void ApplyRounds(
            List<BoardResolver.RoundResult> rounds,
            ScoreKeeper score,
            GameStat stat)
        {
            for (int i = 0; i < rounds.Count; i++)
            {
                BoardResolver.RoundResult round = rounds[i];
                if (round.Round > stat.MaxRound) stat.MaxRound = round.Round;

                for (int k = 0; k < round.GroupCount; k++)
                {
                    MatchGroup group = round.Groups[k];
                    score.Award(group.Rule, round.Round);
                    stat.Groups++;
                    stat.Rules[(int)group.Rule]++;
                }
            }
        }

        static void CollectValidMoves(BoardModel board, MatchDetector detector, List<int> into)
        {
            for (int r = 0; r < Rows; r++)
            {
                for (int c = 0; c < Cols; c++)
                {
                    int flat = r * Cols + c;
                    if (c + 1 < Cols) TryPair(board, detector, flat, flat + 1, into);
                    if (r + 1 < Rows) TryPair(board, detector, flat, flat + Cols, into);
                }
            }
        }

        static void TryPair(BoardModel board, MatchDetector detector, int a, int b, List<int> into)
        {
            Swap(board, a, b);
            bool ok = detector.HasMatch(board);
            Swap(board, a, b);
            if (ok) into.Add(a * BoardModel.CellCount + b);
        }

        static void Swap(BoardModel board, int a, int b)
        {
            int t = board[a];
            board[a] = board[b];
            board[b] = t;
        }

        // ═══════════════════════ 工具 ═══════════════════════

        static int[] AllIndices(FamilyGraph g)
        {
            int[] arr = new int[g.Count];
            for (int i = 0; i < arr.Length; i++) arr[i] = i;
            return arr;
        }

        static void Section(string title)
        {
            Console.WriteLine(title);
        }

        static void Check(string name, bool ok, string detail)
        {
            if (ok)
            {
                _pass++;
                Console.WriteLine("  [PASS] " + name);
            }
            else
            {
                _fail++;
                Console.WriteLine("  [FAIL] " + name + (string.IsNullOrEmpty(detail) ? "" : " —— " + detail));
            }
        }

        static bool Within(double actual, double baseline, double tolerance)
        {
            if (baseline == 0) return Math.Abs(actual) < 0.0001;
            return Math.Abs(actual - baseline) / Math.Abs(baseline) <= tolerance;
        }

        static int Median(List<int> values)
        {
            if (values.Count == 0) return 0;
            var copy = new List<int>(values);
            copy.Sort();
            return copy[copy.Count / 2];
        }

        static string Join(List<string> items)
        {
            if (items.Count == 0) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < items.Count && i < 6; i++)
            {
                if (i > 0) sb.Append("; ");
                sb.Append(items[i]);
            }
            if (items.Count > 6) sb.Append(" …共 " + items.Count + " 条");
            return sb.ToString();
        }
    }
}
