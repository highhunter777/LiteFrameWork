# 实施任务清单：客户端模块化（竖向切分 + 装配就地 + 边界机械化）

> 本文件由 workflow.py 生成，请使用 `task` 子命令更新，勿手改。

## 任务清单

阶段：正式施工（implement） ｜ 完成 6/9（取消 0，阻塞 0）

| ID | 任务 | 依赖 | 状态 | 验收标准 | 验证方式 | 证据 |
|---|---|---|---|---|---|---|
| T1 | 批1 框架装配入口与 Events 独立 | - | 完成 | A3/A5：三个 Create 入口存在；Modules/**Composition.cs 无 new ReferencePoolService(/new LogService(/new FileSystem(/new WorldClock(/new EventBus(；EventsComposition 独立产出 EventBus | unity_editor.refresh 0 error + rg 负断言 + L1 | 新增 LiteFramework/Runtime/Framework/{FrameworkInfrastructure,FrameworkClocks}.cs；PlatformInfrastructureModule 改用 Create … |
| T2 | 批1 装配段就地迁移与守卫同步 | T1, T3 | 完成 | A1/A2/A6：Bootstrap/Modules 不存在；Modules/** 13 个 Composition；ContainerModule 在 App/；守卫全绿 L1 全量；CompositionBudgetTests 迁移前红后绿 | wf verify --red（CompositionBudgetTests）+ unity_editor.refresh 0 error + L1 | 12 段就地迁入 Modules/<域>/<域>Composition.cs，Container/Ui 移 App/ 并删 Bootstrap/Modules；DependsOn 改模块名；BridgeCapabilitySet 下沉解环… |
| T3 | 批1 适配器委托注入与能力端口 | T1 | 完成 | A4：三个 new 只在 AppComposition；Modules/** 无 is 具体适配器；首采样前 Camera==null、ComposeAfter 后 ==Camera.main | rg 负断言 + 启动冒烟断言 + L1 | 能力端口 IRenderCameraSource/IAimCameraSink/ILookDevice 落地并替换 4 处 is 具体适配器；新增 App/AppComposition.cs 的工厂委托（ConfigService/New… |
| T4 | 批2 业务域垂直化与 asmdef 合并 | T3 | 完成 | A7/A8/A8b/A9/A10/A10a/A11 | unity_editor.refresh 0 error + wf verify --red（DomainBoundaryTests）+ L1 | 批2 结构落地：Core/Battle 删除；View+Presentation 迁入 Modules/Battle 按职责子目录（Aim/Arena/Characters/Combat/Commands/Hud/Setup）+ Abst… |
| T5 | 批2 对局冒烟用例与命令接线 | T4 | 完成 | A12 | wf verify（含 battle-smoke-playmode） | A12 落地：Assets/Tests/UI/PlayMode/BattleSmokePlayModeTests.cs（Boot.unity 起服→Main→RequestEnter→Battle→IsBuilt/Hud.Root/Cro… |
| T6 | 批3 确认框端口与 Baseline 清空 | T5 | 完成 | A13/A14 | L1（ModuleDependencyDeclTests）+ Play 冒烟 | A13：ModuleDependencyDeclTests.Baseline 清空（最后一笔 UiService <- Container 由确认缝端口化解）并通过；Ui 段不再 Require<FeedbackService>（App/… |
| T7 | 批4 组合根与 Container 拆解 | T6 | 待办 | A15/A15a/A16/A17 | L1 + 定向 EditMode + 手测 + wf verify | - |
| T8 | 批5 外层域边界收口 | T7 | 待办 | A18~A28 | L1 + wf verify（含 domain-matrix）+ 纪律扫描 | - |
| T9 | 全批 文档回写与收尾 | T8 | 待办 | A29/A30/A31 | wf verify | - |

## 任务详情

### T1 · 批1 框架装配入口与 Events 独立

- 描述: LiteFramework 提供 FrameworkInfrastructure.Create/Clocks.Create/Events.Create；新增 Modules/Framework/EventsComposition.cs；ClocksComposition 不再 new EventBus。

- 文件: Assets/LiteFramework/Runtime/**, Assets/LiteGame/Modules/Framework/**, Assets/LiteGame/App/Bootstrap/Modules/ClocksModule.cs

### T2 · 批1 装配段就地迁移与守卫同步

- 描述: 12 个 Module 按域迁到 Modules/<域>/<域>Composition.cs（MoveAsset，meta 随迁）；ContainerModule 移 App/；删 Bootstrap/Modules；更新 6 文件 7 处守卫路径、双根基、扫描面与类声明身份解析、152 行键删除；新增 CompositionBudgetTests 并先取红。

- 文件: Assets/LiteGame/App/**, Assets/LiteGame/Modules/**, Tests/LiteFramework.Core.Tests/**, Assets/Tools/DisciplineScanner/Scripts/DisciplineScanner.cs

### T3 · 批1 适配器委托注入与能力端口

- 描述: 新增 App/AppComposition.cs 以工厂委托产出适配器；GameEntry 注入委托、段内调用；新增 IRenderCameraSource/IAimCameraSink/ILookDevice 并替换 4 处 is 具体适配器；InputComposition 去 Presentation；瞄准相机 ComposeAfter 迟绑定。

- 文件: Assets/LiteGame/App/AppComposition.cs, Assets/LiteGame/App/GameEntry.cs, Assets/LiteGame/Modules/Camera/Abstractions/**, Assets/LiteGame/Modules/Input/Abstractions/**

### T4 · 批2 业务域垂直化与 asmdef 合并

- 描述: Core/Battle 端口下沉；View+Presentation+Hud 迁入 Modules/Battle 按职责子目录；拆 CameraComposition；三 asmdef 合一并删两个；Battle AssemblyInfo（4+1 friend）；允许表 13 条 + 硬边放行 Adapters.Unity；3 个预算键随迁；DomainBoundaryTests 先红。

- 文件: Assets/LiteGame/Core/Battle/**, Assets/LiteGame/Modules/Battle/**, Assets/LiteGame/Modules/View/**, Assets/LiteGame/Modules/Presentation/**, Tests/LiteFramework.Core.Tests/**

### T5 · 批2 对局冒烟用例与命令接线

- 描述: 新增 BattleSmokePlayModeTests（Boot 起服 + TestModeRuntime 进出局 + 观测面断言 + 容器级归零）；BattleViewRig 内部访问器、BattleHudAssembler.Root、工厂 LastCreated 装饰器与接口键注册；写入 commands。

- 文件: Assets/Tests/UI/PlayMode/**, Assets/LiteGame/Modules/Battle/**, .workflow/config.json

### T6 · 批3 确认框端口与 Baseline 清空

- 描述: 新增 IConfirmDialog 与 ConfirmDialogRouter；UiComposition 去 Require<FeedbackService>；Baseline 清空。

- 文件: Assets/LiteGame/App/Bootstrap/Contracts/**, Assets/LiteGame/Modules/Ui/**, Tests/LiteFramework.Core.Tests/ModuleDependencyDeclTests.cs

### T7 · 批4 组合根与 Container 拆解

- 描述: ClientHost.ComposeRoot；Container 拆 Procedure/Session/Meta + MapComposition + AppComposition.ComposeAfter；归属表迁移产物键/Tick 单点 13 项/释放/开发装饰/装订/ITransportFactory/ProductSettings 单实例；Container 不再 IClientModule。

- 文件: Assets/LiteFramework/Runtime/Modules/Hosting/ClientHost.cs, Assets/LiteGame/App/**, Assets/LiteGame/Modules/Procedure/**, Assets/LiteGame/Modules/Session/**, Assets/LiteGame/Modules/Meta/**, Assets/LiteGame/Modules/Map/**

### T8 · 批5 外层域边界收口

- 描述: Session 建域、Map 收编、Ui 纯化、Meta 契约与 IAppFlow、8 共享域命名空间、口径单源、瞄准改名、domain-matrix 脚本与文档、错位件归位、Procedure 去业务直引。

- 文件: Assets/LiteGame/Modules/**, scripts/gate/domain-matrix.py, Docs/说明/18-域间引用矩阵.md

### T9 · 全批 文档回写与收尾

- 描述: 同步 02/17/18/03/框架总设计/设置专项/热更专项/技术债/进度卷；补 test-plan 新增业务域步骤；wf verify 九条门禁 + Unity 0 error。

- 文件: Docs/**, .workflow/active/client-modularization/**

