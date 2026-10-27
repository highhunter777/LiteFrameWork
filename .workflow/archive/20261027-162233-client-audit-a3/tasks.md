# 实施任务清单：客户端审计 A3：三大件拆分（BattleViewRig / UIBindIndex / HierarchicalStageMachine）

> 本文件由 workflow.py 生成，请使用 `task` 子命令更新，勿手改。

## 任务清单

阶段：已收尾（待归档） ｜ 完成 6/6（取消 0，阻塞 0）

| ID | 任务 | 依赖 | 状态 | 验收标准 | 验证方式 | 证据 |
|---|---|---|---|---|---|---|
| T1 | A3-1a 取件域外提与 Acquire 去重 | - | 完成 | BattleViewRig 无 Acquire*Async；BattleViewAssets 承载全部租约；日志文案逐字不变；编译 0 错 | unity command recompile + recompile_status；grep 'Acquire.*Async' BattleViewRig.cs 无命中；BattleDamageNumberEditModeTests/BattleAudioDriverEditModeTests 通过 | BattleViewAssets 落地；BattleViewRig 无 Acquire*Async/域状态字段（结构探针绿）；Unity recompile 0 错；diff 见 evidence/a3-1-diff.txt |
| T2 | A3-1b 装配域外提与 tick/dispose 相位化 | T1 | 完成 | 主类不再持有 HUD/反馈/视角/租约域字段；相位表逐行保持；Play 冒烟装配+Dispose 正证据；定向战斗表现类通过 | unity recompile；27 个战斗表现定向 EditMode；Play 冒烟（editor_play→RequestExit→find_gameobjects/console） | 五件装配器+权威反馈外提；战斗表现定向 110+8+20+17+20+5 全绿；Play 冒烟 context-created/view-attached/ended reason=Leave、[BattleView]/Hud_Battl… |
| T3 | A3-2 UIBindIndex 三件外提 | - | 完成 | 主类不再持有 _locale/_ltext/_listSources/_listClick/_controlUnsub；公共方法转发完整；UIBind 定向 EditMode 通过 | unity recompile；LTextBind/LTextTypewriter/UiControlListProtocol/LTextWiring EditMode | Unity recompile completed failed=false；LTextBind 9/9、LTextTypewriter 10/10、UiControlListProtocol 5/5、LTextWiring 5/5；di… |
| T4 | A3-3 层级树表示法收回与初始化守卫 | - | 完成 | StageMachineCore 无树字段/BuildTree；层级机持有；构造序注册→建树→校验→审计→OnInit 不变；L1 FSM 全绿 | unity recompile；dotnet test Tests/LiteFramework.Core.Tests 过滤 FSM 相关类 | dotnet test FSM 过滤 81/81 通过；Unity recompile completed failed=false；diff 见 evidence/a3-3-diff.txt |
| T5 | A3 全量验证与结构探针转绿 | T1, T2, T3, T4 | 完成 | AC3/AC4/AC5：编译 0 错、L1+定向通过、Play 装配/Dispose 正证据、结构探针 exit=0 | wf verify；python .workflow/active/client-audit-a3/red-structure-check.py（exit=0）；Play 冒烟命令序列 | wf verify 8/8 exit=0（verify.json）；定向 target-*.txt（Battle111+70/UIBind29/FSM81）；Play 冒烟 attach185/leave208、对象销毁、无 NRE；结构… |
| T6 | A3 审计回写与批次台账 | T5 | 完成 | AC6/AC7：回写完成、doc-lint 0、每子批差集快照无交集 | python scripts/gate/doc-lint.py；git diff 核对登记/归档/批次计划/进度卷；evidence/a3-N-diff.txt | 5 条登记删除/归档 4+1；批次计划 A3 5/A5 9/未结清 122；审计线第5章；doc-lint 0/0；docs commit 34a4d869；代码 commit f18c2af1/e30297b8/30b006b3 |

## 任务详情

### T1 · A3-1a 取件域外提与 Acquire 去重

- 描述: 新增 BattleViewAssets（含泛型 Acquire 助手），BattleViewRig 只委托取件；保持逐项降级、材质整组、音效逐条、HUD 孤儿清理语义。

- 文件: Assets/LiteGame/App/Presentation/Battle/BattleViewAssets.cs, Assets/LiteGame/App/Presentation/Battle/BattleViewRig.cs

### T2 · A3-1b 装配域外提与 tick/dispose 相位化

- 描述: 新增 BattleViewSceneAssembler/BattleHudAssembler/BattleFeedbackAssembler/BattleLookController/AuthoritativeCombatFeedback；BattleViewRig 按相位表驱动，行为零变化。同步 FeatureDebugTunables/LookTuning 常量引用。

- 文件: Assets/LiteGame/App/Presentation/Battle/BattleViewSceneAssembler.cs, Assets/LiteGame/App/Presentation/Battle/BattleHudAssembler.cs, Assets/LiteGame/App/Presentation/Battle/BattleFeedbackAssembler.cs, Assets/LiteGame/App/Presentation/Battle/BattleLookController.cs, Assets/LiteGame/App/Presentation/Battle/AuthoritativeCombatFeedback.cs, Assets/LiteGame/App/Presentation/Battle/BattleViewRig.cs, Assets/LiteGame/App/Debug/FeatureDebugTunables.cs, Assets/LiteGame/App/Presentation/Battle/LookTuning.cs

### T3 · A3-2 UIBindIndex 三件外提

- 描述: 新增 UIBindLocale/UIBindEvents/UIBindListSource；UIBindIndex 保留控件索引、所有权与受控 API 门面；UnbindAll/BindLocale/SetList 语义不变。

- 文件: Assets/LiteClient/UI/Bind/UIBindLocale.cs, Assets/LiteClient/UI/Bind/UIBindEvents.cs, Assets/LiteClient/UI/Bind/UIBindListSource.cs, Assets/LiteClient/UI/Bind/UIBindIndex.cs

### T4 · A3-3 层级树表示法收回与初始化守卫

- 描述: 树字段/BuildTree 从 StageMachineCore 移入 HierarchicalStageMachine；构造序保持；InitializeCore + EnsureInitialized 守卫（Start/Snapshot/ExportGraph）。

- 文件: Assets/LiteFramework/Scripts/Core/Fsm/StageMachineCore.cs, Assets/LiteFramework/Scripts/Core/Fsm/HierarchicalStageMachine.cs, Assets/LiteFramework/Scripts/Core/Fsm/StageMachine.cs

### T5 · A3 全量验证与结构探针转绿

- 描述: 统一跑 L1、27 个定向 EditMode、wf verify、Play 冒烟、结构探针转绿；留证到 evidence/。

- 文件: .workflow/active/client-audit-a3/evidence

### T6 · A3 审计回写与批次台账

- 描述: 删除 5 条登记、归档各追加 1 行；批次计划 A3 5/A5 9/未结清 122；客户端审计线新章；修改指南 §1.6 同步；doc-lint。

- 文件: Docs/审计/客户端/登记/类设计.md, Docs/审计/客户端/登记/重复实现.md, Docs/审计/客户端/归档/类设计-01.md, Docs/审计/客户端/归档/重复实现-01.md, Docs/审计/客户端/批次计划.md, Docs/施工进度/客户端审计线.md, Docs/修改指南.md

