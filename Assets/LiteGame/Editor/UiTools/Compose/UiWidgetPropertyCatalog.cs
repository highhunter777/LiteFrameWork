using System;
using System.Collections.Generic;
using System.Reflection;
using LiteGame.Editor.UiTools.Binding;
using LiteFramework.UI;
using UnityEngine;

namespace LiteGame.Editor.UiTools.Compose
{
    /// <summary>受控属性行的值型（面板按型分派编辑器；未支持型显式列出，不静默跳过）。</summary>
    public enum UiWidgetPropKind
    {
        Bool,
        Int,
        Float,
        String,
        Enum,
        Unsupported,
    }

    /// <summary>受控属性行（白名单发现结果）。</summary>
    public sealed class UiWidgetPropRow
    {
        /// <summary>节点相对 prefab 根的路径（与绑定/报告同口径）。</summary>
        public string NodePath;

        /// <summary>承载组件类型名（同节点可多组件）。</summary>
        public string ComponentTypeName;

        /// <summary>字段名。</summary>
        public string MemberName;

        public UiWidgetPropKind Kind;

        /// <summary>面板显示名（attribute 未给＝字段名）。</summary>
        public string DisplayName;

        public string Tooltip;

        /// <summary>字段反射（本行由同一次 Collect 产出，随 Collect 结果有效）。</summary>
        public FieldInfo Field;

        /// <summary>承载组件实例（同上——显示根变化须重收集）。</summary>
        public Component Owner;

        /// <summary>行键（同节点同组件同字段唯一；面板按需做去重/定位）。</summary>
        public string Key => NodePath + "|" + ComponentTypeName + "|" + MemberName;
    }

    /// <summary>
    /// 受控属性白名单发现（《UI编辑器工具专项设计》§5"受控属性"）：遍历自有控件组件（程序集＝
    /// <c>LiteFramework.UI</c>，锚点为本单源 attribute 自身），只收带 <see cref="UiWidgetPropertyAttribute"/>
    /// 的公开实例字段。**未标注成员不进枚举**——UGUI 契约件（Slider/Image/TMP）与 <c>_私有件</c> 天然不在白名单，
    /// "不改控件内部实现"由此成为机制而非按钮禁用。
    ///
    /// 纯逻辑面（零 Sirenix）：编排面板按本结果分流绘制，EditMode 用例直测此处。
    /// </summary>
    public static class UiWidgetPropertyCatalog
    {
        /// <summary>LiteFramework.UI 程序集锚点（本单源 attribute 自身——不写死程序集名字符串）。</summary>
        private static readonly Assembly UiAssembly = typeof(UiWidgetPropertyAttribute).Assembly;

        /// <summary>该组件是否属自有 UI 程序集（受控面只认自有控件）。</summary>
        public static bool IsControlledComponent(Component component)
            => component != null && component.GetType().Assembly == UiAssembly;

        /// <summary>值型归类（bool/int/float/string/enum 为受控最小集；其余＝Unsupported）。</summary>
        public static UiWidgetPropKind Classify(Type type)
        {
            if (type == typeof(bool)) return UiWidgetPropKind.Bool;
            if (type == typeof(int)) return UiWidgetPropKind.Int;
            if (type == typeof(float)) return UiWidgetPropKind.Float;
            if (type == typeof(string)) return UiWidgetPropKind.String;
            if (type.IsEnum) return UiWidgetPropKind.Enum;
            return UiWidgetPropKind.Unsupported;
        }

        /// <summary>
        /// 收集显示根下的全部受控属性行（按 节点路径/组件类型/字段名 稳定序）。
        /// 未标注成员、非自有程序集组件、私有/静态字段一律不进结果。
        /// </summary>
        public static List<UiWidgetPropRow> Collect(GameObject root)
        {
            var rows = new List<UiWidgetPropRow>();
            if (root == null) return rows;

            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (!IsControlledComponent(component)) continue;
                foreach (var field in component.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    var attribute = (UiWidgetPropertyAttribute)Attribute.GetCustomAttribute(
                        field, typeof(UiWidgetPropertyAttribute), inherit: true);
                    if (attribute == null) continue;

                    rows.Add(new UiWidgetPropRow
                    {
                        NodePath = UiBindingRename.RelativePath(component.transform),
                        ComponentTypeName = component.GetType().Name,
                        MemberName = field.Name,
                        Kind = Classify(field.FieldType),
                        DisplayName = string.IsNullOrEmpty(attribute.DisplayName) ? field.Name : attribute.DisplayName,
                        Tooltip = TooltipOf(field),
                        Field = field,
                        Owner = component,
                    });
                }
            }

            rows.Sort((a, b) =>
            {
                int c = string.CompareOrdinal(a.NodePath, b.NodePath);
                if (c != 0) return c;
                c = string.CompareOrdinal(a.ComponentTypeName, b.ComponentTypeName);
                return c != 0 ? c : string.CompareOrdinal(a.MemberName, b.MemberName);
            });
            return rows;
        }

        private static string TooltipOf(FieldInfo field)
        {
            var tooltip = (TooltipAttribute)Attribute.GetCustomAttribute(field, typeof(TooltipAttribute));
            return tooltip != null && !string.IsNullOrEmpty(tooltip.tooltip) ? tooltip.tooltip : null;
        }
    }
}
