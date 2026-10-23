using System.Collections.Generic;

namespace LiteGame.Editor.UiTools.Workbench
{
    /// <summary>
    /// 阶段可用性（与角色 Lane 正交的第二层门控）：Lane 决定“角色是否该做”，本类决定“工具是否已建”。
    /// P2-1 未提供起页/登记工具（拆分归 P2-2），窗口提示按「阶段原因优先、Lane 原因其次」显示，
    /// 避免“全局置灰掩盖角色差异”。
    /// </summary>
    public static class UiWorkbenchAvailability
    {
        private static readonly Dictionary<UiWorkbenchAction, string> Pending =
            new Dictionary<UiWorkbenchAction, string>
            {
                {
                    UiWorkbenchAction.Scaffold,
                    "起页/登记拆分归 P2-2（本批未提供起页工具；美术起页只建 prefab、不写表）"
                },
                {
                    UiWorkbenchAction.Register,
                    "页面登记拆分归 P2-2（契约评审后由工程登记：表/Lua/Luban/hash）"
                },
            };

        /// <summary>该动作在 P2-1 是否已实现（可点）。</summary>
        public static bool IsImplemented(UiWorkbenchAction action) => !Pending.ContainsKey(action);

        /// <summary>未实现原因（已实现时返回 null）。</summary>
        public static string DisabledReason(UiWorkbenchAction action)
            => Pending.TryGetValue(action, out string reason) ? reason : null;
    }
}
