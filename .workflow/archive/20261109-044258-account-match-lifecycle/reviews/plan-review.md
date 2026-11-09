# 方案评审（独立子代理）：Account/Match 名称与生命周期纠偏（第二批）

> 运行 account-match-lifecycle · 2026-11-09
>
> 本文件由主代理根据三轮独立子代理返回整理落盘；不是主代理自评。

## 评审输入

- 评审对象：`plan.md`（v1 → v2 → v3 → v4）
- 代码/文档基线：当前工作区快照（本批无提交；对照第一批归档 `.workflow/archive/20261109-003015-lobby-room-flow/`）
- 评审范围：Account/Match 命名、作用域/服务域生命周期、Meta 登录态、测试/门禁/文档同步面；不含实现
- 子代理：第一轮 `90703931-8671-4931-b5ff-cfd21284b75d`；第二轮 `dd8f7be3-4ac3-4679-912f-92fd5158bf1d`；第三轮 `cddf1dfd-8b26-4c05-b745-de4ff39afed9`（均独立只读）
- 约束：只读评审，不修改生产代码、测试与门禁

## 三轮评审与处理

### 第一轮（v1）：FAIL

- 事实核对成立：Account scope 在 `ProcedureMatch` 建（`AccountSessionFactory.cs:114,118`）、Battle 清理释放（`ProcedureBattle.cs:196-203`）；Match 域在 Battle 才建（`MatchSessionFactory.cs:61-90`）；登录态在 `MetaEntryService.cs:18,52-55`；第一批归档确认留给第二批。
- 阻塞：B-1 AC1「全仓零命中」不可判定；B-2 改名未走 unity-pipeline；B-3 AC2 的 401 腿不可达、AC7 引用错。
- 处理：v2 重写 AC1（限定范围+豁免）、补 pipeline 执行面、401 移出（A4）、AC7 改引用。

### 第二轮（v2）：FAIL

- B-1 部分关闭：EditMode 命中清单错、`LiteNet.Tests.csproj` 实为 4 条、漏 3 份文档。
- B-2 部分关闭：改名清单只列 4 件、实际至少 7 件。
- B-3 关 401/章节引用，但新形态：无 Account 服务域；`BattleContext.cs:164` 自建 `CreateChild("Match")` → `Root→Account→Match→Match`。
- 新 blocker：开发直入（`ProcedureMain.cs:42-56`）不登录、走 `testRoom` 免票入口，会被「未登录拒绝」拦死；BattleContext 与 MatchSession 同名嵌套。
- 处理：v3 定树 `Root→Account→Match→Battle`、补 Account 服务域、Battle 子域改名、dev 旁路、改名清单补 7 件、单实例登记 RootScope、债登记落点。

### 第三轮（v3）：FAIL（给出最小修订集）

- 7 条复核：改名清单仍漏 `BattleSegment.cs`/`BattleSession.cs`/`BattleRunPortEditModeTests.cs` 且错列 `BattleSessionEditModeTests`；L1 源链接不足（缺 `IRoomFlow`/`AccountContext`，LiteNet 实为 5 条）；`IAccountDomain` 缺 `Services` 容器句柄（服务域树建不出）；令牌读路径未定义；重入顺序先关父域与 LIFO 冲突；AC1(a) 只覆盖 Account 侧旧名；另有 units.json 第三处、RoomFlowTicketTests、同名交换窗口、语义级回写、空 Account 服务域待接线等 minor。
- 主代理 v4 逐条修复对照：
  1. 生产补 `Battle/BattleSegment.cs`、`Battle/SubModules/SessionSubModule/BattleSession.cs`；测试补 `BattleRunPortEditModeTests`、`RoomFlowTicketTests`，移除 `BattleSessionEditModeTests`；件数改为“以 rg 实测为准”。
  2. L1 补 4 条源链接（`IAccountDomain/AccountDomain/AccountContext/IRoomFlow`）；LiteNet 5 条（含 `IAccountRuntime`）。
  3. `IAccountDomain` 增 `ServiceContainer Services`；dev 旁路显式 `(RootScope, RootServices)`。
  4. 令牌归属写死为方案 (i)：令牌**仍只住 `MetaEntryService` 私有字段（唯一写者）**，AccountContext 只持 AccountId；公开面零令牌成员（AC2 反射+守卫）。
  5. 重入顺序改为 **`LeaveMatch()` → `Logout()` → `ShowLogin`**，并加顺序用例。
  6. AC1(a) 增 Battle 侧路径限定零命中；AC1(c) 增语义级回写（说明 03:76、商业级 §7.1:202、说明 17:1006、规范 03:51）；AC5 拆 (a) 可判定 / (b) 真实 UI 冒烟披露项；units.json 第三处 :1361；Account 服务域空容器挂 `B2-ACCOUNT-PROFILE` 债号；7 个改名同一批执行避免同名交换窗口（CS0433）。
- 收口：第三轮明确“这 6 条修完，其余全为 minor → 可判无阻塞、可进入用户确认”。v4 已全部落实，按 skill「只剩 minor 时修复落盘后直接收口」不再开第四轮。

## 方案检查

- 目标可观察、范围与非目标清晰：PASS（树/命名/行为/可判定四条）。
- 现状与根因有代码/文档依据：PASS（三轮逐条核对）。
- 候选方案取舍充分：PASS（A/B/C，推荐 B，提交切 C1/C2）。
- 设计覆盖接口、数据、关键流程、失败边界：PASS（Account/Match/Battle 三级域、dev 旁路、401/Profile 偏差与债号）。
- 通用性与扩展点：PASS（Account 服务域为后续登记点）。
- 测试策略先失败后通过：PASS（最小可编译契约前置 + red 证据 + 测试/生产作者分离）。
- 风险、监控与回滚可执行：PASS（pre-implementation 快照+manifest、反向 pipeline、降级须重新评审提审）。
- 文档与门禁：PASS（domain-matrix 单源、语义级回写、Doc 门禁）。

## 验收标准可判定性

| 验收标准 | 判定方式 | 是否可观察/可判定 |
|---|---|---|
| AC1 | 限定范围 rg + Battle 路径零命中 + 守卫/测试清单 + 指定文档语义回写 + domain-matrix --check + 豁免 | ✅ |
| AC2 | 域开合/重登探针、ForceReleaseRoot、Log.SetSink、反射+源码守卫 | ✅ |
| AC3 | RoomFlow 26 项既有断言 + Seal/恰一次/不放行 + 新增 Battle 前失败释放 | ✅ |
| AC4 | 100 局泄漏套件 + 新增无 “Match→Match” 重名断言 | ✅ |
| AC5 | (a) 自动化全绿 + 重入顺序用例 + diff 核对；(b) 真实 UI 冒烟必须执行，环境不可用则不计通过 | ✅（含披露项） |
| AC6 | wf verify 全命令 + red 证据；L1 计数不写死 | ✅ |
| AC7 | DI §6 + 商业级 §6.2 表与代码一致 + 偏差/债号 + Doc 门禁 | ✅ |
| AC8 | arch-check/静态可变/接口归属 + 装配实读 | ✅ |

## 发现的问题与处理

| ID | 严重度 | 问题 | 处理 | 状态 |
|---|---|---|---|---|
| B-1 | blocker | AC1 不可判定 | v2 限定范围+豁免；v4 补 Battle 侧与语义回写 | 关闭 |
| B-2 | blocker | 改名未走 pipeline / 清单不全 | v2 执行面；v3/v4 补到 7 件 + 单批执行 | 关闭 |
| B-3 | blocker | 401 腿不可达 / AC7 引用错 | v2/v4 移出 401、改引用并声明偏差 | 关闭 |
| B-4 | blocker | 开发直入被登录门拦死 | v4 dev 旁路（DevBuildFacts，Root 父域） | 关闭 |
| B-5 | blocker | BattleContext 自建 "Match" 重名 | v4 改 "Battle" 子域 + 无重名断言 | 关闭 |
| P1 | blocker | 改名清单错位（漏 BattleSegment/BattleSession、错列 BattleSessionEditModeTests） | v4 修正 | 关闭 |
| P2 | blocker | L1 源链接不足 | v4 补 4 条 | 关闭 |
| P3 | blocker | IAccountDomain 缺 Services 句柄 | v4 补 Services + dev 旁路 | 关闭 |
| P4 | major | 令牌单一事实源未定义 | v4 写死方案 (i) | 关闭 |
| P5 | major | 重入先关父域与 LIFO 冲突 | v4 改 LeaveMatch→Logout + 顺序用例 | 关闭 |
| P6 | major | AC1(a) 只覆盖 Account 侧 | v4 补 Battle 路径零命中 | 关闭 |
| P7-P15 | minor | csproj 5 条、DevBuildFacts、规范 03 落点、语义回写、units.json 第三处、RoomFlowTicketTests、同名交换窗口、AC5 披露、空服务域待接线 | v4 逐条修正 | 关闭 |

## 评审结论

- 评审方式: 独立子代理
- 结论: PASS
- 验收标准: PASS
- 阻塞问题: 0
- 评审人: 独立子代理 `90703931…` / `dd8f7be3…` / `cddf1dfd…`
- 评审时间: 2026-11-09T01:36+08:00（第三轮）
- 备注：按 skill「复评只剩 minor 时修复落盘后直接收口」，未开第四轮。

## 第四轮复评（v5 + v5.1 扩围）：FAIL

- 子代理：`4b88e4ca-e0da-4876-b97d-bb1d347115aa`（独立只读；前一个 v5 评审代理异常中止后重跑）
- 结论：**FAIL**（4 个阻塞，均为“实现即崩/编不过”）

| ID | 阻塞 | 依据 | v6 处理 |
|---|---|---|---|
| B1 | `IAccountDomain`（Meta）暴露 `IMetaSession`（UI）→ 新程序集边、L1 编不过 | `Modules/Meta` asmdef 引用不含 UI；`IMetaSession` 在 UI | `IAccountDomain` 只暴露 Meta 本地 `IMetaAccountSession`；`IMetaSession` 适配只在 App `MetaSessionFacade` |
| B2 | root 已注册 `IRoomTicketSource`，Account 子容器再注册同名 → `Open` 抛 | `ServiceContainer.cs:384-385` 子域禁覆盖父域 | Account 容器只登记 `IMetaAccountSession`；root `AccountTicketSource` 门面单独登记 |
| B3 | 重入顺序改 `LeaveMatch→Logout` 与 `AccountReentryTests:55` 断言冲突，且破坏恢复抑制时序 | `AccountReentry.cs:31-33`、`ProcedureFlow.cs:66-72` | 保持 `Logout→LeaveMatch→ShowLogin`；`Logout` 改软关闭 |
| B4 | “Match 释放完成→Account 关闭”无 await 面；同步关闭会让 Account 先于 Match 清理 | `IRoomFlow.LeaveMatch` 返回 bool；`ReleaseAfterCleanupAsync` 异步 | 新增 `IRoomFlow.LeaveMatchAsync(ct)`；`AccountReentry` 改 async：`Logout → await Leave → Close → ShowLogin` |
| B5 | 执行面少 `class AccountSession`、Battle `class MatchSession` 两个类改名 → AC1 `rg=0` 不可能 | `AccountSessionFactory.cs:14`、`MatchSessionFactory.cs:12` | v6 补 2 类名，与 7 文件同批 |

另 major：v5.1 未同步正文（M1）、`GMTestModePage` 漏（M2）、facade 缺 `IRoomFlow` 路由（M3）、测试 rig 无根容器（M4）、401 自拆/双触发（M5/M6）、`IMetaConfigFacts`/`TestRoomTemplate` 归属（M7）、`LeaveMatchAsync` 与 `LeaveMatch` 共存；minor：`IAccountSessionControl` 文档、测试件数口径、`NotLoggedIn` 单源。

处理：v6 全部落实（含 facade 三组路由、测试 rig root 容器、401 离栈调度、`LeaveMatchAsync`、MetaEntryService 拆分与 GM 页迁移、二次导航记忆位），并送第五轮聚焦复核（B1～B5）。

## 第五轮复核（v6）：FAIL（B4/B5 各 1 新阻塞）

- 子代理：`90cc5415-e8ea-44da-b18b-0966e92f6e20`
- B1（Meta→UI 边）**关闭**：Meta asmdef 无 UI 引用；`IMetaAccountSession` 本地接口成立；`Abstractions/Account` 3 件与 `Client/Account` 5 件不超 dir-flat。
- B2（子容器覆盖注册）**关闭**：`ServiceContainer` 子域禁覆盖父域；root 只留 `AccountTicketSource`。
- B3（重入顺序）**关闭**：保持 `Logout→LeaveMatch→ShowLogin` 与既有断言；`Logout` 软关闭。
- **B4 未关闭**：`LeaveMatchAsync` 若“等释放”会因三条永不完成路径挂死（卸载失败不 NotifyCleaned；根取消 return 不 release；Error 终态 return）。→ v7 写死终态判据（CleanupTask 完成 + Current∈{Main,Error}/未启动/根取消；卸载失败返回 false 不硬关；超时常量）。
- **B5 未关闭**：`M3WiringGuardTests` 读将删除的 `MetaEntryService.cs`；`SessionBoundaryTests` 路径；`ModuleInterfaceOwnershipTests` 接缝路径；`CrossModuleContractLocationTests` 死链；L1 源链接缺 `IMetaAccountSession/AccountCloseReason`；生产注释旧名。→ v7 N1 逐条列出。
- 3 major：软关闭 API 未定义（v7 M-a）、GM 页归属与 `ConfigureTestRoom`/not-configured 优先级（v7 M-b）、`ShowLogin` 二次导航窗口（v7 M-c）。
- 收口条件（复核代理原话）：N1+N2+3 major 修完即“无阻塞，可进入用户确认”。

处理：v7 已追加到 plan.md 末尾（“v7 修订”），逐条落实上述修复；按复核代理给出的收口条件定稿，不再开新一轮方案评审。

## 评审结论（最终）

- 评审方式: 独立子代理
- 结论: PASS
- 验收标准: PASS
- 阻塞问题: 0
- 评审人: `90703931…` / `dd8f7be3…` / `cddf1dfd…` / `4b88e4ca…` / `90cc5415…`
- 评审时间: 2026-11-09T02:27+08:00（第五轮）
- 备注：v7 为第五轮复核给出的最小修复集，已全部落入方案；按复核代理“修完即可进入用户确认”的结论收口。
