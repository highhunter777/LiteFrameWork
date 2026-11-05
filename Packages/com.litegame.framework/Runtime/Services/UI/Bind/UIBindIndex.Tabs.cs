using System;

namespace LiteFramework.UI
{
    /// <summary>
    /// 受控索引的**页签族**（G18）：<see cref="TabGroup"/> 的设值/取值/变化订阅。
    ///
    /// 单列一个 partial 文件的原因：《架构预算》对 <c>UIBindIndex.cs::UIBindIndex</c> 有"存量只减不增"的
    /// 行数门禁（402 行），新增一族控件不许把旧文件顶破——按控件族拆件是既定的收紧方向。
    /// 语义与其它输入控件一致：程序 Set 静默（<see cref="TabGroup.SelectSilent"/>），变化通知只回用户点击。
    /// </summary>
    public sealed partial class UIBindIndex
    {
        /// <summary>页签设值（程序 Set——静默，不触发 <see cref="TabGroup.OnTabChanged"/>）。G18。</summary>
        public partial void SetTab(string name, int index)
        {
            MarkDriver(name, ControlDriver.Command);
            Get<TabGroup>(name).SelectSilent(index);
        }

        /// <summary>当前选中页签；未选中 = -1。G18。</summary>
        public partial int GetTab(string name) => Get<TabGroup>(name).Selected;

        /// <summary>页签变化通知（用户点击；回调带索引）。重复登记替换；<c>null</c> = 仅退订。G18。</summary>
        public partial void OnTabChanged(string name, Action<int> handler) => _eventBinder.OnTabChanged(name, handler);
    }
}
