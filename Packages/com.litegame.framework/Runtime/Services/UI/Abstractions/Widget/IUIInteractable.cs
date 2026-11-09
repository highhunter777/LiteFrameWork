namespace LiteFramework.UI
{
    /// <summary>
    /// 自持可交互语义的控件（非 <c>Selectable</c> 的封装件）——受控 API <c>SetInteractable</c> 的第三解析面。
    /// </summary>
    public interface IUIInteractable
    {
        /// <summary>可交互（false = 控件禁用；表现由 inner Selectable 的 disabled 态承担）。</summary>
        bool Interactable { get; set; }
    }
}
