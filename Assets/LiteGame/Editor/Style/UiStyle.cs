using UnityEngine;

namespace LiteGame.EditorTools.UI
{
    /// <summary>
    /// UI 样式 token 单源（**仅编辑器工具语义**——颜色归手作 prefab 的序列化数据，
    /// 本 token 只作为批量刷新/对账的基准值，不进运行时、不挂组件、不覆盖手调结果以外的任何东西）。
    ///
    /// token 值来自《UI控件库Prefab落地规划》灰盒四色系的实际归纳（WidgetPrefabBuilder 39 处颜色的意图聚类）：
    /// Bg/BgDeep/ItemBg（三级底）/ Primary（主色金）/ Accent（功能蓝）/ Warn（警示红）/ Success（确认绿）/
    /// Text/TextDim（正文/弱文字）/ Raycast/Mark（引导件专用）。
    ///
    /// 可选 SO 资产（UiStyleAsset）覆盖默认值——换肤/调基准时改资产重跑刷新即可；
    /// 手工新调的颜色（不在 token 集内）由"对账"报告暴露，不被工具擅改。
    /// </summary>
    public static class UiStyle
    {
        // ---- 三级底 ----
        public static readonly Color Bg = new Color(0.12f, 0.12f, 0.16f, 0.95f);          // 面板/模板底
        public static readonly Color BgDeep = new Color(0.08f, 0.08f, 0.10f, 0.90f);      // 深底（Toast/进度槽）
        public static readonly Color ItemBg = new Color(0.18f, 0.18f, 0.22f, 1f);         // 条目/次级底

        // ---- 语义色 ----
        public static readonly Color Primary = new Color(0.90f, 0.75f, 0.25f, 1f);        // 主色金（选中/星框/主文字）
        public static readonly Color Accent = new Color(0.30f, 0.70f, 0.95f, 1f);         // 功能蓝（填充/确认钮）
        public static readonly Color Warn = new Color(0.90f, 0.25f, 0.20f, 1f);           // 警示红（红点/后血条）
        public static readonly Color Success = new Color(0.40f, 0.85f, 0.40f, 1f);        // 确认绿（勾选/前血条）

        // ---- 文字 ----
        public static readonly Color Text = new Color(0.92f, 0.92f, 0.94f, 1f);           // 正文
        public static readonly Color TextDim = new Color(0.60f, 0.60f, 0.65f, 1f);        // 占位/弱文字/常态灰
        public static readonly Color TextBright = new Color(1f, 1f, 1f, 1f);              // 亮白（计数/倒计时/滑块钮/页签）

        // ---- 引导件专用 ----
        public static readonly Color Raycast = new Color(1f, 1f, 1f, 0.01f);              // 近透明可点击
        public static readonly Color Mark = new Color(1f, 0.85f, 0.30f, 0.25f);           // 引导高亮框

        /// <summary>token 全集（名称 → 基准色）。顺序稳定，供刷新/对账遍历。</summary>
        public static readonly (string Name, Color Value)[] Tokens =
        {
            ("Bg", Bg),
            ("BgDeep", BgDeep),
            ("ItemBg", ItemBg),
            ("Primary", Primary),
            ("Accent", Accent),
            ("Warn", Warn),
            ("Success", Success),
            ("Text", Text),
            ("TextDim", TextDim),
            ("TextBright", TextBright),
            ("Raycast", Raycast),
            ("Mark", Mark),
        };

        /// <summary>颜色与 token 的距离（逐通道差的平方和；alpha 参与比较——灰盒底色 alpha 是语义的一部分）。</summary>
        public static float Distance(Color a, Color b)
        {
            float dr = a.r - b.r, dg = a.g - b.g, db = a.b - b.b, da = a.a - b.a;
            return dr * dr + dg * dg + db * db + da * da;
        }
    }
}
