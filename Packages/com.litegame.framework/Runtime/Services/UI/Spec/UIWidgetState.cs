namespace LiteFramework.UI
{
    /// <summary>
    /// 控件模板视觉状态词表（《UI 系统总设计》§6.5「每个控件模板声明状态：Normal/Hover/Pressed/ Disabled/Selected/Loading/Empty/Error（按需）」的机器可读单源）。
    /// </summary>
    public enum UIWidgetState
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
