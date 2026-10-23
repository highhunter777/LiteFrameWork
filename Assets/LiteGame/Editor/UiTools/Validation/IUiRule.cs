using System.Collections.Generic;

namespace LiteGame.Editor.UiTools.Validation
{
    /// <summary>
    /// 校验输入面（《UI编辑器工具专项设计》§4——同一校验核心供菜单/CLI/测试调用）。
    ///
    /// **双侧**：UI 校验面天然跨两类输入——**源码面**（运行时不得构建视觉，§7）与**资产面**
    /// （模板 prefab 的命名/绑定/引用，§9）。规则按自身需要取用其一，故上下文同携两者，
    /// 端口一次到位（后续绑定/文本规则走资产面，视觉规则走源码面）。
    ///
    /// 资产路径与源码目录均以**工程根相对**表示；规则自行决定是否装载/遍历（纯函数规则可只读文本）。
    /// </summary>
    public sealed class UiValidationContext
    {
        /// <summary>工程根绝对路径（资产/源码相对路径的解析基准）。</summary>
        public string ProjectRoot { get; }

        /// <summary>源码目录集（工程根相对，如 <c>Assets/LiteGame/App</c>）——源码面规则用。</summary>
        public IReadOnlyList<string> SourceDirs { get; }

        /// <summary>资产路径集（工程根相对，如 prefab 路径）——资产面规则用。</summary>
        public IReadOnlyList<string> AssetPaths { get; }

        /// <summary>生成物 hash 清单路径（工程根相对）——绑定族 hash 规则用；缺省＝<see cref="UiBindGenerationHash.DefaultManifestPath"/>。
        /// 测试可注入临时清单（隔离全局清单状态）。</summary>
        public string BindManifestPath { get; }

        /// <summary>例外登记路径（工程根相对）；缺省＝<see cref="UiRuleExceptionSet.DefaultPath"/>。</summary>
        public string RuleExceptionsPath { get; }

        /// <summary>控件清单路径（工程根相对）；缺省＝<see cref="Widgets.UiWidgetManifestPaths.DefaultPath"/>。</summary>
        public string WidgetManifestPath { get; }

        /// <summary>页面清单路径（工程根相对）；缺省＝<see cref="Page.UiPageManifestPaths.DefaultPath"/>。</summary>
        public string PageManifestPath { get; }

        /// <summary>
        /// 文本表种子（测试注入合成 tbltext 条目；缺省 null＝规则走 <see cref="Text.UiLTextTable"/> 真实装载）。
        /// 文本族规则的表数据面缝——与清单路径注入同型。
        /// </summary>
        public System.Collections.Generic.IReadOnlyList<Text.UiLTextEntry> LTextSeed { get; }

        public UiValidationContext(string projectRoot,
            IReadOnlyList<string> sourceDirs = null,
            IReadOnlyList<string> assetPaths = null,
            string bindManifestPath = null,
            string ruleExceptionsPath = null,
            string widgetManifestPath = null,
            string pageManifestPath = null,
            System.Collections.Generic.IReadOnlyList<Text.UiLTextEntry> lTextSeed = null)
        {
            ProjectRoot = projectRoot;
            SourceDirs = sourceDirs ?? new List<string>();
            AssetPaths = assetPaths ?? new List<string>();
            BindManifestPath = bindManifestPath;
            RuleExceptionsPath = ruleExceptionsPath;
            WidgetManifestPath = widgetManifestPath;
            PageManifestPath = pageManifestPath;
            LTextSeed = lTextSeed;
        }
    }

    /// <summary>
    /// 校验规则端口（§4）。一类一规则；<see cref="RuleId"/> 稳定（报告与 CI 按它定位）。
    /// </summary>
    public interface IUiRule
    {
        /// <summary>规则标识（稳定字符串，如 <c>ui.runtime-visual</c>）。</summary>
        string RuleId { get; }

        /// <summary>规则族名（对齐 §4 规则族表：绑定/资源/文本/渲染适配/行为/性能）。</summary>
        string Family { get; }

        /// <summary>执行校验，产出问题列表（可空/空集合 = 无问题）。</summary>
        IEnumerable<UiIssue> Check(UiValidationContext context);
    }
}
