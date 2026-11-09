using System;
using LiteFramework;

namespace LiteGame.Modules.Config
{
    /// <summary>
    /// LText 文案目录投射器（装配层投影件——与生成页面目录同层）：cfg.Tables.Tbltext 行 （key/zh/en 三列）平铺为 key→(locale→text) 的 LocalizationCatalo。
    /// </summary>
    public static class LTextCatalog
    {
        /// <summary>真表行 → 目录（缺 key 行跳过——装载守卫外的防御；空文本合法回退源语言语义交给服务）。</summary>
        public static LocalizationCatalog Build(cfg.Tables tables)
        {
            if (tables == null) throw new ArgumentNullException(nameof(tables));
            var catalog = new LocalizationCatalog();
            foreach (cfg.ltext row in tables.Tbltext.DataList)
            {
                if (row == null || string.IsNullOrEmpty(row.Key)) continue;
                catalog.Add(row.Key, LocalizationCatalog.SourceLocale, row.Zh ?? string.Empty);
                catalog.Add(row.Key, LocalizationCatalog.EnglishLocale, row.En ?? string.Empty);
            }
            return catalog;
        }
    }
}
