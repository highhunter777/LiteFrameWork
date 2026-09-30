# 04 · 技术细节：Sim、联机与服务端

> 本文件的三个主题其实是**一份源码的三种用法**：`Assets/LiteSim/Core`（判定）与 `Assets/RoomServer/Runtime`（房间内核）**两端共编**，`Assets/LiteNet` 提供两端的协议与传输。
> 设计契约以 [状态同步专项设计](../design/networking/状态同步专项设计.md)、[服务端总设计](../design/architecture/商业级通用服务端框架总设计.md)、[Meta 服务专项设计](../design/architecture/Meta服务专项设计.md) 为准。

## 1. 确定性 Sim：`Assets/LiteSim/Core`

### 1.1 驱动：定长逻辑帧

| 件 | 用途 |
|---|---|
| `FrameDriver.Tick(realDelta, …)` | **渲染帧 → 逻辑帧**：累加器按固定 dt 追帧，单帧最多追 `MaxCatchUp`（超限丢余量而不是雪崩）；每个逻辑帧末回调消费事件再清缓冲 |
| `FramePump.Step` | **定次步进**：要几帧给几帧——服务端与无头客户端用它 1:1 步进，避免帧号与输入序列漂移 |
| `SimStep.Step/FlushCommands` | 单个逻辑帧内的**固定顺序**：Input → Movement → Shooting → 伤害命令结算（固定轮次）→ Cleanup；输入就地按 `EntityId` 升序排序 |

### 1.2 "确定性"靠什么守住

| 手段 | 落点 |
|---|---|
| 禁超越函数 / 禁 FMA / 禁 float 等值比较（纪律 R1/R2/R3） | `SimMath.cs`（自研 software sqrt，纯整数、正确舍入）、`DisciplineScanner` 扫描 |
| 整数随机 | `SimRng`：Xorshift64*，状态进快照 |
| 定长数组 + 深拷 | `SimWorldState.CopyTo` 逐数组拷贝；`EntitySlot` 为 blittable 值类型；槽位 Id **版本化**（防复用误命中） |
| 跨运行时逐位对账 | `IeeeProbe`（两端探针）+ `Core/Editor/IeeeBaselineChecker`（编辑器对账）+ L1 用例 `IeeeBoundaryTests` / `SimDeterminismTests` / `SimLayoutContractTests` |
| 状态摘要 | `SimChecksum`：FNV-1a 32 位、float 按位混合；**两个口径**——全量（基线/重放/归档）与**公共**（线上和解用，只含公共可重建字段） |

### 1.3 预测 · 回滚 · 和解（客户端侧）

| 场景 | 动作 |
|---|---|
| 本机输入不符（服务端回传的输入与本地历史不一致） | `RollbackSim.OnRealInput` 逐位比对 → `ExecuteRollback`：`SnapshotRing.TryRestore(F-1)` + 重放历史 |
| 权威快照到达 | `OnAuthoritativeSnapshot`：用**公共 checksum** 比对，不符则权威覆盖 + 重放本地历史；`MaxRollbacksPerFrame` 限幅，越界**停预测**强制等待 |
| 远端实体呈现 | `SimView.TryInterpolate`：前后两份快照按视点帧插值 |
| 本地实体呈现 | 预测位置跟位 + **和解衰减**（`ViewTransformMath.Decay/ShouldSnap`，超阈值硬切） |
| 视觉事件 | **静默门**：和解/回滚帧不派发帧事件（避免回滚时特效/音效抖闪） |

## 2. 协议与传输：`Assets/LiteNet`

| 件 | 说明 |
|---|---|
| 协议单源 | `Proto/battle.proto` → `Proto/Generated/Battle.cs`（生成脚本 `scripts/gen-proto.ps1`） |
| 信封与编解码 | `Protocol/PacketCodec.cs`：`[1B PacketType][proto payload]`；`PacketWriter`、`PacketType` |
| 输入位打包 | `Protocol/InputPacker.cs` |
| 快照 | `SnapshotDiffer`（差分）→ `SnapshotCodec`（编解码）→ `SnapshotReassembler`（重组）；`AoiFilter`（按兴趣域裁剪） |
| 传输 | `Transport/KcpTransportClient.cs` + `Vendor/kcp2k`（KCP V1.41）；快照走 **Unreliable**、`StartGame` 等信令走 **Reliable** |
| 上行冗余 | 客户端在最近 **≤4 帧窗口**重复发送输入（抗丢包） |

### buildHash（两端一致性红线）

| 项 | 内容 |
|---|---|
| 覆盖 | `Assets/LiteSim/Core/Scripts`、`Assets/LiteSim/Core/Systems`、`Assets/LiteNet/Proto`、`Assets/LiteNet/Protocol` ＋ **玩法表数据**（`Assets/GameData/Config` 客户端 bin 与 `RoomServer/Data` 服务端 json 同源；UI 表如 `tbuiform` 显式排除） |
| 算法 | 按相对路径 Ordinal 排序 → 逐个喂 `路径\0内容（行尾 CRLF/CR→LF 归一化）` → SHA-256 → 取前 16 个十六进制字符 |
| 生成 | `python scripts/gen-build-hash.py` → `Assets/LiteNet/Protocol/BuildHash.g.cs` |
| 握手 | `RoomClient.SendJoin → JoinRequest.build_hash`：两端不等 → 拒绝进房（**改 Sim/协议/表必须重跑生成器**） |
| 守卫 | `Tests/LiteNet.Tests/BuildHashTests` 按同规则复算并与常量比对（L1 抓"忘了重跑"） |

## 3. 服务端房间：`Assets/RoomServer` + `RoomServer/`

### 3.1 分层与纯化

- `RoomServer/Runtime`（`RoomServer.Runtime` 程序集）：**纯化内核**——asmdef 零引擎依赖，且纪律 **R11 禁 Console / 系统时钟 / 文件 IO / proto / LiteNet 引用**（违反被 L1 纪律扫描打红）。
- `RoomServer/Application`（`RoomServer.Application.Runtime`）：快照流水线、会话编排等允许引用协议/传输的部分。
- 可执行宿主：`RoomServer/RoomServer.csproj`（net8.0，引用上述两者 + `LiteNet` + `LiteSim.Core`）——`Program.cs`（入口）、`ServerHost.cs`（连接/房间表/排空装配）、`ServerLoop.cs`（**60Hz 节拍**）、`RoomInstance.cs`、`Application/`（SessionManager/ReconnectService 等带 IO 的另一半）。

### 3.2 关键件

| 件 | 职责 |
|---|---|
| `RoomRuntime.Execute / StepFrame` | **权威所在**：唯一持有 `AuthSim` 与席位；每逻辑帧消费预存输入 → `SimStep.Step` → 快照环 `Capture` → 记历史 → 回溯判定 |
| `RoomCommand` / `RoomOutput` | 入参命令联合 / 纯输出事件——内核与宿主之间只有这两类数据 |
| `PlayerSession` | 席位：与连接解耦（断线重连、观战、换连接都只动连接侧） |
| `InputGate.Store / TryConsume` | 输入闸门：帧号、白名单、去重、数值边界校验 → 按帧预存（**不合法输入不进仿真**） |
| `SnapshotPipeline.BroadcastIfDue / SendSnapshot` | 30Hz 抽帧 → 差分 → AOI → **背压分档**（跟不上的客户端降级而不是拖垮房间） |
| `LagCompensator.CompensateFire` | 回溯补偿：回到玩家所见帧单跑 `ShootingSystem` 后还原（打到的判定以"玩家看到的过去"为准） |
| `MatchStateMachine.TryTransition` | 比赛生命周期状态机 |

### 3.3 运维面

- 多房间：`roomId → RoomInstance` 路由、动态建房受 `max_rooms` 约束（见 [服务端多房间记录](../施工进度/服务端多房间.md)）。
- 排空（优雅关闭第 2–4 步）与跨房间过载隔离、真实传输多房间隔离已交付；Worker/Mailbox 核心、宿主生命周期与 Control/Input 入站路由已接线，Runtime/Outbound Worker 迁移与排空第 5 步完整核证仍归 R2。
- 离线隔离开发：房间内核可跑在**进程内本服**（`LocalServerTransport`），便于无网环境开发与联机用例（见 [离线隔离开发](../施工进度/离线隔离开发.md)）。

## 4. Meta 服务（接缝期）

- 宿主骨架：Generic Host + Options 范围校验 + `/live` `/ready` `/metrics` + 优雅关闭与 drain + 入站上限；Web 面零 NuGet（MongoDB.Driver 为服务端首个真 NuGet，2026-09-30 裁决登记）。
- 票据接缝：`IJoinTicketValidator` + HMAC 验证器 + 非法票据矩阵（过期/篡改/重放/密钥轮换/受众/房间/哈希），接入 `ServerHost.HandleJoin`。
- 持久化接缝（M0-c）**已完成（2026-09-30）**：`Contracts/Persistence` 三端口（结算账本双维度幂等/revision CAS 原子边界、有界 Outbox、迁移显式版本化+失败回滚）＋ `Infrastructure/Persistence/Mongo` 真适配器（副本集事务、唯一索引兜底）＋宿主接线（启动迁移 fail-closed、`/ready` 依赖检查、`POST /sample/settlement` 样例端点）；L1 28 例＋L3 26 例（含容器级重启恢复与"重复提交 100 次只生效一次"）——《框架先行》准入项"持久化契约有效"与样例⑤由此达标（见 [待办总览](../待办总览.md)）。

## 5. 一条链的完整时序（把 02 §② 落到类）

```text
客户端                                   服务端
InputService.SampleOnRenderFrame
  └→ BattleContext.Tick(frame+1)
       ├→ Sim.OnRealInput(frame, input)   ← 入 InputHistory
       ├→ RoomClient.SendInput(frame, input, viewFrame) ──KCP──→ InputGate.Store（校验+预存）
       └→ RollbackSim.Tick(realDelta)                              │
            └ FrameDriver 追帧 → SimStep.Step（预测）              ↓
                                                    RoomRuntime.StepFrame（权威）
                                                      → SimStep.Step → SnapshotRing.Capture
                                                      → SnapshotPipeline（30Hz 差分+AOI）
客户端 ←────────────── 快照（Unreliable）──────────────────────────────┘
  └→ RollbackSim.OnAuthoritativeSnapshot（公共 checksum 比对）
       ├ 一致 → 丢弃（保持预测）
       └ 不符 → 权威覆盖 + 重放历史 → SimView 和解衰减
  └→ SimView.Tick → 相机/动画/VFX/音频（静默门放行的帧事件）
```

## 6. 边界与坑（写在这里以免重复踩）

- 改 Sim 源码 / proto / 玩法表 → **必跑 `python scripts/gen-build-hash.py`**（否则全员拒进房）。
- `RoomServer/Runtime` 内不得引入 Console/系统时钟/文件 IO/proto/LiteNet（R11）。
- 快照与 `StartGame` 是两条独立路径（Unreliable vs Reliable）：**"快照先到"完全正常**，判断"是否已在局中"要看 `HasStartGame`，不能拿 `LastSnapshotFrame` 当代理（历史缺陷）。
- 公共 checksum 只含公共可重建字段：客户端**不能**用全量 checksum 做线上和解。
