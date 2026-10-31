# 方案：同步层拆分与判定参数化（LiteSync 去耦 + LiteSim 参数装载）

> 运行 sync-and-sim-params · 交付 B/3
> 前置：交付 A（`client-modularization`）已归档；C（`codegen-consolidation`）尚未开工。
> 证据母本：`.workflow/archive/20261031-153454-module-boundaries/plan.md` 修订 7~9。
> 2026-10-31 用户裁决：P-6 扩范围全迁（SimMapBaker/测试/WeaponGripBaker）、P-3 嵌套 `CombatGeometry`、P-9 B 先且独占 `BakeTargets`。
> 方案评审：`reviews/plan-review.md` 首轮 FAIL（dsh，10 个阻塞）；第二轮 FAIL（4 个）；第三轮传输中断（部分发现已并入）；第四轮 FAIL（4 个）；第五轮 FAIL（2 个）；第六轮 FAIL（1 个）；本版为第六轮整改。

## 背景与目标

- 现状（按 `HEAD=8a8fe0bf` 实测，已修正旧计数）：
  1. **依赖方向是反的**：`LiteNet.asmdef` 只引 `LiteSim`；`Assets/LiteNet` 内 11 个文件 `using LiteSim`（协议映射 + `RoomClient/RoomOutbound/InputWindow`）。`Assets/LiteNet` 共 65 个 `.cs`。
  2. `LiteNet` 混着两处非网络件：`Core/Diagnostics/DiagTrace.cs`（通用诊断环，被 `LiteGame.Adapters.Dev` 读）、`Core/Diagnostics/WeakNet/*`（开发期弱网注入，却随运行时程序集发布）。
  3. `LiteSim`（70 个 `.cs`）混着三类非判定内容：`SimConfig` 的广播/AOI 参数、`Scripts/Map/*.g.cs` 烘焙产物（`BodyBake`/`HeadBake`/`MuzzleBake`/`RootMotionData`）、`RootMotion` 绑定在 `AppComposition.cs:108-113`。
  4. 判定几何住在 `CombatConfig` 常量里：`HitscanRadius=0.45`、`BodyRadius/HitscanHeight`（来自 `BodyBake`）、`HeadHitLine/HeadshotRadius`（来自 `HeadBake`）、`MuzzleOrigin` 三常量、`HardMaxSpeed`、`FaceTurnRadPerSec`、`HeadshotDamageShift`、`HipSpreadFactor`、`CorpseFrames`。机制面在 `SimRaycast.cs:81/85`、`MatchTestRules.cs:73/77`、`ShootingSystem.cs:80/196`、`MovementSystem.cs:79` 等处静态读。
- 目标（可观察）：
  - `LiteNet` **零 `using LiteSim`**，只做传输；新增 `LiteSync`（同步协议层）承载映射与会话；`LiteSync/Client` 不进 BuildHash 源集。
  - `LiteSim` 不再有 AOI/广播参数、不再有 `RootMotion*`/烘焙 `.g.cs`；判定几何全部来自 `Combat/tbcombatgeom` 表。
  - 几何/判定数值仍经**唯一参数通道** `SimStep.Step(..., in CombatValues values, ...)`；`MatchTestRules` 仅承载规则位与非几何 dev 覆写（Damage/Rpm，现状契约）；机制面零静态读。
  - `CombatValues` 10 → 11 字段（新增嵌套 `CombatGeometry`，13 列），构造 8 → 9 参数；`SimStep` 与 9 个 `System.Run` 签名不变。
  - 烘焙产物：`BodyBake/HeadBake/MuzzleBake/RootMotionData/GripBake` 五件 `.g.cs` 全部退役；表值进 `#combatgeom`，根位移进 `rootmotion.bin`，持枪偏移进 `grip.bin`；`BakeTargets.WriteGenerated` 删除。
  - 调参/导出走既有 `IDebugTunableProvider` + `BakingToolbox`。

## 需求澄清与假设

- 既有裁决（保留）：
  1. "WeakNet 移 Dev 侧"；"能不引用 LiteSim 吗" → 新建 `LiteSync`。
  2. **"litesim里参数都要从外部加载，别跟我说什么裁决"** → 裁决常量全部进表。
  3. "RootMotionData.g.cs 改成数据产物"。
  4. "按之前的方案来"（范围 = 几何装载化 + AOI 可配 + RootMotion 迁出，不扩成全参数外部化）。
- 2026-10-31 新增裁决：
  1. **P-6 扩范围全迁**：`SimMapBaker`、相关 EditMode 测试、`WeaponGripBaker` 一并迁移；五件 `.g.cs` 全退役。
  2. **P-3 嵌套 `CombatGeometry`**：13 列装进一个嵌套值类型字段，仍是单一 `CombatValues` 通道。
  3. **P-9 B 先**：B 独占 `BakeTargets` 路径/输出语义与 `gen-build-hash.py` 的 `LiteSync` 源集增项；C 后续只做 toolbox/入口/指纹/`--check`，C 计划同步调整。
- 采用假设：
  - `CombatValues` 的 `Geometry` 是 `readonly struct`，`HeadHitLine = HitscanHeight × HeadRatio`、`HeadshotRadius = HeadRadius` 为实例派生属性；不新增 `SimGeometryValues` 顶层类型，不改 `SimStep` 签名。
  - `AoiSettings`（CellSize/Radius/GridExtentMeters/ThrottleRadius）按房间实例注入；`SnapshotHz` 单独作为广播设置，客户端从 `JoinAck.SnapshotHz` 读取，不从 `SimConfig` 静态读。
  - `rootmotion.bin` 与 `grip.bin` 放 `Assets/GameData/Config/runtime/`（现有 Config 收集组已覆盖）；二者不进 BuildHash，根位移摘要进 `ContentIdentity.RootMotionDigest`，持枪数据只进内容发布，不进判定摘要。
  - BuildHash 源集只增 `LiteSync/{Shared,Snapshot,Input,Events}`；`ProtocolConstants` 迁入 `LiteSync/Shared/`，避免客户端专属改动迫使服务端重建。

## 范围与非目标

- 范围内：
  1. **批 6 LiteSync 去耦**：新建 `Assets/LiteSync/`；15 件搬迁；`LiteNet` 删 `LiteSim`；`SHARED_TARGETS` 增共享子目录；两端引用与守卫同步。
  2. **批 7 LiteSim 参数化**：13 列 `#combatgeom`、嵌套 `CombatGeometry`、`CombatConfig` 发布闸门、全判定面静态读迁移；AOI/广播拆分；RootMotion `.bin`；调参/导出。
  3. **烘焙全迁**：`BodyBake/HeadBake/MuzzleBake/RootMotionData/GripBake` 退役；`BakeTargets` 死路径修正；`SimMapBaker`、`WeaponGripConstraint`、`WeaponAimConstraint`、`AimPitchPostureDriver`、`GripTuning`、`FeatureDebugTunables` 与相关测试迁移。
  4. 守卫根扩展：`LiteSync` 进 asmdef 白名单与静态可变扫描根，新增负例。
- 非目标：
  1. 不做"LiteSim 全参数外部化"（结构常量/位布局/算法 epsilon 不动）。
  2. 不改 protobuf 消息字段（只搬承载层）。
  3. 不把 Unity 烘焙器搬进 Python（属交付 C 的入口登记，不含实现搬迁）。
  4. 不动 `Modules/` 的域划分（属交付 A）。

## 现状与约束

- `LiteNet`（65 `.cs`）：`Core/{Protocol,Transport(+Security),Client,Diagnostics,Proto/Generated}` + `Adapters/`；`references:["LiteSim"]`、`noEngineReferences:true`、仅放行 `Google.Protobuf.dll`。
- `LiteSim`（70 `.cs`）：`Scripts/{Kernel,World,Pipeline,Rules,Config,Map}` + `Systems/`（9 个 System）+ `Diagnostics/` + `Editor/`；`references: []`。
- 装载链：客户端 `ConfigService.ValidateCandidate` → `ApplyCombatValues` → `SimConfigMapper.BuildCombatValues(tbcombatnum, tbmovementconfig, tbentityconfig)` → `CombatConfig.Publish`；服务端 `ServerTableLoader` → `ServerTableLoad(Digest)` → `HostAssembly.Inputs` → `RoomRuntime` → `FixedCombatConfig.Capture`；判定 `SimStep.Step(..., in CombatValues, WeaponTable, MatchTestRules)`。
- `BuildCombatValues` 现有调用点：`ConfigService.cs:191`、`CombatValuesEditModeTests.cs:64`、`ServerTableLoader.cs:70/98`；全部需加 `tbcombatgeom` 参数。
- `TableLayout.Files` 条目是"领域/表名"（如 `Combat/tbcombatnum`）；`DeriveJudgmentFiles` 用 `file.Substring(0, file.IndexOf('/'))` 取域；`JudgmentDomains` 已含 `"Combat"`。`ConfigService.TableDataFiles` 是 `TableLayout.Files` 的别名，`ServerTableLoader` 自动遍历 `Files`。
- `SnapshotHz` 现存依赖：`JoinAck` 字段、`SimView.SnapshotInterval`、`LocalServerHandshakeEditModeTests`；`AoiRadius` 现存依赖：`SnapshotSource`、`ProtocolConstants.ThrottleAoiRadius`、`SnapshotPipeline`。不能只删 `SimConfig` 常量。
- `CombatConfig` 现有 `_loaded = CombatValues.Default`、`Publish`、只读属性；无"未发布"状态。几何常量与 `MuzzleOrigin` 在本类。
- `RootMotionCatalog` 是 `static partial class`，`ContentDigest` 读 `RootMotionData.g.cs` 的 `Entries`；绑定点在 `AppComposition.cs:113`；`ContentIdentity` 已有 `RootMotionDigest` 字段且不引用 `LiteSim`。
- 烘焙消费面：`BodyBake` 被 `SimMapBaker.cs:460/496/498/505/512` 与 `BodyBakeEditModeTests`、`SimMapBakerEditModeTests` 使用；`WriteGenerated` 被 `BodyCylinderBaker`、`HeadHitLineTuner`、`MuzzleOffsetBaker`、`RootMotionBaker`、`WeaponGripBaker` 使用；`GripBake` 被 `WeaponGripConstraint`、`WeaponAimConstraint`、`AimPitchPostureDriver`、`GripTuning`、`FeatureDebugTunables` 使用。
- 外部几何读者：`CombatConfig` 几何名在 `Assets/LiteGame`/`Assets/LiteSim/Editor`/`Tests` 约 40 处（含注释/测试）；实际运行读者见「设计要点·外部消费面」。`new CombatValues(` 共 10 处（生产 3：`SimConfigMapper.cs:46`、`CombatValues.cs:74`、`ServerTableLoader.cs:204`；测试 7）。`ClientAsmdefAllowlistTests.Allowed` 需删 `LiteNet -> LiteSim` 并新增 `LiteSync.asmdef` 条目，`ClientRoots` 需加 `Assets/LiteSync`。
- 迁移面已核对：11 个 `using LiteSim` 文件全部在 15 件搬迁清单内（3 个 Client + 8 个 Protocol）；`ProtocolConstants` 在 LiteNet 内唯一使用者是 `SnapshotDiffer`（随迁），移动不会形成 `LiteNet → LiteSync` 边；`CombatConfig.Loaded` 的外部读者只有 `BattleContext.cs:305`（在 `Publish` 后构造世界）。
- `gen-build-hash.py` 的 `SHARED_TARGETS` 是目录级：`LiteSim/{Scripts,Systems}`、`LiteNet/Core/{Proto,Protocol,Transport/Security}`、`GameData/Generated`；`LiteNet/Core/Client` 不在闭包内。
- 约束：`Assets/` 移动走 `AssetDatabase.MoveAsset`；C# 9；不触其它共享源集；`run_shell_command` 不可用；客户端"一个包都没发过" ⇒ wire 变更无兼容负担，但两端须同批重建。

## 候选方案与取舍

| 方案 | 核心思路 | 优点 | 代价/风险 | 结论 |
|---|---|---|---|---|
| L-C | 新建 `LiteSync` 承载"协议↔判定"映射与会话；`LiteNet` 只剩传输 | 方向修正为零环；LiteNet 可复用 | 15 件搬迁、BuildHash 路径与值变更、两端重建 | **采用** |
| L-A | 接口化反转 | 更彻底 | 多一层接口 + 逐字段拷贝，无第二套内核时收益低 | 否决 |
| L-B | 中性 DTO 共享程序集 | 编译期去耦 | 形状双份 → 与"协议单源"裁决冲突 | 否决 |
| L-D | 仅白名单收窄 | 零成本 | 不真去耦 | 否决 |
| P-1 | 几何平铺进 `CombatValues`（10→23 字段/21 参数） | 最直白 | 构造与调用面膨胀 | 否决（用户选 P-3） |
| P-2 | 独立 `SimGeometryValues` 顶层类型 | 生命周期分离 | 需改 `SimStep` + 系统签名 | 否决 |
| P-3 | **嵌套 `CombatGeometry`（13 字段）作为 `CombatValues` 一个字段** | 单一通道、构造 9 参数、`SimStep` 不变 | 新增一个值类型 | **采用（用户裁决）** |
| R-1 | `RootMotion` → `rootmotion.bin` 数据产物 | 数值不再住代码；digest 对字节算 | 新增 loader、退出 BuildHash | **采用** |
| R-2 | 保留 `.g.cs` | 零改动 | 数值住代码 | 否决 |
| AOI-A | `AoiSettings` 与 `SnapshotHz` 分离 | 语义正确；客户端从握手取 Hz | 改 `SimView`/`SnapshotSource`/`SnapshotPipeline` | **采用** |
| AOI-B | 把 `SnapshotHz` 塞进 `AoiSettings` | 改动少 | 语义混淆；客户端仍静态读 | 否决 |
| B/C-顺序 | B 先、B 独占 `BakeTargets` | 与用户指令一致 | C 需调整计划 | **采用（用户裁决）** |

## 设计要点

### 批 6：LiteSync（同步协议层）

- 职责一句话：让两端看到同一个世界——状态/输入/事件编成线格式并还原，决定全量/差分/AOI/重连。**不跑判定、不碰表现、不做业务决定、不管字节怎么送到**。
- 搬迁清单（**文件级 15 件 + `.meta`**）：
  - `LiteSync/Shared/`：`ProtocolConstants`
  - `LiteSync/Snapshot/`：`SnapshotSource`、`SnapshotCodec`、`SnapshotDiffer`、`SnapshotReassembler`、`AoiFilter`
  - `LiteSync/Input/`：`InputPacker`
  - `LiteSync/Events/`：`CombatEventCodec`
  - `LiteSync/Client/`：`RoomClient`、`RoomOutbound`、`RoomInbound`、`RoomSession`、`ReconnectState`、`RoomClientDiagnostics`、`InputWindow`
- 留在 `LiteNet`：`PacketType`/`PacketCodec`/`PacketWriter`、`BuildHash.g.cs`（原位 + `namespace LiteNet`）、`Proto/`、`Transport/`（含 Security）、`DiagTrace`、`Adapters/`（kcp2k）。
- **不迁**：`NetworkQuality(+Probe)`（纯 RTT/丢包统计，无 sim 依赖）。
- asmdef：`LiteSync` = `references:["LiteNet","LiteSim"]`、`noEngineReferences:true`、`precompiledReferences:["Google.Protobuf.dll"]`、`overrideReferences:true`；csproj 双轨（`LangVersion 9.0`）。
- BuildHash：`SHARED_TARGETS` 增 `Assets/LiteSync/Shared`、`Snapshot`、`Input`、`Events`；**不加 `Assets/LiteSync/Client`**；`SHARED_OUT_FILE` 与 `namespace` 不变；哈希值必变 → 两端重建 + `--check-shared`。B 只改 `SHARED_TARGETS` 这几行；C 后续再改 toolbox/指纹。
- 引用者加 `LiteSync`：`LiteGame.App`、`LiteGame.Adapters.Dev`、`LiteGame.Editor`、`Tests/EditMode`、`Tests/UI/PlayMode`、`Tests/LiteNet.Tests`（或新建 `Tests/LiteSync.Tests`）、`Server/RoomServer`。
- 守卫：`ClientAsmdefAllowlistTests.ClientRoots` 加 `Assets/LiteSync`；`ArchitectureGuardTests` 新硬边（`LiteNet` 不得引 `LiteSim`/`LiteSync`；`LiteSync` 不得引 `LiteGame.*`/`UnityEngine`），并加"植入违规应红"用例。

### 批 7：LiteSim 参数装载

- **`CombatGeometry`（嵌套 readonly struct，13 字段）**：
  - `BodyRadius`、`HitscanHeight`、`HitscanRadius`、`HeadRatio`、`HeadRadius`、`MuzzleForward`、`MuzzleRight`、`MuzzleHeight`、`HeadshotDamageShift`、`HipSpreadFactor`、`CorpseFrames`、`HardMaxSpeed`、`FaceTurnRadPerSec`。
  - 实例派生：`HeadHitLine => HitscanHeight * HeadRatio`；`HeadshotRadius => HeadRadius`；`MuzzleOrigin(in SimVector3 pos, float yaw)` 用三偏移计算，不再读静态。
  - `CombatValues` 现有 10 字段 + `public CombatGeometry Geometry` = 11 字段；构造 8 → 9 参数（`in CombatGeometry`）；`NoDeath/InfiniteAmmo` 仍默认 false。
- **表 `#combatgeom`**：13 列（上列 snake_case）；源 `Luban/Data/#combatgeom.xlsx`；`__tables__.xlsx` 登记；`TableLayout.Files` 加 **`"Combat/tbcombatgeom"`**（领域/表名，自动进 `JudgmentFiles`；`JudgmentDomains` 已含 `"Combat"`，不改）；`ConfigService` 的 `ValidateCandidate`/`ApplyCombatValues` 与 `ServerTableLoader.Parse`/`Load` 全部加表参数。
- **全判定面迁移**：`SimRaycast.cs:81/85`、`MatchTestRules.cs:73/77`、`ShootingSystem.cs:80/196`、`MovementSystem.cs:79` 及所有 `CombatConfig.<几何名>` 改读 `values.Geometry.*`；**删除 `MatchTestRules` 的 `ResolveHeadHitLine`/`ResolveHeadshotRadius` 与 `HeadHitLineOverride`/`HeadshotRadiusOverride` 字段**（几何唯一来源 = `values.Geometry` + `SimParamsOverrides`），grep 守卫零命中；`SimStep` 与 9 个 `System.Run` 签名不变。
- **外部消费面（HUD/Dev/Editor/测试）**：删除 `CombatConfig` 的公开几何常量；非判定读者统一走 `CombatConfig.Geometry` 投影（未发布时取 `CombatValues.Default.Geometry`，避免 Awake/静态初始化崩）或装配期注入 `CombatGeometry`。清单：`ConfigService.cs:197`、`BattleCrosshairDriver.cs:165`、`AutoHeadshotRig.cs:85-86`、`BattleHeadshotDebugDrawer.cs:55-56`、`FeatureDebugTunables.cs:40/49/73/75/89`、`GmTestModePage.cs:332-335`、`HeadHitLineTuner.cs:51/174-175/221`、`BodyCylinderBaker.cs:18-19`、`MuzzleOffsetBaker.cs:71`，以及 `BattleCrosshairEditModeTests`/`BattleLaserEditModeTests`/`BattleSmokePlayModeTests`。常量删除后由编译器兜底；B10 守卫按**代码标识符**扫描 `Assets/**` 的旧几何名（排除注释/字符串），零命中（含负例）。
  - **烘焙器写入源**：`BodyCylinderBaker`/`HeadHitLineTuner`/`MuzzleOffsetBaker` 写入表的值只能来自 prefab/anchor/曲线源；禁止把 `CombatConfig.Geometry` 的未发布默认回退当写入源（显示/对比可读）。
  - `CombatConfig.Loaded` 的外部读者仅 `BattleContext.cs:305`，必须在 `Publish` 之后；B9 加“Loaded 读者时序”断言。
- **发布闸门**：`CombatConfig` 增加 `_published`（初值 false，`_loaded` 初始化保持不变）；`Publish` 置 true；**判定读口 `Loaded`** 未发布时抛 `InvalidOperationException`；新增 `HasPublished`/`TryGetLoaded` 供纯展示读者；`CombatConfig.Geometry` 是非判定投影（未发布返回 `CombatValues.Default.Geometry`，仅作展示兜底、非权威，标 `DisplayOnly`；UI 标注“未装载/展示值”）；不存在 `CombatConfig.Default`（默认值单源是 `CombatValues.Default`）。`ConfigService` 先 `ValidateCandidate`（**只读候选表行**，13 列有限/正/边界；不读 `CombatConfig.Geometry`）→ `BuildCombatValues`（本地测试房再经唯一合并点应用覆盖）→ `Publish`，禁止先发布后抛。`new CombatValues(...)` 的 10 处调用点（生产 3/测试 7）必须同步构造参数；测试用 `Publish` 辅助或 `CombatValues.Default` 夹具。**新增 `CombatConfig.ResetForTests()`（internal + `InternalsVisibleTo`）并配合 `[Collection]` 串行，保证“未发布抛/已发布读”用例顺序无关**。B9 断言：未发布 `Loaded` 抛、已发布可读、编辑器工具未发布不崩、`Default` 仅为展示兜底（不设“Default ≡ 实时表”守卫，避免与导出流冲突）。
- **守卫**：扫描 `Assets/**`（重点 `Assets/LiteSim/Scripts/**`、`Systems/**`、`Assets/LiteGame`、`Assets/LiteSim/Editor`），旧 `CombatConfig.<几何名>` 零命中（常量已删，白名单为空）；增加“植入静态读应红”负例。B10 路径不再写 `Modules/LiteSim/...`。
- **AOI/广播拆分**：
  - `AoiSettings`（LiteSync，落 `LiteSync/Shared/`）：`CellSize`、`Radius`、`GridExtentMeters`、`ThrottleRadius => Radius * 0.5f`；按房间实例注入；注入源=`deploy/roomserver.windows.json` 的房间配置 + GM 房参数（服务端读取并下发给 `SnapshotPipeline`/`AoiFilter`），客户端不读该配置；`AoiFilter` 去 `static readonly` 缓存，改实例计算。
  - `SnapshotHz` 单独：从 `SimConfig` 移出，服务端按房间配置；客户端 `SimView` 在建立会话时从 `JoinAck.SnapshotHz` 取，`SnapshotInterval` 改实例字段；`SnapshotSource`/`SnapshotPipeline` 接收实例设置。
  - `ProtocolConstants.ThrottleAoiRadius` 删除，节流档由 `AoiSettings.ThrottleRadius` 派生；`SnapshotSource` 注释同步。
- **RootMotion**：
  - 烘焙器产 `Assets/GameData/Config/runtime/rootmotion.bin`（magic/formatVersion/tickRate/entryCount/entries，字段序固定；`.bin` 头 `tickRate` 与 `RootMotionCatalog.TickRate` 共用 `SimConfig.TickRate` 单源，烘焙器写头前断言一致；SHA-256 对字节）；
  - `RootMotionCatalog.Load(ReadOnlySpan<byte>)` 住 `Modules/Battle/Arena/`，非 static、实例持有 `Entries` 与 `Digest`；删 `RootMotionData.g.cs`/`BakedContentDigest`；`AppComposition.BindIdentity` 用 `catalog.Digest`；`ContentIdentity` 不引用 `LiteSim`（现状已满足）。
  - 装载入口：`ConfigService`/`ServerTableLoader` 从 Config 目录读 `runtime/rootmotion.bin`；缺文件/损坏 → 启动失败（内容包必须含）；测试夹具提供最小 `.bin`。
  - `.bin` 不进 `SHARED_TARGETS`；`rootmotion.bin`/`grip.bin` 已在现有 `Assets/GameData/Config` 收集组内；内容发布摘要带 `grip.bin` 字节摘要（两端不一致可见）；登记"已烘焙、执行层未接线"。
- **烘焙全迁（P-6）**：
  - `BodyCylinderBaker` → 写 `#combatgeom` 的 `body_radius/hitscan_height`，同一次原子写更新入库 CC 夹具 `Tests/EditMode/Fixtures/BodyCc.json`（记录来源 prefab 路径、radius/height）；`HeadHitLineTuner` → `head_ratio/head_radius`；`MuzzleOffsetBaker` → `muzzle_fwd/right/height`；三者改为写表（共享表写入器复用 `SimParamsExporter` 的 xlsx 读写/备份/原子写，落 `Assets/LiteGame/Editor/Baking/` 并单测；`gen.bat` + `--check-shared`），删除对应 `.g.cs` 与 `BakeTargets` 输出常量。
  - `RootMotionBaker` → `rootmotion.bin`；`WeaponGripBaker` → `Assets/GameData/Config/runtime/grip.bin`；`WeaponGripCatalog.Load` 住 `Modules/Battle/Characters/`，`WeaponGripConstraint`/`WeaponAimConstraint`/`AimPitchPostureDriver`/`GripTuning`/`FeatureDebugTunables` 从 catalog 取值（由战斗装配注入），删除 `GripBake.g.cs`。
  - `SimMapBaker` 的出生点校验**直接读 `BakeTargets.PrefabPath` 的 CharacterController**（与 `BodyCylinderBaker` 同源）；`ValidateSpawns`/`SpawnOverlapsObstacle` 改为接收 `bodyRadius/bodyHeight` 参数，不由表回读。守卫用**入库的 CC 参数夹具**（`Tests/EditMode/Fixtures/BodyCc.json`，由 prefab 导出并版本化）断言“表 row ≡ 夹具（半径+高）”；烘焙前置由 `restore-check` 保证 prefab 存在（缺失=失败，不 skip）。同一工具动作先 `BodyCylinderBaker` 写表、再 `SimMapBaker`；`WriteGenerated` 在五件 `.g.cs` 全部退役后删除（零引用）。
  - `BakingToolbox` 四页 + grip 页改为"导出到表/数据"，显示 diff、备份、`gen.bat`（表）或直接写 `.bin`（数据）；`BakeTargets` 五条输出常量（Head/Muzzle/Body/RootMotion/Grip）逐条给出新去向，四条死路径修正；`CombatConfig` 类头/成员注释（`CombatConfig.cs:12/55/82/130/141/161/176/184/193/200`，含“必须与表值一致/刻意不进 digest”）与 `SimMapBaker.cs:460/505/508` 的 `BodyBake` cref 同步清理，按 `csc.rsp` 的 warnaserror 口径核对。
- **调参/导出**：`SimParamsDebugTunables`（`IDebugTunableProvider`，Geometry 13 项）、`SimParamsOverrides`（dev 三宏；**唯一门 = `SimParamsGate.Evaluate(...)` 的组合根一次求值结果**，客户端与本地服同值，由 `TestModeRuntime.Active` + `LocalServerTransport` 在 Dev 侧派生，不进入共享程序集；**唯一合并点 = 两侧 `BuildCombatValues` 之后、`Publish`/`FixedCombatConfig.Capture` 之前调用 `SimParamsOverrides.Apply(in values)`**；`ServerTableLoader.Load/Parse` 增可选 `Func<CombatValues, CombatValues>` 合并委托，默认 null，本地宿主注入 Dev 侧委托，联机 null，不引入 RoomServer→Dev 依赖；联机/云 sandbox 不读客户端覆盖，GM 几何只改表或禁用；**摘要口径见下条**）、`SimParamsExporter`（写 `#combatgeom.xlsx` + diff + 备份 + 自动 `gen.bat` + `--check-shared`）；旧 `FeatureDebugTunables` 的 HeadHitLine/HeadshotRadius 覆盖退役并入新 13 项；GM/编辑器两处导出同一路径。
- **摘要口径（单源）**：联机 `CombatConfigDigest` = 判定表字节（`Combat/tbcombatgeom` 等）+ `CombatValues` 入口规则位（`NoDeath`/`InfiniteAmmo`，沿用 `MatchTestRules` 现行契约；编码顺序固定）；**不含** `SimParamsOverrides`、中局 GM 开合、dev 覆写。本地测试房摘要按生效 `CombatValues`（含本地覆盖）算，仅本地使用。
- **`MatchTestRules` 边界**：保留规则位（NoDeath/InfiniteAmmo）与非几何 dev 覆写（`DamageOverride`/`RpmOverride`/`ResolveWeaponDamage`/`ResolveFireIntervalFrames`，现状不动：不进摘要、不可重放、联机禁用）；**删除**几何覆写字段/方法（`HeadHitLineOverride`/`HeadshotRadiusOverride`/`ResolveHeadHitLine`/`ResolveHeadshotRadius`）；不把 Damage/Rpm 并入 `SimParamsOverrides`。

### 子步

- T1 建 `LiteSync`（空壳，编译 + L1 绿）
- T2 搬 15 件 + 收窄/补齐双方 csproj 通配
- T3 引用者与测试改引用
- T4 `SHARED_TARGETS` 增 `LiteSync/{Shared,Snapshot,Input,Events}`，重生成，`--check-shared`
- T5 守卫：asmdef 白名单 + `LiteNet`/`LiteSync` 硬边 + 静态可变扫描根 + 负例
- T6 两端重建 + 联机冒烟（本地测试房 + 真 KCP 弱网）
- T7 `CombatGeometry` + `CombatValues` 11 字段 + `CombatConfig._published=false` 初值/发布闸门 + `CombatConfig.cs:12/22` 注释与初始化同批清理
- T8 全判定面静态读迁移（Scripts+Systems）+ 守卫负例
- T9 `#combatgeom` 表与四处接入（`__tables__`/`TableLayout.Files`/`ConfigService`/`ServerTableLoader`）+ `CombatConfigDigest`
- T9b 烘焙全迁（Body/Head/Muzzle/RootMotion/Grip；SimMapBaker/WeaponGrip 消费面；`WriteGenerated` 删除）
- T10 AOI/广播拆分（`AoiSettings` + `SnapshotHz` + `JoinAck`/`SimView`）
- T11 `rootmotion.bin` + loader + digest + `AppComposition` 绑定
- T12 调参面与导出（`SimParamsDebugTunables`/`SimParamsOverrides`/`SimParamsExporter`/GM 页）
- T13 守卫与门禁收口（含：在 T8 读者迁移完成后删除 `CombatConfig` 几何常量；`Loaded` 全读者重扫；`ResetForTests`/`[Collection]` 落地）

## 通用性与扩展性

- 根因：网络层反向依赖判定内核；判定数值住在代码里；表现/烘焙数据住在判定内核。
- 扩展点：新增同步字段只改 `LiteSim`（状态）+ `LiteSync`（编解码）+ `battle.proto`；新增判定数值只加表列（不改代码）；换传输只改 `LiteNet`；换同步策略只改 `LiteSync`；新增武器持枪数据只加 `grip.bin` 行/条目。
- 判据固化：**payload 怎么编码归 LiteSync，包怎么封与送归 LiteNet，判定怎么算归 LiteSim**；数值一律外部加载（含裁决常量）；只有结构容量/位布局留代码。

## 测试策略

- 先失败后通过（红测）：
  - `LiteSync` 程序集/asmdef 与 `LiteNet` 零 `using LiteSim` 守卫（先红）。
  - `CombatGeometry` 13 列装载、`CombatConfig` 未发布读口抛、坏表拒绝（先红）。
  - 全判定面静态读守卫（`Assets/LiteSim/Scripts` + `Systems`）负例（先红）。
  - `LiteSync` 进 `ClientAsmdefAllowlistTests.ClientRoots` + `StaticMutableAllowlistTests` 根（先红）。
- L1：`LiteSync.Tests`（快照编解码/输入窗口/重连/AOI）；`CombatValuesEditModeTests`（11 字段/9 参数/13 值）；`CombatGeometryTableTests`；`RootMotionDataTests`（`.bin` 装载/摘要）；`WeaponGripCatalogTests`；`SimMapBaker` 几何来源。
- EditMode：`BodyBakeEditModeTests`/`SimMapBakerEditModeTests` 迁移；`WeaponGripConstraintEditModeTests`；`LocalServerHandshakeEditModeTests`（`SnapshotHz` 从 `JoinAck`）；`ConfigurationSourceTests`/`ServerTableLoadTests`/`CombatValuesTableTests` 加 `tbcombatgeom`。
- PlayMode / Play 冒烟：进一局验证命中/爆头/枪口/持枪位置；几何用**表值逐字段精确相等 + 确定性 checksum/回放黄金样本**判定，不用"肉眼无偏差"。
- 联测：两端重建后本地测试房 + 真 KCP `lossy`；`BuildHash` 与 `ConfigHash` 一致。
- 文档门禁：`doc-lint.py` 与进度卷回写。

## 验收标准

**批 6（LiteSync 去耦）**
- [ ] B1：`Assets/LiteSync/` 存在，asmdef（`references:["LiteNet","LiteSim"]`、`noEngineReferences:true`、Protobuf 预编译）与双轨 csproj 齐备。
- [ ] B2：`LiteNet.asmdef` 与 `LiteNet.csproj` 均不引用 `LiteSim`；`Assets/LiteNet/**` 内 `using LiteSim` 零命中。
- [ ] B3：**15 件**文件按 `Shared/Snapshot/Input/Events/Client` 目录搬迁到位且 `.meta` 随迁（`git status` 成对、无 GUID 重建）；`LiteNet` 保留项与清单一致。
- [ ] B4：`SHARED_TARGETS` 含 `LiteSync/Shared`、`Snapshot`、`Input`、`Events`，不含 `LiteSync/Client`；`BuildHash.g.cs` 路径与 `namespace LiteNet` 未变；`--check-shared` 通过并记录新哈希。
- [ ] B5：`ClientAsmdefAllowlistTests.Allowed` 删除 `LiteNet -> LiteSim`、新增 `LiteSync.asmdef` 条目；`ClientRoots` 含 `Assets/LiteSync` 且全绿；`ArchitectureGuardTests` 两条新硬边生效，并有“植入违规应红”用例。
- [ ] B6：`WeakNet/*` 已移 `LiteGame.Adapters.Dev`；`LiteNet` 只留 `IClientTransport`/`IClientTransportErrorSource`（grep 断言）；`Tests/LiteNet.Tests` 的 WeakNet 用例改引用后仍绿。
- [ ] B7：`DiagTrace` 保留在 `LiteNet/Core/Diagnostics/` 并登记"承载通用诊断环 + `JoinKey` 单源"理由；Dev 侧只经 `DebugKernel` 读，由 `DebugKernelEditModeTests` 覆盖。
- [ ] B8：L1 全量绿；两端重建后联机冒烟通过、两端 `BuildHash` 一致。

**批 7（LiteSim 参数化）**
- [ ] B9：`CombatValues` 含 `Geometry` 字段（11 字段/9 参数）；`CombatGeometry` 13 列全部从 `Combat/tbcombatgeom` 装载；未 `Publish` 时 `CombatConfig.Loaded` 抛（有用例，`ResetForTests` + `[Collection]` 保证隔离）；`CombatConfig.Geometry` 未发布返回 `CombatValues.Default.Geometry`（展示兜底、非权威）且编辑器工具不崩；`CombatConfig.Loaded` 唯一外部读者 `BattleContext.cs:305` 在 `Publish` 后（时序断言）；`ValidateCandidate` 用候选表行校验坏表（不读 Default）有用例；10 处 `new CombatValues` 调用点全部更新；坏表在 `ValidateCandidate` 阶段拒启动。
- [ ] B10：`Assets/**`、`Server/**`、根 `Tests/**` 不再出现旧 `CombatConfig.<几何名>`（常量已删除；守卫按代码标识符扫描、排除注释/字符串，白名单为空，含植入负例）；`ResolveHeadHitLine|ResolveHeadshotRadius|HeadHitLineOverride|HeadshotRadiusOverride` 零命中（含负例）；HUD/Dev/Editor/测试消费者全部迁移到 `CombatConfig.Geometry` 或注入；判定/烘焙代码不读 `CombatConfig.Geometry`（有断言）；`SimStep` 与 9 个 `System.Run` 签名未变。
- [ ] B11：`#combatgeom` 接入为 `__tables__.xlsx` + `TableLayout.Files` 的 **`"Combat/tbcombatgeom"`**；`JudgmentFiles` 自动包含（不改 `JudgmentDomains`）；`ConfigService`/`ServerTableLoader`/测试的 `BuildCombatValues` 调用点全部更新；`--check-shared` 记录新哈希。
- [ ] B12：`BodyBake.g.cs`/`HeadBake.g.cs`/`MuzzleBake.g.cs`/`RootMotionData.g.cs`/`GripBake.g.cs` 均不存在；五个烘焙器改产表行或 `.bin`，且写入源只来自 prefab/anchor/曲线（禁止未发布默认回退）；`SimMapBaker` 直接读 `BakeTargets.PrefabPath` 的 CC 做 spawn 校验（`ValidateSpawns`/`SpawnOverlapsObstacle` 收 radius/height）并断言表 row ≡ 入库 CC 夹具（半径+高）；`restore-check` 保证 prefab 存在，缺失=失败不 skip；`BodyBakeEditModeTests`、`SimMapBakerEditModeTests`、`WeaponGripConstraintEditModeTests`、`GripTuning`/`FeatureDebugTunables` 迁移；`MatchTestRules` 类头/字段的爆头覆写说明同步删除；`rootmotion.bin`/`grip.bin` 输出摘要等于预期；`BakeTargets` 五条输出常量逐条去向明确；相关 XML 注释清理后编译 0 warning；`BakeTargets.WriteGenerated` 零引用并删除。
- [ ] B13：`Combat/tbcombatgeom` 进 `JudgmentFiles` ⇒ 表字节进 `CombatConfigDigest`；改表后 `GameplayDigestBase` 变化可观测；**联机摘要 = 判定表字节 + `CombatValues` 入口规则位（NoDeath/InfiniteAmmo），不含 `SimParamsOverrides`/中局开合；本地测试房摘要按生效值算**；两端不等时拒局（不要逐值拼摘要）。
- [ ] B14：13 个几何值与**表加载结果**逐字段精确相等（0 容差；比较对象是 `ServerTableLoader`/`ConfigService` 的装载产物，不是同源夹具自比）；固定输入下确定性 checksum/回放结果与改动前黄金样本一致（给出测试名）；黄金样本随表调参的更新流程写明。
- [ ] B15：`AoiSettings` 可运行期注入且服务端生效；三档（全开/30m/15m）通过 `deploy/roomserver.windows.json` 房间配置 + GM 房参数**真实注入**（非测试自造实例）；`AoiFilter` 无 `static readonly` 派生缓存；`SnapshotHz` 与 `AoiSettings` 分离，客户端从 `JoinAck.SnapshotHz` 取，`SimView` 无静态 `SimConfig.SnapshotHz` 读。
- [ ] B16：`LiteSim` 内不再出现 `RootMotion*`；`rootmotion.bin` 位于 `Assets/GameData/Config/runtime/`；`RootMotionCatalog.Load` 位于 `Modules/Battle/Arena/`；`RootMotionData.g.cs` 不存在。
- [ ] B17：`ContentIdentity` 不引用 `LiteSim`；`AppComposition.cs:108-113` 用 `RootMotionCatalog.Digest`（SHA-256 对 `.bin` 字节）；不新增"名称"字段（名称由 `ReleaseId` 提供）。
- [ ] B18：`rootmotion.bin`/`grip.bin` 不进 `SHARED_TARGETS`；`--check-shared` 通过；`LiteSim/Scripts` 下不再有 `RootMotionData.g.cs`；"已烘焙、执行层未接线"在文件头与设计文档登记。
- [ ] B19：`SimParamsDebugTunables` 接 `IDebugTunableProvider`，DevHUD/GM 出现全部判定参数滑杆（含几何 13 项）；`SimParamsOverrides` 仅在 dev 三宏内；**唯一门 `SimParamsGate.Evaluate(...)` 组合根每会话求值一次后以 bool 传递（有“只求值一次”用例）；唯一合并点 `BuildCombatValues` 后、`Publish`/`Capture` 前，调用点集合固定为 `ConfigService` + `ServerTableLoader` 两处；判定路径零静态读**；联机/云 sandbox 不读客户端覆盖（有断言），GM 几何只改表或禁用；**删除** `HeadHitLineOverride`/`HeadshotRadiusOverride`/`ResolveHeadHitLine`/`ResolveHeadshotRadius`；`DamageOverride`/`RpmOverride` 保持现状（非几何 dev 覆写，不进摘要/不可重放/联机禁用）。
- [ ] B20：滑杆改值 → 本地测试房下一局生效；对局中改提示"下一局生效"；四条断言：①13 项设哨兵值 + 门 false ⇒ 逐字段等于 loader 产物，摘要等于**同构建无覆盖摘要**（表字节 + 入口规则位，规则位取同值；编码顺序固定）；②本地权威 tick 级 checksum = 客户端预测 checksum，且两侧 `CombatValues` 实例逐字段相等；③联机下 `Geometry` 逐字段等于 `ServerTableLoader` 装载产物且摘要一致；④GM 页联机禁用几何滑杆，按红线 13 真实操作验证可点性。
- [ ] B21：编辑器与 GM 两处"导出到表"都写 `#combatgeom.xlsx`；导出后自动 `gen.bat` + `--check-shared` 并显示 diff 与"两端需重建"；导出前备份可还原；`grip.bin`/`rootmotion.bin` 导出显示目标路径与内容摘要。
- [ ] B22：`StaticMutableAllowlistTests` 扫描根含 `LiteNet`/`LiteSync`，无新增 `public static` 可变字段。
- [ ] B23：`.workflow/config.json` 新增 `sync-and-sim` 验证命令组并在 `wf verify` 中通过：L1 全量、`LiteSync`/`LiteSim` 定向、`gen-build-hash.py --check-shared`、`l2-unity-gate.py`、两端重建联测；证据列出实际命令与结果。
- [ ] B24：文档回写：`Docs/设计文档/网络/状态同步专项设计.md`（新增/更新“LiteSync 边界与协议面”章节）、`Docs/设计文档/客户端/动画/联机动画与根位移专项设计.md`（`rootmotion.bin` 形态与装载）、`Docs/设计文档/游戏玩法/烘焙工具箱专项设计.md`（烘焙器输出表/数据与归属）、`Docs/施工进度/输入与同步链线.md` + `战斗与武器线.md`（新章）；索引登记，`doc-lint` 全过。

## 风险与回滚

- 风险：
  1. 哈希值变更 → 旧构建互不兼容（本项目无存量包，但两端必须同批重建后联测）。
  2. `LiteSync.csproj` 与 `LiteNet.csproj` 通配重叠 → CS0433/重复定义（T2 逐目录核）。
  3. `RoomClient` 迁走后 `LiteGame`/`RoomServer` 的 using 与构造点全量排查（漏一处编译红）。
  4. 几何装载化把"编译期保证"换成"启动期校验"：表错会阻断启动（表守卫先行）；`CombatValues.Default` 仅作未发布展示兜底，不设与实时表一致守卫；权威值只来自 `Publish`/表；`ValidateCandidate` 校验候选表行（不读 Default）。
  5. `AoiFilter` 去静态缓存改热路径对象布局，需性能冒烟。
  6. 烘焙全迁扩大改动面：表写入器、`grip.bin`/`rootmotion.bin` 的收集与装载、`SimMapBaker`/视图消费面都要同步；任一漏项会在编译或进局时暴露。
  7. `RootMotion`/`grip` 迁出后若客户端内容不一致，只在 `ContentIdentity`（根位移）或内容发布（持枪）可见——发布说明写明。
  8. 与 C 的顺序：B 先；C 启动时必须按本方案调整 `BakeTargets`/`gen-build-hash` 归属，避免二次改同一行。
- 监控：每子步 Unity 重编译 0 error；每批定向 EditMode + L1 全量；批 6 必做两端重建联测；批 7 必做进局手感与表值精确比对。
- 回滚：按子步提交，失败 `git revert`；目录搬迁用 `AssetDatabase.MoveAsset` 反向搬回；表/`.bin` 回退需同批回退两端构建；`wf rewind` 记录原因。

## 评审整改记录

- 首轮 dsh 评审 10 个阻塞（`reviews/plan-review.md`）已在本版处理：
  - P-1 15 件清单；P-2 `Combat/tbcombatgeom` 接入；P-3 嵌套 `CombatGeometry`；P-4 AOI/SnapshotHz 拆分；P-5 全判定面扫描；P-6 烘焙全迁；P-7 RootMotion 路径/loader/绑定；P-8 精确值+checksum；P-9 B 先与归属；P-10 守卫根。
  - 更正评审误计数：System 为 9 个（评审称 8）；文件计数按 65/70 回写。
- 第二轮 dsh 评审 4 个阻塞的处理：
  - 外部消费面：删除 `CombatConfig` 公开几何常量；列出 HUD/Dev/Editor/测试 40 处读者迁移到 `CombatConfig.Geometry` 或注入；B10 守卫扩到 `Assets/**`。
  - 发布闸门：`Loaded` 严格抛；`CombatConfig.Geometry` 未发布返回 `CombatValues.Default.Geometry`；更正 `CombatConfig.Default` 表述；补 10 处 `new CombatValues` 调用点与 B9 三条断言。
  - SimMapBaker：出生点校验直接读 prefab CC（与 BodyCylinderBaker 同源），加“表 row ≡ CC”守卫，避免读自己输出。
  - SimParamsOverrides：唯一门/唯一合并点（见 §调参/导出）；判定路径零静态读；旧 head 覆盖退役。
- 第二轮非阻塞采纳：`Allowed` 表条目、B6/B7 具体断言、`AoiSettings` 注入源、`CombatValues.Default ≡ 表值`、RootMotion tickRate 单源、`grip.bin` 发布摘要、B24 章节标题、共享表写入器落点。
- 第三轮 dsh 在出结论前遇传输错误（未形成 PASS/FAIL）；其已产出的推理发现已并入本版：烘焙器禁止把未发布默认回退当写入源、SimMapBaker 明确 `BakeTargets.PrefabPath` + `ValidateSpawns` 参数、`CombatConfig.Loaded` 读者清点、B10 守卫排除注释/字符串、`ProtocolConstants` 迁移不成环、11 个 `using LiteSim` 文件全部在 15 件清单内。
- 第四轮 dsh 评审 4 个阻塞的处理：
  - prefab 不入库：守卫改用入库 CC 夹具 `Tests/EditMode/Fixtures/BodyCc.json`（半径+高）；`restore-check` 保证烘焙前置 prefab 存在，缺失=失败不 skip。
  - `Default` 双语义：明确 `Default` 只是未发布前的展示兜底（非权威），删除“Default ≡ 实时表”守卫，避免与 B21 导出流冲突。
  - 发布状态测试隔离：新增 `CombatConfig.ResetForTests()`（internal + `InternalsVisibleTo`）或 `[Collection]` 串行。
  - 覆盖值同源：`SimParamsOverrides` 仅本地测试房（单门 `SimParamsGate.IsLocalTestRoom`，单合并点 `BuildCombatValues` 后、`Publish`/`Capture` 前）；联机不读客户端覆盖，GM 几何只改表或禁用。
- 第四轮非阻塞采纳：B10 扫描扩到 `Server/**`+根 `Tests/**`；`SpawnOverlapsObstacle` 同时收 bodyHeight；`AoiSettings` 落 `LiteSync/Shared/`；T3 逐条裁决 9 个 LiteNet 消费 asmdef；注释 XML 清理与 warnaserror；五条输出常量逐条去向；B13 改为“表进 JudgmentFiles ⇒ 字节进摘要”。
- 第五轮 dsh 评审 2 个阻塞的处理：
  - 联机覆盖：定义生效判据 `TestModeRuntime.Current.Active` 且 `LocalServerTransport`（Room-Test 单进程）；联机/云 sandbox 不读客户端覆盖，GM 几何滑杆禁用或只走表；本地覆盖不进联机摘要；B19/B20 加具体断言。
  - `Default` 双语义：删除风险 #4“Default 必须与表值一致”，统一为“仅展示兜底、非权威”；权威值只来自 Publish/表；`ValidateCandidate` 只校验候选表行；`CombatConfig` 类头与成员注释纳入 T9b 清理。
- 第五轮非阻塞采纳：CC 夹具由 `BodyCylinderBaker` 同次原子写更新（记录来源 prefab）；`restore-check` 前置；T13 在 T8 读者迁移后删常量；`HasPublished`/`TryGetLoaded`；`Geometry` 标 `DisplayOnly` 并断言判定/烘焙不读；`ResetForTests` + `[Collection]`；`ValidateCandidate` 坏表用例；B14 比较对象改为表装载结果 vs 黄金样本。
- 第六轮 dsh 评审 1 个阻塞的处理：联机覆盖统一为**单门** `SimParamsGate.IsLocalTestRoom`（组合根一次求值、客户端与本地服同值，Dev 侧派生）+ **单合并点**（`BuildCombatValues` 后、`Publish`/`FixedCombatConfig.Capture` 前）；删除“GM 房参数经 `MatchTestRules` 下发且进摘要”的旧句，改为“GM 几何只改表或禁用”；B19/B20 补三条断言；`MatchTestRules` 现行“不进摘要/不可重放”契约不再被覆盖通道复用。
- 第六轮非阻塞采纳：`CombatConfig.cs:12/22` 注释与初始化同批 T7 清理；`_published=false` 初值写明；`Default.Geometry` 展示值 UI 标注；B9/B10 加“非权威表述唯一”grep。
- 第七轮 dsh 评审 2 个阻塞的处理：
  - 摘要口径：新增“摘要口径（单源）”条目；联机 = 判定表字节 + 入口规则位（NoDeath/InfiniteAmmo），不含 `SimParamsOverrides`/中局开合；本地测试房按生效值算；B13 引用同句。
  - 几何旁路：删除 `MatchTestRules` 的 `ResolveHeadHitLine`/`ResolveHeadshotRadius` 与 `HeadHitLineOverride`/`HeadshotRadiusOverride`；几何唯一来源 = `values.Geometry` + `SimParamsOverrides`；B10 加 grep 零命中守卫。
- 第七轮非阻塞采纳：`ServerTableLoader.Load/Parse` 加可选合并委托（本地宿主注入，联机 null，不引入 RoomServer→Dev）；`SimParamsGate.Evaluate` 每会话一次并加用例；B20 四条断言（哨兵/双 checksum/联机表值/GM 真实操作）；清理旧口径句。
- 第八轮 dsh 评审 2 个阻塞的处理：
  - `MatchTestRules` 边界：保留 `DamageOverride`/`RpmOverride`/`ResolveWeaponDamage`/`ResolveFireIntervalFrames`（非几何 dev 覆写，现状契约：不进摘要/不可重放/联机禁用）；只删除几何覆写字段/方法与两个 `ResolveHead*`；不并入 `SimParamsOverrides`。
  - B20① 摘要表述：改为“同构建无覆盖摘要（表字节 + 入口规则位，规则位取同值；编码顺序固定）”，与摘要单源一致。
- 第八轮非阻塞采纳：`MatchTestRules` 类头/字段说明清理；B10 四项 grep 明确排除注释/字符串并删 XML cref；摘要规则位编码顺序固定。
