namespace LiteClient.UI
{
    /// <summary>
    /// 控件模板视觉状态词表（《UI 系统总设计》§6.5「每个控件模板声明状态：Normal/Hover/Pressed/
    /// Disabled/Selected/Loading/Empty/Error（按需）」的机器可读单源）。
    ///
    /// 只声明、不驱动：状态由控件自身组件/`Selectable`/`UiFx` 实现，模板规格只登记"该控件存在哪些状态"，
    /// 供清单/校验/文档与工作台读取。
    /// </summary>
    public enum UiWidgetState
    {
        Normal = 0,
        Hover = 1,
        Pressed = 2,
        Disabled = 3,
        Selected = 4,
        Loading = 5,
        Empty = 6,
        Error = 7,
    }
}
