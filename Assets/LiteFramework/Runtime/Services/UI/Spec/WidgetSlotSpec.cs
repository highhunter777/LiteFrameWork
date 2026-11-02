using System;
using UnityEngine;

namespace LiteFramework.UI
{
    /// <summary>
    /// 控件模板插槽声明（《UI 系统总设计》§6.5「模板插槽用命名容器（`Cot_Header`/`Cot_Body`/
    /// `Cot_Footer`），插槽内容由页面填充」）：声明"页面可以把内容放进哪个容器"。
    ///
    /// `Name` 为模板根下的相对节点路径（通常是直接子节点，如 `Cot_Content`）；生成清单时校验节点真实存在。
    /// </summary>
    [Serializable]
    public sealed class WidgetSlotSpec
    {
        /// <summary>插槽容器节点路径（`Cot_*`/`Slot_*` 起头）。</summary>
        [Tooltip("插槽容器节点路径（Cot_*/Slot_*）")]
        public string Name = string.Empty;

        /// <summary>是否必需：页面使用该控件时必须填充（文档/校验用）。</summary>
        [Tooltip("页面使用该控件时必须填充该插槽")]
        public bool Required = true;

        /// <summary>插槽用途说明（给美术/工程的交接提示）。</summary>
        [Tooltip("插槽用途说明")]
        public string Description = string.Empty;
    }
}
