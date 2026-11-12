using System;
using LiteGameNext.Platform.Localization;

namespace LiteGameNext.Platform.Config
{
    /// <summary>`tbltext` → `TextCatalog` 投影：`Zh` 归源语言、`En` 归英文；空键/空文案跳过。</summary>
    public static class TextTableProjection
    {
        /// <summary>英文 locale 标识（与 `GameSettings` 的 lang 取值一致）。</summary>
        public const string EnglishLocale = "en";

        /// <summary>把表投影进目录（同 key 覆盖）；返回登记条数。</summary>
        public static int Project(cfg.Tables tables, TextCatalog catalog)
        {
            if (tables == null) throw new ArgumentNullException(nameof(tables));
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (tables.Tbltext == null) return 0;

            int count = 0;
            foreach (cfg.ltext row in tables.Tbltext.DataList)
            {
                if (row == null || string.IsNullOrEmpty(row.Key)) continue;
                bool added = false;
                if (!string.IsNullOrEmpty(row.Zh))
                {
                    catalog.Add(row.Key, TextCatalog.SourceLocale, row.Zh);
                    added = true;
                }
                if (!string.IsNullOrEmpty(row.En))
                {
                    catalog.Add(row.Key, EnglishLocale, row.En);
                    added = true;
                }
                if (added) count++;
            }
            return count;
        }
    }
}
