namespace LiteFramework.UI
{
    /// <summary>
    /// 自持可交互语义的控件（非 <c>Selectable</c> 的封装件）——受控 API <c>SetInteractable</c> 的第三解析面。
    ///
    /// 背景：控件库的封装件（<see cref="UIDropdownWrap"/>/<see cref="UISliderWrap"/>/<see cref="UIToggleWrap"/>/
    /// <see cref="UIInputFieldWrap"/>）自身不是 <c>Selectable</c>，也不是 <see cref="UIWidget"/>；BindNode 把
    /// 封装件本身登记进运行期索引后，"禁用某行控件"在 Selectable/UIWidget 两处都落空。
    /// 由封装件透传 inner 控件的可交互性，页面脚本不必知道封装形态（开闭：新封装件实现本接口即可）。
    /// </summary>
    public interface IUIInteractable
    {
        /// <summary>可交互（false = 控件禁用；表现由 inner Selectable 的 disabled 态承担）。</summary>
        bool Interactable { get; set; }
    }
}
