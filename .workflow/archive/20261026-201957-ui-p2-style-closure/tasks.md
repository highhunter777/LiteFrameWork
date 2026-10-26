# 实施任务清单：UI 系统 P2-3 B4：存量样式收敛与收口

> 本文件由 workflow.py 生成，请使用 `task` 子命令更新，勿手改。

## 任务清单

阶段：已收尾（待归档） ｜ 完成 8/8（取消 0，阻塞 0）

| ID | 任务 | 依赖 | 状态 | 验收标准 | 验证方式 | 证据 |
|---|---|---|---|---|---|---|
| T1 | T1 令牌补集与主题资产生成 | - | 完成 | AC1/AC2：69 唯一 ID（44 精确 + 5 Selectable），44 值逐项；UiTheme.asset 重生成二次 changed=False；真实面 color-drift=0 | UiThemeTokens/StateTokens/Resolver EditMode + ui.style.tokens 连跑两次 + 真实面 style color-drift | 69 色/44 值逐项 + 生成二次 changed=False：UiThemeTokens 18/18、StateTokens 3/3、Resolver 11/11；red 证据 evidence/red.json |
| T2 | T2 Selectable 令牌化组件与迁移件 | T1 | 完成 | AC3：UiSelectableTheme 5 色/仅 ColorTint/保留 multiplier+fade/未知令牌告警一次；EnsureAll 只补组件、跳过嵌套、Added=33/Covered=46/Inherited=13、幂等；真实面 46/46 组件 | UiSelectableThemeEditModeTests + UiSelectableThemeApplierEditModeTests + 真实面组件计数 | UiSelectableTheme 4/4（5 色/multiplier/fade/非 ColorTint/未知/null）；Applier 3/3（33 直挂/13 继承/幂等/不改 ColorBlock）；真实面 Audit 46=3… |
| T3 | T3 三 widget 去默认色 + UiThemeBinder 单源判据 | T1 | 完成 | AC5/AC6：三 widget 无 new Color；Pick 共用 UiStyleResolver.Resolve；UiThemeBinder 注入/refs/writer 祖先跳过/未知告警一次；UIForm 委托 | UiWidgetThemeEditModeTests + UiThemeBinderEditModeTests + UiStyleRefRuleEditModeTests | 三 widget 去默认色 + Resolve：UiWidgetTheme 7/7；UiThemeBinder 6/6（refs/IThemeBound/writer 祖先/SelectableTheme/null/未知一次/真 HUD）… |
| T4 | T4 文字档精确迁移 | T1 | 完成 | AC4：MigrateExactRoleRefs 只加组件不改属性、跳过嵌套、扫 Hud、报告命中角色；真实面 23 组件/37 覆盖/141→104/二次 0 | UiTextStyleEditModeTests + 真实面盘点 + 二次重跑 | UiTextStyle 12/12（23 组件/37 覆盖/14 继承/36px 留债/UGUI/UseInstanceOverrides/幂等）；真实面 unmarked=104、mismatch=0；迁移报告 Added=23/Dir… |
| T5 | T5 ui.style.selectable 规则与令牌 tie-break | T1, T2 | 完成 | AC7：规则五判据正负例；真实面 0；同值 tie-break 确定性；widget 迁移不填 selectable 令牌 | UiStyleSelectableRuleEditModeTests + UiStyleRefEditModeTests + 真实面校验 | UiStyleSelectableRule 5/5（正例/缺组件/未知/漂移/非 ColorTint）；UiStyleRef 8/8（tie-break 过滤）；规则注册进 UiValidator；真实面 selectable=0 |
| T6 | T6 HUD 主题注入链与可观察断言 | T2, T3 | 完成 | AC3/AC6：UIService.ThemeAssetProvider→ProcedureBattle→BattleViewRig 单点注入；真 HUD binder 后 Button_Settings ColorBlock 变、_Slider 不抛；null 降级 | UiThemeBinderEditModeTests 真 HUD 用例 + UiThemeApplyPlayModeTests + 编译/全量回归 | UIService.ThemeAssetProvider→ProcedureBattle→BattleViewRig 单点注入；ContainerModule 传 provider；真 HUD binder 用例 6/6（Button_S… |
| T7 | T7 夹具登记、基线与门禁 | T2, T3, T4, T5, T6 | 完成 | AC8：新套件加入 config commands/red_commands；真实面 Warning 104=0+104；PlayMode selectable/null 告警；wf verify 全绿 | wf verify + UiValidationEditModeTests + UiThemeApplyPlayModeTests | wf verify 57/57 PASS（2026-10-26T20:04，evidence/verify.json；并行线两个 test 文件临时 stash 隔离后恢复，见 build-log 口径）；doc-lint 0/0；真实面… |
| T8 | T8 文档/审计回写 | T7 | 完成 | AC9：UI系统线第13章/总设计 §6.1/§6.2/§6.5/工具链 §8.1/§8.3/UI制作规范/审计登记/README 索引 | doc-lint + docs --check + 审读 | UI系统线第13章+索引、施工进度 README、总设计 §6.2/§6.5、工具链 §8.1/§8.3、UI制作规范 §7、08-UI工作台、审计 606/607/610 回写；doc-lint 0/0 |

## 任务详情

### T1 · T1 令牌补集与主题资产生成

- 文件: Assets/LiteGame/Editor/UiTools/UiStyleTokens.json, Assets/GameData/Config/UI/UiTheme.asset, Assets/Tests/EditMode/UiThemeTokensEditModeTests.cs, Assets/Tests/EditMode/UiThemeStateTokensEditModeTests.cs, Assets/Tests/EditMode/UiStyleResolverEditModeTests.cs

### T2 · T2 Selectable 令牌化组件与迁移件

- 文件: Assets/LiteClient/UI/Style/UiSelectableTheme.cs, Assets/LiteGame/Editor/Style/UiSelectableThemeApplier.cs, Assets/Tests/EditMode/UiSelectableThemeEditModeTests.cs, Assets/Tests/EditMode/UiSelectableThemeApplierEditModeTests.cs

### T3 · T3 三 widget 去默认色 + UiThemeBinder 单源判据

- 文件: Assets/LiteClient/UI/Style/UiStyleResolver.cs, Assets/LiteClient/UI/Style/IThemeGraphicWriter.cs, Assets/LiteClient/UI/Style/UiThemeBinder.cs, Assets/LiteClient/UI/UIForm.cs, Assets/LiteClient/UI/Widgets/StateButton.cs, Assets/LiteClient/UI/Widgets/TabGroup.cs, Assets/LiteClient/UI/Widgets/StarRating.cs, Assets/LiteGame/Editor/Style/UiStyleRefApplier.cs, Assets/LiteGame/Editor/Style/UiStyleTool.cs

### T4 · T4 文字档精确迁移

- 文件: Assets/LiteGame/Editor/Style/UiTextStyleTool.cs, Assets/Tests/EditMode/UiTextStyleEditModeTests.cs

### T5 · T5 ui.style.selectable 规则与令牌 tie-break

- 文件: Assets/LiteGame/Editor/UiTools/Validation/Rules/StyleSelectableRule.cs, Assets/LiteGame/Editor/UiTools/Validation/Rules/StyleRefRule.cs, Assets/LiteGame/Editor/UiTools/Validation/UiValidator.cs, Assets/LiteGame/Editor/UiTools/Validation/UiRuleIds.cs, Assets/Tests/EditMode/UiStyleSelectableRuleEditModeTests.cs, Assets/Tests/EditMode/UiStyleRefEditModeTests.cs

### T6 · T6 HUD 主题注入链与可观察断言

- 文件: Assets/LiteClient/UI/UIService.cs, Assets/LiteGame/App/Procedure/ProcedureBattle.cs, Assets/LiteGame/App/Presentation/Battle/BattleViewRig.cs, Assets/LiteGame/App/Bootstrap/ContainerModule.cs

### T7 · T7 夹具登记、基线与门禁

- 文件: .workflow/config.json, Assets/Tests/EditMode/UiValidationEditModeTests.cs, Assets/Tests/UI/PlayMode/UiThemeApplyPlayModeTests.cs

### T8 · T8 文档/审计回写

- 文件: Docs/施工进度/UI系统线.md, Docs/施工进度/README.md, Docs/设计文档/客户端/UI系统/UI系统总设计.md, Docs/设计文档/客户端/UI系统/UI编辑器工具链专项设计.md, Docs/设计文档/客户端/UI系统/UI制作规范.md, Docs/审计/代码质量审计-客户端.md

