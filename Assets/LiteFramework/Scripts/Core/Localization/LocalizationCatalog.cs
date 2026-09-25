using System;
using System.Collections.Generic;

namespace LiteFramework
{
    /// <summary>
    /// 本地化目录（《UI框架总设计》§9"目标文本链为 `#text.xlsx → 生成 key/语言数据 → LText 服务 →
    /// LTextLabel/页面模型`"）。
    ///
    /// **单一来源**：一张 key → (locale → 文本) 的表，按 locale 索引。不可变构造后只读——
    /// 语言切换只换查询的 locale，不重建目录（§9"语言变更刷新本地化组件…不重跑 OnShow"）。
    ///
    /// 纯数据 + 纯查询，零 Unity 依赖，L1 全覆盖。
    /// </summary>
    public sealed class LocalizationCatalog
    {
        /// <summary>源语言（缺 key 时的回退目标，§9"发布期先回退源语言，再显示可诊断占位"）。</summary>
        public const string SourceLocale = "zh-CN";

        private readonly Dictionary<string, Dictionary<string, string>> _byKey;

        /// <summary>已登记的 locale（诊断/校验用）。</summary>
        private readonly HashSet<string> _locales = new HashSet<string>(StringComparer.Ordinal);

        public LocalizationCatalog()
        {
            _byKey = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        }

        /// <summary>已登记 key 数。</summary>
        public int KeyCount => _byKey.Count;

        /// <summary>已出现的 locale 数。</summary>
        public int LocaleCount => _locales.Count;

        /// <summary>登记一条（同 key 同 locale 覆盖；空 key/locale 显性拒绝——静默吞会让缺 key 更难查）。</summary>
        public LocalizationCatalog Add(string key, string locale, string text)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("key 不能为空", nameof(key));
            if (string.IsNullOrEmpty(locale)) throw new ArgumentException("locale 不能为空", nameof(locale));

            if (!_byKey.TryGetValue(key, out Dictionary<string, string> byLocale))
            {
                byLocale = new Dictionary<string, string>(StringComparer.Ordinal);
                _byKey.Add(key, byLocale);
            }
            byLocale[locale] = text;
            _locales.Add(locale);
            return this;
        }

        /// <summary>该 key 是否存在（任意 locale）。</summary>
        public bool HasKey(string key) => !string.IsNullOrEmpty(key) && _byKey.ContainsKey(key);

        /// <summary>该 key 在指定 locale（或源语言回退）下是否有可用文本。</summary>
        public bool HasText(string key, string locale)
            => TryGet(key, locale, out _, out _);

        /// <summary>
        /// 查询（**带回退信息**）。
        /// <paramref name="usedFallback"/> = 目标 locale 无此 key，用了源语言；
        /// <paramref name="missing"/> = 目标 locale 与源语言都没有（调用方据此走缺 key 策略）。
        /// </summary>
        public bool TryGet(string key, string locale, out string text, out bool usedFallback)
        {
            text = null;
            usedFallback = false;
            if (string.IsNullOrEmpty(key)) return false;
            if (!_byKey.TryGetValue(key, out Dictionary<string, string> byLocale)) return false;

            if (!string.IsNullOrEmpty(locale) && byLocale.TryGetValue(locale, out string hit))
            {
                text = hit;
                return true;
            }

            if (byLocale.TryGetValue(SourceLocale, out string source))
            {
                text = source;
                usedFallback = true;
                return true;
            }
            return false;
        }

        /// <summary>已知 locale 列表（诊断）。</summary>
        public IReadOnlyCollection<string> Locales => _locales;

        /// <summary>某 locale 已覆盖的 key 数（覆盖率诊断——翻译进度可见）。</summary>
        public int CountFor(string locale)
        {
            if (string.IsNullOrEmpty(locale)) return 0;
            int n = 0;
            foreach (Dictionary<string, string> byLocale in _byKey.Values)
                if (byLocale.ContainsKey(locale)) n++;
            return n;
        }
    }

    /// <summary>
    /// key 命名校验（§9"key 命名 `UI.&lt;页面&gt;.&lt;语义&gt;` / `Common.&lt;语义&gt;`"）。
    ///
    /// 校验而非"约定"：命名跑了会让本地化覆盖率统计与批量替换失效，
    /// 且是发布期难以回溯的问题——在候选校验阶段就该拦住。
    /// </summary>
    public static class LTextKey
    {
        /// <summary>允许的顶层前缀。</summary>
        public static readonly string[] AllowedPrefixes = { "UI", "Common" };

        /// <summary>校验 key 命名。返回 null = 合格，否则为拒绝原因。</summary>
        public static string Validate(string key)
        {
            if (string.IsNullOrEmpty(key)) return "key 为空";
            if (key.Length > 160) return $"key 过长（{key.Length}）";

            string[] parts = key.Split('.');
            if (parts.Length < 2) return "key 至少两段（前缀.语义）";

            bool prefixOk = false;
            foreach (string p in AllowedPrefixes)
                if (string.Equals(parts[0], p, StringComparison.Ordinal)) { prefixOk = true; break; }
            if (!prefixOk)
                return $"顶层前缀必须是 {string.Join("/", AllowedPrefixes)} 之一，实为 '{parts[0]}'";

            foreach (string seg in parts)
            {
                if (seg.Length == 0) return "含空段";
                foreach (char c in seg)
                {
                    bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')
                              || (c >= '0' && c <= '9') || c == '_';
                    if (!ok) return $"段 '{seg}' 含非法字符 '{c}'（只允许字母数字与下划线）";
                }
            }
            return null;
        }
    }

    /// <summary>复数类别（§9"英文 one/other 显式选取"；首版只做 zh-CN + en 需要的形式）。</summary>
    public enum PluralCategory
    {
        /// <summary>单数（en: n == 1）。</summary>
        One = 0,
        /// <summary>复数（en: 其余）。zh-CN 无复数，一律走 Other。</summary>
        Other = 1,
    }

    /// <summary>
    /// 本地化查询端口（§9"`Get/Format/Raw` 为目标接口，尚未实现"——本类即其实现）。
    ///
    /// 与 <see cref="LocalizationCatalog"/> 的分工：目录是**数据**，本服务是**查询策略**
    /// （当前 locale、缺 key 策略、复数选取、富文本转义）。
    /// </summary>
    public interface ILocalizationService
    {
        /// <summary>当前语言（BCP-47，如 "zh-CN"/"en"）。</summary>
        string Locale { get; }

        /// <summary>语言变更事件（携带新 locale）。LTextLabel 等订阅它刷新，**不重跑 OnShow**（§9）。</summary>
        event Action<string> OnLocaleChanged;

        /// <summary>切换语言；同值 = 无操作（不触发事件）。</summary>
        void SetLocale(string locale);

        /// <summary>取文本（**未转义**原文；缺 key 走缺 key 策略）。</summary>
        string Raw(string key);

        /// <summary>取文本（**安全文本**：玩家/外部插入的参数按 §9 转义）。</summary>
        string Get(string key);

        /// <summary>取文本并填参（§9"模板参数按编号/语义验证"）。</summary>
        string Format(string key, params object[] args);

        /// <summary>带复数的取文本（§9"英文 one/other 显式选取"）。</summary>
        string FormatPlural(string key, long count, params object[] args);

        /// <summary>缺 key 累计次数（诊断——发布期据此判断翻译完整性）。</summary>
        int MissingKeyCount { get; }
    }
}
