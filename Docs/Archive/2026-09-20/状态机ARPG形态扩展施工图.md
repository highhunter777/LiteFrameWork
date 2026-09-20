# 状态机 ARPG 形态扩展施工图（表驱动 · 抢占 · 恢复栈）

> 2026-09-17。**用户三决策**：① 表达方式 = **表驱动阶段**（先做 A，专用动作机后置）② 被抢占后 = **默认取消 + 可选入栈恢复** ③ 优先级与中断规则 = **静态住表 + 请求可覆盖**。
> 定位：把框架状态机从"少而杂"（流程/UI）扩展到"**多而规则同构**"（单机 ARPG 的几十上百个动作态）的形态。**只动 `LiteFramework.Core`，不改 Sim 红线**。
> 前置：《通用流程状态机施工图》（StageMachine 本体）、《层级状态机施工图》（HSM）、《角色状态与动作实现设计》（为什么联机侧用不了框架状态机）。
> **一句话**：这是给**单机/非 Sim** 场景的形态扩展 —— **联机（Sim 内）依旧用"数据 + 表"，框架状态机借不到力**（红线不变）。

---

## 0. 为什么要改：三个真实缺口

| 缺口 | 现状 | 后果 |
|---|---|---|
| **状态多而规则同构** | `IStage` = 一个状态一个**类** | 60 个格斗/ARPG 动作态 = 60 个类 + 60 处迁移逻辑（表驱动只需 1 个实现 + 若干表行） |
| **状态 id 集合须编译期固定** | `where TId : struct, Enum` | 技能/动作从表来、集合不固定 → **枚举约束直接卡死** |
| **抢占与恢复语义缺失** | 只有"重入抛 / 未注册抛" | ARPG 必需：优先级抢占、霸体（不可打断）、被打断后可恢复 |

## 1. 决策（用户拍板）

| # | 决策 | 落法 |
|---|---|---|
| 1 | **表驱动阶段** | 新增 `StageSpec<TId,TReq>`（一行 = 一个状态的数据）+ 通用 `TableStage<TId,TReq>`（**所有状态共享同一实现**）；`StageMachine` 允许同一实例挂多个 id；id 约束放宽为 `struct, IEquatable<TId>` |
| 2 | **默认取消 + 可选入栈恢复** | `ResumeMode { Cancel, Resume }` 住 `StageSpec`；被抢占时按它决定"丢弃"还是"入恢复栈"；栈深上限 4，超限丢最老并计数 |
| 3 | **静态 + 请求可覆盖** | 优先级与"能否被打断"静态住 `StageSpec`；`Request(..., int priorityOverride)` 可临时覆盖（必杀霸体这类） |

## 2. API 形态（**能力分层版**，2026-09-17 修订）

> **修订说明**：初版把抢占/恢复直接加进 `StageMachine`，使基础机承载两套关注点（通用阶段机 + ARPG 抢占）。
> 已改为**能力分层**——基础机瘦身，ARPG 能力抽到子类（纯加法）：

```csharp
public class   StageMachine<TId, TReq>              // 基础机：状态表 / 迁移 / 守卫 / 计数 / 帧窗口（4 个入口）
public class   PreemptiveStageMachine<TId, TReq>    // 抢占机（子类）：+ 优先级 / 中断规则 / 恢复栈 / priorityOverride
public class   HierarchicalStageMachine<TId, TReq>  // 层级机（独立）：层级 / 历史 / 冒泡（事务语义不同，不合并）
```

| 能力 | 基础机 | 抢占机（子类） | 层级机（独立） |
|---|---|---|---|
| 状态表 / 迁移 / 守卫 / 计数 / `StageFrames` / 表驱动配套 | ✅ | 继承 | ✅（自己的实现） |
| 优先级抢占 + 中断规则 + 恢复栈 + `Request(…, priorityOverride)` | ❌ | ✅ | ❌ |
| 层级 / 历史 / 冒泡 | ❌ | ❌ | ✅ |
| 快照项 | 6 项 | +2（恢复栈深 / 丢弃恢复） | 自己的 9 项 |

**选型一句话**：**流程 / UI / 转场 → 基础机；角色 / 动作（格斗、ARPG）→ 抢占机；界面内子流程 → 层级机。**

```csharp
// 约束放宽（三处同步：StageMachine / PreemptiveStageMachine / HierarchicalStageMachine / CompositeSpec）
public class StageMachine<TId, TReq> ... where TId : struct      // 去掉 Enum 约束

// ---- 可选能力接口（不用默认接口方法——IL2CPP 下 DIM 有坑，走可选接口最稳）----
public interface IPriorityStage { int Priority { get; } }                    // 大者胜；未实现 = 0
public interface IInterruptPolicy<TId> { bool CanBeInterruptedBy(TId incoming); }
//   未实现 = 默认可被打断；实现了 = 细粒度（谁能打断我）

// ---- 表驱动阶段 ----
public enum ResumeMode : byte { Cancel = 0, Resume = 1 }

public sealed class StageSpec<TId, TReq> where TId : struct, IEquatable<TId>
{
    public TId Id;
    public int Priority;                       // 静态优先级（决策 3）
    public ResumeMode Resume = ResumeMode.Cancel;
    public bool CanBeInterrupted = true;       // false = 霸体（简化版规则）
    public Func<TId, bool> CanBeInterruptedBy; // 细粒度规则（非空则优先于 CanBeInterrupted）
    public int DurationFrames;                 // >0：到时自动迁移/恢复
    public TId NextId;                         // DurationFrames 到点后去哪
    public bool AutoResumeOnEnd;               // 到点后优先"恢复栈顶"而不是 NextId
    public Action<StageSpec<TId, TReq>> OnEnterAction;   // 可选钩子（逻辑薄）
    public Action<StageSpec<TId, TReq>> OnUpdateAction;
    public Action<StageSpec<TId, TReq>> OnLeaveAction;
}

public sealed class TableStage<TId, TReq> : IStage<TId, TReq>, IPriorityStage, IInterruptPolicy<TId>
{
    public TableStage(StageSpec<TId, TReq> spec);   // 一行 spec = 一个实例
    // 行为全部读 spec；**自身零状态**（驻留帧数从 host.StageFrames 读，不自存）
}

// ---- 帧窗口（表驱动的"前摇 N 帧内可取消"需要它）----
public interface IStageHost<TId, TReq> {
    ... 既有 4 项 ...
    int StageFrames { get; }        // ★ 新增：当前阶段驻留的**整数帧数**（每次 Tick 调一次 = 1 帧）
}

// ---- 请求返回 bool（被优先级拒绝不抛——"打不动霸体"是正常路径）----
public bool Request(TId nextId, in TReq req);                        // 被拒 → false
public bool Request(TId nextId);
public bool Request(TId nextId, in TReq req, int priorityOverride);  // 决策 3 的覆盖入口
public bool HasResumePending { get; }                                // 恢复栈非空
public bool TryResume();                                             // 弹出栈顶并迁回（被拒 → false）
public int ResumeDepth { get; }                                      // 栈深（诊断/HUD）
```

**语义钉死（新增 8 条，与既有 7 条并存不回退）**：

| # | 语义 |
|---|---|
| N1 | 拒绝是**返回值**不是异常：`Request` 被优先级/中断规则挡下 → 返回 `false`，**不改变 pending** |
| N2 | 抢占判定在 **`Request` 时**（fail-fast），规则 = `incoming.Priority > current.Priority` 且 `current` 允许被 `incoming` 打断 |
| N3 | `priorityOverride` 只作用于**这一次请求**（不写回阶段） |
| N4 | 被抢占的当前阶段：`Resume == Resume` 且**允许被打断**时才入恢复栈（`Cancel` 直接丢弃） |
| N5 | 恢复栈**后进先出**，深度上限 4：超限丢**最老**（栈底）并计入 `ResumeDropped` |
| N6 | `TryResume()` 弹出栈顶并迁移回去；栈空或目标已被优先级拒绝 → `false` |
| N7 | `DurationFrames` 到点：`AutoResumeOnEnd` 优先 `TryResume()`，否则 `Request(NextId)`（`NextId` 未设则不动作） |
| N8 | `StageFrames` **每次 `Tick` 调用 +1**（不是 `dt` 累加），迁移后归零；与 `StageTime`（秒）并存 |

## 3. 能力分层（判据）

| 能力组 | 落点 | 判据 |
|---|---|---|
| 状态表 / 迁移 / 守卫 / 帧窗口 | **基础机** `StageMachine` | 所有状态机都需要 |
| 优先级抢占 / 中断规则 / 恢复栈 | **抢占机** `PreemptiveStageMachine`（子类） | ARPG/格斗这类"动作密集"才需要；流程/UI 用不到 → 不进基础机 |
| 层级 / 历史 / 冒泡 | **独立** `HierarchicalStageMachine` | 其"迁移 = LCA 事务"与平面机**算法本质不同**；硬合=组合爆炸（跨层事务 × 抢占栈 × 历史 × 冒泡） |
| 表驱动（`StageSpec` + `TableStage`） | 配套件，可挂任一平面机 | 状态多而同构时用；被抢占才需要恢复 → 与抢占机配套 |

**实现形态**：抢占机是**子类**而非配置开关 —— 基础机通过两个扩展点（`CanAccept` 准入判定 / `OnStagePreempted` 离场前通知）+ 一个绕过准入的入队口（`EnqueueRequest`，恢复用）开放能力，基础机自身**不含任何抢占代码**（多出的是 3 个空/恒真的扩展点）。

## 4. 与 Sim 红线的关系（必须写清，防误读）

- 本次扩展**只在 `LiteFramework.Core`**；`LiteSim.Core` 依旧 `references: []` → **联机（Sim 内）的角色状态机仍然用"枚举 + 表 + 帧号"自建，框架状态机进不去**（《角色状态与动作实现设计》§2）。
- 因此这套扩展的真实消费者是：**单机 ARPG / 非 Sim 的角色与 AI 状态机 / 界面内多段流程 / 观战·回放的相机与演出编排**。
- **不要**因为"框架现在支持表驱动了"就把它搬进 Sim —— 红线是"Sim 不引用 LiteFramework"，不是"框架能力不够"。

## 5. 影响面清单

| 文件 | 改动 |
|---|---|
| `Core/Fsm/IStageHost.cs` | + `int StageFrames { get; }` |
| `Core/Fsm/StageMachine.cs` | 约束放宽；`Request` 返回 bool（+ 覆盖重载）；抢占判定；恢复栈 + `TryResume/HasResumePending/ResumeDepth/ResumeDropped`；`StageFrames` 计数；`Snapshot` 加栈深 |
| `Core/Fsm/HierarchicalStageMachine.cs` | 约束放宽；+ `StageFrames`（最深活动态） |
| `Core/Fsm/CompositeSpec.cs` | 约束放宽（`HistoryMode`/`CompositeSpec`/`IEventSink` 内的 `TId` 约束） |
| `Core/Fsm/StageSpec.cs`（**新增**） | `ResumeMode` / `StageSpec` / `IPriorityStage` / `IInterruptPolicy` |
| `Core/Fsm/TableStage.cs`（**新增**） | 通用表驱动阶段实现（零自身状态） |
| `Core/Fsm/PreemptiveStageMachine.cs`（**新增**） | 抢占机子类：`CanAccept`（优先级 + 中断规则 + 自身推进放行）、`OnStagePreempted`（入恢复栈）、`TryResume`、`priorityOverride` 重载、快照 +2 |
| `Tests/.../StageMachineArpgTests.cs`（新增） | 见 §6 + 2 条能力分层用例（基础机无抢占 / 快照项数区分） |
| 调用点 | `Request` 由 void → bool：**源码兼容**（忽略返回值即可），现有 4 个流程与测试零改动 |

## 6. 测试清单（新增 ≈14 用例）

**id 放宽**：`int` 作 TId 跑通（表驱动技能 id）；枚举 TId 不回归。
**表驱动**：一行 spec 正常进入/更新/离开；钩子被调用；`DurationFrames` 到点自动迁移；`NextId` 未设则不动作。
**优先级/抢占**：高优先级抢占；低优先级被拒（返回 false 且 pending 不变）；`priorityOverride` 临时越级；霸体（`CanBeInterrupted=false`）挡住；`CanBeInterruptedBy` 细粒度规则生效。
**恢复栈**：`Resume` 模式被抢占 → 入栈；`TryResume` 回到原阶段；`Cancel` 模式不入栈；栈深上限丢最老 + 计数；`AutoResumeOnEnd` 优先恢复。
**帧窗口**：`StageFrames` 每 Tick +1、迁移后归零（阶段用 `host.StageFrames` 做"前 N 帧才允许取消"的判定）。
**不回退**：既有 `StageMachineTests` 11 条 + `HierarchicalStageMachineTests` 26 条语义全绿。

## 7. 不做（判据留档）

| 不做 | 判据 |
|---|---|
| 专用动作机（`ActionMachine`：帧数据 + 取消窗口 + 连段派生） | **后置**——先看表驱动够不够（用户决策①的"动作机后置"）。真上：出现"需要可视化编辑帧数据 + 连段派生图"时 |
| HSM 也加抢占/恢复栈 | 组合爆炸（§3 判据） |
| 把 `IStage` 改默认接口方法（DIM）拿 `Priority` | IL2CPP 对 DIM 的支持有坑 → 走**可选接口** `IPriorityStage` |
| 状态对象持实例字段 | 保持"无状态单例"（多角色共用一个阶段实例；驻留数据走 `StageFrames`/payload） |
| 在 Sim 内使用本扩展 | Sim 红线（§4） |

---

## 附：实施记录（待填）
---

## 附：实施记录

**批次实况（2026-09-17，A1–A3 一次做完）**：

| 件 | 交付 |
|---|---|
| `Core/Fsm/StageSpec.cs`（新增） | `ResumeMode` / `IPriorityStage` / `IInterruptPolicy<TId>` / `IResumeStage` / `StageSpec<TId,TReq>` |
| `Core/Fsm/TableStage.cs`（新增） | 通用表驱动阶段（**零自身状态**：驻留帧数从 `host.StageFrames` 读，故可被多角色共享） |
| `Core/Fsm/StageMachine.cs` | 约束放宽 `struct`；`Request` → **返回 bool**（+ `priorityOverride` 重载）；抢占判定（`CanPreempt`）；恢复栈（`TryResume`/`HasResumePending`/`ResumeDepth`/`ResumeDropped`，深度上限 4）；`StageFrames` 计数；Snapshot 增 4 项 |
| `Core/Fsm/IStageHost.cs` | + `StageFrames`；`Request` 返回 `bool`（表达"可能被拒"）；约束放宽 |
| `Core/Fsm/HierarchicalStageMachine.cs` | 约束放宽；+ `StageFrames`；`Request` 恒返回 `true`（**不做抢占**，见 §3 判据） |
| `Core/Fsm/CompositeSpec.cs` / `ProcedureStage.cs` | 约束放宽（零行为改动） |
| `Tests/.../StageMachineArpgTests.cs`（新增 **16 用例**） | 表驱动/时长/钩子；优先级抢占/拒绝/同优先级/霸体/细粒度规则/覆盖；恢复栈（入栈/迁回/Cancel 不入栈/超深丢弃/AutoResume）；帧窗口；自定义 struct 作 id |

**实施期发现的一处设计修正（重要，已写回 §2 语义 N2）**：

> **必须区分"自身推进"与"外部抢占"** —— 否则"攻击（优先级 10）播完自动回 Idle（优先级 0）"会被
> **自己的优先级规则挡住**（写用例时暴露）。
> 落法：`StageMachine` 增加 `_inStageCallback` 标记，**阶段钩子（`OnEnter`/`OnUpdate`）内发起的迁移一律放行**；
> 抢占规则只约束**钩子外**的请求（玩家输入/系统）。
> 这也让"连段下一段""时长到点回 Idle"这类自身推进天然合法。

**另一处契约调整**：`IStageHost.Request` 由 `void` 改为 `bool`（编译期报 `CS0738` 暴露的）——
"被拒"是**正常业务路径**（打不动霸体），契约应表达它；HSM 侧恒 `true`。既有调用方零改动（语句式调用忽略返回值）。

**两处用例期望写错（实现是对的，已修）**：① 请求了未注册的 `Move` 触发"未注册即抛"（应先查注册表）；② 超深丢弃计数按 6 次请求算成 2，实际 5 次入栈 → 1（首次 `Idle→S0` 的 Idle 不具 Resume）。

**二次修订（同日，能力分层重构）**：用户提问"框架状态机是不是太复杂了，要不要抽内核做两套 API" → 结论是**不抽内核、改做能力分层**（理由：flat 与 HSM 的差异在**算法**（单步迁移 vs LCA 事务）而非骨架，抽内核只省 ~80 行却要付"策略间接层 + 全量重跑"；判据见《通用流程状态机施工图》§10）。
落法：**抢占/恢复抽到 `PreemptiveStageMachine` 子类**（基础机只多 3 个扩展点、零抢占代码）；**文档按"基础 API / 扩展 API"分级**。基础机因此回到"4 个入口"的简单面。

**验收数据**：

- **L1 = 268 绿**：LiteFramework **189**（171 + 16 ARPG + 2 能力分层）/ LiteSim 68 / LiteNet 11，0 失败；既有 11 + 26 条语义**无回退**。
- **Unity 编译 0 错误**。
- **Play 冒烟（Test.unity）**：`current=Main`、`switches=2`、**`frames=382`**（顺带验证新计数）、`assetInit/luaMain=True`、启动后 **error 0**。
- **环境坑（复用既有手法）**：本次 `editor_focus` + `set_autotick` **均未唤醒帧泵**（Play 后 `StageFrames` 冻在 1、`AssetService` 卡 init）→ 改用**项目既有手法 `editor_pause` + `EditorApplication.Step()` 步进 400 帧**，一次通过。**结论：Play 冒烟在网络/失焦环境下优先用"暂停+步进"**，比反复 focus 稳。

**未做**（§7 判据保留）：专用动作机 `ActionMachine`（后置）、HSM 抢占栈、Sim 内使用本扩展。
