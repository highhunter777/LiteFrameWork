using UnityEngine;

namespace LiteFramework.UI
{
    /// <summary>文字语义角色（《UI编辑器工具专项设计》§3.1 初始基线；可按语言/可访问性扩展声明覆盖——随真实需求）。</summary>
    public enum UITextRole
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
    /// 文字样式**声明标记**（《UI编辑器工具专项设计》§3.1"样式表 → UITextStyleRef 等可枚举标记 → 批处理应用/对账"）：挂本组件＝声明"该文本按 <see cref="Role"/> 角色档"——。
    /// </summary>
    [AddComponentMenu("LiteGame/UI/UITextStyleRef 文字档标记")]
    public sealed class UITextStyleRef : MonoBehaviour
    {
        /// <summary>本文本的角色档（§3.1 基线表；改动基线值＝改编辑器 UITextStyle 单源后重跑应用）。</summary>
        [Tooltip("文字语义角色——档位基线见编辑器 UiTextStyle 单源；工具按此应用/对账")]
        public UITextRole Role = UITextRole.Body;

        /// <summary>实例保留自身属性（模板角色不应用/不对账）——用于模板实例的合法属性覆盖；
        /// 本节点仍计入未标记债（不静默通过），不改变任何现有属性。</summary>
        [Tooltip("实例保留自身属性（模板角色不应用/不对账）；仍计入未标记债")]
        public bool UseInstanceOverrides;
    }
}
