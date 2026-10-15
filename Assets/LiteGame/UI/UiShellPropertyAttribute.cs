using System;

namespace LiteGame.UI
{
    /// <summary>
    /// 受控属性声明（《UI编辑器工具专项设计》§5"受控属性"面板）：标在**自有控件组件的序列化字段**上，
    /// 声明"该成员属内容作者可改的受控面"。
    ///
    /// **只声明、不驱动**：运行时零行为、零第三方依赖（不引 Sirenix）——编排面板按声明**枚举**可编辑成员，
    /// 未标注成员不进枚举，故"不暴露控件内部实现"是**物理边界**而非按钮禁用。
    /// 接线引用字段（如 <c>HpBar.Slider</c>）属内部实现，不标；几何/锚点归 Unity Prefab Stage 原生编辑，不进本面。
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class UiShellPropertyAttribute : Attribute
    {
        /// <summary>面板显示名（留空＝用字段名）。</summary>
        public string DisplayName;

        public UiShellPropertyAttribute(string displayName = null)
        {
            DisplayName = displayName;
        }
    }
}
