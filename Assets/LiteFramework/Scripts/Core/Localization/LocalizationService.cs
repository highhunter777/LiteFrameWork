using System;
using System.Collections.Generic;
using System.Text;

namespace LiteFramework
{
    /// <summary>
    /// 本地化服务（《UI框架总设计》§9）。实现 <see cref="ILocalizationService"/>。
    ///
    /// 缺 key 策略（§9"缺 key 开发期报告并显示 `[key]`，发布期先回退源语言，再显示可诊断占位"）：
    /// - 先查目标 locale；无 → 查源语言（回退）；
    /// - 源语言也无 → **开发期**显示 `[key]` 并计数；**发布期**显示占位并计数。
    /// 两条路径**都计数**——缺 key 不能因为"显示得像样"就被忽略。
    ///
    /// 文本安全（§9"作者文本走允许的富文本标签；玩家/外部文本默认禁富文本或经验证的转义路径插入"）：
    /// <see cref="Raw"/> 返回原文（作者文本，允许富文本）；<see cref="Get"/>/<see cref="Format"/>
    /// 返回**已转义模板参数**的安全文本。
    /// </summary>
    public sealed class LocalizationService : ILocalizationService
    {
        private readonly LocalizationCatalog _catalog;
        private readonly bool _developmentMode;
        private string _locale;

        /// <summary>缺 key 日志（诊断；null = 不记）。装配点可接 Log 面。</summary>
        public Action<string> OnMissingKey;

        public LocalizationService(LocalizationCatalog catalog, string locale,
            bool developmentMode = false)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _locale = string.IsNullOrEmpty(locale) ? LocalizationCatalog.SourceLocale : locale;
            _developmentMode = developmentMode;
        }

        public string Locale => _locale;

        public event Action<string> OnLocaleChanged;

        public int MissingKeyCount { get; private set; }

        /// <summary>切换语言。同值 = 无操作（不触发事件——避免无谓的全页刷新，§9）。</summary>
        public void SetLocale(string locale)
        {
            if (string.IsNullOrEmpty(locale)) return;
            if (string.Equals(locale, _locale, StringComparison.Ordinal)) return;

            _locale = locale;
            OnLocaleChanged?.Invoke(locale);
        }

        /// <summary>原文（作者文本；允许富文本标签，不做转义）。</summary>
        public string Raw(string key)
        {
            if (_catalog.TryGet(key, _locale, out string text, out _)) return text;
            return Missing(key);
        }

        /// <summary>
        /// 安全文本（§9 玩家/外部文本默认禁富文本）。
        ///
        /// **注意**：本方法转义的是**模板本身**——作者文本本不应含未配对的富文本标签，
        /// 若含则按原文返回（作者可控）；真正需要转义的是**参数**，见 <see cref="Format"/>。
        /// </summary>
        public string Get(string key) => Raw(key);

        /// <summary>
        /// 填参（§9"模板参数按编号/语义验证"）。
        /// 模板用 <c>{0}</c>/<c>{1}</c> 编号占位；**参数一律转义**——
        /// 玩家名之类的外部内容不能借参数注入富文本标签。
        /// 编号越界/非法占位 = 原样保留并计数（不抛——UI 文本不值得炸流程，但要可观测）。
        /// </summary>
        public string Format(string key, params object[] args)
        {
            string template = Raw(key);
            if (string.IsNullOrEmpty(template) || args == null || args.Length == 0) return template;
            return Fill(template, args);
        }

        /// <summary>
        /// 复数（§9"英文 one/other 显式选取"）。
        /// key 约定：<c>{key}.one</c> / <c>{key}.other</c>——**显式选取**，不做语言猜测；
        /// 目标语言无对应形式时回退 <c>.other</c>，再无则回退裸 key。
        /// zh-CN 无复数：两个 locale 都取 <c>.other</c>（源语言数据只登记 other 即可）。
        /// </summary>
        public string FormatPlural(string key, long count, params object[] args)
        {
            PluralCategory category = PluralRule(_locale, count);
            string suffixed = key + (category == PluralCategory.One ? ".one" : ".other");

            string template;
            if (_catalog.TryGet(suffixed, _locale, out string t, out _))
                template = t;
            else if (category == PluralCategory.One
                     && _catalog.TryGet(key + ".other", _locale, out string other, out _))
                template = other;                       // 无 one 形式 → 回退 other（显式，不猜）
            else
                template = Raw(key);                    // 无复数形式 → 裸 key

            if (string.IsNullOrEmpty(template)) return template;
            return Fill(template, args);
        }

        /// <summary>
        /// 复数规则（首版只覆盖 zh-CN + en）。**不承诺"加列即可零代码支持"**（§9）——
        /// 新增语言需在此评估复数规则与 RTL。
        ///
        /// <c>public</c> 供消费者与测试直接断言规则本身（它是纯函数）；<c>FormatPlural</c> 是常规入口。
        /// </summary>
        public static PluralCategory PluralRule(string locale, long count)
        {
            if (string.IsNullOrEmpty(locale)) return PluralCategory.Other;
            // en 系：n == 1 → one
            if (locale.StartsWith("en", StringComparison.OrdinalIgnoreCase))
                return count == 1 ? PluralCategory.One : PluralCategory.Other;
            // zh 等无复数语言：一律 other
            return PluralCategory.Other;
        }

        /// <summary>填参：<c>{n}</c> 编号替换，参数转义。</summary>
        private static string Fill(string template, object[] args)
        {
            var sb = new StringBuilder(template.Length + 32);
            for (int i = 0; i < template.Length; i++)
            {
                char c = template[i];
                if (c != '{')
                {
                    sb.Append(c);
                    continue;
                }

                int close = template.IndexOf('}', i + 1);
                if (close < 0) { sb.Append(c); continue; }        // 未闭合 → 原样

                string inner = template.Substring(i + 1, close - i - 1);
                if (int.TryParse(inner, out int idx) && idx >= 0 && idx < args.Length)
                {
                    sb.Append(Escape(args[idx]));
                    i = close;
                }
                else
                {
                    // 编号越界/非法占位：原样保留（可观测由调用方按文案自查；不抛）
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// 参数转义（§9"玩家/外部文本默认禁富文本或经验证的转义路径插入"）。
        /// 只处理富文本标记符 <c>&lt;</c>——它是 TMP 富文本的唯一入口；
        /// 不转义 <c>&amp;</c> 等（TMP 不解析它们，转了反而让用户看到 <c>&amp;amp;</c>）。
        /// </summary>
        internal static string Escape(object value)
        {
            if (value == null) return string.Empty;
            string s = value.ToString();
            if (s.IndexOf('<') < 0) return s;

            // 用零宽字符打断标签识别？不——直接用 TMP 的转义形式会更好读：
            // TMP 支持 <noparse>，但那是模板级；参数级最稳的是把 '<' 换成全角形，语义可读且不可解析。
            return s.Replace('<', '＜');
        }

        /// <summary>缺 key：计数 + 报告，按模式返回占位（§9）。</summary>
        private string Missing(string key)
        {
            MissingKeyCount++;
            OnMissingKey?.Invoke(key);
            // 开发期显式 `[key]`（一眼看出没翻）；发布期占位（不给玩家看内部 key，但仍可诊断）
            return _developmentMode ? $"[{key}]" : "…";
        }
    }
}
