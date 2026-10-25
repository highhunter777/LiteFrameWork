using UnityEngine;

namespace LiteGame.EditorTools.UI
{
    /// <summary>
    /// UI 颜色读口（《UI 编辑器工具链专项设计》§8.1）：12 个旧名保留为**只读属性**，值按冻结令牌 ID
    /// 从 <see cref="UiTokenCatalog"/> 读取——本类不再持有任何颜色值，改色板只改令牌 JSON。
    ///
    /// 纪律：不做静态缓存（域重载/重新生成后必须读到新值）；工具内部不逐节点走这里，
    /// 而是每次操作装载一次目录并把实例下传（<see cref="UiStyleTool.ReportDriftWith"/> 等）。
    /// 本门面只服务既有调用点（规则/夹具）与少量一次性读取。
    /// </summary>
    public static class UiStyle
    {
        // ---- 三级底 ----
        public static Color Bg => Get(nameof(Bg));                 // 面板/模板底
        public static Color BgDeep => Get(nameof(BgDeep));               // 深底（Toast/进度槽）
        public static Color ItemBg => Get(nameof(ItemBg));               // 条目/次级底

        // ---- 语义色 ----
        public static Color Primary => Get(nameof(Primary));        // 主色金（选中/星框/主文字）
        public static Color Accent => Get(nameof(Accent));          // 功能蓝（填充/确认钮）
        public static Color Warn => Get(nameof(Warn));              // 警示红（红点/后血条）
        public static Color Success => Get(nameof(Success));        // 确认绿（勾选/前血条）

        // ---- 文字 ----
        public static Color Text => Get(nameof(Text));            // 正文
        public static Color TextDim => Get(nameof(TextDim));             // 占位/弱文字/常态灰
        public static Color TextBright => Get(nameof(TextBright));       // 亮白（计数/倒计时/滑块钮/页签）

        // ---- 引导件专用 ----
        public static Color Raycast => Get(nameof(Raycast));        // 近透明可点击
        public static Color Mark => Get(nameof(Mark));              // 引导高亮框

        /// <summary>
        /// 旧 C# 名 → 冻结令牌 ID 的映射单源（《UI 编辑器工具链专项设计》§8.1；ID 见 P2-3a 方案表）。
        /// 只承载"名字"，不承载颜色值。
        /// </summary>
        public static readonly (string Name, string TokenId)[] LegacyTokenIds =
        {
            ("Bg", "color.bg.screen"),
            ("BgDeep", "color.bg.deep"),
            ("ItemBg", "color.bg.item"),
            ("Primary", "color.brand.primary"),
            ("Accent", "color.brand.accent"),
            ("Warn", "color.state.warn"),
            ("Success", "color.state.success"),
            ("Text", "color.text.primary"),
            ("TextDim", "color.text.dim"),
            ("TextBright", "color.text.bright"),
            ("Raycast", "color.effect.raycast"),
            ("Mark", "color.effect.mark"),
        };

        /// <summary>token 全集（名称 → 当前值），顺序稳定，供刷新/对账遍历与夹具断言。</summary>
        public static (string Name, Color Value)[] Tokens
        {
            get
            {
                UiTokenCatalog catalog = UiTokenCatalog.Load();
                var tokens = new (string Name, Color Value)[LegacyTokenIds.Length];
                for (int i = 0; i < LegacyTokenIds.Length; i++)
                {
                    (string name, string tokenId) = LegacyTokenIds[i];
                    if (!catalog.TryColor(tokenId, out Color value))
                        throw new System.InvalidOperationException("主题资产缺少颜色令牌：" + tokenId);
                    tokens[i] = (name, value);
                }
                return tokens;
            }
        }

        /// <summary>按旧名查令牌 ID；未知名返回 null（调用方显式处理，不猜）。</summary>
        public static string TokenId(string legacyName)
        {
            foreach ((string name, string tokenId) in LegacyTokenIds)
                if (string.Equals(name, legacyName, System.StringComparison.Ordinal)) return tokenId;
            return null;
        }

        /// <summary>颜色与 token 的距离（逐通道差的平方和；alpha 参与比较——灰盒底色 alpha 是语义的一部分）。</summary>
        public static float Distance(Color a, Color b)
        {
            float dr = a.r - b.r, dg = a.g - b.g, db = a.b - b.b, da = a.a - b.a;
            return dr * dr + dg * dg + db * db + da * da;
        }

        private static Color Get(string legacyName)
        {
            string tokenId = TokenId(legacyName);
            if (tokenId == null)
                throw new System.InvalidOperationException("未知颜色名：" + legacyName);
            if (!UiTokenCatalog.Load().TryColor(tokenId, out Color value))
                throw new System.InvalidOperationException("主题资产缺少颜色令牌：" + tokenId);
            return value;
        }
    }
}
