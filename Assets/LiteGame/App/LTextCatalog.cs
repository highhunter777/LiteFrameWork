using System;
using LiteFramework;

namespace LiteGame
{
    /// <summary>
    /// LText 文案目录投射器（装配层投影件——UIFormCatalog 同族）：<c>cfg.Tables.Tbltext</c> 行
    /// （key/zh/en 三列）平铺为 key→(locale→text) 的 <see cref="LocalizationCatalog"/>。
    ///
    /// **列名→locale 的映射单源在此**：表内列名受 C# 标识符约束（zh/en），与 BCP-47 口径的映射为
    /// zh 列 = <see cref="LocalizationCatalog.SourceLocale"/>（zh-CN）、en 列 =
    /// <see cref="LocalizationCatalog.EnglishLocale"/>——新增语言 = 表加列 + 此处加映射行
    /// （复数/RTL 需代码评估，不承诺"加列零代码"——<see cref="LocalizationService"/> 同口径）。
    /// key 命名合规由表守卫用例钉（LTextKey.Validate，EditMode）。
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
