# 战斗 HUD 专项设计（按新规范重规划）

> **版本**：v1.0（2026-10-11）　**权威层级**：专项设计（第 3 层）——受《UI框架总设计》约束，落地《修改指南》§1 红线与 AGENTS.md 资源三层模型。
> **定位**：裁决战斗 HUD 的**资产形态、装配归属、解析契约与驱动纪律**，并给出迁移批次。判定/准心/相机的玩法语义权威仍在《固定斜视角射击方案专项设计》；UI 页面体系（导航/模态）归《UI框架总设计》。
> **触发**：客户端质量审计登记的 HUD 相关债（ProcedureBattle god class、六条场景路径硬编码靠注释契约、构建器常量与解析双写）＋ 资源三层模型落地后 HUD 资产无处安放的缺口。

> **落地状态（2026-10-11）**：批 A 已落地——`Hud_Battle.prefab`（层级见《战斗HUD层级设计》，Icon_Ads 64）+ `BattleHudView` 引用件 + collector 组 UIHud（PackSeparately）+ 场景对象退役 + Rig 接线（`AcquireHudAsync`/引用件取件，**Find 归零**）；批 B 装配拆分已由并行 BattleViewRig 批完成。下表现状为重规划前快照，留档对照。

## 1. 现状盘点（2026-10-11 实测）

| 面 | 现状 | 新规范下的差距 |
|---|---|---|
| 资产形态 | HUD 是**场景对象**（`训练场/Battle HUD`，Canvas Overlay），由 `AgentScripts/BuildBattleHud.cs`（344 行构建器）按常量生成进场景——**视觉单一来源的例外** | 模板资产不在任何收集目录/资源层：HUD 不可独立热更（随 Scenes 组整包）；构建器常量与场景实况靠人肉同步 |
| 解析契约 | `ProcedureBattle` 六条路径常量（`HudRootName/CrosshairPath/PanelPath/VitalsFillPath/…`）+ `GameObject.Find`/`transform.Find`（:594/:602/:624-640）——**与构建器常量是两份知识**，只靠注释互指（审计 [中] 条目） | 违反 §1.1"按名解析必须常量化＋失败可见"的收口方向：常量有了但**双写未机制化**，prefab 化后应整体归零 |
| 装配归属 | `ProcedureBattle`（749 行 god class）内联七个 `Attach*`＋`DetachView` 手工逆序清 15 字段＋OnUpdate 驱动 10 子件（审计 [高] 条目） | 违反 §1.6（>400 行多职责须拆）：HUD/准心/飘字/音效等**表现装配**应出装配件 |
| 驱动纪律 | `BattleHudDriver`（115 行，值变化才写✓）/`BattleCrosshairDriver`（94 行）/`BattleAudioDriver`（91 行，Sound 组 location 常量✓）等 11 件——**全部外部 Tick**（无自驱 Update ✓） | 基本合规；待对齐《UI框架总设计》U3"HUD 分级驱动/DirtySet"目标项（本规划不承诺时点） |
| 通路归属 | HUD 显隐由对局生命周期驱动，**不经 UINavigationController**——与 §1.7"产品 UI 开关唯一走单写者"存在解释空间 | 需明确裁决（见 §2.2），消除"HUD 该不该走导航"的歧义 |
| 诊断 | F11 身位可视化/准心诊断已归 Gm 四页件（`GmTestModePage`，2026-10-11 Gm 分层批） | ✓ 已合规 |

## 2. 目标架构

### 2.1 资产形态：模板 prefab 收编 L3（视觉单一来源闭环）

- HUD 从场景对象改为**模板 prefab**：`Assets/ShareResource/UI/Hud/Hud_Battle.prefab`——**层级/命名/暴露定稿见《[战斗HUD层级设计](战斗HUD层级设计.md)》**（节点树 1:1 继承现构建器产物视觉，按制作规范 §9 词缀化改名，`Hud_` 根前缀已登记命名词表）。
- **视觉单一来源闭环**：prefab 按制作规范**编辑器手工制作**（首次可由构建器一次性生成模板后转手作维护）；`BuildBattleHud` 完成迁移后**退役**（其常量即刻失去第二份知识地位）。观感调参＝改 prefab（与 Screens/Widgets 同规）。
- **收集**：新增收集路径 `Assets/ShareResource/UI/Hud`（UI 域 tag，`PackSeparately`）——HUD 件级热更（改 HUD 只重发 HUD 包，不再随 Scenes 整包）。
- 场景只留对局根（或由 Rig 纯运行时实例化到 UI 层），**场景不再承载 HUD 视觉**。

### 2.2 通路裁决：HUD 属对局表现域，不走页面导航

- §1.7"产品 UI 开关唯一走 `UINavigationController`"约束的是**页面体系**（有导航语义的 Screen/Dialog/Toast）。战斗 HUD 无导航语义（不进返回栈、不模态、不参与遮盖推导），**裁决：HUD 归对局表现域**——由对局装配（§2.3 BattleViewRig）经 `AssetLease` 实例化/归还，生命周期＝对局 Scope（进对局建、离场必还），显隐由驱动按本地对齐态控制（现状语义保留）。
- 该裁决同步写回修改指南 §1.7 备注，作为"单写者"纪律的边界条款，杜绝两套解释。

### 2.3 装配：BattleViewRig 出 ProcedureBattle（god class 减负）

- 新装配件 `BattleViewRig`（`App/Presentation/`）：持有 HUD/准心/飘字/激光/音效等**表现件的租约与驱动实例**，提供 `Build（租约+实例化+驱动构造）/ Tick / Dispose（逆序，取代 DetachView 手工清 15 字段）`。
- `ProcedureBattle` 只剩流程编排：`rig.Build()` 一次、`OnUpdate` 调 `rig.Tick()`、离场 `rig.Dispose()`——审计 [高] 条目（七个 Attach\*＋15 字段手工逆序）的 HUD 侧修复。
- 驱动件职责不变（Tick 外驱、值变化才写、静默降级口径保留——缺 prefab＝warn 一次降级，不判对局失败）。

### 2.4 解析契约：字符串路径归零，改序列化引用

- prefab 根挂轻量引用件 `BattleHudView`（MonoBehaviour，序列化字段持有 `CanvasGroup/RectTransform/Image/TMP_Text` 各件）——**构建期引用，运行时零 Find**；六条路径常量与 `FindChildGameObject` 退役。
- 驱动构造改收 `BattleHudView`（引用面单点）；`BattleCrosshairDriver` 同改（Hip/Ads 引用字段）。
- 与 `UIBindIndex` 绑定体系的关系：HUD 不进 BindNode 表驱动（它是固定结构、无动态列表），**序列化引用即契约**——**这是有意裁决，不是漏用框架**（绑定体系服务页面体系：`UIForm` 生命周期 + `BindName` 索引 + Lua 按名取；HUD 归表现域不走导航，故不适用；逐条对照见《[战斗HUD层级设计](战斗HUD层级设计.md)》§2.1.1）；若未来 HUD 出现数据驱动子件（技能 CD 环）再评估入 Bind 体系（U2 目标项"技能 CD"归 UI 框架节奏）。

### 2.5 纪律对齐清单（红线逐条）

| 红线 | 本设计落点 |
|---|---|
| §1.1 禁硬编码/按名解析收口 | 六条路径常量＋Find 归零；构建器退役＝双写根源消除 |
| §1.1 资源三层 | HUD 模板入 L3 `ShareResource/UI/Hud`（直接寻址→收集目录） |
| §1.4 日志/降级 | 缺 prefab warn 一次降级口径保留（表现增益不 fail 对局） |
| §1.6 god class | BattleViewRig 拆装配；ProcedureBattle 回归流程编排 |
| §1.7 UI 单写者 | §2.2 边界裁决：HUD 属表现域不走导航（写回指南） |
| 资源规范·收集 | 新收集路径＝PackSeparately 件级热更 |

### 2.6 HUD 体系装配：BattleHudRig 再内聚（设计；实施批按需点单）

HUD 体系四件套——模板 prefab（§2.1）＋序列化引用件（§2.4）＋驱动集（值变化才写/静默降级）＋装配件（本节）。装配拆分（§2.3）后 HUD 一族（准心/HUD 面板/排行榜/死亡复活）与对局其余表现件同栖 `BattleViewRig`；排行榜批/阵亡批/退场链持续向该件追加 HUD 内容。**设计裁决：HUD 一族再内聚为独立装配件 `BattleHudRig`**——照 P4 外提先例口径（一类一文件、公共 API 不变、行为零变化），`BattleViewRig` 对应位置只留单行转发。

- **边界判据**：HUD 体系 = `Hud_Battle.prefab` 的**全部消费者**——取件租约/实例化/`BattleHudView` 引用件 ＋ 准心/HUD 面板/排行榜/死亡复活四驱动 ＋ 菜单钮接线（装配期加、拆除期摘）。
- **不属 HUD**：3D 出生保护壳归 FX 域（§4）；SimView 与激光/飘字/枪口火光/命中特效/音效/移动动画留 `BattleViewRig`。
- **五面同形**：`LoadAsync`（仅 HUD 取件）/ `Attach` / `Tick` / `Dispose`；`BattleViewRig` 对应位置转发。
- **注入面**：`SimView` ＋ `IInputService` ＋ `HitFeedbackDispatcher`（死亡 HUD 帧事件注册）＋ `openBattleMenu` 回调——不反向持有 `BattleViewRig`/`BattleContext`。
- **顺序纪律**（行为零变化，逐项保持）：装配序（准心→面板→排行榜→死亡复活）；`Tick` 同序；拆除逆序（排行榜→面板→死亡复活→准心，且死亡复活先于 `HitFeedbackDispatcher` 摘）；菜单钮加/摘两时点。
- **测试零改动**：驱动级 EditMode 用例直接构造驱动、不引用 Rig——外提不碰既有用例。

## 3. 迁移批次（逐批点单执行）

| 批 | 内容 | 同步面 | 验收 |
|---|---|---|---|
| **A 资产** | 编辑器制作 `BattleHud.prefab`（1:1 还原现视觉）→ `ShareResource/UI/Hud/` → collector 新路径 → 场景 `Battle HUD` 对象退役 | collector、修改指南 §3.3/§3.6、代码地图 UI 行、`VisualConstructionScanner`（若扫 Screens\|Widgets 需扩 Hud） | 对局实机观感 1:1；HUD 独立 bundle 出现 |
| **B 装配** | `BattleHudView` 引用件＋`BattleViewRig` 拆装；ProcedureBattle 减负；路径常量/Find/构建器退役 | 审计登记表回写（god class 条目、路径硬编码条目→已修复）；修改指南 §3.3 对局准心行 | `GameObject.Find` 在 ProcedureBattle 归零；L1＋EditMode（BattleCrosshair/BattleHud 相关）＋实机手测（血条/弹药/换弹标签/准心双形态/音效） |
| **C（可选，按需点单）** | HudModel 聚合/DirtySet、技能 CD 环、击杀播报——对齐 UI 框架 U2/U3 目标项 | UI 框架总设计节奏 | 真机预算证据（U3 判据） |
| **D HUD 体系内聚（按需点单）** | HUD 一族外提 `BattleHudRig`（§2.6）；`BattleViewRig` 只留转发 | 修改指南 §3.3 对局准心行、代码地图、审计（如触发新登记） | 门禁绿＋对局手测表现无差异（HUD/准心/排行榜/复活） |

## 4. 边界（如实）

- 本设计**不**承诺 U3 性能目标（DirtySet/分级驱动/图集实测）的时点——那归《UI框架总设计》U2/U3 路线；
- HUD 文案本地化（HP/Ammo 数字为 Invariant 格式，无文案表需求）；技能栏当前为静态占位（无数据面），数据面落地时走批 C；
- 音效 location 常量（`BattleAudioDriver`，Sound 组）与飘字底件（`ShareResource/FX`，批四已落）不在本批范围；
- **3D 出生保护壳**（`fx_spawn_protection`，FX 收集组件级热更）**归 FX 域**——与阵亡批同批落地仅是施工批次事实，不构成 HUD 归属（口径见《[战斗HUD层级设计](战斗HUD层级设计.md)》§0）。

## 5. 未来规划（暂不做与升级触发）

| 项 | 暂不做的判据 | 升级触发 |
|---|---|---|
| 通用表现件宿主骨架（View+Driver 组织的泛化基类/接口） | 单一消费者——先泛化＝空集造机器；`BattleHudRig`（§2.6）即骨架未来的首个实例 | 第二 HUD 形态（观战/第二模式）或第二表现件域出现 |
| HudModel 数据聚合 / DirtySet 分级驱动 | 驱动各自直读预测态/镜像，量级未到；按 UI 框架 U2/U3 节奏（§3 批 C） | U3 真机预算证据要求 |
| HUD 代码目录化（`App/Presentation/Hud/`） | 件数未到阈值（外提后仅装配件＋4 驱动） | HUD 件数膨胀或第二形态落地 |
| 技能 CD 环入 Bind 体系 | 技能系统未做（技能栏静态占位） | 技能数据面落地时评估（§2.4 尾注） |
