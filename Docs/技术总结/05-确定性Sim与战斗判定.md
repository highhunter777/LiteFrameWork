# 05 · 确定性 Sim 与战斗判定

> **本章回答**：一局战斗的"真相"如何被计算出来——逻辑帧怎么推进、系统按什么顺序跑、确定性靠什么守住、客户端预测/回滚/和解如何与权威对齐、表现层如何不闪不抖。
> **上游权威**：[状态同步专项设计](../设计文档/网络/状态同步专项设计.md)（契约）、[Sim组织专项设计](../设计文档/游戏玩法/Sim组织专项设计.md)（系统组织）、[角色状态与动作专项设计](../设计文档/游戏玩法/角色状态与动作专项设计.md)（输入/动作）。本章讲实现事实。
> **口径**：2026-10-22 工作区；Sim 源码在 `Assets/LiteSim`，两端共编。

## 1. 为什么必须是确定性的

联机模型是**服务器权威 + 客户端预测**：

- 服务端跑唯一权威 Sim，客户端本地也跑同一份 Sim 来"猜"自己的移动/开火，从而隐藏 RTT；
- 服务端下发快照后，客户端若发现猜测与权威不符，就回滚到过去帧重放；
- 这套机制成立的前提是：**相同输入 + 相同初始状态 ⇒ 逐位相同的结果**。否则每次和解都会漂移，表现为抖动、穿模、打不中。

因此 Sim 的设计目标不是"快"，而是**可复现**：固定 dt、固定系统顺序、固定数学、固定内存布局、可摘要校验。

## 2. 逻辑帧：60Hz 定长 + 累加器追帧

`Assets/LiteSim/Scripts/Pipeline/FrameDriver.cs`：

```text
_accumulator += realDelta
while (steps < MaxCatchUp && _accumulator >= Dt) {
    SimStep.Step(...)          // 一个逻辑帧
    onLogicalFrame(...)        // 帧事件交付的唯一时机
    s.Events.Clear()           // 事件是帧内瞬态
    _accumulator -= Dt
}
if (_accumulator >= Dt) _accumulator = 0;   // 追不上就丢余量，防死亡螺旋
```

| 常量 | 值 | 含义 |
|---|---|---|
| `TickRate` | 60 | 逻辑帧率；射击手感要求，不接受可变 dt |
| `Dt` | `1f/60f` | 与 `SimMath.Dt` 同源，禁止第二处定义 |
| `MaxCatchUp` | 5 | 单渲染帧最多追 5 帧；打满仍积压则丢弃余量 |
| `InputDelay` | 1 | 输入延迟帧 |
| `MaxPredictionLead` | 6 | 预测领先上限（`MaxCatchUp+1`），防止和解把帧号顶出输入冗余窗口 |
| `MaxRollbackFrames` | 8 | 快照环容量（实际环 9） |
| `InterpFrames` | 4 | 远端插值延迟（约 66ms） |
| `MaxInputHistory` | 32 | 输入历史容量 |
| `MaxRollbacksPerFrame` | 2 | 单渲染帧回滚次数上限，超限停预测 |

**关键边界**：`FrameDriver` 不读 `IGameClock`、不实现 `ITickable`——逻辑帧恒 60Hz，表现层时钟的暂停/变速不影响判定。`PeekSteps` 是与 `Tick` 同判据的纯查询，供"多步渲染帧提前上行沿用帧"使用（客户端 `BattleInputPipeline`）。

## 3. 系统顺序：`SimStep` 是唯一契约

`Assets/LiteSim/Scripts/Pipeline/SimStep.cs` 是**硬编码直线调用**，没有注册表/数组/特性扫描：

```text
前置：Match.Phase == Finished → 清命令缓冲并 return
SortInputs（按 EntityId 升序就地稳定插入排序）
① RespawnSystem.Run
② InputSystem.Run
③ MovementSystem.Run
④ WeaponSystem.Run
⑤ ShootingSystem.Run
⑥ FlushCommands（最多 3 轮：DamageSystem → ScoreSystem，轮末清缓冲）
⑦ CleanupSystem.Run
⑧ s.Frame++
⑨ if (advanceMatch) MatchSystem.Tick
```

为什么不用"系统注册表"：系统集合编译期确定，顺序就是确定性契约；注册表会引入发现顺序/反射/集合遍历等不确定源，且难以静态审计。代价是新增系统必须手改这一处，且必须同步文档与测试。

**生产路径的一处差异**：`RoomRuntime.StepFrame` 传 `advanceMatch: false`，把 `MatchSystem.Tick` 挪到回溯判定（`ConsumePendingFire`）之后，并对同帧 `SnapshotHistory.Capture` 二次捕获，以覆盖最终计分/比赛状态。即"Match 在最后"只在默认形态成立；**以 `RoomRuntime` 的调用序为准**。

**输入排序**：同一逻辑帧内多个输入按 `EntityId` 升序稳定排序，乱序输入与升序输入结果一致——数组顺序不是处理顺序的来源。

## 4. 确定性的三根桩

### 4.1 数学：自研 software sqrt + 双精度累积

`SimMath` 不用 `Math.Sqrt`：Mono 与 .NET 8 的 `double` 开方实测存在 ulp 差异，长链（位置积分）会放大。替代方案是**纯整数/位运算的正确舍入 sqrt**（`SimMath.cs`），并配套：

- 禁超越函数（Sin/Cos/Atan2 走 `SimTrig` 查表）；
- 禁 FMA（融合乘加跨 CPU/编译器结果可能不同）；
- 多乘加用双精度累积、单次舍入（`MulAdd2/3/Sub`）；
- 禁 float 等值比较，统一 `NearlyEqual`（1e-6）。

守卫：`DisciplineScanner` R1/R2/R3 + `IeeeProbe`（Sqrt 逐值、四则相邻对、10k 步链 checksum，逐位非容差）+ .NET 基线文本对账。

### 4.2 随机：Xorshift64*，状态进快照

`SimRng` 是整数 Xorshift64*，`ulong State` 随 `SimWorldState` 进快照/回滚；`NextFloat01` 取 24 位尾数，`NextRange` 取模。**没有 `new Random()`、没有时间种子**——R11 扫描会拦。

### 4.3 内存：定长数组 + 版本化槽位 + 深拷

- `SimWorldState` 用定长数组/位图，`CopyTo` 逐数组深拷（含 `_versions`/`_nextFree`）。
- `EntitySlot.Id = version(48b) << 16 | slot`，0 无效；`TryResolve` 同时校验活体与版本，防止槽位复用误命中。
- `CustomData` 每实体 32B、`Globals` 256B，只用于版本化扩展；正式状态一律进定型数组。

## 5. Checksum：全量与公共两个口径

`SimChecksum` 用 FNV-1a 32 位，float 按位混合。两个口径**不能混用**：

| 口径 | 包含 | 用途 |
|---|---|---|
| **全量** | 全部状态（含私有面、RngState、细节数组） | 基线、重放、归档、L1 逐位对账 |
| **公共** | Frame + 活体位图 + 实体公共面 + 主动作摘要 + Match + 分型表公共面 | **线上和解**（客户端能重建的字段） |

公共口径还拆出 `ComputeVisibleChecksum`、`ComputeMatchChecksum` 与逐槽 `ComputeEntityPublicChecksum`，供增量快照的**部分和解**：整帧 checksum 相等则短路；不等则逐个 `SlotDelta.checksum` 定位差异槽，只覆盖差异，避免 AOI 裁剪造成的"每帧误和解"。

## 6. 预测、回滚、和解（客户端）

`RollbackSim` 的机制：

- **环与历史**：快照环容量 `MaxRollbackFrames+1 = 9`，输入历史 32 帧；构造期 `Capture(0)` 锚定帧 0。
- **每渲染帧**：先 `PrepareNext(Frame+1)`、重置 `_rollbacksThisFrame`，再 `FrameDriver.Tick`。
- **每逻辑帧**：`Capture`（当前状态）→ `Record`（输入）→ `PrepareNext`（下一帧输入）→ `OnFrameEvents`（交付事件）。
- **真实输入到达**（`OnRealInput`）：
  - 早到帧（frame > 已执行）只入史并解锁"停预测"；
  - 若与预测逐位一致（`BitUtil.Equal`）→ 不回滚；
  - 不一致 → `ExecuteRollback`：恢复到 `F-1`，重放 `F..last`；
  - 恢复失败 → `_halted` 停预测强制等待；超 `MaxRollbacksPerFrame=2` → 丢弃并计 `DeferredRollbacks`。
- **预测口径**：真实输入优先；缺失时沿用上一帧并 `Buttons &= PredictedButtons`——**只预测瞄准，不预测开火/离散动作**。
- **权威快照到达**（两个重载）：
  - 全量：公共 checksum 相等则短路；不等则整帧覆盖 + 重放；
  - 增量：`visible+match` 相等则短路；不等则逐槽 checksum 定位，只写差异槽；
  - 重放终点截到 `MaxPredictionLead=6`，防止本地帧号越界。

**客户端预测不产战斗结果**：`SimStep.Step(..., resolveCombat: false)` 时，武器消费/开火窗/`Fire` 事件照常，但**不产 Damage 命令与 Hit/Crit/Death 事件**——命中/伤害/死亡只由权威快照与权威反馈面驱动。这是"预测不越权"的关键。

## 7. 表现侧：插值、衰减、静默门

`Assets/LiteView/SimView.cs`：

- **远端 jitter buffer**：容量 8，按**快照帧号**推进；渲染帧 = 最新快照帧 − `RenderDelayFrames(4)`，按 60Hz 匀速推进并封顶；插值 alpha 用真实帧差（丢包时铺满，不冻结）。
- **硬切条件**：远端 `LifeStartFrame` 变化（复活）或位移 > `SnapDistance=3m`；本地复活无条件硬切（直写 `CharacterController`，不走带扫掠的 `Move`），异常单帧跳变兜底 2m。
- **常规收敛**：指数衰减，`ReconcileSharpness=10`；超过阈值硬切。
- **事件静默门**：`OnReconcile/OnRollback` 抬高 `SilenceUntilFrame`；`OnFrameEvents` 丢弃该帧及之前的事件并计 `SilencedEvents`。事件只在逻辑帧边界交付，事后轮询读不到。

这三个机制共同保证"回滚不闪、和解不抖、丢包不冻结"。

## 8. 数值、规则与地图

| 面 | 实现 |
|---|---|
| 数值实例 | `CombatValues`（10 字段：5 基础 + 瞄准限速/开火窗 + 2 GM 规则位等），`in` 参数传入每个系统；`Default` 是属性（每次返回副本，防静态可变复辟） |
| 武器表 | `WeaponTable` 定容 16 行 + 槽位默认映射（first-wins）；装载期写、此后只读；默认步枪 16 伤害/600rpm/弹匣 30/备弹 90/换弹 132 帧/射程 100m |
| 比赛规则 | `MatchRules` 四字段（时长/击杀上限/复活延迟/出生保护），默认 3min/20 杀/3s/2s；先判击杀上限，再计时 |
| 复活 | 仅 `Running` 且到 `RespawnFrame`；从未占用出生点中确定性选点（**不消费 RngState**，用帧号⊕实体 Id 混洗），保留 Id/KD |
| 伤害 | 扣血→跨死线写 Kill 命令 + Death 事件；爆头 ×2；伤害浮动 ±`DamageSpread` 消费 RngState；免死规则保底 Hp=1 |
| 地图/碰撞 | `SimMapData` 静态障碍 Circle/Box、`MaxObstacles=256`、出生点 16；移动先全体积分再统一去穿插（实体升序 × 障碍升序）；子弹遇障碍更近则截停，不穿墙 |

## 9. 边界、坑与文档冲突

1. **`FramePump` 零调用点**：定次步进（1:1）已实现，但客户端走 `FrameDriver`、服务端直调 `SimStep.Step`；它不是生产路径。
2. **`MovementValues` 装载了没人读**：18 字段只有 `Gravity` 经 `CombatValues` 进机制，走/跑/冲/滑铲/钩爪等 17 字段无机制消费；`MovementConfig` 的 18 个读口也零生产读者。
3. **静态读口仍在**：`CombatConfig.Loaded`、`WeaponConfig.Loaded` 作为表现面读口存在，机制面由 R13 禁止读取；生产构造仍从静态取实例（`BattleContext`），这是"实例化已治大半、未全清"的现状。
4. **TPS 过渡态**：工作区已落地越肩相机/屏心瞄准（`TpsCameraMath`、相机服务 POV 轴、屏心射线求 AimPoint），但《固定斜视角射击方案专项设计》仍写固定斜视角/鼠标不驱动相机；本章按代码写，设计文档待回写。
5. **文档数值滞后**：`状态同步专项设计` 仍列 `AimX/AimZ`（实际 `reserved 4,5,10`）；`12-联机全栈参考` 的 checksum 常量与 `recent_kills/fires/hits` 字段已过时。
6. **远端换弹已可读**：`WeaponSystem` 写主动作摘要 `ActionId=Reload/StartFrame`，`SimView.IsReloading` 对远端不再恒 false；旧文档的"远端换弹表现待公共位扩展"已部分过时。

## 10. 本章要点

- 确定性的本质是**消除一切不确定源**：时间、数学、随机、容器顺序、内存布局、系统顺序；每消除一个，都要有守卫防止回潮。
- 预测的边界要写死：**只预测瞄准与移动，不预测开火结果**；命中/死亡只认权威。
- 和解不是"覆盖状态"这么简单：全量/增量两个锚点、逐槽 checksum、重放终点截断、静默门，缺一个都会在弱网下暴露。
- 表现层的"不闪"是设计出来的：jitter buffer 按快照帧推进、丢包铺满、硬切与衰减分层、回滚期静默。

---

**上一章**：[04 · UI 系统与 Lua 脚本层](04-UI系统与Lua脚本层.md) ｜ **下一章**：[06 · 联机同步与网络协议](06-联机同步与网络协议.md)
