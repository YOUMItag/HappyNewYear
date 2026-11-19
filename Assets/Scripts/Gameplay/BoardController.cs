using System;
using System.Collections.Generic;
using Game.Core;
using Game.UI;
using TMPro;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// 玩法主控：串起 盘面模型 → 检测 → 结算 → 视图，并处理点击选择与交换。
    ///
    /// 为什么交换逻辑直接写在这里，而不是单独拆一个 InputRouter：
    /// 这段逻辑只有 20 行（选中 / 取消 / 相邻判定 / 改选），拆成独立类要多一层回调转发，
    /// 收益为负。等 M3 加了「结算面板挡住输入」「动画期间禁手」再考虑拆。
    ///
    /// 原方案里列的 InputRouter 因此合并进来，行为不变。
    ///
    /// @trace MECH-004, MECH-008, MECH-009, MECH-010, MECH-011, MECH-012
    /// </summary>
    public sealed class BoardController : MonoBehaviour
    {
        /// <summary>同名上限：盘面上同一种称谓最多出现几个。2 是实测最优（DOC-05）。</summary>
        public const int SameNameCap = 2;

        FamilyGraph _graph;
        BoardView _view;
        BoardModel _board;

        MatchDetector _detector;
        CellPool _pool;
        BoardGenerator _generator;
        BoardResolver _resolver;
        MoveValidator _validator;
        ScoreKeeper _score;
        System.Random _rng;

        int _selected = -1;
        readonly System.Collections.Generic.List<int> _autoMoveBuffer =
            new System.Collections.Generic.List<int>(24);

        /// <summary>盘面或分数发生变化。HUD 订阅它刷新显示。</summary>
        public event System.Action BoardChanged;

        /// <summary>60 手用完时触发，参数是最终计分板。结算面板订阅它。</summary>
        public event System.Action<ScoreKeeper> LevelFinished;

        public ScoreKeeper Score { get { return _score; } }
        public BoardModel Board { get { return _board; } }
        public FamilyGraph Graph { get { return _graph; } }

        /// <summary>本局手数是否已用完。自动演示与外部工具用它判断该重开了。</summary>
        public bool IsFinished { get { return _score != null && _score.IsFinished; } }

        /// <summary>正在播动画。此期间的点击一律忽略，避免动画没放完就叠下一手。</summary>
        public bool IsAnimating { get { return _view != null && _view.IsPlaying; } }

        /// <summary>
        /// 关掉动画让每手瞬间出结果。给自动化测试用 —— 逻辑断言只看结果不看时序。
        /// 播放模式下按正常速度运行时保持 false。
        /// </summary>
        /// <summary>族谱展开时让盘面缩小左移；收起时复原。</summary>
        public void SetTreePanelOpen(bool open)
        {
            if (_view != null) _view.SetTreeMode(open);
        }

        public void SetInstantPlayback(bool instant)
        {
            if (_view != null) _view.InstantPlayback = instant;
        }

        /// <summary>本局出现过的最大连锁轮次，结算页显示「最大连锁 ×N」。</summary>
        public int MaxChainRound { get; private set; }

        /// <summary>
        /// 自动走一手：内部找一个合法交换并执行，走的是与玩家点击完全相同的路径
        /// （同样计步、同样结算、同样触发 HUD 与结算事件）。
        /// 供游戏内自动演示（AutoPlayer）与需要程序化推进局面的工具使用。
        /// 返回 false 表示当前没有合法步（已尝试洗牌）或本局已结束。
        /// </summary>
        public bool TryRandomMove()
        {
            if (_board == null || _score == null) return false;
            if (_score.IsFinished) return false;
            if (IsAnimating) return false;

            int a, b;
            if (!AutoMoveFinder.TryPick(_board, _detector, _rng, _autoMoveBuffer, out a, out b))
            {
                // 无合法步：先洗牌，这一步不计入手数
                bool shuffled = EnsurePlayable();
                if (shuffled && _view != null) _view.Refresh(_board);
                if (BoardChanged != null) BoardChanged();
                return false;
            }

            _selected = -1;
            if (_view != null) _view.ClearSelection();
            TrySwap(a, b);
            return true;
        }

        /// <summary>
        /// 找一步可行交换并用脉冲描边标出来，**不执行**。
        /// 返回 false 表示当前给不出提示（动画中 / 已结算 / 盘面无解）。
        /// 提示只改画面不动数据，所以随便点、随时换、不会影响计分。
        /// </summary>
        public bool TryShowHint(out int flatA, out int flatB)
        {
            flatA = -1;
            flatB = -1;

            if (_board == null || _score == null) return false;
            if (IsAnimating || _score.IsFinished) return false;

            if (!AutoMoveFinder.TryPick(_board, _detector, _rng, _autoMoveBuffer, out flatA, out flatB))
                return false;

            if (_view != null) _view.FlashHint(flatA, flatB);
            Debug.Log("[Board] 提示：(" + BoardModel.RowOf(flatA) + "," + BoardModel.ColOf(flatA)
                + ") ⇄ (" + BoardModel.RowOf(flatB) + "," + BoardModel.ColOf(flatB) + ")");
            return true;
        }

        public void Prepare(FamilyGraph graph)
        {
            if (graph == null) throw new System.ArgumentNullException(nameof(graph));

            _graph = graph;
            _rng = new System.Random();
            _detector = new MatchDetector(graph);

            int[] roster = new int[graph.Count];
            for (int i = 0; i < roster.Length; i++) roster[i] = i;

            _pool = new CellPool(graph, _rng, roster, SameNameCap);
            _generator = new BoardGenerator(graph, _detector, _pool, _rng, 3, 400);
            _resolver = new BoardResolver(_detector, _pool);
            _validator = new MoveValidator(_detector);
            _score = new ScoreKeeper();

            AudioBank.Init(gameObject);

            if (_pool.CapacitySlots < BoardModel.CellCount)
            {
                Debug.LogWarning("[Board] 同名上限 " + SameNameCap + " 只装得下 " + _pool.CapacitySlots
                    + " 格（需 64），约束会退化为纯随机");
            }
        }

        /// <summary>某组被消掉（三人索引）。族谱面板订阅它。</summary>
        public event Action<int, int, int> GroupCleared;

        public void AttachView(BoardView view)
        {
            _view = view;
            _view.GroupCleared += (a, b, c) =>
            {
                if (GroupCleared != null) GroupCleared(a, b, c);
            };
        }

        public void StartNewGame()
        {
            if (_view == null) throw new System.InvalidOperationException("AttachView 必须先调用");

            _score.Reset();
            MaxChainRound = 0;
            _board = _generator.Build();
            _selected = -1;
            _view.ClearSelection();
            _view.ClearHint();          // 新一局不该还留着上一局的提示脉冲
            _view.CancelPlayback();     // 上一局没放完的动画必须丢掉，否则会搬到新盘面上
            _view.Refresh(_board);

            Debug.Log("[Board] 新局：可走 " + _detector.CountMoves(_board) + " 手，"
                + "同名上限 " + SameNameCap);

            if (BoardChanged != null) BoardChanged();
        }

        public void OnBlockClicked(int flat)
        {
            if (_board == null) return;

            // 动画没播完不接受输入（否则方块还在飞就能再下一手，画面会错位）
            if (IsAnimating) return;

            // 60 手用完后禁止继续操作（结算面板的遮罩已挡一层，这里是第二道保险）
            if (_score != null && _score.IsFinished) return;

            // 第一次点击：选中
            if (_selected < 0)
            {
                _selected = flat;
                _view.SetSelected(flat);
                return;
            }

            // 再点同一格：取消选中
            if (_selected == flat)
            {
                _selected = -1;
                _view.ClearSelection();
                return;
            }

            // 点相邻格：尝试交换
            if (MoveValidator.AreAdjacent(_selected, flat))
            {
                int from = _selected;
                _selected = -1;
                _view.ClearSelection();
                TrySwap(from, flat);
                return;
            }

            // 点不相邻的格子：改选（不交换、不报错，符合玩家直觉）
            _selected = flat;
            _view.SetSelected(flat);
        }

        void TrySwap(int a, int b)
        {
            if (IsAnimating) return;

            if (!_validator.TrySwap(_board, a, b))
            {
                // 回退反馈：抖三下 + 一声低音，让玩家知道「这步不成立」而不是「点了没反应」
                if (_view != null) _view.PlayShake(a, b);
                AudioBank.PlayInvalid();
                Debug.Log("[Board] 交换不成立，已弹回（不计步）");
                return;
            }

            // 交换已生效的盘面要留一份：动画从「交换后」的状态开始演
            BoardModel afterSwap = _board.Clone();

            _score.ConsumeMove();
            List<BoardResolver.RoundResult> rounds = ResolveBoard();
            bool reshuffled = EnsurePlayable();

            PlayAndNotify(rounds, afterSwap, a, b, reshuffled);
        }

        /// <summary>把这一手交给视图回放，动画结束后再刷新 HUD / 弹结算。</summary>
        void PlayAndNotify(
            List<BoardResolver.RoundResult> rounds, BoardModel afterSwap,
            int a, int b, bool reshuffled)
        {
            if (_view == null)
            {
                OnMoveSettled();
                return;
            }

            _view.PlayMove(afterSwap, a, b, rounds, _board, reshuffled, OnMoveSettled);
        }

        /// <summary>一手棋真正「落定」：HUD 刷新、必要时弹结算。</summary>
        void OnMoveSettled()
        {
            if (BoardChanged != null) BoardChanged();

            if (_score.IsFinished)
            {
                Debug.Log("[Board] 60 手用完，结算：" + _score.Score + " 分，" + _score.Stars
                    + " 星，最大连锁 ×" + MaxChainRound);
                if (LevelFinished != null) LevelFinished(_score);
            }
        }

        List<BoardResolver.RoundResult> ResolveBoard()
        {
            List<BoardResolver.RoundResult> rounds = _resolver.Resolve(_board);

            int groups = 0;
            for (int i = 0; i < rounds.Count; i++)
            {
                BoardResolver.RoundResult round = rounds[i];
                if (round.Round > MaxChainRound) MaxChainRound = round.Round;

                for (int k = 0; k < round.GroupCount; k++)
                {
                    MatchGroup group = round.Groups[k];
                    _score.Award(group.Rule, round.Round);
                    groups++;
                }
            }

            Debug.Log("[Board] 第 " + _score.MovesUsed + " 手：消 " + groups + " 组，累计 "
                + _score.Score + " 分");

            return rounds;
        }

        /// <summary>消除后若盘面无合法步，立刻重造盘面。返回是否真的洗了。</summary>
        bool EnsurePlayable()
        {
            if (_detector.HasMatch(_board)) return false;
            if (_detector.CountMoves(_board, 1) > 0) return false;

            Debug.LogWarning("[Board] 盘面无合法步，自动洗牌（反向构造）");
            _board = _generator.Build();
            return true;
        }
    }
}
