using System;
using System.Collections.Generic;

namespace LiteGameNext.Platform.Localization
{
    /// <summary>文案目录：key→locale→text；查询带回退信息（目标 locale → 源语言）。</summary>
    public sealed class TextCatalog
    {
        /// <summary>源语言（缺文案时的回退目标）。</summary>
        public const string SourceLocale = "zh-CN";

        private readonly Dictionary<string, Dictionary<string, string>> _byKey =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

        /// <summary>已登记文案条数。</summary>
        public int KeyCount => _byKey.Count;

        /// <summary>登记一条（同 key 同 locale 覆盖；空 key/locale 显性拒绝）。</summary>
        public TextCatalog Add(string key, string locale, string text)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("key 不能为空", nameof(key));
            if (string.IsNullOrEmpty(locale)) throw new ArgumentException("locale 不能为空", nameof(locale));
            if (!_byKey.TryGetValue(key, out Dictionary<string, string> byLocale))
            {
                byLocale = new Dictionary<string, string>(StringComparer.Ordinal);
                _byKey.Add(key, byLocale);
            }
            byLocale[locale] = text;
            return this;
        }

        /// <summary>该 key 是否存在（任意 locale）。</summary>
        public bool HasKey(string key) => !string.IsNullOrEmpty(key) && _byKey.ContainsKey(key);

        /// <summary>查询；命中目标 locale 返回 false（未回退），命中源语言返回 true。</summary>
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
    }
}
