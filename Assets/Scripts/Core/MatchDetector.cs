using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>
    /// 匹配检测：扫 8×8 盘面的全部 96 个「三连窗口」（横 48 + 竖 48，不含对角线），
    /// 按优先级占位去重，输出本帧应该消除的组合。
    ///
    /// 冲突裁决（DOC-02 第 5 节）：
    ///   ① 优先级 R1 → R2 → R3
    ///   ② 同级按「行优先 → 列优先」
    ///   ③ 逐条占位，格子被占过的候选直接丢弃（先到先得）
    ///
    /// 为什么是「先到先得」而不是全消：一格被两条组合同时消耗会导致双重计分，玩家看着也乱。
    ///
    /// @trace MECH-008
    /// </summary>
    public sealed class MatchDetector
    {
        readonly FamilyGraph _graph;

        public MatchDetector(FamilyGraph graph)
        {
            _graph = graph;
        }

        /// <summary>是否已经存在现成三连。发现第一个就返回，用于生成期的快速校验。</summary>
        public bool HasMatch(BoardModel board)
        {
            for (int r = 0; r < BoardModel.Rows; r++)
            {
                int rowBase = r * BoardModel.Cols;
                for (int c = 0; c <= BoardModel.Cols - 3; c++)
                {
                    int a = board[rowBase + c];
                    if (a < 0) continue;
                    int b = board[rowBase + c + 1];
                    if (b < 0) continue;
                    int d = board[rowBase + c + 2];
                    if (d < 0) continue;
                    RuleKind rule;
                    if (_graph.TryClassify(a, b, d, out rule)) return true;
                }
            }

            for (int c = 0; c < BoardModel.Cols; c++)
            {
                for (int r = 0; r <= BoardModel.Rows - 3; r++)
                {
                    int a = board[r * BoardModel.Cols + c];
                    if (a < 0) continue;
                    int b = board[(r + 1) * BoardModel.Cols + c];
                    if (b < 0) continue;
                    int d = board[(r + 2) * BoardModel.Cols + c];
                    if (d < 0) continue;
                    RuleKind rule;
                    if (_graph.TryClassify(a, b, d, out rule)) return true;
                }
            }
            return false;
        }

        /// <summary>找出本帧全部待消除组合（已按优先级 + 行列顺序去重）。会分配新列表，仅用于测试与低频调用。</summary>
        public List<MatchGroup> FindMatches(BoardModel board)
        {
            var result = new List<MatchGroup>(16);
            FindMatches(board, result);
            return result;
        }

        /// <summary>同上，但写入调用方提供的列表，避免每帧分配。玩法循环应使用这个重载。</summary>
        public void FindMatches(BoardModel board, List<MatchGroup> into)
        {
            into.Clear();
            var raw = new List<MatchGroup>(24);
            Collect(board, raw);

            if (raw.Count == 0) return;

            // ① 优先级，② 行优先 → 列优先
            raw.Sort(CompareGroup);

            // ③ 逐条占位，先到先得
            var used = new bool[BoardModel.CellCount];
            for (int i = 0; i < raw.Count; i++)
            {
                MatchGroup g = raw[i];
                if (used[g.CellA] || used[g.CellB] || used[g.CellC]) continue;
                used[g.CellA] = true;
                used[g.CellB] = true;
                used[g.CellC] = true;
                into.Add(g);
            }
        }

        static int CompareGroup(MatchGroup x, MatchGroup y)
        {
            int ruleCompare = ((int)x.Rule).CompareTo((int)y.Rule);
            if (ruleCompare != 0) return ruleCompare;
            return x.FirstCell.CompareTo(y.FirstCell);
        }

        void Collect(BoardModel board, List<MatchGroup> raw)
        {
            for (int r = 0; r < BoardModel.Rows; r++)
            {
                int rowBase = r * BoardModel.Cols;
                for (int c = 0; c <= BoardModel.Cols - 3; c++)
                {
                    int a = board[rowBase + c];
                    int b = board[rowBase + c + 1];
                    int d = board[rowBase + c + 2];
                    if (a < 0 || b < 0 || d < 0) continue;
                    RuleKind rule;
                    if (!_graph.TryClassify(a, b, d, out rule)) continue;
                    raw.Add(new MatchGroup(rule, rowBase + c, rowBase + c + 1, rowBase + c + 2));
                }
            }

            for (int c = 0; c < BoardModel.Cols; c++)
            {
                for (int r = 0; r <= BoardModel.Rows - 3; r++)
                {
                    int a = board[r * BoardModel.Cols + c];
                    int b = board[(r + 1) * BoardModel.Cols + c];
                    int d = board[(r + 2) * BoardModel.Cols + c];
                    if (a < 0 || b < 0 || d < 0) continue;
                    RuleKind rule;
                    if (!_graph.TryClassify(a, b, d, out rule)) continue;
                    raw.Add(new MatchGroup(
                        rule,
                        r * BoardModel.Cols + c,
                        (r + 1) * BoardModel.Cols + c,
                        (r + 2) * BoardModel.Cols + c));
                }
            }
        }

        /// <summary>
        /// 在 (row,col) 这一格填入某个人之后，是否会形成三连。
        /// 只检查经过这一格的窗口（最多 6 个），用于生成器逐格填坑时的高频校验。
        /// </summary>
        public bool CellCreatesMatch(BoardModel board, int flat)
        {
            int row = BoardModel.RowOf(flat);
            int col = BoardModel.ColOf(flat);

            for (int start = col - 2; start <= col; start++)
            {
                if (start < 0 || start + 2 >= BoardModel.Cols) continue;
                if (CheckWindow(board, BoardModel.ToFlat(row, start), 0, 1)) return true;
            }

            for (int start = row - 2; start <= row; start++)
            {
                if (start < 0 || start + 2 >= BoardModel.Rows) continue;
                if (CheckWindow(board, BoardModel.ToFlat(start, col), 1, 0)) return true;
            }

            return false;
        }

        bool CheckWindow(BoardModel board, int startFlat, int deltaRow, int deltaCol)
        {
            int step = deltaRow * BoardModel.Cols + deltaCol;
            int a = board[startFlat];
            int b = board[startFlat + step];
            int c = board[startFlat + step * 2];
            if (a < 0 || b < 0 || c < 0) return false;
            RuleKind rule;
            return _graph.TryClassify(a, b, c, out rule);
        }

        /// <summary>
        /// 统计「有多少个相邻交换能形成三连」。用于生成期要求可走步数 ≥ 3（F-01 开局保底）。
        /// stopAt 用于提前退出：只要确认达到下限就停，不必数完。
        /// </summary>
        public int CountMoves(BoardModel board, int stopAt = int.MaxValue)
        {
            int count = 0;
            for (int r = 0; r < BoardModel.Rows; r++)
            {
                for (int c = 0; c < BoardModel.Cols; c++)
                {
                    int flat = BoardModel.ToFlat(r, c);

                    if (c + 1 < BoardModel.Cols)
                    {
                        int right = flat + 1;
                        SwapCells(board, flat, right);
                        bool ok = HasMatch(board);
                        SwapCells(board, flat, right);
                        if (ok)
                        {
                            count++;
                            if (count >= stopAt) return count;
                        }
                    }

                    if (r + 1 < BoardModel.Rows)
                    {
                        int down = flat + BoardModel.Cols;
                        SwapCells(board, flat, down);
                        bool ok = HasMatch(board);
                        SwapCells(board, flat, down);
                        if (ok)
                        {
                            count++;
                            if (count >= stopAt) return count;
                        }
                    }
                }
            }
            return count;
        }

        static void SwapCells(BoardModel board, int a, int b)
        {
            int t = board[a];
            board[a] = board[b];
            board[b] = t;
        }
    }
}
