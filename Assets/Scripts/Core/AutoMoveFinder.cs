using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>
    /// 自动选步：枚举当前盘面上所有「交换后能形成三连」的相邻对。
    ///
    /// 两个地方共用：
    ///   1. 游戏内自动演示（按 F9 让游戏自己打）—— 你不用手点也能看 UI 与结算面板
    ///   2. 离线批量对局脚本（Tools/Playtest）—— 几十秒跑几百局，统计难度分布
    ///
    /// 抽出来共用是为了避免两处各写一份「找合法步」，一旦判定改了两边会不一致。
    ///
    /// 注意：这里返回的是**所有**合法步，用 List 承载；
    /// 与 MatchDetector.CountMoves（只要个数、可提前退出）用途不同，不要互相替代 ——
    /// 生成期只需要「够不够 3 步」，用 CountMoves 更快。
    ///
    /// @trace MECH-004
    /// </summary>
    public static class AutoMoveFinder
    {
        /// <summary>把全部合法步写入 into，返回个数。每个元素编码为 a * CellCount + b（a &lt; b）。</summary>
        public static int Collect(BoardModel board, MatchDetector detector, List<int> into)
        {
            if (board == null) throw new ArgumentNullException(nameof(board));
            if (detector == null) throw new ArgumentNullException(nameof(detector));
            if (into == null) throw new ArgumentNullException(nameof(into));

            into.Clear();

            for (int row = 0; row < BoardModel.Rows; row++)
            {
                for (int col = 0; col < BoardModel.Cols; col++)
                {
                    int flat = row * BoardModel.Cols + col;
                    if (col + 1 < BoardModel.Cols) TryPair(board, detector, flat, flat + 1, into);
                    if (row + 1 < BoardModel.Rows) TryPair(board, detector, flat, flat + BoardModel.Cols, into);
                }
            }
            return into.Count;
        }

        /// <summary>随机挑一个合法步。没有合法步时返回 false。</summary>
        public static bool TryPick(
            BoardModel board, MatchDetector detector, Random rng,
            List<int> buffer, out int flatA, out int flatB)
        {
            flatA = -1;
            flatB = -1;

            if (Collect(board, detector, buffer) == 0) return false;

            int key = buffer[rng.Next(buffer.Count)];
            flatA = key / BoardModel.CellCount;
            flatB = key % BoardModel.CellCount;
            return true;
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
    }
}
