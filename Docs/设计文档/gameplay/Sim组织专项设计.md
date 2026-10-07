# Sim组织专项设计

> 状态：现行专项设计
> 版本：2.0
> 更新日期：2026-10-07
> 适用范围：LiteSim/Core 战斗权威模拟的**数据面与逻辑面**组织
> 维护责任：Sim 架构

- 权威层级：本文是 Sim Core 数据面与逻辑面组织的唯一权威，取代《实体分型表设计》v1.0（已归档 `Docs/归档记录/2026-10-07/`）；快照/协议面以《状态同步专项设计》为准，数值配置面以《玩法数值与Luban配置专项设计》为准。本文不得违反《游戏业务系统总设计》。
- 结论先行：
  1. **权威模拟核心保持 DOD 不动摇**——确定性、快照可拷、零分配回滚三条硬约束不因逻辑复杂化而放松。
  2. **数据面：脊柱不动、分型表平行展开、不上 ECS**——ECS 的组合性收益用"同槽位索引的平行数组"取得；结构变更/快照重写/确定性重证的成本不付。
  3. **逻辑面：逻辑按 OOP 组织、状态按 DOD 存放**——组合性行为（技能/状态效果/投掷物/区域）由无状态静态策略类承载，运行态全部留在既有平行值类型数组。
  4. 模拟外一切（LiteSim/View 表现层、LiteGame App 编排、LiteNet 传输、局外系统）维持面向对象规范，不在本文管辖。

> 当前进度：数据面——分型表首批（kind 位 + 三表接入 CopyTo/Checksum/Despawn 三面）已交付；系统消费面（Item/Projectile/ZoneSystem、CustomData blob 退役、EntityHp 归还）未开始，见[施工进度](../../施工进度/实体分型与配置归拢.md)。逻辑面——现行五系统（Input/Movement/Shooting/Damage/Cleanup）事实符合本设计；策略外壳显式契约与 P1 四系统落法未施工。

---

## 1. 硬约束：为什么核心内禁止"有状态 OOP"

三条硬约束（均有现行执行机制）推出两条推论：

| 硬约束 | 现行执行机制 | 有状态 OOP 的破坏点 |
| --- | --- | --- |
| 确定性（同种子同输入同结果） | `SimStep` 固定系统顺序、输入按 EntityId 升序、单一 `RngState`、IEEE 基线校验 | 堆对象引用身份与 GC 时机进入结果；静态可变字段跨帧漂移 |
| 快照完整性（漏字段 = 静默分叉） | `CopyTo` 逐数组登记 + 反射布局自检 + 公共/私有两口径 checksum | 对象图不可 `Array.Copy` 整块深拷；反射自检对 class 失效 |
| 回溯重入（系统可在历史帧副本上单独重跑） | `ShootingSystem` 回溯契约①②、`SnapshotRing` | 对象内部计时/缓存不随快照恢复，重跑结果分叉 |

**推论**：
- **结构必须可整块拷贝**——状态一律值类型定容数组，逐数组 `Array.Copy` 即深拷；
- **策略类必须无状态**——零实例字段、零静态可变字段，一切输入经参数传入，一切产出只写状态数组段 / `Cmds` / `Events`。

---

# 第一部分 数据面

## 2. 脊柱与分型表

### 2.1 脊柱不动

脊柱（`EntitySlot` 数组 + `AliveBitmap` + Id/代次）**不动**——快照/回滚/SlotDelta/Checksum 基线全部保持有效。P0 战斗运行态（`Weapons/Actions/Status/MatchBag/Resources`）同一条纪律：值类型定容数组、槽位寻址 `slot * PerEntity + i`；公开/私有分层由 `[StateLayer]` 标注单源（公共面进 SlotDelta，私有面进 PrivateStateSnapshot）。

### 2.2 分型表平行展开

脊柱之外新增"每型一张定容平行表"：

```csharp
// SimWorldState 内（全部定容、零分配、槽位索引即键）：
public ItemState[]     Items;      // 道具：类型/数量/归属/存在标记
public ProjectileState[] Projectiles; // 投掷物：速度/引信/主人/命中标记（手雷/闪光等）
public ZoneState[]     Zones;      // 区域效果：EMP/雷达/毒圈——半径/剩余时间/归属
```

- **型别掩码**：`EntityFlags` kind 位（`KindItem/KindProjectile/KindZone`）——"该槽位持有哪张分型表数据"的迷你 archetype mask；输入位与实体标志位是两个位空间的既有纪律照旧。
- **结构 = 槽位占用**：没有 ECS 的结构变更机制——分型行跟槽位同生共死（`Spawn` 置 kind 位、`Despawn`/`CleanupSystem` 清行）。
- **迭代序 = 槽位升序**：与输入升序排序同源，确定性免费。
- **CustomData blob 退役方向**：需要跨端恢复的正式状态一律进定型表。

### 2.3 布局硬约束单源

布局硬约束（#1～#13）**单源 = `SimWorldState.cs`/`EntitySlot.cs` 头注**，本文不复制条款。要点：纯托管零 unsafe、struct 内禁数组字段（浅拷共享必错）、自定义态落平面 byte 数组、运行期零 new、不做 swap-remove（保遍历序）、跨帧引用唯一入口 `TryResolve`。新增字段/新表时逐条对照代码头注执行。

## 3. 三面契约（缺一即隐形分叉）

新状态面（分型表及一切后续扩面）必须同时进**三个确定性面**：

1. **CopyTo/SimWorldStateSnapshot**：整段拷贝（定容数组 memcpy 级）。
2. **SlotDelta/增量协议**：每型一列（或一型一 delta 分组）——公共面只增字段号的既有纪律照旧扩号；私有面不进协议的照旧。
3. **SimChecksum 双口径**：公共可重建面纳入公共字段；私有面照 P0 口径（线上 checksum = 公共面）。

`CombatConfigDigest` 不动（分型表是状态面，不是配置面）。

**配置与状态的连接**：`ItemConfig`/`MovementConfig` 静态面是分型表与机制数值的数据源——**不新建配置面**，拾取/使用/效果数值全部按行读表；钩爪/传送走 `MovementConfig`，触发属输入位面（离散意图位），本设计不扩输入位。

---

# 第二部分 逻辑面

## 4. 三层模型

| 层 | 载体 | 职责 | 现状 |
| --- | --- | --- | --- |
| 编排层 System | 静态类 `Run(s, ...)` | 遍历（槽位升序）、占槽仲裁、槽位账本推进（冷却/阶段/窗/充能）、副作用产出顺序 | 现行五系统事实成立 |
| 行为层 Behavior（策略外壳） | 静态策略类纯函数 | 单实体单行为的结算细节（位移怎么算、半径怎么取目标、状态怎么写槽） | 本文新契约，P1 起显式化 |
| 配置层 Config | Luban 表（`tb_action`/`tb_action_num` 等）+ `CombatConfig` | 行为参数单源；`contentHash` 门禁、`CombatConfigDigest` 进 checksum | 总设计 §5/§6 既定 |

数据流：输入帧 → System 固定序编排 → 按配置行声明的行为类别查表选 Behavior → Behavior 读槽位段 + 配置行 → 写槽位数组段 / `Cmds` / `Events` → `FlushCommands` 固定轮次结算。

分工判据：**"何时发生、按什么顺序"归 System；"发生时具体算什么"归 Behavior；"参数是多少"归 Config。**

## 5. 策略外壳模式定义

### 5.1 统一签名与分发

- 策略类一律 `static class`、方法一律纯函数；上下文经 `in` 参数传入，直写只允许指向 `SimWorldState` 持有的数组段与 `Cmds`/`Events`。
- **分发键 = 配置行声明的行为类别**（编译期封闭的枚举），不是 defId 本身——多个动作可共用同类行为（总设计 §6.2 `tb_action` 动作类别列）；defId 行携带类别字段，装载时校验类别枚举全覆盖。
- 分发机制冻结：**行为类别 → 静态只读委托表**，构造期一次填充；运行期零分配、无装箱、无闭包。退化路径允许 switch 跳转（确定性等价），新增行为类别默认走委托表。

```csharp
// 示意（以施工为准；配置行类型以 Luban 生成物为准）：
internal static class ActionBehaviors                       // 行为策略：static、纯函数、零字段
{
    internal static void Dash(SimWorldState s, in SimMapData map, int slot, int startFrame, in ActionDefRow def) { }
    internal static void AoeBlast(SimWorldState s, in SimMapData map, int slot, int startFrame, in ActionDefRow def) { }
}

internal static class ActionBehaviorTable                   // 分发表：构造期填充并校验覆盖，运行期只读
{
    internal delegate void Behavior(SimWorldState s, in SimMapData map, int slot, int startFrame, in ActionDefRow def);
    internal static readonly Behavior[] ByKind = { /* 行为类别枚举序；缺项 = 启动失败（§7） */ };
}
```

### 5.2 策略类确定性义务（六条，违一即分叉风险）

1. 无实例字段、无静态可变字段；
2. 运行期零 new（闭包/装箱/LINQ 禁入）；引擎 API、日志、时钟禁入；
3. 浮点只经 `SimMath`/`SimTrig` 单源；计时用整数帧（禁止浮点倒计时）；
4. 随机数只经 `s.RngState` 局部副本推进写回（`SimRng` 使用约定）；
5. 副作用只写：本实体槽位数组段、`Cmds`、`Events`；多目标产出按 EntityId 升序；
6. 不重排遍历、不依赖哈希序——调用方保证的槽位升序是结果的一部分。

前两条是 `ShootingSystem` 回溯契约（不改时序、只写瞬态与授权槽位）的推广；违例由 §8 测试层级兜底。

### 5.3 与布局契约的关系

策略类**永不新增状态字段**。需要新状态一律扩 `SimWorldState` 平面数组（值类型、定容、槽位寻址），并进 §3 的三面契约。策略外壳是逻辑组织工具，不是存储工具。

## 6. 系统落位与策略应用

### 6.1 SimStep 顺序插位

目标固定顺序（分型系统消费面待接入）：`Input → Movement → Shooting → Item → Projectile → Zone → FlushCommands → Cleanup`——投掷物/区域在射击后、命令结算前（命中产伤害命令进当帧缓冲）。系统集合编译期确定，无自动扫描。

### 6.2 系统落位与行为粒度

| 系统 | 独占读写 | 上游配置 | 语义 | Behavior 粒度 |
| --- | --- | --- | --- | --- |
| `ItemSystem` | `Items[]` | tbitemconfig（刷新间隔/最大存在数/拾取半径/携带上限/使用时长） | 刷新/拾取判定/使用计时 | 每道具类别一策略 |
| `ProjectileSystem` | `Projectiles[]` | tbitemconfig（投掷物速度/手雷伤害/半径/衰减） | 推进/引信/命中结算 | 每投掷物类别一策略 |
| `ZoneSystem` | `Zones[]` | tbitemconfig（EMP/雷达半径与持续时间） | 区域生效/衰减/回收 | 每区域类别一策略 |
| `StatusSystem` | `Status[]` | 效果来源行（技能/道具行携带的数值） | tick/过期/投影写回（`EntitySlot.Shield` 投影单源） | 每效果类别一策略 |
| `ActionSystem` | `Actions[]` | tb_action/tb_action_num | 占槽仲裁/四阶段/冷却账本 | 每行为类别一策略（§6.3） |

### 6.3 ActionSystem 落地（对照总设计 §6.3 首版三技能）

| 技能 | Behavior 结算（策略外壳管） | System 账本（不进策略） |
| --- | --- | --- |
| 冲刺 Dash | 位移经 `MovementSystem` 既有障碍口径执行（不另开碰撞路径）；无敌帧以 `EntityFlags` 位承载（位定义归 EntityFlags 扩展），`DamageSystem` 结算时校验 | 占槽仲裁、四阶段推进、冷却账本、死亡打断 |
| 范围爆破 AOE | 生效帧取目标 = 槽位升序全量扫 + 水平距离平方比较（零分配，当前实体量级不需空间索引）→ 逐目标 `Cmds.Write(Damage)` + `Events.Write(Explosion)` | 前摇/生效/后摇节拍 |
| 能量护盾 Shield | 写 `StatusSlotData`（EffectId/EndFrame/Param）+ `EntitySlot.Shield` 公共投影 | 前摇节拍、持续时间到点回收 |

- 占槽仲裁仍归 `ActionSlots.TryOccupy`（总设计 §6.4：优先级 → EntityId）；策略只负责"占用后怎么演"。
- 冷却/充能（`ActionRuntime.CooldownEnd`/`Charges`）是槽位账本，System 每帧推进，策略只读。
- 技能请求不预测、View 按 `FrameEvent`/`ActionState` 播放表现——策略外壳不改变该边界（总设计 §6.1）。

## 7. 失败语义与容量/性能预算

- **行为表缺项：构造期 fail-fast**——配置装载时核对每个行为类别都有注册策略，缺失即启动失败；运行期不判空、不跳过（静默跳过 = 两端版本行为不一致时分叉，宁可起不来）。
- **Cmds/Events 满载**：沿用既有 Overflow 丢弃 + 计数上抛语义（`CommandBuffer`/`FrameEventBuffer`），策略不扩容、不重试。
- **性能预算**：策略调用路径零分配（静态委托 + `in` 参数）；逐帧热点预算沿用《性能工程专项设计》，本文不复制数值。

## 8. 测试层级与验收

| 层级 | 内容 | 对应机制 |
| --- | --- | --- |
| 单元 | 单策略在构造态世界上的输入→产出断言 | .NET 8 无头工程（与 `SimReplayRunner` 同宿主） |
| 确定性 | 同种子同输入重放 checksum 一致；回滚 diff 零漂移 | `SimChecksum`/`SimWorldDiff`/IEEE 基线 |
| 覆盖 | 配置表每个行为类别均有注册策略（缺项即失败） | 构造期装载校验（§7） |
| 两端 | 三技能前摇/判定/持续/冷却/消耗两端一致；回滚不重复伤害/消耗/表现 | 总设计 §6.5 验收沿用 |

数据面验收基线已由 `EntityKindTablesTests` 钉住（CopyTo 三表深拷、清行后 checksum 与空槽恒定、双口径纳入）；系统消费面接入时沿用同基线，另验增量往返与房间链路不回归。

## 9. 明确不做

- 不上 ECS、不引入对象图状态、不做模拟内 Lua/脚本行为——总设计 §6.1 已定 Sim 只执行有界效果指令。
- 策略类不做实例多态（虚分派本身确定，但实例化即堆分配且诱导持有状态）——static class + 委托表封死。
- 不为"纯 OOP 好看"把现行五系统改写成策略外壳——它们逻辑单一，保持现状；模式只对组合性行为生效。
