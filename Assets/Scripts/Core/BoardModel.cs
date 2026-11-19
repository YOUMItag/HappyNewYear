namespace Game.Core
{
    /// <summary>
    /// 盘面数据模型：8×8 网格，存人员索引，-1 表示空位。
    /// 只有数据，没有任何表现出逻辑 —— 视觉层通过 Subscribe 式的轮询或事件自行同步。
    ///
    /// 为什么用一维数组：64 格访问次数极多（每次判定要扫 96 个窗口），一维数组比二维少一次索引计算，
    /// 且能直接拿扁平索引做 HashSet 键与位运算。
    ///
    /// @trace MECH-001
    /// </summary>
    public sealed class BoardModel
    {
        public const int Rows = FamilyGraph.Rows;
        public const int Cols = FamilyGraph.Cols;
        public const int CellCount = FamilyGraph.CellCount;
        public const int Empty = -1;

        readonly int[] _cells;

        public BoardModel()
        {
            _cells = new int[CellCount];
            Clear();
        }

        public int this[int row, int col]
        {
            get { return _cells[row * Cols + col]; }
            set { _cells[row * Cols + col] = value; }
        }

        public int this[int flat]
        {
            get { return _cells[flat]; }
            set { _cells[flat] = value; }
        }

        public void Clear()
        {
            for (int i = 0; i < CellCount; i++) _cells[i] = Empty;
        }

        /// <summary>
        /// 复制一份当前盘面。给表现层用：结算时每一轮都要留一张「落稳之后」的快照，
        /// 回放动画时才能算出每个方块从哪一格落到哪一格。
        /// </summary>
        public BoardModel Clone()
        {
            var copy = new BoardModel();
            CopyTo(copy);
            return copy;
        }

        public bool IsEmpty(int flat)
        {
            return _cells[flat] == Empty;
        }

        public void CopyTo(BoardModel target)
        {
            for (int i = 0; i < CellCount; i++) target._cells[i] = _cells[i];
        }

        public static int ToFlat(int row, int col)
        {
            return row * Cols + col;
        }

        public static int RowOf(int flat)
        {
            return flat / Cols;
        }

        public static int ColOf(int flat)
        {
            return flat % Cols;
        }

        /// <summary>先复制一份「没执行交换」的盘面，供 MoveValidator 回退用。</summary>
        public int[] Snapshot()
        {
            int[] copy = new int[CellCount];
            for (int i = 0; i < CellCount; i++) copy[i] = _cells[i];
            return copy;
        }

        public void Restore(int[] snapshot)
        {
            for (int i = 0; i < CellCount; i++) _cells[i] = snapshot[i];
        }
    }
}
