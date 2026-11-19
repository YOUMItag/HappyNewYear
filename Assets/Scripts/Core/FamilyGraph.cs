using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>
    /// 族谱图：负责「三人是否构成合法组合」的全部判定。
    ///
    /// 设计要点：
    /// 1. 三条规则的三元组在构造时就全部枚举成哈希集合，运行时判定是 O(1) 查表，不做图遍历。
    ///    47 人共 535 组（R1 64 + R2 11 + R3 460），内存与耗时都可忽略。
    /// 2. 三元组用整数编码（三人索引排序后 (a*N+b)*N+c），避免运行时拼字符串。
    /// 3. 优先级通过「按 R1 → R2 → R3 顺序查表」天然实现，见 TryClassify。
    ///
    /// 硬约束：本文件不得出现 using UnityEngine —— Core 层要能脱离引擎跑单元测试。
    ///
    /// @trace MECH-002, MECH-005, MECH-006, MECH-007, MECH-008
    /// </summary>
    public sealed class FamilyGraph
    {
        public const int Rows = 8;
        public const int Cols = 8;
        public const int CellCount = Rows * Cols;

        readonly List<FamilyPerson> _persons;
        readonly Dictionary<string, int> _idToIndex;
        readonly int[] _father;
        readonly int[] _mother;
        readonly int[] _spouse;
        readonly int[] _titleIndex;
        readonly string[] _titles;
        readonly string[] _familyTitle;
        readonly List<int>[] _children;

        readonly HashSet<int> _r1 = new HashSet<int>();
        readonly HashSet<int> _r2 = new HashSet<int>();
        readonly HashSet<int> _r3 = new HashSet<int>();
        readonly List<int[]> _triples = new List<int[]>();

        readonly int _maxGen;

        public FamilyGraph(IEnumerable<FamilyPerson> persons)
        {
            if (persons == null) throw new ArgumentNullException(nameof(persons));

            _persons = new List<FamilyPerson>(persons);
            int n = _persons.Count;
            if (n == 0) throw new ArgumentException("族谱为空", nameof(persons));

            _idToIndex = new Dictionary<string, int>(n);
            for (int i = 0; i < n; i++)
            {
                string id = _persons[i].Id;
                if (string.IsNullOrEmpty(id)) throw new ArgumentException("第 " + i + " 个人的 id 为空");
                if (_idToIndex.ContainsKey(id)) throw new ArgumentException("重复的 id: " + id);
                _idToIndex.Add(id, i);
            }

            _father = new int[n];
            _mother = new int[n];
            _spouse = new int[n];
            _children = new List<int>[n];
            for (int i = 0; i < n; i++) _children[i] = new List<int>(4);

            for (int i = 0; i < n; i++)
            {
                _father[i] = Resolve(_persons[i].FatherId);
                _mother[i] = Resolve(_persons[i].MotherId);
                _spouse[i] = Resolve(_persons[i].SpouseId);
            }

            for (int i = 0; i < n; i++)
            {
                if (_father[i] >= 0) _children[_father[i]].Add(i);
                if (_mother[i] >= 0 && _mother[i] != _father[i]) _children[_mother[i]].Add(i);
            }

            int maxGen = int.MinValue;
            for (int i = 0; i < n; i++) if (_persons[i].Gen > maxGen) maxGen = _persons[i].Gen;
            _maxGen = maxGen;

            // 称谓编号：同一称谓（同名）合并成一个编号，用于「同名上限」与「称谓互异」判定
            var titleMap = new Dictionary<string, int>(n);
            var titleList = new List<string>(n);
            _titleIndex = new int[n];
            for (int i = 0; i < n; i++)
            {
                string t = _persons[i].Title;
                int idx;
                if (!titleMap.TryGetValue(t, out idx))
                {
                    idx = titleList.Count;
                    titleMap.Add(t, idx);
                    titleList.Add(t);
                }
                _titleIndex[i] = idx;
            }
            _titles = titleList.ToArray();

            _familyTitle = new string[n];
            for (int i = 0; i < n; i++) ComputeFamilyTitle(i, 0);

            BuildR1();
            BuildR2();
            BuildR3(KinCategory.Tang, KinCategory.Biao, true);
            BuildR3(KinCategory.Zhi, KinCategory.Sheng, false);
        }

        // ───────────────────────── 基础访问 ─────────────────────────

        public int Count { get { return _persons.Count; } }
        public IReadOnlyList<FamilyPerson> Persons { get { return _persons; } }
        public int R1Count { get { return _r1.Count; } }
        public int R2Count { get { return _r2.Count; } }
        public int R3Count { get { return _r3.Count; } }
        public int TripleCount { get { return _triples.Count; } }

        /// <summary>全部合法三元组（每项为 3 个人员索引）。供盘面生成器做「反向构造」随机取材。</summary>
        public IReadOnlyList<int[]> Triples { get { return _triples; } }

        public int TitleCount { get { return _titles.Length; } }

        public FamilyPerson Get(int index)
        {
            return _persons[index];
        }

        public int IndexOf(string id)
        {
            if (string.IsNullOrEmpty(id)) return -1;
            int idx;
            return _idToIndex.TryGetValue(id, out idx) ? idx : -1;
        }

        public string Title(int index) { return _persons[index].Title; }
        public string FamilyTitle(int index) { return _familyTitle[index]; }
        public int Gen(int index) { return _persons[index].Gen; }
        public KinCategory CategoryOf(int index) { return _persons[index].Category; }
        public int TitleIndex(int index) { return _titleIndex[index]; }
        public int FatherOf(int index) { return _father[index]; }
        public int MotherOf(int index) { return _mother[index]; }
        public int SpouseOf(int index) { return _spouse[index]; }
        public IReadOnlyList<int> ChildrenOf(int index) { return _children[index]; }

        /// <summary>parent 是否为 child 的父亲或母亲。只看亲子边，配偶关系不参与。</summary>
        public bool IsParent(int parent, int child)
        {
            if (parent < 0 || child < 0) return false;
            return _father[child] == parent || _mother[child] == parent;
        }

        /// <summary>
        /// 姻亲判定：父母都不在族谱里，且不是最高代。
        /// 第 2 个条件不能省 —— 爷爷、奶奶、外公、外婆同样没有父母，但他们是这一族的起点，算血亲。
        /// @trace MECH-006
        /// </summary>
        public bool IsInLaw(int index)
        {
            return _father[index] < 0 && _mother[index] < 0 && _persons[index].Gen < _maxGen;
        }

        // ───────────────────────── 判定 ─────────────────────────

        /// <summary>
        /// 判定三人命中的规则。按 R1 → R2 → R3 顺序查，天然实现优先级 MECH-008。
        /// 三条规则互不重叠（见 DOC-02 第 4 节），所以「先查到哪条」不会产生歧义。
        /// </summary>
        public bool TryClassify(int a, int b, int c, out RuleKind rule)
        {
            int key = Encode(a, b, c);
            if (_r1.Contains(key)) { rule = RuleKind.R1; return true; }
            if (_r2.Contains(key)) { rule = RuleKind.R2; return true; }
            if (_r3.Contains(key)) { rule = RuleKind.R3; return true; }
            rule = RuleKind.R1;
            return false;
        }

        /// <summary>三人索引排序后编码为唯一整数。顺序无关（任意顺序都算同一组）。</summary>
        public int Encode(int a, int b, int c)
        {
            if (a > b) { int t = a; a = b; b = t; }
            if (b > c) { int t = b; b = c; c = t; }
            if (a > b) { int t = a; a = b; b = t; }
            int n = _persons.Count;
            return (a * n + b) * n + c;
        }

        // ───────────────────────── 构造 ─────────────────────────

        int Resolve(string id)
        {
            if (string.IsNullOrEmpty(id)) return -1;
            int idx;
            return _idToIndex.TryGetValue(id, out idx) ? idx : -1;
        }

        bool HasParents(int index)
        {
            return _father[index] >= 0 || _mother[index] >= 0;
        }

        void ComputeFamilyTitle(int i, int depth)
        {
            if (_familyTitle[i] != null) return;

            string result;
            int f = _father[i];
            int m = _mother[i];

            if (f >= 0 || m >= 0)
            {
                bool fatherIsBlood = f >= 0 && HasParents(f);
                bool motherIsBlood = m >= 0 && HasParents(m);
                if (fatherIsBlood) result = _persons[f].Title + "家";
                else if (motherIsBlood) result = _persons[m].Title + "家";
                else result = _persons[f >= 0 ? f : m].Title + "家";
            }
            else if (_spouse[i] >= 0)
            {
                int sp = _spouse[i];
                if (HasParents(sp))
                {
                    ComputeFamilyTitle(sp, depth + 1);
                    result = _familyTitle[sp];
                }
                else
                {
                    // 祖辈夫妻：双方都没有父母，统一用男方当户主，避免两人算出两个家
                    result = (_persons[i].Gender == Gender.Male ? _persons[i].Title : _persons[sp].Title) + "家";
                }
            }
            else
            {
                result = _persons[i].Title + "家";
            }

            if (result == null) result = _persons[i].Title + "家";
            _familyTitle[i] = result;
        }

        void AddTriple(HashSet<int> set, int a, int b, int c)
        {
            int key = Encode(a, b, c);
            if (!set.Add(key)) return;
            int x = a, y = b, z = c;
            if (x > y) { int t = x; x = y; y = t; }
            if (y > z) { int t = y; y = z; z = t; }
            if (x > y) { int t = x; x = y; y = t; }
            _triples.Add(new[] { x, y, z });
        }

        /// <summary>R1 直系三代：A 是 B 的父母，B 是 C 的父母。@trace MECH-005</summary>
        void BuildR1()
        {
            int n = _persons.Count;
            var ups = new List<int>(2);
            for (int mid = 0; mid < n; mid++)
            {
                ups.Clear();
                if (_father[mid] >= 0) ups.Add(_father[mid]);
                if (_mother[mid] >= 0 && _mother[mid] != _father[mid]) ups.Add(_mother[mid]);
                if (ups.Count == 0) continue;

                List<int> downs = _children[mid];
                if (downs.Count == 0) continue;

                for (int u = 0; u < ups.Count; u++)
                {
                    for (int d = 0; d < downs.Count; d++)
                    {
                        int up = ups[u];
                        int down = downs[d];
                        if (up == down) continue;
                        AddTriple(_r1, up, mid, down);
                    }
                }
            }
        }

        /// <summary>R2 同代姻亲：三人都是姻亲，且代际完全相同。@trace MECH-006</summary>
        void BuildR2()
        {
            int n = _persons.Count;
            var byGen = new Dictionary<int, List<int>>();
            for (int i = 0; i < n; i++)
            {
                if (!IsInLaw(i)) continue;
                int gen = _persons[i].Gen;
                List<int> list;
                if (!byGen.TryGetValue(gen, out list))
                {
                    list = new List<int>(4);
                    byGen.Add(gen, list);
                }
                list.Add(i);
            }

            foreach (KeyValuePair<int, List<int>> pair in byGen)
            {
                List<int> group = pair.Value;
                if (group.Count < 3) continue;
                for (int i = 0; i < group.Count - 2; i++)
                    for (int j = i + 1; j < group.Count - 1; j++)
                        for (int k = j + 1; k < group.Count; k++)
                            AddTriple(_r2, group[i], group[j], group[k]);
            }
        }

        /// <summary>
        /// R3 同类三消：三个不同称谓，且（平辈池）来自三个不同家庭。
        /// 子辈池只有哥哥家、姐姐家两支，凑不出三家，故不要求家庭互异 —— 见 DOC-04 MECH-007。
        /// @trace MECH-007
        /// </summary>
        void BuildR3(KinCategory catX, KinCategory catY, bool requireThreeFamilies)
        {
            var pool = new List<int>(32);
            for (int i = 0; i < _persons.Count; i++)
            {
                KinCategory c = _persons[i].Category;
                if (c == catX || c == catY) pool.Add(i);
            }
            if (pool.Count < 3) return;

            for (int i = 0; i < pool.Count - 2; i++)
            {
                int a = pool[i];
                for (int j = i + 1; j < pool.Count - 1; j++)
                {
                    int b = pool[j];
                    if (_titleIndex[a] == _titleIndex[b]) continue;
                    if (requireThreeFamilies && _familyTitle[a] == _familyTitle[b]) continue;

                    for (int k = j + 1; k < pool.Count; k++)
                    {
                        int c = pool[k];
                        if (_titleIndex[a] == _titleIndex[c]) continue;
                        if (_titleIndex[b] == _titleIndex[c]) continue;
                        if (requireThreeFamilies)
                        {
                            if (_familyTitle[a] == _familyTitle[c]) continue;
                            if (_familyTitle[b] == _familyTitle[c]) continue;
                        }
                        AddTriple(_r3, a, b, c);
                    }
                }
            }
        }
    }
}
