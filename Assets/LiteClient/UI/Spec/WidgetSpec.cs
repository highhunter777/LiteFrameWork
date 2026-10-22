using System;
using UnityEngine;

namespace LiteClient.UI
{
    /// <summary>
    /// 控件模板能力声明（《UI 系统总设计》§1.4「Widget Spec＝控件模板的能力声明：插槽、属性、状态、
    /// 事件、约束、Owner」；《UI 制作规范》§3.2 控件模板根 MUST 声明）。
    ///
    /// **只声明、不驱动**：运行时零行为、零第三方依赖；清单生成器（编辑器）读本组件汇总
    /// `UiWidgetManifest`。可配置属性不在此重复声明——属性真相是控件字段上的
    /// <see cref="UiWidgetPropertyAttribute"/>（`UiWidgetPropertyCatalog` 枚举）。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("LiteGame/UI/WidgetSpec 控件规格")]
    public sealed class WidgetSpec : MonoBehaviour
    {
        /// <summary>模板归属（RACI：模板视觉归 UI 美术，默认 `art.ui`）。</summary>
        [Tooltip("模板归属（RACI；默认 art.ui）")]
        public string Owner = "art.ui";

        /// <summary>一句话用途（对齐《UI 命名与控件词表参考》§3「用途」列）。</summary>
        [Tooltip("一句话用途（词表 §3 的「用途」列）")]
        public string Summary = string.Empty;

        /// <summary>插槽容器（页面可填充内容的位置）。</summary>
        [Tooltip("插槽容器（Cot_*/Slot_*）")]
        public WidgetSlotSpec[] Slots = Array.Empty<WidgetSlotSpec>();

        /// <summary>该控件存在的视觉状态（设计 §6.5 词表）。</summary>
        [Tooltip("视觉状态（设计 §6.5 词表）")]
        public UiWidgetState[] States = Array.Empty<UiWidgetState>();

        /// <summary>对外事件声明：`类型.成员`（如 `StateButton.OnClickEvent`）；只声明模板内真实存在的公开事件/UnityEvent 字段。</summary>
        [Tooltip("对外事件：类型.成员（如 StateButton.OnClickEvent）")]
        public string[] Events = Array.Empty<string>();

        /// <summary>尺寸行为。</summary>
        [Tooltip("尺寸行为（固定/拉伸/随内容）")]
        public UiWidgetSizeBehavior SizeBehavior = UiWidgetSizeBehavior.Fixed;

        /// <summary>使用约束（事实性说明，如「固定尺寸单轴」）。</summary>
        [Tooltip("使用约束（事实性说明）")]
        public string[] Constraints = Array.Empty<string>();
    }
}
