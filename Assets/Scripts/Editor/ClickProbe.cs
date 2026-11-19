using System;
using System.Collections.Generic;
using System.Text;
using Game.Boot;
using Game.Core;
using Game.Gameplay;
using Game.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.EditorTools
{
    /// <summary>
    /// 点击链路自检：在**真实播放模式**里验证「鼠标点击 → UGUI 射线 → 方块 → 控制器」。
    ///
    /// 为什么必须在播放模式跑，而不能像 VerifyRuntime 那样在编辑模式下 SendMessage：
    ///   GraphicRaycaster.Raycast 的第一行是
    ///       if (GraphicRegistry.GetRaycastableGraphicsForCanvas(canvas).Count == 0) return;
    ///   而 GraphicRegistry 由 Graphic.OnEnable 登记；编辑模式下场景组件的 OnEnable 不执行，
    ///   注册表恒为空、EventSystem.current 恒为 null，射线永远 0 命中 —— 与游戏本身无关。
    ///   进播放模式，这一整套由引擎按正常顺序跑，测出来的才是玩家会遇到的路径。
    ///
    /// 为什么分成多个阶段、每阶段间隔若干帧：
    ///   1. 进播放模式会触发域重载，静态字段与委托全被清空 —— 状态必须存 SessionState。
    ///   2. 结算面板刚 SetActive 时 Graphic.depth 还是 -1（没进过渲染批次），
    ///      射线按 depth 排序会误判「面板没挡住棋盘」；必须等一帧让批次跑过。
    ///
    /// 仍然测不到的一步：StandaloneInputModule 从操作系统读鼠标位置与按键。
    /// 这一步只是把 Input.mousePosition 填进 PointerEventData.position；
    /// 之后的射线命中判定与事件派发（ExecuteEvents）全部由本自检覆盖。
    ///
    /// 用法：
    ///   菜单 Tools → 过年啦 → 自检鼠标点击链路（播放模式）
    ///   命令行 -executeMethod Game.EditorTools.ClickProbe.RunFromCommandLine（不要加 -quit）
    /// </summary>
    public static class ClickProbe
    {
        const string ScenePath = "Assets/Scenes/Main.unity";
        const int WarmupFrames = 5;

        const string ArmedKey = "ClickProbe.Armed";
        const string FramesKey = "ClickProbe.Frames";
        const string PhaseKey = "ClickProbe.Phase";
        const string OkKey = "ClickProbe.Ok";
        const string LastFrameKey = "ClickProbe.LastFrame";
        const string WaitKey = "ClickProbe.Wait";
        const string ArmTimeKey = "ClickProbe.ArmTime";
        const string MinVisKey = "ClickProbe.MinVisible";
        const string EnteredKey = "ClickProbe.Entered";

        [InitializeOnLoadMethod]
        static void ResumeAfterDomainReload()
        {
            if (!SessionState.GetBool(ArmedKey, false)) return;

            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            Debug.Log("[CLICK] 域重载后恢复监听");
        }

        [MenuItem("Tools/过年啦/自检鼠标点击链路（播放模式）", false, 23)]
        public static void Run()
        {
            Begin();
        }

        /// <summary>供 -executeMethod 调用。注意：不能同时传 -quit，否则会在进入播放模式前退出。</summary>
        public static void RunFromCommandLine()
        {
            Begin();
        }

        static void Begin()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogError("[CLICK] 编辑器已在播放模式，请先退出");
                return;
            }

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            SessionState.SetBool(ArmedKey, true);
            SessionState.SetBool(EnteredKey, false);
            SessionState.SetBool(OkKey, true);
            SessionState.SetInt(FramesKey, 0);
            SessionState.SetInt(PhaseKey, 0);
            SessionState.SetFloat(ArmTimeKey, Time.realtimeSinceStartup);

            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.isPlaying = true;

            Debug.Log("[CLICK] 已请求进入播放模式，等待场景初始化…");
        }

        static void Tick()
        {
            if (!SessionState.GetBool(ArmedKey, false))
            {
                EditorApplication.update -= Tick;
                return;
            }

            if (!EditorApplication.isPlaying)
            {
                // 进过播放模式又退出来 = 播放被中断（重编译/崩溃），不能在这里无限等
                if (SessionState.GetBool(EnteredKey, false))
                {
                    SessionState.SetBool(EnteredKey, false);
                    SessionState.SetBool(ArmedKey, false);
                    EditorApplication.update -= Tick;
                    Debug.LogError("[CLICK] 播放模式被中断（已进入过又退出），自检中止");
                    EditorApplication.Exit(3);
                }
                return;
            }

            if (!SessionState.GetBool(EnteredKey, false))
            {
                SessionState.SetBool(EnteredKey, true);
                Debug.Log("[CLICK] 已进入播放模式");
            }

            int frames = SessionState.GetInt(FramesKey, 0) + 1;
            SessionState.SetInt(FramesKey, frames);
            if (frames < WarmupFrames) return;

            // 编辑器的 update 比游戏帧快，光数 update 次数不保证「隔了一帧」。
            // 阶段之间必须真的跨过一个游戏帧，否则刚 SetActive 的面板还没进渲染批次。
            int lastFrame = SessionState.GetInt(LastFrameKey, -1);
            if (Time.frameCount <= lastFrame) return;

            SessionState.SetInt(FramesKey, 0);
            SessionState.SetInt(LastFrameKey, Time.frameCount);

            // 总超时自保：无论卡在哪个阶段，最多跑 150 秒就收工，
            // 避免出现「进程活着但永远不进下一阶段」把编辑器挂死。
            float armTime = SessionState.GetFloat(ArmTimeKey, 0f);
            if (armTime > 0f && Time.realtimeSinceStartup - armTime > 150f)
            {
                int stuck = SessionState.GetInt(PhaseKey, -1);
                Debug.LogError("[CLICK] 总超时（150 秒），卡在阶段 " + stuck + "，强制结束");
                SessionState.SetBool(ArmedKey, false);
                EditorApplication.update -= Tick;
                EditorApplication.Exit(4);
                return;
            }

            int phase = SessionState.GetInt(PhaseKey, 0);
            bool ok = true;
            bool done = false;

            try
            {
                switch (phase)
                {
                    case 0:
                        SessionState.SetInt(PhaseKey, 1);
                        ok = VerifyInputChain();
                        SessionState.SetBool(OkKey, SessionState.GetBool(OkKey, true) && ok);
                        break;

                    case 1:
                        SessionState.SetInt(PhaseKey, 2);
                        FinishLevel();
                        break;

                    case 2:
                        SessionState.SetInt(PhaseKey, 3);
                        ok = VerifyPanelBlocking();
                        SessionState.SetBool(OkKey, SessionState.GetBool(OkKey, true) && ok);
                        break;

                    case 3:
                        SessionState.SetInt(PhaseKey, 4);
                        ok = VerifyRestart();
                        SessionState.SetBool(OkKey, SessionState.GetBool(OkKey, true) && ok);
                        break;

                    case 4:
                        // 新手指引分两帧验：面板刚 SetActive 的当帧 Graphic.depth 还是 -1，
                        // 同帧验「能不能挡住棋盘」会误判成挡不住。
                        SessionState.SetInt(PhaseKey, 5);
                        ok = OpenRulesPanel();
                        SessionState.SetBool(OkKey, SessionState.GetBool(OkKey, true) && ok);
                        break;

                    case 5:
                        SessionState.SetInt(PhaseKey, 20);   // 跳到暂停用例（编号取 20 以免重排后面所有 case）
                        ok = VerifyRulesBlockingAndClose();
                        SessionState.SetBool(OkKey, SessionState.GetBool(OkKey, true) && ok);
                        break;

                    case 6:
                        // 手数为 0，正好拿一手真动画来验「动画期间点不动」
                        SessionState.SetInt(PhaseKey, 7);
                        SessionState.SetFloat(WaitKey, Time.realtimeSinceStartup);
                        ok = StartAnimationCheck();
                        SessionState.SetBool(OkKey, SessionState.GetBool(OkKey, true) && ok);
                        break;

                    case 7:
                        ok = WaitAnimationIdle();
                        SessionState.SetBool(OkKey, SessionState.GetBool(OkKey, true) && ok);
                        break;

                    case 8:
                        // 自动试玩仍用瞬时播放：它的职责是验证「结算即停」，不必再付一遍动画时间
                        SessionState.SetInt(PhaseKey, 9);
                        SessionState.SetFloat(WaitKey, Time.realtimeSinceStartup);
                        ok = StartAutoPlayer();
                        SessionState.SetBool(OkKey, SessionState.GetBool(OkKey, true) && ok);
                        break;

                    case 9:
                        // 等自动试玩把 60 手打完
                        if (!IsLevelFinished())
                        {
                            float waited = Time.realtimeSinceStartup - SessionState.GetFloat(WaitKey, 0f);
                            if (waited > 60f)
                            {
                                Debug.LogError("[CLICK] 自动试玩等了 " + waited.ToString("F0") + " 秒还没打完 60 手");
                                SessionState.SetInt(PhaseKey, 10);
                                SessionState.SetFloat(WaitKey, Time.realtimeSinceStartup);
                                ok = false;
                            }
                            // 未超时就停在阶段 10，下一帧继续等
                        }
                        else
                        {
                            SessionState.SetInt(PhaseKey, 10);
                            // 结算后再多等一会儿，给 AutoPlayer.Update 停止的机会
                            SessionState.SetFloat(WaitKey, Time.realtimeSinceStartup);
                        }
                        break;

                    case 20:
                        SessionState.SetInt(PhaseKey, 21);
                        ok = OpenPausePanel();
                        SessionState.SetBool(OkKey, SessionState.GetBool(OkKey, true) && ok);
                        break;

                    case 21:
                        SessionState.SetInt(PhaseKey, 6);
                        ok = VerifyPauseAndResume();
                        SessionState.SetBool(OkKey, SessionState.GetBool(OkKey, true) && ok);
                        break;

                    case 40:
                        SessionState.SetInt(PhaseKey, 41);
                        ok = RestartHintAndAutoStart();
                        SessionState.SetBool(OkKey, SessionState.GetBool(OkKey, true) && ok);
                        break;

                    case 41:
                        SessionState.SetInt(PhaseKey, 42);
                        ok = OpenPauseWhileAuto();
                        SessionState.SetBool(OkKey, SessionState.GetBool(OkKey, true) && ok);
                        break;

                    case 42:
                        ok = RestartWhileAuto();
                        SessionState.SetBool(OkKey, SessionState.GetBool(OkKey, true) && ok);
                        done = true;
                        break;

                    default:
                        if (Time.realtimeSinceStartup - SessionState.GetFloat(WaitKey, 0f) < 0.5f)
                            break;   // 还在观察窗内，下一帧再断言

                        ok = VerifyAutoPlayerStopped();
                        SessionState.SetBool(OkKey, SessionState.GetBool(OkKey, true) && ok);
                        SessionState.SetInt(PhaseKey, 40);
                        break;
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[CLICK] 阶段 " + phase + " 抛异常：" + e);
                SessionState.SetBool(OkKey, false);
                done = true;
            }

            if (!done) return;

            SessionState.SetBool(ArmedKey, false);
            EditorApplication.update -= Tick;

            bool all = SessionState.GetBool(OkKey, false);
            Debug.Log("[CLICK] 结束，结论：" + (all ? "点击链路通过" : "存在问题，见上方红色日志"));
            EditorApplication.Exit(all ? 0 : 1);
        }

        // ═══════════════ 阶段 1：环境 + 射线 + 点击语义 ═══════════════

        static bool VerifyInputChain()
        {
            bool ok = true;

            Debug.Log("[CLICK] ── 阶段 1：环境与点击语义（Time.frameCount = " + Time.frameCount
                + "，屏幕 " + Screen.width + "×" + Screen.height + "）");

            // ── 环境 ──
            EventSystem es = EventSystem.current;
            if (es == null) { Debug.LogError("[CLICK] EventSystem.current 为空，点击不会被派发"); return false; }

            var modules = es.GetComponents<BaseInputModule>();
            var names = new StringBuilder();
            for (int i = 0; i < modules.Length; i++)
                names.Append(modules[i].GetType().Name).Append(modules[i].enabled ? "(启用) " : "(禁用) ");
            Debug.Log("[CLICK] EventSystem.current = " + es.name + "，输入模块：" + (modules.Length == 0 ? "<无>" : names.ToString()));
            if (modules.Length == 0) { ok = false; Debug.LogError("[CLICK] 没有输入模块"); }

            var canvas = UnityEngine.Object.FindObjectOfType<Canvas>();
            if (canvas == null) { Debug.LogError("[CLICK] 找不到 Canvas"); return false; }

            var raycaster = canvas.GetComponent<GraphicRaycaster>();
            Debug.Log("[CLICK] Canvas " + canvas.renderMode + "，scaleFactor " + canvas.scaleFactor.ToString("F3")
                + "，GraphicRaycaster " + (raycaster == null ? "<缺失>" : "存在")
                + "，盘面屏幕边长约 " + (BoardTheme.BoardExtent * canvas.scaleFactor).ToString("F0") + " px");
            if (raycaster == null) { ok = false; Debug.LogError("[CLICK] Canvas 上没有 GraphicRaycaster"); }

            var listener = UnityEngine.Object.FindObjectOfType<AudioListener>();
            var source = UnityEngine.Object.FindObjectOfType<AudioSource>();
            Debug.Log("[CLICK] 音效：AudioListener " + (listener == null ? "<缺失>" : listener.gameObject.name)
                + "，AudioSource " + (source == null ? "<缺失>" : source.gameObject.name));
            if (listener == null) { ok = false; Debug.LogError("[CLICK] 场景里没有 AudioListener，音效不会出声"); }
            if (source == null) { ok = false; Debug.LogError("[CLICK] 没有 AudioSource，音效不会出声"); }

            Transform boardRoot = canvas.transform.Find("BoardRoot");
            if (boardRoot == null) { Debug.LogError("[CLICK] 找不到 BoardRoot"); return false; }
            int blockCount = CountBlocks(boardRoot);
            if (blockCount != 64) { ok = false; Debug.LogError("[CLICK] 方块数 " + blockCount + "，期望 64"); }

            var controller = UnityEngine.Object.FindObjectOfType<BoardController>();
            if (controller == null) { Debug.LogError("[CLICK] 找不到 BoardController"); return false; }

            // 前几个阶段验的是「点一下的结果」，动画会把时序拉长，先关掉
            controller.SetInstantPlayback(true);
            Debug.Log("[CLICK] 已关掉动画（瞬时播放）；阶段 9 会再打开验一次真动画");

            // ── 几何：64 格中心是否都投影到屏幕内 ──
            int outOfScreen = 0;
            for (int row = 0; row < BoardModel.Rows; row++)
                for (int col = 0; col < BoardModel.Cols; col++)
                {
                    Vector2 p = ScreenPosOf(boardRoot, row, col);
                    if (p.x < 0f || p.y < 0f || p.x > Screen.width || p.y > Screen.height) outOfScreen++;
                }
            Debug.Log("[CLICK] 64 格中心落在屏幕外的：" + outOfScreen + " 个");
            if (outOfScreen > 0)
                Debug.LogWarning("[CLICK] 有格子投到屏幕外。GraphicRaycaster 对 Overlay 画布会先做屏幕范围裁剪，"
                    + "这些格子在本分辨率下点不到（1920×1080 下应为 0）。");

            // ── 射线命中的是不是期望的那一格（9 点采样，含四角）──
            int[] sample = { 0, 3, 7 };
            int rayOk = 0, rayTried = 0;
            for (int i = 0; i < sample.Length; i++)
                for (int j = 0; j < sample.Length; j++)
                {
                    int row = sample[i], col = sample[j];
                    rayTried++;

                    Vector2 p = ScreenPosOf(boardRoot, row, col);
                    var hits = RaycastAt(p);
                    if (hits.Count == 0)
                    {
                        Debug.LogError("[CLICK] 点 " + row + "," + col + " 射线未命中任何 UI（屏幕点 " + p.ToString("F1") + "）");
                        continue;
                    }

                    GameObject top = hits[0].gameObject;
                    GameObject handler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(top);
                    bool good = handler != null && handler.name == "Block_" + row + "_" + col;
                    if (good) rayOk++;

                    Debug.Log("[CLICK] 点 " + row + "," + col + " → 最上层 " + PathOf(top.transform)
                        + "（处理器 " + (handler == null ? "<无>" : handler.name) + "）" + (good ? " ✔" : " ✘"));
                }
            Debug.Log("[CLICK] 射线命中 " + rayOk + "/" + rayTried + " 正确");
            if (rayOk != rayTried) ok = false;

            // ── 单击选中 / 再点取消 ──
            string diag;
            if (!ClickBlock(boardRoot, 0, 0, out diag)) { ok = false; Debug.LogError("[CLICK] " + diag); }
            Debug.Log("[CLICK] 点 (0,0)：" + diag
                + " → Ring α = " + RingAlpha(boardRoot, 0, 0).ToString("F2")
                + "，缩放 " + BlockScaleX(boardRoot, 0, 0).ToString("F3"));
            if (RingAlpha(boardRoot, 0, 0) < 0.99f) { ok = false; Debug.LogError("[CLICK] 选中后没有出现描边"); }
            if (Mathf.Abs(BlockScaleX(boardRoot, 0, 0) - 1.04f) > 0.002f) { ok = false; Debug.LogError("[CLICK] 选中后没有放大"); }

            ClickBlock(boardRoot, 0, 0, out diag);
            Debug.Log("[CLICK] 再点 (0,0)：" + diag + " → Ring α = " + RingAlpha(boardRoot, 0, 0).ToString("F2")
                + "，缩放 " + BlockScaleX(boardRoot, 0, 0).ToString("F3"));
            if (RingAlpha(boardRoot, 0, 0) > 0.01f) { ok = false; Debug.LogError("[CLICK] 再点同一格没有取消选中"); }
            if (Mathf.Abs(BlockScaleX(boardRoot, 0, 0) - 1f) > 0.002f) { ok = false; Debug.LogError("[CLICK] 取消选中后没有恢复原尺寸"); }

            // ── 点不相邻的格子：只改选，不交换 ──
            int movesBefore = controller.Score.MovesUsed;
            ClickBlock(boardRoot, 0, 0, out diag);
            ClickBlock(boardRoot, 7, 7, out diag);
            Debug.Log("[CLICK] 点 (0,0) 再点 (7,7)：Ring(0,0) α = " + RingAlpha(boardRoot, 0, 0).ToString("F2")
                + "，Ring(7,7) α = " + RingAlpha(boardRoot, 7, 7).ToString("F2")
                + "，手数 " + controller.Score.MovesUsed + "（应不变）");
            if (RingAlpha(boardRoot, 0, 0) > 0.01f) { ok = false; Debug.LogError("[CLICK] 改选后旧格描边没清掉"); }
            if (RingAlpha(boardRoot, 7, 7) < 0.99f) { ok = false; Debug.LogError("[CLICK] 改选后新格没有描边"); }
            if (controller.Score.MovesUsed != movesBefore) { ok = false; Debug.LogError("[CLICK] 点不相邻格子却计了步"); }

            // ── 相邻但换不出组合：弹回、不计步 ──
            var detector = new MatchDetector(controller.Graph);
            var legal = new List<int>(64);
            int legalCount = AutoMoveFinder.Collect(controller.Board, detector, legal);
            Debug.Log("[CLICK] 当前盘面合法步 " + legalCount + " 个");

            int badA = -1, badB = -1;
            for (int flat = 0; flat < BoardModel.CellCount && badA < 0; flat++)
            {
                int r = BoardModel.RowOf(flat), c = BoardModel.ColOf(flat);
                if (c + 1 < BoardModel.Cols && !IsLegalMove(legal, flat, flat + 1)) { badA = flat; badB = flat + 1; }
                else if (r + 1 < BoardModel.Rows && !IsLegalMove(legal, flat, flat + BoardModel.Cols)) { badA = flat; badB = flat + BoardModel.Cols; }
            }

            if (badA >= 0)
            {
                int before = controller.Score.MovesUsed;
                ClickBlockFlat(boardRoot, badA, out diag);
                ClickBlockFlat(boardRoot, badB, out diag);
                Debug.Log("[CLICK] 相邻但无效的交换 " + FlatName(badA) + " ⇄ " + FlatName(badB)
                    + "：手数 " + before + " → " + controller.Score.MovesUsed + "（应不变）");
                if (controller.Score.MovesUsed != before) { ok = false; Debug.LogError("[CLICK] 无效交换却计了步"); }
                if (RingAlpha(boardRoot, BoardModel.RowOf(badA), BoardModel.ColOf(badA)) > 0.01f)
                { ok = false; Debug.LogError("[CLICK] 无效交换后描边没清掉"); }
            }
            else
            {
                Debug.LogWarning("[CLICK] 盘面上每一对相邻格都能消（罕见），跳过无效交换用例");
            }

            // ── 相邻且有效：交换、计步、加分、HUD 跟着变 ──
            if (legalCount > 0)
            {
                int key = legal[legal.Count / 2];
                int a = key / BoardModel.CellCount, b = key % BoardModel.CellCount;

                int beforeMoves = controller.Score.MovesUsed;
                int beforeScore = controller.Score.Score;
                string hudBefore = ReadText(canvas.transform, "HudRoot/Score");

                ClickBlockFlat(boardRoot, a, out diag);
                ClickBlockFlat(boardRoot, b, out diag);

                string hudAfter = ReadText(canvas.transform, "HudRoot/Score");
                Debug.Log("[CLICK] 有效交换 " + FlatName(a) + " ⇄ " + FlatName(b)
                    + "：手数 " + beforeMoves + " → " + controller.Score.MovesUsed
                    + "，分数 " + beforeScore + " → " + controller.Score.Score
                    + "，HUD '" + hudBefore + "' → '" + hudAfter + "'");

                if (controller.Score.MovesUsed != beforeMoves + 1) { ok = false; Debug.LogError("[CLICK] 有效交换没有计步"); }
                if (controller.Score.Score <= beforeScore) { ok = false; Debug.LogError("[CLICK] 有效交换没有加分"); }
                if (hudAfter == hudBefore) { ok = false; Debug.LogError("[CLICK] HUD 分数没有跟着刷新"); }
                if (RingAlpha(boardRoot, BoardModel.RowOf(a), BoardModel.ColOf(a)) > 0.01f)
                { ok = false; Debug.LogError("[CLICK] 交换后选中描边没清掉"); }
            }

            // ── 族谱：消除后默认收起，玩家点按钮才展开；展开时盘面让位，再点收起复原 ──
            Transform treeRoot = canvas.transform.Find("TreeRoot");
            Transform treeToggle = canvas.transform.Find("HudRoot/TreeToggle");
            if (treeRoot == null || treeToggle == null)
            {
                ok = false;
                Debug.LogError("[CLICK] 找不到族谱面板或入口按钮");
            }
            else
            {
                bool autoOpened = treeRoot.gameObject.activeSelf;
                float idleScale = boardRoot.localScale.x;

                // 直系三代那一手：族谱闭着时先记下这一手，再点开验证血缘链被点亮
                var treePanel = treeToggle.GetComponent<FamilyTreePanel>();
                string r1Names = "无";
                int r1Hot = -1;
                if (treePanel != null)
                {
                    FamilyGraph g = treePanel.Graph;
                    for (int t = 0; t < g.TripleCount; t++)
                    {
                        var trip = g.Triples[t];
                        RuleKind rk;
                        if (!g.TryClassify(trip[0], trip[1], trip[2], out rk) || rk != RuleKind.R1) continue;
                        treePanel.ShowMatch(trip[0], trip[1], trip[2]);
                        r1Names = g.Title(trip[0]) + "/" + g.Title(trip[1]) + "/" + g.Title(trip[2]);
                        break;
                    }
                }

                string diag1, diag2;
                bool hitOpen = ClickRect(treeToggle, out diag1);
                bool opened = treeRoot.gameObject.activeSelf;
                float openScale = boardRoot.localScale.x;
                float openY = ((RectTransform)boardRoot).anchoredPosition.y;
                if (treePanel != null) r1Hot = treePanel.HotLineCount;

                bool hitClose = ClickRect(treeToggle, out diag2);
                bool closed = !treeRoot.gameObject.activeSelf;
                float restored = boardRoot.localScale.x;

                Debug.Log("[CLICK] 族谱：消除后默认收起 " + (!autoOpened)
                    + "（盘面缩放 " + idleScale.ToString("F2") + "）；点「族谱」→ 展开 " + opened
                    + "，盘面缩放 " + openScale.ToString("F2") + " / 盘面 y " + openY.ToString("F0")
                    + "；这一手 " + r1Names + " 点亮 " + r1Hot + " 段"
                    + "；再点「族谱」→ 收起 " + closed + "，盘面缩放复原 " + restored.ToString("F2"));

                if (autoOpened) { ok = false; Debug.LogError("[CLICK] 消除后族谱仍会自动展开"); }
                if (r1Hot >= 0 && r1Hot < 6)
                { ok = false; Debug.LogError("[CLICK] 直系三代没有点亮连线：" + r1Hot + " 段"); }
                if (!hitOpen || !opened) { ok = false; Debug.LogError("[CLICK] 点「族谱」没有展开（投递 " + hitOpen + "）"); }
                if (openScale > 0.95f || openScale < 0.4f) { ok = false; Debug.LogError("[CLICK] 族谱展开后盘面缩放异常：" + openScale.ToString("F2")); }
                if (!hitClose || !closed) { ok = false; Debug.LogError("[CLICK] 再点「族谱」没有收起（投递 " + hitClose + "）"); }
                if (Mathf.Abs(restored - 1f) > 0.01f) { ok = false; Debug.LogError("[CLICK] 族谱收起后盘面没有复原"); }
            }

            Debug.Log("[CLICK] 阶段 1 " + (ok ? "通过" : "未通过"));
            return ok;
        }

        /// <summary>
        /// 新手指引第一步：点按钮展开。
        /// 只断言「展开了」，不在这里验遮挡 —— 面板刚 SetActive 的当帧深度还没算出来。
        /// </summary>
        static bool OpenRulesPanel()
        {
            var canvas = UnityEngine.Object.FindObjectOfType<Canvas>();
            if (canvas == null) return false;

            Transform toggle = canvas.transform.Find("HudRoot/RulesToggle");
            if (toggle == null) toggle = canvas.transform.Find("RulesToggle");
            Transform root = canvas.transform.Find("RulesRoot");
            if (toggle == null || root == null)
            {
                Debug.LogError("[CLICK] 找不到新手指引按钮或面板（HudRoot/RulesToggle、RulesRoot）");
                return false;
            }

            bool closedBefore = !root.gameObject.activeSelf;

            // 先把族谱打开，用来验证「面板展开时族谱自动收起」
            Transform treeToggle = canvas.transform.Find("HudRoot/TreeToggle");
            Transform treeRoot = canvas.transform.Find("TreeRoot");
            bool treeOpened = false;
            if (treeToggle != null && treeRoot != null)
            {
                string treeDiag;
                ClickRect(treeToggle, out treeDiag);
                treeOpened = treeRoot.gameObject.activeSelf;
            }

            string diag;
            bool hit = ClickRect(toggle, out diag);
            bool opened = root.gameObject.activeSelf;
            bool treeClosed = treeRoot == null || !treeRoot.gameObject.activeSelf;

            Debug.Log("[CLICK] ── 阶段 5：新手指引 —— 初始收起 " + closedBefore
                + "；先打开族谱 " + treeOpened
                + "，点按钮 → 面板展开 " + opened + "（投递 " + hit + "），族谱已收起 " + treeClosed);

            bool ok = closedBefore && opened && hit && treeOpened && treeClosed;
            if (!closedBefore) Debug.LogError("[CLICK] 新手指引初始就是展开状态");
            if (!opened) Debug.LogError("[CLICK] 点按钮没有展开新手指引");
            if (!treeOpened) Debug.LogError("[CLICK] 打开面板前没能先展开族谱（前置条件不成立）");
            if (!treeClosed) Debug.LogError("[CLICK] 打开玩法说明时族谱没有自动收起");

            Debug.Log("[CLICK] 阶段 5 " + (ok ? "通过" : "未通过"));
            return ok;
        }

        /// <summary>
        /// 新手指引第二步（下一帧）：展开时挡住棋盘 → 点「知道了」收起。
        /// </summary>
        static bool VerifyRulesBlockingAndClose()
        {
            bool ok = true;

            var canvas = UnityEngine.Object.FindObjectOfType<Canvas>();
            if (canvas == null) return false;

            Transform root = canvas.transform.Find("RulesRoot");
            Transform close = canvas.transform.Find("RulesRoot/Card/CloseButton");
            Transform boardRoot = canvas.transform.Find("BoardRoot");
            if (root == null || close == null || boardRoot == null) return false;

            // 展开时点棋盘：应当被遮罩吃掉
            GameObject hit;
            string handler = ClickAt(ScreenPosOf(boardRoot, 0, 0), out hit);
            bool blocked = handler != "Block_0_0";

            string diag;
            bool closeHit = ClickRect(close, out diag);
            bool closed = !root.gameObject.activeSelf;

            Debug.Log("[CLICK] ── 阶段 6：展开时点 (0,0) 命中 " + handler + "（应不是方块）"
                + "；点「知道了」→ 收起 " + closed + "（投递 " + closeHit + "）");

            if (!blocked) { ok = false; Debug.LogError("[CLICK] 新手指引展开时仍能点到棋盘"); }
            if (!closeHit) { ok = false; Debug.LogError("[CLICK] 「知道了」没接住点击"); }
            if (!closed) { ok = false; Debug.LogError("[CLICK] 点「知道了」没有收起"); }

            Debug.Log("[CLICK] 阶段 6 " + (ok ? "通过" : "未通过"));
            return ok;
        }

        // ═══════════════ 阶段 2：打满 60 手（只推进，不做断言）═══════════════

        static void FinishLevel()
        {
            var controller = UnityEngine.Object.FindObjectOfType<BoardController>();
            if (controller == null) { Debug.LogError("[CLICK] 找不到 BoardController"); return; }

            int guard = 0;
            while (!controller.IsFinished && guard++ < 300) controller.TryRandomMove();

            Debug.Log("[CLICK] ── 阶段 2：打满 60 手，分数 " + controller.Score.Score
                + "，星级 " + controller.Score.Stars + "，最大连锁 ×" + controller.MaxChainRound);

            var canvas = UnityEngine.Object.FindObjectOfType<Canvas>();
            Transform resultRoot = canvas == null ? null : canvas.transform.Find("ResultRoot");
            Debug.Log("[CLICK] 结算面板 active = " + (resultRoot != null && resultRoot.gameObject.activeSelf)
                + "（下一帧再验遮挡，因为 Graphic.depth 要等渲染批次刷新）");
        }

        // ═══════════════ 阶段 3：结算面板是否真的挡住棋盘 ═══════════════

        static bool VerifyPanelBlocking()
        {
            bool ok = true;

            var canvas = UnityEngine.Object.FindObjectOfType<Canvas>();
            var controller = UnityEngine.Object.FindObjectOfType<BoardController>();
            if (canvas == null || controller == null) { Debug.LogError("[CLICK] 找不到 Canvas / BoardController"); return false; }

            Transform boardRoot = canvas.transform.Find("BoardRoot");
            Transform resultRoot = canvas.transform.Find("ResultRoot");
            if (boardRoot == null || resultRoot == null) { Debug.LogError("[CLICK] 找不到 BoardRoot / ResultRoot"); return false; }

            Debug.Log("[CLICK] ── 阶段 3：结算面板遮挡（Time.frameCount = " + Time.frameCount + "）");

            if (!resultRoot.gameObject.activeSelf) { ok = false; Debug.LogError("[CLICK] 结算面板没有显示"); }

            int lastIndex = canvas.transform.childCount - 1;
            Debug.Log("[CLICK] Canvas 子物体：" + ChildNames(canvas.transform)
                + "；ResultRoot 序号 " + resultRoot.GetSiblingIndex() + "/" + lastIndex);
            if (resultRoot.GetSiblingIndex() != lastIndex)
            { ok = false; Debug.LogError("[CLICK] 结算面板不是最后一个子物体，绘制层序不在最上"); }

            // 遮罩是否铺满画布（注意：画布 rect 是世界单位 1663×1247，屏幕像素是 640×480，不能混用）
            var dim = resultRoot.Find("Dim") as RectTransform;
            if (dim == null) { ok = false; Debug.LogError("[CLICK] 没有 Dim 遮罩"); }
            else
            {
                var canvasRect = canvas.GetComponent<RectTransform>();
                float w = Screen.width - 1f, h = Screen.height - 1f;
                var corners = new[]
                {
                    new Vector2(0f, 0f), new Vector2(w, 0f),
                    new Vector2(0f, h), new Vector2(w, h)
                };

                int inside = 0;
                for (int i = 0; i < corners.Length; i++)
                    if (RectTransformUtility.RectangleContainsScreenPoint(dim, corners[i], null)) inside++;

                Debug.Log("[CLICK] Dim 画布尺寸 " + dim.rect.size.ToString("F0")
                    + "，Canvas " + canvasRect.rect.size.ToString("F0")
                    + "，四角被遮罩覆盖 " + inside + "/4（屏幕 " + Screen.width + "×" + Screen.height + "）");
                if (inside != 4) { ok = false; Debug.LogError("[CLICK] 遮罩没有铺满屏幕，有角落能点到棋盘"); }
            }

            int dimDepth = DepthOf(canvas.transform, "ResultRoot/Dim");
            int blockDepth = DepthOf(boardRoot, "Block_0_0/Background");
            Debug.Log("[CLICK] 绘制深度：Dim = " + dimDepth + "，Block_0_0/Background = " + blockDepth);

            Vector2 sp00 = ScreenPosOf(boardRoot, 0, 0);
            var hits = RaycastAt(sp00);
            string top = hits.Count > 0 ? PathOf(hits[0].gameObject.transform) : "<无>";
            Debug.Log("[CLICK] 结算后点 (0,0) 的最上层：" + top);

            int beforeMoves = controller.Score.MovesUsed;
            int beforeScore = controller.Score.Score;

            string diag;
            bool hitBlock = ClickBlock(boardRoot, 0, 0, out diag);
            Debug.Log("[CLICK] 结算后点击：" + diag + "，落到方块上 = " + hitBlock);

            // 第二道保险：即使射线被方块接住，控制器也必须拒绝
            if (controller.Score.MovesUsed != beforeMoves || controller.Score.Score != beforeScore)
            { ok = false; Debug.LogError("[CLICK] 结算后仍能下棋（手数/分数变了）"); }

            if (dimDepth >= 0)
            {
                if (!top.Contains("ResultRoot")) { ok = false; Debug.LogError("[CLICK] 遮罩没拿到有效深度，射线仍优先命中棋盘"); }
                if (hitBlock) { ok = false; Debug.LogError("[CLICK] 遮罩在第一层，但射线仍命中方块"); }
            }
            else
            {
                Debug.LogWarning("[CLICK] Dim 深度仍为 -1（没进渲染批次），射线层序无法判定；"
                    + "已改为结构判定：面板为最后一个子物体 + 遮罩铺满画布 + 控制器二次保险生效");
            }

            Debug.Log("[CLICK] 阶段 3 " + (ok ? "通过" : "未通过"));
            return ok;
        }

        // ═══════════════ 阶段 4：点「再来一局」重开 ═══════════════

        static bool VerifyRestart()
        {
            bool ok = true;

            var canvas = UnityEngine.Object.FindObjectOfType<Canvas>();
            var controller = UnityEngine.Object.FindObjectOfType<BoardController>();
            if (canvas == null || controller == null) { Debug.LogError("[CLICK] 找不到 Canvas / BoardController"); return false; }

            Transform boardRoot = canvas.transform.Find("BoardRoot");
            Transform button = canvas.transform.Find("ResultRoot/Panel/RestartButton");
            if (button == null) { Debug.LogError("[CLICK] 找不到「再来一局」按钮"); return false; }

            Debug.Log("[CLICK] ── 阶段 4：点「再来一局」（Time.frameCount = " + Time.frameCount + "）");

            var hits = RaycastAt(ScreenPosOfRect((RectTransform)button));
            string top = hits.Count > 0 ? PathOf(hits[0].gameObject.transform) : "<无>";
            Debug.Log("[CLICK] 按钮处最上层：" + top);

            string diag;
            bool delivered = ClickRect(button, out diag);
            Debug.Log("[CLICK] 点击按钮：" + diag + "，投递 = " + delivered);

            Transform resultRoot = canvas.transform.Find("ResultRoot");
            Debug.Log("[CLICK] 重开结果：面板 active = " + resultRoot.gameObject.activeSelf
                + "，手数 " + controller.Score.MovesUsed
                + "，分数 " + controller.Score.Score
                + "，可走 " + new MatchDetector(controller.Graph).CountMoves(controller.Board) + " 手");

            if (!delivered) { ok = false; Debug.LogError("[CLICK] 按钮没接住点击"); }
            if (resultRoot.gameObject.activeSelf) { ok = false; Debug.LogError("[CLICK] 点「再来一局」后面板没关闭"); }
            if (controller.Score.MovesUsed != 0) { ok = false; Debug.LogError("[CLICK] 点「再来一局」后手数没有归零"); }
            int reopened = CountBlocks(boardRoot);
            if (reopened != 64) { ok = false; Debug.LogError("[CLICK] 重开后方块数不对：" + reopened); }

            Debug.Log("[CLICK] 阶段 4 " + (ok ? "通过" : "未通过"));
            return ok;
        }

        // ═══════════════ 阶段 7/8：暂停与继续 ═══════════════

        /// <summary>点「暂停」：面板出现，且 Time.timeScale 归零（动画/飘字/自动演示一起停）。</summary>
        static bool OpenPausePanel()
        {
            var canvas = UnityEngine.Object.FindObjectOfType<Canvas>();
            if (canvas == null) return false;

            Transform toggle = canvas.transform.Find("HudRoot/PauseToggle");
            if (toggle == null) toggle = canvas.transform.Find("PauseToggle");
            Transform root = canvas.transform.Find("PauseRoot");
            if (toggle == null || root == null)
            {
                Debug.LogError("[CLICK] 找不到暂停按钮或暂停面板");
                return false;
            }

            bool closedBefore = !root.gameObject.activeSelf;
            float scaleBefore = Time.timeScale;

            string diag;
            bool hit = ClickRect(toggle, out diag);
            bool opened = root.gameObject.activeSelf;
            float scaleAfter = Time.timeScale;

            Debug.Log("[CLICK] ── 阶段 7：暂停 —— 初始收起 " + closedBefore + "，timeScale " + scaleBefore
                + "；点按钮 → 展开 " + opened + "，timeScale " + scaleAfter
                + "（" + diag + "）");

            bool ok = closedBefore && opened && hit;
            if (!closedBefore) Debug.LogError("[CLICK] 暂停面板初始就是展开的");
            if (!opened) Debug.LogError("[CLICK] 点「暂停」没有展开面板");
            if (Mathf.Abs(scaleAfter) > 0.001f) { ok = false; Debug.LogError("[CLICK] 暂停后 Time.timeScale 未归零"); }

            Debug.Log("[CLICK] 阶段 7 " + (ok ? "通过" : "未通过"));
            return ok;
        }

        /// <summary>暂停中：点棋盘无反应，点「继续」恢复且 timeScale 回到 1。</summary>
        static bool VerifyPauseAndResume()
        {
            bool ok = true;

            var canvas = UnityEngine.Object.FindObjectOfType<Canvas>();
            if (canvas == null) return false;

            Transform root = canvas.transform.Find("PauseRoot");
            Transform resume = canvas.transform.Find("PauseRoot/Card/ResumeButton");
            Transform boardRoot = canvas.transform.Find("BoardRoot");
            if (root == null || resume == null || boardRoot == null) return false;

            var controller = UnityEngine.Object.FindObjectOfType<Game.Gameplay.BoardController>();
            int movesBefore = controller != null ? controller.Score.MovesUsed : -1;

            // 暂停时点棋盘：遮罩应当吃掉
            GameObject hit;
            string handler = ClickAt(ScreenPosOf(boardRoot, 0, 0), out hit);
            bool blocked = handler != "Block_0_0";
            int movesAfter = controller != null ? controller.Score.MovesUsed : -1;

            // 「退出游戏」不真点 —— 它会停掉 Play，自检就断在这儿了。
            // 但可以验证它确实在最上层、能接住点击。
            Transform quit = canvas.transform.Find("PauseRoot/Card/QuitButton");
            bool quitReachable = false;
            if (quit != null)
            {
                var quitHits = RaycastAt(ScreenPosOfRect((RectTransform)quit));
                if (quitHits.Count > 0)
                {
                    GameObject quitHandler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(quitHits[0].gameObject);
                    quitReachable = quitHandler != null && quitHandler.name == "QuitButton";
                }
            }

            string diag;
            bool resumeHit = ClickRect(resume, out diag);
            bool closed = !root.gameObject.activeSelf;
            float scaleAfter = Time.timeScale;

            Debug.Log("[CLICK] ── 阶段 8：暂停中点 (0,0) 命中 " + handler + "（应不是方块）"
                + "，手数 " + movesBefore + " → " + movesAfter
                + "；点「继续」→ 收起 " + closed + "，timeScale " + scaleAfter
                + "；「退出游戏」可点 = " + quitReachable);

            if (!blocked) { ok = false; Debug.LogError("[CLICK] 暂停时仍能点到棋盘"); }
            if (movesBefore != movesAfter) { ok = false; Debug.LogError("[CLICK] 暂停时手数变了"); }
            if (!resumeHit) { ok = false; Debug.LogError("[CLICK] 「继续」没接住点击"); }
            if (!closed) { ok = false; Debug.LogError("[CLICK] 点「继续」没有收起暂停面板"); }
            if (Mathf.Abs(scaleAfter - 1f) > 0.001f) { ok = false; Debug.LogError("[CLICK] 继续后 Time.timeScale 没有恢复"); }
            if (!quitReachable) { ok = false; Debug.LogError("[CLICK] 「退出游戏」按钮点不到（被遮挡或没接点击）"); }

            Debug.Log("[CLICK] 阶段 8 " + (ok ? "通过" : "未通过"));
            return ok;
        }

        // ═══════════════ 阶段 9/10：真动画与输入闸门 ═══════════════

        /// <summary>打开动画，下一手真播一遍，同时验证「动画期间点不动」。</summary>
        static bool StartAnimationCheck()
        {
            bool ok = true;

            var controller = UnityEngine.Object.FindObjectOfType<BoardController>();
            var canvas = UnityEngine.Object.FindObjectOfType<Canvas>();
            if (controller == null || canvas == null) { Debug.LogError("[CLICK] 找不到控制器/画布"); return false; }

            Transform boardRoot = canvas.transform.Find("BoardRoot");
            if (boardRoot == null) return false;

            controller.SetInstantPlayback(false);
            SessionState.SetInt(MinVisKey, 999);   // 动画期间的可见方块数，用来抓「整盘闪一帧」

            var detector = new MatchDetector(controller.Graph);
            var legal = new List<int>(64);
            if (AutoMoveFinder.Collect(controller.Board, detector, legal) == 0)
            {
                Debug.LogWarning("[CLICK] 重开后的盘面没有合法步，跳过动画用例");
                return true;
            }

            int key = legal[0];
            int a = key / BoardModel.CellCount, b = key % BoardModel.CellCount;

            int before = controller.Score.MovesUsed;
            string diag;
            ClickBlockFlat(boardRoot, a, out diag);
            ClickBlockFlat(boardRoot, b, out diag);

            bool animating = controller.IsAnimating;
            int afterFirst = controller.Score.MovesUsed;

            // 动画还在播时再点一对相邻格：应当被完全忽略
            ClickBlockFlat(boardRoot, 0, out diag);
            ClickBlockFlat(boardRoot, 1, out diag);
            int afterSecond = controller.Score.MovesUsed;

            Debug.Log("[CLICK] ── 阶段 9：动画与输入闸门 —— 交换 " + FlatName(a) + " ⇄ " + FlatName(b)
                + "，IsAnimating = " + animating
                + "，手数 " + before + " → " + afterFirst + " → " + afterSecond + "（动画中点击应被忽略）");

            if (!animating) { ok = false; Debug.LogError("[CLICK] 交换后没有进入动画状态"); }
            if (afterFirst != before + 1) { ok = false; Debug.LogError("[CLICK] 这次交换没计步"); }
            if (afterSecond != afterFirst) { ok = false; Debug.LogError("[CLICK] 动画期间仍能下棋"); }

            Debug.Log("[CLICK] 阶段 9 " + (ok ? "通过" : "未通过"));
            return ok;
        }

        /// <summary>等动画放完，并确认盘面已稳定、输入已恢复。</summary>
        static bool WaitAnimationIdle()
        {
            var controller = UnityEngine.Object.FindObjectOfType<BoardController>();
            if (controller == null) return false;

            float waited = Time.realtimeSinceStartup - SessionState.GetFloat(WaitKey, 0f);

            if (controller.IsAnimating)
            {
                var canvas = UnityEngine.Object.FindObjectOfType<Canvas>();
                Transform boardRoot = canvas != null ? canvas.transform.Find("BoardRoot") : null;
                if (boardRoot != null)
                {
                    int visible = CountVisibleBlocks(boardRoot);
                    int min = SessionState.GetInt(MinVisKey, 999);
                    if (visible < min) SessionState.SetInt(MinVisKey, visible);
                }

                if (waited < 8f) return true;   // 还在放，下一帧再查

                Debug.LogError("[CLICK] 动画 " + waited.ToString("F1") + " 秒仍未结束");
                SessionState.SetInt(PhaseKey, 8);
                return false;
            }

            var detector = new MatchDetector(controller.Graph);
            bool hasMatch = detector.HasMatch(controller.Board);

            Debug.Log("[CLICK] ── 阶段 10：动画结束，耗时 " + waited.ToString("F2") + " 秒，残留三连 = "
                + hasMatch + "，手数 " + controller.Score.MovesUsed);

            int minVisible = SessionState.GetInt(MinVisKey, 999);
            Debug.Log("[CLICK] 动画期间可见方块最少 " + minVisible + " / 64（低于 40 说明整盘闪过）");

            bool ok = !hasMatch;
            if (hasMatch) Debug.LogError("[CLICK] 动画结束后盘面还有现成三连");
            if (minVisible < 40) { ok = false; Debug.LogError("[CLICK] 动画期间盘面几乎全隐，出现频闪（最少 " + minVisible + " 块可见）"); }

            Debug.Log("[CLICK] 阶段 10 " + (ok ? "通过" : "未通过"));
            SessionState.SetInt(PhaseKey, 8);
            return ok;
        }

        // ═══════════════ 阶段 11/12：自动试玩必须在结算时停下 ═══════════════

        static bool StartAutoPlayer()
        {
            var player = UnityEngine.Object.FindObjectOfType<AutoPlayer>();
            if (player == null) { Debug.LogError("[CLICK] 找不到 AutoPlayer（编辑器/开发包才会挂）"); return false; }

            // 自动试玩不再付一遍动画时间
            var controller = UnityEngine.Object.FindObjectOfType<BoardController>();
            if (controller != null) controller.SetInstantPlayback(true);

            if (!player.Running) player.Toggle();

            Debug.Log("[CLICK] ── 阶段 11：启动自动试玩，Running = " + player.Running);
            return player.Running;
        }

        static bool IsLevelFinished()
        {
            var controller = UnityEngine.Object.FindObjectOfType<BoardController>();
            return controller != null && controller.IsFinished;
        }

        /// <summary>
        /// 自动试玩打到 60 手后必须停住、且不能自己重开。
        /// 这条以前是缺陷：AutoPlayer 的 AutoRestart 会直接调 StartNewGame()，
        /// 绕过结算面板的遮挡与 IsFinished 拦截，等于演示脚本走了一条玩家走不到的路。
        /// </summary>
        static bool VerifyAutoPlayerStopped()
        {
            bool ok = true;

            var player = UnityEngine.Object.FindObjectOfType<AutoPlayer>();
            var controller = UnityEngine.Object.FindObjectOfType<BoardController>();
            var canvas = UnityEngine.Object.FindObjectOfType<Canvas>();
            if (player == null || controller == null || canvas == null)
            { Debug.LogError("[CLICK] 找不到 AutoPlayer / BoardController / Canvas"); return false; }

            Transform resultRoot = canvas.transform.Find("ResultRoot");
            bool panelShown = resultRoot != null && resultRoot.gameObject.activeSelf;

            Debug.Log("[CLICK] ── 阶段 12：自动试玩收尾检查（Time.frameCount = " + Time.frameCount + "）");
            Debug.Log("[CLICK] Running = " + player.Running
                + "，手数 " + controller.Score.MovesUsed + "/" + ScoreKeeper.LevelMoves
                + "，分数 " + controller.Score.Score + "，星级 " + controller.Score.Stars
                + "，结算面板 active = " + panelShown);

            if (player.Running)
            { ok = false; Debug.LogError("[CLICK] 结算后自动试玩仍在跑，没有停下"); }

            if (controller.Score.MovesUsed != ScoreKeeper.LevelMoves)
            {
                ok = false;
                Debug.LogError("[CLICK] 手数 " + controller.Score.MovesUsed + " 不是 " + ScoreKeeper.LevelMoves
                    + " —— 很可能被自动重开了");
            }

            if (!panelShown)
            { ok = false; Debug.LogError("[CLICK] 结算面板没留在屏幕上（被自动重开顶掉了？）"); }

            // 结算后暂停必须失效：否则 ESC 会在结算面板「下面」开一个看不见的暂停层，
            // 时间被冻住、画面没变化，看起来像卡死。
            var pause = UnityEngine.Object.FindObjectOfType<PausePanel>();
            if (pause != null)
            {
                bool beforeOpen = pause.IsOpen;
                pause.Toggle();
                bool afterOpen = pause.IsOpen;
                float scale = Time.timeScale;

                Debug.Log("[CLICK] 结算后调暂停：IsOpen " + beforeOpen + " → " + afterOpen
                    + "，timeScale " + scale + "（应保持关闭、时间为 1）");

                if (afterOpen != beforeOpen) { ok = false; Debug.LogError("[CLICK] 结算后仍能打开暂停"); }
                if (Mathf.Abs(scale - 1f) > 0.001f) { ok = false; Debug.LogError("[CLICK] 结算后暂停冻住了时间"); }
            }

            Debug.Log("[CLICK] 阶段 12 " + (ok ? "通过" : "未通过"));
            return ok;
        }

        // ═══════════════ 阶段 13~15：提示 / 重新开始时停掉自动演示 ═══════════════

        /// <summary>结算面板点「再来一局」开新局 → 点「提示」应亮两个格子 → 启动自动演示。</summary>
        static bool RestartHintAndAutoStart()
        {
            bool ok = true;

            var canvas = UnityEngine.Object.FindObjectOfType<Canvas>();
            var controller = UnityEngine.Object.FindObjectOfType<Game.Gameplay.BoardController>();
            var player = UnityEngine.Object.FindObjectOfType<AutoPlayer>();
            if (canvas == null || controller == null || player == null) return false;

            Transform boardRoot = canvas.transform.Find("BoardRoot");
            Transform resultRoot = canvas.transform.Find("ResultRoot");
            Transform restartButton = canvas.transform.Find("ResultRoot/Panel/RestartButton");
            Transform hintButton = canvas.transform.Find("HudRoot/HintButton");
            if (boardRoot == null || resultRoot == null || restartButton == null || hintButton == null)
            {
                Debug.LogError("[CLICK] 找不到 再来一局 / 提示 按钮");
                return false;
            }

            string diag;
            bool restarted = ClickRect(restartButton, out diag);
            bool panelClosed = !resultRoot.gameObject.activeSelf;
            int movesAfterRestart = controller.Score.MovesUsed;

            // 提示：不应消耗手数，且正好点亮两个格子
            bool hintHit = ClickRect(hintButton, out diag);
            int lit = CountLitRings(boardRoot);
            int movesAfterHint = controller.Score.MovesUsed;

            if (!player.Running) player.Toggle();

            Debug.Log("[CLICK] ── 阶段 13：再来一局（投递 " + restarted + "，面板关 " + panelClosed
                + "，手数 " + movesAfterRestart + "）；点「提示」（投递 " + hintHit
                + "）→ 亮起 " + lit + " 格，手数仍 " + movesAfterHint
                + "；启动自动演示 Running = " + player.Running);

            if (!restarted || !panelClosed) { ok = false; Debug.LogError("[CLICK] 「再来一局」没生效"); }
            if (movesAfterRestart != 0) { ok = false; Debug.LogError("[CLICK] 重开后手数不是 0"); }
            if (!hintHit) { ok = false; Debug.LogError("[CLICK] 「提示」按钮没接住点击"); }
            if (lit != 2) { ok = false; Debug.LogError("[CLICK] 提示点亮的格子数不是 2，而是 " + lit); }
            if (movesAfterHint != 0) { ok = false; Debug.LogError("[CLICK] 提示消耗了手数"); }
            if (!player.Running) { ok = false; Debug.LogError("[CLICK] 自动演示没启动"); }

            Debug.Log("[CLICK] 阶段 13 " + (ok ? "通过" : "未通过"));
            return ok;
        }

        /// <summary>自动演示进行中点「暂停」。</summary>
        static bool OpenPauseWhileAuto()
        {
            var canvas = UnityEngine.Object.FindObjectOfType<Canvas>();
            if (canvas == null) return false;

            Transform toggle = canvas.transform.Find("HudRoot/PauseToggle");
            Transform root = canvas.transform.Find("PauseRoot");
            if (toggle == null || root == null) return false;

            string diag;
            bool hit = ClickRect(toggle, out diag);
            bool opened = root.gameObject.activeSelf;

            Debug.Log("[CLICK] ── 阶段 14：自动演示中打开暂停（投递 " + hit + "，展开 " + opened + "）");

            bool ok = hit && opened;
            if (!ok) Debug.LogError("[CLICK] 自动演示中「暂停」打不开");
            Debug.Log("[CLICK] 阶段 14 " + (ok ? "通过" : "未通过"));
            return ok;
        }

        /// <summary>暂停里点「重新开始」：自动演示必须停、面板收起、时间恢复、手数归零。</summary>
        static bool RestartWhileAuto()
        {
            bool ok = true;

            var canvas = UnityEngine.Object.FindObjectOfType<Canvas>();
            var controller = UnityEngine.Object.FindObjectOfType<Game.Gameplay.BoardController>();
            var player = UnityEngine.Object.FindObjectOfType<AutoPlayer>();
            if (canvas == null || controller == null || player == null) return false;

            Transform root = canvas.transform.Find("PauseRoot");
            Transform restart = canvas.transform.Find("PauseRoot/Card/RestartButton");
            if (root == null || restart == null) return false;

            bool wasRunning = player.Running;

            string diag;
            bool hit = ClickRect(restart, out diag);

            bool closed = !root.gameObject.activeSelf;
            bool stopped = !player.Running;
            int moves = controller.Score.MovesUsed;
            float scale = Time.timeScale;

            Debug.Log("[CLICK] ── 阶段 15：自动演示中「重新开始」—— 点击前 Running " + wasRunning
                + "，点后 Running " + player.Running + "，面板收起 " + closed
                + "，手数 " + moves + "，timeScale " + scale);

            if (!wasRunning) { ok = false; Debug.LogError("[CLICK] 点击前自动演示没在跑，用例前提不成立"); }
            if (!hit) { ok = false; Debug.LogError("[CLICK] 「重新开始」没接住点击"); }
            if (!closed) { ok = false; Debug.LogError("[CLICK] 重开后暂停面板没收起"); }
            if (!stopped) { ok = false; Debug.LogError("[CLICK] 重新开始后自动演示仍在跑"); }
            if (moves != 0) { ok = false; Debug.LogError("[CLICK] 重开后手数不是 0"); }
            if (Mathf.Abs(scale - 1f) > 0.001f) { ok = false; Debug.LogError("[CLICK] 重开后 timeScale 没恢复"); }

            Debug.Log("[CLICK] 阶段 15 " + (ok ? "通过" : "未通过"));
            return ok;
        }

        /// <summary>数有几格的描边是亮着的（提示脉冲 / 选中都会点亮描边）。</summary>
        static int CountLitRings(Transform boardRoot)
        {
            int count = 0;
            for (int row = 0; row < BoardModel.Rows; row++)
                for (int col = 0; col < BoardModel.Cols; col++)
                    if (RingAlpha(boardRoot, row, col) > 0.3f) count++;
            return count;
        }

        // ═══════════════ 工具 ═══════════════

        static Vector2 ScreenPosOf(Transform boardRoot, int row, int col)
        {
            var rect = (RectTransform)boardRoot.Find("Block_" + row + "_" + col);
            return ScreenPosOfRect(rect);
        }

        static Vector2 ScreenPosOfRect(RectTransform rect)
        {
            return RectTransformUtility.WorldToScreenPoint(null, rect.position);
        }

        static List<RaycastResult> RaycastAt(Vector2 screenPoint)
        {
            var list = new List<RaycastResult>();

            var ped = new PointerEventData(EventSystem.current);
            ped.position = screenPoint;
            ped.button = PointerEventData.InputButton.Left;

            EventSystem.current.RaycastAll(ped, list);
            return list;
        }

        /// <summary>投递一次 pointerClick 到屏幕点上最上层的处理器，返回处理器名。</summary>
        static string ClickAt(Vector2 screenPoint, out GameObject hit)
        {
            hit = null;

            var hits = RaycastAt(screenPoint);
            if (hits.Count == 0) return "<射线未命中>";

            hit = hits[0].gameObject;
            GameObject handler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(hit);
            if (handler == null) return "<无处理器>";

            var ped = new PointerEventData(EventSystem.current);
            ped.position = screenPoint;
            ped.button = PointerEventData.InputButton.Left;
            ped.pointerPress = handler;
            ped.rawPointerPress = hit;
            ExecuteEvents.Execute(handler, ped, ExecuteEvents.pointerClickHandler);

            return handler.name;
        }

        static bool ClickRect(Transform target, out string diag)
        {
            var rect = (RectTransform)target;
            GameObject hit;
            string handler = ClickAt(ScreenPosOfRect(rect), out hit);

            bool ok = hit != null && handler == target.name;
            diag = "最上层 " + (hit == null ? "<无>" : PathOf(hit.transform)) + " → " + handler;
            return ok;
        }

        /// <summary>按屏幕坐标投递一次 pointerClick，并判断是否落在期望的方块上。</summary>
        static bool ClickBlock(Transform boardRoot, int row, int col, out string diag)
        {
            GameObject hit;
            string handler = ClickAt(ScreenPosOf(boardRoot, row, col), out hit);

            bool right = handler == "Block_" + row + "_" + col;
            diag = "最上层 " + (hit == null ? "<无>" : PathOf(hit.transform)) + " → " + handler
                + (right ? "" : "（不是这一格）");
            return right;
        }

        static bool ClickBlockFlat(Transform boardRoot, int flat, out string diag)
        {
            return ClickBlock(boardRoot, BoardModel.RowOf(flat), BoardModel.ColOf(flat), out diag);
        }

        static string FlatName(int flat)
        {
            return "(" + BoardModel.RowOf(flat) + "," + BoardModel.ColOf(flat) + ")";
        }

        static bool IsLegalMove(List<int> moves, int a, int b)
        {
            int lo = a < b ? a : b, hi = a < b ? b : a;
            return moves.Contains(lo * BoardModel.CellCount + hi);
        }

        static float RingAlpha(Transform boardRoot, int row, int col)
        {
            Transform block = boardRoot.Find("Block_" + row + "_" + col);
            if (block == null) return -1f;
            Transform ring = block.Find("Ring");
            if (ring == null) return -2f;
            var img = ring.GetComponent<Image>();
            return img == null ? -3f : img.color.a;
        }

        static float BlockScaleX(Transform boardRoot, int row, int col)
        {
            Transform block = boardRoot.Find("Block_" + row + "_" + col);
            return block == null ? -1f : block.localScale.x;
        }

        /// <summary>Graphic 在 Canvas 里的绘制层序。越大越靠上；-1 表示尚未进入渲染批次。</summary>
        static int DepthOf(Transform root, string path)
        {
            Transform t = root.Find(path);
            if (t == null) return -99;
            var g = t.GetComponent<Graphic>();
            return g == null ? -98 : g.depth;
        }

        /// <summary>数「可见的方块」：激活且底色不透明度 > 0.5。用来抓整盘隐身的闪帧。</summary>
        static int CountVisibleBlocks(Transform boardRoot)
        {
            int count = 0;
            for (int row = 0; row < BoardModel.Rows; row++)
            {
                for (int col = 0; col < BoardModel.Cols; col++)
                {
                    Transform block = boardRoot.Find("Block_" + row + "_" + col);
                    if (block == null || !block.gameObject.activeSelf) continue;

                    Transform bg = block.Find("Background");
                    var img = bg != null ? bg.GetComponent<Image>() : null;
                    if (img != null && img.color.a > 0.5f) count++;
                }
            }
            return count;
        }

        /// <summary>只数方块。BoardRoot 下面还有飘字层，不能直接拿 childCount。</summary>
        static int CountBlocks(Transform boardRoot)
        {
            int count = 0;
            for (int i = 0; i < boardRoot.childCount; i++)
                if (boardRoot.GetChild(i).name.StartsWith("Block_")) count++;
            return count;
        }

        static string ChildNames(Transform parent)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < parent.childCount; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(parent.GetChild(i).name);
            }
            return sb.ToString();
        }

        static string ReadText(Transform root, string path)
        {
            Transform t = root.Find(path);
            if (t == null) return "<找不到 " + path + ">";
            var tmp = t.GetComponent<TMPro.TextMeshProUGUI>();
            return tmp == null ? "<不是文本>" : tmp.text;
        }

        static string PathOf(Transform t)
        {
            var sb = new StringBuilder(t.name);
            Transform p = t.parent;
            int guard = 0;
            while (p != null && guard++ < 8)
            {
                sb.Insert(0, p.name + "/");
                p = p.parent;
            }
            return sb.ToString();
        }
    }
}
