using Game.Core;
using Game.Gameplay;
using Game.UI;
using TMPro;
using UnityEngine;

namespace Game.Boot
{
    /// <summary>
    /// 入口：按固定顺序初始化各系统，避免依赖各个 MonoBehaviour 的 Awake 隐式顺序。
    ///   1. 载入族谱 JSON → FamilyGraph
    ///   2. 建 BoardController（纯逻辑编排）
    ///   3. 建 BoardView 并回连控制器
    ///   4. 开新局
    ///
    /// 三个引用由编辑器脚本（M2Setup）自动填好，不需要在 Inspector 里手动拖。
    ///
    /// @trace MECH-001, MECH-002
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        [SerializeField] TextAsset _familyJson;
        [SerializeField] TMP_FontAsset _font;
        [SerializeField] RectTransform _boardRoot;

        void Start()
        {
            if (_familyJson == null)
            {
                Debug.LogError("[GameBootstrap] 未指定族谱 JSON（Assets/Data/FamilyTree.json）");
                return;
            }
            if (_font == null)
            {
                Debug.LogError("[GameBootstrap] 未指定 TMP 字体资产。请先执行 Tools → 过年啦 → 一键搭建 M2 场景");
                return;
            }
            if (_boardRoot == null)
            {
                Debug.LogError("[GameBootstrap] 未指定 BoardRoot");
                return;
            }

            FamilyGraph graph;
            try
            {
                graph = new FamilyGraph(FamilyDatabase.LoadFromJson(_familyJson.text));
            }
            catch (System.Exception e)
            {
                Debug.LogError("[GameBootstrap] 族谱解析失败：" + e.Message);
                return;
            }

            Debug.Log("[GameBootstrap] 族谱载入：" + graph.Count + " 人 / " + graph.TitleCount
                + " 种称谓 / 三元组 " + graph.TripleCount
                + "（R1 " + graph.R1Count + " + R2 " + graph.R2Count + " + R3 " + graph.R3Count + "）");

            BoardController controller = gameObject.GetComponent<BoardController>();
            if (controller == null) controller = gameObject.AddComponent<BoardController>();
            controller.Prepare(graph);

            BoardView view = _boardRoot.GetComponent<BoardView>();
            if (view == null) view = _boardRoot.gameObject.AddComponent<BoardView>();

            view.Build(_boardRoot, _font, graph, controller.OnBlockClicked);
            controller.AttachView(view);

            // Canvas = BoardRoot 的父节点，HUD 与结算面板都挂在它下面
            Transform canvas = _boardRoot.parent;
            if (canvas == null)
            {
                Debug.LogError("[GameBootstrap] BoardRoot 没有父节点，找不到 Canvas");
                return;
            }

            HudView hud = HudView.Create(canvas, _font);
            // 规则块与暂停面板都必须建在结算面板之前：
            // 结算面板靠「最后一个子物体」保证画在所有东西上面
            RulesPanel rules = RulesPanel.Create(canvas, _font);
            PausePanel pause = PausePanel.Create(canvas, _font);
            FamilyTreePanel tree = FamilyTreePanel.Create(canvas, _font, graph);
            ResultPanel result = ResultPanel.Create(canvas, _font);

            // 任一面板弹出时先收起族谱：族谱带建得比这些面板晚，层级更高，会压住面板底部。
            rules.Opened += tree.Hide;
            pause.Opened += tree.Hide;

            tree.OpenChanged += open => controller.SetTreePanelOpen(open);

            // 族谱只在玩家点按钮时展开：自动展开会盖住「玩法说明」等面板，也打断对局节奏。
            // 消除时只更新里面高亮的内容，玩家想看再自己点开。
            controller.GroupCleared += (a, b, c) => tree.ShowMatch(a, b, c);
            controller.BoardChanged += () => hud.Refresh(controller.Score);
            controller.LevelFinished += score =>
            {
                tree.Hide();                  // 结算面板同样不能与族谱带叠在一起
                pause.Hide();
                pause.Interactable = false;   // 结算后 ESC / 暂停按钮都失效，避免开出看不见的暂停层
                result.Show(score, controller.MaxChainRound);
            };

            hud.HintRequested += () =>
            {
                int hintA, hintB;
                if (!controller.TryShowHint(out hintA, out hintB))
                    Debug.Log("[GameBootstrap] 当前给不出提示（动画中 / 已结算 / 盘面无解）");
            };

            result.RestartRequested += () =>
            {
                StopAutoPlay("重新开始");
                result.Hide();
                pause.Interactable = true;
                controller.StartNewGame();
            };

            pause.ContinueRequested += pause.Hide;
            pause.RestartRequested += () =>
            {
                StopAutoPlay("重新开始");
                pause.Hide();                 // 先把 timeScale 恢复，否则新局一开局就是冻住的
                result.Hide();
                pause.Interactable = true;
                controller.StartNewGame();
            };
            pause.QuitRequested += QuitGame;

            controller.StartNewGame();

            // 自动演示 + M 静音：**正式包也带**（用户明确要求，不做 UI，只在新手指引里说明按键）。
            // 它不按 F9 就什么都不做，常驻代价只有两个按键查询。
            if (gameObject.GetComponent<AutoPlayer>() == null) gameObject.AddComponent<AutoPlayer>();

            Debug.Log("[GameBootstrap] ready");
        }

        /// <summary>
        /// 重新开始前先停下自动演示。
        /// 否则演示脚本会接着在新一局里继续打，玩家点的「重新开始」等于没生效。
        /// </summary>
        void StopAutoPlay(string reason)
        {
            var player = GetComponent<AutoPlayer>();
            if (player != null && player.Running) player.Stop(reason);
        }

        /// <summary>
        /// 退出游戏。编辑器里 Application.Quit() 不会关编辑器，只会把 Play 停掉 —— 所以分两条路。
        /// </summary>
        static void QuitGame()
        {
#if UNITY_EDITOR
            Debug.Log("[GameBootstrap] 退出游戏（编辑器内：停止 Play）");
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Debug.Log("[GameBootstrap] 退出游戏");
            Application.Quit();
#endif
        }
    }
}
