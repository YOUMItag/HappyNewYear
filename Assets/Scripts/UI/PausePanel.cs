using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// 暂停面板：右上「暂停」按钮 + 全屏弹层（继续 / 重新开始 / 退出游戏）。
    ///
    /// 暂停用 Time.timeScale = 0 实现：
    ///   动画、飘字、自动演示的计时全部走 Time.deltaTime，一处归零就全部停住，
    ///   不需要给每个系统单独加「是否暂停」的判断。
    ///   代价是必须保证 Show/Hide 成对，否则会把游戏永久冻住 —— 所以 OnDestroy 里兜了一次底。
    ///
    /// 组件挂在「暂停按钮」上而不是弹层上：
    ///   弹层收起时是 SetActive(false)，挂在它身上 Update 就不跑了，ESC 键会失效。
    ///
    /// 层级：本组建在 ResultRoot 之前，结算面板仍然画在最上层。
    ///
    /// @trace MECH-012
    /// </summary>
    public sealed class PausePanel : MonoBehaviour
    {
        const float CardWidth = 480f;
        const float CardHeight = 420f;

        static readonly Color CardColor = new Color32(0x1E, 0x24, 0x2C, 0xFF);
        static readonly Color DimColor = new Color(0f, 0f, 0f, 0.68f);
        static readonly Color PrimaryColor = new Color32(0x2F, 0x6D, 0xB5, 0xFF);
        static readonly Color DangerColor = new Color32(0x8C, 0x3B, 0x38, 0xFF);
        static readonly Color NeutralColor = new Color32(0x33, 0x3B, 0x46, 0xFF);

        public event Action ContinueRequested;
        public event Action RestartRequested;
        public event Action QuitRequested;

        /// <summary>
        /// 是否允许暂停。结算面板弹出后要置 false ——
        /// 否则按 ESC 会在结算面板「下面」开出一个看不见的暂停层，
        /// 时间被冻住、画面却没变化，玩家会以为游戏卡死。
        /// </summary>
        public bool Interactable = true;

        RectTransform _root;

        public bool IsOpen { get { return _root != null && _root.gameObject.activeSelf; } }

        /// <summary>暂停层显示时通知外部（族谱带要跟着收起，避免两层叠在一起）。</summary>
        public event Action Opened;

        public static PausePanel Create(Transform canvas, TMP_FontAsset font)
        {
            // ── 暂停按钮：挂在 HUD 右侧、剩余手数下方，与左侧「新手指引」对称 ──
            Transform hudRoot = canvas.Find("HudRoot");
            Transform buttonParent = hudRoot != null ? hudRoot : canvas;

            Button toggle = UiFactory.CreateButton(
                buttonParent, "PauseToggle", font, "暂停",
                NeutralColor, Color.white, 20f,
                Vector2.zero, new Vector2(112f, 50f));

            var toggleRect = (RectTransform)toggle.transform;
            toggleRect.anchorMin = new Vector2(1f, 1f);
            toggleRect.anchorMax = new Vector2(1f, 1f);
            toggleRect.pivot = new Vector2(1f, 1f);
            toggleRect.anchoredPosition = new Vector2(-48f, -106f);

            // 组件挂在按钮上：按钮一直存在，ESC 才一直有效
            var panel = toggle.gameObject.AddComponent<PausePanel>();

            // ── 弹层 ──
            RectTransform root = UiFactory.CreateRect(
                canvas, "PauseRoot",
                new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);

            panel._root = root;

            UiFactory.CreateImage(root, "Dim", DimColor,
                new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero, true);

            var card = UiFactory.CreateImage(root, "Card", CardColor,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(CardWidth, CardHeight), true);
            RectTransform cardRect = card.rectTransform;

            UiFactory.CreateText(cardRect, "Title", font, "暂停", 32f,
                BoardTheme.HudText, TextAlignmentOptions.Center,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -28f), new Vector2(CardWidth - 80f, 44f));

            Button resume = UiFactory.CreateButton(cardRect, "ResumeButton", font, "继续",
                PrimaryColor, Color.white, 24f, new Vector2(0f, 40f), new Vector2(256f, 62f));

            Button restart = UiFactory.CreateButton(cardRect, "RestartButton", font, "重新开始",
                NeutralColor, Color.white, 24f, new Vector2(0f, -40f), new Vector2(256f, 62f));

            Button quit = UiFactory.CreateButton(cardRect, "QuitButton", font, "退出游戏",
                DangerColor, Color.white, 24f, new Vector2(0f, -120f), new Vector2(256f, 62f));

            toggle.onClick.AddListener(panel.Toggle);
            resume.onClick.AddListener(() => { if (panel.ContinueRequested != null) panel.ContinueRequested(); });
            restart.onClick.AddListener(() => { if (panel.RestartRequested != null) panel.RestartRequested(); });
            quit.onClick.AddListener(() => { if (panel.QuitRequested != null) panel.QuitRequested(); });

            panel.Hide();
            return panel;
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) Toggle();
        }

        public void Toggle()
        {
            if (!Interactable) return;

            if (IsOpen) Hide();
            else Show();
        }

        public void Show()
        {
            if (!Interactable || IsOpen) return;
            _root.gameObject.SetActive(true);
            Time.timeScale = 0f;
            if (Opened != null) Opened();
        }

        public void Hide()
        {
            if (_root != null) _root.gameObject.SetActive(false);
            Time.timeScale = 1f;
        }

        void OnDestroy()
        {
            // 兜底：面板被销毁时如果还冻着，恢复时间
            if (Time.timeScale == 0f) Time.timeScale = 1f;
        }
    }
}
