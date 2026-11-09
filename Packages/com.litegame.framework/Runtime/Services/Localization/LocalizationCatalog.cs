using System;
using System.Collections.Generic;

namespace LiteFramework
{
    /// <summary>
    /// 本地化目录（目标文本链为 `Luban 表源 #ltext.xlsx → tbltext 生成物 → 装配层投射（LTextCatalog）→ LText 服务 → LTextLabel/页面模型`）。
    /// </summary>
    public sealed class LocalizationCatalog
    {
        /// <summary>源语言（缺 key 时的回退目标——发布期先回退源语言，再显示可诊断占位）。</summary>
        public const string SourceLocale = "zh-CN";

        /// <summary>英语 locale（复数规则覆盖面 zh-CN + en 的另一半；表列 zh/en → BCP-47 的映射单源在
        /// LiteGame 侧 LTextCatalog——Core 不认识表）。</summary>
        public const string EnglishLocale = "en";

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

        /// <summary>
        /// 查询（**带回退信息**）。
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

    }

    /// <summary>
    /// key 命名校验（key 命名 `UI.&lt;页面&gt;.&lt;语义&gt;` / `Common.&lt;语义&gt;`）。
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

    /// <summary>复数类别（英文 one/other 显式选取；只做 zh-CN + en 需要的形式）。</summary>
    public enum PluralCategory
    {
        /// <summary>单数（en: n == 1）。</summary>
        One = 0,
        /// <summary>复数（en: 其余）。zh-CN 无复数，一律走 Other。</summary>
        Other = 1,
    }

}
