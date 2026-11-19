using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Game.Boot;
using Game.Core;
using Game.Gameplay;
using Game.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace Game.EditorTools
{
    /// <summary>
    /// M2 场景搭建：生成中文 TMP 字体资产 + 生成可运行场景并写入构建列表。
    ///
    /// 为什么用编辑器脚本生成场景，而不是手写 .unity 的 YAML：
    /// 场景文件里全是 GUID 与组件引用，手写极易产出「编辑器能打开但引用全丢」的文件。
    ///
    /// 字体来源：Noto Sans SC（OFL 1.1，可商用可再分发），从系统字体目录复制进工程。
    /// 不使用微软雅黑 —— 它是微软版权字体，打包分发会侵权。
    ///
    /// 用法：
    ///   菜单 Tools → 过年啦 → 一键搭建 M2 场景
    ///   或命令行 -executeMethod Game.EditorTools.M2Setup.BuildAllFromCommandLine
    ///
    /// @trace MECH-003
    /// </summary>
    public static class M2Setup
    {
        const string SystemFontPath = @"C:\Windows\Fonts\NotoSansSC-VF.ttf";
        const string FontSourcePath = "Assets/Art/Fonts/NotoSansSC-VF.ttf";
        const string FontAssetPath = "Assets/Art/Fonts/NotoSansSC SDF.asset";
        const string FamilyJsonPath = "Assets/Data/FamilyTree.json";
        const string ScenePath = "Assets/Scenes/Main.unity";

        // ═══════════════════════ 入口 ═══════════════════════

        [MenuItem("Tools/过年啦/一键搭建 M2 场景", false, 10)]
        public static void BuildAll()
        {
            EnsureTmpEssentials();
            TMP_FontAsset font = EnsureFontAsset();
            BuildScene(font);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[M2Setup] 完成：字体资产与场景均已生成。打开 Assets/Scenes/Main.unity 按 Play 即可。");
        }

        /// <summary>
        /// TMP 的 shader、默认字体、TMP Settings 不在包里，必须导入到工程 Assets/TextMesh Pro。
        /// 缺了它们，CreateFontAsset 生成的材质没有 shader，文字会完全不可见（或紫红）。
        /// 正常在编辑器里首次使用 TMP 会弹窗让人点 Import，这里用代码直接导入，省掉这一步。
        /// </summary>
        static void EnsureTmpEssentials()
        {
            if (AssetDatabase.IsValidFolder("Assets/TextMesh Pro")) return;

            TMP_PackageResourceImporter.ImportResources(true, false, false);
            AssetDatabase.Refresh();
            Debug.Log("[M2Setup] 已导入 TMP 基础资源到 Assets/TextMesh Pro");
        }

        [MenuItem("Tools/过年啦/仅生成中文字体资产", false, 11)]
        public static void BuildFontOnly()
        {
            TMP_FontAsset font = EnsureFontAsset();
            Debug.Log("[M2Setup] 字体资产就绪：" + AssetDatabase.GetAssetPath(font));
        }

        [MenuItem("Tools/过年啦/仅生成场景", false, 12)]
        public static void BuildSceneOnly()
        {
            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            if (font == null) throw new InvalidOperationException("字体资产不存在，请先执行「仅生成中文字体资产」");
            BuildScene(font);
            Debug.Log("[M2Setup] 场景已生成：" + ScenePath);
        }

        /// <summary>
        /// 删掉旧字体资产后重建，连带重建场景（字体 GUID 会变，场景引用必须跟着重连）。
        /// 用在「字符集变了」的场景 —— 例如 M3 新增了 ★☆× 这些符号，
        /// 旧字体资产里没有这些字形，必须重建才会生效。
        /// </summary>
        [MenuItem("Tools/过年啦/重建字体与场景（字符集变更后使用）", false, 13)]
        public static void RebuildAll()
        {
            if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath) != null)
            {
                AssetDatabase.DeleteAsset(FontAssetPath);
                Debug.Log("[M2Setup] 已删除旧字体资产，准备按新字符集重建");
            }
            BuildAll();
        }

        /// <summary>供命令行调用：强制重建字体与场景。成功退出码 0。</summary>
        public static void RebuildAllFromCommandLine()
        {
            try
            {
                RebuildAll();
                ExitIfBatch(0);
            }
            catch (Exception e)
            {
                Debug.LogError("[M2Setup] 重建失败：" + e);
                ExitIfBatch(1);
            }
        }

        /// <summary>供 -executeMethod 调用。成功退出码 0，失败 1，便于命令行判断。</summary>
        public static void BuildAllFromCommandLine()
        {
            try
            {
                BuildAll();
                ExitIfBatch(0);
            }
            catch (Exception e)
            {
                Debug.LogError("[M2Setup] 失败：" + e);
                ExitIfBatch(1);
            }
        }

        // ═══════════════════════ 自检 ═══════════════════════

        /// <summary>
        /// 打开生成的场景，逐个读取 GameBootstrap 的序列化字段，确认引用不是 null。
        /// 不能靠肉眼看 .unity 文件里的 GUID —— 本引擎的 .meta 用 base64 GUID，场景里写 hex，
        /// 两者对不上，只有把场景真正载入并读字段才能确认引用是否连上。
        /// </summary>
        [MenuItem("Tools/过年啦/自检场景与 UI", false, 20)]
        public static void VerifyScene()
        {
            bool ok;
            try { ok = RunVerify(); }
            catch (Exception e) { Debug.LogError("[Verify] 异常：" + e); ok = false; }
            ExitIfBatch(ok ? 0 : 1);
        }

        /// <summary>供 -executeMethod 调用，不主动退出，便于和其他步骤串联。</summary>
        public static void VerifySceneFromCommandLine()
        {
            bool ok;
            try { ok = RunVerify(); }
            catch (Exception e) { Debug.LogError("[Verify] 异常：" + e); ok = false; }
            ExitIfBatch(ok ? 0 : 1);
        }

        static bool RunVerify()
        {
            UnityEngine.SceneManagement.Scene scene =
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            bool ok = true;

            // ── 1. GameBootstrap 的三个引用 ──
            var bootstrap = UnityEngine.Object.FindObjectOfType<GameBootstrap>();
            if (bootstrap == null)
            {
                Debug.LogError("[Verify] 场景里找不到 GameBootstrap");
                return false;
            }
            var so = new SerializedObject(bootstrap);
            ok &= ReportRef(so, "_familyJson");
            ok &= ReportRef(so, "_font");
            ok &= ReportRef(so, "_boardRoot");

            var canvas = UnityEngine.Object.FindObjectOfType<Canvas>();
            if (canvas == null)
            {
                Debug.LogError("[Verify] 场景里找不到 Canvas");
                return false;
            }
            Debug.Log("[Verify] Canvas " + canvas.renderMode + "；场景根对象 " + scene.rootCount + " 个");

            // ── 2. 场景里不应该再有 HUD 副本（HUD 改由运行时创建）──
            Transform staleHud = canvas.transform.Find("HudRoot");
            if (staleHud != null)
            {
                Debug.LogError("[Verify] 场景里残留 HudRoot，会和运行时创建的 HUD 重复");
                ok = false;
            }
            else
            {
                Debug.Log("[Verify] 场景无残留 HudRoot ✓（HUD 由运行时创建）");
            }

            // ── 3. UI 构建自检：真实调用 HudView / ResultPanel ──
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            if (font == null)
            {
                Debug.LogError("[Verify] 找不到字体资产");
                return false;
            }

            ok &= SmokeTestUi(canvas.transform, font);

            Debug.Log("[Verify] 结论：" + (ok ? "全部通过" : "存在问题，见上方红色日志"));
            return ok;
        }

        /// <summary>
        /// 在本场景里临时创建 HUD 与结算面板，检查层级和文案，然后把它们删掉且不保存场景。
        /// 这样能在不进入 Play 模式的情况下验证 UI 构建代码不会空引用、文案格式正确。
        /// </summary>
        static bool SmokeTestUi(Transform canvas, TMP_FontAsset font)
        {
            bool ok = true;

            HudView hud = HudView.Create(canvas, font);
            ResultPanel panel = ResultPanel.Create(canvas, font);

            Transform hudRoot = canvas.Find("HudRoot");
            Transform resultRoot = canvas.Find("ResultRoot");
            if (hudRoot == null) { Debug.LogError("[Smoke] HudRoot 未创建"); ok = false; }
            if (resultRoot == null) { Debug.LogError("[Smoke] ResultRoot 未创建"); ok = false; }

            if (hudRoot != null)
            {
                Debug.Log("[Smoke] HudRoot 子节点 " + hudRoot.childCount + " 个（期望 5：Title/Hint/Score/Stars/Moves）");
                if (hudRoot.childCount != 5) ok = false;

                var keeper = new ScoreKeeper();
                keeper.Award(RuleKind.R1, 1);   // 500
                keeper.Award(RuleKind.R2, 1);   // 300
                keeper.Award(RuleKind.R3, 1);   // 100
                hud.Refresh(keeper);

                string scoreText = ReadText(hudRoot, "Score");
                string movesText = ReadText(hudRoot, "Moves");
                string starsText = ReadText(hudRoot, "Stars");

                Debug.Log("[Smoke] 分数文本 = '" + scoreText + "'（期望 分数 900）");
                Debug.Log("[Smoke] 手数文本 = '" + movesText + "'（期望 剩余 60 手）");
                Debug.Log("[Smoke] 星级文本 = '" + starsText + "'（期望 ☆☆☆，900 分不够 1 星）");

                if (scoreText != "分数 900") { ok = false; Debug.LogError("[Smoke] 分数文案不符"); }
                if (movesText != "剩余 60 手") { ok = false; Debug.LogError("[Smoke] 手数文案不符"); }
                if (starsText != "☆☆☆") { ok = false; Debug.LogError("[Smoke] 星级文案不符"); }
            }

            if (resultRoot != null)
            {
                // 造一个刚好 3 星的分数：R1 五组满连接近 3000，这里直接堆 R1
                var keeper = new ScoreKeeper();
                for (int i = 0; i < 90; i++) keeper.Award(RuleKind.R1, 1);
                panel.Show(keeper, 3);

                bool active = resultRoot.gameObject.activeSelf;
                Debug.Log("[Smoke] 结算面板 active = " + active + "（期望 True）");
                if (!active) ok = false;

                string bigScore = ReadText(resultRoot, "Panel/ScoreValue");
                string stars = ReadText(resultRoot, "Panel/Stars");
                string r1 = ReadText(resultRoot, "Panel/直系三代Value");
                string chain = ReadText(resultRoot, "Panel/最大连锁Value");

                Debug.Log("[Smoke] 结算分数 = '" + bigScore + "'；星级 '" + stars
                    + "'；R1 '" + r1 + "'；连锁 '" + chain + "'");
                if (r1 != "90 组") { ok = false; Debug.LogError("[Smoke] R1 组数文案不符"); }
                if (chain != "×3") { ok = false; Debug.LogError("[Smoke] 连锁文案不符"); }

                panel.Hide();
                if (resultRoot.gameObject.activeSelf) { ok = false; Debug.LogError("[Smoke] Hide 未生效"); }
            }

            // 清理：删掉临时对象，不保存场景
            if (hudRoot != null) UnityEngine.Object.DestroyImmediate(hudRoot.gameObject);
            if (resultRoot != null) UnityEngine.Object.DestroyImmediate(resultRoot.gameObject);

            return ok;
        }

        static string ReadText(Transform root, string path)
        {
            Transform t = root.Find(path);
            if (t == null) return "<路径不存在:" + path + ">";
            var text = t.GetComponent<TMP_Text>();
            return text == null ? "<无 TMP 组件>" : text.text;
        }

        // ═══════════════════════ 完整运行链路自检 ═══════════════════════

        /// <summary>
        /// 在编辑器里跑完整运行链路：直接触发 GameBootstrap.Start()，
        /// 让 64 个方块、HUD、结算面板真正被创建，再自动打满 60 手，检查结算面板弹出。
        ///
        /// 这是「不按 Play 也能验证运行时」的手段：Unity 在编辑模式下不会自动调用 Start，
        /// 用 SendMessage 手动触发即可，跑完不保存场景。
        /// </summary>
        [MenuItem("Tools/过年啦/自检完整运行链路", false, 21)]
        public static void VerifyRuntime()
        {
            bool ok;
            try { ok = RunRuntimeVerify(); }
            catch (Exception e) { Debug.LogError("[RT] 异常：" + e); ok = false; }
            ExitIfBatch(ok ? 0 : 1);
        }

        /// <summary>供 -executeMethod 调用。</summary>
        public static void VerifyRuntimeFromCommandLine()
        {
            bool ok;
            try { ok = RunRuntimeVerify(); }
            catch (Exception e) { Debug.LogError("[RT] 异常：" + e); ok = false; }
            ExitIfBatch(ok ? 0 : 1);
        }

        static bool RunRuntimeVerify()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            bool ok = true;

            var bootstrap = UnityEngine.Object.FindObjectOfType<GameBootstrap>();
            if (bootstrap == null) { Debug.LogError("[RT] 场景里找不到 GameBootstrap"); return false; }

            var canvas = UnityEngine.Object.FindObjectOfType<Canvas>();
            if (canvas == null) { Debug.LogError("[RT] 找不到 Canvas"); return false; }

            GameObject gameRoot = bootstrap.gameObject;

            // ── 1. 触发运行时初始化（等价于进 Play 模式）──
            gameRoot.SendMessage("Start", SendMessageOptions.RequireReceiver);
            Debug.Log("[RT] GameBootstrap.Start 已执行");

            // ── 2. 方块 ──
            Transform boardRoot = canvas.transform.Find("BoardRoot");
            if (boardRoot == null) { Debug.LogError("[RT] 找不到 BoardRoot"); return false; }
            int blockCount = 0;
            for (int i = 0; i < boardRoot.childCount; i++)
                if (boardRoot.GetChild(i).name.StartsWith("Block_")) blockCount++;
            Debug.Log("[RT] BoardRoot 方块 " + blockCount + " 个（期望 64，另含飘字层）");
            if (blockCount != 64) ok = false;

            // ── 3. HUD ──
            Transform hud = canvas.transform.Find("HudRoot");
            if (hud == null) { Debug.LogError("[RT] 没有 HudRoot"); return false; }
            // HUD 里除了三处文本，还挂着「新手指引」按钮，所以不能按子物体个数断言
            bool hasScore = hud.Find("Score") != null;
            bool hasMoves = hud.Find("Moves") != null;
            bool hasStars = hud.Find("Stars") != null;
            Debug.Log("[RT] HudRoot 子节点 " + hud.childCount + " 个，分数/手数/星级三处文本 "
                + (hasScore && hasMoves && hasStars ? "齐全" : "有缺失"));
            if (!hasScore || !hasMoves || !hasStars) ok = false;

            Debug.Log("[RT] 初始 HUD：'" + ReadText(hud, "Score") + "' / '"
                + ReadText(hud, "Moves") + "' / '" + ReadText(hud, "Stars") + "'");

            if (ReadText(hud, "Score") != "分数 0") { ok = false; Debug.LogError("[RT] 初始分数文案不符"); }
            if (ReadText(hud, "Moves") != "剩余 60 手") { ok = false; Debug.LogError("[RT] 初始手数文案不符"); }

            // ── 4. 自动走几手 ──
            var controller = gameRoot.GetComponent<BoardController>();
            if (controller == null) { Debug.LogError("[RT] 找不到 BoardController"); return false; }

            // 关掉动画：本自检是「每走一手立刻断言」，动画会把时序拉长
            controller.SetInstantPlayback(true);

            for (int i = 0; i < 5; i++) controller.TryRandomMove();
            Debug.Log("[RT] 走 5 手后：手数 " + controller.Score.MovesUsed
                + "，分数 " + controller.Score.Score
                + "，HUD '" + ReadText(hud, "Score") + "' / '" + ReadText(hud, "Moves") + "'");

            if (controller.Score.MovesUsed != 5) { ok = false; Debug.LogError("[RT] 手数未按预期增加"); }
            if (controller.Score.Score <= 0) { ok = false; Debug.LogError("[RT] 5 手后分数仍为 0"); }

            // ── 5. 打满 60 手，看结算面板是否弹出 ──
            int guard = 0;
            while (!controller.IsFinished && guard++ < 300) controller.TryRandomMove();

            Transform resultRoot = canvas.transform.Find("ResultRoot");
            if (resultRoot == null) { Debug.LogError("[RT] 没有 ResultRoot"); return false; }

            Debug.Log("[RT] 打满：手数 " + controller.Score.MovesUsed
                + "，分数 " + controller.Score.Score
                + "，星级 " + controller.Score.Stars
                + "，结算面板 active = " + resultRoot.gameObject.activeSelf);

            if (!controller.IsFinished) { ok = false; Debug.LogError("[RT] 60 手没打完"); }
            if (!resultRoot.gameObject.activeSelf) { ok = false; Debug.LogError("[RT] 结算面板没有弹出"); }

            Debug.Log("[RT] 结算内容：主分数 '" + ReadText(resultRoot, "Panel/ScoreValue")
                + "'，星级 '" + ReadText(resultRoot, "Panel/Stars")
                + "'，R1 '" + ReadText(resultRoot, "Panel/直系三代Value")
                + "'，R2 '" + ReadText(resultRoot, "Panel/同代姻亲Value")
                + "'，R3 '" + ReadText(resultRoot, "Panel/同类三消Value")
                + "'，连锁 '" + ReadText(resultRoot, "Panel/最大连锁Value") + "'");

            // ── 6. 结算后不能再操作 ──
            int before = controller.Score.MovesUsed;
            bool movedAfterFinish = controller.TryRandomMove();
            Debug.Log("[RT] 结算后再走一手：返回 " + movedAfterFinish + "，手数 " + controller.Score.MovesUsed + "（应仍为 " + before + "）");
            if (movedAfterFinish || controller.Score.MovesUsed != before)
            {
                ok = false;
                Debug.LogError("[RT] 结算后仍能操作，输入拦截失效");
            }

            // ── 7. 自动演示组件是否挂上（仅编辑器/开发包）──
            var auto = gameRoot.GetComponent<AutoPlayer>();
            Debug.Log("[RT] AutoPlayer " + (auto == null ? "<未挂载>" : "已挂载（F9 开关）"));

            Debug.Log("[RT] 结论：" + (ok ? "完整运行链路通过" : "存在问题，见上方红色日志"));
            return ok;
        }

        static bool ReportRef(SerializedObject so, string propertyName)
        {
            SerializedProperty prop = so.FindProperty(propertyName);
            if (prop == null)
            {
                Debug.LogError("[Verify] 找不到字段 " + propertyName);
                return false;
            }
            UnityEngine.Object value = prop.objectReferenceValue;
            if (value == null)
            {
                Debug.LogError("[Verify] " + propertyName + " = <null>");
                return false;
            }
            Debug.Log("[Verify] " + propertyName + " = " + value.name + "（" + value.GetType().Name + "）");
            return true;
        }

        // ═══════════════════════ 字体 ═══════════════════════

        static TMP_FontAsset EnsureFontAsset()
        {
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            if (existing != null)
            {
                Debug.Log("[M2Setup] 字体资产已存在，跳过创建：" + FontAssetPath);
                return existing;
            }

            EnsureFolder("Assets/Art");
            EnsureFolder("Assets/Art/Fonts");

            if (!File.Exists(FontSourcePath))
            {
                if (!File.Exists(SystemFontPath))
                    throw new FileNotFoundException("系统里找不到中文字体：" + SystemFontPath);

                File.Copy(SystemFontPath, FontSourcePath, false);
                AssetDatabase.ImportAsset(FontSourcePath, ImportAssetOptions.ForceSynchronousImport);
                Debug.Log("[M2Setup] 已复制系统字体进工程：" + FontSourcePath + "（Noto Sans SC，OFL 1.1）");
            }

            var sourceFont = AssetDatabase.LoadAssetAtPath<Font>(FontSourcePath);
            if (sourceFont == null)
                throw new InvalidOperationException("字体文件导入失败：" + FontSourcePath);

            TMP_FontAsset fontAsset = TMP_FontAsset.CreateFontAsset(
                sourceFont, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024,
                AtlasPopulationMode.Dynamic, true);

            if (fontAsset == null)
                throw new InvalidOperationException("TMP_FontAsset.CreateFontAsset 返回 null");

            fontAsset.name = "NotoSansSC SDF";

            string charset = CollectCharset();
            string missing;
            bool added = fontAsset.TryAddCharacters(charset, out missing);
            int missingCount = string.IsNullOrEmpty(missing) ? 0 : missing.Length;

            Debug.Log("[M2Setup] 字符集 " + charset.Length + " 个不同字符；预生成 glyph " + added
                + "，缺失 " + missingCount + (missingCount > 0 ? "（" + missing + "）" : ""));

            if (fontAsset.material == null || fontAsset.material.shader == null)
            {
                Debug.LogWarning("[M2Setup] 字体材质缺少 shader，说明 TMP 基础资源未导入。"
                    + "请在编辑器里执行 Window → TextMeshPro → Import TMP Essential Resources 后重跑本脚本。");
            }

            AssetDatabase.CreateAsset(fontAsset, FontAssetPath);

            Texture2D[] atlases = fontAsset.atlasTextures;
            if (atlases != null)
            {
                for (int i = 0; i < atlases.Length; i++)
                {
                    if (atlases[i] != null) AssetDatabase.AddObjectToAsset(atlases[i], fontAsset);
                }
            }
            if (fontAsset.material != null) AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);

            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(FontAssetPath, ImportAssetOptions.ForceSynchronousImport);

            var saved = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            if (saved == null) throw new InvalidOperationException("字体资产保存后无法重新载入：" + FontAssetPath);
            return saved;
        }

        /// <summary>
        /// 收集需要的字符：47 个称谓 + 「家」 + 全部 UI 文案。
        /// 只预生成这些字，atlas 才装得下；运行时若出现新字，Dynamic 模式会自动补。
        /// </summary>
        static string CollectCharset()
        {
            var raw = new StringBuilder();

            var jsonAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(FamilyJsonPath);
            if (jsonAsset != null)
            {
                try
                {
                    List<FamilyPerson> persons = FamilyDatabase.LoadFromJson(jsonAsset.text);
                    for (int i = 0; i < persons.Count; i++) raw.Append(persons[i].Title);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[M2Setup] 解析族谱失败，字符集将不含称谓：" + e.Message);
                }
            }
            else
            {
                Debug.LogWarning("[M2Setup] 找不到 " + FamilyJsonPath + "，字符集将不含称谓");
            }

            raw.Append("家");
            raw.Append("过年啦点一个方块再相邻的交换凑成亲戚关系就能消");
            // 星级与结算页用到的符号：★ ☆ × 与全角冒号、顿号。缺了会显示豆腐块，
            // 所以一并并入字符集，由「缺失 N」这条日志验证。
            raw.Append("分数剩余手数星级结算总分最高连击再来一局提示无法");
            raw.Append("本局直系三代同代姻亲同类三消最大连锁组再来一局");
            raw.Append("★☆×：、");

            var seen = new HashSet<char>();
            var result = new StringBuilder();
            for (int i = 0; i < raw.Length; i++)
            {
                char ch = raw[i];
                if (ch == '\n' || ch == '\r' || ch == ' ' || ch == '\t') continue;
                if (seen.Add(ch)) result.Append(ch);
            }
            return result.ToString();
        }

        // ═══════════════════════ 场景 ═══════════════════════

        static void BuildScene(TMP_FontAsset fontAsset)
        {
            EnsureFolder("Assets/Scenes");

            var familyJson = AssetDatabase.LoadAssetAtPath<TextAsset>(FamilyJsonPath);
            if (familyJson == null) throw new FileNotFoundException("找不到族谱数据：" + FamilyJsonPath);

            UnityEngine.SceneManagement.Scene scene =
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ── 相机：Screen Space - Overlay 的 Canvas 其实不依赖相机，但留着做后续屏幕抖动与粒子 ──
            var cameraGo = new GameObject("Main Camera", typeof(Camera));
            var camera = cameraGo.GetComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5.4f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = BoardTheme.SceneBackground;
            cameraGo.transform.position = new Vector3(0f, 0f, -10f);
            cameraGo.tag = "MainCamera";

            // ── EventSystem：点击靠它分发 ──
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            // ── Canvas：1920×1080 基准，等比缩放 ──
            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            // ── BoardRoot：8×8 方块的容器，位置与尺寸来自 BoardTheme ──
            var boardGo = new GameObject("BoardRoot", typeof(RectTransform));
            boardGo.transform.SetParent(canvasGo.transform, false);
            var boardRect = boardGo.GetComponent<RectTransform>();
            boardRect.anchorMin = new Vector2(0.5f, 0.5f);
            boardRect.anchorMax = new Vector2(0.5f, 0.5f);
            boardRect.pivot = new Vector2(0.5f, 0.5f);
            boardRect.sizeDelta = new Vector2(BoardTheme.BoardExtent, BoardTheme.BoardExtent);
            boardRect.anchoredPosition = BoardTheme.BoardAnchoredPosition;

            // ── HUD 与结算面板不在场景里摆 ──
            // 它们在运行时由 HudView / ResultPanel 创建（GameBootstrap 里调用），
            // 好处是布局参数集中在一处，改一个数就生效，也不会和场景里的副本重复。
            // 场景只保留 BoardRoot 这个「空容器」，由 BoardView 往里填 64 个方块。

            // ── GameRoot：挂 GameBootstrap 并填好三个引用 ──
            var gameRoot = new GameObject("GameRoot");
            var bootstrap = gameRoot.AddComponent<GameBootstrap>();

            var so = new SerializedObject(bootstrap);
            so.FindProperty("_familyJson").objectReferenceValue = familyJson;
            so.FindProperty("_font").objectReferenceValue = fontAsset;
            so.FindProperty("_boardRoot").objectReferenceValue = boardRect;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            // 场景必须进构建列表，否则打包后画面全黑且编译期不报错
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            Debug.Log("[M2Setup] 场景已保存并写入构建列表：" + ScenePath);
        }

        // ═══════════════════════ 工具 ═══════════════════════

        /// <summary>
        /// 只在命令行（-batchmode）下退出编辑器。
        /// 从菜单手动触发时必须正常返回 —— 否则会把用户正在用的编辑器直接关掉。
        /// </summary>
        static void ExitIfBatch(int exitCode)
        {
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(exitCode);
            }
            else
            {
                Debug.Log("[M2Setup] 非命令行模式，不自动退出编辑器（退出码 " + exitCode + "）");
            }
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            string parent = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(parent)) return;
            parent = parent.Replace('\\', '/');

            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
