#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using System;
using System.Collections.Generic;
using System.Text;

namespace LiteGame
{
    /// <summary>
    /// HUD 过滤词（`@tag` OR + 文本子串 AND）。解析/匹配/裁剪都在这里，
    /// DevHUD 只负责输入态与持久化；空词 = 不过滤。
    /// </summary>
    internal sealed class DevHudFilter
    {
        private const string TagPrefix = "@";

        private readonly List<string> _tags = new List<string>(4);
        private readonly StringBuilder _text = new StringBuilder(64);

        public string Raw { get; private set; } = "";
        public string Needle { get; private set; } = "";
        public IReadOnlyList<string> Tags => _tags;
        public bool Active => Needle.Length > 0 || _tags.Count > 0;

        public void Set(string raw)
        {
            Raw = raw ?? "";
            Parse();
        }

        public void Append(string typed)
        {
            if (string.IsNullOrEmpty(typed)) return;
            Raw += typed;
            Parse();
        }

        public void Backspace()
        {
            if (Raw.Length == 0) return;
            Raw = Raw.Substring(0, Raw.Length - 1);
            Parse();
        }

        public void Clear() => Set("");

        public bool Matches(in HudLine line)
        {
            if (Needle.Length > 0 && line.Text.IndexOf(Needle, StringComparison.OrdinalIgnoreCase) < 0) return false;
            if (_tags.Count == 0) return true;
            if (string.IsNullOrEmpty(line.Tag)) return false;

            for (int i = 0; i < _tags.Count; i++)
                if (string.Equals(line.Tag, _tags[i], StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>原地裁剪（保持顺序、零新容器）。</summary>
        public void Apply(List<HudLine> lines)
        {
            int write = 0;
            for (int i = 0; i < lines.Count; i++)
                if (Matches(lines[i])) lines[write++] = lines[i];
            if (write < lines.Count) lines.RemoveRange(write, lines.Count - write);
        }

        private void Parse()
        {
            _tags.Clear();
            _text.Clear();

            int i = 0;
            while (i < Raw.Length)
            {
                while (i < Raw.Length && char.IsWhiteSpace(Raw[i])) i++;
                int start = i;
                while (i < Raw.Length && !char.IsWhiteSpace(Raw[i])) i++;
                if (i == start) break;

                string token = Raw.Substring(start, i - start);
                if (token.Length > TagPrefix.Length && token.StartsWith(TagPrefix, StringComparison.Ordinal))
                    _tags.Add(token.Substring(TagPrefix.Length));
                else
                {
                    if (_text.Length > 0) _text.Append(' ');
                    _text.Append(token);
                }
            }

            Needle = _text.ToString();
        }
    }
}
#endif
