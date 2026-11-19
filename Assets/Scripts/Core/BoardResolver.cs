using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>
    /// 消除结算：消除 → 重力下落 → 顶部补充 → 再检测，直到盘面稳定。
    ///
    /// 每一轮（Round）的分数倍率不同（连锁倍率 1 + 0.25×(k−1)，见 MECH-013），
    /// 所以必须把「第几轮」明确交回给调用方，而不是在这里直接加分。
    ///
    /// 上限 30 轮是保险丝：正常盘面的连锁在 4 轮内就停了（DOC-05 实测 1 轮 72%、2 轮 21%），
    /// 超过 30 轮说明判定或补充逻辑出错，必须停而不是让游戏卡死。
    ///
    /// @trace MECH-010
    /// </summary>
    public sealed class BoardResolver
    {
        public const int MaxRounds = 30;

        readonly MatchDetector _detector;
        readonly CellPool _pool;
        readonly List<MatchGroup> _buffer = new List<MatchGroup>(24);

        public BoardResolver(MatchDetector detector, CellPool pool)
        {
            if (detector == null) throw new ArgumentNullException(nameof(detector));
            if (pool == null) throw new ArgumentNullException(nameof(pool));
            _detector = detector;
            _pool = pool;
        }

        /// <summary>一轮结算的结果。Round 从 1 开始，用于算连锁倍率。</summary>
        public sealed class RoundResult
        {
            readonly List<MatchGroup> _groups;
            readonly int[] _cleared;

            public RoundResult(int round, List<MatchGroup> groups, int[] cleared, BoardModel afterGravity)
            {
                Round = round;
                _groups = groups;
                _cleared = cleared;
                BoardAfterGravity = afterGravity;
            }

            public int Round { get; private set; }
            public int GroupCount { get { return _groups.Count; } }
            public IReadOnlyList<MatchGroup> Groups { get { return _groups; } }

            /// <summary>本轮被消掉的格子（扁平索引）。表现层按它播消除动画。</summary>
            public IReadOnlyList<int> ClearedCells { get { return _cleared; } }

            /// <summary>本轮「消完 + 落稳 + 补满」之后的盘面快照。表现层按相邻两轮快照算下落位移。</summary>
            public BoardModel BoardAfterGravity { get; private set; }
        }

        /// <summary>把盘面结算到稳定状态，返回逐轮明细。</summary>
        public List<RoundResult> Resolve(BoardModel board)
        {
            var rounds = new List<RoundResult>(4);

            for (int round = 1; round <= MaxRounds; round++)
            {
                _detector.FindMatches(board, _buffer);
                if (_buffer.Count == 0) break;

                var groups = new List<MatchGroup>(_buffer.Count);
                for (int i = 0; i < _buffer.Count; i++) groups.Add(_buffer[i]);

                // 每组的三个格子拼成消除清单。检测器保证同一格不会落在两组里，不必去重。
                var cleared = new int[groups.Count * 3];
                for (int i = 0; i < groups.Count; i++)
                {
                    MatchGroup g = groups[i];
                    board[g.CellA] = BoardModel.Empty;
                    board[g.CellB] = BoardModel.Empty;
                    board[g.CellC] = BoardModel.Empty;

                    cleared[i * 3] = g.CellA;
                    cleared[i * 3 + 1] = g.CellB;
                    cleared[i * 3 + 2] = g.CellC;
                }

                Gravity(board);

                rounds.Add(new RoundResult(round, groups, cleared, board.Clone()));
            }

            return rounds;
        }

        /// <summary>
        /// 重力下落 + 顶部补充。逐列把非空方块压到底部，空出的格子用 CellPool 补。
        /// 不用物理引擎：逻辑网格必须严格整数对齐，物理下落会引入小数偏移与堆叠抖动。
        /// </summary>
        public void Gravity(BoardModel board)
        {
            _pool.ResetCounts(board);

            for (int col = 0; col < BoardModel.Cols; col++)
            {
                int writeRow = BoardModel.Rows - 1;

                for (int row = BoardModel.Rows - 1; row >= 0; row--)
                {
                    int value = board[row, col];
                    if (value == BoardModel.Empty) continue;

                    if (writeRow != row)
                    {
                        board[writeRow, col] = value;
                        board[row, col] = BoardModel.Empty;
                    }
                    writeRow--;
                }

                for (int row = writeRow; row >= 0; row--)
                {
                    board[row, col] = _pool.Next();
                }
            }
        }
    }
}
