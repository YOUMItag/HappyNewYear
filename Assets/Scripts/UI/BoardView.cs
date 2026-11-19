using System;
using System.Collections.Generic;
using Game.Core;
using TMPro;
using UnityEngine;

namespace Game.UI
{
    /// <summary>
    /// 64 个方块的容器：负责创建、摆放、刷新，以及**整手的动画回放**。
    ///
    /// 数据与画面分开：BoardController 在同步代码里把盘面算完（消除、下落、补位、加分都已定案），
    /// 再把这些「已经发生的事」打包成一条时间线交给这里，由这里花 0.4~1 秒慢放出来。
    /// 好处是逻辑层完全不知道动画的存在 —— 离线批量试玩、运行链路自检都不受时序影响。
    ///
    /// 动画由一个 Update 循环统一驱动 64 个方块，不给每个方块挂协程：
    /// 64 个 Update 的调用开销加生命周期管理，比一个循环里改 64 次 Transform 更贵。
    ///
    /// @trace MECH-003
    /// </summary>
    public sealed class BoardView : MonoBehaviour
    {
        // ── 时长（秒）。想调手感只改这几个数 ──
        const float SwapDuration = 0.12f;      // 两个方块对调
        const float ClearDuration = 0.18f;     // 消除：缩放 1→0 + 淡出
        const float FallPerRow = 0.12f;        // 下落：每格
        const float ColumnStagger = 0.02f;     // 同一次下落按列错开，避免整块板一起跳
        const float MaxFallDuration = 0.45f;   // 单块下落时长上限，防止大塌方拖沓
        const float ShakeDuration = 0.22f;     // 交换失败的抖动
        const float FadeDuration = 0.18f;      // 洗牌后的整体淡入

        static readonly Color GoldFlash = new Color32(0xF2, 0xC2, 0x4E, 0xFF);
        static readonly Color SilverFlash = new Color32(0xD8, 0xDF, 0xEA, 0xFF);
        static readonly Color WhiteFlash = new Color32(0xEE, 0xF2, 0xF8, 0xFF);
        static readonly Color HintFlash = new Color32(0x6F, 0xE3, 0xFF, 0xFF);   // 青蓝，与选中蓝/金/银都区分得开
        const float HintDuration = 2.5f;

        enum StepKind { Swap, Clear, Fall, Shake, Fade }

        sealed class AnimStep
        {
            public StepKind Kind;
            public float Duration;

            public int A = -1, B = -1;          // Swap / Shake 用
            public int[] Cleared;               // Clear 用
            public Color[] Flash;               // Clear 用（与 Cleared 等长）

            public Vector2[] From;              // Fall 用：索引 = flat，起始位置
            public float[] Delay;               // Fall 用：该格开始下落的延迟
            public float[] Dur;                 // Fall 用：该格下落时长

            public Action OnEnter;
            public Action OnExit;
        }

        BlockView[] _blocks;
        int _selected = -1;
        FloatingTextLayer _floats;

        int _hintA = -1;
        int _hintB = -1;
        float _hintLeft;

        readonly List<AnimStep> _steps = new List<AnimStep>(8);
        int _stepIndex;
        float _elapsed;
        Action _onDone;

        /// <summary>某组被消掉时触发（三个人的索引）。族谱面板靠它画关系图。</summary>
        public event Action<int, int, int> GroupCleared;

        public bool IsBuilt { get { return _blocks != null; } }

        /// <summary>正在播动画。控制器据此拦住点击，避免动画期间又下一手。</summary>
        public bool IsPlaying { get; private set; }

        /// <summary>
        /// 测试用：为 true 时动画一律瞬间完成（直接出终局画面）。
        /// 逻辑断言只看结果不看时序，开着动画会让「点一下立刻断言」全部失效。
        /// </summary>
        public bool InstantPlayback;

        public void Build(RectTransform root, TMP_FontAsset font, FamilyGraph graph, Action<int> onClick)
        {
            if (_blocks != null) return;   // 重复调用直接忽略，避免叠出 128 个方块

            _blocks = new BlockView[BoardModel.CellCount];

            for (int row = 0; row < BoardModel.Rows; row++)
            {
                for (int col = 0; col < BoardModel.Cols; col++)
                {
                    int flat = BoardModel.ToFlat(row, col);
                    BlockView view = BlockView.Create(root, font, graph, onClick);
                    view.name = "Block_" + row + "_" + col;
                    view.SetFlat(flat);
                    view.SetPosition(row, col);
                    view.Clear();
                    _blocks[flat] = view;
                }
            }

            // 飘字层最后建 = 最后一个子物体 = 画在方块之上
            _floats = FloatingTextLayer.Create(root, font);
        }

        /// <summary>按盘面数据刷新全部格子（瞬间生效，不走动画）。</summary>
        public void Refresh(BoardModel board)
        {
            if (_blocks == null) return;

            for (int flat = 0; flat < BoardModel.CellCount; flat++)
            {
                int person = board[flat];
                if (person >= 0) _blocks[flat].Bind(person);
                else _blocks[flat].Clear();
            }
        }

        public void SetSelected(int flat)
        {
            if (_blocks == null) return;

            if (_selected >= 0 && _selected < _blocks.Length) _blocks[_selected].SetSelected(false);
            _selected = flat;
            if (_selected >= 0 && _selected < _blocks.Length) _blocks[_selected].SetSelected(true);
        }

        public void ClearSelection()
        {
            SetSelected(-1);
        }

        // ═══════════════════════ 布局：族谱展开时让位 ═══════════════════════

        /// <summary>
        /// 族谱展开时把盘面整体缩小并向左让位；收起时复原。
        /// 缩放的是 BoardRoot 的 localScale —— 方块坐标、点击射线都由 Transform 自动跟着变，
        /// 不需要逐个改 64 个方块的坐标。
        /// </summary>
        public void SetTreeMode(bool treeOpen)
        {
            var rect = (RectTransform)transform;

            if (!treeOpen)
            {
                rect.localScale = Vector3.one;
                rect.anchoredPosition = BoardTheme.BoardAnchoredPosition;
                return;
            }

            // 族谱带贴在画面底部（BottomMargin + BandHeight），
            // 盘面可用区 = HUD 下沿 到 族谱带上沿。按这个高度反算缩放。
            float canvasHeight = 1080f;
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                var canvasRect = canvas.transform as RectTransform;
                if (canvasRect != null && canvasRect.rect.height > 1f) canvasHeight = canvasRect.rect.height;
            }

            const float bandOccupy = 24f + 300f;    // 与 FamilyTreePanel 的 BottomMargin/BandHeight 对齐
            const float hudHeight = 110f;

            float top = canvasHeight * 0.5f - hudHeight;
            float bottom = -canvasHeight * 0.5f + bandOccupy;
            float available = Mathf.Max(200f, top - bottom);

            float scale = Mathf.Min(0.78f, available * 0.94f / BoardTheme.BoardExtent);

            rect.localScale = new Vector3(scale, scale, 1f);
            rect.anchoredPosition = new Vector2(0f, (top + bottom) * 0.5f);
        }

        // ═══════════════════════ 下一步提示 ═══════════════════════

        /// <summary>
        /// 把一对可交换的格子用脉冲描边标出来，不替玩家动手。
        /// 提示只是「画面上亮一下」，不动数据，所以随时可以取消、也不会影响结算。
        /// </summary>
        public void FlashHint(int flatA, int flatB, float duration = HintDuration)
        {
            if (_blocks == null) return;

            ClearHint();
            if (flatA < 0 || flatB < 0) return;

            _hintA = flatA;
            _hintB = flatB;
            _hintLeft = duration;
            TickHint();
        }

        public void ClearHint()
        {
            if (_blocks != null)
            {
                if (_hintA >= 0) _blocks[_hintA].SetFlash(null);
                if (_hintB >= 0) _blocks[_hintB].SetFlash(null);
            }

            _hintA = -1;
            _hintB = -1;
            _hintLeft = 0f;
        }

        void TickHint()
        {
            if (_hintA < 0) return;

            _hintLeft -= Time.unscaledDeltaTime;
            if (_hintLeft <= 0f)
            {
                ClearHint();
                return;
            }

            // 呼吸式脉冲：0.55~1.0，比一闪一闪更不刺眼
            float alpha = 0.775f + 0.225f * Mathf.Sin(Time.unscaledTime * 7f);
            _blocks[_hintA].SetFlash(HintFlash, alpha);
            _blocks[_hintB].SetFlash(HintFlash, alpha);
        }

        // ═══════════════════════ 对外播放接口 ═══════════════════════

        /// <summary>
        /// 回放一次交换：对调 → 逐轮（消除 → 下落）→ 收尾。
        /// afterSwap 是交换已生效的盘面，final 是这手棋全部结算完的终局盘面。
        /// </summary>
        public void PlayMove(
            BoardModel afterSwap, int flatA, int flatB,
            List<BoardResolver.RoundResult> rounds,
            BoardModel final, bool reshuffled, Action onDone)
        {
            if (_blocks == null)
            {
                if (onDone != null) onDone();
                return;
            }

            if (InstantPlayback)
            {
                Refresh(final);
                ClearSelection();

                // 即时播放下没有时间线，OnEnter 不会跑，所以这里显式补发消除事件，
                // 否则族谱面板收不到任何一次消除（自检跑的就是这条路径）。
                if (GroupCleared != null)
                {
                    BoardModel prevBoard = afterSwap;
                    for (int i = 0; i < rounds.Count; i++)
                    {
                        BoardResolver.RoundResult round = rounds[i];
                        FireGroupCleared(round, prevBoard);
                        prevBoard = round.BoardAfterGravity;
                    }
                }

                if (onDone != null) onDone();
                return;
            }

            ClearHint();
            CancelPlayback();
            Refresh(afterSwap);
            ClearSelection();

            if (rounds.Count == 0)
            {
                Refresh(final);
                ClearSelection();
                if (onDone != null) onDone();
                return;
            }

            _steps.Add(BuildSwap(flatA, flatB));

            BoardModel prev = afterSwap;
            for (int i = 0; i < rounds.Count; i++)
            {
                BoardResolver.RoundResult round = rounds[i];
                _steps.Add(BuildClear(round, prev));
                _steps.Add(BuildFall(prev, round.BoardAfterGravity, round.ClearedCells));
                prev = round.BoardAfterGravity;
            }

            // 只有洗牌才需要「淡入」这一步；不洗牌时终局盘面等于最后一轮的快照，不必再刷一遍
            AnimStep settle = BuildSettle(final, reshuffled);
            if (settle != null) _steps.Add(settle);

            _onDone = onDone;
            _stepIndex = 0;
            _elapsed = 0f;
            IsPlaying = true;
            BeginStep(_steps[0]);
        }

        /// <summary>
        /// 立刻丢掉正在播的时间线。
        /// 重新开始时必须调用：否则上一局没放完的步骤会继续作用在新盘面上，
        /// 方块会被搬来搬去，而且结束回调还会在本局触发一次 BoardChanged。
        /// </summary>
        public void CancelPlayback()
        {
            if (_blocks != null && _steps.Count > 0)
            {
                for (int flat = 0; flat < BoardModel.CellCount; flat++)
                {
                    _blocks[flat].SetAlpha(1f);
                    _blocks[flat].SetScale(1f);
                    _blocks[flat].SetFlash(null);
                    RestorePosition(flat);
                }
            }

            _steps.Clear();
            _stepIndex = 0;
            _elapsed = 0f;
            IsPlaying = false;
            _onDone = null;
        }

        /// <summary>交换失败的抖动。不走时间线队列，直接当作唯一一步播。</summary>
        public void PlayShake(int flatA, int flatB)
        {
            if (_blocks == null || InstantPlayback) return;

            ClearHint();

            var step = new AnimStep
            {
                Kind = StepKind.Shake,
                Duration = ShakeDuration,
                A = flatA,
                B = flatB,
                OnExit = () =>
                {
                    RestorePosition(flatA);
                    RestorePosition(flatB);
                }
            };

            _steps.Clear();
            _steps.Add(step);

            _onDone = null;
            _stepIndex = 0;
            _elapsed = 0f;
            IsPlaying = true;
            BeginStep(step);
        }

        // ═══════════════════════ 时间线构建 ═══════════════════════

        AnimStep BuildSwap(int a, int b)
        {
            var from = new Vector2[BoardModel.CellCount];
            from[a] = HomeOf(b);
            from[b] = HomeOf(a);

            return new AnimStep
            {
                Kind = StepKind.Swap,
                Duration = SwapDuration,
                A = a,
                B = b,
                From = from
            };
        }

        /// <summary>
        /// 把这一轮消掉的组报给外部。注意报的是**人员索引**（盘面里存的值），
        /// 不是格子索引 —— 格子是 0~63，人员是 0~46，两者混用会越界。
        /// </summary>
        void FireGroupCleared(BoardResolver.RoundResult round, BoardModel beforeClear)
        {
            if (GroupCleared == null) return;

            for (int i = 0; i < round.GroupCount; i++)
            {
                MatchGroup g = round.Groups[i];
                int a = beforeClear[g.CellA];
                int b = beforeClear[g.CellB];
                int c = beforeClear[g.CellC];

                if (a < 0 || b < 0 || c < 0) continue;
                GroupCleared(a, b, c);
            }
        }

        AnimStep BuildClear(BoardResolver.RoundResult round, BoardModel beforeClear)
        {
            int count = round.ClearedCells.Count;
            var cleared = new int[count];
            var flash = new Color[count];

            for (int i = 0; i < count; i++)
            {
                cleared[i] = round.ClearedCells[i];
                // 消除清单按「每组三个」排列，所以第 i 格属于第 i/3 组
                RuleKind rule = round.Groups[i / 3].Rule;
                flash[i] = rule == RuleKind.R1 ? GoldFlash
                         : rule == RuleKind.R2 ? SilverFlash
                         : WhiteFlash;
            }

            int roundNumber = round.Round;

            return new AnimStep
            {
                Kind = StepKind.Clear,
                Duration = ClearDuration,
                Cleared = cleared,
                Flash = flash,
                OnEnter = () =>
                {
                    for (int i = 0; i < cleared.Length; i++) _blocks[cleared[i]].SetFlash(flash[i]);
                    AudioBank.PlayClear(roundNumber);
                    SpawnPopups(round);

                    FireGroupCleared(round, beforeClear);
                },
                OnExit = () =>
                {
                    for (int i = 0; i < cleared.Length; i++) _blocks[cleared[i]].Clear();
                }
            };
        }

        AnimStep BuildFall(BoardModel prev, BoardModel next, IReadOnlyList<int> cleared)
        {
            var isCleared = new bool[BoardModel.CellCount];
            for (int i = 0; i < cleared.Count; i++) isCleared[cleared[i]] = true;

            var from = new Vector2[BoardModel.CellCount];
            var delay = new float[BoardModel.CellCount];
            var dur = new float[BoardModel.CellCount];
            float total = 0.05f;

            for (int col = 0; col < BoardModel.Cols; col++)
            {
                // 这一列消掉几个 ⇒ 顶上就要补几个
                int clearedInColumn = 0;
                for (int row = 0; row < BoardModel.Rows; row++)
                    if (isCleared[BoardModel.ToFlat(row, col)]) clearedInColumn++;

                // 幸存的方块按原顺序压到底部
                int writeRow = BoardModel.Rows - 1;
                for (int row = BoardModel.Rows - 1; row >= 0; row--)
                {
                    int flat = BoardModel.ToFlat(row, col);
                    if (prev[flat] < 0 || isCleared[flat]) continue;

                    if (writeRow != row)
                    {
                        int target = BoardModel.ToFlat(writeRow, col);
                        from[target] = BoardTheme.PositionOf(row, col);
                        delay[target] = col * ColumnStagger;

                        float d = Mathf.Min((writeRow - row) * FallPerRow, MaxFallDuration);
                        dur[target] = d;

                        float end = delay[target] + d;
                        if (end > total) total = end;
                    }
                    writeRow--;
                }

                // 顶部补的新方块：从盘面上方滑进来
                for (int row = 0; row < clearedInColumn; row++)
                {
                    int flat = BoardModel.ToFlat(row, col);
                    from[flat] = BoardTheme.PositionOf(row - clearedInColumn, col);
                    delay[flat] = col * ColumnStagger;

                    float d = Mathf.Min(clearedInColumn * FallPerRow, MaxFallDuration);
                    dur[flat] = d;

                    float end = delay[flat] + d;
                    if (end > total) total = end;
                }
            }

            return new AnimStep
            {
                Kind = StepKind.Fall,
                Duration = total,
                From = from,
                Delay = delay,
                Dur = dur,
                OnEnter = () =>
                {
                    Refresh(next);                       // 内容先到位（含新补的方块）
                    for (int flat = 0; flat < BoardModel.CellCount; flat++)
                    {
                        if (dur[flat] > 0f) _blocks[flat].SetAnchoredPosition(from[flat]);
                    }
                },
                OnExit = () =>
                {
                    for (int flat = 0; flat < BoardModel.CellCount; flat++) RestorePosition(flat);
                }
            };
        }

        /// <summary>收尾：只有洗牌后才需要（整体淡入）。不洗牌返回 null，表示「无事可做」。</summary>
        AnimStep BuildSettle(BoardModel final, bool reshuffled)
        {
            if (!reshuffled) return null;

            return new AnimStep
            {
                Kind = StepKind.Fade,
                Duration = FadeDuration,
                OnEnter = () =>
                {
                    Refresh(final);
                    AudioBank.PlayShuffle();
                    for (int flat = 0; flat < BoardModel.CellCount; flat++) _blocks[flat].SetAlpha(0f);
                },
                OnExit = () =>
                {
                    for (int flat = 0; flat < BoardModel.CellCount; flat++)
                    {
                        _blocks[flat].SetAlpha(1f);
                        _blocks[flat].SetFlash(null);
                        RestorePosition(flat);
                    }
                }
            };
        }

        void SpawnPopups(BoardResolver.RoundResult round)
        {
            if (_floats == null) return;

            for (int i = 0; i < round.GroupCount; i++)
            {
                MatchGroup group = round.Groups[i];
                RuleKind rule = group.Rule;

                Color color = rule == RuleKind.R1 ? GoldFlash
                            : rule == RuleKind.R2 ? SilverFlash
                            : WhiteFlash;
                float size = rule == RuleKind.R1 ? 38f : 32f;

                Vector2 center = (HomeOf(group.CellA) + HomeOf(group.CellB) + HomeOf(group.CellC)) / 3f;
                _floats.Spawn(center, "+" + ScoreKeeper.PointsFor(rule, round.Round), color, size);
            }
        }

        // ═══════════════════════ 播放循环 ═══════════════════════

        void Update()
        {
            TickHint();

            if (!IsPlaying) return;

            AnimStep step = _steps[_stepIndex];
            _elapsed += Time.deltaTime;

            float t = step.Duration <= 0f ? 1f : Mathf.Clamp01(_elapsed / step.Duration);
            Apply(step, t);

            if (t < 1f) return;

            if (step.OnExit != null) step.OnExit();

            _stepIndex++;
            _elapsed = 0f;

            if (_stepIndex >= _steps.Count)
            {
                IsPlaying = false;
                _steps.Clear();

                Action done = _onDone;
                _onDone = null;
                if (done != null) done();
                return;
            }

            BeginStep(_steps[_stepIndex]);
        }

        void BeginStep(AnimStep step)
        {
            _elapsed = 0f;
            if (step.OnEnter != null) step.OnEnter();
            Apply(step, 0f);
        }

        void Apply(AnimStep step, float t)
        {
            switch (step.Kind)
            {
                case StepKind.Swap:
                {
                    float k = Smooth(t);
                    _blocks[step.A].SetAnchoredPosition(Vector2.Lerp(step.From[step.A], HomeOf(step.A), k));
                    _blocks[step.B].SetAnchoredPosition(Vector2.Lerp(step.From[step.B], HomeOf(step.B), k));
                    break;
                }

                case StepKind.Clear:
                {
                    // 先「顿一下」再缩小：EaseIn 让消失集中在后半段，玩家更容易看清是哪一组没了
                    float k = t * t;
                    for (int i = 0; i < step.Cleared.Length; i++)
                    {
                        BlockView block = _blocks[step.Cleared[i]];
                        block.SetScale(1f - k);
                        block.SetAlpha(1f - t);
                    }
                    break;
                }

                case StepKind.Fall:
                {
                    for (int flat = 0; flat < BoardModel.CellCount; flat++)
                    {
                        if (step.Dur[flat] <= 0f) continue;

                        float k = Mathf.Clamp01((_elapsed - step.Delay[flat]) / step.Dur[flat]);
                        _blocks[flat].SetAnchoredPosition(
                            Vector2.Lerp(step.From[flat], HomeOf(flat), k * k));   // 加速下落，像重力
                    }
                    break;
                }

                case StepKind.Shake:
                {
                    float damp = 1f - t;
                    float dx = Mathf.Sin(t * 3f * Mathf.PI * 2f) * 9f * damp;

                    _blocks[step.A].SetAnchoredPosition(HomeOf(step.A) + new Vector2(dx, 0f));
                    _blocks[step.B].SetAnchoredPosition(HomeOf(step.B) + new Vector2(-dx, 0f));
                    break;
                }

                case StepKind.Fade:
                {
                    // 时长 0 = 这次不需要淡入。此时 t 恒为 1，但 BeginStep 会先用 t=0 调一次，
                    // 真按 0 写就是「整个盘面透明一帧」—— 每走一手闪一下，正是之前的频闪来源。
                    if (step.Duration <= 0f) break;

                    for (int flat = 0; flat < BoardModel.CellCount; flat++) _blocks[flat].SetAlpha(t);
                    break;
                }
            }
        }

        static float Smooth(float t)
        {
            return t * t * (3f - 2f * t);
        }

        static Vector2 HomeOf(int flat)
        {
            return BoardTheme.PositionOf(BoardModel.RowOf(flat), BoardModel.ColOf(flat));
        }

        void RestorePosition(int flat)
        {
            if (flat < 0 || flat >= _blocks.Length) return;
            _blocks[flat].SetAnchoredPosition(HomeOf(flat));
        }
    }
}
