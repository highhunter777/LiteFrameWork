using System;

namespace LiteFramework.UI
{
    /// <summary>
    /// 受控属性声明（《UI编辑器工具专项设计》§5"受控属性"面板）：标在**自有控件组件的序列化字段**上， 声明"该成员属内容作者可改的受控面"。
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class UIWidgetPropertyAttribute : Attribute
    {
        /// <summary>面板显示名（留空＝用字段名）。</summary>
        public string DisplayName;

        public UIWidgetPropertyAttribute(string displayName = null)
        {
            DisplayName = displayName;
        }
    }
}
