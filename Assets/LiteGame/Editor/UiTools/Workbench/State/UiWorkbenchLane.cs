namespace LiteGame.Editor.UiTools.Workbench
{
    /// <summary>
    /// 工作台角色 Lane（《UI 编辑器工具链专项设计》§2.3）：**UX 分层与默认动作集，不是权限系统**。
    /// 同一 owner 仍遵守「一个 prefab 同一时间一个 owner」。
    /// </summary>
    public enum UiWorkbenchLane
    {
        /// <summary>美术：起页、控件库、布局、令牌、静态 key、暴露标记、契约生成、增量校验、预览。</summary>
        Art,

        /// <summary>TA：令牌/字体/图集/控件模板、控件规范、性能预算、全量校验。</summary>
        Technical,

        /// <summary>工程：页面登记、绑定生成、契约评审/消费、逻辑接线、全量校验、运行时调试。</summary>
        Engineering,
    }

    /// <summary>工作台可门控动作（P2-1 可用集；起页/登记在 P2-2 拆成独立工具后仍走同一动作表）。</summary>
    public enum UiWorkbenchAction
    {
        /// <summary>打开页面（Prefab Stage / Project 选中跟随），三 Lane 共享。</summary>
        OpenPage,

        /// <summary>编辑页面清单维护字段（Owner/设计稿/备注/状态）。</summary>
        EditMetadata,

        /// <summary>起页（仅 prefab + 草稿清单；P2-2 落地，P2-1 置灰）。</summary>
        Scaffold,

        /// <summary>页面登记（表/Lua/Luban/hash；P2-2 落地，P2-1 置灰）。</summary>
        Register,

        /// <summary>控件库插入 <c>Tmp_*</c> 实例。</summary>
        InsertWidget,

        /// <summary>绑定标记/改名/引用扫描（绑定编辑）。</summary>
        EditBinding,

        /// <summary>绑定代码生成（<c>ui.bind-generate</c> 目标 ID，P2-1 不实现生成器，仅门控位）。</summary>
        GenerateBinding,

        /// <summary>生成页面契约（`ui.contract`）。</summary>
        GenerateContract,

        /// <summary>契约对账（`ui.contract-diff`）。</summary>
        DiffContract,

        /// <summary>校验（含全量；增量归 P2-3）。</summary>
        Validate,

        /// <summary>单页布局预览（`ui.preview` 的 P2-1 形态：打开既有预览场景）。</summary>
        Preview,
    }

    /// <summary>透视标识（§2.2 七透视的 P2-1 子集：页面/控件库/绑定/契约/校验；样式与预览归 P2-3）。</summary>
    public enum UiWorkbenchPerspectiveId
    {
        Page,
        Widgets,
        Binding,
        Contract,
        Validation,
    }
}
