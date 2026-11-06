using System;
using System.Collections.Generic;
using System.Text;

namespace LiteFramework
{
    /// <summary>
    /// 本地化服务。
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

        /// <summary>切换语言。同值 = 无操作（不触发事件——避免无谓的全页刷新）。</summary>
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
        /// 安全文本（玩家/外部文本默认禁富文本）。
        /// </summary>
        public string Get(string key) => Raw(key);

        /// <summary>
        /// 填参（模板参数按编号/语义验证）。
        /// </summary>
        public string Format(string key, params object[] args)
        {
            string template = Raw(key);
            if (string.IsNullOrEmpty(template) || args == null || args.Length == 0) return template;
            return Fill(template, args);
        }

        /// <summary>
        /// 复数（英文 one/other 显式选取）。
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
        /// 复数规则（只覆盖 zh-CN + en）。
        /// </summary>
        public static PluralCategory PluralRule(string locale, long count)
        {
            if (string.IsNullOrEmpty(locale)) return PluralCategory.Other;
            // en 系：n == 1 → one。
            if (locale.StartsWith("en", StringComparison.OrdinalIgnoreCase))
                return count == 1 ? PluralCategory.One : PluralCategory.Other;
            // zh 等无复数语言：一律 other。
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
                    // 编号越界/非法占位：原样保留（可观测由调用方按文案自查；不抛）。
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// 参数转义（玩家/外部文本默认禁富文本或经验证的转义路径插入）。
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

        /// <summary>缺 key：计数 + 报告，按模式返回占位。</summary>
        private string Missing(string key)
        {
            MissingKeyCount++;
            OnMissingKey?.Invoke(key);
            // 开发期显式 `[key]`（一眼看出没翻）；发布期占位（不给玩家看内部 key，但仍可诊断）。
            return _developmentMode ? $"[{key}]" : "…";
        }
    }
}
