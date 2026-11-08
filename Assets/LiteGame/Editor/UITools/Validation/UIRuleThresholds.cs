using System.Collections.Generic;
using System.Linq;

namespace LiteGame.Editor.UITools.Validation
{
    /// <summary>
    /// 校验规则侧的**集中配置单源**（工具链设计 §6.3「规则阈值集中配置，不写死在扫描器」）： 数值阈值（预算/深度上限）与规则词表（富文本白名单、Lua 受控 API 名单）各只此一份， 规则实现与测试均引用本表。
    /// </summary>
    public static class UIRuleThresholds
    {
        /// <summary>性能：单个 prefab 上 `raycastTarget` 开启数的预算上限（超出报 Warning）。</summary>
        public const int RaycastTargetBudget = 60;

        /// <summary>性能：LayoutGroup/ContentSizeFitter 连续嵌套链深度上限（超出报 Warning）。</summary>
        public const int LayoutDepthLimit = 4;

        /// <summary>文本：作者文本允许的富文本标签白名单（UI-TEXT-05；`size` 限范围检查不在此层）。</summary>
        public static readonly IReadOnlyList<string> RichTextAllowedTags = new[] { "b", "i", "color", "size" };

        /// <summary>
        /// Lua 受控 API 名单（名字型第一参；《UI 逻辑 API 参考》§3——`self.ui:方法("控件名", …)` 形态）。
        /// 引用对账（未消费/越界）按此名单提取调用点。
        /// </summary>
        public static readonly IReadOnlyList<string> LuaNameFirstApis = new[]
        {
            "SetTextKey", "SetTextKeyArgs", "SetTextKeyPlural",
            "SetText", "SetInteractable", "SetVisible", "SetAnchoredPosition",
            "OnButton", "OnListClick", "OnToggleChanged", "OnSliderChanged", "OnDropdownChanged",
            "SetSlider", "GetSlider", "SetToggle", "GetToggle",
            "SetDropdownOptions", "SetDropdown", "GetDropdown",
            "SetList", "Pulse", "Flash", "Slide",
        };

        /// <summary>文本双写面：对节点名做动态文本写入的 Lua API（静态文案节点不得再被这些 API 写）。</summary>
        public static readonly IReadOnlyList<string> LuaTextWriteApis = new[]
        {
            "SetText", "SetTextKey", "SetTextKeyArgs", "SetTextKeyPlural",
        };

        /// <summary>事件接线 API → 契约事件名（events[].event 与受控 API 同文；稳定排序单源）。</summary>
        public static readonly IReadOnlyList<(string Api, string Event)> LuaEventApis = new (string, string)[]
        {
            ("OnButton", "onClick"),
            ("OnListClick", "onListClick"),
            ("OnToggleChanged", "onToggleChanged"),
            ("OnSliderChanged", "onSliderChanged"),
            ("OnDropdownChanged", "onDropdownChanged"),
        };

        /// <summary>事件 API 名（`On*` 接线族）。</summary>
        public static IReadOnlyList<string> LuaEventApiNames => LuaEventApis.Select(p => p.Api).ToList();

        /// <summary>
        /// C# 页源受控 API 名单（名字型第一参；《UI 逻辑 API 参考》C# 面——`SetTextKey("控件名", …)` 形态）。
        /// 与 `LuaNameFirstApis` 的差异只是语言写法（`SetDropdown`→`SetDropdownValue`、多 `GetControl`/`OnTabChanged`）。
        /// </summary>
        public static readonly IReadOnlyList<string> PageNameFirstApis = new[]
        {
            "SetTextKey", "SetTextKeyPlural",
            "SetText", "SetInteractable", "SetVisible", "SetAnchoredPosition",
            "OnButton", "OnListClick", "OnToggleChanged", "OnSliderChanged", "OnDropdownChanged", "OnTabChanged",
            "SetSlider", "GetSlider", "SetToggle", "GetToggle",
            "SetDropdownOptions", "SetDropdownValue", "GetDropdownValue",
            "SetTab", "GetTab", "SetList", "Pulse", "Flash", "Slide",
            "GetControl", "HasControl", "UnbindTextKey", "ShowBubble",
        };

        /// <summary>文本双写面（C#）：对节点名做动态文本写入的 API。</summary>
        public static readonly IReadOnlyList<string> PageTextWriteApis = new[]
        {
            "SetText", "SetTextKey", "SetTextKeyPlural",
        };

        /// <summary>事件接线 API → 契约事件名（C# 页源；比 Lua 面多 `OnTabChanged`——页签是 C# 页才接入的接线面）。</summary>
        public static readonly IReadOnlyList<(string Api, string Event)> PageEventApis = new (string, string)[]
        {
            ("OnButton", "onClick"),
            ("OnListClick", "onListClick"),
            ("OnToggleChanged", "onToggleChanged"),
            ("OnSliderChanged", "onSliderChanged"),
            ("OnDropdownChanged", "onDropdownChanged"),
            ("OnTabChanged", "onTabChanged"),
        };
    }
}
