using System;
using System.Collections.Generic;
using Game.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// 族谱面板：**完整**的 47 人族谱，横铺在画面最下方，盘面展开时缩小让位。
    ///
    /// 布局分三层：
    ///   1. **分组**：47 人 → 29 块（只把堂亲 8 人、表亲 12 人各并成一块，其余单人一块）
    ///   2. **家庭单元**：夫妻两块合成一个单元（伯父+伯母、爸爸+妈妈…），单元才是布局与连线的单位
    ///   3. **定位**：以节点最多的父辈行为基准铺满，子女绕父母对称排开、父母落在子女质心正上方，
    ///      行内用对称松弛消重叠 —— 这样父母与子女的横向距离最短，连线才不会横穿整行。
    ///
    /// 连线用 S 形曲线（离开父辈竖直向下、进入子女竖直向下），比直线更像族谱：
    ///   亲子线从「父母单元底边中点」连到「子女块顶边中点」，跨得越远越淡。
    ///
    /// 层级：本组建在 ResultRoot 之前，结算面板仍然画在最上层。
    ///
    /// @trace MECH-002, MECH-005
    /// </summary>
    public sealed class FamilyTreePanel : MonoBehaviour
    {
        const float BottomMargin = 24f;   // 族谱带离画面底边的距离
        const float BandHeight = 300f;    // 表头 34 + 4 行 × 70 = 314 → 内高 288，刚好放得下
        const float HeaderHeight = 34f;
        const float RowHeight = 70f;
        const float NodeHeight = 40f;
        const float LabelWidth = 68f;
        const float SideMargin = 16f;
        const float GapInUnit = 10f;      // 夫妻两块之间的缝（橙色短线画在这里）
        const float GapBetweenUnits = 26f;// 两个家庭块之间的缝

        static readonly Color BandColor = new Color32(0x14, 0x18, 0x1F, 0xF2);
        static readonly Color NodeColor = new Color32(0x28, 0x2F, 0x3A, 0xFF);
        static readonly Color MatchColor = new Color32(0xD9, 0xA4, 0x41, 0xFF);
        static readonly Color NodeTextDark = new Color32(0x1A, 0x14, 0x08, 0xFF);
        static readonly Color NodeTextLight = new Color32(0xE4, 0xE8, 0xEE, 0xFF);
        static readonly Color BloodColor = new Color(0x6F / 255f, 0x9E / 255f, 0xD8 / 255f, 0.60f);
        static readonly Color SpouseColor = new Color(0xC9 / 255f, 0x9A / 255f, 0x6A / 255f, 0.75f);
        static readonly Color BloodHot = new Color(0x9E / 255f, 0xD0 / 255f, 0xFF / 255f, 1f);   // 高亮亲子线
        static readonly Color SpouseHot = new Color(0xFF / 255f, 0xC8 / 255f, 0x6E / 255f, 1f);  // 高亮夫妻线

        FamilyGraph _graph;
        TMP_FontAsset _font;

        RectTransform _root;
        RectTransform _band;
        TextMeshProUGUI _ruleText;
        TextMeshProUGUI _toggleLabel;

        // ── 分组：一人多号的称谓合并成一决 ──
        sealed class Group
        {
            public string Label;
            public int Gen;
            public readonly List<int> Members = new List<int>(8);
        }

        // ── 家庭单元：夫妻两块合成一个布局单位 ──
        sealed class Unit
        {
            public int Gen;
            public readonly List<int> Nodes = new List<int>(2);
        }

        sealed class Edge
        {
            public int Parent;   // 父母所在单元
            public int Child;    // 子女所在分组
        }

        readonly List<Group> _groups = new List<Group>(32);
        readonly List<Unit> _units = new List<Unit>(24);
        readonly List<Edge> _edges = new List<Edge>(32);

        int[] _groupOf;                 // 人员索引 → 分组下标
        int[] _unitOf;                  // 分组下标 → 单元下标
        List<int>[] _unitParents = new List<int>[0];
        List<int>[] _unitChildren = new List<int>[0];
        int[] _rank;                    // 单元下标 → 家庭分支次序
        float[] _unitX;                 // 单元中心 x（族谱带坐标，原点在左上角）

        RectTransform _lines;
        readonly List<LineRef> _lineRefs = new List<LineRef>(128);
        bool[] _lit = new bool[0];

        /// <summary>一条线的引用：高亮时要改颜色与粗细，不能画完就丢。</summary>
        sealed class LineRef
        {
            public Image Image;
            public RectTransform Rect;
            public float Length;
            public int Kind;      // 0 = 亲子，1 = 夫妻
            public int Unit;      // 亲子：父母单元；夫妻：所在单元
            public int Child;     // 亲子：子女分组，夫妻为 -1
            public Color Base;
        }

        Vector2[] _nodeTopLeft;
        Vector2[] _nodeSize;

        Image[] _nodeBg;
        TextMeshProUGUI[] _nodeTitle;
        TextMeshProUGUI[] _nodeFamily;

        readonly List<TextMeshProUGUI> _labels = new List<TextMeshProUGUI>(4);

        float _nodeWidth = 112f;
        float _usable = 1555f;

        public FamilyGraph Graph { get { return _graph; } }

        /// <summary>当前被点亮的连线段数（自检用：直系三代应当点亮两跳链）。</summary>
        public int HotLineCount { get; private set; }

        public bool IsOpen { get { return _root != null && _root.gameObject.activeSelf; } }
        public int MatchCount { get; private set; }

        /// <summary>展开/收起时通知外部（盘面要跟着缩放并上移）。</summary>
        public event Action<bool> OpenChanged;

        public static FamilyTreePanel Create(Transform canvas, TMP_FontAsset font, FamilyGraph graph)
        {
            // ── 入口按钮：HUD 右侧第三个（暂停 -48 / 提示 -172 / 族谱 -296）──
            Transform hudRoot = canvas.Find("HudRoot");
            Transform buttonParent = hudRoot != null ? hudRoot : canvas;

            Button toggle = UiFactory.CreateButton(
                buttonParent, "TreeToggle", font, "族谱",
                new Color32(0x4A, 0x3C, 0x6B, 0xFF), Color.white, 20f,
                Vector2.zero, new Vector2(112f, 50f));

            var toggleRect = (RectTransform)toggle.transform;
            toggleRect.anchorMin = new Vector2(1f, 1f);
            toggleRect.anchorMax = new Vector2(1f, 1f);
            toggleRect.pivot = new Vector2(1f, 1f);
            toggleRect.anchoredPosition = new Vector2(-296f, -106f);

            var panel = toggle.gameObject.AddComponent<FamilyTreePanel>();
            panel._graph = graph;
            panel._font = font;
            panel._toggleLabel = toggle.transform.Find("Label").GetComponent<TextMeshProUGUI>();

            // ── 族谱带：横铺整个画面宽度，贴在画面最下方 ──
            RectTransform root = UiFactory.CreateRect(
                canvas, "TreeRoot",
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, BottomMargin), new Vector2(0f, BandHeight));

            panel._root = root;

            UiFactory.CreateImage(root, "Background", BandColor,
                new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero, true);

            RectTransform band = UiFactory.CreateRect(root, "Band",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -6f), new Vector2(-2f * SideMargin, BandHeight - 12f));

            panel._band = band;

            // 连线层先建再放到最前 = 所有节点都画在它上面，线穿过节点时会被节点挡住。
            // 锚点必须与节点一致（族谱带左上角、尺寸 0），否则父矩形的 pivot 会把线整体平移半个带宽。
            panel._lines = UiFactory.CreateRect(band, "Lines",
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                Vector2.zero, Vector2.zero);
            panel._lines.SetAsFirstSibling();

            UiFactory.CreateText(band, "Title", font, "族谱", 20f,
                BoardTheme.HudText, TextAlignmentOptions.Left,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(4f, 0f), new Vector2(80f, HeaderHeight));

            panel._ruleText = UiFactory.CreateText(band, "Rule", font, "消掉一组亲戚，这里会亮起他们", 15f,
                new Color32(0xE0, 0xB0, 0x55, 0xFF), TextAlignmentOptions.Right,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-4f, 0f), new Vector2(-90f, HeaderHeight));

            panel.BuildNodes();
            panel.Highlight(-1, -1, -1);

            toggle.onClick.AddListener(panel.Toggle);

            panel.Hide();
            return panel;
        }

        // ═══════════════════════ 建节点（一次性） ═══════════════════════

        void BuildNodes()
        {
            BuildGroups();
            BuildUnits();

            int count = _groups.Count;
            _nodeTopLeft = new Vector2[count];
            _nodeSize = new Vector2[count];
            _nodeBg = new Image[count];
            _nodeTitle = new TextMeshProUGUI[count];
            _nodeFamily = new TextMeshProUGUI[count];

            for (int i = 0; i < count; i++)
            {
                Group group = _groups[i];

                Image bg = UiFactory.CreateImage(_band, "Group_" + i, NodeColor,
                    new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                    Vector2.zero, new Vector2(_nodeWidth, NodeHeight), false);

                RectTransform rect = bg.rectTransform;
                _nodeBg[i] = bg;

                _nodeTitle[i] = UiFactory.CreateText(rect, "Title", _font, group.Label, 15f,
                    NodeTextLight, TextAlignmentOptions.Center,
                    new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
                    new Vector2(0f, 3f), new Vector2(-4f, -14f),
                    true, 9f);

                string sub = group.Members.Count > 1
                    ? group.Members.Count + " 人"
                    : _graph.FamilyTitle(group.Members[0]);

                _nodeFamily[i] = UiFactory.CreateText(rect, "Count", _font, sub, 10f,
                    BoardTheme.HudSubText, TextAlignmentOptions.Center,
                    new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                    new Vector2(0f, 2f), new Vector2(-6f, 14f));
            }
        }

        void BuildGroups()
        {
            _groups.Clear();
            _groupOf = new int[_graph.Count];
            var index = new Dictionary<string, int>(32);

            for (int i = 0; i < _graph.Count; i++)
            {
                string label = GroupLabelFor(i);
                int gen = _graph.Gen(i);
                string key = label + "@" + gen;

                int g;
                if (!index.TryGetValue(key, out g))
                {
                    g = _groups.Count;
                    index[key] = g;
                    _groups.Add(new Group { Label = label, Gen = gen });
                }

                _groups[g].Members.Add(i);
                _groupOf[i] = g;
            }
        }

        /// <summary>
        /// 只把「堂哥/堂姐/堂弟/堂妹」「表哥/表姐/表弟/表妹」这种一人多号的合并成一块，
        /// 其余全部单人成块 —— 伯父、姑妈、爷爷、侄子各有各的位置，否则看不出谁跟谁是血亲。
        /// </summary>
        string GroupLabelFor(int person)
        {
            KinCategory category = _graph.CategoryOf(person);
            if (category == KinCategory.Tang) return "堂亲";
            if (category == KinCategory.Biao) return "表亲";
            return _graph.Title(person);
        }

        /// <summary>
        /// 家庭单元 = 一对夫妻的两块。布局与连线都以单元为单位：
        /// 这样伯父和伯母永远挨着，父母与子女的连线从一个「家」的中点出发。
        /// </summary>
        void BuildUnits()
        {
            _units.Clear();
            _unitOf = new int[_groups.Count];
            for (int i = 0; i < _unitOf.Length; i++) _unitOf[i] = -1;

            for (int g = 0; g < _groups.Count; g++)
            {
                if (_unitOf[g] >= 0 || _groups[g].Members.Count != 1) continue;

                var unit = new Unit { Gen = _groups[g].Gen };
                unit.Nodes.Add(g);

                int spouse = _graph.SpouseOf(_groups[g].Members[0]);
                if (spouse >= 0)
                {
                    int sg = _groupOf[spouse];
                    if (sg != g && sg >= 0 && _unitOf[sg] < 0 && _groups[sg].Members.Count == 1)
                        unit.Nodes.Add(sg);
                }

                int index = _units.Count;
                _units.Add(unit);
                for (int i = 0; i < unit.Nodes.Count; i++) _unitOf[unit.Nodes[i]] = index;
            }

            for (int g = 0; g < _groups.Count; g++)
            {
                if (_unitOf[g] >= 0) continue;
                int index = _units.Count;
                _units.Add(new Unit { Gen = _groups[g].Gen, Nodes = { g } });
                _unitOf[g] = index;
            }

            BuildEdges();
            BuildRank();
        }

        /// <summary>
        /// 亲子边：一个人有父有母，两边都算来源（父母同属一个单元时只留一条）。
        /// 之前「父优先、无父取母」的写法会让姑丈、姨夫这类姻亲也伸出蓝线，与「蓝线 = 血亲」自相矛盾。
        /// </summary>
        void BuildEdges()
        {
            _edges.Clear();
            var seen = new HashSet<int>();

            for (int g = 0; g < _groups.Count; g++)
            {
                for (int m = 0; m < _groups[g].Members.Count; m++)
                {
                    int person = _groups[g].Members[m];
                    AddEdge(seen, g, _graph.FatherOf(person));
                    AddEdge(seen, g, _graph.MotherOf(person));
                }
            }
        }

        void AddEdge(HashSet<int> seen, int childGroup, int parentPerson)
        {
            if (parentPerson < 0) return;

            int pu = _unitOf[_groupOf[parentPerson]];
            if (pu == _unitOf[childGroup]) return;
            if (!seen.Add(pu * 128 + childGroup)) return;

            _edges.Add(new Edge { Parent = pu, Child = childGroup });
        }

        /// <summary>按家庭分支 DFS 给单元排定次序：同一家的兄弟姐妹因此相邻。</summary>
        void BuildRank()
        {
            int n = _units.Count;
            _unitParents = new List<int>[n];
            _unitChildren = new List<int>[n];
            for (int i = 0; i < n; i++)
            {
                _unitParents[i] = new List<int>(2);
                _unitChildren[i] = new List<int>(4);
            }

            for (int e = 0; e < _edges.Count; e++)
            {
                int pu = _edges[e].Parent, cu = _unitOf[_edges[e].Child];
                if (!_unitParents[cu].Contains(pu)) _unitParents[cu].Add(pu);
                if (!_unitChildren[pu].Contains(cu)) _unitChildren[pu].Add(cu);
            }

            _rank = new int[n];
            for (int i = 0; i < n; i++) _rank[i] = -1;

            var roots = new List<int>(4);
            for (int u = 0; u < n; u++) if (_unitParents[u].Count == 0) roots.Add(u);
            roots.Sort((a, b) => FirstMember(a).CompareTo(FirstMember(b)));

            int next = 0;
            var stack = new Stack<int>();

            for (int i = roots.Count - 1; i >= 0; i--) stack.Push(roots[i]);
            while (true)
            {
                if (stack.Count == 0)
                {
                    int pick = -1;
                    for (int scan = 0; scan < n; scan++)
                        if (_rank[scan] < 0 && (pick < 0 || FirstMember(scan) < FirstMember(pick))) pick = scan;
                    if (pick < 0) break;
                    stack.Push(pick);
                }

                int u = stack.Pop();
                if (_rank[u] >= 0) continue;
                _rank[u] = next++;

                var kids = new List<int>(_unitChildren[u]);
                kids.Sort((a, b) => FirstMember(b).CompareTo(FirstMember(a)));
                for (int i = 0; i < kids.Count; i++) if (_rank[kids[i]] < 0) stack.Push(kids[i]);
            }

            for (int u = 0; u < n; u++) if (_rank[u] < 0) _rank[u] = next++;
        }

        /// <summary>
        /// 行的次序按「这一家养出的是堂亲还是表亲」分三桶：
        ///   0 = 父系兄弟家（伯父、叔父 → 堂亲）靠左
        ///   1 = 自家（爸爸 → 哥哥弟弟姐姐妹妹）
        ///   2 = 姑/舅/姨家（→ 表亲）靠右
        /// 这样堂亲天然落最左、表亲落最右，两家不必跨半个屏幕去连父母。
        /// </summary>
        int OrderKey(int unit)
        {
            return BucketOf(unit) * 1024 + _rank[unit];
        }

        int BucketOf(int unit)
        {
            List<int> kids = _unitChildren[unit];
            int best = 3;
            for (int i = 0; i < kids.Count; i++)
            {
                int b = LabelBucket(_groups[_units[kids[i]].Nodes[0]].Label);
                if (b < best) best = b;
            }
            return best < 3 ? best : LabelBucket(_groups[_units[unit].Nodes[0]].Label);
        }

        static int LabelBucket(string label)
        {
            if (label == "堂亲") return 0;
            if (label == "表亲") return 2;
            return 1;
        }

        int FirstMember(int unit)
        {
            return _groups[_units[unit].Nodes[0]].Members[0];
        }

        // ═══════════════════════ 布局（每次展开重算） ═══════════════════════

        void Layout()
        {
            float width = _band.rect.width;
            if (width <= 1f) width = 1800f;
            _usable = Mathf.Max(360f, width - LabelWidth - 8f);

            // 辈分从高到低：祖辈(3) → 父辈(2) → 我辈(1) → 子辈(-1)
            var gens = new List<int>(4);
            var rows = new Dictionary<int, List<int>>();
            for (int u = 0; u < _units.Count; u++)
            {
                int gen = _units[u].Gen;
                List<int> list;
                if (!rows.TryGetValue(gen, out list))
                {
                    list = new List<int>(16);
                    rows[gen] = list;
                    gens.Add(gen);
                }
                list.Add(u);
            }
            gens.Sort((a, b) => b.CompareTo(a));

            // 节点宽度：让「最挤的一行」刚好铺满可用宽度
            int busiest = 0;
            for (int r = 0; r < gens.Count; r++)
            {
                List<int> row = rows[gens[r]];
                int nodes = 0;
                for (int i = 0; i < row.Count; i++) nodes += _units[row[i]].Nodes.Count;
                if (nodes > busiest) busiest = nodes;
            }
            int busiestUnits = 0;
            for (int r = 0; r < gens.Count; r++)
            {
                List<int> row = rows[gens[r]];
                int nodes = 0;
                for (int i = 0; i < row.Count; i++) nodes += _units[row[i]].Nodes.Count;
                if (nodes == busiest) { busiestUnits = row.Count; break; }
            }

            float raw = (_usable - GapBetweenUnits * (busiestUnits - 1) - GapInUnit * (busiest - busiestUnits)) / busiest;
            _nodeWidth = Mathf.Clamp(raw, 96f, 128f);
            for (int i = 0; i < _groups.Count; i++)
                _nodeBg[i].rectTransform.sizeDelta = new Vector2(_nodeWidth, NodeHeight);

            _unitX = new float[_units.Count];

            // 基准行 = 单元最多的那一行，先均匀铺开
            int baseGen = gens[0];
            int baseCount = 0;
            for (int r = 0; r < gens.Count; r++)
            {
                if (rows[gens[r]].Count > baseCount) { baseCount = rows[gens[r]].Count; baseGen = gens[r]; }
            }
            List<int> baseRow = rows[baseGen];
            baseRow.Sort((a, b) => OrderKey(a).CompareTo(OrderKey(b)));

            float total = 0f;
            for (int i = 0; i < baseRow.Count; i++) total += UnitWidth(baseRow[i]) + GapBetweenUnits;
            total -= GapBetweenUnits;
            float x = (_usable - total) * 0.5f;
            for (int i = 0; i < baseRow.Count; i++)
            {
                _unitX[baseRow[i]] = x + UnitWidth(baseRow[i]) * 0.5f;
                x += UnitWidth(baseRow[i]) + GapBetweenUnits;
            }

            // 子女：绕父母对称排开（唯一父母）或多父母取质心
            for (int r = 0; r < gens.Count; r++)
            {
                int gen = gens[r];
                if (gen >= baseGen) continue;
                PackRow(rows[gen], ChildrenIdeals(gen, rows[gen]));
            }

            // 祖辈：落在子女的正上方
            for (int r = gens.Count - 1; r >= 0; r--)
            {
                int gen = gens[r];
                if (gen <= baseGen) continue;
                var ideal = new Dictionary<int, float>(8);
                List<int> row = rows[gen];
                for (int i = 0; i < row.Count; i++)
                {
                    int u = row[i];
                    float sum = 0f; int k = 0;
                    List<int> kids = _unitChildren[u];
                    for (int c = 0; c < kids.Count; c++)
                    {
                        if (_units[kids[c]].Gen != gen - 1) continue;
                        sum += _unitX[kids[c]]; k++;
                    }
                    ideal[u] = k > 0 ? sum / k : _unitX[u];
                }
                PackRow(row, ideal);
            }

            // 整体右移，让开左侧「祖辈/父辈/我辈/子辈」标签栏
            float gutter = LabelWidth + 8f;
            for (int u = 0; u < _units.Count; u++) _unitX[u] += gutter;

            // ── 落到具体节点 ──
            for (int r = 0; r < gens.Count; r++)
            {
                int gen = gens[r];
                List<int> row = rows[gen];
                float rowTop = -(HeaderHeight + r * RowHeight);
                SetLabel(r, rowTop, GenLabel(gen));

                for (int i = 0; i < row.Count; i++)
                {
                    int u = row[i];
                    float left = _unitX[u] - UnitWidth(u) * 0.5f;
                    for (int k = 0; k < _units[u].Nodes.Count; k++)
                    {
                        int g = _units[u].Nodes[k];
                        RectTransform rect = _nodeBg[g].rectTransform;
                        rect.anchoredPosition = new Vector2(left + k * (_nodeWidth + GapInUnit), rowTop);

                        _nodeTopLeft[g] = rect.anchoredPosition;
                        _nodeSize[g] = rect.sizeDelta;

                        _nodeTitle[g].fontSize = _nodeWidth >= 104f ? 15f : 13f;
                        _nodeTitle[g].fontSizeMin = 9f;
                    }
                }
            }

            RedrawLines();
        }

        /// <summary>子女的理想位置：只有一个父母时绕父母对称排开，多个父母（堂亲/表亲这类合并块）取质心。</summary>
        Dictionary<int, float> ChildrenIdeals(int gen, List<int> row)
        {
            var ideal = new Dictionary<int, float>(row.Count);
            float pitch = _nodeWidth * 2f + GapInUnit + GapBetweenUnits;

            for (int i = 0; i < row.Count; i++)
            {
                int u = row[i];
                List<int> ps = _unitParents[u];

                if (ps.Count == 1)
                {
                    int p = ps[0];
                    List<int> sibs = _unitChildren[p];
                    var sameRow = new List<int>(sibs.Count);
                    for (int s = 0; s < sibs.Count; s++)
                        if (_units[sibs[s]].Gen == gen) sameRow.Add(sibs[s]);
                    sameRow.Sort((a, b) => OrderKey(a).CompareTo(OrderKey(b)));

                    int k = sameRow.IndexOf(u);
                    int m = Mathf.Max(1, sameRow.Count);
                    ideal[u] = _unitX[p] + (k - (m - 1) * 0.5f) * pitch;
                }
                else if (ps.Count > 1)
                {
                    float sum = 0f;
                    for (int s = 0; s < ps.Count; s++) sum += _unitX[ps[s]];
                    ideal[u] = sum / ps.Count;
                }
                else ideal[u] = _unitX[u];
            }

            return ideal;
        }

        /// <summary>
        /// 行内排布：先按理想位置落位，再用对称松弛把重叠推开（推的是双方，所以整体不会偏到一侧）。
        /// </summary>
        void PackRow(List<int> row, Dictionary<int, float> ideal)
        {
            row.Sort((a, b) => OrderKey(a).CompareTo(OrderKey(b)));

            for (int i = 0; i < row.Count; i++) _unitX[row[i]] = ideal[row[i]];

            for (int iter = 0; iter < 40; iter++)
            {
                bool moved = false;
                for (int i = 0; i < row.Count - 1; i++)
                {
                    int a = row[i], b = row[i + 1];
                    float need = (UnitWidth(a) + UnitWidth(b)) * 0.5f + GapBetweenUnits;
                    float d = _unitX[b] - _unitX[a];
                    if (d < need - 0.01f)
                    {
                        float push = (need - d) * 0.5f;
                        _unitX[a] -= push;
                        _unitX[b] += push;
                        moved = true;
                    }
                }
                if (!moved) break;
            }

            float lo = _unitX[row[0]] - UnitWidth(row[0]) * 0.5f;
            float hi = _unitX[row[row.Count - 1]] + UnitWidth(row[row.Count - 1]) * 0.5f;

            if (hi - lo > _usable)
            {
                float sc = _usable / (hi - lo);
                for (int i = 0; i < row.Count; i++) _unitX[row[i]] = lo + (_unitX[row[i]] - lo) * sc;
                lo = 0f; hi = _usable;
            }

            float shift = 0f;
            if (lo < 0f) shift = -lo;
            else if (hi > _usable) shift = _usable - hi;
            if (Mathf.Abs(shift) > 0.01f)
                for (int i = 0; i < row.Count; i++) _unitX[row[i]] += shift;
        }

        float UnitWidth(int unit)
        {
            int n = _units[unit].Nodes.Count;
            return n * _nodeWidth + (n - 1) * GapInUnit;
        }

        // ═══════════════════════ 亲缘连线 ═══════════════════════

        /// <summary>一条待画的亲子折线：先算好车道，再一次性落地。</summary>
        sealed class EdgeDraw
        {
            public int Unit;
            public int Child;
            public Vector2 From;
            public Vector2 To;
            public float Left;
            public float Right;
            public int Gap;
            public int Lane;
            public float LaneY;
        }

        /// <summary>
        /// 走线是「直线折角」：父母中点竖直下到行间 → 横走 → 再竖直下到子女顶边。
        ///
        /// 横梁**分车道**：同一条行间缝隙里，按「横段区间是否重叠」做贪心着色，
        /// 互不重叠的家庭共用一条车道（允许重叠），重叠的才另起一条。
        /// 这样既不会几十条线糊成一片，也不用给每个家庭都开一条道（缝隙只有 30 px）。
        /// </summary>
        void RedrawLines()
        {
            for (int i = _lines.childCount - 1; i >= 0; i--) Destroy(_lines.GetChild(i).gameObject);
            _lineRefs.Clear();

            int spouse = 0;
            for (int u = 0; u < _units.Count; u++)
            {
                if (_units[u].Nodes.Count < 2) continue;
                int a = _units[u].Nodes[0], b = _units[u].Nodes[1];
                Vector2 p = new Vector2(_nodeTopLeft[a].x + _nodeSize[a].x, _nodeTopLeft[a].y - _nodeSize[a].y * 0.5f);
                Vector2 q = new Vector2(_nodeTopLeft[b].x, _nodeTopLeft[b].y - _nodeSize[b].y * 0.5f);
                SpawnLine(p, q, SpouseColor, 1, u, -1);
                spouse++;
            }

            var draws = new List<EdgeDraw>(_edges.Count);
            float maxSpan = 0f;

            for (int e = 0; e < _edges.Count; e++)
            {
                int pu = _edges[e].Parent;
                int child = _edges[e].Child;

                Vector2 from = new Vector2(_unitX[pu], NodeBottomY(_units[pu].Nodes[0]));
                Vector2 to = new Vector2(_nodeTopLeft[child].x + _nodeSize[child].x * 0.5f, _nodeTopLeft[child].y);

                float span = Mathf.Abs(to.x - from.x);
                if (span > maxSpan) maxSpan = span;

                draws.Add(new EdgeDraw
                {
                    Unit = pu, Child = child, From = from, To = to,
                    Left = Mathf.Min(from.x, to.x), Right = Mathf.Max(from.x, to.x),
                    Gap = _units[pu].Gen * 16 + _groups[child].Gen
                });
            }

            // 每条缝隙单独分配车道
            var byGap = new Dictionary<int, List<EdgeDraw>>(4);
            for (int i = 0; i < draws.Count; i++)
            {
                List<EdgeDraw> list;
                if (!byGap.TryGetValue(draws[i].Gap, out list))
                {
                    list = new List<EdgeDraw>(8);
                    byGap[draws[i].Gap] = list;
                }
                list.Add(draws[i]);
            }

            foreach (var pair in byGap)
            {
                List<EdgeDraw> list = pair.Value;
                list.Sort((a, b) => a.Left.CompareTo(b.Left));

                var laneRight = new List<float>(4);
                for (int i = 0; i < list.Count; i++)
                {
                    int lane = 0;
                    while (lane < laneRight.Count && laneRight[lane] > list[i].Left + 0.5f) lane++;
                    if (lane == laneRight.Count) laneRight.Add(list[i].Right);
                    else laneRight[lane] = list[i].Right;
                    list[i].Lane = lane;
                }

                int lanes = Mathf.Max(1, laneRight.Count);
                float gapHeight = Mathf.Abs(list[0].From.y - list[0].To.y);
                float pitch = Mathf.Clamp((gapHeight - 10f) / Mathf.Max(1f, lanes - 1f), 4f, 9f);
                if (lanes <= 1) pitch = 0f;

                float midY = (list[0].From.y + list[0].To.y) * 0.5f;

                for (int i = 0; i < list.Count; i++)
                {
                    list[i].LaneY = midY + (list[i].Lane - (lanes - 1) * 0.5f) * pitch;
                    float span = list[i].Right - list[i].Left;
                    float alpha = span < 240f ? 0.60f : (span < 460f ? 0.40f : 0.28f);
                    SpawnElbow(list[i], new Color(BloodColor.r, BloodColor.g, BloodColor.b, alpha));
                }
            }

            ApplyLineColors();

            Debug.Log("[Tree] 连线完成：夫妻 " + spouse + " 条，亲子 " + _edges.Count
                + " 条；最大水平跨距 " + maxSpan.ToString("F0") + "，可用宽度 " + _usable.ToString("F0")
                + "；线元 " + _lineRefs.Count + " 段，缝隙 " + byGap.Count + " 条");
        }

        float NodeBottomY(int group)
        {
            return _nodeTopLeft[group].y - _nodeSize[group].y;
        }

        /// <summary>折角走线：竖 → 横（走本车道）→ 竖；两点 x 相同时退化成一条竖线。</summary>
        void SpawnElbow(EdgeDraw draw, Color color)
        {
            int unit = draw.Unit, child = draw.Child;

            if (Mathf.Abs(draw.To.x - draw.From.x) < 0.5f)
            {
                SpawnLine(draw.From, draw.To, color, 0, unit, child);
                return;
            }

            var laneStart = new Vector2(draw.From.x, draw.LaneY);
            var laneEnd = new Vector2(draw.To.x, draw.LaneY);

            SpawnLine(draw.From, laneStart, color, 0, unit, child);
            SpawnLine(laneStart, laneEnd, color, 0, unit, child);
            SpawnLine(laneEnd, draw.To, color, 0, unit, child);
        }

        LineRef SpawnLine(Vector2 from, Vector2 to, Color color, int kind, int unit, int child)
        {
            Vector2 delta = to - from;
            float length = delta.magnitude;
            if (length < 0.5f) return null;

            var go = new GameObject("Line", typeof(RectTransform));
            go.transform.SetParent(_lines, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = (from + to) * 0.5f;
            rect.sizeDelta = new Vector2(length, 1.6f);
            rect.localEulerAngles = new Vector3(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);

            var image = go.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;

            var line = new LineRef
            {
                Image = image, Rect = rect, Length = length,
                Kind = kind, Unit = unit, Child = child, Base = color
            };
            _lineRefs.Add(line);
            return line;
        }

        /// <summary>
        /// 高亮规则：两端都在本次消除里的人连线才算「这一手的血亲链」。
        /// 亲子线要求「父母这一家里有人亮着」且「子女亮着」；夫妻线只要家里有人亮着就点亮 ——
        /// 同代姻亲那一手，正是靠「亮着的伯母被一条橙线牵到没亮的伯父身上」来说明她是嫁进来的。
        /// </summary>
        void ApplyLineColors()
        {
            int hot = 0;
            for (int i = 0; i < _lineRefs.Count; i++)
            {
                LineRef line = _lineRefs[i];
                bool lit = line.Kind == 1
                    ? AnyLitInUnit(line.Unit)
                    : AnyLitInUnit(line.Unit) && line.Child >= 0 && line.Child < _lit.Length && _lit[line.Child];

                line.Image.color = lit
                    ? (line.Kind == 1 ? SpouseHot : BloodHot)
                    : line.Base;
                line.Rect.sizeDelta = new Vector2(line.Length, lit ? 4f : 1.6f);
                if (lit) hot++;
            }

            HotLineCount = hot;
        }

        bool AnyLitInUnit(int unit)
        {
            List<int> nodes = _units[unit].Nodes;
            for (int i = 0; i < nodes.Count; i++)
                if (nodes[i] < _lit.Length && _lit[nodes[i]]) return true;
            return false;
        }

        void SetLabel(int index, float y, string text)
        {
            while (_labels.Count <= index)
            {
                _labels.Add(UiFactory.CreateText(_band, "GenLabel_" + _labels.Count, _font, string.Empty, 13f,
                    BoardTheme.HudSubText, TextAlignmentOptions.Left,
                    new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                    Vector2.zero, new Vector2(LabelWidth, 20f)));
            }

            TextMeshProUGUI label = _labels[index];
            label.text = text;
            label.rectTransform.anchoredPosition = new Vector2(6f, y - 24f);
            label.gameObject.SetActive(true);
        }

        static string GenLabel(int gen)
        {
            if (gen >= 3) return "祖辈";
            if (gen == 2) return "父辈";
            if (gen == 1) return "我辈";
            return "子辈";
        }

        // ═══════════════════════ 高亮 ═══════════════════════

        void Highlight(int a, int b, int c)
        {
            if (_lit.Length != _groups.Count) _lit = new bool[_groups.Count];
            for (int i = 0; i < _lit.Length; i++) _lit[i] = false;

            if (a >= 0 && a < _groupOf.Length) _lit[_groupOf[a]] = true;
            if (b >= 0 && b < _groupOf.Length) _lit[_groupOf[b]] = true;
            if (c >= 0 && c < _groupOf.Length) _lit[_groupOf[c]] = true;

            ApplyLineColors();

            for (int i = 0; i < _groups.Count; i++)
            {
                bool matched = _lit[i];
                _nodeBg[i].color = matched ? MatchColor : NodeColor;
                _nodeTitle[i].color = matched ? NodeTextDark : NodeTextLight;
                _nodeFamily[i].color = matched
                    ? new Color(0.16f, 0.12f, 0.05f, 0.85f)
                    : BoardTheme.HudSubText;
            }
        }

        // ═══════════════════════ 展开 / 收起 ═══════════════════════

        public void Toggle()
        {
            if (IsOpen) Hide();
            else Show();
        }

        public void Show()
        {
            if (IsOpen) return;

            Layout();                 // 画布尺寸可能变了，每次展开重算一次
            _root.gameObject.SetActive(true);
            RefreshToggleLabel();
            if (OpenChanged != null) OpenChanged(true);
        }

        public void Hide()
        {
            bool wasOpen = IsOpen;
            if (_root != null) _root.gameObject.SetActive(false);
            RefreshToggleLabel();
            if (wasOpen && OpenChanged != null) OpenChanged(false);
        }

        void RefreshToggleLabel()
        {
            if (_toggleLabel != null) _toggleLabel.text = IsOpen ? "族谱 ×" : "族谱";
        }

        // ═══════════════════════ 显示一次消除 ═══════════════════════

        /// <summary>高亮刚消掉的三人。a/b/c 是人员索引，不是格子索引。</summary>
        public void ShowMatch(int a, int b, int c)
        {
            MatchCount++;

            RuleKind rule;
            bool classified = _graph.TryClassify(a, b, c, out rule);
            _ruleText.text = classified ? RuleName(rule) : "这一手没构成规则";

            Highlight(a, b, c);
        }

        static string RuleName(RuleKind rule)
        {
            if (rule == RuleKind.R1) return "这一手：直系三代 ×5　祖辈 → 父辈 → 我辈";
            if (rule == RuleKind.R2) return "这一手：同代姻亲 ×3　同一辈分的姻亲三人";
            return "这一手：同类三消 ×1　同辈同类的三个不同变体";
        }
    }
}
