#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using LiteClient;
using LiteFramework;

namespace LiteGame
{
    /// <summary>
    /// 预览语言构建器（P2-3 B3）：tbltext **文件直载**（`TableLayout.Files` 含 `UI/tbltext`、
    /// `LTextCatalog.Build(cfg.Tables)` 纯函数——与 <see cref="ConfigTableFileSource"/> 共用全表字节模式）
    /// 构建 <see cref="ILocalizationService"/>，驱动布局档中/英/伪本地化切换；不经内容服务全链。
    /// 失败返回 null（调用方降级：key 显 `[key]` 并在矩阵报告标注）。
    /// </summary>
    public static class UiPreviewLocale
    {
        /// <summary>预览支持的语言集（zh-CN/en/pseudo）；矩阵/工作台/驱动共用，避免三份字面量。</summary>
        public static IReadOnlyList<string> SupportedLocales { get; } =
            new ReadOnlyCollection<string>(new[] { "zh-CN", "en", "pseudo" });

        /// <summary>语言状态词（off/unavailable/ok；pending 由编辑器写入，驱动应用后落终态）。</summary>
        public static string StatusFor(string locale, bool serviceAvailable)
            => string.IsNullOrEmpty(locale) ? "off" : (serviceAvailable ? "ok" : "unavailable");

        /// <summary>从工程表字节构建预览语言服务；失败返回 null（不抛——降级口径）。</summary>
        public static ILocalizationService BuildService(string dataDir = null)
        {
            try
            {
                cfg.Tables tables = ConfigTableFileSource.Load(
                    string.IsNullOrEmpty(dataDir) ? ConfigService.DataDir : dataDir);
                LocalizationCatalog catalog = LTextCatalog.Build(tables);
                return new LocalizationService(catalog, LocalizationCatalog.SourceLocale, developmentMode: true);
            }
            catch (Exception error)
            {
                Log.Warning("[UiPreview] tbltext 直载失败：" + error.Message, LogTag.UI);
                return null;
            }
        }
    }
}
#endif
