# 服务端 R3-Profile 批：施工进度

> 依据：[《上云测试专项设计》](../设计文档/architecture/上云测试专项设计.md) §2/§3 批A/§4 裁决点①③/§5/§6；[《Meta 服务专项设计》](../设计文档/architecture/Meta服务专项设计.md) §8.2 结算链/§11.3。
> 提交：工作区交付（未提交，提交号回填待办）。
> 本文件只记录已执行的施工状态与验证证据；设计裁决只写入设计文档。
> **接手说明**：本批接手并行线在途 WIP（账本半部：AppendMode + `Contracts/Profile` + `ApplyMatchResultUseCase` + 其测试），在其上收口为"一条提交、双侧落库"并补齐端点到管道全链——合并方案与边界见下。

## 批次规划（R3 线）

| 批 | 范围 | 状态 |
|---|---|---|
| R3-Auth-1 / R3-Lobby | 见[服务端R3首批](服务端R3首批.md)/[服务端R3-Lobby](服务端R3-Lobby.md) | 已完成 |
| **R3-Profile** | 结算提交消费（账本 Apply 半部）+ **归档与按账号查询** + 房间侧**提交管道**（重试/退避/重启续投）+ 结算载荷 SeatAccountIds + **SIGTERM 排空** | **已完成（附验证证据）** |
| R3-Auth-2 | 正式身份（账号绑定/刷新/吊销） | 未开始（封闭测试范围外） |

## 施工记录

### 2026-10-10 · R3-Profile：结算链闭环（并行线 WIP 接手合并）

**Meta 侧（结算提交消费 + 结果归档查询）**

- `Server/MetaServer/Contracts/Profile/ProfileContracts.cs`（并行线建，本批扩）：`MatchResultSubmission` 扩为**一条提交双侧承载**——match 级 `Seed/FinalFrame/EndReason/GameplayEndReason/WinnerEntityId`（归档事实）＋玩家级 `AccountId/Kills/Deaths`（查询归属，裁决点①③）；保留并行线的账本字段（`Delta/ExpectedRevision=-1/OperationId`）与响应/错误码族（`profile.*`）。
- `Server/MetaServer/Contracts/Persistence/MatchResultArchive.cs`（本批建）：归档端口 `IMatchResultArchive`（幂等键 = MatchId；`FinishedUtc` 存储端赋值；双态 Stored/Duplicate；存储未确认抛既有 `SettlementStoreUnavailableException`）+ `MatchResultArchiveEntry/MatchResultPlayerEntry`/`AccountMatchResult`。
- `Server/MetaServer/Infrastructure/Persistence/Mongo/MongoMatchResultArchive.cs`（本批建）：`match_results` 集合，`_id`=matchId 即幂等裁判（重复提交读回首次快照）；账号查询 `Players.AccountId` 索引 + 完成时刻倒序。
- 迁移 **v4**（`MongoMigrations.cs`：match_results 建集 + `ix_match_result_account`）。
- `Server/MetaServer/Modules/Profile/ApplyMatchResultUseCase.cs`（并行线建，本批扩）：执行序 = 验证（扩：AccountId 必填、kills/deaths/各 int 字段非负）→ **归档面先落**（`Processed.Duplicate` 携带归档幂等位）→ 账本 fan-out（并行线原语义不变）——两面具幂等，任一面失联 → `Unconfirmed`（重投收敛）。
- `Server/MetaServer/Host/SettlementEndpoints.cs`（本批建）：`POST /matches/result`（实例密钥——`InstanceKeyAuth` 与 Lobby 注册共用凭据；200 含 duplicate 位；任一玩家 CAS conflict → 409；形状错误 400 一次报全）+ `GET /matches`（访问令牌按**令牌账号**查询，`limit` 有界 1..100）。
- `MetaHost` 装配：Mongo 块内注册归档 + 用例；`Ops` 增 `meta_match_result_store_total`/`meta_match_result_duplicate_total`。

**RoomServer 侧（载荷 + 提交管道 + SIGTERM）**

- `SettlementOutbox.cs`：新增 `PendingSettlement`（摘要 + **SeatAccountIds 与 Players 同序等长**）；`Enqueue(summary, seatAccountIds)`；`ListPending()` 返回待提交记录；日志追加字段 `SeatAccountIds`（**旧行兼容**：缺省 null → 空数组，先例＝`Kind` 字段）。
- `ServerHost`：SettlementReady 时 `BuildSeatAccountIds`——从当前房间席位会话取账号（查不到 → 空串），随行持久化（重启续投不丢归属）。
- `SettlementSubmitService.cs`（本批建）：Outbox 待提交头部 → POST Meta（实例密钥）→ 200 即 `TryMarkCompleted`（duplicate 同样算完成——Meta 幂等是最终裁判）；失败保持待提交 + **指数退避封顶**（日志节流）；**重启续投零内存记账**（进度全在日志）；载荷 delta=0 占位（无经济，P4 接真实 RewardDelta）。
- `RoomServerConfig`：`settlement` 分区（`submit_url` 空 = 关闭；非绝对 URL/半段配置/区间越界拒启）；`HostAssembly` 注册（**装配收敛批2 第二批对象**）；`Program` fail-closed（启用但缺实例密钥拒启）+ 启动日志（含待提交数）。
- **SIGTERM 排空接线**：`Program` 常驻分支注册 `PosixSignalRegistration(SIGTERM)`——容器 stop/K8s 与 Ctrl+C 同排空路径（`context.Cancel=true` 阻止默认终止）；不支持该信号的平台如实降级 null（本地仍由 Ctrl+C 承担）。
- `Config/roomserver.json`：settlement 说明 + 占位（url 空默认关闭）。

## 验证证据

**实测缺陷（接手盘点中发现并根治）**：全量 L1 车道复跑一次中 `LiteSim.Core.Tests` 翻红一例——
`确定性_中途快照CopyTo逐元素一致且不扰动后续`（期望/实际为两个不同世界值）。诊断链：单类隔离跑
3×10 恒绿 → 车道环境（`DOTNET_PROCESSOR_COUNT=4`）整项目复跑 **1/6 复现** → 根因 =
**用例间共享 `SimTestRules` 全局静态的并行竞态**（写方 `SimWorldTests.测试房免死`/
`WeaponSystemTests` 置 `NoDeath/InfiniteAmmo` 的 try/finally 窗口，被并行读者的 3000 帧确定性
运行撞上——`DamageSystem.cs:40` 读 `NoDeath`）。根治：`Tests/LiteSim.Core.Tests/TestParallelization.cs`
置 `[assembly: CollectionBehavior(DisableTestParallelization = true)]`（本项目内串行集合）——
修复后同环境复跑 **10/10 全绿**。根治路径备注（标志位入世界/房间实例）随测试模式规则表化另行评估。

| 层级 | 命令 | 结果 |
|---|---|---|
| 双服务构建 | `dotnet build Server/MetaServer|RoomServer -c Release` | 0 错误 |
| 用例（Meta 账本/归档/端点） | `dotnet test Tests/MetaServer.Tests -c Release` | **149/149 通过**（含并行线 `ApplyMatchResultUseCaseTests` 收编 +13、归档侧新例 4、功能关闭路径 2） |
| 用例（提交管道/配置/日志兼容） | `dotnet test Tests/LiteNet.Tests`（聚焦） | **85/85**（提交管道 8 例：载荷字段/账号映射/旧行空账号/失败保持/重启续投/退避封顶；配置分区 11 例；SeatAccountIds 日志往返） |
| **L3 真 Mongo 结算链** | `dotnet test Tests/MetaServer.Integration.Tests --filter SettlementL3Tests` | **4/4**：①同一局提交 **100 次只落一条**（首次 false/其后 duplicate，Ops 计数 1/99）②按账号倒序/限长/他人隔离 + 字段核对 ③鉴权与形状稳定码（401/400）④**闭环**：真 `FileSettlementOutbox` → 真 `SettlementSubmitService`（真 HTTP）→ 真 Meta → 真 Mongo；重启续投 + 重投幂等出一份 |
| 全量 L1 | `powershell -File scripts/gate/test.ps1 -Lane L1 -Profile PullRequest -ResultsDirectory TestResults/r3-profile-l1b` | **1570/1570 通过，0 失败**（1537 → +33：MetaServer.Tests 115→128、LiteNet.Tests 371→391 等；产物 `TestResults/r3-profile-l1b/`；含并行竞态修复后的复跑全绿） |
| 全量 L3 | `powershell -File scripts/gate/test.ps1 -Lane L3 -Profile PullRequest -ResultsDirectory TestResults/r3-profile-l3b` | **163/163 通过，0 失败 0 跳过**（156 → +7：集成工程 30→34、MetaServer.Tests 集成段 +2、LiteNet 集成段 +1；真容器全实跑） |
| L0 | `scripts/gate/l0-dep-scan.ps1` | 1 条**既有**红（JoinAdmissionGateTests 测试字面量误报，非本批引入）；本批零新增 |
| buildHash | `python scripts/codegen/gen-build-hash.py --check` | **stale：`07dbe182f67d0e69` ≠ 复算 `3d7ee6f28c05ef83`**——归因夜批 Sim 面改动（`f25b1a1`/`1254b82`，承接"开发期不重跑、出包 `--check` 拦"裁决）；**本批改动全在 hash 闭包外（Server/Tests/根 Config），零贡献** |
| 冒烟（四形态） | RoomServer `--duration`：①裸 KCP ②信封 ③结算启用（无在盒） ④结算启用 + 日志预置在盒条目（Meta 不可达） | 四形态 **exit 0**、严格 60Hz；④启动即投递失败→节流日志→退出后日志条目仍在（**不丢**） |

## 边界与下一批

- 本批不启动 Unity（L2/EditMode/PlayMode 未跑——本机无 Unity 环境）；`buildHash` 重录与 Unity 重编译对齐随出包门禁（承接夜批裁决）。
- **结算增量 delta=0 占位**：封闭测试无经济系统——账本 revision 推进/余额不变；真实 RewardDelta 换算归 P4（换算在提交方，Meta 不接受客户端上报）。
- 完整 §6 端到端（真实 KCP 双客户端 + 登录→进房→对局→可查→**杀进程重启**→重放）未整体重跑：本批以"②闭环（真 HTTP+真 Mongo+真管道+重启续投）+ 既有 KCP 对局级用例（LiteNet.Tests 真传输/真 Runtime→Outbox）"两段衔接取证；**完整进程级冒烟归批B 部署前执行**（SIGTERM 亦在该环境实证——本机 Windows 无法注入 SIGTERM）。
- 已知边界：提交管道头部条目若被 Meta 永久拒绝（如旧行无账号），会以退避持续重试并显式计数（不静默丢弃、不阻塞对账；封闭测试全新环境不产生此类行）。
- 并行线合并说明：其账本半部语义（AppendMode/双维度幂等/fan-out）原样保留；本批新增归档面挂在同一用例内（"一条提交、双侧落库"），错误码沿用其 `profile.*` 族——不另立第二套结算错误码。
