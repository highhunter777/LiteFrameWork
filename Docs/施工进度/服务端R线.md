# 服务端 R 线（R0～R4：正确性止血 → 运行时纯化 → 安全 → Meta 服务 → 可观测）

> **定位**：服务端地基与应用层主线。R0 定正确性，R1 纯化内核与分离会话，R2 安全四件，R3 拆成 Meta 服务三模块，R4 起可观测。

> **章法**：一卷一条线；每章＝原一份批次记录（标题、正文、证据原样并入，仅降一级标题）。
> **状态图例**（全目录统一）：`已完成（附验证证据）` / `进行中` / `未开始` / `阻塞（附原因）`。
> 每章开头的引用块是该批的原始状态与证据；本卷只做归并与章节化，未改写任何批次结论。

## 章节索引

| 章 | 标题 | 原文件 |
|---:|---|---|
| 1 | 服务端 R0 施工进度：正确性止血 | `服务端R0.md` |
| 2 | 服务端 R1 施工进度：RoomRuntime 纯化与会话分离 | `服务端R1.md` |
| 3 | 服务端 R2 安全批（重连票据 CSPRNG · 规范化远端地址 · 分层限流）：施工进度 | `服务端R2安全.md` |
| 4 | 安全信封：施工进度 | `安全信封.md` |
| 5 | Meta 服务宿主骨架：施工进度 | `Meta服务宿主.md` |
| 6 | 服务端 R3 首批：施工进度 | `服务端R3首批.md` |
| 7 | 服务端 R3-Lobby 批：施工进度 | `服务端R3-Lobby.md` |
| 8 | 服务端 R3-Profile 批：施工进度 | `服务端R3-Profile.md` |
| 9 | R4 可观测首批：RoomServer /metrics 零依赖出口 | `R4可观测首批.md` |

---

## 第1章 服务端 R0 施工进度：正确性止血

<a id="ch1-服务端R0"></a>

> **原文件**：`Docs/施工进度/服务端R0.md`（归并前独立成文，内容整段并入本章）

> 依据：《商业级通用服务端框架总设计》§5 P0-1～P0-5 + §16 R0；《待办总览》§7 施工包 R0-A/R0-B。
> 本文件只记录施工状态与证据；目标与验收以设计为准，不在此重复定义。

### 批次规划

| 批 | 范围 | 状态 |
|---|---|---|
| R0-A 精确节拍 + 输入边界/定容环 | ServerLoop 高精度有理数时间轴 + 时钟/延迟注入；PendingInputRing 定容环；InputGate finite/向量边界/repeated 上限；ServerHost 包长与 Join 字节上限；旧测试按正确契约修 | 已完成（见下） |
| R0-B 可信 ACK/背压 + 稳定 configHash | Session 发送 ledger + ACK 四道验证；RoomClient ack 语义修正；CombatConfigDigest（SHA-256 规范化）替换 GetHashCode；KCP 安全边界声明 | 已完成（见下） |
| R0-C 验证收口与提交 | L1/L3/L2/Unity/纪律/BuildHash 全链门禁；施工记录；分批提交推送 | 已完成（见下） |

### 施工记录

#### 2026-09-22 · R0 全量交付

> 代码交付：2ffbaef（fix(r0) 服务端正确性止血，21 文件）；本记录：4a35ed6。均已推送 origin/LiteGame。

**① P0-1 精确逻辑时间轴（`RoomServer/Application/ServerLoop.cs` 重写）**

- 旧缺陷：`1000/60=16ms` 整数锚点实际给 **62.5Hz**（时间轴本身错 4%——追帧只补执行落后，补不了错误目标）。旧用例"空载跑一秒"接受 62~63 tick，属"为保绿保留错误行为"，已按正确契约重写。
- 新实现：第 n 帧目标 = `start + n × frequency / tickRate`（**整数有理数**，floor 语义，任意时钟频率零累计漂移）；`IMonotonicClock`/`IDelayStrategy` 注入（生产 Stopwatch + 粗睡自旋；L1 虚拟时钟零真实 Sleep）；`tickRate` 参数化（生产 60）；保留 MaxCatchUp 有界追赶 + 丢债记账（高精度化）；常驻 `LoopBody` 与 `Run` 共用同一份追赶规则（两处手抄必漂移）。
- 验收对齐：虚拟 10 分钟 **精确** 36,000 tick；30/60/120Hz 参数化均精确无漂移；虚拟慢帧过载下有界追赶 + 丢债可观测。

**② P0-2 多玩家同帧输入隔离（`RoomServer/PendingInputRing.cs` 新增）**

- Sync-P0 已修 (playerId, frame) 复合键语义（保留）；R0 把 Dictionary 换**定容环**：容量 16 ≥ FutureFrameTolerance(8)+冗余窗(4)+1；slot = frame+SimInputFrame[playerCapacity]+valid 位图；**slot 换代完整清理**（陈旧输入不复活）；先验证后索引不变。
- 验收对齐：8 人同帧独立预存/消费、环回绕换代清理、2~8 人不串位。

**③ P0-3 数值与协议边界（`RoomServer/InputGate.cs` + `ServerHost.cs` + `ProtocolConstants`）**

- Move/Aim 四浮点 **finite** 强制；Move 分量与长度² ≤1；Aim 分量与长度² ≤1；**Fire/Skill 意图帧 Aim 必须非零**（Reload/Pickup/Use 走槽位/目标语义不依赖）；repeated `Frames.Count > MaxRedundancy` 整条拒绝；非法包**不烧帧槽**（接受记账只在全项通过后更新）。
- ServerHost：入包长度上限 4096B（protobuf 解析**之前**）；Join 的 roomId/token/buildHash UTF-8 字节上限（64/256/128）；坏包/超长分类计数进 Ops；拒绝日志不回显 token/payload。
- 拒绝分类计数：`DroppedNonFinite/DroppedIllegalVector/DroppedOversizedMessage/PacketRejects/PacketOversized` 等，Ops 汇总行扩展。
- 模糊测试（固定种子 256 轮随机位型浮点 + 随机按钮/帧号）：不抛、不进权威态、防伪覆写恒成立。

**④ P0-4 可信 ACK 与背压（`RoomServer/SnapshotLedger.cs` 新增 + `Session/Room/RoomBroadcaster`）**

- 旧缺陷：`OnClientAck` 任意非负 ack 即 `AckedBytes = SendQueueBytes` **全清水位**——伪造 ack 可让慢客户端伪装成快客户端，E1 降级形同虚设；且 `HandleInput/OnInput` 存在**未验证直写** `LastAckSnapshot` 的路径（NeedsFull 判据吃到伪造值）。
- 新实现：`Session.RecordSnapshotSend(frame, bytes)`（广播器/测试同入口）；有界 ledger 环（容量 128 ≈ 30Hz 下 4.3s 窗 ≥ 4× ack 落后判据窗）记 (帧号, 发送后累计字节)；`Room.OnClientAck` 四道闸——非负且单调前进 → ≤ LastSentSnapshotFrame（只认真实发送过的帧）→ 命中 ledger → 释放=累计值（天然单调）。回退/超前/超窗分类计数；`LastAckSnapshot` 只在验证后更新。
- **客户端真缺陷修正**（`RoomClient`）：`InputMessage.AckSnapshot` 原错报服务器 `AckInput`（输入确认帧号），应为**已收最新快照帧号**（协议注释口径）——诚实 ACK 链路下"从未发送过的帧号"会被整体忽略，背压永不释放。旧用例"分开跟踪"按正确契约反转断言。

**⑤ P0-5 稳定 configHash（`LiteSim/Core/Scripts/CombatConfigDigest.cs` 新增）**

- `StartGame.ConfigHash` 从 `string.GetHashCode()`（进程随机种子，跨进程必不一致）换为 **SHA-256 低 32 位**：CombatConfig 8 字段固定序、InvariantCulture "R" 规范化、UTF-8。两端同源单源（客户端校验与服务器下发同一函数）。
- 测试纪律：配置变化语义经**文本级摘要入口**验证，不改写全局装载状态（测试类并行，改静态 CombatConfig 会与基线/校验用例竞态——实施中实证过一次 L1 假红，已按此纪律修正）。

**⑥ 安全边界现状声明（R0 交付项："明确当前 KCP 安全能力与公网暴露限制"）**

- kcp2k V1.41 cookie 只解决 UDP 探测/放大防护，**不提供业务身份、机密性或完整性**；
- token 校验为原型级（非空 + 长度）；~~重连票据为可预测串（非 CSPRNG）~~ → 已于 2026-09-30 CSPRNG 化（[服务端R2安全](服务端R线.md#ch3-服务端R2安全)）；
- 本服务**在安全信封完成前不得公网暴露**（R2 安全批已交付 CSPRNG 票据与远端地址限流，签名 Join Ticket 已于 M0-d 交付；安全信封另立专项）——已写入 `ServerHost.HandleJoin` 注释红线与本记录。（后注 2026-10-03：信封批1 已交付，[安全信封](服务端R线.md#ch4-安全信封)；红线延续为部署门禁——真机核证/批2〔随 R4〕闭合前不得公网。）

**测试面**

- 新增：`SnapshotAckTests`（合法释放精确累计/伪造超前忽略/回退重复忽略/中间帧只释放到该帧/超窗保守/广播记账追溯 6 例）、`CombatConfigDigestTests`（确定性/文本变化敏感/文化无关 3 例）。
- 重写：`ServerLoopTests`（虚拟时钟 Theory 30/60/120Hz 精确 tick + 实钟 60Hz 容差冒烟 + 虚拟过载丢债；删除 62~63Hz 错误接受）。
- 扩展：`InputGateTests`（8 人同帧/环换代清理/非有限/向量边界/开火零瞄准/超窗 repeated/模糊包）、`ServerHostTransportTests`（超长包/超长字段/坏包计数）、`RoomBroadcastTests`（背压用例改 `RecordSnapshotSend` 诚实记账播种）、`RoomClientInputTests`（ack 契约反转）。

**验证证据**

| 门禁 | 结果 | 命令 |
|---|---|---|
| L1 全量 | PASS 405 项（0 失败） | `scripts/test.ps1 -Lane L1 -Profile PullRequest` |
| L3 网络/无头集成 | PASS 8 项（含 30s 双端对跑不发散——真实链路 ACK 已验证化） | `scripts/test.ps1 -Lane L3 -Profile PullRequest` |
| Unity 编译 | recompile 0 错误（`errors:[]`） | `unity command recompile/recompile_status --project-path ... --proxy-disable` |
| L2 门禁 | 通过（meta 扫描 + 编译状态 + EditMode 30/30） | `scripts/gate/l2-unity-gate.ps1 -ProjectPath ...` |
| 纪律扫描 | 零违规（L1 内真实源码全目标） | `DisciplineScannerTests.真实源码_全部目标_零违规` |
| BuildHash | 已重跑（69a82432… 起，以最终提交为准） | `python scripts/gen-build-hash.py` |

**已知边界与后置项（不阻塞 R0 验收）**

- 精确节拍的生产收益上限受宿主调度粒度约束（Windows 定时器 ~15ms 粗睡 + 自旋收尾）；L3 实钟形态只保证"锚点正确 + 追赶有界"，绝对零漂移由虚拟时钟验收钉死。
- `SessionManager` 容量上限与周期清理、Transport 真实 pending bytes 暴露、Connection/PlayerSession 分离、重连状态机——R1（RoomRuntime 纯化）范围。
- 签名 Join Ticket、CSPRNG 重连票据、per-IP 限流、安全信封——R2（安全）范围；当前原型不得公网。→ 后记（2026-09-30）：前三项已交付（[签名 Join Ticket](服务端R线.md#ch5-Meta服务宿主) M0-d、[CSPRNG/远端地址/分层限流](服务端R线.md#ch3-服务端R2安全) R2 安全批），~~**安全信封仍缺（另立专项），信封完成前不得公网**~~。→ 再后记（2026-10-03）：信封批1 已交付（[安全信封](服务端R线.md#ch4-安全信封)）；红线延续为部署门禁（真机核证/批2〔随 R4〕）闭合前不得公网。
- Ops 计数为进程内内存口径；结构化日志/Metrics/Tracing/Health——R4（运营化）范围。

---

## 第2章 服务端 R1 施工进度：RoomRuntime 纯化与会话分离

<a id="ch2-服务端R1"></a>

> **原文件**：`Docs/施工进度/服务端R1.md`（归并前独立成文，内容整段并入本章）

> 依据：《商业级通用服务端框架总设计》§7/§8/§9/§16 R1；《框架先行建设与业务接入专项设计》§4 施工包"网络与服务端宿主"/§5 接缝；《待办总览》§5 G1 服务端交付行。
> 本文件只记录施工状态与证据；目标与验收以设计为准，不在此重复定义。

### 批次规划

| 批 | 范围 | 状态 |
|---|---|---|
| R1-A 分层拆分 | RoomServer 工程 namespace/文件夹边界（§7 过渡步骤 1，不拆 csproj）：`Runtime/`（纯运行时）+ `Application/`（应用与宿主装配）；Room/RoomBroadcaster 职责并入；ServerHost 协议边界装配 | 已完成（见下） |
| R1-B 命令面 + Match 状态机 + 会话分离 | RoomCommand/RoomOutput、MatchStateMachine §9.1 全表、Session（连接级）/PlayerSession（席位级）分离、SessionManager 容量与清理、ReconnectService 时钟注入/容量、房间级 FixedCombatConfig | 已完成（见下） |
| R1-C 重连闭环 | Restoring 相位 + 恢复完成 ACK 门闩 + 整帧全量重锚 + RoomClient 完整重连状态机（§9.3）+ 协议版本确认 | 已完成（见下） |
| R1-D 收口 | L0 纪律扫描 R11（Runtime 纯化把守）+ BuildHash 重生成 + 全链门禁 + 分批提交推送 | 已完成（见下） |

### 施工记录

#### 2026-09-24 · 批①② 接手复核 + 批③④ 交付

> 批①② 为 R1 并行会话在途工作区成果（未提交），按用户指令接手：逐文件复核 + 构建验证（接手时 L1 LiteNet 130/130）后随批③一并入库。代码交付：`dd6d335`（已推送 origin/LiteGame）。

**① 批① 分层（`RoomServer/Runtime/` + `RoomServer/Application/`）**

- Runtime 纯化红线全守（§8.1 禁止项；L0 纪律扫描 R11 把守）：不引用 Transport/Socket/系统墙钟/Console/文件/proto——输入经 `ClientInputBatch` 纯数据、输出经 `RoomOutput` 纯事件、时间只从 `RoomCommand.Tick` 的 nowMs 进入；LiteNet 常量在 Runtime 层**复述**（`ClientInputBatch.MaxFrames`），两端一致性由 L1 契约用例钉死（改 LiteNet 一侧必红）。
- `RoomRuntime`：房间内确定性状态唯一持有者，`Execute(RoomCommand)` 唯一入口；seed=0 派生去 `DateTime.Now`（Tick 注入时间戳混黄金比例常量，确定性不依赖墙钟）；掉线成员沿用空输入（§4.5-2）；单线程执行由调用方保证（R2 Worker/Mailbox 承接）。
- `SnapshotPipeline` 自 `RoomBroadcaster` 迁入 App 层：30Hz 抽帧编排 / SnapshotDiffer 两段式差分 / AOI / E1 背压分档 / 全量兜底。归属裁定：差分器与编码器消费 proto、是快照格式**唯一单源**（§19 禁第二套快照 DTO）——快照构建整体留 App，Runtime 只持权威态（`AuthSim`/`Gate`）由管线每帧拉取。
- Session（连接级：背压水位/ACK 四道闸/发送 ledger）与 `PlayerSession`（席位级：playerId/实体归属/重连窗口保留）分离（§9.2）；SessionManager 容量上限（4×房间容量，超限断开计数）+ 周期清理（断线超窗/未进房静默）；ReconnectService 时钟注入（去 `Environment.TickCount64`）+ 容量上限淘汰最老 + 周期清理。
- 删除 `Room.cs`/`RoomBroadcaster.cs`（职责并入 RoomRuntime/SnapshotPipeline）；`ServerHost` 改协议边界装配：解码→校验→构造命令→Runtime 执行→输出映射 proto 发送；`CombatNumbers.cs` 注释引用修正（《克隆后自备清单》→《环境恢复指南》，C1 记录移交项）。

**② 批② 命令面 + Match 状态机（§8.1/§9.1）**

- `RoomCommand`（struct 判别联合 + 静态工厂，热路径零分配）：AuthenticatedJoin / ClientInput / Disconnect / Rebind / Tick / Shutdown（App 完成票据/buildHash/房间号/字节上限校验后构造；Runtime 信任命令来源，不重复验证协议细节）。
- `RoomOutput` 纯事件族：Signal（PlayerAdmitted / MatchStarted / SeatRestored）/ JoinRejected / CloseConnection / MatchStateChanged（from/to/reason/matchId/frame/nowMs）/ SettlementReady（Finishing 冻结的 MatchResultSummary；完整结算信封/Outbox/归档归 R3）。
- `MatchStateMachine` §9.1 全表：Created→WaitingForPlayers→Starting→Running→Finishing→Settling→Closed；任意阶段→Aborted（不可恢复错误/运维终止/未开局放弃）；非法迁移**拒绝不改状态**（调用方输出 Reason=Rejected 事件，不抛——单条坏命令不炸权威循环）；每次成功迁移记录原因与进入时刻；等待超时/对局时限由 RoomRuntime 按 EnteredMs 判定（RoomConfig 可配，0=不限时）。
- `FixedCombatConfig`：房间创建时一次性固定不可变玩法配置并同刻绑定 CombatConfigDigest（§4/P0-5）；Create 侧消费一律读快照，StartGame 下发"本局数值身份"；Sim 系统内部全局读取的参数化迁移归 G1 ConfigSnapshot/热更批（单房间形态两者一致）。
- 新增 L1：MatchStateMachineTests 7、RoomRuntimeConfigTests 4、SessionLifecycleTests 5；旧测试按新架构迁移改写（RoomBroadcastTests/RoomInputTests/SnapshotAckTests 改命令面驱动 + `RecordSnapshotSend` 诚实记账，InputGateTests 补契约常量互锁等）。

**③ 批③ 重连闭环（§9.2/§9.3）**

- 协议（battle.proto，只增字段号）：`RestoreComplete` 包型（C→S，**Reliable**——丢失即重传，§9.3 步骤 6 门闩）；`ReconnectResponse` 追加 `seed`/`config_hash`/`build_hash`（§9.3 步骤 2 版本确认——客户端校验"还是同一局/同一构建"）。protoc 3.36.1 重生成；PacketType=11 + PacketCodec 登记。
- Runtime：Rebind 原子换绑 → 席位置 `Restoring`（增量广播抑制相位）；`RestoreAck` 命令（幂等：非 Restoring 静默忽略，重复/迟到 ACK 无副作用）→ Active + `SeatRestored` 信令；终态房间拒绝重绑（App 已拦 Running，Runtime 兜底）；旧连接迟到断开不误伤已重绑席位（connectionId 复用不能复活旧身份）。
- SnapshotPipeline：`BroadcastIfDue` 增席位可播判定参数（ServerHost 传"席位==Active"）——**双环抑制**：发送环不向 Restoring 席位投递；NeedsFull 扫描环同样跳过（其 fresh 水位 ack=-1 若参与判定会把整帧持续强制成全量）。恢复完成（SeatRestored）时 `RequestFullSnapshot` **整帧全量重锚**——抑制期间差分基线推进到 B（> 重连响应快照帧 N_r），不重锚会留 (N_r,B] 陈旧缝（实施中定位并修正的真缺陷）；重锚后增量无缺口。
- ServerHost：票据房间号绑定校验（消费语义保持 fail-closed：票据在任何校验失败时即作废）；RestoreComplete 分派 → RestoreAck 命令；`Ops.RestoresCompleted` 计数（Ops 汇总行扩展）；重连响应携带版本确认 + 权威全量（公共+比赛+本人私有）+ 输入历史。
- **RoomClient 客户端会话状态机**（§18"完整 Join/Reconnect/Restore 状态机"）：`ClientSessionPhase`（Idle/Connected/SuspectedLost/Reconnecting/Restoring/Failed）+ `OnPhaseChanged`；JoinAck 存一次性票据→Connected；传输断线→SuspectedLost（Reconnecting 保持由超时线裁决）；`BeginReconnect` 凭票据重拨（无票据→Failed——唯一出路重新 Join）+ 连接建立自动发 ReconnectRequest；响应 Ok 且版本一致→Restoring（Ok 但 seed/configHash/buildHash 不符→**Failed，绝不发恢复 ACK**）；`CompleteRestore` 发 RestoreComplete→Connected（重复宣告幂等）；拒绝→Failed；Reconnecting 超时→Failed（注入时钟 + Tick 驱动巡检，无线程/定时器）。默认时钟 = Stopwatch 时间戳换算（`Environment.TickCount64` 为 .NET 6+ 专有，Unity 编译不过——实施中实证后修正）。
- HeadlessClient（L3 harness）接状态机：首连才自动 Join（重连不重复 Join——相位守卫）；OnReconnectResponse 权威全量重建持久镜像 + 和解本地预测 + `CompleteRestore`；断线窗口只推进预测不发输入。

**④ L0 纪律扫描 R11（Runtime 纯化把守）**

- DisciplineScanner 新增 `LintRule.R11RuntimePurity`：禁 Console/DateTime.Now/Environment.TickCount/Stopwatch/Thread.Sleep/System.Net/Socket/Kcp*/IRoomTransport/using LiteNet/LiteNet. 限定名/Google.Protobuf/System.IO/File.*/new Random()（确定性随机走 Sim 的 RngState）。
- ScanTargets 新增目标 `RoomServer/Runtime`；DisciplineScannerTests 补命中/豁免/门控用例；真实源码**全部目标零违规**保持绿。

**测试面（净增 21 例：130→143 + L3 8→9 + 纪律 22→24）**

- RoomRuntimeReconnectTests（4）：**R1 退出条件**——纯 L1（无 Socket/Sleep/文件）完整一局+重连：Join×2→输入→断线（席位保留/对局继续）→Rebind→Restoring→RestoreAck→Active→继续到时限收尾（Finishing→Settling→Closed + SettlementReady）；实体归属不变；席位侧去重水位保留（陈旧帧不复活）；恢复中再掉线可再次重绑；终态拒绝重连。
- RoomClientReconnectTests（7）：断线相位/完整闭环（重拨+自动请求+验版本+Reliable ACK+幂等宣告）/版本不符→Failed 不发恢复 ACK/拒绝→Failed/无票据→Failed/超时（注入时钟零真实等待）/首连不自动重连。
- ServerHostTransportTests +1：假传输全链重连闭环（票据→重绑 Restoring→抑制增量→票据重放拒→恢复 ACK→整帧全量重锚；零 Socket）。
- RoomBroadcastTests +1：Restoring 双环抑制（其余成员不得被 fresh 水位拖成全帧）+ 恢复后整帧全量。
- HeadlessRunTests +1（L3 真链路）：真实 UDP 回环断线重连——席位保留/对局继续/恢复后增量广播继续/对局不中断；服务器侧以 `RestoresCompleted` 事实断言（相位在发送即翻转，规避处理竞态）。

**验证证据**

| 门禁 | 结果 | 命令 |
|---|---|---|
| L1 全量 | PASS 484 项（LiteFramework.Core 251 + LiteNet 143 + LiteSim 83 + LiteTesting 7；0 失败） | `scripts/test.ps1 -Lane L1 -Profile PullRequest` |
| L3 网络/无头集成 | PASS 9 项（8 + 新增断线重连真链路；30s 双端对跑不发散复验通过） | `scripts/test.ps1 -Lane L3 -Profile PullRequest` |
| Unity 编译 | recompile 0 错误（compilationFailed=false，errors=[]） | `unity command recompile/recompile_status --project-path … --proxy-disable` |
| 纪律扫描 | 24/24（含 R11 新用例；真实源码全部目标零违规——RoomServer/Runtime 已入扫描目标） | DisciplineScannerTests（L1 内） |
| BuildHash | 已重跑 `f71a4f1622b81490`（Proto/Protocol 变更后） | `python scripts/gen-build-hash.py` |

**已知边界与后置项（不阻塞 R1 验收）**

- ~~重连票据仍是可预测串、Join token 仅判非空（R2：CSPRNG/签名 Join Ticket/per-IP 限流/安全信封）——R2 完成前不得公网（R0 声明不变）。~~
  → 后记（2026-09-30）：CSPRNG 票据、签名 Join Ticket（M0-d）、per-IP 限流均已交付（[服务端R2安全](服务端R线.md#ch3-服务端R2安全)）；红线续期为**安全信封完成前不得公网**（信封另立专项）。→ 再后记（2026-10-03）：信封批1 已交付（[安全信封](服务端R线.md#ch4-安全信封)）；红线延续为部署门禁（真机核证/批2〔随 R4〕）闭合前不得公网。
- Restoring 无服务器侧恢复超时——依赖 kcp2k 连接超时自然回收；挂起恢复不占他人带宽（抑制的是发给该席位的包）。
- 重连输入历史不重放（HeadlessClient 形态：镜像即恢复终点帧，后续权威循环从该帧继续；历史重放调优留 M11 表现层消费）。
- RoomServer.Host/Generic Host、Options 校验、Transport.Abstractions/Kcp 拆 csproj、Worker Pool/有界 Mailbox、真实多房间与 drain——R2；结算信封/Outbox/归档——R3；结构化日志/Metrics——R4。
- 单房间 MVP：TryGetOrCreateRoom 返回唯一预置房间（R2 RoomRegistry 接管路由）；票据绑定校验在多房间形态下才有路由意义。

---

## 第3章 服务端 R2 安全批（重连票据 CSPRNG · 规范化远端地址 · 分层限流）：施工进度

<a id="ch3-服务端R2安全"></a>

> **原文件**：`Docs/施工进度/服务端R2安全.md`（归并前独立成文，内容整段并入本章）

> 依据：[《商业级通用服务端框架总设计》](../设计文档/架构/商业级通用服务端框架总设计.md) §P0-6（Reconnect Ticket 用 CSPRNG 生成、≥128 bit 熵、一次性、短期、绑定 Match/Player；"不得自创密码算法"；KCP cookie 不等同业务身份）、§4 冲突裁定「重连票据」「per-IP 限流」两行、§343（"票据表、发送 ledger、限流桶必须有容量上限和周期清理"）、§557（R2 交付面：Join/Reconnect Ticket、安全信封、远端地址和分层限流）、§595（Beta 门槛：限流可按 **IP/账号/Session** 生效）、§429（范围校验）、§520/§600（不把估算值写死为事实）。
> 本文件只记录施工状态与证据；目标与验收以设计为准，不在此重复定义。
> **范围裁决（2026-09-30，用户）**：本批只做 ①重连票据 CSPRNG ②规范化远端地址 ③分层限流；**④安全信封另立专项**。
> 承接 R2 批一（`3c90cbc`，固定 Worker Pool/有界 Mailbox 宿主接线 + 排空第 4 步本地结算 Outbox）。

### 1. 本批范围与未交付边界

**交付**（`a0f5129`，2026-09-30）：

| # | 交付 | 设计对应 |
| --- | --- | --- |
| ① | 重连票据 **CSPRNG 化**：16 B 随机数 → base64url 22 字符不透明串；旧 `rc{serial:x}-{playerId:x}` 可预测串（且把席位号编码进票据）删除 | §P0-6"CSPRNG 生成、至少 128 bit 熵"；一次性消费/短期 TTL/容量淘汰沿用 R1 既有语义 |
| ② | 规范化**远端地址暴露**：`IRoomTransport.GetRemoteAddress`（窄端口扩展）+ `KcpTransportServer` 实现（kcp2k `GetClientEndPoint`）+ `RemoteAddress` 归一（IPv4 去端口 / IPv4-mapped 归一为 IPv4 / IPv6 压缩小写 / 未知返回 null） | §4「per-IP 限流」行——"Transport 必须提供规范化远端地址和连接元数据后才可宣称实现" |
| ③ | **分层限流**：四条 lane（IP·连接 / IP·入场 / 账号·入场 / Session·包速率）、令牌桶突发+稳态、**桶表容量上限 + 空闲周期清理**、宿主四路径接线 + Ops 观测段 | §P0-6"远端地址限流"、§343（容量上限+周期清理）、§595（可按 IP/账号/Session 生效） |

**未交付**（如实标注）：

| 项 | 归属 | 说明 |
| --- | --- | --- |
| 安全信封（机密性/完整性/序号/重放窗口） | **专项另立**（用户裁决 2026-09-30）→ **批1 已交付（2026-10-03，[安全信封](服务端R线.md#ch4-安全信封)）** | 业务载荷仍无机密性与完整性保护；kcp2k V1.41 cookie 只解决 UDP 探测/放大防护。**信封完成前本服务不得直接暴露公网**——R0 声明红线的口径不变，只是本批把"重连票据/远端限流"两项从缺口清单里划掉。（后注 2026-10-03：信封批1 已交付——PSK 握手/HKDF/AES-256-GCM/64 位重放窗口；红线延续为部署门禁：IL2CPP 真机核证与密钥轮换〔批2，随 R4〕闭合前不得公网） |
| 非对称验签（HMAC → 公钥）与票据签发端 | G3 | 验签接缝已交付（M0-d，`IJoinTicketValidator`）；密钥来源/轮换随 Auth 端 |
| per-IP 限流的部署级容量实证 | R4/G5 | 桶容量/速率缺省值是**可用形态**不是实测值（§520/§600）；NAT 后真实玩家群的规模需容量测试定标 |
| 结算信封/归档 | R3 | 与本批无关，引用 §42 归属 |

### 2. 交付物与依据

| 交付 | 位置 | 设计依据 |
| --- | --- | --- |
| 重连票据 CSPRNG | `RoomServer/Application/ReconnectService.cs`（`TokenEntropyBytes = 16`、`RandomNumberGenerator.GetBytes`、base64url 去 padding） | §P0-6 |
| 规范化远端地址（纯函数） | `Assets/LiteNet/Transport/RemoteAddress.cs`（新增，含 .meta） | §4 冲突裁定「per-IP 限流」 |
| 传输端口扩展 | `Assets/LiteNet/Transport/IRoomTransport.cs` + `KcpTransportServer.cs` | 同上（"连接元数据"；null = 未知地址语义入契约） |
| 分层限流器 | `RoomServer/Application/RateLimiter.cs`（新增：`RateLimitSettings` + `RateLimiter`；显式虚拟时钟 `nowMs`） | §343、§595 |
| 配置装载 + 范围校验 | `RoomServer/RoomServerConfig.cs`（`rate_limit` 分区：四 lane 突发/速率 + `buckets` + `idle_ms`；缺省走 Default，越界/非对象拒载；`Describe()` 带 `rateLimit=[…]`） | §429、§520/§600 |
| 自带配置 | `Config/roomserver.json`（`rate_limit` 段 + `_comment` 说明） | 同上 |
| 宿主接线 | `RoomServer/ServerHost.cs`（OnConnected / OnTransportData / HandleJoin(IP+账号) / HandleReconnect 四路径；`PurgeIdle` 进周期清理；ctor 注入形态 + `RateLimit` 公开面） | §P0-6、§343 |
| 观测 | `RoomServer/Application/Ops.cs`（`rl:` 段：各 lane 拒绝数 / 表满数 / 回收数 / 当前桶数） | §514 观测前提 |

### 3. 四条 lane（参数与触发点）

| lane | 触发点 | 键 | 缺省 突发 / 稳态 | 超限动作 |
| --- | --- | --- | --- | --- |
| `IpConnect` | `OnTransportConnected`（kcp 握手后、会话登记**前**） | 规范化远端地址 | 32 / 4·s⁻¹ | `Disconnect(conn)`（不登记会话） |
| `IpEntry` | `HandleJoin` 首段 + `HandleReconnect` 首段 | 规范化远端地址 | 32 / 8·s⁻¹ | Join：`Reject`（计数+日志，不回 JoinAck）；Reconnect：**静默丢弃** |
| `AccountEntry` | `HandleJoin` 验签通过后 | `principal.AccountId` | 16 / 2·s⁻¹ | `Reject`（见 §4.2 nonce 已消费的代价） |
| `SessionPackets` | `OnTransportData`（超长包检查后、会话解析前） | connectionId（int，零分配） | 256 / 128·s⁻¹ | 静默丢弃 |

桶表：每 lane 独立容量上限 `buckets`（缺省 8192）；空闲超 `idle_ms`（缺省 120 s）的桶由宿主周期 `PurgeIdle` 回收。
缺省值出处 `RateLimitSettings.Default`，注释明示"可用形态，不是实测值"。

### 4. 关键裁决与实测踩坑

#### 4.1 重连超限 = **静默丢弃**（防放大）

重连洪水**不回 `ReconnectResponse`**：对洪水响应即放大，且任何明确拒绝都会向探测者泄露票据窗口状态。
与客户端语义一致：`RoomClient` 把拒绝/超时一律映射为终态 Failed（不自动重试），故"动态限流 + 静默丢弃"不会诱发重试风暴。
与 Join 的 `Reject`（既有出口：计数+日志，协议面本就不回 JoinAck）保持同一"不放大"原则。

#### 4.2 账号维度只能在验签之后（nonce 已消费——接受并写明）

验签前拿不到 `AccountId`，故账号 lane 位置只能在验签通过后；此时票据 nonce **已被消费**，被限即该票作废。
代价不对称但可接受：合法客户端入场频率远低于 16 突发/2·s⁻¹（缺省），且客户端对拒绝不自动重试。
该取舍已写入 `ServerHost.HandleJoin` 代码注释，不留给读者猜。

#### 4.3 桶表满 = 拒绝新键，不淘汰旧桶（安全 > 可用）

与 `HmacJoinTicketValidator` 的 nonce 窗口同纪律：淘汰会让攻击者用新键挤掉旧桶的欠账记录。
`RejectedTableFull > 0` 是运维信号：容量与来源规模不匹配，或正被分布式试探——据此调大配置，而不是自动清表。

#### 4.4 探测不到地址 → **跳过** IP 维度（不是全拒）

限不了看不见的地址：`GetRemoteAddress` 返回 null 时放行（假件/嵌入式/未握手形态不被误杀）。
"全拒"会打死本地开发形态与旧用例；"跳过"的可见性由 Ops 的 `ipConn/ipEntry` 计数与真实传输用例共同兜底。

#### 4.5 缺省值是可用形态，不是事实（§520/§600）

Default（32/4、32/8、16/2、256/128、8192、120 s）只保证"能用的起步形态"；生产经 `rate_limit` 分区显式给值。
NAT 规模、单连接包速率的定标归容量/压测（R4/G5），本批不把估算冒充实测。

#### 4.6 两处自纠（如实记录）

1. `RateLimitSettings` 从嵌套解平后签名残留裸 `Lane` → 编译红（CS0246），改 `RateLimitSettings.Lane`。
2. 计数器初版只声明未接线——加 `ref long rejected` 参数把拒绝数挂进各 lane，并明确 `RejectedTableFull`
   同时计入对应 lane（子原因），避免"表满了但 lane 计数不动"的观测假象。

#### 4.7 双轨归属（新文件落点均经核）

`RateLimiter`（Host 侧、带时钟）与 `ReconnectService` 改动落**根 `RoomServer/` 工程**——
`Assets/RoomServer/Application` 的 csproj 头注释明示"带 Socket/kcp2k/文件/时钟的 Application 文件
（ServerHost、ServerLoop、ReconnectService…）**不得**挪进来"。`RemoteAddress`（纯函数）住
`Assets/LiteNet/Transport`，随 LiteNet 双轨共编。全部新文件在 buildHash 闭包（Sim/Protocol/Proto/Data）之外。

### 5. 验证证据

| 门禁 | 命令 | 结果 |
| --- | --- | --- |
| L1 | `powershell -NoProfile -File scripts/test.ps1 -Lane L1 -Profile PullRequest` | **972 通过 / 1 失败 / 973 选择**——失败为 **BuildHash 快照既有红**（常量与快照不同步，Meta 记录在案），与本批无关：本批全部文件在哈希闭包之外。分项：LiteFramework 523/523、LiteNet 249/250、LiteSim 147/147、LiteTesting 7/7、MetaServer 46/46 |
| L3 | `powershell -NoProfile -File scripts/test.ps1 -Lane L3 -Profile PullRequest` | **126 通过 / 0 失败**（LiteNet 94、MetaServer.Integration 26、MetaServer 6） |
| L0 | `powershell -NoProfile -File scripts/l0-dep-scan.ps1` | **通过**（依赖可复现 / 零浮动依赖 / vendored 许可 / 无密钥命中） |
| Release 构建 | 由两条 lane 内的 `Tests.slnx` Release 构建覆盖 | 通过（门禁构建失败即 throw，未触发） |

**新增用例 34 例**（全部纯 .NET、显式虚拟时钟，无 Socket/墙钟）：

| 组 | 例数 | 覆盖 |
| --- | --- | --- |
| `RateLimiterTests` | 8 | 突发耗尽后按速率恢复 / 空闲再久令牌封顶在突发容量 / 时间回退不补令牌 / 表满拒新键且旧键不受影响 / 各 lane 独立同键不共享桶 / 空键放行 / 周期清理按空闲阈值回收并计数 / 清理后重建桶配额复位 |
| `ServerHostRateLimitTests` | 6 | IP 连接超限断开且空地址放行 / IP 入场超限 Join 被拒 / 账号入场超限第二张票被拒 / 重连超限**静默丢弃** / Session 包限流前段包照常处理 / 配置分区接入宿主生效 |
| `RoomServerConfigTests` | 14 | 缺失走缺省 / 部分字段覆盖 / 11 组越界拒绝 / 非对象拒绝；仓库自带配置回归补限流断言 |
| `RemoteAddressTests` | 4 | IPv4 去端口 / IPv4-mapped 归一（双栈同键）/ IPv6 压缩小写 / 未知返回空 |
| `SessionLifecycleTests` | 2 | 票据 CSPRNG 不可复现且熵宽达标（22 字符 base64url、16 B）/ 批量签发无重复 |

**假件随端口扩展**：`FakeRoomTransport` 补 `GetRemoteAddress` + `SetRemoteAddress`/`Disconnects` 观测面；
`ServerHostWorkerPoolTests.DisposeOrderTransport` 补一行实现（端口扩宽的编译面自证）。
全仓 `IRoomTransport` 实现者共三处（`KcpTransportServer`、`FakeRoomTransport`、`DisposeOrderTransport`），均已同步。

### 6. 记录

#### 2026-09-30 · R2 安全批①—③（`a0f5129`）

**范围裁决**（用户 2026-09-30）：本批只做 ①重连票据 CSPRNG ②远端地址暴露 ③分层限流；
**安全信封另立专项**——红线保持："信封完成前本服务不得直接暴露公网"。（后注 2026-10-03：信封批1 已交付，[安全信封](服务端R线.md#ch4-安全信封)；红线延续为部署门禁——真机核证/批2〔随 R4〕闭合前不得公网。）

**交付**：

- ① `ReconnectService`：`TokenEntropyBytes = 16`（128 bit）+ `RandomNumberGenerator.GetBytes` →
  base64url（去 padding，22 字符）；旧 `rc{serial:x}-{playerId:x}` 删除（可预测且泄露席位号）。
  一次性消费/5 分钟 TTL/容量淘汰沿用 R1 语义；类注释同步为"信道侧（安全信封）仍未达公开部署标准"。
- ② `RemoteAddress.Normalize`（IPv4 去端口、IPv4-mapped 归一、IPv6 压缩小写、未知 null）+ 
  `IRoomTransport.GetRemoteAddress` 端口（null 语义入契约）+ `KcpTransportServer` 实现
  （kcp2k 握手后 `OnConnected` 时地址已可达，注释指 `KcpServer.cs` 时序）。
- ③ `RateLimiter`（四 lane 令牌桶；同一把锁；`int` 键零分配；`nowMs` 显式传入——测试零墙钟）+
  宿主四路径接线 + `PurgeIdle` 进周期清理 + `Ops` `rl:` 观测段 + `rate_limit` 配置分区
  （缺省 Default、越界拒载）+ `roomserver.json` 自带示例。

**验证**：见 §5——L1 972/1（唯一失败为快照既有 BuildHash 红）、L3 126/0、L0 通过。

**未执行/未覆盖**（如实标注）：

- Unity/L2/Player 不跑（本机无编辑器/设备；本批为纯 .NET 面，证据类型与范围相符）。
- 真实多 IP 压测与桶容量定标归容量测试（R4/G5）；缺省值未被实测标定，也未冒充已标定。
- ~~**安全信封未做（专项另立）——完成前不得公网**~~ **批1 已交付（2026-10-03，[安全信封](服务端R线.md#ch4-安全信封)）；部署门禁（真机核证/批2〔随 R4〕）闭合前不得公网**；非对称验签随签发端（G3）。

---

## 第4章 安全信封：施工进度

<a id="ch4-安全信封"></a>

> **原文件**：`Docs/施工进度/安全信封.md`（归并前独立成文，内容整段并入本章）

> 依据：[《安全信封专项设计》](../设计文档/网络/安全信封专项设计.md)（目标契约与验收以设计为准，本文件只记录施工状态与证据，不在此重复定义）。
> 背景：R2 安全批（2026-09-30）交付重连票据 CSPRNG/远端地址/分层限流三件，**安全信封经用户裁决另立专项**（业务载荷无机密性/完整性/重放保护，完成前不得公网——红线见[服务端R2安全](服务端R线.md#ch3-服务端R2安全)）。**2026-10-03 接手**：并行会话交付信封内核与双端装配后中断，本会话接手收口（补攻击面/真传输测试、buildHash 源集、文档与索引）。

### 批次规划

| 批 | 范围 | 状态 |
|---|---|---|
| 批1 信封内核与双端装配 | `LiteNet/Transport/Security` 六件（Options/Crypto/Protocol/Session/双端装饰器）：PSK 三步握手 + HKDF-SHA256 派生 + AES-256-GCM 会话（帧头 AAD）+ 64 位重放窗口 + fail-closed 处置；RoomServer 装配（`--envelope-key`/环境变量，**未提供即拒启**，`--allow-insecure-local` 仅本机联调）；客户端 `BattleClient` 对称 fail-closed（真实 KCP 必须装配，注入传输/本地服不受影响） | **已完成**（2026-10-03） |
| 批2 运维接缝 | Ops 信封握手失败分类计数、密钥轮换（kid 对齐 `HmacJoinTicketValidator` 形态） | 未开始（随 R4） |

### 施工记录

#### 2026-10-03 · 批1 交付（并行会话实现 + 本会话接手收口）

**接手盘点**：并行会话已交付 `Assets/LiteNet/Transport/Security` 六件（约 900 行）与双端装配（`Program.cs` 服务器侧 fail-closed、`BattleClient`/`ProcedureMatch` 客户端侧对称）、4 例假传输测试；**缺**：握手攻击面用例、真 KCP 回环验证、buildHash 源集纳入、设计/进度文档与本索引。

**本会话接手补齐**：

- **攻击面用例 6 例**（假传输）：垃圾首包断开、ClientHello 不可靠信道拒绝、伪造 ClientFinish 拒绝不建会话、建立会话后重放握手包按解密失败处置断开、伪服务器 ServerHello 客户端验证明失败自断、载荷上限边界（64KB 通过/越界加密口抛）。
- **真实 KCP 回环 1 例**：信封装饰器套真 KCP 两端（127.0.0.1:28896），握手 + Join→JoinAck 全链可用（假件抹不掉的 Socket 时序/MTU/KCP 会话层由此钉住）。
- **buildHash 源集**：`Assets/LiteNet/Transport/Security` 进 `gen-build-hash.py` 闭包（52→58 文件，哈希 `13badf83…`）——信封是线上协议层，帧形状/握手版本变更必须两端同变、错配在 Join 握手被拒；KCP 适配层本体维持不入闭包（纯传输）。
- **文档**：本文件 + 《安全信封专项设计》（设计索引同步）+ `BattleClient` 新参数 XML 注释（fail-closed 口径写透）。

**验证证据**：

| 门禁 | 命令 | 结果 |
| --- | --- | --- |
| L1 全量 | `dotnet test Tests/LiteNet.Tests -c Release --nologo` | **382 / 0**（含信封 11 例：4 假传输既有 + 6 攻击面 + 1 真 KCP 回环；BuildHash 守卫绿——新源集已覆盖） |
| L0/依赖 | MetaServer/RoomServer 工程编译 | 零改动零回归（本批不触两宿主代码；MetaServer 硬化批同日并行无冲突） |

**已知边界**：

- **IL2CPP Player 的 AesGcm 可用性未真机核证**（Windows Player RSA 先例同路径，归真机矩阵）；Unity 侧 .NET Standard 2.1 面 API 兼容（`NET8_0_OR_GREATER` 分支已处理 tag 长度构造差异）。
- **密钥分发/轮换**：进程级 PSK，经环境变量/CLI 传入——生产 Secret Provider 与 kid 轮换归 G4/R4（批2）。
- **无会话内重钥**：对局分钟级时长下 GCM 单密钥帧量安全；超长会话评估归 R4（设计 §4 非目标登记）。
- **`--allow-insecure-local` 是显式逃生门**：仅本机联调；开启即横幅警告，公网部署审计按此红线。
- Unity EditMode/PlayMode 门禁随编辑器窗口期（LiteNet 源在 Unity 侧同编，dotnet 面已全绿）。

#### 2026-10-15 · 托管 AES-GCM 平台回退（接手收口）

**接手盘点**：前序会话交付工作区半成品四件——`IAesGcmCipher`（AEAD 端口）/`ManagedAesGcmCipher`（托管 SP 800-38D：AES-ECB 取块 + CTR + GHASH，常量时间标签比较、先认证后解密）/`SecureEnvelopeSession.CreateCipher` 平台探测回退（构造期一次，catch `NotSupportedException`——Unity Mono 无平台 AesGcm，构造即抛 `PlatformNotSupportedException`，Editor 实测信封建不起来 → JoinAck 永不到）/`ManagedAesGcmCipherTests`（互通硬契约对账）；门禁未跑。

**本会话收口补齐**：
- **`LiteNet.csproj` 补编 `Properties/AssemblyInfo.cs`**——显式 Compile 清单漏该目录，且**整个 Properties 目录从未入库**（磁盘孤儿件）：dotnet 轨 `InternalsVisibleTo` 从未生效，L1 internal 面首个消费者（本组互通用例）暴露该缺口；同批目录三件（cs/.meta/folder meta）入库。
- **注释日期戳去痕**（托管实现类注释——L1 注释卫生守卫红线）。
- **设计文档 v1.1**：《安全信封专项设计》§2 增 **AEAD 实现面契约**（端口/探测/回退/互通硬契约/先认证后解密/常量时间）、§5 增托管回退互通验收行、适用范围补两新件、版本头更新。

**验证证据**：

| 项 | 结果 |
| --- | --- |
| Unity recompile | **0 错**（两个新 Security 件导入 + 会话接线编译过） |
| LiteNet.Tests 全量 | **578/578**（含 `ManagedAesGcmCipherTests` **6 例**——40 组互通矩阵〔尺寸 × AAD：0..65536 逐位等价〕/双向互解/篡改三面拒收/错钥/空明文空 AAD/nonce 用法错误；+ `SecureEnvelope*` 回归 11 例；早前偶发红本轮全绿） |
| LiteSim.Core.Tests / LiteFramework.Core.Tests | **365/365** · **716/716**（纪律扫描含本批注释面） |
| L1 全量门禁（solution build） | **被本机活服进程文件锁拦**（MSB3027：MetaServer/RoomServer bin DLL 被 .NET Host 进程锁定——本批零改动宿主代码，信封为源链接同码双端；停服后可复跑取全量数字） |
| buildHash | 重跑 `gen-build-hash.py`：shared **`8ee08553b5e5f20f`**（102→104 文件）——留工作区随闭包收口（见各批隔离惯例） |

**边界/挂账**：
- **IL2CPP Player 的 AesGcm 可用性**仍随真机矩阵核证——不可用时同链路自动落托管回退（构造期探测，无常驻可变状态）。
- **Mono 路径性能**：托管 CTR/GHASH 慢于平台实现——仅 Editor Play Mode 与 Mono 桌面包联调路径触达；IL2CPP 出包走平台实现。
- 服务端（.NET 8）恒平台实现（`NET8_0_OR_GREATER` 分支）——互通由托管↔平台交叉对账钉住，两端帧形状不变。

---

## 第5章 Meta 服务宿主骨架：施工进度

<a id="ch5-Meta服务宿主"></a>

> **原文件**：`Docs/施工进度/Meta服务宿主.md`（归并前独立成文，内容整段并入本章）

> 依据：[《Meta 服务专项设计》](../设计文档/架构/Meta服务专项设计.md) §4.1（宿主选型）/§4.2（工程结构）/§5.2（错误与版本）/§10（配置、启动与关闭）/§11.2-11.3（Metrics/Health）/§15（施工映射 G1 行）；[服务端总设计](../设计文档/架构/商业级通用服务端框架总设计.md) §12（Generic Host、非零退出码）、§P0-3（解析前限制长度）、§P0-5（buildHash）；[《框架先行建设与业务接入专项设计》](../设计文档/架构/框架先行建设与业务接入专项设计.md) §4"持久化"行、§5-4"会话与房间端口"。
> 本文件只记录施工状态与证据；目标与验收以设计为准，不在此重复定义。

### 批次规划

| 批 | 范围 | 状态 |
|---|---|---|
| M0-a 宿主骨架 | `MetaServer/` 工程 + Generic Host 装配 + Options 范围校验 `ValidateOnStart` + `/live` `/ready` `/metrics` + 优雅关闭与 drain + 入站请求体上限 | **已完成**（见下） |
| M0-b 接缝登记 | gitignore 白名单、`Tests.slnx`、L0 纪律扫描目标（R11 纯化边界） | **已完成**（见下） |
| M0-c 持久化接缝 | 存储端口、迁移/事务/幂等约束、故障夹具、一个持久化样例（框架先行 §4"持久化"行） | **已完成**（2026-09-30：批一契约面 + L1、批二真 Mongo 实存储 + L3 重启恢复报告——见下） |
| M0-d 票据接缝 | `IJoinTicketValidator` 接口 + 非法票据测试（服务端总设计 §P0-6；§5-4"没有真实登录业务时也不能省略票据验证接口与非法票据测试"） | **已完成**（2026-09-26，见下） |

**范围界定**：本批（M0）只交付宿主骨架，**不含业务模块**——当时 Auth/Lobby/Profile 归 G3（《Meta 服务专项设计》§15），不提前建空壳模块。R3 Auth 首批已另立[服务端 R3 首批](服务端R线.md#ch6-服务端R3首批)记录，不回写为 M0 交付。

**M0-d 归属更正（2026-09-26）**：票据验证器接口**不在 MetaServer 工程内**，落在 `RoomServer/Application/`——
《服务端总设计》§7 的"建议最小接口"把 `IJoinTicketValidator` 列在 RoomServer 工程结构下，Meta 专项 §15
R2 行亦写"RoomServer 侧：Join Ticket **本地验签**"。Meta 侧只**签发**（§6.2"唯一身份接缝"）。
本表原把它记作 Meta 批次，属归类不准；实现按设计归属走。

### 施工记录

#### 2026-10-03 · MetaServer 硬化批（架构审查 P2/P3 修复：线程安全/脱敏/元数据权威/门禁化）

**背景**：2026-10-03 服务端架构审查登记的 MetaServer 侧债务，按"信封线并行不相交"原则收口本批
（不触 RoomServer/Transport；MetaServer 亦不在 RoomServer buildHash 闭包内，无哈希联动）。

**交付物**：

| # | 项 | 内容 |
| --- | --- | --- |
| H1 | `Ops` 线程安全 | Kestrel 并发请求共用同一实例——计数改 `Interlocked`（`CountRequest`/`CountRejected` 方法，读面保留只读属性）；`/metrics` 格式化改每次调用独立缓冲（原共享 StringBuilder 并发互踩）。L1 并发用例（8 线程 × 1 万次）钉住不丢计数 |
| H2 | Mongo 连接串脱敏 | 非法 URI 校验错误**不再回显原串**（可能含凭据，原样泄入 stderr/日志）；只报长度与形状提示 |
| H3 | Outbox 元数据存储端权威 | `OutboxEnvelope` 增 `CreatedAtUtc`/`LastFailureReason`（默认参数，既有 4 参构造零破坏）；**调用方只声明 OperationId/Payload**——入队时存储端强制 Pending/0/当前 UTC（修复 Attempts 播种）；`RecordFailureAsync` 改条件更新：**只对 Pending 生效**（已 Confirmed 幂等不累加、未知无操作）并落原因/时刻（修复"丢 reason 且 Confirmed 也累加"）。`OutboxDoc` 增三字段（Bson 无 schema，旧文档读回缺省值，**无迁移**、索引不变）。UTC 口径对齐 §5.2 |
| H4 | Program 退出诊断与 CLI 门禁 | 未知参数/`--bind` 缺值 = 配置错误退出码 2（修复"静默忽略拼错覆盖项"与 fail-closed 矛盾）；解析收口 `MetaHost.ParseCliOverrides`（L1 可测）；启动异常改打完整堆栈（`ToString`——迁移/依赖失败需要原始抛点） |
| H5 | 超时可配 | `MongoServerSelectionTimeoutMs`（默认 5000，100..60000）/`ReadyPingTimeoutMs`（默认 2000，100..10000）落 Options 管线（修复两处硬编码），范围校验进 `Validate` |
| H6 | TLS 门禁化 | http 绑定只允许回环地址（127.0.0.1/::1/localhost）；非回环 http 默认拒绝、需显式 `AllowNonLoopbackHttp=true`（受信内网豁免）——§12"外部接口一律 TLS"从注释落成启动期门禁 |

**验证证据**：

| 门禁 | 命令 | 结果 |
| --- | --- | --- |
| L1 | `dotnet test Tests/MetaServer.Tests -c Release` | **69 / 0**（+15 例：超时范围×4、回环三形态、非回环拒绝/豁免、https 不受门禁影响、连接串脱敏、CLI 解析×3、Ops 并发计数、Outbox 元数据权威×3） |
| L3 | `MONGO_TEST_URI=mongodb://127.0.0.1:27017/?replicaSet=rs0 dotnet test Tests/MetaServer.Integration.Tests -c Release` | **24 真跑 / 0 失败** + 2 例容器重启恢复跳过（docker 依赖，同既有记录口径；用户态 mongod 8.0.32 副本集）（后注：2026-10-04 docker 引擎就绪后全量容器矩阵复跑——两例实跑通过，Integration 26/26，[框架联合验收](框架先行与验收线.md#ch3-框架联合验收) §7） |

**已知边界**：`/ready` 未含 Outbox 积压阈值（依赖派发器语义，归 R3）；`appsettings.json` 版本化文件载体仍缺（需"代码默认值 vs 文件基线"单源裁决，登记待办）；`SampleSettlementCommand` 挪出 Contracts 与样例错误形状统一（归 G3 契约批）；安全信封由并行线施工中（后注：同日 2026-10-03 批1 已交付，[安全信封](服务端R线.md#ch4-安全信封)）。

#### 2026-09-30 · M0-c 持久化接缝（批二：真 Mongo 实存储 + L3 重启恢复报告）——M0-c 关闭

**范围**：MongoDB.Driver 装配裁决、真适配器三件、宿主接线（配置/启动迁移/样例端点//ready）、L3 容器夹具与用例、
**重启恢复报告**（§5-5"实际存储验证"+§14 L3 矩阵——M0-c 的完成判据）。本批达成，M0-c 标已完成。

**交付物**：

| 文件 | 内容 |
| --- | --- |
| `MetaServer.csproj` | **MongoDB.Driver 3.12.0**——服务端第一个真 NuGet。裁决登记于 csproj 注释：§4.1"零 NuGet"论证范围是 Web 包；§9.1 唯一权威存储/§14 Mongo 测试容器/§15 G1 存储端口共同要求真实驱动，自研线协议被 §1.1 否决。驱动只落 Infrastructure，Contracts 零驱动依赖 |
| `Infrastructure/Persistence/Mongo/MongoDocuments.cs` | 集合名单单源（迁移/适配器/L3 断言共用）+ 五个文档形状 |
| `Infrastructure/Persistence/Mongo/MongoSettlementLedger.cs` | 真结算账本：幂等双维度（_id＋业务键复合唯一索引）、**副本集事务内** CAS 推进＋账目写入（冲突/撞键整笔回滚）、Duplicate 返回首次快照、存储异常＝未确认（重试安全） |
| `Infrastructure/Persistence/Mongo/MongoOutboxStore.cs` | 真 Outbox：入队幂等（唯一 _id 裁判）、有界容量（满则 RejectedFull）、OutboxSeq 原子自增承载入队次序（$natural 无排序保证不用）、确认条件更新不回退、失败只累加计数 |
| `Infrastructure/Persistence/Mongo/MongoMigrations.cs` | 真迁移步骤 v1（ledger 建集＋业务键唯一索引）/v2（outbox 建集＋扫描索引）；**回滚＝结构回退（移除索引）不 drop 集合**——不销毁数据，步骤注释声明该与"完全互逆"的偏差 |
| `Infrastructure/Persistence/SchemaMigrationRunner.cs`（硬化） | 回滚后版本读回按尽力报告：存储不可达→recovered=-1＋rollbackFailed=true（原先裸异常穿透类型化结果承诺——死端口用例暴露） |
| `Host/MetaConfig.cs` | `MongoConnectionString`/`MongoDatabaseName`/`OutboxCapacity` 三项＋范围校验（连接串形状/配套库名/容量界） |
| `Host/MetaHost.cs` | 装配：连接串非空→注册 Mongo 家族（客户端/库/三端口/迁移执行器/样例用例/HostedService）；**功能门读**经 ConfigurationBinder 绑同节、**消费值**一律 IOptions（避开 2026-09-25 双实例失效）；`/ready` 在配置存储时 ping Mongo（2s 超时） |
| `Host/MigrationStartupService.cs` | 启动迁移；失败/被拒/存储异常统一 `InvalidOperationException` → **宿主拒绝启动（fail-closed，§9.1）** |
| `Host/SampleEndpoints.cs` | `POST /sample/settlement`——样例⑤"简单测试命令"HTTP 载体：accepted/duplicate 200（首次快照）、conflict 409（**领域体** `SampleConflict`：expected/actual 数字）、invalid 400、未配置存储/未确认 503。**错误体经 `MetaResults` 统一形状**（P2 批二）：`profile.disabled` / `profile.invalid-request`（违规清单进 `args`）/ `profile.store-unconfirmed`（内部 reason 不出体）——与 `SettlementEndpoints` 同一张 `ProfileErrorCodes` 表；判据=协议级错误→`{code,messageKey,args}`、领域结果（含 409）→领域 DTO。**缺省不挂载（P3）**：`EnableSampleEndpoints` 缺省 false——生产形态下该路径**不存在（404）**、`SettlementSampleUseCase` 也不注册；本地联调/L3 用例显式开启 |
| `Tests/MetaServer.Integration.Tests/`（5 件） | `MongoFixture`（MONGO_TEST_URI→探活→docker 自起容器含 rs.initiate；用例级独立库＋统一清理；`Xunit.SkippableFact` 显式跳过）＋四组 26 例 L3 |
| `scripts/test.ps1` | **命名契约守卫**（见下——防再次静默漏跑） |
| `Tests.slnx`/`.gitignore` | 新工程登记（含 csproj 白名单） |

**三处实测教训（如实）**：

1. **发现契约静默漏跑（本批最大陷阱）**：设计 §4.2 树形命名 `MetaServer.IntegrationTests` **不匹配** test.ps1 的
   `*.Tests.csproj` 发现通配符（"IntegrationTests.csproj" 不含 ".Tests.csproj" 子串）→ 门禁**静默丢弃**该工程
   （L3 总数 93 而非 118，无任何报错；M0-b 记录的警告以"照设计文本命名"的方式重现）。修复＝工程改名
   `MetaServer.Integration.Tests` 满足契约＋test.ps1 加守卫（`Tests/**/*.csproj` 不满足契约即显性 throw）。
   **门禁通配符是硬约束，设计文本让位并留痕**。
2. **docker 输出管道死锁**：`RunDockerAsync` 等退出后再读 stdout——`docker inspect` 的 KB 级 JSON 填满管道缓冲、
   进程写阻塞不退出 → 恒定 10s 超时 → docker 被误判不可用（重启用例 1ms 假跳过、容器时间戳证明 restart 根本没执行）。
   修复＝并发排空输出管道。定位靠指纹："每用例恰 10s＝inspect 超时值"。
3. **R11 豁免问题的实际答案＝无需豁免**：适配器纯编排（驱动在 NuGet 包内不被扫描）、自写代码不触碰 R11 禁用原语
   ——L1 纪律扫描零违规实证。批一登记的"两处注释矛盾"以"Infrastructure 保持 R11-clean by construction"化解，
   `ScanTargets.cs` 零改动。

**验证证据**（非原开发机；Mongo＝Docker `litegame-mongo` 容器，mongo:8.0 **单节点副本集 rs0**——事务要求副本集，§8.1）：

| 门禁 | 结果 |
| --- | --- |
| L1 全量 | **882 通过 / 1 失败**——失败为 BuildHash 快照既有红（批一定性：快照提交内容与常量不同步、远端 main 同样存在，与本批无关）；含纪律扫描：**驱动适配器首次落 Infrastructure，R11 零违规** |
| L3 全量 | **119 通过 / 0 失败 / 0 跳过**（LiteNet.Tests 87 ＋ MetaServer.Integration.Tests **26** ＋ MetaServer.Tests 6） |
| 冒烟（手验，非门禁） | 宿主**进程级**重启：两次起 `MetaServer.exe`（env `META_Meta__Mongo*`），同命令重发 → `duplicate` 首次快照；mongosh 核验 schema_version=2、`ux_settlement_key` 唯一索引在（**集合自动建不算证据，索引才是迁移实证**） |

**重启恢复报告**（§14 必备故障矩阵逐项——M0-c 完成判据）：

| 矩阵项 | 证据 | 形态 |
| --- | --- | --- |
| 实际持久化确认 | 首次提交 accepted＋介质可查（mongosh 直读账目）；迁移建索引可证 | L3＋冒烟 |
| 重复提交（两维度） | 同操作号/同业务键换操作号 → Duplicate 首次快照；**重复提交一百次只生效一次**（§17"幂等"行样例级闭环） | L3 |
| 提交边界（CAS 冲突） | 真实事务**整笔回滚**：账目无残留、修订未动；携带实际修订号重试成功 | L3 |
| 响应丢失 | L1 语义钉（FailAfterCommit→未确认→重试命中首次）；宿主级重发 POST → 200 duplicate 首次快照 | L1＋L3 |
| 进程恢复 | **宿主进程重启**（冒烟手验）；**容器重启**（`docker restart` → 账目仍在、Outbox 待处理恢复/确认不复活，2 例）；客户端重建（新 IMongoClient，多例） | L3＋冒烟 |
| 迁移失败 | v2 注入失败 → v1 **真实回滚**（唯一索引移除＋版本归零）；死端口 → 宿主拒绝启动（fail-closed） | L3 |
| OS 级"确认点前后杀进程" | **未做**——事务提交边界＋介质级重启覆盖确认点语义；OS kill 矩阵归 G4 真机（与热更专项 §8 杀进程同批） | 后置（如实登记） |

**已知边界**：

- 事务内并发 write-conflict **不自动重试**：以未确认抛出、调用方重试（驱动可重试写不覆盖多语句事务提交冲突）。
- Outbox 容量检查与插入非原子：并发窗口可短暂越界——容量是显式承诺不是硬不变量（契约注释声明）。
- M0-c 记录中的样例端点是样例⑤载体**非业务 API**（错误形状简化为 code+data；§5.2 正式
  `{code,messageKey,args}` 归 G3 Contracts）。
- R3-Auth-1 游客登录、R3-Lobby（实例注册/容量分配/Join Ticket 签发端）与 **R3-Profile（结算提交消费/结果归档查询/提交管道，2026-10-10，[服务端R3-Profile](服务端R线.md#ch8-服务端R3-Profile)）已独立落地**；真实 Auth 的正式账号/刷新吊销（R3-Auth-2）仍归后续 R3 批次。
- Redis、TLS/Dockerfile/Secret Provider/编排面归 R4；`BuildHash` 占位未接（§P0-5）。

#### 2026-09-30 · M0-c 持久化接缝（批一：契约面 + L1 语义段）

**范围**：按"非本机开发、不跑 Unity"约束只做纯 .NET 可验部分；L3 实存储段（Mongo 驱动装配、副本集容器夹具、进程级重启恢复报告）待具备 Mongo/容器环境后施工（批二）。
**状态判据**：批一交付 ≠ M0-c 完成——§5-5"只做 fake 存储不能证明重启恢复"，完成以 L3 重启恢复报告为准。

**交付物**：

| 文件 | 内容 |
| --- | --- |
| `Contracts/Persistence/SettlementLedger.cs` | 结算账本端口 `ISettlementLedger`：**双维度幂等**（operationId + (playerId, matchId, settlementType)，唯一索引为最终裁判，§11.2/§11.3）、首次应用 = 唯一账目 + revision CAS 推进的**同一原子边界**（CAS 冲突整笔回滚不残留）、`Duplicate` 携带**首次快照**（重复提交不重新计算）、`SettlementStoreUnavailableException` = 未确认语义（不冒充成功、重试安全） |
| `Contracts/Persistence/Outbox.cs` | 持久 Outbox 端口 `IOutboxStore`：**有界容量**（满则显式 `RejectedFull`，§11.2"任何队列必须有显式容量与清理策略"）、入队幂等（Confirmed 条目不复活不重投）、**失败保持可重试**只累加计数（§10"未提交项保持可重试状态"）；`OutboxStatus` 刻意两态——失败不落独立状态，重试语义由状态本身承载。**装配状态（P3）**：宿主 DI **不再注册**（无可接消费者——Meta 结算路径是事务内落账、重试归 RoomServer 侧 Outbox）；端口/实现/L1+L3 用例保留为 §14-G1"接缝"交付物，G3 接通 Meta 外发写时补一行注册 + 关闭第 3 步刷新 + `/ready` 未超阈 |
| `Contracts/Persistence/SchemaMigration.cs` | 迁移契约：`IMigrationStep`（Migrate/Rollback 必须互逆、Migrate 可重复应用）、`ISchemaVersionStore`（**版本写回 = 确认点**，任意一步后进程终止、重启从最后确认版本继续）、`MigrationOutcome` 四态（失败显式报告"哪一步、回到哪版、回滚是否失败"——§9.1 不静默半迁移） |
| `Contracts/Persistence/SampleSettlementCommand.cs` | 样例⑤"简单测试命令"DTO + `Validate()` 一次报全（与 `MetaConfig.Validate` 同口径）；**非业务模型**——§5-5 不冻结库存/奖励语义 |
| `Infrastructure/Persistence/SettlementSampleUseCase.cs` | 样例命令消费者：验证 → 原子应用 → `SampleCommandResult` 五态（Accepted/DuplicateHit/Conflict/Rejected/Unconfirmed）；**非业务模块**（Auth/Lobby/Profile 归 G3；生产装配缺真实存储时拒绝启动、不悄悄退回替身） |
| `Infrastructure/Persistence/SchemaMigrationRunner.cs` | 迁移执行器：步骤表连续性校验（缺口/重复/降级/越界全部显式拒绝）→ 逐版本应用每步写回 → 失败**逆序回滚含失败步本身**并显式报告；全程经端口注入，无墙钟/无 IO |
| `Tests/MetaServer.Tests/`（4 文件） | `PersistenceTestDoubles.cs`（外持状态替身＝"介质比进程活得久"的 L1 形态；故障注入 `FailBeforeCommit`/`FailAfterCommit`）+ 三组契约测试 28 例（SettlementPersistence 11 / OutboxPersistence 7 / SchemaMigration 10，Trait=Contract） |

**登记面**：零新工程、零 csproj——`Contracts/`、`Infrastructure/` 落盘即自动受 R11 扫（M0-b 已预留）；`Tests.slnx`、`.gitignore`、`ScanTargets.cs` 本批零改动。

**过程性记录（如实）**：

1. **CAS 冲突测试场景首建模错误**（首跑 2 例红暴露）：冲突笔误用同业务键（match-1/settle）——同键换操作号按契约属**重复提交**（上游重复签发），不属冲突。改为"同账号、不同 match、修订号过期"。该修正同时把"幂等判定先于 CAS"的优先序钉进了测试。
2. **弱断言自纠**：首批 9 处 `Assert.Equal(1/0, Count)`（xUnit2013）已全部改 `Single/Empty`——意图表达明确。既有 `MetaConfigTests` 3 处 xUnit1031 警告非本批引入，未动。

**验证证据**（本机为**非原开发机**：zip 快照、无 .git；dotnet SDK 10.0.301 编 net8.0——`global.json` `latestMajor` 允许；本机执行策略拦 ps1 → 以 `-ExecutionPolicy Bypass` 运行）：

| 门禁 | 结果 |
| --- | --- |
| L1 全量 | **882 通过 / 1 失败**：LiteFramework.Core.Tests **523**（含纪律扫描——MetaServer 新增 `Contracts/`、`Infrastructure/` **首次受扫，R11 零违规**）、LiteNet.Tests 159/160、LiteSim.Core.Tests **147**、LiteTesting.Core.Tests **7**、MetaServer.Tests **46**（18 既有 + **28 本批**） |
| L3 全量 | **93 通过 / 0 失败**（LiteNet.Tests 87 + MetaServer.Tests 6；本批只加 L1 用例，跑 L3 是为确认改了测试工程本身后既有集成用例零回归） |

**1 例 L1 失败为快照既有问题，与本批无关**（证据链，待原机裁决）：
`BuildHash_与当前源码复算一致_未忘记重跑生成器` 红——常量 `55b99f87388c9b13` vs 快照复算 `0ccf063313acf873`。① python 生成器（`gen-build-hash.py`）只读复算同得 `0ccf…`——跨语言口径一致、算法无漂移，快照内容自洽；② 本批 10 个新文件全部在该 53 文件闭包之外（`MetaServer/`、`Tests/`）；③ 文件时间戳全部为解压时刻（zip 重置），无法定位差异文件。**判定：快照提交内容与其常量不同步**（原机常量生成自含未提交生成物的工作树，或常量重生成后的文件未全量推送）。**未擅动**——buildHash 是跨端协议门禁，本机重生成会把快照现状铸成新门禁值、与原机 `55b9…` 分叉。

**未完成（批二，待 Mongo/容器环境）**：

- **MongoDB.Driver 引入裁决**：服务端第一个真 NuGet（M0-a 的零 `PackageReference` 只约束 Web 包，§4.1 论证范围；§14 已点名 Mongo 测试容器）——契约零驱动依赖的分层已就位，驱动只落 `Infrastructure/`，走 C0-① 依赖治理/许可扫描登记。
- **R11 豁免裁决**：真实 IO 落 `Infrastructure/` 时需明确豁免范围——`ScanTargets.cs` 两处注释说法不一（`MetaPurityRules` 注释称"宿主装配层合法使用 Web/IO 故排除在外"，`MetaHostExcludes` 实际只排 `Host/`）。本批以"Infrastructure 保持 R11-clean"回避了该冲突。
- 真 Mongo 适配（结算账本两个唯一索引 + 事务、Outbox 持久实现、真实迁移步骤）＋ **单节点副本集**容器夹具（§8.1 副本集模式——事务在单机模式不可用，事务重试用例跑不了）。
- L3 用例与接线：真实存储确认/重复提交/提交前后**进程级终止**/重启恢复/迁移失败矩阵；`/ready` 判定接真依赖（Mongo 可达、Outbox 未超阈，§10）；样例命令的 HTTP 入口接线（缺真实存储时拒绝启动）。
- 完成后形成**重启恢复报告**，M0-c 方可标已完成。

#### 2026-09-26 · M0-d Join 票据验证接缝交付

**目标**（§5-4"没有真实登录业务时也不能省略运行时的票据验证接口与非法票据测试"）：
接口形状由设计钉死为 `IJoinTicketValidator.Validate(string ticket, JoinContext)`（服务端总设计 §7），
故失败不走异常也不走 bool，而是返回 `JoinPrincipal` 带 `JoinTicketRejection` 分类。

**交付物**（全部在 `RoomServer/Application/`，R11 纯化边界之外）：

| 文件 | 内容 |
| --- | --- |
| `JoinTicket.cs` | 接缝：`IJoinTicketValidator` / `JoinPrincipal` / `JoinContext` / `JoinTicketKey` / `JoinTicketRejection`（11 分类） |
| `JoinTicketFormat.cs` | **线格式单一来源**：字段序、Base64Url 编码、被签名覆盖的范围。签发端与验证端共用 |
| `JoinTicketValidator.cs` | `HmacJoinTicketValidator`：验签 + 六项绑定 + 有界 nonce 重放窗口 |
| `ServerHost.HandleJoin` | **接入真实准入路径**：装配验证器后 token 逐一验签，非空不再构成准入理由 |

**三处设计要点（都是被"测试要测它声称测的那件事"逼出来的）**：

1. **线格式单一来源**。测试签发器 `TestTicketIssuer` 与验证器共用 `JoinTicketFormat`——
   若测试侧另写一份拼串，签发端漂移时验证端仍然全绿。这与工程既有的"协议单源红线"是同一条纪律。
2. **重放判定排在最后**（④时效 → ⑤绑定 → ⑥重放）。若提前，攻击者可用乱签票据耗尽 nonce 窗口
   = 拒绝服务。`重放判定在验签之后_伪造签名不占用nonce窗口` 用**同一 nonce** 先坏签名后真签名钉住顺序。
3. **nonce 窗口满时拒绝新票据，不淘汰旧条目**。淘汰等于把已用过的 nonce 放出窗口 = 重开重放口子。
   安全 > 可用；`RejectedWindowFull` 计数 > 0 表示宿主未及时 `PurgeExpiredNonces`。

**一轮实测踩坑（记录以免重犯）**：准入用例最初 12/34 失败——`Ctx()` 的受众缺省值写成了 `"any"`，
而签发器缺省是 `"test"`，于是**全组用例都先撞 AudienceMismatch**；又因受众校验**排在房间/哈希/版本之前**，
把后三者的失配一并掩盖了。两个教训：绑定项判定的**顺序**决定了失配时的可诊断性；
跨文件缺省值不一致时，症状会出现在与原因无关的地方。

**验证证据**：

| 门禁 | 命令 | 结果 |
| --- | --- | --- |
| L3 | `scripts/test.ps1 -Lane L3` | **57 通过 / 0 失败**（LiteNet.Tests 由 9 → **51**，+42 即本批；MetaServer 6） |
| L1 | `scripts/test.ps1 -Lane L1` | **761 通过 / 0 失败**（本批用例标 `Integration`，按分层归 **L3** 不占 L1） |

**未完成**：~~Meta 侧 Join Ticket 签发端（后续 R3-Lobby）~~ **已于 R3-Lobby 交付（2026-10-08，[服务端R3-Lobby](服务端R线.md#ch7-服务端R3-Lobby)——签发单源走 `JoinTicketFormat` 源链接共编）**；非对称验签（R2 可选）；~~重连票据 CSPRNG 化（R2）~~ 已于 2026-09-30 交付（[服务端R2安全](服务端R线.md#ch3-服务端R2安全)）；~~远端限流~~ 已交付（同上），~~**安全信封仍缺（另立专项）**~~ **批1 已于 2026-10-03 交付（[安全信封](服务端R线.md#ch4-安全信封)）；批2 运维接缝随 R4**。

#### 2026-09-25 · 宿主骨架交付（含三处实测缺陷修正）

**① 工程与宿主（M0-a）**

- `MetaServer/MetaServer.csproj`：`Microsoft.NET.Sdk` + `<FrameworkReference Include="Microsoft.AspNetCore.App" />`，**零 `PackageReference`**（实测 `project.assets.json` 的 `libs: 0`）；`net8.0` / `LangVersion 10.0` / `Nullable disable` / `ImplicitUsings disable`，与 `RoomServer` 同档除语法版本。
- `MetaServer/Host/MetaConfig.cs`：宿主装配参数（BindAddress / MaxInboundBytes / ShutdownTimeoutSeconds）+ `Validate()` 返回全部违规项。
- `MetaServer/Host/MetaHost.cs`：`Build()` 单一装配路径（测试与生产共用）、`WireGracefulShutdown()`、健康端点、请求计数中间件。
- `MetaServer/Host/Ops.cs`：进程级计数 + Prometheus 文本最小出口（`/metrics`）。
- `MetaServer/Host/BuildHash.cs`：`"meta-0.0.0-unwired"` **显式占位**——`gen-build-hash.py` 接线随 R2/G3，不伪造权威哈希（§20 完成定义第 1 条）。
- `MetaServer/Host/Program.cs`：`Build()` 与 `StartAsync()` 同置于捕获 `OptionsValidationException` 的 try 内；非法配置打印全部违规项并**返回退出码 2**。

**② 语法档位裁决修正（文档同步）**

`LangVersion` 取 **10.0** 而**非** `RoomServer` 的 9.0。2026-09-25 实测：C# 9 下最小 API 惯用写法全部编译失败（`CS1593` target-typed lambda / `CS1503` 方法组 / `CS0030`），只有逐端点显式委托转换可用。该实测**证伪**了《Meta 服务专项设计》§1.1 原写的"与 RoomServer 同档"，已按总设计 §0 规则 2 更新文档。

**③ 三处静默失效缺陷：实测发现并修正**

本批首版"看起来"符合 §10/§12，实测后确认三处**不抛异常、不报警告**的失效，均违反设计：

| # | 缺陷 | 实测证据 | 违反条款 |
|---|---|---|---|
| 1 | `ValidateOnStart` 形同虚设 | 探针：`MaxInboundBytes=10`（越下限）下宿主**启动成功** | §10/§12"不能带默认错配置继续运行" |
| 2 | 配置源全部静默失效 | 字段形态类经 `ConfigurationBinder` **完全不绑定**；文件/环境变量/命令行覆盖一律读不到 | §10"版本化文件、环境变量" |
| 3 | 入站上限未落地 | `MaxInboundBytes` 只是配置对象里的数，**从未限制请求体** | §P0-3"包体在解析前限制长度" |

根因与修正：

- **实例身份**：`AddSingleton(config)` 与 `AddOptions<MetaConfig>()` 解析出**两个不同实例**，`IValidateOptions` 校验的是没人使用的那个。改为配置**只由 Options 管线持有一份**，消费方一律经 `IOptions<MetaConfig>`。
- **绑定形态**：`ConfigurationBinder` **只绑定属性**，`RoomConfig` 式的 public 字段会让配置源完全不生效。`MetaConfig` 改用**属性**——这是与 `RoomConfig` 的**有意差异**，已在两处注释说明。
- **限制落地**：`MaxInboundBytes` 接入 `KestrelServerOptions.limits.MaxRequestBodySize` 与 `FormOptions.MultipartBodyLengthLimit`；`ShutdownTimeoutSeconds` 接入 `HostOptions.ShutdownTimeout`（原配置项未接线，属装饰）。
- **删除手写预校验**：首版在 `Program` 里手写了一次 `config.Validate()` 兜底——它恰好掩盖了缺陷 1，且只看命令行覆盖、看不到配置文件。已移除，改用设计要求的 `ValidateOnStart` 作为唯一门禁。
- **`HttpRejected` 实装**：原字段有注释无写入（恒为 0），现按响应状态码（≥400）记账。

装配契约（三个陷阱的规避方式）已写入《Meta 服务专项设计》§10，回归钉落在 `Tests/MetaServer.Tests/MetaConfigTests.cs`。

**④ 接缝登记（M0-b）**

- `.gitignore`：补 `!MetaServer/*.csproj` 与 `!Tests/MetaServer.Tests/*.csproj`。**不加则工程文件被 `*.csproj` 全局忽略、交付静默丢失**。
- `Tests/Tests.slnx`：追加 `MetaServer` 与 `MetaServer.Tests`。slnx 内的非测试工程**必须**被某测试工程 `ProjectReference`，否则 `test.ps1` 的 no-restore 构建失败——已由测试工程引用满足。
- `Assets/Tools/DisciplineScanner/Scripts/ScanTargets.cs`：新增 `MetaServer` 扫描目标（`MetaPurityRules` = R11），排除 `MetaServer/Host`（宿主装配层合法使用 Web/IO/Console，§12）。**`Contracts/`、`Modules/`、`Infrastructure/` 落盘即自动受扫**，无需再改配置。
- `Tests/LiteFramework.Core.Tests/DisciplineScannerTests.cs`：补登记意图用例（`纪律_MetaServer_登记为受守根_仅宿主层豁免`）。
- 首次扫描命中 `MetaServer/` 根下文件——根因是文件未按 §4.2 结构落位，已迁入 `Host/`。

### 测试面

- `Tests/MetaServer.Tests/`（命名必须匹配 `*.Tests.csproj`，否则被 `test.ps1` 静默漏跑）。
- **零依赖 HTTP 集成测试**（§4.1 依据 5）：`WebHost` 绑定 `http://127.0.0.1:0` 取临时端口 → `IServerAddressesFeature` 取回真实地址 → `HttpClient` 打真实回环。**不用 `WebApplicationFactory`/`TestServer`**（两者都要 `PackageReference`，与 §4.1 零 NuGet 约束冲突）。
- L1（18 例）：配置范围校验边界、`覆盖项_经IOptions生效`、`入站上限_落到Kestrel限制`、`关闭时限_落到HostOptions`、`非法配置_拒绝启动`。
- L3（6 例）：存活/就绪分离、drain 后 live 仍 200 而 ready 转 503、指标出口无高基数字段、请求/拒绝计数、未知路由 404、多实例端口隔离。

### 验证证据

| 项 | 命令 | 结果 |
|---|---|---|
| L1 | `powershell -NoProfile -File scripts/test.ps1 -Lane L1 -Profile PullRequest` | **630 通过 / 0 失败**（含 MetaServer 18；纪律扫描"全部目标零违规"绿） |
| L3 | `powershell -NoProfile -File scripts/test.ps1 -Lane L3 -Profile PullRequest` | **15 通过 / 0 失败**（含 MetaServer 6） |
| 非法配置 | `MetaServer.exe --bind "not-a-uri"` | 打印违规项，**退出码 2** |
| 正常路径 | `MetaServer.exe --bind http://127.0.0.1:18322` + curl | `/live`→200、`/ready`→200、`/metrics`→计数（4 请求 / 1 拒绝）、未知路由→404 |

**本次未跑 L2/Unity**：纯 .NET 新增，未触碰 `Assets/` 下 Unity 侧代码——除 `ScanTargets.cs` 与 `DisciplineScannerTests.cs`（纯 C# 工具与测试，不参与 Unity 编译）。**未跑 Player 构建**，本批不涉及。

### 已知边界

- **M0-c 已完成（2026-09-30，批一契约面＋批二真 Mongo 实存储）**：存储端口/约束/夹具/样例全部落地，重启恢复报告见批二记录（§14 矩阵逐项）。后置边界：OS 级杀进程矩阵归 G4 真机；R3-Auth-1 游客登录与 R3-Lobby 已另批落地（[服务端R3-Lobby](服务端R线.md#ch7-服务端R3-Lobby)），正式身份、Profile、Redis/编排面仍按后续 R3/R4 批次执行。
- **M0-d 已交付但范围有限**：交付的是**验证接缝 + HMAC-SHA256 参考实现 + 非法票据矩阵**，落在 RoomServer 侧。
  - **算法是共享密钥 HMAC，不是非对称签名**。§P0-6 允许"本地公钥**或共享验证器**"，框架期 Meta/RoomServer
    同信任域故取后者；换非对称只替换 `HmacJoinTicketValidator` 一个类，接口与消费者不变。
  - **密钥由部署注入**（`--ticket-key <kid>:<base64>`），仓库内**没有**密钥生成/登记工具——
    ~~Meta 侧签发端的实现归 G3（Auth/Lobby），本批只交消费端~~ **Meta 侧签发端已交付（R3-Lobby，
    2026-10-08，[服务端R3-Lobby](服务端R线.md#ch7-服务端R3-Lobby)：`LobbyTicketSigner` 走 `JoinTicketFormat` 源链接单源，与本节验证器同 key）。**
  - **未装配验证器时退回原型级非空校验**：这是刻意保留的联调形态（否则全部历史用例与本地联调齐断），
    但**未验证 ≠ 已验证**——`Session.Principal` 保持 null，且启动时打印显式告警（§6"不能悄悄退回 fake"）。
    生产装配**必须**传 `--ticket-key`。
  - ~~**重连票据仍未达标**：`ReconnectService` 签发的仍是**可预测串**（非 CSPRNG），归 R2，见该文件类注释。~~
    → **已于 2026-09-30 交付**（R2 安全批①：16 B CSPRNG → base64url 不透明串，[服务端R2安全](服务端R线.md#ch3-服务端R2安全)）。
- `BuildHash` 为占位值，未接 `gen-build-hash.py`——按 §P0-5，该字段在接线前**不可**用作版本身份或签名。
- 生产编排面（TLS、限流分层、Secret Provider、Docker）未接，归 R4/§12。
- 本批无业务端点，`/metrics` 为最小文本出口；正式 Prometheus/OTel 出口归 R4（§11.2）。

---

## 第6章 服务端 R3 首批：施工进度

<a id="ch6-服务端R3首批"></a>

> **原文件**：`Docs/施工进度/服务端R3首批.md`（归并前独立成文，内容整段并入本章）

> 依据：[《Meta 服务专项设计》](../设计文档/架构/Meta服务专项设计.md) §6（Auth）/§9（Mongo）/§10（配置与启动）/§14（测试矩阵）；[《上云测试专项设计》](../设计文档/架构/上云测试专项设计.md) §2～§6（游客登录闭环的首段）。
> 提交：`8c95bfc`（本机复跑 L1 1472/1472 ＋ L3 真容器 145/145 确认）。
> 本文件只记录已执行的施工状态与验证证据；设计裁决只写入设计文档。

### 批次规划

| 批 | 范围 | 状态 |
|---|---|---|
| R3-Auth-1 游客登录 | 访问令牌格式与 HMAC 签发/校验、游客账号 get-or-create、Mongo `accounts` 集合与 `ux_account_device` 唯一索引、`POST /auth/guest`、Auth 配置门与稳定错误码 | **已完成（附验证证据）**——真实 Mongo 容器证据已补齐（本机 docker 可达，2026-10-08，L3 145/145 全实跑，见边界与下方施工记录） |
| R3-Auth-2 正式身份 | 账号绑定、刷新令牌、吊销与受保护端点中间件 | 未开始 |
| R3-Lobby | 房间注册/容量分配、Join Ticket 签发与查询 | **已完成（附验证证据）**——见[服务端R3-Lobby](服务端R线.md#ch7-服务端R3-Lobby) |
| R3-Profile | 结算提交消费、Ledger/Inventory、结果查询与 Archive | **已完成（附验证证据）**——见[服务端R3-Profile](服务端R线.md#ch8-服务端R3-Profile) |

### 施工记录

#### 2026-10-08 · R3-Auth-1 游客登录闭环

已落地以下代码路径：

- `Server/MetaServer/Contracts/Auth/`：Auth 错误码、游客登录命令/响应、访问令牌线格式与拒收分类。
- `Server/MetaServer/Modules/Auth/AccessTokenService.cs`：CSPRNG `jti`、HMAC-SHA256 签发、`kid`/签名/声明/过期/受众/用途的 fail-closed 校验；时钟由宿主注入，令牌不写日志。
- `Server/MetaServer/Modules/Auth/GuestLoginUseCase.cs`：`deviceId` get-or-create；唯一键竞态通过“插入返回 null 后重读”收敛；`requestId`/`deviceId` UTF-8 字节边界校验；不以进程内字典替代存储。
- `Server/MetaServer/Contracts/Persistence/AccountStore.cs` 与 `Server/MetaServer/Infrastructure/Persistence/Mongo/MongoAccountStore.cs`：账号存储端口及 Mongo 适配器，创建时刻由存储端赋值。
- `Server/MetaServer/Infrastructure/Persistence/Mongo/MongoDocuments.cs`、`MongoMigrations.cs`：`accounts` 集合与 `ux_account_device` 唯一索引，迁移版本提升到 3，保留可重复迁移与结构回滚语义。
- `Server/MetaServer/Host/MetaConfig.cs`、`MetaHost.cs`、`AuthEndpoints.cs`：Base64 密钥、`kid`、`audience`、TTL 范围校验；配置密钥才注册 Auth；Mongo 缺失不回退假存储；接入 `POST /auth/guest`，返回稳定 `auth.disabled` / `auth.store-unavailable` / `auth.invalid-request` 错误码。

### 验证证据

| 层级 | 命令 | 结果 |
|---|---|---|
| MetaServer 构建 | `dotnet build Server/MetaServer/MetaServer.csproj --no-restore -c Release` | **通过，0 警告 / 0 错误** |
| Auth 与宿主 L1 | `dotnet test Tests/MetaServer.Tests/MetaServer.Tests.csproj --no-restore -c Release` | **90/90 通过，0 跳过** |
| Meta 集成工程构建 | `dotnet build Tests/MetaServer.Integration.Tests/MetaServer.Integration.Tests.csproj --no-restore -c Release` | **通过，0 警告 / 0 错误** |
| Mongo L3 | `dotnet test Tests/MetaServer.Integration.Tests/MetaServer.Integration.Tests.csproj --no-restore -c Release` | **1 通过 / 0 失败 / 29 跳过**；账号/迁移真存储用例已纳入，当前 Mongo/docker 不可达，不能作为真实 Mongo 验收证据；**→ 本机复跑（docker 可达，2026-10-08）：L3 车道 145/145——本工程 30/30 全实跑（0 跳过），账号存储/迁移真容器证据补齐** |
| 全量 L1 | `pwsh -NoProfile -File Scripts/gate/test.ps1 -Lane L1 -Profile PullRequest -ResultsDirectory TestResults/r3-auth-l1-final` | **1472/1472 通过，0 失败**；产物目录 `TestResults/r3-auth-l1-final/` |

### 边界与下一批

- 本批不启动 Unity，不跑 EditMode/PlayMode/IL2CPP；客户端一键游客入口归上云测试专项批 C，需本机 Unity 环境。
- ~~当前环境未形成真实 Mongo 容器运行证据；实现、L1 语义和集成用例已具备，接入可达 Mongo 后必须补跑并记录账号存储与迁移用例~~ → **已补跑（本机 docker 可达，2026-10-08）：L3 车道 145/145（本工程 Integration 30/30 全实跑、0 跳过）——账号存储与迁移真容器证据已记录。**
- **R3-Lobby 已先行交付（2026-10-08，[服务端R3-Lobby](服务端R线.md#ch7-服务端R3-Lobby)）**——封闭测试关键路径需要 Meta 侧 Join Ticket 签发端，先于 R3-Auth-2；**R3-Profile 已交付（2026-10-10，[服务端R3-Profile](服务端R线.md#ch8-服务端R3-Profile)：结算提交/结果归档查询/提交管道/SIGTERM）**；正式账号绑定/刷新吊销（R3-Auth-2）仍按后续批次实施。

---

## 第7章 服务端 R3-Lobby 批：施工进度

<a id="ch7-服务端R3-Lobby"></a>

> **原文件**：`Docs/施工进度/服务端R3-Lobby.md`（归并前独立成文，内容整段并入本章）

> 依据：[《Meta 服务专项设计》](../设计文档/架构/Meta服务专项设计.md) §6.2/§7/§12（Lobby：实例注册、容量驱动分配、drain 协同、版本准入、Join Ticket 签发）；[《上云测试专项设计》](../设计文档/架构/上云测试专项设计.md) §2/§4/§5（封闭测试"游客→进房"链的签发端）。
> 提交：`3da054f`（本机复跑 L1 1525/1525 ＋ L3 156/156 确认）。
> 本文件只记录已执行的施工状态与验证证据；设计裁决只写入设计文档。

### 批次规划（R3 线）

| 批 | 范围 | 状态 |
|---|---|---|
| R3-Auth-1 游客登录 | 访问令牌/游客账号/`POST /auth/guest`（见[服务端R3首批](服务端R线.md#ch6-服务端R3首批)） | 已完成（附验证证据） |
| **R3-Lobby** | 实例注册/心跳（TTL 清扫、有界）、容量驱动分配（跳过排空/满员）、版本准入、**Join Ticket 签发**（`JoinTicketFormat` 单源）、房间投影查询、RoomServer 注册客户端（含排空上报） | **已完成（附验证证据）** |
| R3-Auth-2 正式身份 | 账号绑定、刷新令牌、吊销与受保护端点中间件 | 未开始 |
| R3-Profile | 结算提交消费、Ledger/Inventory、结果查询与 Archive | **已完成（附验证证据）**——见[服务端R3-Profile](服务端R线.md#ch8-服务端R3-Profile) |

### 施工记录

#### 2026-10-08 · R3-Lobby 实例注册与 Join Ticket 签发端

**Meta 侧（分配与签发）**

- `Server/MetaServer/Contracts/Lobby/LobbyContracts.cs`：稳定错误码表（`lobby.invalid-request` / `lobby.instance-unauthorized` / `lobby.disabled` / `lobby.no-capacity` / `lobby.version-mismatch` / `lobby.registry-full`）与 DTO（注册/心跳、签发命令与响应、房间投影；访问令牌失败复用 `auth.unauthorized`）。
- `Server/MetaServer/Modules/Lobby/InstanceRegistry.cs`：内存实例注册表——**注册即心跳**（get-or-create 幂等键 = `instanceId`；更新以换对象表达，快照读者不见半更新）；**有界**（表满拒新实例，既有实例心跳不受影响）；心跳 TTL 惰性清扫（时钟注入——L1 用虚拟时钟造超时，不 sleep）；**容量驱动分配**（跳过排空与满员；排序确定：玩家占用 → 房间占用 → Ordinal 实例标识）；版本过滤并**区分"版本冲突"与"无容量"**（处置不同）。
- `Server/MetaServer/Modules/Lobby/LobbyTicketSigner.cs`：签发端——组装/签名走 **`JoinTicketFormat`（源链接共编，单源红线）**；nonce = CSPRNG 128 bit；`MatchId`/`SimVersion` 置空（对局由房间创建自带 MatchId；Sim 版本分离未启用——不为"占位"编造值）。
- `Server/MetaServer/Modules/Lobby/JoinTicketIssueUseCase.cs`：字段边界（UTF-8 字节上限同游客登录口径）→ 分配 → 按实例上报的构建哈希**盖章**签发；结果型返回（端点映射 HTTP）。
- `Server/MetaServer/Host/LobbyEndpoints.cs`：
  - `POST /lobby/instances/register`——**实例密钥** Bearer（常量时间比较；Base64 解码后比对）；
  - `POST /lobby/join-ticket`——**访问令牌**鉴权（消费 AccessTokenService——Lobby 为首个受保护端点，§6.1 注释预留的消费方由此接入）；分配 + 签发；
  - `GET /lobby/rooms`——房间投影查询（同实例密钥鉴权）。
- `Server/MetaServer/Host/MetaConfig.cs` / `MetaHost.cs` / `Ops.cs`：Lobby 配置门（**半段配置拒启**：两密钥 + Auth 密钥三缺一即启动失败；密钥 ≥32 字节、kid 形状同房间端约束、TTL/心跳/容量范围校验）＋装配（消费值经 `IOptions`，与 Auth/持久化块同款）＋指标 `meta_lobby_instance_register_total` / `meta_ticket_issue_total`（有实装才声明）。
- `Server/MetaServer/MetaServer.csproj`：**源链接 `Assets/RoomServer/Application/JoinTicketFormat.cs`** 共编——签发与验签同一份形状规则（《上云测试》§2"物理上不会漂"）；该文件只依赖 BCL，不把 LiteNet/proto 带进 Meta。

**RoomServer 侧（注册客户端）**

- `Server/RoomServer/LobbyRegistrationClient.cs`：启动首发 + 周期心跳；载荷 = 宿主实时快照（buildHash = `ServerBuildHash`、容量、占用、**排空位**、实际监听端口回读）；**失败不退出对局服务**（失败计数 + 节流日志 + 下周期重试——Lobby 不可达只影响"新玩家分配"）；单在途请求、无队列；HTTP handler 可注入（L1 伪造 handler 直测载荷与失败路径）。
- `Server/RoomServer/RoomServerConfig.cs`：`lobby` 分区（url 空 = 不注册；url 配置时 `instance_id`/`advertise_host` 必填、**半段配置拒启**、心跳间隔 1000..300000ms 范围校验）；`Describe()` 含 lobby 段。
- `Server/RoomServer/HostAssembly.cs`：**装配收敛专项批2 首批对象**——Lobby 注册客户端注册进容器（惰性工厂；解析序=创建序 → 释放逆序天然"先停心跳、再收宿主"）。
- `Server/RoomServer/Program.cs`：`--lobby-instance-key` / `LITENET_LOBBY_INSTANCE_KEY`（**密钥不进配置文件**；配置 url 缺密钥启动即拒——fail-closed）；心跳在排空期间继续（`Draining=true` 上报至进程退出——§7 drain 协同的上行）。
- `Server/RoomServer/ServerHost.cs`：`OccupiedPlayerCount`（已进房席位占用；未进房连接不算占用）。
- `Config/roomserver.json`：lobby 分区说明 + 占位（url 空 = 默认关闭）。

**实施中实测（一处）**：

- **同步 `HttpClient.Send` 与自定义 handler 不兼容**：首版客户端用同步 `Send`，测试替身（只覆写 `SendAsync` 的 `HttpMessageHandler`）直接抛 `NotSupportedException`。改 `SendAsync + GetAwaiter().GetResult()`（专用心跳线程上同步等待，无同步上下文不可能死锁）——真 handler 与测试替身同一形态，7 例客户端用例转绿。

### 验证证据

| 层级 | 命令 | 结果 |
|---|---|---|
| 双服务构建 | `dotnet build Server/MetaServer/MetaServer.csproj -c Release` / `Server/RoomServer/RoomServer.csproj -c Release` | 0 错误（MetaServer 0 警告；RoomServer 仅 Luban 生成物既有 CS8981 警告） |
| 全量 L1 | `powershell -File scripts/gate/test.ps1 -Lane L1 -Profile PullRequest -ResultsDirectory TestResults/r3-lobby-l1-final` | **1525/1525 通过，0 失败**（1472 → +53；产物目录 `TestResults/r3-lobby-l1-final/`） |
| 跨端同源闭环（L1） | `Tests/MetaServer.Tests/JoinTicketIssueUseCaseTests.cs` | Meta 签发票据 → 房间端**真实** `HmacJoinTicketValidator` 验签通过；篡改/过期/受众/房间不符/重放分类逐项断言——**签发端形状漂移本组必红** |
| 注册表与用例（L1） | `LobbyRegistryTests` / `MetaConfigTests` / `LiteNet.Tests/LobbyRegistrationClientTests` / `RoomServerConfigTests` | 心跳超时/表满/分配确定序/版本过滤；配置半段拒启；客户端载荷字段与排空位透传、失败不抛 |
| L3 真 HTTP 闭环 | `Tests/MetaServer.Tests/LobbyHttpTests.cs`（Integration trait，真 Kestrel + 真回环 HTTP） | **11/11**：实例注册（鉴权/字段/表满）→ 分配 → **真 `LobbyRegistrationClient`** → 签发 → 房间端验证器验签全链；心跳超时/排空窗口/无容量/版本 409/令牌拒绝各路径 |
| L3 车道 | `powershell -File scripts/gate/test.ps1 -Lane L3 -Profile PullRequest -ResultsDirectory TestResults/r3-lobby-l3` | **156/156 通过，0 失败**（145 → +11＝LobbyHttpTests；本工程 MetaServer.Integration.Tests **30/30 真容器全实跑、0 跳过**） |
| L0 依赖与密钥扫描 | `powershell -File scripts/gate/l0-dep-scan.ps1` | 1 条**既有**红（`JoinAdmissionGateTests` 测试字面量误报，非本批引入，留裁决）；**本批零新增** |
| buildHash | `python scripts/codegen/gen-build-hash.py --check` | **CHECK OK：`07dbe182f67d0e69` 不变（75 文件）**——本批全在 hash 闭包外（Sim/协议/表未动） |
| 冒烟（三形态） | `RoomServer.exe --duration`：①裸 KCP ②信封 ③lobby 启用（指向不可达 Meta） | 三形态 **exit 0**、严格 60Hz（180/210 ticks、掉时债 0）；lobby 形态失败按节流日志提示"对局服务不受影响"，恢复后自动续报 |

### 边界与下一批

- 本批未启动 Unity，不跑 L2 EditMode/PlayMode/Player（本机无 Unity 环境）；buildHash 未变，Unity 侧无需重编译对齐。
- **注册表驻进程内存**：单 Meta 实例形态；多 Meta 实例部署需共享存储（Redis 注册表——《Meta 服务专项设计》§9.2"可丢失/可重建"数据）。本批不做，属部署形态。
- Meta 侧**未做限流分层**（§12）：封闭测试由 IP 白名单 + 规模约束覆盖；正式公网接入前需补（随 Auth-2 或 R4）。
- Lobby 完整语义未做：实例心跳在途健康判定降级、实例再平衡、`MatchStartSnapshot`（归 R3-Profile）。「Join Ticket 签发与查询」的"查询"落为房间投影 `GET /lobby/rooms`。
- 进程级 Ctrl+C 排空手验沿用 2026-10-04 三场景基线（[服务端多房间](服务端房间线.md#ch1-服务端多房间)）；本批"排空位上报"以两侧证：L1 载荷透传 + L3 排空实例拒分配；未重跑进程级全链。
- 客户端一键游客入口与结果页（《上云测试》批 C）需 Unity 环境；`POST /matches/result`、`GET /matches`、RoomServer 提交管道与 SIGTERM 排空（批 A 剩余项）归 R3-Profile——**已于 R3-Profile 交付（2026-10-10，[服务端R3-Profile](服务端R线.md#ch8-服务端R3-Profile)）**。

---

## 第8章 服务端 R3-Profile 批：施工进度

<a id="ch8-服务端R3-Profile"></a>

> **原文件**：`Docs/施工进度/服务端R3-Profile.md`（归并前独立成文，内容整段并入本章）

> 依据：[《上云测试专项设计》](../设计文档/架构/上云测试专项设计.md) §2/§3 批A/§4 裁决点①③/§5/§6；[《Meta 服务专项设计》](../设计文档/架构/Meta服务专项设计.md) §8.2 结算链/§11.3。
> 提交：`5022173`（本机复跑 L1 1570/1570 ＋ L3 163/163 确认）。
> 本文件只记录已执行的施工状态与验证证据；设计裁决只写入设计文档。
> **接手说明**：本批接手并行线在途 WIP（账本半部：AppendMode + `Contracts/Profile` + `ApplyMatchResultUseCase` + 其测试），在其上收口为"一条提交、双侧落库"并补齐端点到管道全链——合并方案与边界见下。

### 批次规划（R3 线）

| 批 | 范围 | 状态 |
|---|---|---|
| R3-Auth-1 / R3-Lobby | 见[服务端R3首批](服务端R线.md#ch6-服务端R3首批)/[服务端R3-Lobby](服务端R线.md#ch7-服务端R3-Lobby) | 已完成 |
| **R3-Profile** | 结算提交消费（账本 Apply 半部）+ **归档与按账号查询** + 房间侧**提交管道**（重试/退避/重启续投）+ 结算载荷 SeatAccountIds + **SIGTERM 排空** | **已完成（附验证证据）** |
| R3-Auth-2 | 正式身份（账号绑定/刷新/吊销） | 未开始（封闭测试范围外） |

### 施工记录

#### 2026-10-10 · R3-Profile：结算链闭环（并行线 WIP 接手合并）

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
- `SettlementSubmitService.cs`（本批建）：Outbox 待提交头部 → POST Meta（实例密钥）→ 200 即 `TryMarkCompleted`（duplicate 同样算完成——Meta 幂等是最终裁判）；失败保持待提交 + **指数退避封顶**（日志节流）；**重启续投零内存记账**（进度全在日志）；载荷 delta 经**注入的奖励策略**换算（P3 起：`ISettlementRewardPolicy` + `NoEconomyRewardPolicy` = 无经济口径 0；当时为隐式占位，P4 定口径后只改装配一行）。
- `RoomServerConfig`：`settlement` 分区（`submit_url` 空 = 关闭；非绝对 URL/半段配置/区间越界拒启）；`HostAssembly` 注册（**装配收敛批2 第二批对象**）；`Program` fail-closed（启用但缺实例密钥拒启）+ 启动日志（含待提交数）。
- **SIGTERM 排空接线**：`Program` 常驻分支注册 `PosixSignalRegistration(SIGTERM)`——容器 stop/K8s 与 Ctrl+C 同排空路径（`context.Cancel=true` 阻止默认终止）；不支持该信号的平台如实降级 null（本地仍由 Ctrl+C 承担）。
- `Config/roomserver.json`：settlement 说明 + 占位（url 空默认关闭）。

### 验证证据

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

### 边界与下一批

- 本批不启动 Unity（L2/EditMode/PlayMode 未跑——本机无 Unity 环境）；`buildHash` 重录与 Unity 重编译对齐随出包门禁（承接夜批裁决）。
- **结算增量**：封闭测试无经济系统——账本 revision 推进/余额不变。**P3 已把它从"隐式 0 占位"改为"显式接缝"**：`ISettlementRewardPolicy`（唯一换算点，`BuildPayload` 只经它取 delta）+ `NoEconomyRewardPolicy`（当前口径）+ `Settings.RewardPolicy` 装配注入；真实 RewardDelta 口径归 P4（换算在提交方，Meta 不接受客户端上报也不重算），落定时只改装配一行。**不在此之前自造数字**：设计只定义了 RewardDelta 的形状与归属，全仓无奖励口径（§520/§600"不把估算值写死为事实"）。
- 完整 §6 端到端（真实 KCP 双客户端 + 登录→进房→对局→可查→**杀进程重启**→重放）未整体重跑：本批以"②闭环（真 HTTP+真 Mongo+真管道+重启续投）+ 既有 KCP 对局级用例（LiteNet.Tests 真传输/真 Runtime→Outbox）"两段衔接取证；**完整进程级冒烟归批B 部署前执行**（SIGTERM 亦在该环境实证——本机 Windows 无法注入 SIGTERM）。
- 已知边界：提交管道头部条目若被 Meta 永久拒绝（如旧行无账号），会以退避持续重试并显式计数（不静默丢弃、不阻塞对账；封闭测试全新环境不产生此类行）。
- 并行线合并说明：其账本半部语义（AppendMode/双维度幂等/fan-out）原样保留；本批新增归档面挂在同一用例内（"一条提交、双侧落库"），错误码沿用其 `profile.*` 族——不另立第二套结算错误码。

---

## 第9章 R4 可观测首批：RoomServer /metrics 零依赖出口

<a id="ch9-R4可观测首批"></a>

> **原文件**：`Docs/施工进度/R4可观测首批.md`（归并前独立成文，内容整段并入本章）

> **批次**：R4-可观测-①（G4 关口首个增量）　**日期**：2026-10-12　**状态**：已完成（附验证证据，未提交）
> **来源**：《[商业级通用服务端框架总设计](../设计文档/架构/商业级通用服务端框架总设计.md)》§11.2"出口 /metrics 走
> Prometheus/OpenTelemetry"、§16 R4 交付；《Meta服务专项设计》§11.2 红线口径（禁高基数标签）。
> **裁决**：R4 全量为 OTel/Dashboard/告警/部署/长稳/故障注入（3～5 周档），本批只取**第一刀**——
> RoomServer 侧 /metrics 低成本文本出口（MetaServer Ops 先例同款），**零新依赖**（BCL HttpListener）；
> OTel/正式出口归后续 R4 批，出口本体可整体替换而不动计数面。

### 交付

- **`Ops.FormatMetrics()`**（`Server/RoomServer/Application/Ops.cs`）：Prometheus 文本最小子集（`# TYPE`
  + `name value` 行），**30 个指标**（29 counter + rooms/mismatch_last_frame 两 gauge）——计数值与周期行
  `Format` **同源对账**（读面同名，肉眼可核）；只出**进程级累计**，刻意不含高基数字段（roomId/连接号——
  §11.2 红线）与房间内瞬时量（帧号/快照归逐房间周期行）；低频出口逐调用独立缓冲，不与周期行复用缓冲互踩。
  房间数 gauge 经 `BindRoomsGauge(()->host.RoomCount)` 委托注入（Ops 住 Application 层不认识宿主）。
- **`MetricsHttpListener`**（`Server/RoomServer/MetricsHttpListener.cs`，public——同 ServerHost 对测试工程
  的既有先例）：HttpListener + 单后台线程；**只认 GET /metrics**（诊断端点零可写面），其余 404；单请求失败
  不杀出口线程；Dispose 幂等。绑定面缺省 `+`（跨机抓取），`Start(port, host)` 可指定 localhost
  （测试/无 urlacl 形态）。
- **`RoomServerConfig.MetricsPort`**：根字段 `metrics_port`（0 = **关闭**，缺省——行为零变化；1..65535 范围
  装载期校验；`Describe()` 可见）。Program 装配：`metrics_port > 0` 启动出口并打印端点；**Start 失败走
  告警降级**（宿主继续运行，无 /metrics——可观测面不打断服务可用性；Windows 非管理员绑 `+` 需
  `netsh urlacl`）。
- **用例**（`Tests/LiteNet.Tests/RoomMetricsTests.cs` 5 件）：零计数全集格式与不缺项断言（≥30 值行——将来
  静默丢指标在此断红）/ 计数与周期行同源 / gauge 未绑定恒 0、绑定随源 / 配置三态（缺省 0、显式 9101、
  越界与类型错装载期拒）/ HTTP 出口语义（GET 200 文本、其余 404、Dispose 后不可达）。

### 验证

- **定向**：RoomMetricsTests 5/5；LiteNet.Tests L1 全量含新例全绿。
- **L3**（真 Mongo `litegame-mongo` 容器 + 真 HTTP，2026-10-12 复跑）：LiteNet Integration 109/109 ·
  MetaServer.Integration 34/34——PR 口径（排除 LongRunning）全绿。
- **BuildHash**：`--check` 双常量不变（`Server/RoomServer` 不在共享/Meta 源集；Ops.cs 属宿主侧，与
  `Assets/RoomServer/Application` 双轨无 IO 子集**分属两目录**，无双轨漂移面）。

### 边界与后续（如实）

- **不做**（归 R4 后续批，不冒充完成）：OTel/Trace 接入、Dashboard/告警、`/live` `/ready` 探针、
  部署/灰度/回滚演练、24h 长稳、故障注入、P8 集成压测（目标容量须由产品并发目标定——§17 不虚构）。
- **挂账**：`Ops.Format` 的 `outRej=` 与逐房间 gauge（如帧号）未进 metrics——逐房间维度属有界标签
  （≤max_rooms），等 OTel/正式出口批一并裁。
