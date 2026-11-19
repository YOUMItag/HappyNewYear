using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>
    /// 盘面生成器：用「反向构造」造出「差一格就能消」的盘面。
    ///
    /// 核心思路（DOC-05 已验证）：
    ///   1. 从全部合法三元组里随机抽一条 (A,B,C)
    ///   2. 在一条 4 格线上摆成 A B [挡板X] C
    ///   3. 挡板 X 必须让「A B X」不成链，否则开局就有现成三连
    ///   4. 其余格子按顺序逐格填，每填一格都验证不产生三连
    ///   5. 整体校验：无现成三连 + 可走步数 ≥ 3
    ///
    /// 玩家看到的是「差一格」，把 C 挪到 X 的位置就成链 —— 这就是新手第一手必然可做的事。
    ///
    /// 实测（DOC-05）：300 次生成，自带三连 0 次、无合法步 0 次、尝试次数中位 1 次。
    ///
    /// @trace MECH-009, MECH-011
    /// </summary>
    public sealed class BoardGenerator
    {
        readonly FamilyGraph _graph;
        readonly MatchDetector _detector;
        readonly CellPool _pool;
        readonly Random _rng;
        readonly int _minMoves;
        readonly int _maxAttempts;

        readonly List<int> _empties = new List<int>(BoardModel.CellCount);
        readonly List<int> _candidates = new List<int>(BoardModel.CellCount);
        readonly List<int> _decoys = new List<int>(BoardModel.CellCount);

        public BoardGenerator(
            FamilyGraph graph,
            MatchDetector detector,
            CellPool pool,
            Random rng,
            int minMoves = 3,
            int maxAttempts = 400)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));
            if (detector == null) throw new ArgumentNullException(nameof(detector));
            if (pool == null) throw new ArgumentNullException(nameof(pool));
            if (rng == null) throw new ArgumentNullException(nameof(rng));

            _graph = graph;
            _detector = detector;
            _pool = pool;
            _rng = rng;
            _minMoves = minMoves;
            _maxAttempts = maxAttempts;
        }

        /// <summary>上一次 Build 用了几次尝试。-1 表示走了兜底的纯随机填充。</summary>
        public int LastAttempts { get; private set; }

        public BoardModel Build()
        {
            for (int attempt = 1; attempt <= _maxAttempts; attempt++)
            {
                LastAttempts = attempt;
                BoardModel board = TryBuildOnce();
                if (board == null) continue;

                // 校验①：不能有现成三连（否则玩家还没操作，盘面就自己开始消了）
                if (_detector.HasMatch(board)) continue;

                // 校验②：可走步数 ≥ 3（F-01 开局保底，避免「看两眼找不到能动的」）
                if (_detector.CountMoves(board, _minMoves) < _minMoves) continue;

                return board;
            }

            // 兜底：反向构造全部失败时退回纯随机，逐步筛到合格为止
            for (int i = 0; i < 4000; i++)
            {
                LastAttempts = -1;
                var board = new BoardModel();
                _pool.ResetCounts(board);
                for (int cell = 0; cell < BoardModel.CellCount; cell++) board[cell] = _pool.Next();

                if (_detector.HasMatch(board)) continue;
                if (_detector.CountMoves(board, _minMoves) < _minMoves) continue;
                return board;
            }

            throw new InvalidOperationException(
                "盘面生成失败：" + _maxAttempts + " 次反向构造 + 4000 次随机填充均未得到合法盘面。" +
                "请检查族谱数据（三元组数量是否过少）或降低 minMoves。");
        }

        BoardModel TryBuildOnce()
        {
            IReadOnlyList<int[]> triples = _graph.Triples;
            if (triples.Count == 0) return null;

            int[] triple = triples[_rng.Next(triples.Count)];
            int p0 = triple[0];
            int p1 = triple[1];
            int p2 = triple[2];

            bool horizontal = _rng.Next(2) == 0;
            int baseRow, baseCol, dr, dc;
            if (horizontal)
            {
                baseRow = _rng.Next(BoardModel.Rows);
                baseCol = _rng.Next(BoardModel.Cols - 3);
                dr = 0;
                dc = 1;
            }
            else
            {
                baseRow = _rng.Next(BoardModel.Rows - 3);
                baseCol = _rng.Next(BoardModel.Cols);
                dr = 1;
                dc = 0;
            }

            int CellAt(int offset)
            {
                return BoardModel.ToFlat(baseRow + dr * offset, baseCol + dc * offset);
            }

            var board = new BoardModel();
            board[CellAt(0)] = p0;
            board[CellAt(1)] = p1;
            board[CellAt(3)] = p2;

            // 挡板：放在第 3 格（索引 2），必须不能与 p0、p1 组成合法三连
            _decoys.Clear();
            int[] roster = _pool.Roster;
            for (int i = 0; i < roster.Length; i++)
            {
                RuleKind ignored;
                if (!_graph.TryClassify(p0, p1, roster[i], out ignored)) _decoys.Add(roster[i]);
            }
            if (_decoys.Count == 0) return null;
            board[CellAt(2)] = _decoys[_rng.Next(_decoys.Count)];

            // 其余格子逐格填，每格都验证不产生三连
            _pool.ResetCounts(board);
            _empties.Clear();
            for (int cell = 0; cell < BoardModel.CellCount; cell++)
            {
                if (board[cell] < 0) _empties.Add(cell);
            }
            Shuffle(_empties);

            for (int i = 0; i < _empties.Count; i++)
            {
                int cell = _empties[i];

                _pool.FillEligible(_candidates);
                if (_candidates.Count == 0) _candidates.AddRange(roster);
                Shuffle(_candidates);

                int placed = BoardModel.Empty;
                for (int k = 0; k < _candidates.Count; k++)
                {
                    int person = _candidates[k];
                    board[cell] = person;
                    if (!_detector.CellCreatesMatch(board, cell))
                    {
                        placed = person;
                        break;
                    }
                }

                if (placed == BoardModel.Empty)
                {
                    board[cell] = BoardModel.Empty;
                    return null;
                }

                board[cell] = placed;
                _pool.AddCount(placed);
            }

            return board;
        }

        void Shuffle(List<int> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                int t = list[i];
                list[i] = list[j];
                list[j] = t;
            }
        }
    }
}
