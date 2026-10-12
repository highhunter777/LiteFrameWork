using UnityEngine;

namespace LiteGame.UI
{
    /// <summary>文字语义角色（《UI编辑器工具专项设计》§3.1 初始基线；可按语言/可访问性扩展声明覆盖——随真实需求）。</summary>
    public enum UiTextRole
    {
        /// <summary>标题：24 号、居中、不换行、初始 Truncate（非关键标题；关键标题按容器调整）、常规字重。</summary>
        Title = 0,

        /// <summary>正文：20 号、居中、换行、初始 Overflow（仅自适应/滚动容器内允许扩展）、常规字重。</summary>
        Body = 1,

        /// <summary>说明/注脚：14 号、左对齐、换行、初始 Overflow（由容器限制或滚动）、常规字重。</summary>
        Caption = 2,

        /// <summary>单行标签：20 号、居中、不换行、Truncate（关键操作需另行扩展/换行策略）、常规字重。</summary>
        LabelSingle = 3,

        /// <summary>强调文本：20 号、居中、换行、同 Body、**加粗**。</summary>
        Emphasis = 4,
    }

    /// <summary>
    /// 文字样式**声明标记**（《UI编辑器工具专项设计》§3.1"样式表 → UiTextStyleRef 等可枚举标记 →
    /// 批处理应用/对账"）：挂本组件＝声明"该文本按 <see cref="Role"/> 角色档"——档位基线值
    /// （字号/对齐/换行/溢出策略/字重）住编辑器单源 <c>UiTextStyle</c>，由样式工具**批量应用**写入
    /// 文本组件、或**对账**报告属性漂移。
    ///
    /// 与 <see cref="UiStyle"/>（颜色 token）分工：本标记只辖文字档，不复制颜色值（§3.1）。
    /// **只声明、不驱动**：组件不进运行时表现路径——工具按声明写组件属性，运行时按状态改实例合法
    /// （§3.1"运行时按状态改实例合法，样式对账只读盘上资产"）；空 <see cref="Role"/> 无默认档
    /// （起名作者面必须显式选档，工具不得猜语义）。
    /// </summary>
    [AddComponentMenu("LiteGame/UI/UiTextStyleRef 文字档标记")]
    public sealed class UiTextStyleRef : MonoBehaviour
    {
        /// <summary>本文本的角色档（§3.1 基线表；改动基线值＝改编辑器 UiTextStyle 单源后重跑应用）。</summary>
        [Tooltip("文字语义角色——档位基线见编辑器 UiTextStyle 单源；工具按此应用/对账")]
        public UiTextRole Role = UiTextRole.Body;
    }
}
