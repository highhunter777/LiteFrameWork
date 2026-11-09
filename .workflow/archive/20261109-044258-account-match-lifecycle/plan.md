# 方案：Account/Match 名称与生命周期纠偏（第二批）

> 运行 account-match-lifecycle · 2026-11-09（Asia/Hong_Kong）。v7：按用户验收确认（401 接线、Meta 会话迁入 Account、只做自动化）与五轮独立评审收口。评审记录见 `reviews/plan-review.md`。

## 背景与目标

第一批把进房/离房编排收口到 `IRoomFlow`/`RoomFlowLifetime`，把「Account/Match 名称和生命周期的全量纠偏」留给第二批。现状与权威设计《商业级通用客户端框架总设计》§6.2、DI §6 不一致：

- Account 现状是**每房间连接**（`AccountSessionFactory.Create` 在 `ProcedureMatch` 建 `"Account"` scope + 服务域，`ProcedureBattle` 清理释放）；Match 是**局内服务域**（Battle 才 `CreateMatchServices()`），`BattleContext` 再自建 `"Match"` 子域 → `Root→Account→Match→Match`；
- 登录态（AccountId/令牌）与 `IMetaSession` 是根级单例（`MetaEntryService` 私有字段 + `AppComposition` 一次性构造），不在登录域；401 只在 `MetaEntrySession.GuestLoginAsync`（不带 bearer）收口，令牌调用点不触发重入；
- 命名混用：per-room 的 `AccountSession`/`AccountSessionFactory`/`IAccountRuntime` 与登录控制的 `IAccountSessionControl`；后者还含 `LeaveMatch`；仓库已有 `IRoomSessionFace/Bridge`。

目标（可观察）：
1. **树与两张权威表一致**：`Root → Account（登录→登出）→ Match（进房→离场/结算）→ Battle（本局）`；Account/Match 各一个服务域；Battle 是 Match 子域。
2. **账户域是真正登录域**：登录成功开启，持 AccountId/令牌/per-account 账户会话/票据源与 Account 服务域；登出/401/宿主关闭关闭；页面经 root 稳定门面读“当前账户”，不重建。
3. **401 收口**：账户会话的 4 个令牌调用点 `IsAuthLost` → 账号重入（一次、幂等、不递归、不在自身栈上关域）。
4. **命名无歧义**：per-room“连接＋匹配域”统一 Match；本局子域 Battle；登录控制只叫 Account 登录；全仓无 per-room 语义旧名。
5. **行为**：除 401 收口外与第一批一致；本地测试直入保留；协议/页面/UI 布局不变；真实 UI 本批不做（用户确认只做自动化）。

## 需求澄清与假设

- 用户已确认：AC 按 v4 通过；**401 本批接线**；**Meta 会话本批迁入 Account（方案 C）**；**真实 UI 降为自动化**。
- A1 树形：`Root→Account→Match→Battle`；Account 归 Meta，Match 归 Session，Battle 归 Battle。
- **A2 分层边界（v6 关键）**：`IAccountDomain` 住 Meta，**只暴露 Meta 本地类型**：`IMetaAccountSession Account`、`ServiceContainer Services`、`ClientScope Scope`、`bool IsOpen`、`string AccountId`、开合方法；**不引用 `IMetaSession`（UI）**，避免 Meta→UI 新程序集边。
- **A3 三个门面（v6 关键）**：
  - `MetaLoginService`（root，Meta）：游客登录 + 部署事实（IsConfigured/ServerUrl/BuildHash/TestRoomTemplate 常量单源）；401 不在登录路径触发。
  - `AccountTicketSource`（root，Meta，实现 `IRoomTicketSource`）：转当前 `IMetaAccountSession`，无账户返回 `NotLoggedIn`；`ProcedureFlow` 只持这个稳定门面。
  - `MetaSessionFacade`（root，App，实现 `IMetaSession`）：按三组路由——登录/部署事实 → `MetaLoginService`；账户令牌面 → `IAccountDomain.Account`（转 MetaResult）；流程面（QuickEnter/EnterRoom/CreateRoom/LeaveMatch）→ `IRoomFlow`。页面不重建。
- **A4 401**：只有 `IMetaAccountSession` 的有 bearer 调用点触发；`IsAuthLost` 时先构造失败结果、再 `UniTask.Yield()` 调度 `IAccountReentry`（不在自身栈上关域），调用返回后不再触碰 `this`。登录路径（无 bearer）不接 401。
- **A5 关闭时序（v6 关键）**：保持既有 **Logout → LeaveMatch → ShowLogin** 顺序（不破坏 `AccountReentryTests` 与恢复抑制时序）；`Logout` 为**软关闭**（抑制恢复、失效在途操作、标记未登录、取消账户请求），**硬释放 Account 域延后到 Match 清理完成之后**：新增 `IRoomFlow.LeaveMatchAsync(ct)`（`UniTask<bool>`，完成 = 操作清理/释放完成或无可离场），`AccountReentry.ReturnToLoginAsync` 改 async：`Logout` → `await LeaveMatchAsync` → `IAccountDomain.Close` → `ShowLogin`；`_returning` 跨 await 保持 true。
- **A6 Match/Battle 容器**：`MatchSession` 进房创建（Account 子域；dev 直入父域 `RootScope/RootServices`），服务域**交付即 Seal**（保留 `AccountSessionFactoryTests:91` 断言）；`BattleContext` 子 scope 改 `"Battle"`，由 `IBattleDomainFactory` 从 Match 服务域派生；不新增未 Seal 窗口。
- **A7 开发直入**：`testRoom && DevBuildFacts.IsDevelopment` → 父域 Root、不要求登录；不新增开发宏。
- **A8 命名**：per-room/匹配域 Match；局内子域 Battle；登录控制 `IAccountLoginControl`（AccountId/IsLoggedIn/Logout；离场复用 `IRoomFlow`）。
- A9 账户容器只登记 **Meta 本地接口**（`IMetaAccountSession`），**不得**再登记 `IRoomTicketSource`/`IMetaSession`（父域已有 → 子域覆盖会抛 `InvalidOperationException`，`ServiceContainer.cs:384-385`）。

## 范围与非目标

- 范围内：
  1. **命名矩阵**（含类名）：`IAccountSession/AccountSession`→`IMatchSession/MatchSession`；`IAccountSessionFactory/AccountSessionFactory`→`IMatchSessionFactory/MatchSessionFactory`（Session）；Battle `MatchSession/MatchSessionFactory/IMatchSessionFactory`→`BattleDomain/BattleDomainFactory/IBattleDomainFactory`；`IAccountRuntime`→`IMatchRuntime`；`IAccountSessionControl`→`IAccountLoginControl`。
  2. **账户域**：`IAccountDomain`（Meta 本地接口）+ `AccountDomain`/`AccountContext`/`AccountCloseReason`；`MetaLoginService`、`IMetaAccountSession`/`MetaAccountSession`、`AccountTicketSource`；`MetaSegment` 持有 domain 单实例并登记 RootScope；登录成功 Open、登出/401/宿主关闭关闭。
  3. **门面**：`MetaSessionFacade`（App）三组路由；`PageContext.Meta` 指向它；页面不重建。
  4. **401**：账户会话 4 个令牌调用点 + 调度/去重/边界用例。
  5. **Match/Battle 域**：`MatchSession` 进房创建（交付即 Seal）；`BattleDomainFactory` 派生 `"Battle"`；`BattleContext` 子 scope 改名；`IRoomFlow.LeaveMatchAsync`。
  6. **执行面**：7 文件改名 + 2 类改名（同批）+ 2 新目录，全部走 unity-pipeline；测试/守卫/csproj/文档/domain-matrix/债登记同步。
- 非目标：不改协议/Sim/Net/Sync/表源/资源收集；不改页面布局/UI 契约/路由；不虚构 Profile/RemoteConfig（无实现，只留登记点）；不搬模块目录；不改 `BattleContext` 字段/构造语义（仅子 scope 名）；真实 UI 不做。

## 现状与约束

- 相关代码：见 v5 评审输入清单（Session/Battle/Procedure/Meta/App/UI 端口/框架容器/测试/asmdef）。
- 约束：
  - `Assets/` 改名走 unity-pipeline，**7 文件 + 2 类名同一批**（避免 Session/Battle 短暂同名 CS0433）；GUID/孤儿 meta 核对；pipeline 不可达按规范 02 §1.10 标注。
  - `MetaEntryService.cs` 拆分为 `MetaLoginService.cs` + `MetaAccountSession.cs`（+ `IMetaAccountSession.cs`、`AccountTicketSource.cs`）；`MetaEntrySession.cs` 由 `MetaSessionFacade.cs` 取代；`GMTestModePage.cs` 改经 `IAccountDomain`（root 容器不再有 MetaEntryService）。
  - 测试面：`RoomFlowRig.cs`/`RoomFlowTicketTests.cs` 需 root scope + root container + 门面（`AccountSessionFactoryTests.NewRoot` 同型）；`MetaEntryEditModeTests` 13 例拆成 loginService + Open + accountSession 双对象（断言不变，装配变）；`AccountReentryTests` 改 async 顺序断言。
  - csproj：LiteNet 5 条 Session include（含 `IAccountRuntime`）；Core.Tests +4 条（`IAccountDomain/AccountDomain/AccountContext/IRoomFlow`），若签 `LeaveMatchAsync` 还要 `IRoomFlow` 已含。
  - 文档：`说明 02/03/14/17/18(生成)`、`规范 03/04 §6.10`、`说明/架构图/README`、`待办总览`、`DI §6`、`商业级 §6.2/§7.1`、`路线图 §4.2`、`地图数据专项`；语义回写（说明 03:76、商业级 §7.1:202、说明 17:1006、规范 03:51）；`施工进度` 只追加一章。
  - `ScanTargets`/`Tests.slnx` 不改。

## 候选方案与取舍

| 方案 | 核心 | 结论 |
|---|---|---|
| A 只改名 | 小，生命周期不动 | 不采用 |
| B 改名+账户作用域，Meta 会话留 root | 中 | 不采用（用户选 C） |
| **C（用户确认）** | B + per-account 账户会话/票据源 + root 门面 + 401 + Match/Battle 域 | 采用 |
| D C+真实 UI | 用户确认只做自动化 | 不采用 |

提交切分：**C1 纯改名**（7 文件+2 类名，保全部断言）；**C2 账户域/门面/401/Match/Battle**。

## 设计要点

### 命名矩阵（含类名）

| 现名 | 新名 |
|---|---|
| `IAccountSession` / `class AccountSession` | `IMatchSession` / `class MatchSession` |
| `IAccountSessionFactory` / `class AccountSessionFactory`（Session） | `IMatchSessionFactory` / `class MatchSessionFactory` |
| Battle `class MatchSession` / `MatchSessionFactory` / `IMatchSessionFactory` | `BattleDomain` / `BattleDomainFactory` / `IBattleDomainFactory` |
| `IAccountRuntime` | `IMatchRuntime` |
| `IAccountSessionControl` | `IAccountLoginControl` |
| `ClientScope` 名 per-room `"Account"`→`"Match"`；BattleContext 子 `"Match"`→`"Battle"` | — |

### 作用域/服务域与门面

```
RootScope（进程）
├─ Root ServiceContainer
│   ├─ MetaLoginService（登录+部署事实；IMetaConfigFacts）
│   ├─ AccountTicketSource : IRoomTicketSource（root 稳定门面）
│   └─ MetaSessionFacade : IMetaSession（App，root 稳定门面；三组路由）
└─ Account（登录→登出）             ← IAccountDomain 单实例登记 RootScope
   ├─ ServiceContainer("Account")   ← 只登记 IMetaAccountSession（Meta 本地接口）
   ├─ AccountContext（AccountId+令牌）
   └─ Match（进房→离场/结算）        ← MatchSession；产品父域=Account（dev 直入=Root）
      ├─ ServiceContainer("Match")  ← 交付即 Seal
      └─ Battle（本局）             ← IBattleDomainFactory 派生
         ├─ ClientScope("Battle")
         └─ ServiceContainer("Battle") ← BattleContext/EventBus/FrameDriver
```

### 关键流程

1. **登录**：`MetaSessionFacade.GuestLoginAsync` → `MetaLoginService.GuestLoginAsync` → `IAccountDomain.Open(accountId, token)`（创建 `MetaAccountSession` 并登记 Account 容器）→ 返回成功。
2. **401**：`MetaAccountSession` 的 4 个令牌调用 `IsAuthLost` → 构造失败 → `Yield` 调度 `IAccountReentry.ReturnToLoginAsync`（一次）→ 返回；不在自身栈关域。
3. **重入**：`AccountReentry`（async）：`IAccountLoginControl.Logout`（软关闭：抑制恢复/失效在途/标记未登录）→ `await IRoomFlow.LeaveMatchAsync`（等清理/释放）→ `IAccountDomain.Close`（硬释放 Account）→ `ShowLogin`；`_returning` 跨 await 保持。
4. **宿主关闭**：`MetaSegment.ShutdownAsync` → `Close(Shutdown)`；`ForceReleaseRoot` 由 domain 登记 RootScope 兜底。
5. **进房（产品）**：账户未开 → `NotLoggedIn`，零连接；已开 → `IMatchSessionFactory.Create(account.Scope, account.Services, ...)` → JoinAck → 移交 Battle；失败/取消释放恰一次。
6. **进房（dev 直入）**：`testRoom && DevBuildFacts.IsDevelopment` → 父域 `RootScope/RootServices`。
7. **局内**：Battle → `IBattleDomainFactory.Create(matchRuntime, rules)` → `"Battle"` 域 → `BattleSession` → 清理释放。
8. **离房/结算**：Battle 清理 → `MatchSession.Dispose()` → 回大厅/结算。

### 失败与边界

- Open 失败：释放新 scope/容器/上下文，保持未登录、可重登。
- Close 失败：聚合异常不抛；测试断言 `DisposeFailures`。
- 重复 Logout/Close/401：软关闭幂等；`_returning` 跨 await；`ShowLogin` 记忆位防同帧双导航。
- 未登录产品进房：`NotLoggedIn`、零连接；dev 直入不受影响。
- 401 不在自身栈：调度后不得再触 `this`；用例覆盖“调用返回前 Account 未释放 / 释放发生在清理后”。
- Battle 前失败：Match 容器已 Seal，直接释放；`OwnedCount==0`/`IsDisposed`。
- `ForceReleaseRoot`：domain 登记 RootScope → 释放当前 Account。

## 通用性与扩展性

- 根因：生命周期由创建/释放点定义；本批把 Account/Match/Battle 所有权写死，401 单点收口。
- 扩展点：Account 服务域登记 Profile/RemoteConfig（当前无实现）；Match 域连接资源；Battle 子域局内服务。
- 无硬编码：`AccountCloseReason`、`TestRoomTemplate` 单源；`NotLoggedIn` 失败码/文案单源。

## 执行面（Unity 资产）

1. 同一批改名 7 文件 + 2 类名：`IMatchSession`、`IMatchSessionFactory`、`MatchSessionFactory`（Session）、`IMatchRuntime`、`BattleDomainFactory`、`IBattleDomainFactory`、`IAccountLoginControl`；类 `AccountSession→MatchSession`、Battle `MatchSession→BattleDomain`。
2. 新增文件：`Meta/Abstractions/Account/{IAccountDomain,IMetaAccountSession,AccountCloseReason}.cs`、`Meta/Client/Account/{AccountDomain,AccountContext,MetaLoginService,MetaAccountSession,AccountTicketSource}.cs`、`App/PageServices/MetaSessionFacade.cs`。
3. 文件拆分：`Meta/Client/MetaEntryService.cs` → 登录/账户两件（删除原文件）；`MetaEntrySession.cs` → `MetaSessionFacade.cs`（删除原位）。
4. 目录：`Meta/Abstractions/Account/`、`Meta/Client/Account/`；GUID/孤儿 meta 核对。

## 测试策略

- **前置小步**：先落最小可编译契约（`IAccountDomain/IMetaAccountSession/IAccountLoginControl/IMatchSession/IMatchRuntime/IBattleDomainFactory/LeaveMatchAsync`），再跑红。
- 落点：L1（`AccountDomain` 纯逻辑 + `AccountReentry` async 顺序 + 401 触发计数；+4 源链接）；Unity EditMode（`MetaEntryEditModeTests` 13 例改双对象；`RoomFlow` 26 项 + rig root 容器；`MatchLoopLeak`；`BattleRunPort`；`BattleSession`）。
- 用例：登录开域/登出关域/重登恰一次/门面切换旧会话不可读/`ForceReleaseRoot`/Open 失败/401×4 一次且不在自身栈/未登录拒绝/dev 直入/Match 交付即 Seal + 释放恰一次/Battle 派生与无重名/令牌不进日志与公开端口/重入顺序（Logout→await Leave→Close→ShowLogin）。
- 红→绿：`wf verify --red` 真实断言失败；测试/生产作者分离；只改调用不降断言。
- 真实 UI：不做。

## 验收标准

- [ ] **AC1 命名与位置**：(a) `rg` 旧名零命中（生产，含类名）；Battle 侧 `class MatchSession|IMatchSessionFactory|MatchSessionFactory`=0；(b) 测试/守卫/csproj 清单同步（数量以 `rg` 命令为准，AC 内写命令）；(c) 文档语义回写清单；(d) `arch/units.json` 三处 + `domain-matrix --check`；(e) 豁免（施工进度历史、`ClientScope("Account")` 示例、Account 域名）。
- [ ] **AC2 账户域**：登录开（`MetaAccountSession` 登记 Account 容器）；登出/401/宿主关闭/`ForceReleaseRoot` 关；重登旧域恰一次；门面切新、旧会话不可读；软关闭/硬释放分离且顺序可测；令牌不进日志与公开端口；不自拆。
- [ ] **AC3 Match 域**：进房创建、交付即 Seal、释放恰一次；失败/取消释放；Battle 前失败直接释放；清理完成前不放行；RoomFlow 26 项绿。
- [ ] **AC4 Battle 子域**：`BattleContext` 只在 `"Battle"` 容器解析、随清理释放；100 局无泄漏；无重名（新断言）。
- [ ] **AC5 行为**：除 401 外与第一批一致（自动化 + diff）；重入顺序 `Logout→LeaveMatchAsync→Close→ShowLogin` 有顺序用例；协议/页面/UI 不变。
- [ ] **AC6 自动验证**：先 red 后 green；RoomFlow/MetaEntry/PageFlow/Core/Net 全绿；L1/doc/l0/domain-matrix/toolchain/comment/dir-flat/Unity 编译全过（L1 计数不写死）。
- [ ] **AC7 文档一致**：DI §6 + 商业级 §6.2 与代码一致（Account/Match 各一服务域；Battle 子域；Scene/UI 保留）；Meta 会话入 Account；Profile/RemoteConfig 无实现只留登记点；说明 02/03/14/17 与进度卷回写；Doc 门禁通。
- [ ] **AC8 接线与边界**：`IAccountDomain` 消费者（MetaSegment/门面/GM 页）与释放者（MetaSegment/RootScope）；401 有真实调用点；**不新增程序集边**（Meta 不引 UI）；无 public static 可变；arch/静态/接口归属守卫绿。

## 债登记

- Profile/RemoteConfig 无实现、Account 服务域暂空登记点：`Docs/审计/客户端/登记/技术债.md`，`B2-ACCOUNT-PROFILE`。
- 401 本批已接线，不登记为债。

## 风险与回滚

- 风险：①改名/拆分遗漏（MetaEntryService 拆两件、GM 页、测试 rig）；②软关闭/硬释放顺序错→use-after-dispose；③401 同帧/异步双触发；④子域覆盖注册抛错；⑤Meta→UI 程序集边；⑥13 例 Meta 测试重写风险；⑦`LeaveMatchAsync` 新异步面。
- 监控：域开合/顺序/401/强制释放用例、RoomFlow/MetaEntry/MatchLoopLeak、L1/Unity 编译、domain-matrix/M3/SessionBoundary。
- 快照：`.workflow/active/account-match-lifecycle/evidence/pre-implementation/` + manifest；改名回滚反向 pipeline。
- 降级：C2 风险超预期 → 停止施工 → 重新评审 → 重新提审。
- 提交切分：C1 改名 / C2 账户域+401+Match/Battle。

## v7 修订（按 v6 聚焦复核的最小修复集）

> 复核结论：B1/B2/B3 已关闭；B4/B5 各留 1 个新阻塞；另 3 条 major。以下为逐条修正，正文其余部分继续有效；冲突处以本节为准。

### N1 测试/守卫/csproj/注释同步面（B5）

- **`M3WiringGuardTests.cs`**：`:68` 读 `Meta/Client/MetaEntryService.cs`（本批删除）→ 改读 `Meta/Client/Account/{MetaLoginService,MetaAccountSession}.cs`；`:72/:74/:89` 的 `IAccountSessionControl` 断言改 `IAccountLoginControl`；`:108` 的 `new MetaEntrySession(...)` 断言改 `MetaSessionFacade` + `IAccountDomain`。
- **`SessionBoundaryTests.cs:29`**：路径改 `Session/MatchSessionFactory.cs`；`:26` 方法名同步。
- **`ModuleInterfaceOwnershipTests.cs:26`**：`SeamExceptions` 路径 `IMatchSessionFactory.cs` → `IBattleDomainFactory.cs`（仍返回 `BattleDomain`，继续登记）。
- **`CrossModuleContractLocationTests.cs:107`（测试源码链接死链）**：`Tests/LiteFramework.Core.Tests.csproj:47-48`、`Tests/LiteNet.Tests.csproj:42-44,:50` 全部随改名更新；改完全仓 `rg` 无旧路径。
- **L1 源链接（修正版）**：Core.Tests 增 `IAccountDomain.cs`、`IMetaAccountSession.cs`、`AccountCloseReason.cs`、`IRoomFlow.cs`；**不链** `AccountDomain.cs`（其闭包含 `MetaAccountSession`/`IMetaClient`，L1 测试改用测试替身实现 `IAccountDomain`，纯逻辑用接口 + 假实现）。LiteNet.Tests 5 条（含 `IAccountRuntime → IMatchRuntime`）。
- **生产注释旧名**：`IMatchJoin.cs:8`、`ProcedureArgs.cs:18`、`IAccountRuntime.cs:8`；语义过期注释 `ProcedureSegment.cs:84`、`ProcedureBattle.cs:195`（“Account Scope”→“Match Scope”）、`AccountSessionFactory.cs:50`。
- **AC1(b) 固定清单**：`rg` 口径与命令写进 AC（见 AC1 更新）。

### N2 `LeaveMatchAsync` 终态判据（B4）

- 契约：`UniTask<bool> IRoomFlow.LeaveMatchAsync(CancellationToken ct = default)`；语义 = “先调同步 `LeaveMatch()`（有无在途/房间），再等待到**终态**”；返回 `true` 表示可安全关闭 Account，`false` 表示仍有未完成清理或超时。
- 终态判据（必须写死）：
  1. 无当前操作（`_lifetime.Current == null`）→ true；
  2. `CleanupTask` 已完成 **且** 阶段机 `Current ∈ {Main, Error}` 或 `!Started` → true；
  3. 根取消/`_disposed` → true（由根关闭兜底）；
  4. 卸载失败分支（`CleanupDone==false` 且 `Main`）→ **false**；调用方（重入）不得硬关 Account，保留 busy 并返回失败（不覆盖既有 fatal 语义）。
- 实现只**观察**，不新增释放者：复用 `ReleaseAfterCleanupAsync` 作为唯一释放者；`LeaveMatchAsync` 用 `await operation.CleanupTask` + 轮询阶段终态/`RootToken`，带 `accountReentryLeaveTimeout`（如 10s，常量）上限；超时返回 false。
- 同步 `LeaveMatch()` 保留给页面（`IMetaSession.LeaveMatch`）；契约写明 `LeaveMatchAsync` = `LeaveMatch()` + 等待终态，避免两条竞态路径。
- `AccountReentry`：`Logout`（软）→ `await LeaveMatchAsync` → 若 true 则 `Close` 硬释放 → `ShowLogin`；false 则记录并仍 `ShowLogin`（保留 busy，不硬关）。

### M-a 软关闭 API（B3 补）

- `IAccountDomain` 增 `void Revoke(AccountCloseReason reason)`：标记未登录、`Cancel()` 账户 scope 令牌、停止接受新请求；**不释放** `Account` 容器/上下文。
- `IAccountDomain.Close(reason)`：`Revoke` + 释放 Account scope/容器/上下文（幂等，可重复调用）。
- `AccountTicketSource.Logout(reason)` 调 `Revoke`；`AccountReentry` 在 `LeaveMatchAsync` 返回 true 后调 `Close`；`MetaSegment.ShutdownAsync`/`ForceReleaseRoot` 调 `Close`。
- `IMetaAccountSession` 成员写死：`AccountId`、`IsLoggedIn`、`ListRoomsAsync`、`RequestJoinTicketAsync`、`CreateRoomTicketAsync`、`ListRecordsAsync`、`Logout`（软）。

### M-b GM 页与 not-configured 优先级（M2/M7）

- `GMTestModePage.cs`：`IsConfigured`/`TestRoomTemplate` → `MetaLoginService`；`AccountId` → `IAccountDomain`；建房 → `IAccountDomain.Account.CreateRoomTicketAsync(..., template, gmKey)`；执行面必须列该文件。
- `ConfigureTestRoom(template, gmKey)` 状态归属 **`MetaAccountSession`**（每账户，登录时由部署注入）；`MetaLoginService` 只出部署事实。
- 失败优先级：**NotConfigured > NotLoggedIn**；门禁放 `AccountTicketSource`/`MetaSessionFacade`（root 无账户时先判配置再判登录），`MetaEntryEditModeTests` 的 4 条 NotConfigured 断言按此保。
- `PageFlow.ShowLogin` 二次导航窗口：`AccountReentry` 增 `_navigating` 记忆位，保持到 `_surface.IsLoginOpen` 为真或下一帧；不依赖 `ShowLogin` 同步返回。

### M-c 其他

- `IAccountDomain` 注册分两步：`context.Put<IAccountDomain>`（服务解析）+ `RootScope.Register(domain)`（释放兜底），正文 A2 的“单实例登记 RootScope”按此理解。
- `SessionSegment.cs:52` 构造点与 `IMatchSessionFactory.Create` 的 `token` 形参随改名/账户化调整，列入执行面。
- `ProcedureBoundaryTests` 禁列加 `MetaLoginService`/`MetaAccountSession`（保持守卫意图）；`NotLoggedIn` 失败码/文案提静态单源（`MetaFailure`/常量），不复制字符串。
- `MetaEntryEditModeTests` 口径改为“装配改、断言语义不降”（13 例全量重写装配为 loginService+Open+accountSession），不再声称“只改调用”。

### 收口条件

- 上述 N1/N2/M-a/M-b/M-c 全部落进方案与执行面后，v6 复核的 2 个阻塞 + 3 个 major 即关闭；本方案据此定稿，进入用户最终确认（不再开新一轮方案评审）。
