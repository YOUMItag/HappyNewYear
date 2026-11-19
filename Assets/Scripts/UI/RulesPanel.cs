using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// 新手指引：一枚「新手指引」按钮（挂在 HUD 分数下方）+ 一块可展开/收起的规则卡片。
    ///
    /// 为什么做成弹层而不是常驻面板：
    /// 棋盘已经占满中间 920×920，常驻说明只能塞在边上，字会小到看不清；
    /// 而新手只需要看一次，看完关掉，不该长期挤占战局界面。
    ///
    /// 展开时有一层全屏遮罩（和结算面板同款做法），
    /// 作用是「挡住棋盘的点击」——否则玩家会隔着说明卡点到后面的方块。
    ///
    /// 层级顺序：本组两个物体（RulesToggle / RulesRoot）都建在 ResultRoot **之前**，
    /// 这样结算面板仍然是最后一个子物体、仍然画在最上层。
    ///
    /// @trace MECH-002, MECH-005
    /// </summary>
    public sealed class RulesPanel : MonoBehaviour
    {
        const float CardWidth = 680f;
        const float CardHeight = 700f;

        static readonly Color CardColor = new Color32(0x1E, 0x24, 0x2C, 0xFF);
        static readonly Color DimColor = new Color(0f, 0f, 0f, 0.68f);
        static readonly Color AccentColor = new Color32(0xE0, 0xB0, 0x55, 0xFF);
        static readonly Color BodyColor = new Color32(0xD5, 0xDD, 0xE8, 0xFF);
        static readonly Color SubColor = new Color32(0x8A, 0x93, 0xA0, 0xFF);

        static readonly string Body =
            "怎么玩（三步）\n" +
            "1   点一个方块选中，再点相邻的方块交换\n" +
            "2   三个亲戚排成一条直线就消除，上方方块掉下来补位\n" +
            "3   掉下来的又凑成一组就是连锁，分数按轮次翻倍\n" +
            "\n" +
            "三条消除规则（优先级从高到低）\n" +
            "<color=#E0B055>● 直系三代</color>  祖辈→父辈→我辈，同一条血亲线\n" +
            "    顺序不限，每消一组 <b>×5</b> 分\n" +
            "<color=#D8DFEA>● 同代姻亲</color>  同一辈分的姻亲三人，每消一组 <b>×3</b> 分\n" +
            "<color=#EEF2F8>● 同类三消</color>  堂亲或表亲的三个不同变体；\n" +
            "    侄亲或甥亲的三个不同变体，每消一组 <b>×1</b> 分\n" +
            "    （三个是同一个变体不算）\n" +
            "\n" +
            "一局 60 手，打完整局按总分给星\n" +
            "★ 22,000    ★★ 29,000    ★★★ 32,500\n" +
            "\n" +
            "快捷键：F9 自动演示（再按暂停）    M 静音";

        RectTransform _root;
        Button _toggle;
        TextMeshProUGUI _toggleLabel;

        public bool IsOpen { get { return _root != null && _root.gameObject.activeSelf; } }

        /// <summary>卡片展开时通知外部（族谱带要跟着收起，否则会压住卡片底部）。</summary>
        public event Action Opened;

        public static RulesPanel Create(Transform canvas, TMP_FontAsset font)
        {
            // ── 入口按钮：挂在 HUD 里、分数与星级的正下方 ──
            // 位置靠左（x=48），而棋盘左边缘在 x=-460，所以按钮与棋盘横向不重叠，不会误触。
            Transform hudRoot = canvas.Find("HudRoot");
            Transform toggleParent = hudRoot != null ? hudRoot : canvas;

            Button toggle = UiFactory.CreateButton(
                toggleParent, "RulesToggle", font, "新手指引",
                new Color32(0x2F, 0x6D, 0xB5, 0xFF), Color.white, 20f,
                Vector2.zero, new Vector2(176f, 50f));

            var toggleRect = (RectTransform)toggle.transform;
            toggleRect.anchorMin = new Vector2(0f, 1f);
            toggleRect.anchorMax = new Vector2(0f, 1f);
            toggleRect.pivot = new Vector2(0f, 1f);
            toggleRect.anchoredPosition = new Vector2(48f, -106f);

            // ── 弹层：全屏（遮罩 + 卡片）──
            RectTransform root = UiFactory.CreateRect(
                canvas, "RulesRoot",
                new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);

            var panel = root.gameObject.AddComponent<RulesPanel>();
            panel._root = root;
            panel._toggle = toggle;
            panel._toggleLabel = toggle.transform.Find("Label").GetComponent<TextMeshProUGUI>();

            // 遮罩：压暗背景并吃掉点击
            UiFactory.CreateImage(root, "Dim", DimColor,
                new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero, true);

            var card = UiFactory.CreateImage(root, "Card", CardColor,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(CardWidth, CardHeight), true);

            RectTransform cardRect = card.rectTransform;

            UiFactory.CreateText(cardRect, "Title", font, "玩法说明", 32f,
                BoardTheme.HudText, TextAlignmentOptions.Center,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -26f), new Vector2(CardWidth - 80f, 44f));

            UiFactory.CreateText(cardRect, "Divider", font, "· · · · · · · · · · · ·", 14f,
                SubColor, TextAlignmentOptions.Center,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -66f), new Vector2(CardWidth - 80f, 20f));

            TextMeshProUGUI body = UiFactory.CreateText(cardRect, "Body", font, Body, 18f,
                BodyColor, TextAlignmentOptions.TopLeft,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -92f), new Vector2(CardWidth - 90f, 510f));
            body.enableWordWrapping = true;   // UiFactory 默认关换行，正文必须打开
            body.lineSpacing = 5f;

            Button close = UiFactory.CreateButton(
                cardRect, "CloseButton", font, "知道了",
                new Color32(0x2F, 0x6D, 0xB5, 0xFF), Color.white, 22f,
                new Vector2(0f, -CardHeight * 0.5f + 52f), new Vector2(220f, 56f));

            toggle.onClick.AddListener(panel.Toggle);
            close.onClick.AddListener(panel.Hide);

            panel.Hide();
            return panel;
        }

        public void Toggle()
        {
            if (IsOpen) Hide();
            else Show();
        }

        public void Show()
        {
            // 不调整层级：本组建在 ResultRoot 之前，天然压在 HUD 之上、结算面板之下。
            // 若在这里 SetAsLastSibling，会跑到结算面板上面去。
            _root.gameObject.SetActive(true);
            RefreshToggleLabel();
            if (Opened != null) Opened();
        }

        public void Hide()
        {
            _root.gameObject.SetActive(false);
            RefreshToggleLabel();
        }

        void RefreshToggleLabel()
        {
            if (_toggleLabel != null) _toggleLabel.text = IsOpen ? "新手指引 ×" : "新手指引";
        }
    }
}
