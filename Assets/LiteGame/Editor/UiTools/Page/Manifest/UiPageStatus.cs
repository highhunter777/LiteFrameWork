using System;

namespace LiteGame.Editor.UiTools.Page
{
    /// <summary>
    /// 页面清单状态（《UI 系统总设计》§5.1 与《UI 美术协作与交接规范》§5.3 两处表述合并后的五态；
    /// 词表单源＝《UI 编辑器工具链专项设计》§8.5）。
    /// </summary>
    public enum UiPageStatus
    {
        /// <summary>草案：起页已产出 prefab，尚未登记 tbuiform。</summary>
        Draft = 0,
        /// <summary>评审：Owner 提交评审，等待工程/TA 过目。</summary>
        InReview = 1,
        /// <summary>已登记：tbuiform 有行、Lua/C# 骨架已建。</summary>
        Registered = 2,
        /// <summary>已接线：工程已按契约接线（导航/数据/反馈）。</summary>
        Wired = 3,
        /// <summary>冻结：基线冻结，破坏性变更需新基线。</summary>
        Frozen = 4,
    }

    /// <summary>
    /// 页面状态迁移表（唯一实现源）：`SetStatus` 只接受本表允许的迁移；同值＝无操作。
    /// 迁移表变更须同步《UI 编辑器工具链专项设计》§8.5。
    /// </summary>
    public static class UiPageStatusRules
    {
        /// <summary>词表（按枚举序）。</summary>
        public static readonly string[] Names = Enum.GetNames(typeof(UiPageStatus));

        /// <summary>解析状态名（大小写不敏感）；未知返回 false。</summary>
        public static bool TryParse(string text, out UiPageStatus status)
        {
            status = default;
            if (string.IsNullOrEmpty(text)) return false;
            foreach (string name in Names)
            {
                if (!string.Equals(name, text.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
                status = (UiPageStatus)Enum.Parse(typeof(UiPageStatus), name);
                return true;
            }
            return false;
        }

        /// <summary>该迁移是否合法（同值＝合法但无操作）。</summary>
        public static bool CanTransition(UiPageStatus from, UiPageStatus to)
        {
            if (from == to) return true;
            switch (from)
            {
                case UiPageStatus.Draft:
                    return to == UiPageStatus.InReview || to == UiPageStatus.Registered;
                case UiPageStatus.InReview:
                    return to == UiPageStatus.Draft || to == UiPageStatus.Registered || to == UiPageStatus.Frozen;
                case UiPageStatus.Registered:
                    return to == UiPageStatus.Wired || to == UiPageStatus.InReview || to == UiPageStatus.Frozen;
                case UiPageStatus.Wired:
                    return to == UiPageStatus.Frozen || to == UiPageStatus.Registered;
                case UiPageStatus.Frozen:
                    return to == UiPageStatus.InReview;   // 解冻须回到评审
                default:
                    return false;
            }
        }
    }
}
