# 实施任务清单：Account/Match 名称与生命周期纠偏（第二批）

> 本文件由 workflow.py 生成，请使用 `task` 子命令更新，勿手改。

## 任务清单

阶段：已收尾（待归档） ｜ 完成 9/9（取消 0，阻塞 0）

| ID | 任务 | 依赖 | 状态 | 验收标准 | 验证方式 | 证据 |
|---|---|---|---|---|---|---|
| T1 | 最小可编译契约与依赖闭包 | - | 完成 | 生产编译 0 错；新增 MetaLoginService/MetaAccountSession/AccountTicketSource/MetaSessionFacade 真实 public 签名与空实现；AccountReentry 增 async 目标形状（新构造 + LeaveMatchAsync 调用，行为暂沿旧路径）；IRoomFlow.LeaveMatchAsync 桩返回 true；测试工程可编译；不改变现有行为。 | unity --project-path E:\unityProject\Test command recompile；unity --project-path E:\unityProject\Test command recompile_status；dotnet build Tests/LiteFramework.Core.Tests/LiteFramework.Core.Tests.csproj | 契约编译 0 错；AccountReentry 新构造(async LeaveMatchAsync)+旧构造兼容；evidence/t1-contracts.txt |
| T2 | C1 纯改名（生产） | T1 | 完成 | AC1(a)：生产旧名零命中（rg 见 verify）；改名 GUID 不变、无孤儿 meta；不要求 Unity 全量编译（测试程序集由 T9 同步后才绿）。 | rg 生产旧名（期望 0）；rg Battle 侧 class MatchSession/IMatchSessionFactory/MatchSessionFactory（期望 0）；rename_asset 回执 GUID 清单 | 7 文件改名 GUID 不变；生产旧名词边界 0；evidence/t2-rename.txt |
| T3 | 先写完整失败测试（独立测试作者） | T1, T2, T9, T8 | 完成 | L1 只测 AccountReentry 顺序（假端口）；AccountDomain 生命周期（开/关/重登/软硬/ForceReleaseRoot）放 EditMode 真实 MetaSegment/RootScope；401×4/门面切换/LeaveMatchAsync 四终态含 !Started/卸载失败 false/超时/未登录/dev 直入/13 例/rig root 均覆盖；red 五命令各有断言级失败并写 red-manifest。 | python .agents/skills/staged-delivery/scripts/workflow.py --root C:\Users\hunter\.codex\tmp\workflows\account-match-lifecycle-20261109 --run account-match-lifecycle verify --red；python scripts/gate/unity-tests.py --mode editor --filter MetaEntryEditModeTests --timeout 600；--filter RoomFlow --timeout 900；--filter BattleRunPort --timeout 600；--filter MatchLoopLeakEditModeTests --timeout 900；dotnet test Tests/LiteFramework.Core.Tests --filter FullyQualifiedName~AccountDomain | 80 例/50 红；red.json + red-manifest.json + t3-cases.md；编译 0 错 |
| T4 | 账户域、门面与 401 接线 | T2, T3 | 完成 | AC2/AC8：登录开、登出/401/宿主关闭/ForceReleaseRoot 关；软/硬分离；重登恰一次；门面切换；401 一次不自拆；token 不进日志/公开端口（M3 守卫改为 MetaLoginService/MetaAccountSession 并新增断言）；facade 在 AppComposition 装配；M3/ProcedureBoundary 守卫同步；Meta 不引 UI。 | python scripts/gate/test.py --suite L1 --profile PullRequest；python scripts/gate/unity-tests.py --mode editor --filter MetaEntryEditModeTests --timeout 600；python scripts/gate/domain-matrix.py --check；python scripts/gate/arch-check.py --check | 账户域/门面/401；MetaEntry 22/22；L1 11/11；evidence/t4-t5-production.txt |
| T5 | Match/Battle 域与异步离场 | T3, T4 | 完成 | AC3/AC4/AC5：Match 释放恰一次、Battle 前失败释放、无重名、100 局无泄漏；LeaveMatchAsync 四终态（无操作/CleanupTask+Main,Error/!Started/根取消）、卸载失败 false、超时 false；重入 false 仍 ShowLogin；重入顺序通过；IAccountLoginControl 移除 LeaveMatch（离场统一 IRoomFlow）。 | python scripts/gate/unity-tests.py --mode editor --filter RoomFlow --timeout 900；python scripts/gate/unity-tests.py --mode editor --filter MatchLoopLeakEditModeTests --timeout 900；python scripts/gate/unity-tests.py --mode editor --filter BattleRunPort --timeout 600；dotnet test Tests/LiteFramework.Core.Tests --filter FullyQualifiedName~AccountReentryTests | Match/Battle/LeaveMatchAsync；RoomFlow 39/39；evidence/t4-t5-production.txt |
| T6 | 文档回写、门禁单源与债登记 | T4, T5 | 完成 | AC1(c)(d)/AC7：文档语义与代码一致、domain-matrix --check 绿、Doc 0/0。 | python scripts/gate/arch-check.py --check；python scripts/gate/domain-matrix.py --write；python scripts/gate/domain-matrix.py --check；python scripts/gate/test.py --suite Doc | docs 11 文件 + units/domain-matrix + 进度 57 章 + 债 B2-ACCOUNT-PROFILE |
| T7 | 全量回归与交付证据 | T4, T5, T6, T8 | 完成 | AC6：verify.json 覆盖 AC6 全部命令且 exit=0；red-manifest 与 green 证据齐全；git diff/status 证据核对协议/页面契约/UI 零变更；未验证项如实披露。 | python .agents/skills/staged-delivery/scripts/workflow.py --root C:\Users\hunter\.codex\tmp\workflows\account-match-lifecycle-20261109 --run account-match-lifecycle verify | verify 19/19 exit=0；evidence/verify.json；red.json/red-manifest |
| T8 | 门禁配置（red/green 命令） | - | 完成 | config show 列出新增命令；T3 首次 verify --red 的失败来自本批用例（非旧命令/编译/环境）。 | python .agents/skills/staged-delivery/scripts/workflow.py --root C:\Users\hunter\.codex\tmp\workflows\account-match-lifecycle-20261109 --run account-match-lifecycle config show --json（断言含 account-domain-red/meta-entry-red/room-flow-red/battle-red/match-loop-red、room-flow-editmode/meta-entry-editmode） | config 19 commands/5 red；net 过滤 MatchSessionFactoryTests；red 名称已核对 |
| T9 | 测试与守卫机械同步（T2b，独立测试作者） | T2 | 完成 | 只改旧符号/路径，不改断言/期望值/用例数；旧类型名在 Tests/ 与 EditMode/ 零命中；类名/文件随产物改名（MatchSessionFactoryTests），net 过滤同步；Unity recompile 0 错；既有 L1 用例全绿。 | rg -w 旧类型名 Tests Assets/Tests/EditMode（期望 0）；unity recompile；dotnet test Tests/LiteFramework.Core.Tests --filter AccountReentryTests\|M3WiringGuardTests\|SessionBoundaryTests\|ProcedureBoundaryTests | L1 1961 PASS；类名/文件改名 MatchSessionFactoryTests；Unity 0 错 |

## 任务详情

### T1 · 最小可编译契约与依赖闭包

- 描述: 为 401/账户域/门面/LeaveMatchAsync 落最小可编译契约（接口+空实现/NotImplemented），不改行为；补齐 L1/Core 源链接与测试替身所需签名，使测试作者可在现有 API 上写真实断言。禁止把新 API 不存在作为延期理由。

- 文件: Assets/LiteGame/Modules/Meta/Abstractions/Account/, Assets/LiteGame/Modules/Meta/Client/Account/, Assets/LiteGame/Modules/Meta/Abstractions/IRoomFlow.cs, Assets/LiteGame/Modules/Procedure/Flow/ProcedureFlow.cs, Assets/LiteGame/Modules/Procedure/Flow/RoomFlowLifetime.cs, Assets/LiteGame/Modules/Procedure/Flow/AccountReentry.cs, Assets/LiteGame/App/PageServices/, Tests/LiteFramework.Core.Tests/LiteFramework.Core.Tests.csproj

### T2 · C1 纯改名（生产）

- 描述: C1 纯改名（仅生产）：7 文件 + 2 类名同一批 unity-pipeline；同步生产引用/注释/ClientScope 名；测试与守卫符号替换归 T9。

- 文件: Assets/LiteGame/Modules/Session/, Assets/LiteGame/Modules/Battle/, Assets/LiteGame/Modules/Meta/Abstractions/, Assets/LiteGame/Modules/Procedure/

### T3 · 先写完整失败测试（独立测试作者）

- 描述: 独立 dsh 测试作者在契约与改名后写账户域/401/门面/离场顺序/Match-Battle 域真实断言；复用 rig/doubles；只改调用不降断言。

- 文件: Tests/LiteFramework.Core.Tests/AccountDomainTests.cs, Tests/LiteFramework.Core.Tests/Meta/AccountReentryTests.cs, Assets/Tests/EditMode/Meta/MetaEntryEditModeTests.cs, Assets/Tests/EditMode/Flow/RoomFlow/, Assets/Tests/EditMode/Battle/Session/, Assets/Tests/EditMode/Battle/Net/MatchLoopLeakEditModeTests.cs

### T4 · 账户域、门面与 401 接线

- 描述: MetaLoginService + MetaAccountSession + IAccountDomain（Open/Revoke/Close）+ AccountTicketSource + MetaSessionFacade 三组路由 + 401 四调用点离栈调度 + GM 页迁移 + NotConfigured>NotLoggedIn。

- 文件: Assets/LiteGame/Modules/Meta/, Assets/LiteGame/App/AppComposition.cs, Assets/LiteGame/App/PageServices/, Assets/LiteGame/Modules/UI/Pages/PageContext.cs, Assets/LiteGame/Editor/Gm/GMTestModePage.cs, Tests/LiteFramework.Core.Tests/M3WiringGuardTests.cs, Tests/LiteFramework.Core.Tests/ProcedureBoundaryTests.cs

### T5 · Match/Battle 域与异步离场

- 描述: MatchSession 进房创建（Account 子域，交付即 Seal）；BattleDomainFactory 派生 Battle 子域；BattleContext 子 scope 改 Battle；IRoomFlow.LeaveMatchAsync 终态判据 + 超时常量；AccountReentry async（Logout→await→Close→ShowLogin）；dev 直入 Root 旁路。

- 文件: Assets/LiteGame/Modules/Session/, Assets/LiteGame/Modules/Battle/, Assets/LiteGame/Modules/Procedure/, Assets/LiteGame/Modules/Meta/Abstractions/IAccountLoginControl.cs

### T6 · 文档回写、门禁单源与债登记

- 描述: 回写说明 02/03/14/17、DI §6、商业级 §6.2/§7.1、路线图、地图专项、架构图 README、规范 03/04、待办总览；arch/units.json 三处 + domain-matrix --write；技术债 B2-ACCOUNT-PROFILE；施工进度追加一章。

- 文件: Docs/, arch/units.json

### T7 · 全量回归与交付证据

- 描述: 主代理跑全量 RoomFlow/MetaEntry/PageFlow/Core/Net/L1/doc/l0/domain-matrix/toolchain/comment/dir-flat/Unity 编译；整理 red→green 与未验证项；wf verify。

- 文件: .workflow/active/account-match-lifecycle/evidence/

### T8 · 门禁配置（red/green 命令）

- 描述: 主代理独占：更新 .workflow/config.json 的 commands/red_commands，使本批 red 来自新用例、verify 覆盖 RoomFlow/MetaEntry/PageFlow/Core full/BattleSession/BattleRunPort/MatchLoopLeak/comment/dir-flat/Unity 编译。

- 文件: .workflow/config.json

### T9 · 测试与守卫机械同步（T2b，独立测试作者）

- 描述: 纯符号替换：Tests/** 守卫与 EditMode 夹具中的旧名（M3WiringGuardTests、SessionBoundaryTests、ModuleInterfaceOwnershipTests、ProcedureBoundaryTests、CrossModuleContractLocationTests、AccountReentryTests、AccountSessionFactoryTests、RoomFlow/**、Battle/Session|Net/**），只改符号不改断言，与 T2 生产改名同批串行。

- 文件: Tests/LiteFramework.Core.Tests/M3WiringGuardTests.cs, Tests/LiteFramework.Core.Tests/SessionBoundaryTests.cs, Tests/LiteFramework.Core.Tests/ProcedureBoundaryTests.cs, Tests/LiteFramework.Core.Tests/ModuleInterfaceOwnershipTests.cs, Tests/LiteFramework.Core.Tests/CrossModuleContractLocationTests.cs, Tests/LiteFramework.Core.Tests/Meta/AccountReentryTests.cs, Tests/LiteNet.Tests/MatchSessionFactoryTests.cs, Tests/LiteNet.Tests/LiteNet.Tests.csproj, Tests/LiteFramework.Core.Tests/LiteFramework.Core.Tests.csproj, Assets/Tests/EditMode/Flow/RoomFlow/, Assets/Tests/EditMode/Battle/Session/, Assets/Tests/EditMode/Battle/Net/MatchLoopLeakEditModeTests.cs

