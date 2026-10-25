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

        /// <summary>文本：静态文案 key 必须在 tbltext（`LTextKeyRef` 与 Lua `SetTextKey*` 两路）。</summary>
        public const string TextStaticKey = "ui.text.static-key";

        /// <summary>文本：占位符使用不符（无参静态键挂带参条目/`SetTextKeyArgs` 参数数与条目不符）。</summary>
        public const string TextPlaceholder = "ui.text.placeholder";

        /// <summary>文本：语言列缺失（源语言空＝Error；英文列空＝Warning）。</summary>
        public const string TextLocaleColumn = "ui.text.locale-column";

        /// <summary>文本：富文本标签越白名单（作者文本只允许白名单标签）。</summary>
        public const string TextRichText = "ui.text.rich-text";

        /// <summary>样式：Graphic 颜色不命中任何 token（手调/漂移——对账报告暴露，不擅改）。</summary>
        public const string StyleColorDrift = "ui.style.color-drift";

        /// <summary>样式：文字档对账（未标记＝裸字号/属性漂移/标记失效）。</summary>
        public const string StyleTextDrift = "ui.style.text-drift";

        /// <summary>样式：令牌源 JSON 与主题资产 SO 不一致（SO 过期或手改；提示跑 `ui.style.tokens`）。</summary>
        public const string StyleTokenDrift = "ui.style.token-drift";

        /// <summary>布局/适配：根 CanvasScaler 参数偏离基线，或页面自带第二套缩放（UI-LAYOUT-01）。</summary>
        public const string LayoutScaler = "ui.layout.scaler";

        /// <summary>性能：单 prefab `raycastTarget` 开启数超预算。</summary>
        public const string PerformanceRaycastTarget = "ui.performance.raycast-target";

        /// <summary>性能：LayoutGroup/ContentSizeFitter 嵌套链深度超上限。</summary>
        public const string PerformanceLayoutDepth = "ui.performance.layout-depth";

        /// <summary>性能：页面根挂 Animator（Error，UI-PREFAB-03）；UI 子树任意 Animator（Warning）。</summary>
        public const string PerformanceAnimator = "ui.performance.animator";

        /// <summary>契约：盘上契约与按 prefab 重生成结果不一致（含手改检出）。</summary>
        public const string ContractDrift = "ui.contract.drift";

        /// <summary>契约：引用对账（required 控件未被 Lua 消费＝Warning；Lua 调用点名不在契约面＝Error）。</summary>
        public const string ContractReference = "ui.contract.reference";

        /// <summary>契约：静态文案节点（`LTextKeyRef`）被 Lua `SetText*` 再写（文本双写）。</summary>
        public const string ContractTextDoubleWrite = "ui.contract.text-double-write";

        /// <summary>例外登记自身：条目字段非法。</summary>
        public const string ExceptionInvalid = "ui.rule-exception.invalid";

        /// <summary>例外登记自身：条目已过期（回到原级别）。</summary>
        public const string ExceptionExpired = "ui.rule-exception.expired";
    }
}
