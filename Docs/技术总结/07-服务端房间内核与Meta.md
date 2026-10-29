# 07 · 服务端：房间内核与 Meta

> **本章回答**：权威房间如何步进与裁决、输入闸门拦什么、宿主如何编排多房/重连/排空/结算、Meta 如何登录/建房/签票/持久化。
> **上游权威**：[服务端总设计](../设计文档/架构/商业级通用服务端框架总设计.md)、[Meta 服务专项设计](../设计文档/架构/Meta服务专项设计.md)、[服务端宿主装配收敛专项设计](../设计文档/架构/服务端宿主装配收敛专项设计.md)。字段与端点全表见 [12-联机全栈参考](../项目说明/12-联机全栈参考.md)。
> **口径**：2026-10-22 工作区；Room 内核与客户端共编同一份 `LiteSim.Core`。

## 1. 三层边界：纯内核 / 无 IO 应用 / 带 IO 宿主

| 层 | 目录 | 程序集 | 允许 | 禁止 |
|---|---|---|---|---|
| 纯内核 | `Assets/RoomServer/Runtime` | `RoomServer.Runtime` | 只引 `LiteSim.Core` | **R11**：Console/系统时钟/文件 IO/proto/LiteNet |
| 无 IO 应用子集 | `Assets/RoomServer/Application` | `RoomServer.Application.Runtime` | proto、快照/会话编排 | 文件/网络 IO |
| 可执行宿主 | `Server/RoomServer` | `RoomServer` | Socket/HTTP/文件/DI | —— |

`Application` 在 Unity 侧也有 asmdef（用 vendor Protobuf DLL），但 `autoReferenced:false`，只有开发档 `LiteGame.Dev`（`Dev/LocalServer/`）引用，不进入正式客户端运行链。这条分层不是审美：它让**房间裁决可以在 L1 里被纯函数式测试**，不需要起进程/开端口。

## 2. 权威步进：`StepFrame` 的固定顺序

`RoomRuntime.StepFrame`（`Assets/RoomServer/Runtime/RoomRuntime.cs`）：

| # | 动作 | 说明 |
|---:|---|---|
| 1 | `AuthSim.Events.Clear()` | 事件是帧内瞬态 |
| 2 | `frame = AuthSim.Frame + 1` | inputDelay=1 语义 |
| 3 | 逐席位 `Gate.TryConsume(frame, i)` | 缺席用空输入；有 pending 开火则打 `ButtonDeferHit` |
| 4 | `SimStep.Step(..., advanceMatch:false)` | 权威步进 |
| 5 | `StepsCount++` | 诊断 |
| 6 | 逐席位核验本帧**真产** `Fire` 事件（武器门放行才算）→ 清 `ButtonDeferHit` | 空仓/冷却/死亡不能旁路资源门 |
| 7 | `CaptureEvents(frame, fireOnly:true)` | **必须在回溯清缓冲之前** |
| 8 | `SnapshotHistory.Capture`（第 1 次） | 回溯基料 |
| 9 | `_recentInputs.Record` | 重连补发基料 |
| 10 | `LagComp.RecordInputs` | 回溯历史输入 |
| 11 | `ConsumePendingFire` | 逐个射手回溯判定 + `FlushCommands` + 事件捕获 |
| 12 | `MatchSystem.Tick` | **整帧含回溯之后**才判胜 |
| 13 | `SnapshotHistory.Capture`（第 2 次） | 覆盖最终计分/比赛态 |

两个易误解点：

- **Match 不在 `SimStep` 内**（生产传 `advanceMatch:false`），所以"Match 最后"只是默认形态；生产形态是"回溯判定之后"。
- **同帧 Capture 两次**：第 1 次供回溯，第 2 次覆盖最终状态；环成本 ×2，但换来回溯与计分的一致性。

## 3. 输入闸门：14 项校验

`InputGate` 是纯校验器（不持世界状态），单帧逐项检查，违规按原因计数并丢：

| 组 | 校验 | 计数 |
|---|---|---|
| 消息形状 | `Count > MaxFrames(16)`；`Frames == null` 或 `Count > Length` | `DroppedOversizedMessage` / `DroppedOutOfRange` |
| 帧号 | 帧号上限 `serverFrame+8`；窗口取主帧；`frame<=0`；已消费帧 `frame<=serverFrame` | `DroppedOutOfRange` / `DroppedStaleFrame` |
| 意图 | 按键白名单；切枪槽位 `[0,2)`；离散 seq 非零且严格递增（同帧同 seq 例外） | `DroppedIllegalButtons` / `DroppedIllegalWeaponSlot` / `DroppedIllegalActionSeq` |
| 数值 | Move/AimPoint 有限；Move 分量 ≤1、长度²≤1；Fire/Skill 帧 AimPoint 非零；**瞄准点距离闸** `dist² ≤ (射程×1.1)²` | `DroppedNonFinite` / `DroppedIllegalVector` |

补充语义：

- **同帧去重是"后到生效"**（未消费前允许覆盖，只计数不丢）。
- **EntityId 一律覆写**为会话实体（防伪）。
- **`ackSnapshot` 越界只钳位 + 计数，不丢输入**。
- **未来帧预存**：`(serverFrame+2 .. serverFrame+8]` 逐帧同口径校验后入库。
- 存储环 `PendingInputRing`：容量 32 槽、玩家上限 32、`slot = frame & 31`、**换代即整槽清空**。

**距离闸的意义**：客户端上报的是世界坐标 AimPoint，若不限制距离，客户端可送超远点让服务器从枪口朝任意方向扫射。闸门把"打得到"变成可验证约束。

## 4. 回溯补偿：以"玩家看到的过去"为裁判

`LagCompensator.CompensateFire`：

| 环节 | 行为 |
|---|---|
| 目标帧 | `targetFrame = viewFrame`（客户端渲染帧，不再减插值） |
| 窗口 | clamp 到 `[auth.Frame-16, auth.Frame]`；捏造只会被夹到边界 |
| 退化 | 历史窗口关闭/目标帧不在环/取不到输入 → `RunDegraded`（退化为提前量判定） |
| 射手 | `TryResolve` 失败 → `InvalidShooter`；**射手取当前位/朝向/武器**，仅当 Hp>0 才拷武器行 |
| 判定 | 暂存当前态 → 恢复到目标帧 → 单跑 `ShootingSystem`（带 `ButtonFireFlag`）→ 过滤历史命中 |
| 落账 | 命令/事件写回**当前帧**，由本步 `FlushCommands` 结算 |
| 同帧互杀 | 后结算射手取**历史 Hp/武器行**；活着的射手用当前武器行覆盖 |

结果四态：`NoFire / Compensated / DegradedFire / InvalidShooter`，计数进 Ops。

## 5. 比赛生命周期与席位

```text
Created(0) → WaitingForPlayers(1) → Starting(2) → Running(3)
   → Finishing(4) → Settling(5) → Closed(6)
任意 → Aborted(7)
```

| 规则 | 行为 |
|---|---|
| 开局 | 房主 `StartEarly` 是**唯一开局入口**；满员不自动开局 |
| 进房闸 | `Starting/Running/Finishing/Settling` 一律拒 |
| 席位 | 取最低空位；满员拒；`NextPlayerId` 高水位只升不降 |
| 断线 | 未开局断线即释放席位；对局中断线**保留席位**（重连窗内可重绑） |
| owner | 首个入座者；离席迁给最低在线席位；旧房主重连**不夺回** |
| 非法迁移 | `IsAllowed` 是唯一真值；非法迁移输出 `Rejected:`，**不改状态不抛** |

## 6. 内核与宿主的契约：命令进、事件出、快照拉

- **命令入**（8 种）：`AuthenticatedJoin / ClientInput / Disconnect / Rebind / Tick / Shutdown / RestoreAck / StartEarly`。
- **事件出**：`SignalOutput`（`PlayerAdmitted`/`MatchStarted`/`SeatRestored`/`OwnerChanged`/`SeatsChanged`）、`JoinRejectedOutput`、`MatchStateChangedOutput`、`SettlementReadyOutput`、`RoomAbortedOutput`、`StartRejectedOutput`。
- **快照不走输出事件**：`SnapshotPipeline` 在每次权威步进后**拉取** `AuthSim`/`Gate` 构建下发，避免第二套快照 DTO。

这条契约让内核保持"纯状态机"：它不知道 socket、不知道 proto、不知道房间表。

## 7. 宿主：组合根、60Hz Pump、多房编排

### 7.1 装配

- `HostAssembly.Register/Build` 是 MS DI 组合根，`ValidateOnBuild` + `ValidateScopes` 常开；容器只在装配层，**运行期零解析**，per-room 对象不进容器。
- 解析序 = 创建序：outbox → transport → host；释放逆序。
- **必填输入缺失即拒装配**：`CombatValues`/`Weapons`/`Map` 不静默跑默认值。
- 生产形态：`mailboxRouting:true`、`drainMailboxesImmediately:false`、`workerExecution:true`。

### 7.2 `Pump` 一帧

```text
now → TickIncoming → 排空断开队列 → FlushGmActions
   → 逐房间：
        Worker 形态：投 Tick + DrainOutbound + 销毁检查
        owner 形态：先排 Control → Input → Outbound，再投 Tick → 广播 → 销毁
   → 周期清理（会话/票据/限流桶，600 ticks）
   → TickOutgoing → MaybePrintOps（5s 一行）
```

**owner 形态必须先排输入再投 Tick**：否则 Tick 先推进帧号，同批里对 `frame+1` 合法的输入会被判 stale。

### 7.3 入站路由与三道前置闸

| Packet | 处理 |
|---|---|
| `Join` | `InboundJoinHandlers.Handle` |
| `Input` | `InboundHandlers.HandleInput` |
| `MismatchReport` | 只计 `Ops.NoteMismatch` |
| `ReconnectRequest` | `InboundReconnectHandlers.Handle` |
| `RestoreComplete` | `RestoreAck`（`Restoring → Active`） |
| `Leave` | 转 `Disconnect` |
| `StartMatchRequest` | 房主提前开局 |
| `Heartbeat` | 原样回显 |

每包前置：包长 > 4096 → `PacketOversized`；Session 包速率限流**静默丢弃**；会话 `Disconnected` 丢。

## 8. 准入与限流：顺序不可交换

`JoinAdmissionGate` 六步：

1. IP 入场频率（先于验签与建房）；
2. 字段 UTF-8 字节边界（RoomId/Token/BuildHash）；
3. token 非空红线；
4. **buildHash 逐字比对**；
5. **票据验签（先于建房）**——垃圾票据不能撑大房间表；
6. 账号入场频率（验签通过后才有 AccountId）。

之后：排空红线 → 既存房间控制 lane 容量 → 建房/复用 → `RoomCommand.Join`。

限流三层四 lane：

| lane | 维度 | 突发 | 补充 |
|---|---|---:|---|
| `IpConnect` | IP·连接 | 32 | 4/s |
| `IpEntry` | IP·入场 | 32 | 8/s |
| `AccountEntry` | 账号·入场 | 16 | 2/s |
| `SessionPackets` | Session·包速率 | 256 | 128/s |

桶表容量 8192/ lane，满则拒新键（安全优先），空闲 TTL 120s；探测不到远端地址则跳过该维度。

## 9. 重连：一次性票据 + 恢复门闩

| 环节 | 事实 |
|---|---|
| 签发 | Join 成功时 `ReconnectService.Issue` → `JoinAck.reconnect_token` |
| 票据 | 128 bit CSPRNG base64url；TTL 300s；容量 256；**一次性**（无论是否过期先 Remove） |
| 入站 | IP 限流 → Peek → 找房 → 队列容量 → **容量锁内消费票据** → `Rebind` |
| 席位 | `Rebind` 置 `Restoring` + `RebindGeneration++`，旧连接映射失效 |
| 广播抑制 | 管线在增量/全量两个环都跳过 `Restoring` 席位 |
| 响应 | Seed/ConfigHash/BuildHash + 全量快照 + 私有面 + 事件窗 + 最近 32 帧输入历史 |
| 恢复 | `RestoreComplete` → `RestoreAck`：`Restoring→Active` + `SeatRestored`（幂等） |
| 重锚 | `SeatRestored` → `RequestFullSnapshot()` 补缝 |
| 前置 | 只在 `Running` 有效 |

## 10. 排空与结算

### 10.1 排空五步

| 步 | 实现 |
|---|---|
| ① 停止分配 | `Draining=true` 随心跳上报，Lobby 停止分配 |
| ② 停新 Join + 房间 drain | `_draining` 置位后 Join 一律拒；逐房 `SetDrainDeadline`（只可前移） |
| ③ 限时完成 / 超时 Abort | `Pump` 检查 deadline，CAS 只投一次 `Shutdown(DrainTimeout)` |
| ④ 刷 Outbox | `FlushSettlementOutbox()`（本就 write-through，此处收口 + 压实） |
| ⑤ 停 Worker/Transport/Host | `Dispose`：Worker `Stop(drain:true)` → 房间收尾 → Transport → Outbox |

收敛判据 `DrainComplete` = 在排空且所有在册房间 `Runtime.Closed`。Ctrl+C 与 SIGTERM 走同一条链。

### 10.2 结算

- 冻结：`Finish` 产 `SettlementReadyOutput(MatchResultSummary)`；`SendMatchResult` 广播 `MatchEnded`。
- **测试房短路**：隐藏房不入盒（仍发 `MatchEnded`，结算页按"无结果"）。
- 入盒四态：`Appended / Duplicate / RejectedFull / Failed`。
- 日志：JSONL，`FileOptions.WriteThrough` + 每行 Flush；`Kind = s/c/x`。
- 幂等：内存 `_seen` 按 `matchId`，`matchId = RoomId + ":" + Guid`（不跨局复用）。
- 有界：容量按**待提交**数计，满则显式 `RejectedFull`。
- 压实：完成标记累计 1024 → 重写日志（temp + `File.Replace`）。
- 提交：后台线程取最老待提交 → POST `submit_url` + Bearer → **2xx** 即完成。
- 失败分级：可重试（传输异常/401/403/404/408/429/5xx）保持待提交 + 指数退避；确定性 4xx（400/409/413/422）弃置。
- 重启续投：不做内存进度，启动即从日志待提交面重建。
- 奖励：`ISettlementRewardPolicy` 单点，当前 `NoEconomyRewardPolicy` 恒 0（**不是忘记填**）。

## 11. Meta：模块化单体

### 11.1 端点

| 方法 | 路径 | 鉴权 |
|---|---|---|
| POST | `/auth/guest` | 无（游客登录） |
| POST | `/lobby/instances/register` | Lobby 实例密钥（注册即心跳） |
| POST | `/lobby/join-ticket` | 访问令牌（版本不符 409） |
| POST | `/lobby/create-room` | 访问令牌（服务端出号） |
| GET | `/lobby/rooms` / `/lobby/rooms/public` | 实例密钥 / 访问令牌 |
| POST | `/matches/result` | **结算实例密钥**（与注册分权） |
| GET | `/matches?limit=N` | 访问令牌（按账号查） |
| GET | `/live` `/ready` `/metrics` | 无 |
| POST | `/sample/settlement` | 无（仅 `EnableSampleEndpoints=true`） |

统一错误体 `{code, messageKey, args[]}`，`args` 只放字段名/上限，不回显字段值。

### 11.2 身份与票据

- 游客登录：`deviceId` 唯一索引做幂等裁判；`accountId = "a" + 20 hex`（CSPRNG）。
- 访问令牌 9 段线格式（`v1.kid.accountId.jti.audience.purpose.iatMs.expMs.sig`），HMAC-SHA256 覆盖前 8 段；TTL 600s；校验 fail-closed（kid→签名→声明→时效→audience→purpose）。
- 建房：前缀 `R-` + 6 位随机（去 `I/1/O/0`）+ 保留窗 120s + 重试 16。
- 实例分配：只选"未 drain 且未满"的实例；声明 buildHash 时只选同值；排序确定（玩家少→房间少→实例标识序）。
- 签票：**16 段、签名覆盖前 15 段**；`matchId/simVersion` 置空、`nbfMs=0`；nonce 128 bit；TTL 300s；票据格式与房间端**物理同一份**（源链接 `JoinTicketFormat.cs`，类注释仍写 v3/15 段——注释漂移）。
- 客户端**不解析票据**，只透传。

### 11.3 持久化

- 一条提交、双侧落库：归档面（幂等键 `matchId`）+ 账本面（逐玩家 fan-out）；归档先落、账本随后。
- 账本幂等**双维度**：`operationId` + 复合唯一索引 `(playerId, matchId, settlementType)`。
- 追加默认 `expectedRevision=-1`（不做 CAS）；≥0 走 CAS，不符则整包 409。
- 原子边界：副本集事务内「账目写 + revision 推进」；撞键回滚后读回首次记录返回 duplicate。
- 批量上界：单提交 ≤64 玩家；`delta` 绝对值 ≤1,000,000；同 `playerId` 不得重复。
- 迁移：schema `LatestVersion=4`；失败**逆序回滚并拒绝启动**（fail-closed）。

## 12. 边界与已知问题

1. **`PurgeExpiredNonces` 无生产调用点**：票据 nonce 窗口满时策略是**拒新票**，不是清理。
2. **`CompensateFire` 的 `clientAckSnapshot` 形参未使用**：对齐已改为直接信任 `viewFrame`，调用处仍传值。
3. **测试房不产结算**：`sandbox` 模板不限时/不按击杀结束，隐藏房也不入盒——"打半天没战绩"通常是预期行为。
4. **奖励恒 0**：`NoEconomyRewardPolicy` 是有意为之，经济系统未接入。
5. **Meta 单实例**：实例注册表/出号保留集驻进程内存，不支持多实例。
6. **容量与阶段限制**：`max_rooms=8`、席位上限 32、`Running` 后不可中途加入、观战/回放无实现。
7. **GM 审计走裸 Console**：`[GM]` 行未经 `HostLog`，已登记为偏差。
8. **输入丢弃原因只暴露 5/10**：`InputGate` 有 10 个计数（重复/越界/过期/ack/按键/seq/槽位/非有限/向量/超长），`Ops` 只输出其中 5 个；seq/槽位/非有限/向量/超长的丢弃在观测面不可见。
9. **访问令牌无吊销面**：`/auth/guest` 签发的令牌只有 `jti` 锚点，没有撤销列表/黑名单；Meta 侧单实例也意味着无法跨实例吊销。
10. **公网面口径不一**：`/metrics` 缺省绑 `+`（全接口），GM 强制非通配；对局 UDP 无 bind 配置（`KcpTransportServer.Start(port)` 只收端口）。
11. **文档行号漂移**：`12-联机全栈参考` 的部分 file:line 引用早于当前工作区（如 `RoomRuntime.StepFrame` 的实际行号已后移），语义仍准，定位时以符号名为准。

## 13. 本章要点

- 内核纯化不是形式主义：它让"权威裁决"可以在 L1 里反复重放，而不依赖网络与进程。
- 输入闸门的核心不是"校验字段"，而是**把客户端能伪造的面逐条关掉**：按键/槽位/seq/数值/瞄准距离/EntityId。
- 回溯补偿的裁判标准是"玩家看到的过去"；这也解释了为什么 `viewFrame` 必须取渲染帧，而不是最新快照。
- 宿主的复杂度集中在**顺序**：先排输入再 Tick、票据验签先于建房、排空只前移 deadline、结算先落盘再提交。
- Meta 的可靠性来自**双维度幂等 + 事务边界 + 迁移 fail-closed**，而不是"重试一下"。

---

**上一章**：[06 · 联机同步与网络协议](06-联机同步与网络协议.md) ｜ **下一章**：[08 · 内容、热更与资源管线](08-内容热更与资源管线.md)
