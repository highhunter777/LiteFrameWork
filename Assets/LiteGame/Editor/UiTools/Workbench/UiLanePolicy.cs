using System;
using System.Collections.Generic;

namespace LiteGame.Editor.UiTools.Workbench
{
    /// <summary>
    /// Lane × 动作门控的单源真相（《UI 编辑器工具链专项设计》§2.3）：矩阵表驱动，
    /// 窗口禁用态、原因文案与夹具断言共用同一份表；禁止在窗口里散落 <c>if (lane == …)</c>。
    /// 角色 Lane 只表达“UX 分层与默认动作集”，不是权限系统。
    /// </summary>
    public static class UiLanePolicy
    {
        /// <summary>一条矩阵行：动作 + 允许角色 + 对不适用角色的统一原因。</summary>
        private sealed class Rule
        {
            public UiWorkbenchAction Action;
            public UiWorkbenchLane[] Lanes;
            public string ForbiddenReason;
        }

        private static readonly Rule[] Rules =
        {
            Row(UiWorkbenchAction.OpenPage, All(), null),
            Row(UiWorkbenchAction.EditMetadata, new[] { UiWorkbenchLane.Art, UiWorkbenchLane.Engineering },
                "页面元数据由美术/工程维护（TA 只读）"),
            Row(UiWorkbenchAction.Scaffold, new[] { UiWorkbenchLane.Art },
                "起页是美术职责（设计 §2.3）"),
            Row(UiWorkbenchAction.Register, new[] { UiWorkbenchLane.Engineering },
                "登记是工程职责（表/Lua/绑定生成）"),
            Row(UiWorkbenchAction.InsertWidget,
                new[] { UiWorkbenchLane.Art, UiWorkbenchLane.Technical },
                "控件插入属视觉组装；工程改结构须走美术评审"),
            Row(UiWorkbenchAction.EditBinding, All(), null),
            Row(UiWorkbenchAction.GenerateBinding, new[] { UiWorkbenchLane.Engineering },
                "绑定代码生成是工程职责"),
            Row(UiWorkbenchAction.GenerateContract, All(), null),
            Row(UiWorkbenchAction.DiffContract,
                new[] { UiWorkbenchLane.Technical, UiWorkbenchLane.Engineering },
                "契约对账由 TA/工程评审触发；美术跑增量校验与预览"),
            Row(UiWorkbenchAction.Validate, All(), null),
            Row(UiWorkbenchAction.Preview, All(), null),
        };

        /// <summary>该 Lane 是否允许执行该动作；未登记的动作返回 false（保守）。</summary>
        public static bool IsAllowed(UiWorkbenchLane lane, UiWorkbenchAction action)
        {
            Rule rule = Find(action);
            return rule != null && Array.IndexOf(rule.Lanes, lane) >= 0;
        }

        /// <summary>禁止原因（允许时为 null）——窗口 tooltip 与夹具断言共用。</summary>
        public static string DisabledReason(UiWorkbenchLane lane, UiWorkbenchAction action)
        {
            Rule rule = Find(action);
            if (rule == null) return "动作未登记角色策略：" + action;
            if (Array.IndexOf(rule.Lanes, lane) >= 0) return null;
            return rule.ForbiddenReason ?? "该动作不属于此角色";
        }

        /// <summary>该 Lane 的允许动作集（按枚举声明顺序，保证 UI 顺序稳定）。</summary>
        public static IReadOnlyList<UiWorkbenchAction> Allowed(UiWorkbenchLane lane)
        {
            var allowed = new List<UiWorkbenchAction>();
            foreach (UiWorkbenchAction action in Enum.GetValues(typeof(UiWorkbenchAction)))
            {
                if (IsAllowed(lane, action)) allowed.Add(action);
            }

            return allowed;
        }

        private static Rule Find(UiWorkbenchAction action)
        {
            foreach (Rule rule in Rules)
            {
                if (rule.Action == action) return rule;
            }

            return null;
        }

        private static UiWorkbenchLane[] All()
            => (UiWorkbenchLane[])Enum.GetValues(typeof(UiWorkbenchLane));

        private static Rule Row(UiWorkbenchAction action, UiWorkbenchLane[] lanes, string forbiddenReason)
            => new Rule { Action = action, Lanes = lanes, ForbiddenReason = forbiddenReason };
    }
}
