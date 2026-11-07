using System;

namespace LiteFramework.UI
{
    /// <summary>
    /// 受控索引的**页签族**（G18）：<see cref="TabGroup"/> 的设值/取值/变化订阅。
    /// </summary>
    public sealed partial class UIBindIndex
    {
        /// <summary>页签设值（程序 Set——静默，不触发 <see cref="TabGroup.OnTabChanged"/>）。G18。</summary>
        public partial void SetTab(string name, int index)
        {
            MarkDriver(name, ControlDriver.Command);
            if (TryGet<ToggleTabGroup>(name, out ToggleTabGroup toggleGroup))
            {
                toggleGroup.SelectSilent(index);
                return;
            }

            Get<TabGroup>(name).SelectSilent(index);
        }

        /// <summary>当前选中页签；未选中 = -1。G18。</summary>
        public partial int GetTab(string name)
        {
            if (TryGet<ToggleTabGroup>(name, out ToggleTabGroup toggleGroup)) return toggleGroup.Selected;
            return Get<TabGroup>(name).Selected;
        }

        /// <summary>页签变化通知（用户点击；回调带索引）。重复登记替换；<c>null</c> = 仅退订。G18。</summary>
        public partial void OnTabChanged(string name, Action<int> handler) => _eventBinder.OnTabChanged(name, handler);
    }
}
