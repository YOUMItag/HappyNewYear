using System;
using Game.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// 单个方块的视觉：底色（按辈分）+ 称谓 + 家庭归属 + 选中描边 + 命中高亮描边。
    ///
    /// 方块在运行时用代码创建，不使用预制体：
    /// 预制体要靠编辑器脚本生成并维护 .prefab，一旦字段改名就悄悄丢引用；
    /// 方块只有「底图 + 两行字」，代码创建反而更稳，也少一个需要人工点的步骤。
    ///
    /// 层级顺序（同一父节点下按子物体顺序绘制）：
    ///   Block ─┬─ Ring      描边（选中 = 蓝；消中高亮 = 金/银）
    ///          ├─ Background 底色
    ///          ├─ Title     称谓
    ///          └─ Family    家庭归属
    ///
    /// 动画由 BoardView 用一个 Update 循环统一驱动（改 64 个方块的位置/缩放/透明度），
    /// 所以这里只提供原子操作，不各自跑协程 —— 64 个 MonoBehaviour 各自 Update 的开销没必要付。
    ///
    /// @trace MECH-003
    /// </summary>
    public sealed class BlockView : MonoBehaviour, IPointerClickHandler
    {
        RectTransform _rect;
        Image _ring;
        Image _background;
        TextMeshProUGUI _title;
        TextMeshProUGUI _family;

        FamilyGraph _graph;
        Action<int> _onClick;
        int _flat = -1;

        Color _baseBackground;     // 按辈分算出的底色，淡入淡出时以此为基准
        float _alpha = 1f;
        bool _selected;

        public int Flat { get { return _flat; } }

        public static BlockView Create(RectTransform parent, TMP_FontAsset font, FamilyGraph graph, Action<int> onClick)
        {
            var go = new GameObject("Block", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var view = go.AddComponent<BlockView>();
            view._graph = graph;
            view._onClick = onClick;

            view._rect = go.GetComponent<RectTransform>();
            view._rect.anchorMin = new Vector2(0.5f, 0.5f);
            view._rect.anchorMax = new Vector2(0.5f, 0.5f);
            view._rect.pivot = new Vector2(0.5f, 0.5f);
            view._rect.sizeDelta = new Vector2(BoardTheme.BlockSize, BoardTheme.BlockSize);

            view._ring = CreateImage(go.transform, "Ring",
                new Vector2(BoardTheme.BlockSize + 8f, BoardTheme.BlockSize + 8f),
                new Color(BoardTheme.Selection.r, BoardTheme.Selection.g, BoardTheme.Selection.b, 0f));

            view._background = CreateImage(go.transform, "Background",
                new Vector2(BoardTheme.BlockSize, BoardTheme.BlockSize),
                BoardTheme.BackgroundForGen(1));
            // 底色要能接收点击（Ring 不接收，避免重复触发）
            view._ring.raycastTarget = false;

            view._title = CreateText(go.transform, "Title", font,
                new Vector2(BoardTheme.BlockSize - 12f, 42f), 14f,
                BoardTheme.TitleSizeMin, BoardTheme.TitleSizeMax, BoardTheme.TextPrimary);

            view._family = CreateText(go.transform, "Family", font,
                new Vector2(BoardTheme.BlockSize - 12f, 22f), -22f,
                BoardTheme.SubtitleSize, BoardTheme.SubtitleSize, BoardTheme.TextSecondary);

            view._baseBackground = BoardTheme.BackgroundForGen(1);

            return view;
        }

        static Image CreateImage(Transform parent, string name, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = size;

            var image = go.AddComponent<Image>();
            image.color = color;
            return image;
        }

        static TextMeshProUGUI CreateText(
            Transform parent, string name, TMP_FontAsset font,
            Vector2 size, float y, float sizeMin, float sizeMax, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, y);
            rect.sizeDelta = size;

            var text = go.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.color = color;
            text.alignment = TextAlignmentOptions.Center;
            text.enableAutoSizing = true;
            text.fontSizeMin = sizeMin;
            text.fontSizeMax = sizeMax;
            text.enableWordWrapping = false;
            text.raycastTarget = false;   // 点击统一交给底色，避免子物体抢事件
            text.text = string.Empty;
            return text;
        }

        public void SetFlat(int flat)
        {
            _flat = flat;
        }

        public void SetPosition(int row, int col)
        {
            _rect.anchoredPosition = BoardTheme.PositionOf(row, col);
        }

        /// <summary>绑定到某个人并显示。fontSize 由 TMP 自动收缩，保证 3 个字（如「外甥女」）不溢出。</summary>
        public void Bind(int personIndex)
        {
            if (_graph == null || personIndex < 0 || personIndex >= _graph.Count)
            {
                Clear();
                return;
            }

            FamilyPerson person = _graph.Get(personIndex);
            _title.text = person.Title;
            _family.text = _graph.FamilyTitle(personIndex);
            _baseBackground = BoardTheme.BackgroundForGen(person.Gen);

            _alpha = 1f;
            _rect.localScale = Vector3.one;
            ApplyColors();

            gameObject.SetActive(true);
        }

        /// <summary>空位（消除后、补充前的短暂状态）。</summary>
        public void Clear()
        {
            _title.text = string.Empty;
            _family.text = string.Empty;
            _background.color = new Color(1f, 1f, 1f, 0f);
            _ring.color = new Color(BoardTheme.Selection.r, BoardTheme.Selection.g, BoardTheme.Selection.b, 0f);
            _alpha = 1f;
            _rect.localScale = Vector3.one;
            gameObject.SetActive(false);
        }

        // ───────────── 动画原子操作（由 BoardView 驱动）─────────────

        /// <summary>直接设锚点坐标。下落/交换动画靠它插值。</summary>
        public void SetAnchoredPosition(Vector2 position)
        {
            _rect.anchoredPosition = position;
        }

        public Vector2 AnchoredPosition { get { return _rect.anchoredPosition; } }

        public void SetScale(float scale)
        {
            _rect.localScale = new Vector3(scale, scale, 1f);
        }

        /// <summary>整体透明度（底色 + 两行字）。0 = 完全透明。</summary>
        public void SetAlpha(float alpha)
        {
            _alpha = alpha < 0f ? 0f : (alpha > 1f ? 1f : alpha);
            ApplyColors();
        }

        /// <summary>描边高亮：R1 金边 / R2 银边 / R3 白边；提示脉冲也走这里。传 null 清除。</summary>
        public void SetFlash(Color? color, float alpha = 1f)
        {
            if (color.HasValue)
            {
                Color c = color.Value;
                _ring.color = new Color(c.r, c.g, c.b, Mathf.Clamp01(alpha));
            }
            else if (!_selected)
            {
                _ring.color = new Color(BoardTheme.Selection.r, BoardTheme.Selection.g, BoardTheme.Selection.b, 0f);
            }
        }

        void ApplyColors()
        {
            Color bg = _baseBackground;
            bg.a = _alpha;
            _background.color = bg;

            Color title = BoardTheme.TextPrimary;
            title.a = _alpha;
            _title.color = title;

            Color family = BoardTheme.TextSecondary;
            family.a = _alpha;
            _family.color = family;
        }

        public void SetSelected(bool selected)
        {
            _selected = selected;
            Color c = BoardTheme.Selection;
            _ring.color = new Color(c.r, c.g, c.b, selected ? 1f : 0f);
            _rect.localScale = selected ? new Vector3(1.04f, 1.04f, 1f) : Vector3.one;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (_flat < 0) return;
            if (_onClick != null) _onClick(_flat);
        }
    }
}
