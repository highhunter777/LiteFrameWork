# 战斗 HUD 对局倒计时专项设计

> 状态：草案（待评审）
> 版本：0.1
> 日期：2026-10-30
> 定位：回答"一局还剩多少时间"这件事如何从权威逻辑帧轴变成 `Hud_Battle` 上的一行 `mm:ss`——数据语义、真值源、显示状态机、刷新纪律、prefab 形态与验收判据。
> 不做的事：不定义赛制（时长/击杀上限/复活与保护期归 `MatchRules` 与房间模板配置）；不做开局倒计时、等待期倒计时、加时赛；不做低时音效/脉冲动效（归 §12 扩展）；不改协议与公共快照字段（`MatchStateDelta` 既有 `timer` 承载）；不决定字体与配色的美术定稿（制作侧/TA 定档，本文只给约束与语义档位）。
> 冲突裁决：数据语义以 `Assets/LiteSim/Scripts/Pipeline/SimMatchRuntime.cs`（`MatchStateData`）、`Assets/LiteSim/Systems/MatchSystem.cs` 为准；HUD 装配与刷新纪律以 [UI 系统总设计](UI系统总设计.md#ch-hud) §8 与 `Assets/LiteGame/App/Presentation/Hud/**` 现行代码为准；制作硬规则以[规范 05 UI 制作](../../../规范/05-UI制作.md)为准。
> 关联：[UI 系统总设计](UI系统总设计.md) · [UI 命名与控件词表参考](UI命名与控件词表参考.md) · [规范 05 UI 制作](../../../规范/05-UI制作.md) · [说明 05 Sim 与战斗判定](../../../说明/05-Sim与战斗判定.md) · [说明 07 联机全栈参考](../../../说明/07-联机全栈参考.md) · 施工状态归 `Docs/施工进度/`

## 1. 需求与口径

### 1.1 一句话

在战斗 HUD 常驻一行**局时倒计时**（`mm:ss`，整秒跳动），对应当前对局 `MatchStateData.Timer`（剩余逻辑帧）；时间到 0 时冻结 `00:00` 直到流程层离开对局。

### 1.2 做什么 / 不做什么

| 面 | 本文范围 | 说明 |
| --- | --- | --- |
| 局时倒计时（剩余时间） | ✔ | 唯一交付物：一行文本 + 一个驱动 + 读模型事实 |
| 复活倒计时 | ✘ | 已有 `BattleRespawnHudDriver`（中央面板），本件不得与之复用同一文本节点 |
| 开局倒计时（3/2/1） | ✘ | 协议与 Sim 无对应阶段；出现真实需求再立专项 |
| 等待期/房间倒计时 | ✘ | 属房间页（Screen UI），不走 HUD |
| 时间到后的裁决 | ✘ | 归 `MatchSystem.Finish(TimeLimit)` 与 `MatchEnded` 流程 |
| 低时变色/脉冲 | ✘（扩展） | 见 §12；v1 不做，避免代码写 RGB |

### 1.3 术语

| 术语 | 含义 |
| --- | --- |
| **局时** | 本局从开局到时限的剩余时间，单一来源 `MatchStateData.Timer`（逻辑帧） |
| **秒值** | `ceil(Timer / SimConfig.TickRate)`，即玩家看到的整秒 |
| **不限时** | 房间规则 `DurationFrames == 0`：`Timer` 恒为 0 但 `Phase` 仍 `Running` |

## 2. 数据语义（唯一真值）

`MatchStateData`（公共面，随每份快照下发）中与本文相关的字段：

| 字段 | 类型 | 语义 | 本文用法 |
| --- | --- | --- | --- |
| `Timer` | `int` | 剩余**逻辑帧**；`MatchSystem.Tick` 每帧递减 1，归零即 `Finish(TimeLimit)` | 秒值换算的唯一输入 |
| `Phase` | `int` | `0=Unmanaged / 1=Running / 2=Finished`（`SimMatchPhase`） | 判别 `Timer==0` 的二义（不限时 vs 已结束） |
| `EndReason` | `MatchEndReason` | `TimeLimit / KillLimit / External` | 不参与显示（v1 不区分结束原因文案） |

**换算口径**：

```text
秒值  = (Timer + TickRate - 1) / TickRate      // 整数上取整（TickRate = 60）
文本  = 秒值 / 60 : 秒值 % 60                  // "03:00"、"00:09"
可见  = Phase == Running && Timer > 0          // 其余情形见 §4
```

上取整的理由：开局 `Timer=10800` 必须显示 `03:00` 而不是 `02:59`；归零前一帧 `Timer=1` 显示 `00:01`，`Timer=0` 才显示 `00:00`——与"整秒跳动、最后一秒真实存在"的玩家预期一致，且与既有 `BattleRespawnHudDriver` 的整秒口径同源。

> 边界：房间若配非 60 整数倍的 `match_duration_frames`，首帧可能多显示 1 秒（上取整的必然结果）。要严格对齐由配置取整，不在 HUD 侧特判。

## 3. 真值源与刷新模型

### 3.1 帧轴（现行形态）

```text
服务器：每逻辑帧 SimStep → MatchSystem.Tick（Timer--） → 每 2 帧（SnapshotHz=30）广播一份快照
                                                          └─ SnapshotDiffer 每份都带 MatchStateDelta.timer
客户端：收到快照 → SnapshotReassembler.Apply 重建镜像 Match
                    → BattleSnapshotMirror.ApplyAndReconcile：和解则整帧覆盖；未和解则
                      OverlayPrivateAndMatch 把权威 Match 写回本地预测态
                    → 预测态每逻辑帧再 SimStep → MatchSystem.Tick（Timer 同步递减）
HUD   ：每渲染帧读本地预测态 → 秒值 → 变化才写
```

结论：**本地预测态的 `Match.Timer` 是"权威锚定 + 本地逐帧推进"的量**——每份快照被权威值覆盖一次，帧间由本地继续递减。

### 3.2 裁决：读预测态，不读墙钟，不直读权威镜像

| 候选 | 结论 | 理由 |
| --- | --- | --- |
| 本地预测态 `context.Sim.State.Match`（经读模型） | **采用** | 60Hz 平滑；与 HUD 其余件同一读源实例；快照到达即自愈，显示不会停秒 |
| 权威镜像 `context.AuthorityState.Match` 直读 | 不采用 | 30Hz + 丢包/抖动时"停一秒再跳一秒"；且 HUD 面板件读的是预测态，两个读源会让同一屏出现两种时基 |
| `Countdown` 控件（`Widgets/Countdown.cs`，`Time.unscaledDeltaTime` 墙钟） | 不采用 | 引入第二时基：暂停、卡顿、断线重连、低帧率都会与判定帧轴漂移；UI 时层是给页面动效用的，不是给对局事实用的 |
| 服务端时间戳/系统时钟换算 | 不采用 | 客户端不持有权威时钟；任何"本地时间 ± 偏移"的做法都需要额外校时协议 |

**校正边界**：客户端预测领先上限 `SimConfig.MaxPredictionLead`（6 帧 ≈ 100ms），远小于 1 秒，故秒值跳变正常；即使出现一次丢包导致秒数回跳，也被 §5 的"整秒脏位"吸收为最多一次重写。

**不做的事**：倒计时显示不得回写 Sim、不得触发 `MatchSystem` 任何动作、不得作为胜负判据——它只是 `Timer` 的只读投影。

## 4. 显示状态机

| # | 条件 | 根件 | 文本 | 备注 |
| --- | --- | --- | --- | --- |
| S0 | 未对齐（无 Sim / `Match` 未建） | 隐藏 | — | 如实收起，不猜 |
| S1 | `Phase == Unmanaged(0)` | 隐藏 | — | 开局前（理论窗口极短） |
| S2 | `Phase == Running && Timer > 0` | 显示 | `mm:ss` 每秒更新 | 常规态 |
| S3 | `Phase == Running && Timer == 0` | 隐藏 | — | **不限时**房间：不显示 `00:00` 假剩余 |
| S4 | `Phase == Finished` | 显示 | 冻结 `00:00` | 到点/击杀上限/外部结束一致；等流程层离场拆 HUD |
| S5 | prefab 缺件（制作期漏接/错改） | 不创建驱动 | — | 一次性 `Log.Warning`，HUD 其余面照常（防御性降级，非兼容承诺） |

**不变量**：

- 永不显示负值或 `-1`；`Timer` 只经上取整换算。
- 文本只由本驱动写（单写者）；`Txt_MatchTimer` 不挂 `BindNode` / `LTextKeyRef`（HUD 不走页面绑定与本地化 key，见 UI 总设计 §3.2 裁决 4）。
- S3 与 S4 的区分**只能**靠 `Phase`：`Timer` 为 0 本身不携带"是否已结束"的信息。
- 玩家阵亡等待期间（`Cot_Respawn` 中央面板可见）计时器保持可见——局时对死者同样有意义。

## 5. 结构（prefab 形态）

### 5.1 节点与落位

```text
Hud_Battle
└─ Cot_Panel                     （整组显隐的 CanvasGroup 所在层）
   ├─ Cot_Rankboard              （顶部中央，1120×56 @ y=-16，8 格横排）
   ├─ Cot_MatchTimer             （新增；空 RectTransform 容器，无 Image）
   │  └─ Txt_MatchTimer          （新增；TMP 文本，唯一写者）
   ├─ Cot_NetStats               （左上）
   └─ Cot_KillFeed               （右上，y=-84 起）
```

| 节点 | RectTransform | 值 | 理由 |
| --- | --- | --- | --- |
| `Cot_MatchTimer` | anchorMin/Max | `(0.5,1)` / `(0.5,1)` | 顶部中央带，与 `Cot_Rankboard` 同一锚点族 |
| | pivot | `(0.5,1)` | 向下生长，分辨率变化时不侵入排行榜 |
| | anchoredPosition | `(0, -84)` | 排行榜行（-16..-72）正下方的第二行；避开左上 NetStats（-24..-52）与右上 KillFeed（-84 起，中心列无交叠） |
| | sizeDelta | `(168, 34)` | 容纳 `03:00` 五字符 + 边距 |
| `Txt_MatchTimer` | 铺满父件（offsets 0） | — | 容器尺寸即文本区，改字号不动锚点 |
| | 对齐 | 水平/垂直居中 | 数字居中，秒跳时不横移 |

> 交叠说明：`Txt_Score`（团队比分预留节点，`FFA` 下恒隐藏）位于 `(0,-64)`、140×26，与计时器上沿有约 6px 交叠——当前不可见故无影响；`Team=1` 落地时按 §11 R5 与计时器同排重排。

### 5.2 文本与视觉约束

| 项 | 约束 | 依据 |
| --- | --- | --- |
| 字体资产 | 复用 HUD 全树同一份 `Assets/ShareResource/Fonts/NotoSansSC SDF.asset` | 不为一个读数引入第二字体/第二图集 |
| 字号 | 语义档位"读数级"，建议 30–34（`Txt_Score`=22、`Txt_Ammo`=54、`Txt_RespawnCountdown`=72 之间） | 最终值制作侧定档；**代码不设置字号** |
| 颜色 | 沿用 HUD 现有烘焙值口径（本节点不挂 `UiStyleRef`） | HUD 全树当前零令牌引用，单点接令牌会造成视觉来源分叉；如接令牌须同批登记 HUD 例外（规范 05 UI-TEXT-08） |
| `raycastTarget` | **false**（TMP 默认 true，必须显式关） | 规范 05 UI-RENDER-02；`BattleHudMenuEditModeTests` 钉住"全 HUD 恰 1 个 raycastTarget=true（菜单钮）" |
| 富文本/自动换行 | 关闭 | 纯数字文本，无样式标签 |
| 静态文案 | 不挂 `LTextKeyRef` | 动态数值，走驱动写 `text` |
| 布局组 | 不挂 LayoutGroup / LayoutElement | 固定单件，避免与父层布局互相拉扯 |

### 5.3 不进页面的理由

HUD 属对局表现域，由 `BattleHudAssembler` 随对局视图创建/销毁，不走 `UIService` 导航、无 `tbuiform` 行、无页面契约（UI 总设计 §3.2 / §8.1）。倒计时是高频读数，走页面反而要额外承担遮罩、焦点与缓存语义。

## 6. 驱动与接线

### 6.1 新增驱动

`Assets/LiteGame/App/Presentation/Hud/BattleMatchTimerDriver.cs`（`IDisposable`，单一职责）：

| 成员 | 契约 |
| --- | --- |
| 构造 | `(IBattleReadModel model, GameObject root, TextMeshProUGUI text)`；`root`/`text` 为 null → `ArgumentNullException`（缺件由装配层判，不在驱动内猜） |
| `Tick()` | 每渲染帧；取 `TryGetMatchClock` → 按 §4 表切可见性/文本；**秒值未变不写**；`_disposed` 后空转 |
| `Dispose()` | 幂等；根件归零（隐藏 + 文本清空），不销毁 GameObject（根件归装配层） |
| 常量 | `TicksPerSecond = SimConfig.TickRate`（不写 60 字面量）；格式串一处定义 |

已知既有先例：`BattleRespawnHudDriver` 的整秒脏位写法（`_lastSeconds` 哨兵 + 变化才写）可直接对照，但**不得**复用其类或文本节点。

### 6.2 引用件与装配

| 位置 | 改动 |
| --- | --- |
| `BattleHudView` | 新增 `[SerializeField] RectTransform _matchTimerRoot; [SerializeField] TextMeshProUGUI _matchTimerText;`；暴露只读属性 + `bool HasMatchTimer`（两件齐备判据，与 `HasRankboard`/`HasKillFeed` 同款——**接线自检**，防制作期漏接） |
| `BattleHudAssembler.AttachHud` | 面板驱动建好后创建计时器驱动，复用同一 `_predictedModel`（同一读源、少一份缓冲）；`!HasMatchTimer` → `Log.Warning` 一次后跳过（HUD 通用降级口径：缺件不当作对局失败） |
| `BattleHudAssembler.Tick` | 顺序：`_hud` → `_timer` → 排行榜 → 击杀提示 → 网络 → 阵亡（同帧 UI 值，先本人面板后公共读数） |
| `BattleHudAssembler.DisposePanel` | 先 `_timer?.Dispose()` 再 `_hud?.Dispose()`（消费者逆序，与现有逐件 Dispose 风格一致） |

**不新增**：不新建装配器、不改 `BattleViewRig` 相位、不改 `ProcedureBattle`、不改收集组（`Assets/ShareResource/UI/Hud` 已在 `UIHud` 组内，件级热更自动覆盖）。

## 7. 读模型契约扩展

HUD 目录有守卫：`Tests/LiteFramework.Core.Tests/PresentationReadModelGuardTests.cs` 规定 `App/Presentation/Hud/**` 除 `BattleReadModel.cs` 外不得出现 `SimWorldState`。因此不得在驱动里直读 `Match`，必须扩端口：

```csharp
// Assets/LiteBattle/Abstractions/Battle/IBattleReadModel.cs
public readonly struct BattleMatchClockFacts
{
    public readonly int Phase;            // SimMatchPhase（0/1/2）
    public readonly int RemainingFrames;  // Timer（>0 才有意义）
    public BattleMatchClockFacts(int phase, int remainingFrames) { ... }
}

bool TryGetMatchClock(out BattleMatchClockFacts clock);   // false = 未对齐/无 Sim
```

| 面 | 影响 |
| --- | --- |
| 实现 | 仅 `Assets/LiteGame/App/Presentation/Hud/BattleReadModel.cs`（唯一翻译点）：`state == null → false`，否则读 `state.Match` |
| 其他实现者 | 无（全仓仅 `BattleReadModel` 实现该接口；EditMode 用例走真实 `BattleReadModel` + 委托注入） |
| 二进制/协议 | 无——`Assets/LiteBattle/**` 不在 BuildHash 源集（`scripts/codegen/gen-build-hash.py` 的 `SHARED_TARGETS`），也不被服务端链接 |
| 备选（未采纳） | 驱动直接吃 `Func<MatchStateData>`：绕开读模型收口，把 Sim 布局知识带回驱动，且与守卫基线冲突 |

## 8. 刷新纪律与预算

| 项 | 目标 | 手段 |
| --- | --- | --- |
| 文本写入 | ≤ 1 次/秒 | 整秒脏位（`_lastSeconds` 哨兵） |
| 每帧开销 | 一次结构体读取 + 一次整数比较 | 不做字符串拼接比较（先比秒值再写） |
| 分配 | 每秒 ≤ 1 个短字符串（格式化） | 与阵亡倒计时同口径；如需零分配可后续换 `SetCharArray` 复用缓冲，不列为 v1 阻断项 |
| Canvas 重建 | 0（稳定态） | 值不变不写 `text`；显隐走根 `SetActive`，不动层级顺序 |
| 显隐 | 只切 `Cot_MatchTimer` 根 | 不在每帧写 `CanvasGroup.alpha` |

## 9. 测试与验收

### 9.1 EditMode（新增 `BattleMatchTimerEditModeTests`）

| # | 用例 | 断言 |
| --- | --- | --- |
| 1 | 起表 | `Timer=10800, Phase=1` → 可见、文本 `03:00` |
| 2 | 整秒脏位 | `10799 → 10740`：前段不写、`10740` 写一次 `02:59`（写入次数用计数器/文本引用计数钉住） |
| 3 | 末段 | `Timer=60 → 00:01`；`Timer=1 → 00:01`；`Timer=0, Phase=2 → 00:00` |
| 4 | 不限时 | `Phase=1, Timer=0` → 隐藏（不显示 `00:00`） |
| 5 | 未开局 | `Phase=0` → 隐藏 |
| 6 | 未对齐 | `state=null` → 隐藏、不抛 |
| 7 | 结束冻结 | `Phase=2` 后继续 Tick：文本不变、根件仍显示 |
| 8 | 幂等 | 重复 `Dispose` 不抛、重复 `Tick` 空转 |

### 9.2 结构守卫（并入 `BattleHudMenuEditModeTests` 或新用例）

- `Hud_Battle/Cot_Panel/Cot_MatchTimer/Txt_MatchTimer` 在位，`TMP_Text` 存在。
- `BattleHudView` 两字段非空（`SerializedObject` 接线有效）。
- 全树 `Graphic.raycastTarget == true` 仍恰为 1（菜单钮）；计时器不引入新射线面。
- 计时器节点不挂 `BindNode` / `LTextKeyRef`。

### 9.3 回归与手测

| 面 | 判据 |
| --- | --- |
| L1 | `python scripts/gate/test.py --suite L1 --profile PullRequest` 全绿（读模型接口扩展不改变既有用例行为） |
| EditMode | 新增用例 + 既有 `BattleHud*`/`BattleHudMenu*` 全绿 |
| 手测（真机/Play，真实操作路径） | 打满一局：`03:00` 起表、逐秒递减、阵亡等待仍可见、到点冻结 `00:00` 且进结算；中途断线重连：秒数最多回跳 1 秒后继续 |

> 验证纪律按 AGENTS.md 红线 13：不得以直调驱动 API 代替"在真实对局里看过一眼"。

## 10. 施工顺序与回退

| 批 | 内容 | 出口判据 |
| --- | --- | --- |
| A 数据面 | `BattleMatchClockFacts` + `IBattleReadModel.TryGetMatchClock` + `BattleReadModel` 实现 | 编译零错误、L1 全绿 |
| B 驱动面 | `BattleMatchTimerDriver` + `BattleMatchTimerEditModeTests` | 用例全绿（先红后绿） |
| C 资产面 | `Hud_Battle.prefab` 加两节点 + `BattleHudView` 字段接线（**走 unity-pipeline CLI，禁直改 YAML**） | 结构守卫全绿 |
| D 装配面 | `BattleHudAssembler` 三处接线 | EditMode + 手测通过 |
| E 收尾 | 进度卷新章 + 审计登记（如引入 HUD 令牌/字号例外） | 门禁全绿、文档索引无孤儿 |

**回退与发布口径**：

- 客户端**未发布**，不承诺跨版本兼容：`Hud_Battle` 与代码同批出包（同一构建），不做"旧 prefab × 新代码"的配对与回归。
- 回退 = 同批回退资产与代码；或只删 `BattleHudAssembler` 的装配一行（驱动与读模型保留不产生副作用）。
- 开发期单独拉代码或单独拉资产时：缺件走 S5 一次性警告降级、多余节点无人消费且无射线——这是防御性行为，**不是兼容承诺**。

## 11. 风险与边界

| # | 风险 | 影响 | 处置 |
| --- | --- | --- | --- |
| R1 | 预测领先使显示比权威快 ≤6 帧 | 秒值最多提前 1 秒出现 | 快照覆盖自愈；秒级不可见，不额外补偿 |
| R2 | 丢包/抖动导致秒数回跳 | 视觉上"多跳一格" | 整秒脏位只多写一次；不做平滑（平滑会造假时间） |
| R3 | `Timer==0` 二义 | 不限时房间显示假 `00:00` | 用 `Phase` 判别（§4 S3/S4） |
| R4 | 客户端预测先到 0 触发本地 `Finish` | 极短窗口内本地已结束、服务器尚未结束 | 既有行为（`SimStep` 带 `advanceMatch`），本文不改；HUD 只冻结显示 |
| R5 | Team 模式启用后与 `Txt_Score` 同带冲突 | 两行叠在一起 | `Txt_Score` 当前恒隐藏；Team 模式落地时与本文 §5.1 版面一并重排（登记约束，不预先摊开） |
| R6 | 字号/配色未经美术定档 | 观感偏差 | 制作侧定档后只改 prefab，不动代码 |

## 12. 扩展（本轮不做）

| 扩展 | 触发条件 | 形态约束 |
| --- | --- | --- |
| 低时提示（≤10s 变色/脉冲） | 玩家反馈"看不到快没时间了" | **禁止代码写 RGB**：prefab 预置 normal/urgent 两个变体节点（各自挂 `UiStyleRef`），驱动切显隐；脉冲走 `UiFx` + `UiAnimationClock`（红线 9） |
| 准备/开局倒计时 | 出现"开局前 3-2-1"赛制需求 | 需 Sim 侧新增阶段或房间命令，先立协议与判定专项 |
| 加时赛 | 赛制需要平局加时 | `MatchStateData.Round` + `MatchRules` 扩展，HUD 复用同一驱动 |
| 团队模式比分同排 | `Team=1` 落地 | 与 `Txt_Score` 联合排版，作为一次 Level 设计 |
| 本地化 | 若出现文字型状态（如"时间到"） | HUD 当前固定英文/数字口径；出现文字即接入 key 体系，不在驱动里拼中文 |
