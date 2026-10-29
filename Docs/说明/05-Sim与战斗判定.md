# 05 Sim 与战斗判定

> **定位**：一局战斗的权威事实如何被算出来——定长逻辑帧、固定系统顺序、确定性的数学/随机/内存边界，以及客户端的预测、回滚、和解与服务器回溯判定。
> **权威边界**：不裁决目标架构（归 [设计文档](../设计文档/README.md)）、不记批次（归 [施工进度](../施工进度/README.md)）。
> **来源**：合并自 `技术总结/05-确定性Sim与战斗判定.md`（旧路径已删，映射见 [说明索引](README.md)）。
> **关联**：[06 联机同步与网络协议](06-联机同步与网络协议.md)、[07 联机全栈参考](07-联机全栈参考.md)、[状态同步专项设计](../设计文档/网络/状态同步专项设计.md)、[Sim 组织专项设计](../设计文档/游戏玩法/Sim组织专项设计.md)、[角色状态与动作专项设计](../设计文档/游戏玩法/角色状态与动作专项设计.md)、[输入与同步链线](../施工进度/输入与同步链线.md)、[战斗与武器线](../施工进度/战斗与武器线.md)。

## 1. 判定层的位置与目标

判定代码位于 `Assets/LiteSim/`：纯 C#、零引擎依赖，客户端（Unity）与服务端（RoomServer，.NET 8）**引用同一份源**。战斗的"真相"只在这一层产生：网络层搬运差异与摘要，表现层只读。

联机模型是**服务器权威 + 客户端预测**：

- 服务端跑唯一权威 Sim；客户端本地跑同一份 Sim 来"猜"自己的移动与开火，从而隐藏 RTT；
- 服务端下发快照后，客户端若发现猜测与权威不符，就回滚到过去帧重放；
- 这套机制成立的前提是 **相同输入 + 相同初始状态 ⇒ 逐位相同的结果**。否则每次和解都会漂移，表现为抖动、穿模、打不中。

因此 Sim 的设计目标不是"快"，而是**可复现**：固定 dt、固定系统顺序、固定数学、固定内存布局、可摘要校验。

## 2. 逻辑帧：60Hz 定长 + 累加器追帧

`Assets/LiteSim/Scripts/Pipeline/FrameDriver.cs` 是客户端预测路径的帧推进器（`RollbackSim` 持有）：

```text
_accumulator += realDelta
while (steps < MaxCatchUp && _accumulator >= Dt) {
    SimStep.Step(...)          // 一个逻辑帧
    onLogicalFrame(...)        // 帧事件交付的唯一时机
    s.Events.Clear()           // 事件是帧内瞬态
    _accumulator -= Dt
}
if (_accumulator >= Dt) _accumulator = 0   // 追不上就丢余量，防死亡螺旋
```

| 常量 | 值 | 含义 |
|---|---|---|
| `TickRate` | 60 | 逻辑帧率；射击手感要求，不接受可变 dt |
| `Dt` | `SimMath.Dt` = `1f/60f` | 单帧步长；禁止第二处定义 |
| `MaxEntities` | 256 | 实体槽位容量 |
| `InputDelay` | 1 | 输入延迟帧 |
| `MaxCatchUp` | 5 | 单渲染帧最多追 5 帧；打满仍积压则丢弃余量 |
| `MaxPredictionLead` | 6 | 预测领先上限（`MaxCatchUp+1`），防止和解把帧号顶出输入冗余窗口 |
| `MaxRollbackFrames` | 8 | 回滚深度（`SnapshotRing` 实际容量 9） |
| `InterpFrames` | 4 | 远端插值延迟（约 66ms） |
| `LagCompHistory` | 16 | 服务器回溯判定窗口（0 = 关闭） |
| `MaxInputHistory` | 32 | 输入历史容量 |
| `MaxRollbacksPerFrame` | 2 | 单渲染帧回滚次数上限，超限停预测 |

同一文件还收着布局常量（实体自定义 32B/全局 256B/武器 2 槽/动作 4 槽/状态 4 槽/背包 12 格）与广播旋钮（快照 30Hz、AOI 格 10m/半径 30m/栅格 ±640m）；后者只影响广播裁剪，不参与判定。

**边界**：`FrameDriver` 不读 `IGameClock`、不实现 `ITickable`——逻辑帧恒 60Hz，表现层时钟的暂停/变速不影响判定。`PeekSteps(realDelta)` 是与 `Tick` 同判据的纯查询（不改累加器、不推帧），供"多步渲染帧提前上行沿用帧输入"使用，调用方为 `BattleContext` / `BattleInputPipeline`。

`FramePump`（1:1 定次步进）已实现，但在 `Assets/` 下除自身文件外**没有调用点**：客户端走 `FrameDriver`，服务端直调 `SimStep.Step`。

## 3. 系统顺序：`SimStep` 是唯一契约

`Assets/LiteSim/Scripts/Pipeline/SimStep.cs` 是硬编码直线调用，没有注册表、数组或特性扫描：

```text
前置：Match.Phase == Finished → 清命令缓冲并 return
SortInputs（按 EntityId 升序就地稳定插入排序）
① RespawnSystem.Run
② InputSystem.Run
③ MovementSystem.Run
④ WeaponSystem.Run
⑤ ShootingSystem.Run（resolveCombat 透传）
⑥ FlushCommands（最多 3 轮：DamageSystem → ScoreSystem，轮末清缓冲）
⑦ CleanupSystem.Run
⑧ s.Frame++
⑨ if (advanceMatch) MatchSystem.Tick
```

不用"系统注册表"：系统集合编译期确定，顺序就是确定性契约；注册表会引入发现顺序、反射、集合遍历等不确定源，且难以静态审计；代价是新增系统必须手改这一处。

**输入排序**：同一逻辑帧内多个输入按 `EntityId` 升序稳定排序，因此乱序输入与升序输入结果一致——数组顺序不是处理顺序的来源。

**命令轮次**：`FlushCommands` 每轮只消费上一轮产生的区段，轮内新命令进入下一轮；最多 3 轮，空轮提前收敛。伤害在第一轮结算，死亡产生的 Kill 在下一轮由 `ScoreSystem` 登记。命令缓冲是帧内瞬态，不进快照与 checksum。

**权威生产路径的差异**：`RoomRuntime.StepFrame`（`Assets/RoomServer/Core/RoomRuntime.cs`）传 `advanceMatch: false`，并在单步之后自行补序：

```text
SimStep.Step(..., advanceMatch: false)
→ CaptureEvents(fireOnly: true) / SnapshotHistory.Capture / 历史输入与回溯历史记账
→ ConsumePendingFire（逐射手回溯判定；每人间 FlushCommands，避免后续射手清掉待结算命令）
→ MatchSystem.Tick（整帧含回溯结算后才判胜）
→ SnapshotHistory.Capture（二次捕获，覆盖本帧最终计分/比赛状态）
```

即"Match 在最后"只在默认形态成立；**以 `RoomRuntime` 的调用序为准**。

## 4. 确定性的三根桩

### 4.1 数学：自研 software sqrt + 查表三角

`SimMath` 不用 `Math.Sqrt`：Mono 与 .NET 8 的 `double` 开方实测存在 ulp 差异（逐值样本一致，10k 步运算链 checksum 不同），长链位置积分会把它放大。替代实现是**纯整数/位运算的正确舍入 sqrt**：特例处理后整数逐位开方、精确比较判进位，结果与正确舍入的 IEEE binary32 sqrt 逐位一致。

配套纪律：

- 禁超越函数：Sin/Cos/Tan/Atan2 走 `SimTrig` 查表；
- 禁 FMA 显式写法（`FusedMultiplyAdd`）；
- 多乘加用 `MulAdd2/MulAdd3` 双精度累积、单次舍入；
- `float` 禁 `==`/`!=`，统一 `SimMath.NearlyEqual`（绝对容差 `1e-6`）。

守卫两层：`Assets/Tools/DisciplineScanner` 的 R1（超越函数）/R2（FMA）/R3（浮点等值）/R4（确定性容器与不稳定排序）；`IeeeProbe` 产出 Sqrt 逐值 + 相邻对四则 + 10k 步链 checksum 的逐位基线，.NET 侧与 Unity 侧比对**同一份文本**（非容差）。

### 4.2 随机：Xorshift64*

`SimRng` 是整数 Xorshift64*（`ulong State`），状态随 `SimWorldState` 进全量快照与回滚；`NextFloat01` 取 24 位尾数构造 `[0,1)`，`NextRange` 取模。种子为 0 时替换为常量，防止 xorshift 锁死。**没有 `new Random()`、没有时间种子**。

唯一消费点是 `ShootingSystem`：每弹丸两笔（偏转角 + 方位角，`spread=0` 时零消耗）、每次命中一笔（伤害浮动 ±`DamageSpread`）。打空/被墙截停不退散布笔，无效开火（无 AimPoint）两处都不消费。服务器回溯判定时由调用方还原 `RngState`，不消耗权威随机流。

### 4.3 内存与身份：定长、版本化、深拷

- `SimWorldState` 用定长数组/位图；`CopyTo` 逐数组深拷（含分配器版本表与游标），运行期零分配。
- `EntitySlot.Id = version(48bit) << 16 | slot`，0 无效；`TryResolve` 同时校验活体与版本，防止槽位复用误命中。
- `CustomData` 每实体 32B、`Globals` 256B，只用于版本化扩展；正式状态一律进定型数组（武器/动作/状态/局内背包/资源/比赛）。
- `SnapshotRing` 每槽独立 `SimWorldState` 实例，`Capture` 在 Step 后标记"该帧执行后状态"，`TryRestore(F-1)` 即"F 帧执行前状态"。服务端回溯环复用同一个类，容量 = `LagCompHistory`。

## 5. 状态摘要：全量与公共两个口径

`SimChecksum` 用 FNV-1a 32 位（offset `2166136261`、prime `16777619`），浮点按位混合。口径**不能混用**：

| 口径 | 入口 | 覆盖 | 用途 |
|---|---|---|---|
| 全量 | `ComputeChecksum` | Frame + RngState + 实体全字段 + Globals/CustomData + 武器/动作/状态/背包/资源 + 分型表 + Match | 确定性基线、重放对账、诊断录制 |
| 全量（无帧） | `ComputeStateChecksum` | 同上，不含 Frame | 跨实例"同种子同输入应逐位同步"判定 |
| 公共 | `ComputePublicChecksum` | Frame + 活体位图 + 实体公共面 + 主动作摘要 + Match + 分型表公共面 | 全量快照的 `checksum`（线上和解锚点） |
| 可见 | `ComputeVisibleChecksum` | 指定槽位集合的公共面折叠 | 增量快照的 `visible_checksum` |
| 比赛 | `ComputeMatchChecksum` | Match 公共面 | 增量快照的 `match_checksum` |
| 单槽 | `ComputeEntityPublicChecksum` | 单实体公共面 + 主动作摘要 | `SlotDelta.checksum`（逐槽定位差异） |

公共口径只覆盖"全量快照可重建、客户端可预测"的层：私有面（他人弹药/技能 CD/背包/资源）、`RngState`、状态效果明细**不进比对**——客户端永远无法重建，进了只会制造永假和解。

字段清单不手写：实体与运行态结构的公开/全量字段由 `[StateLayer]` 标注生成到 `Assets/LiteSim/Diagnostics/Generated/EntitySlot.Sync.g.cs`（生成器 `scripts/codegen/gen_sync_code.py`）。给结构体加判定字段后必须重跑生成器，漏改表现为"该层分叉但和解不报"的静默漂移。

## 6. 客户端预测、回滚、和解

`Assets/LiteSim/Scripts/Pipeline/RollbackSim.cs`：

- **环与历史**：快照环容量 `MaxRollbackFrames+1 = 9`，输入历史 32 帧；构造期 `Capture(0)` 锚定帧 0，第 1 步回滚天然可用。
- **每渲染帧**：`Tick` 先 `PrepareNext(Frame+1)`、重置 `_rollbacksThisFrame`，再 `FrameDriver.Tick`；停预测期不推进。
- **每逻辑帧**：`Capture`（Step 后状态）→ `Record`（输入与预测位）→ `PrepareNext`（下一帧输入）→ `OnFrameEvents`（帧事件交付，必须最后做）。
- **真实输入到达**（`OnRealInput`）：
  - 早到帧（`frame > 已执行帧`）只入史，并解锁停预测；
  - 该帧未被预测或与预测逐位一致（`InputHistory.Differs`）→ 不回滚；
  - 不一致 → `ExecuteRollback`：`TryRestore(F-1)`，重放 `F..last`（逐帧 Capture、清事件），再 `PrepareNext`；
  - 恢复失败 → `_halted` 强制等待并计 `HaltCount`；超 `MaxRollbacksPerFrame=2` → 丢弃并计 `DeferredRollbacks`（由权威快照覆盖兜底）。
- **预测口径**：真实输入优先；缺失时沿用上一帧的移动/朝向，且 `Buttons &= SimInputFrame.PredictedButtons`（当前仅 `ButtonAim`）——**只预测瞄准，开火与离散意图不预测**。
- **权威快照到达**（两个重载）：
  - 全量：按 `ComputePublicChecksum` 比对，一致零和解；不一致权威整帧覆盖 + 重放本地历史；
  - 增量：`visible + match` 两个摘要先短路；不一致则逐槽 `SlotDelta.checksum` 定位，只把差异槽的公共面写回（视野外保留本地预测），再重放；
  - 快照超前或已出环：权威态直接覆盖后继续；
  - 重放终点一律截到 `frame + MaxPredictionLead(6)`，避免本地帧号越过输入冗余窗口。
- **计数与回调**：`RollbackCount`/`DeferredRollbacks`/`HaltCount`/`ReconcileCount`；`OnRollback`/`OnReconcile` 是偶发信号（表现层接缝），`OnFrameEvents` 每逻辑帧一次；重放段不触发它——重放事件不消费即清，避免一次性副作用重播。

**预测不越权**：`SimStep.Step(..., resolveCombat: false)` 时，武器消费、开火窗、`Fire`/`ObstacleHit` 事件与 RNG 推进照常，但**不写 Damage 命令、不产 Hit/Crit/Death 事件**——命中/伤害/死亡只由权威驱动。

## 7. 服务器回溯判定（延迟补偿）

`Assets/RoomServer/Core/LagCompensator.cs`：客户端在输入包里带 `view_frame`（= 开枪那一刻渲染到的帧，单源 `SimView.RenderFrame`）。服务器把它 clamp 到 `[当前帧 − LagCompHistory, 当前帧]`（捏造只夹边界，拉不长窗口），环内恢复历史帧，**射手取当前位**、只回溯目标；还原 `RngState` 后带 `ButtonFireFlag` 单独跑 `ShootingSystem`（跳过资源门，几何用历史帧、瞄准点用本次准入输入），只保留目标帧产生的命中/死亡，结果落到当前帧。每个射手结算后立即 `FlushCommands`，避免下一个射手清掉待结算命令。

退化路径（`LagCompHistory<=0`、目标帧不在环、历史输入缺失）走前向判定（`RunDegraded`），不回溯。

## 8. 战斗规则与数值

### 8.1 数值实例与武器表

| 面 | 实现 |
|---|---|
| 玩法数值 | `CombatValues`（10 字段）：`MoveSpeed`/`Gravity`/`HitscanRange`/`BaseDamage`/`DamageSpread`/`EntityHp`/`AimMoveSpeed`/`FireStanceFrames` + 两个测试规则位 `NoDeath`/`InfiniteAmmo`；按值传入每个系统，`Default` 是属性（每次返回副本） |
| 武器表 | `WeaponTable` 定容 16 行 + 槽位默认映射（`first-wins`）；装载期写入，此后只读；槽位数 2 |
| 默认步枪 | id 0：伤害 16、600 rpm、弹匣 30、备弹 90（上限同值）、换弹 132 帧、射程 100m、散布 1.2°、单弹丸、切枪 30 帧、自动 |
| 其它默认 | `MoveSpeed=5`、`AimMoveSpeed=2.5`、`HitscanRange=100`、`BaseDamage=25`（未装备兜底）、`DamageSpread=1`、`FireStanceFrames=60` |
| 击发节拍 | `FireIntervalFrames = ceil(TickRate×60/rpm)`——600 rpm @60Hz = 6 帧 |

已装备实体的伤害/射程/弹丸数取武器表行；未装备（测试/沙盒直调）才回落 `CombatValues.BaseDamage`/`HitscanRange`。

### 8.2 命中、爆头、伤害与死亡

- **命中形状**是双柱阶梯（`SimRaycast` 单源）：身体柱 `[0, HeadHitLine) × HitscanRadius(0.45m)` + 爆头柱 `[HeadHitLine, HitscanHeight] × HeadshotRadius`；`HeadHitLine = HitscanHeight × HeadBake.Ratio`。
- **判定方向** = 逻辑枪口（`CombatConfig.MuzzleOrigin`）指向 `AimPoint`；障碍更近则截停（写 `Fire` + `ObstacleHit`，不写伤害），子弹不穿墙，距离并列时实体优先。
- **爆头判定**取 `AimPoint.Y` 而非子弹交点 Y（准心所指即所判）；AimPoint 须水平落在本目标 `HitscanRadius` 内，否则回退子弹交点。爆头伤害 `dmg << 1`（×2），事件发 `Crit` 而非 `Hit`。
- **散布**：表值 = 瞄准态最大偏转角，腰射再乘 `HipSpreadFactor=4`；每弹丸独立"偏转 → 求交 → 伤害判定"，弹丸序恒定。
- **伤害结算**（`DamageSystem`）：扣血；跨死线时置尸体期 180 帧、清水平速度与开火窗、清战斗运行态、写 `Kill` 命令 + `Death` 事件。保护期内（`InvulnerableUntilFrame`）不结算。测试房免死下跨死线保底 `Hp=1`，不写 Kill/Death。
- **计分**（`ScoreSystem`）：死亡只登记一次（`RespawnFrame==0` 守卫），`Deaths++`、`RespawnFrame = Frame + RespawnDelayFrames`；自杀/无归属不奖励击杀。

### 8.3 复活、出生点与比赛裁决

- 复活仅在 `Match.Phase == Running` 且到达 `RespawnFrame`：从**未占用**出生点中按 `帧号 ⊕ 实体 Id` 混洗选点（`SpawnPointSelector`，**不消费 `RngState`**），保留 Id/KD 与背包，重置运行态并开出生保护。
- `MatchRules` 四字段：`DurationFrames`/`KillLimit`/`RespawnDelayFrames`/`SpawnProtectionFrames`，默认 3 分钟（10800 帧）/20 杀/3 秒（180 帧）/2 秒（120 帧）；构造器做范围校验。
- `MatchSystem.Tick` **先判击杀上限，再计时**：Timer 为 0 表示不限时；每次 Tick 递减 1，归零以 `TimeLimit` 结束。结束时最高击杀为胜者，并列则 `Winner=0`（平局）。
- 帧事件由 `FrameEventKind` 单一定义（Fire/Hit/Death/Crit/Explosion/Respawn/ObstacleHit），事件是帧内瞬态，不进快照与 checksum。

### 8.4 地图与碰撞

`SimMapData` 是对局中只读的常量配置：`MaxObstacles=256`、`MaxSpawnPoints=16`、`GroundY`、`HalfWidth`/`HalfDepth`；障碍只有 XZ 平面的 Circle（半径）与 Box（半宽/半深），带 y 区间与表面类型（表面类型只服务命中特效分流，判定不读）。地图**不进快照、不进 checksum**。

移动是"先全体积分、再统一去穿插"：先按速度积分与重力/地面/边界钳制，再按实体升序 × 障碍升序逐个推出（推出而非预先阻挡，贴墙滑行是积分+去穿插的固有产物）。身体半径用 `BodyRadius`（烘焙自 prefab CC），与命中柱 `HitscanRadius` 解耦。另有 `HardMaxSpeed=25` 的合速度硬上限护栏。

### 8.5 玩法数据身份

`CombatConfigDigest` 是判定面表数据与规则位的运行期摘要（SHA-256 低 32 位）：基摘要覆盖 `TableLayout.JudgmentFiles` 的 `.bytes` 原始字节（固定顺序），终值再混入本局规则位。服务端在房间创建时固定（`StartGame.config_hash`），客户端按本地表字节复算，不等即拒局；代码侧判定常量随代码进 BuildHash。

## 9. 表现侧：插值、衰减、静默门

`Assets/LiteView/Modules/SimView.cs` 只读 Sim、只写视图：

- **`SnapshotInterpolationBuffer`**（远端 jitter buffer）：容量 8，按快照帧号有序；渲染帧 = `最新快照帧 − RenderDelayFrames(4)`，按 `60 × dt` 匀速推进并封顶；前后快照间按真实帧差插值（丢包时铺满而非冻结），快照停摆不外推。
- **硬切条件**：远端 `LifeStartFrame` 变化（复活）或位移 > `SnapDistance(3m)`；本地复活无条件硬切（直写 `CharacterController`，不走带扫掠的 `Move`），异常单帧跳变兜底 2m（平方阈值 4）。
- **`LocalReconciler`**：本地表现跟预测态 0 延迟；常规收敛走指数衰减 `ReconcileSharpness=10`，超阈值硬切，换实体/重连重新落位。
- **`FrameEventSilenceGate`**：`OnReconcile`/`OnRollback` 抬高 `SilenceUntilFrame`；`OnFrameEvents` 丢弃帧号 ≤ 该值的帧事件并计 `SilencedEvents`，放行的交 `EventSink`。

## 10. 边界与当前形态

1. **`FramePump` 零生产调用点**：定次步进（1:1）已实现，但客户端与服务端都不用它。
2. **`MovementValues` 装载了没人读**：18 字段中只有 `Gravity` 经 `CombatValues` 进机制，其余 17 项（走/跑/冲/滑铲/钩爪/闪现等）在 `Assets/LiteSim` 内无引用。
3. **静态读口仍在**：`CombatConfig.Loaded`、`WeaponConfig.Loaded` 作为表现面读口存在，机制面由纪律扫描 R13 禁止读取；生产构造（`BattleContext.OnStartGame`）仍从静态取实例后按参数下传。
4. **瞄准是屏心射线**：`AimPoint` 由相机屏幕射线与"实体圆柱→地面"求交得到，采集侧解算、服务端校验与判定；表现层的越肩相机数学在 `Assets/LiteFramework/Runtime/Modules/Camera/TpsCameraMath.cs`。
5. **私有面不预测**：弹药/技能 CD/背包/资源只随本人快照与权威反馈刷新，客户端不重建；这也是增量和解只比对公共面的原因。

## 11. 本章要点

- 确定性是**逐项消除不确定源**（时间/数学/随机/容器顺序/内存布局/系统顺序），每消一项都要有守卫防回潮；预测只覆盖移动与瞄准，和解按全量/增量分锚点。
