namespace Game.Core
{
    /// <summary>性别。只影响族谱树的排版，不参与消除判定。</summary>
    public enum Gender
    {
        Male = 0,
        Female = 1
    }

    /// <summary>
    /// 可扩容的亲属类别。只有这五类参与 R3「同类三消」，其余亲属（爷爷、爸爸、伯父…）不归类。
    /// @trace MECH-007
    /// </summary>
    public enum KinCategory
    {
        None = 0,
        Sibling = 1,
        Tang = 2,
        Biao = 3,
        Zhi = 4,
        Sheng = 5
    }

    /// <summary>
    /// 消除规则。枚举值即优先级（小的先结算），见 DOC-02 与 DOC-04 的优先级表。
    /// @trace MECH-005, MECH-006, MECH-007, MECH-008
    /// </summary>
    public enum RuleKind
    {
        /// <summary>R1 直系三代：祖 → 父 → 子，两段亲子边。</summary>
        R1 = 0,
        /// <summary>R2 同代姻亲：三人都是姻亲且代际相同。</summary>
        R2 = 1,
        /// <summary>R3 同类三消：同类且称谓互异；平辈池另要求三家不同。</summary>
        R3 = 2
    }

    /// <summary>
    /// 族谱中一个人。纯数据，不含行为。
    /// 父母 / 配偶用 id 而不是索引，因为数据来自外部 JSON，索引由 FamilyGraph 负责建立。
    /// @trace MECH-002
    /// </summary>
    public sealed class FamilyPerson
    {
        public FamilyPerson(
            string id,
            string title,
            Gender gender,
            int gen,
            string fatherId,
            string motherId,
            string spouseId,
            KinCategory category)
        {
            Id = id;
            Title = title;
            Gender = gender;
            Gen = gen;
            FatherId = fatherId;
            MotherId = motherId;
            SpouseId = spouseId;
            Category = category;
        }

        public string Id { get; private set; }
        public string Title { get; private set; }
        public Gender Gender { get; private set; }

        /// <summary>代际：儿子女儿那一代 = 1，父母 = 2，祖辈 = 3，孙辈 = -1。数字越大辈分越高。</summary>
        public int Gen { get; private set; }

        public string FatherId { get; private set; }
        public string MotherId { get; private set; }
        public string SpouseId { get; private set; }
        public KinCategory Category { get; private set; }

        public override string ToString()
        {
            return Title + "(" + Id + ")";
        }
    }

    /// <summary>
    /// 一次命中的三连。用扁平格子索引（0..63）表示，行优先：index = row * 8 + col。
    /// @trace MECH-008
    /// </summary>
    public struct MatchGroup
    {
        public MatchGroup(RuleKind rule, int cellA, int cellB, int cellC)
        {
            Rule = rule;
            CellA = cellA;
            CellB = cellB;
            CellC = cellC;
        }

        public RuleKind Rule { get; private set; }
        public int CellA { get; private set; }
        public int CellB { get; private set; }
        public int CellC { get; private set; }

        /// <summary>三格扁平索引中最小的一个，用于同优先级下的「行优先 → 列优先」排序。</summary>
        public int FirstCell
        {
            get
            {
                int min = CellA;
                if (CellB < min) min = CellB;
                if (CellC < min) min = CellC;
                return min;
            }
        }

        public bool Contains(int cell)
        {
            return cell == CellA || cell == CellB || cell == CellC;
        }

        public override string ToString()
        {
            return Rule + "[" + CellA + "," + CellB + "," + CellC + "]";
        }
    }
}
