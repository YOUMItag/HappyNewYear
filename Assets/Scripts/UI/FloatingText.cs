using TMPro;
using UnityEngine;

namespace Game.UI
{
    /// <summary>
    /// 分数飘字层：每组消除在盘面上浮出一行「+500」，让玩家看见「这一手赚了多少」。
    ///
    /// 挂在 BoardRoot 下面且是最后一个子物体，所以坐标与棋盘一致、图层压在方块之上；
    /// 不接收射线（raycastTarget = false），不会挡住点击。
    ///
    /// 每个飘字自己带一个 Update（同时最多也就五六条），不做一个集中管理的大循环，
    /// 因为飘字的生命周期彼此独立，集中管理反而要维护列表与索引。
    /// </summary>
    public sealed class FloatingTextLayer : MonoBehaviour
    {
        TMP_FontAsset _font;

        public static FloatingTextLayer Create(RectTransform parent, TMP_FontAsset font)
        {
            var go = new GameObject("FloatingText", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(BoardTheme.BoardExtent, BoardTheme.BoardExtent);

            var layer = go.AddComponent<FloatingTextLayer>();
            layer._font = font;
            return layer;
        }

        public void Spawn(Vector2 anchoredPosition, string text, Color color, float fontSize)
        {
            var go = new GameObject("Float", typeof(RectTransform));
            go.transform.SetParent(transform, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = new Vector2(140f, 40f);

            var label = go.AddComponent<TextMeshProUGUI>();
            label.font = _font;
            label.text = text;
            label.color = color;
            label.fontSize = fontSize;
            label.alignment = TextAlignmentOptions.Center;
            label.enableWordWrapping = false;
            label.raycastTarget = false;
            label.fontStyle = FontStyles.Bold;

            go.AddComponent<FloatingTextItem>();
        }
    }

    /// <summary>单条飘字：上浮 + 淡出，1 秒后自毁。</summary>
    public sealed class FloatingTextItem : MonoBehaviour
    {
        const float Life = 0.9f;
        const float Rise = 58f;
        const float FadeStart = 0.35f;

        RectTransform _rect;
        TextMeshProUGUI _label;
        Vector2 _origin;
        Color _color;
        float _age;

        void Awake()
        {
            _rect = (RectTransform)transform;
            _label = GetComponent<TextMeshProUGUI>();
            _origin = _rect.anchoredPosition;
            _color = _label != null ? _label.color : Color.white;
        }

        void Update()
        {
            _age += Time.deltaTime;
            float k = _age / Life;

            if (k >= 1f)
            {
                Destroy(gameObject);
                return;
            }

            // 起手略微放大，收尾淡出 —— 只靠位移会显得轻飘飘
            float pop = k < 0.15f ? Mathf.Lerp(1.25f, 1f, k / 0.15f) : 1f;
            _rect.localScale = new Vector3(pop, pop, 1f);
            _rect.anchoredPosition = _origin + new Vector2(0f, Rise * Mathf.Sqrt(k));

            if (_label != null)
            {
                float alpha = k < FadeStart ? 1f : Mathf.Lerp(1f, 0f, (k - FadeStart) / (1f - FadeStart));
                var c = _color;
                c.a = alpha;
                _label.color = c;
            }
        }
    }
}
