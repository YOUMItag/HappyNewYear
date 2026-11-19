namespace Game.Core
{
    /// <summary>
    /// 交换校验：相邻两格交换后能否形成三连。不能则原样换回，且不计步（DOC-04 MECH-004）。
    ///
    /// 为什么不用「先改数据再看要不要回退」的写法：棋盘是唯一真相源，视觉层只是它的投影。
    /// 交换失败必须把棋盘恢复原状，否则会留下一个「视觉上没换、数据上换了」的脏状态。
    ///
    /// @trace MECH-004
    /// </summary>
    public sealed class MoveValidator
    {
        readonly MatchDetector _detector;

        public MoveValidator(MatchDetector detector)
        {
            _detector = detector;
        }

        /// <summary>两格是否四邻相接（不含斜角）。</summary>
        public static bool AreAdjacent(int flatA, int flatB)
        {
            if (flatA < 0 || flatB < 0) return false;
            if (flatA >= BoardModel.CellCount || flatB >= BoardModel.CellCount) return false;

            int rowA = BoardModel.RowOf(flatA);
            int colA = BoardModel.ColOf(flatA);
            int rowB = BoardModel.RowOf(flatB);
            int colB = BoardModel.ColOf(flatB);

            int distance = (rowA > rowB ? rowA - rowB : rowB - rowA)
                         + (colA > colB ? colA - colB : colB - colA);

            return distance == 1;
        }

        /// <summary>
        /// 尝试交换。返回 true 表示交换成功（已保留交换结果，调用方应继续结算）；
        /// 返回 false 表示不合法（盘面已恢复原状，调用方应播放「弹回」反馈且不计步）。
        /// </summary>
        public bool TrySwap(BoardModel board, int flatA, int flatB)
        {
            if (!AreAdjacent(flatA, flatB)) return false;

            Swap(board, flatA, flatB);
            if (_detector.HasMatch(board)) return true;

            Swap(board, flatA, flatB);
            return false;
        }

        static void Swap(BoardModel board, int a, int b)
        {
            int t = board[a];
            board[a] = board[b];
            board[b] = t;
        }
    }
}
