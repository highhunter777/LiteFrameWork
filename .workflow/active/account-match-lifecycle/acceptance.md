# 验收标准确认：Account/Match 名称与生命周期纠偏（第二批）

> 运行：account-match-lifecycle；日期：2026-11-09（Asia/Hong_Kong）。v7：按用户选择（401 接线、Meta 会话迁入 Account、只做自动化）与五轮独立评审定稿。

## 验收标准

| 编号 | 验收标准 | 判定方式 |
|---|---|---|
| AC1 | **命名与位置**：(a) 生产旧名零命中（`AccountSession/IAccountRuntime/IAccountSessionFactory/IAccountSessionControl`，含类名；Battle 侧 `class MatchSession/IMatchSessionFactory/MatchSessionFactory`=0）；(b) 测试/守卫/csproj 固定清单同步（`M3WiringGuardTests`、`SessionBoundaryTests`、`ModuleInterfaceOwnershipTests.SeamExceptions`、`CrossModuleContractLocationTests` 死链、LiteNet 5 条、Core +4 条）；(c) 指定文档语义回写（说明 02/03/14/17/18(生成)、规范 03/04 §6.10、架构图 README、待办总览、DI §6、商业级 §6.2/§7.1、路线图 §4.2、地图数据专项）；(d) `arch/units.json` 三处 + `domain-matrix --check`；(e) 豁免（施工进度历史、`ClientScope("Account")` 示例、Account 域名）。 | 限定范围 `rg` + 守卫 + 文档清单 + domain-matrix |
| AC2 | **账户域**：登录成功开（per-account `MetaAccountSession` 登记 Account 容器）；登出/401/宿主关闭/`ForceReleaseRoot` 关；`Revoke`（软）与 `Close`（硬）分离、软关闭后仍可硬释放；重登旧域恰一次；门面切新、旧会话不可读；令牌不进日志（`Log.SetSink`）与公开端口（反射+守卫）；401 不在自身栈关域。 | 域开合/重登/门面/强制释放/日志 sink/反射 |
| AC3 | **Match 域**：进房创建、交付即 Seal；失败/取消/离场各释放恰一次；Battle 前失败直接释放；清理完成前不放行；RoomFlow 26 项绿。 | RoomFlow 生命周期断言 + 新增用例 |
| AC4 | **Battle 子域**：`BattleContext` 只在 `"Battle"` 容器解析、随清理释放；100 局无泄漏；无跨局复用；无 `Root→Account→Match→Match` 重名。 | `MatchLoopLeakEditModeTests` + 新增无重名断言 |
| AC5 | **行为**：除 401 收口（新行为）外，三类入口/取消/失败恢复/离场/账号重入/本地测试直入与第一批一致（自动化 + diff）；`LeaveMatchAsync` 终态判据（CleanupTask 完成 + Current∈{Main,Error}/未启动/根取消；卸载失败 false 不硬关；超时常量）有顺序用例；协议/页面契约/UI 布局不变；**真实 UI 本批不做**。 | 自动化全量 + 顺序用例 + diff 核对 |
| AC6 | **自动验证**：新增用例先 red 后 green（证据落 `evidence/`）；RoomFlow/MetaEntry/PageFlow/Core/Net 全绿；`L1`、`doc`、`l0`、`domain-matrix --check`、`toolchain check`、comment/dir-flat、Unity 编译 0 错（L1 计数不写死）。 | `wf verify` 全命令 exit=0 + red 证据 |
| AC7 | **文档一致**：DI §6 + 商业级 §6.2 与代码一致（Account/Match 各一服务域；Battle 子域；Scene/UI 行保留）；Meta 会话已入 Account；Profile/RemoteConfig 无实现只留登记点；说明 02/03/14/17 与进度卷回写；Doc 门禁通。 | 实读文档 + `--suite Doc` |
| AC8 | **接线与边界**：`IAccountDomain` 生产消费者（`MetaSegment`/门面/GM 页）与释放者（`MetaSegment`/RootScope）；`IAccountReentry` 在 401 有真实调用点；**不新增程序集边（Meta 不引 UI）**；无 public static 可变；arch/静态/接口归属守卫绿。 | 守卫 + 装配实读 |

## 用户已确认的选择

1. AC1～AC8 按 v4 确认通过；
2. **401 令牌路径本批接线**；
3. **Meta 会话本批迁入 Account 域（方案 C）**；
4. **真实 UI 冒烟降为只做自动化**。

## 本版新增的实现形状（需在确认时知晓）

- `MetaEntryService.cs` **拆分**为 `MetaLoginService`（root：登录+部署事实）与 `MetaAccountSession`（per-account：令牌+房间/票据/战绩）；`MetaEntrySession.cs` 由 `MetaSessionFacade`（App，root）取代，三组路由（登录事实/账户令牌面/流程面）。
- 账户域**软关闭 `Revoke` / 硬关闭 `Close`** 分离；`IRoomFlow` 新增 `LeaveMatchAsync(ct)`（等待清理终态，带超时常量），`AccountReentry` 改 async，顺序 `Logout → await LeaveMatchAsync → Close → ShowLogin`。
- **401 行为变化**：令牌调用点 `IsAuthLost` 触发账号重入（旧行为：返回失败、无收口）；登录路径（无 bearer）不接 401。
- 测试面：`MetaEntryEditModeTests` 13 例装配重写（loginService+Open+accountSession 双对象，断言语义不降）；`RoomFlowRig`/`RoomFlowTicketTests` 需 root scope/容器/门面；`AccountReentryTests` 改 async 顺序断言。
- **真实 UI 冒烟不在本批**。

## 提审记录

- 提审方式：v7 经五轮独立评审收口后，逐条提交用户确认（2026-11-09T02:35+08:00）。
- 用户原话选择：401 令牌路径本批接线；Meta 会话本批迁入 Account 域；真实 UI 冒烟降为只做自动化；最终确认 v7。
- 既有授权：第一批竣工时确认“第二批再评估 Account/Match 命名与 Scope 全量纠偏”。

## 用户确认

- 确认结论: 通过
- 确认人: 用户（对话确认 v7）
- 确认时间: 2026-11-09T02:35+08:00
- 变更要求：无（按 v7 执行）

## 变更记录

- v1 首版 → 五轮独立评审（FAIL×5）→ v7 定稿；期间用户扩大范围（401、Meta 会话、只做自动化）。
- 评审记录与逐条处理见 `reviews/plan-review.md`。
