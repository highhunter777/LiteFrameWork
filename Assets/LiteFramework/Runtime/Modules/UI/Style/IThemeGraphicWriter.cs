using UnityEngine;
using UnityEngine.UI;

namespace LiteClient.UI
{
    /// <summary>
    /// Graphic 写权能力（《UI 系统总设计》§6.2 单写者）：实现方声明"我拥有哪些 Graphic 的颜色写权"——
    /// 只对 <see cref="Owns"/> 返回 true 的目标生效（不是整棵子树）；`UiStyleRef` 在该目标上由
    /// `ui.style.ref` 报双写者、由应用核心跳过。`UiSelectableTheme` **不实现**本接口（只写状态 tint，不写基色）。
    /// </summary>
    public interface IThemeGraphicWriter
    {
        /// <summary>是否拥有该 Graphic 的颜色写权（StateButton=自身 Image；TabGroup=Tabs；StarRating=Stars）。</summary>
        bool Owns(Graphic graphic);
    }
}