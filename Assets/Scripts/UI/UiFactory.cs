using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// 运行时创建 UI 元素的工具方法。
    ///
    /// 为什么不用预制体：HUD 和结算面板都是纯文字 + 纯色块，没有美术资源；
    /// 用代码创建可以保证布局参数集中在一处（改一个数就生效），也不会因为字段改名而丢引用。
    ///
    /// 所有元素的 raycastTarget 默认关闭，只有需要接收点击的（按钮、遮罩）显式打开 ——
    /// 否则文字会挡住下方方块的点击，表现为「点了没反应」。
    /// </summary>
    public static class UiFactory
    {
        public static RectTransform CreateRect(
            Transform parent, string name,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 anchoredPosition, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;

            if (size.x <= 0f && size.y <= 0f)
            {
                // 全屏拉伸
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }
            else
            {
                rect.sizeDelta = size;
            }
            return rect;
        }

        public static TextMeshProUGUI CreateText(
            Transform parent, string name, TMP_FontAsset font, string content,
            float fontSize, Color color, TextAlignmentOptions alignment,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 anchoredPosition, Vector2 size,
            bool autoSize = false, float autoSizeMin = 12f)
        {
            RectTransform rect = CreateRect(parent, name, anchorMin, anchorMax, pivot, anchoredPosition, size);

            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.text = content;
            text.color = color;
            text.alignment = alignment;
            text.enableWordWrapping = false;
            text.raycastTarget = false;

            if (autoSize)
            {
                text.enableAutoSizing = true;
                text.fontSizeMin = autoSizeMin;
                text.fontSizeMax = fontSize;
            }
            else
            {
                text.fontSize = fontSize;
            }
            return text;
        }

        public static Image CreateImage(
            Transform parent, string name, Color color,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 anchoredPosition, Vector2 size, bool raycastTarget)
        {
            RectTransform rect = CreateRect(parent, name, anchorMin, anchorMax, pivot, anchoredPosition, size);

            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycastTarget;
            return image;
        }

        /// <summary>
        /// 创建按钮。targetGraphic 用底图，颜色由 Button 的 colorBlock 乘上去，
        /// 所以底图要给白色，靠 colorBlock 得到常态/悬停/按下的差别。
        /// </summary>
        public static Button CreateButton(
            Transform parent, string name, TMP_FontAsset font, string label,
            Color background, Color labelColor, float fontSize,
            Vector2 anchoredPosition, Vector2 size)
        {
            RectTransform rect = CreateRect(
                parent, name, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), anchoredPosition, size);

            var image = rect.gameObject.AddComponent<Image>();
            image.color = background;
            image.raycastTarget = true;

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;

            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.96f, 0.96f, 0.96f, 1f);
            colors.pressedColor = new Color(0.78f, 0.78f, 0.78f, 1f);
            colors.selectedColor = Color.white;
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            CreateText(rect, "Label", font, label, fontSize, labelColor,
                TextAlignmentOptions.Center,
                new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);

            return button;
        }
    }
}
