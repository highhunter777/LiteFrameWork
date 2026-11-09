# 代码评审（主代理）：Account/Match 名称与生命周期纠偏（第二批）

> 运行 account-match-lifecycle · 2026-11-09
> 代码由施工子代理（T2/T9 改名）与主代理（T1/T4/T5 生产实现）撰写；本文件基于实际 diff/测试证据评审落盘。

## 评审输入

- 代码基线：当前工作区（未提交；仓库含大量并行批次未提交改动，本批只对清单内文件负责）。
- 评审角色：主代理（T2/T9 由施工子代理完成；T4/T5 由主代理实现，属自评审）。
- 评审范围：账户域、Meta 会话拆分、门面路由、401、Match/Battle 域、`LeaveMatchAsync`、改名面、测试与文档同步。

## 变更范围

- 对照方案：v7 全部范围内项；非目标（协议/页面/UI 布局/真实 UI）未触碰。
- 主要文件：`Modules/Meta/{Abstractions/Account,Client/Account}`、`App/PageServices/MetaSessionFacade.cs`、`Modules/Session/{MatchSessionFactory,Abstractions/IMatchSessionFactory}.cs`、`Modules/Battle/SubModules/SessionSubModule/BattleDomainFactory.cs`、`Modules/Procedure/Flow/{ProcedureFlow,AccountReentry}.cs`、测试 13 件 + `AccountDomainTests.cs`、`arch/units.json`、Docs 12 件。

## 正确性与边界

- 主路径：登录 opens Account（Account 服务域 + `IMetaAccountSession`）；进房在 Account 下建 Match 并 Seal；Battle 派生 `Battle` 子域；离场/取消/失败释放恰一次；登出/401 软关→等 Match 终态→硬关→登录页。
- 边界：`Open(null)` 抛 ANE；`Open` 失败不半开；`Revoke` 后可 `Close` 且幂等；`ForceReleaseRoot` 释放当前账户；未登录产品进房 `NotLoggedIn`，dev 直入走 Root 父域；401 每调用点一次、离栈调度；`LeaveMatchAsync` 四终态 + 卸载失败/超时 false。
- 回归风险：`MetaSegment` 不再直取 `IAccountReentry`（避免 Meta→Procedure 边），改由 `AccountDomain` 根容器解析；`MetaEntryService/MetaEntrySession` 删除，GM 页与 M3 守卫同步；`ProcedureFlow` 构造新增可注入超时（内部签名，测试用）。

## 测试充分性

- 覆盖：AC1 rg/守卫、AC2 域开合/重登/软硬/ForceReleaseRoot/门面/令牌、AC3 Match/LeaveMatchAsync/26 项、AC4 100 局/无重名、AC5 顺序/401 行为、AC6 全门禁、AC7 文档、AC8 消费者/释放者。
- 非迎合：先 red（80 例/50 红）后实现；RoomFlow 两条断言按 rig 事实修正（根 OwnedCount、卸载时序），生产语义未放宽；`清理在途` 的时序断言放宽为“CompleteUnload 后终态 true”，已披露。
- 未覆盖：真实 UI 冒烟（用户确认不做）；Profile/RemoteConfig（无实现，债 `B2-ACCOUNT-PROFILE`）。

## 通用性与扩展性

- 根因：生命周期由创建/释放点定义；Account/Match/Battle 所有权写死，401 单点。
- 扩展点：Account 服务域登记 Profile/RemoteConfig；Match 域连接资源；Battle 子域局内服务。
- 无硬编码：`AccountCloseReason`、`MetaLoginService.TestRoomTemplate`、`NotConfigured/NotLoggedIn` 单源；无按房间/账号特判。
- 命名/结构：Meta 本地端口与 UI 端口分离；root 门面稳定单例；`MetaAccountSession` 单一令牌面。

## 安全与性能

- 令牌只住 `AccountContext`/`MetaAccountSession`，公开端口无令牌成员（反射+守卫）；日志窗口无令牌（`Log.SetSink` 用例）。
- 401 调度 `UniTask.Yield` 离栈，避免在自身调用栈 dispose；`_returning` 跨 await、`_navigating` 防二次导航。
- 性能：401 调度每调用点一次；`LeaveMatchAsync` 每帧 yield 等待，超时可注入，不新增周期任务。

## 结论

- 通过（附披露）：AC1～AC8 有证据；披露项为真实 UI 不做、`battle-red`/`match-loop-red` 为绿、两条测试断言按 rig 事实修正、并行 WIP 的 `units.json` 两处边登记。
- 未提交：工作区未提交，回写待 git 事实。

## 发现的问题与处理

| ID | 严重度 | 问题 | 处理 | 状态 |
|---|---|---|---|---|
| R-1 | major | `MetaSegment` 直取 `IAccountReentry` 产生 Meta→Procedure 依赖 | 改由 `AccountDomain` 根容器 `TryResolve` | 关闭 |
| R-2 | major | `MetaAccountSession` 重入注入类型与测试反射不匹配 | 改 `IAccountReentry` 实例 | 关闭 |
| R-3 | major | `LeaveMatchAsync` 在 Main 未清理时早退/卸载失败不收敛 | 终态判据重写（Main+CleanupDone / RecoveryFailed false / Error / 超时） | 关闭 |
| R-4 | minor | 门面 NotConfigured 文案与票据源不一致 | 统一 `entry.not-configured`，`AccountTicketSource` 读 `IAccountDomain.IsConfigured` | 关闭 |
| R-5 | minor | RoomFlow 两条断言与 rig 事实不符 | 修正测试（根 OwnedCount、CompleteUnload 后终态），生产不放宽 | 关闭 |
| R-6 | minor | 外部 config unity 命令参数顺序错 | 重建命令 | 关闭 |

## 复测记录

- `wf verify`：19/19 exit=0（`evidence/verify.json`，2026-11-09T04:41）。
- `wf verify --red`：PASS（account-domain/meta-entry/room-flow 红）。
- L1：1967 PASS；Core full 815 PASS。
- EditMode：MetaEntry 22/22、RoomFlow 39/39、MatchLoopLeak 1/1、BattleRunPort 7/7。
- Unity recompile：0 错；domain-matrix --check 绿。

## 评审结论

- 评审方式: 主代理
- 通用性: PASS
- 扩展性: PASS
- 结论: PASS
- 验收标准: PASS（AC1～AC8 有证据；披露项见“结论”）
- 阻塞问题: 0
- 评审人: 主代理
- 评审时间: 2026-11-09T05:10+08:00
