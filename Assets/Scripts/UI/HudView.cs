using System;
using Game.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// HUD：分数 / 星级 / 剩余手数 / 操作提示。全部运行时创建，不需要在场景里摆。
    ///
    /// 布局（1920×1080，Canvas 中心为原点，顶部 110 px 归 HUD）：
    ///   左上：分数（含「分数」前缀）
    ///   左上下方：三星显示
    ///   右上：剩余手数
    ///   顶部居中：标题 + 一行操作提示
    ///
    /// 显示器星级用 ★/☆ 文字，不用图片：省两个美术资源，且缩放不失真。
    /// 前提是字体里有 ★☆ 字形 —— 字体生成阶段会把它们并入字符集并校验缺失数。
    ///
    /// @trace MECH-012, MECH-014
    /// </summary>
    public sealed class HudView : MonoBehaviour
    {
        static readonly string[] StarLabels = { "☆☆☆", "★☆☆", "★★☆", "★★★" };

        /// <summary>玩家点了「提示」。由 GameBootstrap 接到控制器上。</summary>
        public event Action HintRequested;

        TextMeshProUGUI _score;
        TextMeshProUGUI _stars;
        TextMeshProUGUI _moves;

        public static HudView Create(Transform canvas, TMP_FontAsset font)
        {
            RectTransform root = UiFactory.CreateRect(
                canvas, "HudRoot",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                Vector2.zero, new Vector2(0f, 110f));

            var hud = root.gameObject.AddComponent<HudView>();

            UiFactory.CreateText(root, "Title", font, "过年啦！", 34f,
                BoardTheme.HudText, TextAlignmentOptions.Center,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -8f), new Vector2(700f, 52f));

            UiFactory.CreateText(root, "Hint", font,
                "点一个方块，再点相邻的方块交换；凑成亲戚关系就能消", 16f,
                BoardTheme.HudSubText, TextAlignmentOptions.Center,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -58f), new Vector2(760f, 26f));

            hud._score = UiFactory.CreateText(root, "Score", font, "分数 0", 34f,
                BoardTheme.HudText, TextAlignmentOptions.Left,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(48f, -12f), new Vector2(360f, 48f));

            hud._stars = UiFactory.CreateText(root, "Stars", font, StarLabels[0], 28f,
                BoardTheme.HudSubText, TextAlignmentOptions.Left,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(50f, -60f), new Vector2(360f, 40f));

            hud._moves = UiFactory.CreateText(root, "Moves", font, "剩余 60 手", 30f,
                BoardTheme.HudText, TextAlignmentOptions.Right,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-48f, -12f), new Vector2(360f, 48f));

            // 提示按钮：贴右侧、与「暂停」并排（暂停在 -48，它在 -172）
            Button hint = UiFactory.CreateButton(
                root, "HintButton", font, "提示",
                new Color32(0x2A, 0x6B, 0x5A, 0xFF), Color.white, 20f,
                Vector2.zero, new Vector2(112f, 50f));

            var hintRect = (RectTransform)hint.transform;
            hintRect.anchorMin = new Vector2(1f, 1f);
            hintRect.anchorMax = new Vector2(1f, 1f);
            hintRect.pivot = new Vector2(1f, 1f);
            hintRect.anchoredPosition = new Vector2(-172f, -106f);

            hint.onClick.AddListener(() =>
            {
                if (hud.HintRequested != null) hud.HintRequested();
            });

            return hud;
        }

        public void Refresh(ScoreKeeper score)
        {
            if (score == null) return;

            _score.text = "分数 " + score.Score.ToString("N0");
            _moves.text = "剩余 " + score.MovesLeft + " 手";

            int stars = score.Stars;
            if (stars < 0) stars = 0;
            if (stars > 3) stars = 3;
            _stars.text = StarLabels[stars];
        }
    }
}
