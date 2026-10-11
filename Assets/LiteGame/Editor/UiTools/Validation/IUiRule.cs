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

        public UiValidationContext(string projectRoot,
            IReadOnlyList<string> sourceDirs = null,
            IReadOnlyList<string> assetPaths = null)
        {
            ProjectRoot = projectRoot;
            SourceDirs = sourceDirs ?? new List<string>();
            AssetPaths = assetPaths ?? new List<string>();
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
