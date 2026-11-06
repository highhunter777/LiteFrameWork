using System.Collections.Generic;

namespace LiteGame.Editor.UITools.Workbench
{
    /// <summary>
    /// 阶段可用性（与角色 Lane 正交的第二层门控）：Lane 决定“角色是否该做”，本类决定“工具是否已建”。
    /// </summary>
    public static class UIWorkbenchAvailability
    {
        private static readonly Dictionary<UIWorkbenchAction, string> Pending =
            new Dictionary<UIWorkbenchAction, string>();

        /// <summary>该动作是否已实现（可点）。</summary>
        public static bool IsImplemented(UIWorkbenchAction action) => !Pending.ContainsKey(action);

        /// <summary>未实现原因（已实现时返回 null）。</summary>
        public static string DisabledReason(UIWorkbenchAction action)
            => Pending.TryGetValue(action, out string reason) ? reason : null;
    }
}
