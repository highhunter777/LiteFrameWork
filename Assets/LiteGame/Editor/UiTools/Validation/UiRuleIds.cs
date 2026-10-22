namespace LiteGame.Editor.UiTools.Validation
{
    /// <summary>
    /// 规则标识常量（《UI 编辑器工具链专项设计》§6.2/§6.3）：规则 ID 稳定，报告、例外文件与 CI 按它定位。
    /// 既有规则（`ui.runtime-visual`/`ui.binding-naming`/`ui.binding-hash`/`ui.asset-table-paths`）沿用各自类内常量。
    /// </summary>
    public static class UiRuleIds
    {
        /// <summary>结构：页面根契约（Canvas/CanvasScaler/GraphicRaycaster/CanvasGroup + SafeAreaReceiver）。</summary>
        public const string PageStructure = "ui.page-structure";

        /// <summary>契约：控件模板必须声明 `WidgetSpec`。</summary>
        public const string WidgetSpec = "ui.widget-spec";

        /// <summary>契约：控件清单与磁盘生成物一致（漂移检查）。</summary>
        public const string WidgetManifest = "ui.widget-manifest";

        /// <summary>契约：页面清单与 `tbuiform`/磁盘一致（漂移与对账检查）。</summary>
        public const string PageManifest = "ui.page-manifest";

        /// <summary>例外登记自身：条目字段非法。</summary>
        public const string ExceptionInvalid = "ui.rule-exception.invalid";

        /// <summary>例外登记自身：条目已过期（回到原级别）。</summary>
        public const string ExceptionExpired = "ui.rule-exception.expired";
    }
}
