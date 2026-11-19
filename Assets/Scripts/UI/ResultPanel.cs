using System;
using Game.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// 结算面板：60 手用完后弹出，显示总分、星级、三条规则各消几组、最大连锁。
    ///
    /// 遮罩（Dim）本身可接收点击，因此面板显示期间会挡住棋盘的点击 ——
    /// 这是「输入禁用」的第一道保险；BoardController 里另有一道 IsFinished 判断作为第二道。
    ///
    /// @trace MECH-012, MECH-013, MECH-014
    /// </summary>
    public sealed class ResultPanel : MonoBehaviour
    {
        static readonly string[] StarLabels = { "☆☆☆", "★☆☆", "★★☆", "★★★" };

        /// <summary>玩家点「再来一局」时触发。由 GameBootstrap 订阅。</summary>
        public event Action RestartRequested;

        TextMeshProUGUI _scoreValue;
        TextMeshProUGUI _stars;
        TextMeshProUGUI _r1Value;
        TextMeshProUGUI _r2Value;
        TextMeshProUGUI _r3Value;
        TextMeshProUGUI _chainValue;

        public static ResultPanel Create(Transform canvas, TMP_FontAsset font)
        {
            RectTransform root = UiFactory.CreateRect(
                canvas, "ResultRoot",
                new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);

            var panel = root.gameObject.AddComponent<ResultPanel>();

            // 遮罩：压暗背景并吃掉点击
            UiFactory.CreateImage(root, "Dim", new Color(0f, 0f, 0f, 0.72f),
                new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero, true);

            Image panelImage = UiFactory.CreateImage(root, "Panel",
                new Color32(0x1E, 0x24, 0x2C, 0xFF),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(640f, 560f), true);
            RectTransform panelRect = panelImage.rectTransform;

            UiFactory.CreateText(panelRect, "Title", font, "本局结算", 30f,
                BoardTheme.HudSubText, TextAlignmentOptions.Center,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 228f), new Vector2(560f, 40f));

            panel._scoreValue = UiFactory.CreateText(panelRect, "ScoreValue", font, "0", 54f,
                BoardTheme.HudText, TextAlignmentOptions.Center,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 160f), new Vector2(560f, 70f));

            panel._stars = UiFactory.CreateText(panelRect, "Stars", font, StarLabels[0], 38f,
                BoardTheme.HudText, TextAlignmentOptions.Center,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 94f), new Vector2(560f, 50f));

            panel._r1Value = CreateRow(panelRect, font, 24f, "直系三代");
            panel._r2Value = CreateRow(panelRect, font, -18f, "同代姻亲");
            panel._r3Value = CreateRow(panelRect, font, -60f, "同类三消");
            panel._chainValue = CreateRow(panelRect, font, -102f, "最大连锁");

            Button restart = UiFactory.CreateButton(
                panelRect, "RestartButton", font, "再来一局",
                new Color32(0x2F, 0x6D, 0xB5, 0xFF), Color.white, 24f,
                new Vector2(0f, -200f), new Vector2(240f, 64f));
            restart.onClick.AddListener(() =>
            {
                if (panel.RestartRequested != null) panel.RestartRequested();
            });

            root.gameObject.SetActive(false);
            return panel;
        }

        static TextMeshProUGUI CreateRow(RectTransform panel, TMP_FontAsset font, float y, string label)
        {
            UiFactory.CreateText(panel, label + "Label", font, label, 20f,
                BoardTheme.HudSubText, TextAlignmentOptions.Left,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(-260f, y), new Vector2(320f, 30f));

            return UiFactory.CreateText(panel, label + "Value", font, "0", 20f,
                BoardTheme.HudText, TextAlignmentOptions.Right,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(260f, y), new Vector2(200f, 30f));
        }

        public void Show(ScoreKeeper score, int maxChainRound)
        {
            if (score == null) return;

            _scoreValue.text = score.Score.ToString("N0");

            int stars = score.Stars;
            if (stars < 0) stars = 0;
            if (stars > 3) stars = 3;
            _stars.text = StarLabels[stars];

            _r1Value.text = score.CountOf(RuleKind.R1) + " 组";
            _r2Value.text = score.CountOf(RuleKind.R2) + " 组";
            _r3Value.text = score.CountOf(RuleKind.R3) + " 组";
            _chainValue.text = "×" + maxChainRound;

            gameObject.SetActive(true);
            transform.SetAsLastSibling();
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }
    }
}
