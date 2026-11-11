#if UNITY_EDITOR
using System;
using System.Text;
using LiteFramework;

namespace LiteGame.Adapters.Dev.Projection.Preview
{
    /// <summary>
    /// 伪本地化装饰器（P2-3 B3）。
    /// </summary>
    public sealed class PseudoLocale : ILocalizationService
    {
        private const double MinimumGrowth = 1.3d;
        private const char GrowthMarker = '·';

        private readonly ILocalizationService _inner;

        public PseudoLocale(ILocalizationService inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public string Locale => _inner.Locale;

        public event Action<string> OnLocaleChanged
        {
            add => _inner.OnLocaleChanged += value;
            remove => _inner.OnLocaleChanged -= value;
        }

        public int MissingKeyCount => _inner.MissingKeyCount;

        public void SetLocale(string locale) => _inner.SetLocale(locale);

        public string Raw(string key) => Transform(_inner.Raw(key));

        public string Get(string key) => Transform(_inner.Get(key));

        public string Format(string key, params object[] args) => Transform(_inner.Format(key, args));

        public string FormatPlural(string key, long count, params object[] args)
            => Transform(_inner.FormatPlural(key, count, args));

        private static string Transform(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            var builder = new StringBuilder(input.Length * 2);
            bool inTag = false;
            foreach (char value in input)
            {
                if (value == '<') inTag = true;
                if (inTag)
                {
                    builder.Append(value);
                    if (value == '>') inTag = false;
                    continue;
                }

                builder.Append(Accent(value));
                if (char.IsLetterOrDigit(value)) builder.Append(GrowthMarker);
            }

            int target = (int)Math.Ceiling(input.Length * MinimumGrowth);
            while (builder.Length < target) builder.Append(GrowthMarker);
            return builder.ToString();
        }

        /// <summary>ASCII 元音加音标；其余字符原样（CJK 由生长标记承担加长）。</summary>
        private static char Accent(char value) => value switch
        {
            'a' => 'á', 'e' => 'é', 'i' => 'í', 'o' => 'ó', 'u' => 'ú', 'y' => 'ý',
            'A' => 'Á', 'E' => 'É', 'I' => 'Í', 'O' => 'Ó', 'U' => 'Ú', 'Y' => 'Ý',
            _ => value,
        };
    }
}
#endif
