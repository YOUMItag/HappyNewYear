using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>
    /// 方块供应池：按「同名上限」挑下一个该出现的方块。
    ///
    /// 为什么需要它：R3 要求三人称谓互异，所以「两个同名方块挨在一起」是永远消不掉的死邻接。
    /// 实测（DOC-05）同名上限设为 2 时，每局洗牌次数从 9.1 次降到 2.5 次，降幅约 70%。
    /// 上限 3 无效 —— 因为 35 种称谓 × 3 = 105，远大于 64 格，约束根本卡不住。
    ///
    /// 注意：这是「生成规则」，不是玩法规则。它不改变任何判定，只改变方块怎么掉下来。
    ///
    /// @trace MECH-011
    /// </summary>
    public sealed class CellPool
    {
        readonly FamilyGraph _graph;
        readonly Random _rng;
        readonly int[] _roster;
        readonly int _cap;
        readonly int[][] _byTitle;
        readonly int[] _counts;
        readonly List<int> _eligibleTitles;

        /// <param name="cap">同名上限。0 或负数表示不限。</param>
        public CellPool(FamilyGraph graph, Random rng, int[] roster, int cap)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            if (roster == null || roster.Length == 0) throw new ArgumentException("roster 不能为空", nameof(roster));

            _graph = graph;
            _rng = rng;
            _roster = (int[])roster.Clone();
            _cap = cap;

            int titleCount = graph.TitleCount;
            _counts = new int[titleCount];
            _eligibleTitles = new List<int>(titleCount);

            var groups = new List<int>[titleCount];
            for (int i = 0; i < titleCount; i++) groups[i] = new List<int>(4);
            for (int i = 0; i < _roster.Length; i++)
            {
                groups[graph.TitleIndex(_roster[i])].Add(_roster[i]);
            }
            _byTitle = new int[titleCount][];
            for (int i = 0; i < titleCount; i++) _byTitle[i] = groups[i].ToArray();
        }

        public int Cap { get { return _cap; } }
        public int[] Roster { get { return _roster; } }

        /// <summary>理论上能放满的格数 = 可用称谓数 × 上限。小于 64 说明这条约束装不满盘面。</summary>
        public int CapacitySlots
        {
            get
            {
                if (_cap <= 0) return int.MaxValue;
                int usableTitles = 0;
                for (int i = 0; i < _byTitle.Length; i++) if (_byTitle[i].Length > 0) usableTitles++;
                return usableTitles * _cap;
            }
        }

        /// <summary>按当前盘面重算各称谓的占用数。每次消除结算后、补充方块前必须调一次。</summary>
        public void ResetCounts(BoardModel board)
        {
            for (int i = 0; i < _counts.Length; i++) _counts[i] = 0;
            for (int i = 0; i < BoardModel.CellCount; i++)
            {
                int v = board[i];
                if (v >= 0) _counts[_graph.TitleIndex(v)]++;
            }
        }

        public void AddCount(int personIndex)
        {
            _counts[_graph.TitleIndex(personIndex)]++;
        }

        /// <summary>
        /// 取下一个方块。优先挑「还没到上限」的称谓；全部到顶则退回纯随机，保证不会卡死。
        /// 每取一个都会自增对应计数，所以调用方不必再手动 AddCount。
        /// </summary>
        public int Next()
        {
            if (_cap > 0)
            {
                _eligibleTitles.Clear();
                for (int t = 0; t < _counts.Length; t++)
                {
                    if (_byTitle[t].Length > 0 && _counts[t] < _cap) _eligibleTitles.Add(t);
                }

                if (_eligibleTitles.Count > 0)
                {
                    int title = _eligibleTitles[_rng.Next(_eligibleTitles.Count)];
                    int[] people = _byTitle[title];
                    int person = people[_rng.Next(people.Length)];
                    _counts[title]++;
                    return person;
                }
            }

            return _roster[_rng.Next(_roster.Length)];
        }

        /// <summary>
        /// 把「当前还能放的方块」写进 into。生成器逐格填坑时用这个拿候选集合，
        /// 而不是用 Next() —— 因为它需要对同一格试多个候选直到不产生三连。
        /// </summary>
        public void FillEligible(List<int> into)
        {
            into.Clear();
            if (_cap <= 0)
            {
                into.AddRange(_roster);
                return;
            }
            for (int i = 0; i < _roster.Length; i++)
            {
                int title = _graph.TitleIndex(_roster[i]);
                if (_counts[title] < _cap) into.Add(_roster[i]);
            }
        }
    }
}
