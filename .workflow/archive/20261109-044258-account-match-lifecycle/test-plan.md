# 测试计划：Account/Match 名称与生命周期纠偏（第二批）

> 运行 account-match-lifecycle · 2026-11-09 · 对应方案 v7、AC1～AC8。红→绿：先跑 `wf verify --red`（本批用例断言失败），再实现转绿。

## 测试策略

| 层 | 位置 | 说明 |
|---|---|---|
| L1（dotnet） | `Tests/LiteFramework.Core.Tests/AccountDomainTests.cs` | 只测 `AccountReentry` 顺序（假 `IAccountLoginControl`/`IRoomFlow`/`IAccountDomain`）；401 计数在生产 `MetaAccountSession`，放 EditMode；**不**用假域证明生产域生命周期 |
| L1（dotnet） | `Tests/LiteFramework.Core.Tests/Meta/AccountReentryTests.cs` | async 重入：Logout→LeaveMatchAsync→Close→ShowLogin；false 仍 ShowLogin |
| Unity EditMode | `Assets/Tests/EditMode/Meta/MetaEntryEditModeTests.cs` | 13 例改 loginService+Open+accountSession 双对象；新增真实 `MetaSegment/RootScope` 下的登录开域/登出/重登/软硬关闭/ForceReleaseRoot/401×4/门面切换 |
| Unity EditMode | `Assets/Tests/EditMode/Flow/RoomFlow/**` | 26 项改名适配 + Match 域进房创建/交付即 Seal/失败取消释放/`LeaveMatchAsync` 四终态（真实 `ProcedureFlow`） |
| Unity EditMode | `Assets/Tests/EditMode/Battle/Session/BattleRunPortEditModeTests.cs` | Battle 子域派生/释放 |
| Unity EditMode | `Assets/Tests/EditMode/Battle/Net/MatchLoopLeakEditModeTests.cs` | 100 局无泄漏 + 无 `Match→Match` 重名 |
| 门禁 | `wf verify` | L1/doc/l0/domain-matrix/toolchain/comment/dir-flat/Unity 编译 |

## 用例清单

| # | 用例 | AC | 层 | 红期断言 |
|---|---|---|---|---|
| 1 | 真实装配：登录 → `IsOpen` true、AccountId 正确、Account 服务域可解析 `IMetaAccountSession` | AC2 | EditMode | 桩不开启 → 失败 |
| 2 | 登出 → `IsOpen` false、旧 scope `IsDisposed`、`OwnedCount==0` | AC2 | EditMode | 桩不关 → 失败 |
| 3 | 重复登录 → 旧域释放恰一次（探针）、新 scope 不等 | AC2 | EditMode | 桩不释放 → 失败 |
| 4 | `Revoke` 后仍可 `Close`，`Close` 幂等 | AC2 | EditMode | 无 Revoke → 失败 |
| 5 | 宿主优雅关闭 + `ClientHost.ForceReleaseRoot` 释放 | AC2 | EditMode | 未登记 → 失败 |
| 6 | `Open` 失败不留半开域、可重登 | AC2 | EditMode | 不清理 → 失败 |
| 7 | 门面：登录后读新会话；登出后读 NotLoggedIn；旧会话不可再读；页面缓存同一门面 | AC2 | EditMode | 不切换 → 失败 |
| 8 | 401：ListRooms/取票/建房/战绩 各一次 → 重入恰一次、不在自身栈关域 | AC2/AC5 | EditMode | 不触发 → 失败 |
| 9 | 令牌不进 `Log.SetSink`；`IAccountDomain/IMetaAccountSession/IMetaSession` 公开成员名无 token | AC2 | EditMode | 桩输出 → 失败 |
| 10 | 未登录产品进房 → `NotLoggedIn`、零连接 | AC5 | EditMode | 放行 → 失败 |
| 11 | `testRoom && DevBuildFacts.IsDevelopment` 直入仍可进 | AC5 | EditMode | 被拦 → 失败 |
| 12 | Match 域进房创建、交付即 `Services.IsSealed` | AC3 | EditMode | 未建/未 Seal → 失败 |
| 13 | Match 失败/取消/离场各释放恰一次；Battle 前失败直接释放、`OwnedCount==0` | AC3 | EditMode | 泄漏 → 失败 |
| 14 | 清理未完成不放行下一次入场 | AC3 | EditMode | 放行 → 失败 |
| 15 | Battle 子域 `"Battle"` 派生/释放；100 局无泄漏；只在 `"Battle"` 容器可解析（父域解析失败） | AC4 | EditMode | 复用/泄漏 → 失败 |
| 16 | 全链 scope 名无 `Account→Match→Match` 重名 | AC4 | EditMode | 重名 → 失败 |
| 17a | `LeaveMatchAsync`：无操作 true；CleanupTask+Main/Error true；`!Started` true；根取消 true | AC3/AC5 | EditMode | 桩恒 true 时靠 `UniTask` 时序断言失败（真实 `ProcedureFlow`） |
| 17b | 卸载失败 false；超时 false（真实 10s 常量，用例上限 15s） | AC3/AC5 | EditMode | 桩恒 true → 失败 |
| 18 | 重入 false 仍 ShowLogin（不硬关 Account） | AC5 | L1（AccountDomainTests） | 不导航 → 失败 |
| 19 | 重入顺序 Logout→LeaveMatchAsync→Close→ShowLogin；`_returning` 跨 await | AC5 | L1（AccountDomainTests） | 顺序错 → 失败 |
| 20 | RoomFlow 26 项（改名后）全绿 | AC3/AC5 | EditMode | 编译/断言失败 |
| 21 | 守卫：M3/Seam/ProcedureBoundary/CrossModule 死链/接口归属 + AC8 消费者/释放者（`MetaSegment Put<IAccountDomain>`/`RootScope.Register`/`Close`） | AC1/AC8 | L1 | 旧名/死链 → 失败 |

## 预期失败证据

- `wf verify --red`（`--root` 外部运行根）输出 `evidence/red.json`。
- 另存 `evidence/red-manifest.json`：`{case: 名称, command: 过滤命令, failed: n, message: 摘要}`；五条 red 命令各自至少 1 个断言级失败，覆盖 401、重登恰一次、LeaveMatchAsync 卸载失败、门面切换、AccountReentry 顺序。
- 红不得来自编译错误、缺类型、环境错误；空桩按 T1 契约（返回 true/false 但可断言失败）。

## 运行方式

- 先行：`python .agents/skills/staged-delivery/scripts/workflow.py --root C:\Users\hunter\.codex\tmp\workflows\account-match-lifecycle-20261109 --run account-match-lifecycle verify --red`（5 条 red）。
- 施工后：同命令去掉 `--red`（19 条）。
- Unity：`python scripts/gate/unity-tests.py --mode editor --filter <类名> --timeout 900`。
- L1：`dotnet test Tests/LiteFramework.Core.Tests --filter FullyQualifiedName~<类名>`。

## 绿证据

- `wf verify`（19 条命令）全 exit=0，`evidence/verify.json`。
- `git status --porcelain` + `git diff --stat` 证明协议/页面契约/UI 资源零变更。
- 未验证项（真实 UI 按用户确认不做）在 build-log 披露。
