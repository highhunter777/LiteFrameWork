using System;
using System.Collections.Generic;

namespace LiteGame.Editor.UITools.Workbench
{
    /// <summary>
    /// Lane × 动作门控的单源真相（《UI 编辑器工具链专项设计》§2.3）：矩阵表驱动，
    /// 窗口禁用态、原因文案与夹具断言共用同一份表；禁止在窗口里散落 <c>if (lane == …)</c>。
    /// 角色 Lane 只表达“UX 分层与默认动作集”，不是权限系统。
    /// </summary>
    public static class UILanePolicy
    {
        /// <summary>一条矩阵行：动作 + 允许角色 + 对不适用角色的统一原因。</summary>
        private sealed class Rule
        {
            public UIWorkbenchAction Action;
            public UIWorkbenchLane[] Lanes;
            public string ForbiddenReason;
        }

        private static readonly Rule[] Rules =
        {
            Row(UIWorkbenchAction.OpenPage, All(), null),
            Row(UIWorkbenchAction.EditMetadata, new[] { UIWorkbenchLane.Art, UIWorkbenchLane.Engineering },
                "页面元数据由美术/工程维护（TA 只读）"),
            Row(UIWorkbenchAction.Scaffold, new[] { UIWorkbenchLane.Art },
                "起页是美术职责（设计 §2.3）"),
            Row(UIWorkbenchAction.Register, new[] { UIWorkbenchLane.Engineering },
                "登记是工程职责（表/Lua/绑定生成）"),
            Row(UIWorkbenchAction.InsertWidget,
                new[] { UIWorkbenchLane.Art, UIWorkbenchLane.Technical },
                "控件插入属视觉组装；工程改结构须走美术评审"),
            Row(UIWorkbenchAction.EditBinding, All(), null),
            Row(UIWorkbenchAction.GenerateBinding, new[] { UIWorkbenchLane.Engineering },
                "绑定代码生成是工程职责"),
            Row(UIWorkbenchAction.GenerateContract, All(), null),
            Row(UIWorkbenchAction.DiffContract,
                new[] { UIWorkbenchLane.Technical, UIWorkbenchLane.Engineering },
                "契约对账由 TA/工程评审触发；美术跑增量校验与预览"),
            Row(UIWorkbenchAction.Validate, All(), null),
            Row(UIWorkbenchAction.Preview, All(), null),
        };

        /// <summary>该 Lane 是否允许执行该动作；未登记的动作返回 false（保守）。</summary>
        public static bool IsAllowed(UIWorkbenchLane lane, UIWorkbenchAction action)
        {
            Rule rule = Find(action);
            return rule != null && Array.IndexOf(rule.Lanes, lane) >= 0;
        }

        /// <summary>禁止原因（允许时为 null）——窗口 tooltip 与夹具断言共用。</summary>
        public static string DisabledReason(UIWorkbenchLane lane, UIWorkbenchAction action)
        {
            Rule rule = Find(action);
            if (rule == null) return "动作未登记角色策略：" + action;
            if (Array.IndexOf(rule.Lanes, lane) >= 0) return null;
            return rule.ForbiddenReason ?? "该动作不属于此角色";
        }

        /// <summary>该 Lane 的允许动作集（按枚举声明顺序，保证 UI 顺序稳定）。</summary>
        public static IReadOnlyList<UIWorkbenchAction> Allowed(UIWorkbenchLane lane)
        {
            var allowed = new List<UIWorkbenchAction>();
            foreach (UIWorkbenchAction action in Enum.GetValues(typeof(UIWorkbenchAction)))
            {
                if (IsAllowed(lane, action)) allowed.Add(action);
            }

            return allowed;
        }

        private static Rule Find(UIWorkbenchAction action)
        {
            foreach (Rule rule in Rules)
            {
                if (rule.Action == action) return rule;
            }

            return null;
        }

        private static UIWorkbenchLane[] All()
            => (UIWorkbenchLane[])Enum.GetValues(typeof(UIWorkbenchLane));

        private static Rule Row(UIWorkbenchAction action, UIWorkbenchLane[] lanes, string forbiddenReason)
            => new Rule { Action = action, Lanes = lanes, ForbiddenReason = forbiddenReason };
    }
}
