namespace LiteClient.UI
{
    /// <summary>
    /// 控件模板尺寸行为（《UI 制作规范》§3.2「声明 WidgetSpec：…尺寸行为」的机器可读单源）。
    ///
    /// - <see cref="Fixed"/>：尺寸由模板自身固定（图标/红点/星级等）。
    /// - <see cref="Expand"/>：沿父容器拉伸（血条/进度条/列表/输入框/遮罩等）。
    /// - <see cref="FitContent"/>：随内容自适应（气泡/提示/文本/条目等）。
    ///
    /// 只声明、不驱动：实际布局仍由 RectTransform/布局组件决定。
    /// </summary>
    public enum UiWidgetSizeBehavior
    {
        Fixed = 0,
        Expand = 1,
        FitContent = 2,
    }
}
