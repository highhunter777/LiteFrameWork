using System;
using System.Text;
using LiteFrameworkNext;

namespace LiteGameNext.Platform.Localization
{
    /// <summary>本地化服务：文案查询/填参/切语言；缺文案计数与诊断回调。</summary>
    public sealed class LocalizationService
    {
        private readonly TextCatalog _catalog;
        private readonly bool _developmentMode;
        private string _locale;

        public LocalizationService(TextCatalog catalog, string locale = TextCatalog.SourceLocale,
            bool developmentMode = false)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _locale = string.IsNullOrEmpty(locale) ? TextCatalog.SourceLocale : locale;
            _developmentMode = developmentMode;
        }

        /// <summary>当前语言。</summary>
        public string Locale => _locale;

        /// <summary>切语言后触发（用于刷新页面文本）；同值不触发。</summary>
        public event Action<string> LocaleChanged;

        /// <summary>缺文案累计次数（诊断）。</summary>
        public int MissingKeyCount { get; private set; }

        /// <summary>缺文案回调（装配点可接日志）。</summary>
        public Action<string> OnMissingKey;

        /// <summary>切语言；空值忽略，同值不触发事件。</summary>
        public void SetLocale(string locale)
        {
            if (string.IsNullOrEmpty(locale)) return;
            if (string.Equals(locale, _locale, StringComparison.Ordinal)) return;
            _locale = locale;
            LocaleChanged?.Invoke(locale);
        }

        /// <summary>把另一份目录并入（表投影/热更文案用）；同 key 同 locale 覆盖。</summary>
        public int Merge(TextCatalog other)
        {
            if (other == null) return 0;
            int merged = other.CopyInto(_catalog);
            if (merged > 0) LocaleChanged?.Invoke(_locale);
            return merged;
        }

        /// <summary>取原始文案（允许富文本）；缺失返回诊断占位。</summary>
        public string Raw(string key)
        {
            if (_catalog.TryGet(key, _locale, out string text, out bool usedFallback))
            {
                if (usedFallback) WarnMissing(key, "回退源语言");
                return text;
            }
            return Missing(key);
        }

        /// <summary>玩家可见文本（与 <see cref="Raw"/> 同源；保留入口以便将来禁富文本）。</summary>
        public string Get(string key) => Raw(key);

        /// <summary>填参；模板缺参或格式错时返回原文并记日志（不抛给玩家路径）。</summary>
        public string Format(string key, params object[] args)
        {
            string template = Raw(key);
            if (args == null || args.Length == 0) return template;
            try
            {
                return string.Format(template, args);
            }
            catch (FormatException)
            {
                Log.Warning("文案填参失败：" + key);
                return template;
            }
        }

        /// <summary>按数量的单复数选择：优先查 `locale.one`/`locale.other` 变体，缺失回退主键（zh-CN 无复数）。</summary>
        public string FormatPlural(string key, int count, params object[] args)
        {
            string suffix = PluralSuffix(count);
            string template = Raw(key);
            if (!string.IsNullOrEmpty(suffix)
                && _catalog.TryGet(key, _locale + suffix, out string variant, out _))
            {
                template = variant;
            }
            object[] merged = args == null || args.Length == 0 ? new object[] { count } : Prepend(count, args);
            try
            {
                return string.Format(template, merged);
            }
            catch (FormatException)
            {
                Log.Warning("文案填参失败：" + key);
                return template;
            }
        }

        private string PluralSuffix(int count)
        {
            if (!string.Equals(_locale, "en", StringComparison.Ordinal)) return string.Empty;
            return count == 1 ? ".one" : ".other";
        }

        private static object[] Prepend(object first, object[] rest)
        {
            var result = new object[rest.Length + 1];
            result[0] = first;
            Array.Copy(rest, 0, result, 1, rest.Length);
            return result;
        }

        private void WarnMissing(string key, string reason)
        {
            if (!_developmentMode) return;
            Log.Warning("文案" + reason + "：" + key + "（locale=" + _locale + "）");
            OnMissingKey?.Invoke(key);
        }

        private string Missing(string key)
        {
            MissingKeyCount++;
            OnMissingKey?.Invoke(key);
            // 开发期显式可见 [key]，发布期用省略号，避免玩家看到内部 key。
            return _developmentMode ? "[" + key + "]" : "…";
        }
    }
}
