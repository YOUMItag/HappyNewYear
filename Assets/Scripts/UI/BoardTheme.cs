using UnityEngine;

namespace Game.UI
{
    /// <summary>
    /// 棋盘视觉规格：尺寸、配色、字号。
    ///
    /// 尺寸推导（1920×1080 基准）：
    ///   单格 108 + 间隙 8 = 步距 116
    ///   盘面 = 8×108 + 7×8 = 920 px
    ///   垂直：HUD 占 110 px，盘面 920 px，剩余 50 px 上下留白 → 装得下
    /// 早期方案写「单格 180 px」是错的：8 格会得到 1496 px，超屏 416 px。
    ///
    /// 配色沿用族谱编辑器的色系，保持工具与游戏观感一致。
    /// @trace MECH-003
    /// </summary>
    public static class BoardTheme
    {
        public const float BlockSize = 108f;
        public const float CellGap = 8f;
        public const float Pitch = BlockSize + CellGap;          // 116
        public const float BoardExtent = 8f * BlockSize + 7f * CellGap;  // 920

        public const float TitleSizeMax = 32f;
        public const float TitleSizeMin = 15f;
        public const float SubtitleSize = 17f;

        /// <summary>盘面在 Canvas 中的垂直位置：略偏下，给顶部 HUD 让位。</summary>
        public static readonly Vector2 BoardAnchoredPosition = new Vector2(0f, -55f);

        public static readonly Color TextPrimary = new Color32(0xEE, 0xF2, 0xF8, 0xFF);
        public static readonly Color TextSecondary = new Color32(0xA9, 0xB6, 0xC7, 0xFF);
        public static readonly Color Selection = new Color32(0x4A, 0x9E, 0xFF, 0xFF);
        public static readonly Color SceneBackground = new Color32(0x16, 0x1A, 0x20, 0xFF);
        public static readonly Color HudText = new Color32(0xE4, 0xE8, 0xEE, 0xFF);
        public static readonly Color HudSubText = new Color32(0x8A, 0x93, 0xA0, 0xFF);

        static readonly Color GenGrand = new Color32(0x2D, 0x4A, 0x6B, 0xFF);  // 祖辈 ≥3
        static readonly Color GenParent = new Color32(0x2D, 0x5A, 0x4A, 0xFF); // 父辈 2
        static readonly Color GenSelf = new Color32(0x4A, 0x3D, 0x6B, 0xFF);   // 己辈 1
        static readonly Color GenChild = new Color32(0x6B, 0x4A, 0x2D, 0xFF);  // 子辈 ≤-1
        static readonly Color GenOther = new Color32(0x33, 0x3A, 0x44, 0xFF);  // 兜底

        /// <summary>按辈分取底色。同辈同色，玩家扫一眼就能看出「这片是祖辈、那片是子辈」。</summary>
        public static Color BackgroundForGen(int gen)
        {
            if (gen >= 3) return GenGrand;
            if (gen == 2) return GenParent;
            if (gen == 1) return GenSelf;
            if (gen <= -1) return GenChild;
            return GenOther;
        }

        /// <summary>行列 → 局部坐标。第 0 行在最上，第 0 列在最左。</summary>
        public static Vector2 PositionOf(int row, int col)
        {
            float half = (Core.BoardModel.Rows - 1) * 0.5f;
            return new Vector2(
                (col - half) * Pitch,
                (half - row) * Pitch);
        }
    }
}
