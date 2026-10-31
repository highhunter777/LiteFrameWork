# 代码评审（主代理）：同步层拆分与判定参数化（LiteSync 去耦 + LiteSim 参数装载）

> 运行 sync-and-sim-params · 创建于 2026-10-30T19:40:29+08:00
>
> 代码由施工子代理撰写；本文件由主代理基于实际 diff 与验证证据评审落盘。

## 评审输入

- 代码基线：提交 `8a8fe0bf` + 工作区未提交改动（B 批 T43–T84）；`git diff --stat` 摘要为 288 个跟踪文件、+2164/−4405，另有未跟踪新件（`Assets/LiteSync/` 整目录、SimParams/几何/`.bin` 装载件、服务端几何测试、e2e 脚本、文档与工作流产物）。工作区同时含 tps/android 并行线的未提交文件，**不在本次 B 批评审范围**（提交时需按线拆分）。
- 评审角色：主代理（代码作者为施工子代理）
- 评审范围：LiteSync 拆分与 BuildHash 源集、`CombatGeometry`/`#combatgeom` 装载、SimParams 门/覆写/导出、AOI/SnapshotHz 房级参数、RootMotion/Grip `.bin`、服务端装载链、两端 e2e 脚本、T82 文档回写与新增测试。
- 评审依据：`git diff` 关键件逐件阅读、未跟踪新件全文阅读、`evidence/verify.json`（阶段 8 全量 verify，本次运行）。

## 变更范围

- 对照任务与方案：T43–T84 的交付面与 B1–B24 验收标准逐项对应；文件搬迁/删除面与 `LiteSync/{Shared,Snapshot,Input,Events,Client}` 清单一致，BuildHash `SHARED_TARGETS` 只增共享四目录、不含 `Client`。
- 变更文件/模块：
  - 程序集与协议：`Assets/LiteSync/**`、`Assets/LiteNet/**`（同步件迁出、asmdef/csproj 去 LiteSim）、`BuildHash.g.cs`。
  - 判定与配置：`CombatGeometry`、`CombatValues`、`CombatConfig` 发布闸门、`ConfigService`/`SimConfigMapper`、`#combatgeom` 表源与表写入器、`SimParamsOverrides/Gate/Activation/DebugTunables/Exporter`。
  - 广播与服务端：`AoiSettings`、`AoiFilter`、`SnapshotDiffer/SnapshotPipeline`、`RoomServerConfig/RoomConfig/RoomRuntime`、`HandshakePacker`、`ServerTableLoader`。
  - 客户端表现：`SimView` 快照频率、`BattleContext` 接线、`RootMotionCatalog`/`WeaponGripCatalog`/`BinTextAssetImporter`。
  - 测试/门禁/文档：`SyncAndSimContractTests`、`SimParamsOverridesTests`、`RootMotionDataTests`、Aoi/ServerTableLoad 测试、`sync-and-sim-e2e.py`、T82 三份设计与两份进度卷。

## 正确性与边界

- 主路径：表值单源（`Combat/tbcombatgeom` 13 列）→ 客户端 `ConfigService` / 服务端 `ServerTableLoader` 装载 → `CombatValues` 实例 → 房间/机制消费；客户端发布面与本地服房间数值在进房时点经 `SimParamsActivation` 同源解析，真联机不读覆写。双端 BuildHash 与运行期 `ConfigHash` 对账链路保持。
- 边界与错误路径：`CombatConfig.Loaded` 未发布抛、`Geometry` 展示兜底；AOI/SnapshotHz 逐字段范围校验，坏配置拒启动；`.bin` magic/版本/截断/尾随字节/NaN 全部 `InvalidDataException`；`SimParamsOverrides` 非法列/非有限/越界当场抛；BuildHash 与 e2e 步骤均 fail-fast。
- 回归风险：工作区含并行线改动；提交必须拆线。已修 C-1（正式服几何丢失）后，服务端权威判定与客户端表值同源；仍需全量 verify 复测确认。

## 测试充分性

- 行为面：`LiteNet.Tests` 682、`LiteSim.Core.Tests` 412、契约 `SyncAndSimContractTests` 11/11；新增 `SimParamsOverridesTests` 覆盖 13 列单列覆写/校验/门真值表/激活解析；`AoiFilterTests` 新增实例隔离用例；`RootMotionDataTests` 覆盖装载/坏输入/编码往返/摘要；`ServerTableLoadTests` 新增“改表字节的体半径必须传递到权威实例”用例。
- 结构面：`SyncAndSimContractTests` 的 UT01–UT20 是源码/文件结构守卫，非行为断言；行为面由上述 L1/EditMode 套件承担，二者合围可接受。
- 是否存在迎合实现的测试：未发现删断言或只测实现细节；C-1 修复后新增用例直接改表字节验证真实传递路径，不是复述默认值。

## 通用性与扩展性

- 根因与复用边界：LiteNet 不再向下依赖 LiteSim/LiteSync，同步协议层独立；判定几何不再散落烘焙 `.g.cs`，统一由表与实例通道承载；AOI 参数按房间实例化，去掉进程级静态缓存。
- 扩展点与接入方式：新增几何列走 `#combatgeom` + `CombatGeometry` + `TableLayout.JudgmentFiles`；新增生成器/同步件走既有 asmdef/csproj/BuildHash 源集；新增房间参数走 `RoomConfig` + 配置解析。
- 硬编码、特判、魔法值：已把硬上限/枪口/爆头等常量迁表；跨语言 13 列名单新增守卫（见 C-5）。容器/本地房间配置未补 AOI 字段，走缺省值（登记为余项，非阻塞）。
- 重复逻辑与过度抽象：`ServerCombatValues` 仍是一层 DTO，但已补齐几何透传；未新增第二套表映射。行数门禁删除后保留的抽件（`SimMapSpawnGate`/`SlotWeaponConstraints`/`WireSimView`）为行为等价结构调整，测试覆盖。
- 相关场景复用与回归影响：客户端/服务端共用 `SimConfigMapper`、`TableLayout`、`CombatValues`；`ConfigService` 与服务端装载同摘要口径。
- 命名、结构与复杂度：C-2/C-3/C-4 已清；未见新增 public static 可变字段（`StaticMutableAllowlistTests` 绿）。

## 安全与性能

- 安全：e2e 临时配置剥离 lobby/settlement，不打印密钥；`--allow-insecure-local` 仅本机；部署配置无密钥；AOI/SnapshotHz 输入有范围校验，坏配置拒启动；`.bin` 装载对长度/越界/尾随字节 fail-fast。
- 性能与资源：AOI 网格按房间实例持有并按帧缓存；快照 stride 有最小 1 钳制；RootMotion/Grip 装载一次、无静态缓存；e2e 仅测试面。

## 发现的问题与处理

| ID | 严重度 | 位置 | 问题 | 处理 | 状态 |
|---|---|---|---|---|---|
| C-1 | blocker | `Server/RoomServer/ServerTableLoader.cs:214` | 正式服 `ServerCombatValues.ToValues()` 用 `CombatGeometry.Default` 重建实例，表几何 13 列未进入权威判定 → 表改值后服务端与客户端静默分叉 | `ServerCombatValues` 增 `Geometry` 字段；装载映射透传 `row.Geometry`；`ToValues()` 使用该字段；新增改表字节体半径的服务端回归用例 | 已修复，定向 8/8；全量 verify 复测中 |
| C-2 | minor | `Assets/LiteGame/Adapters/Dev/Debug/Tunables/SimParamsDebugTunables.cs:13` | 注释仍写“配置装载点合并”，与 T84 每局进房求值不符 | 改为“每局进房时点求值一次” | 已修复 |
| C-3 | minor | `Assets/LiteGame/Editor/Baking/CombatGeomTableWriter.cs:16` | 助手路径少下划线（`combatgeom_row.py`） | 改为 `_combatgeom_row.py` | 已修复 |
| C-4 | minor | `Assets/LiteGame/Modules/Battle/BattleContext.cs:220`、`Assets/LiteSync/Client/RoomClient.cs:318` | 行数门禁时代的语句压缩残留（多条语句挤一行） | 拆行，零行为变化 | 已修复 |
| C-5 | major | `scripts/codegen/_combatgeom_row.py` vs `SimParamsOverrides.cs` | 13 列名单跨语言仅靠约定，漂移无门禁 | 新增 Python 用例从 C# 常量提取列序并逐序比对 | 已修复，表助手 14/14 |
| C-6 | minor | `Assets/LiteGame/Adapters/Luban/ConfigService.cs:53-69,220-229`、`Server/RoomServer/ServerTableLoader.cs:41-49,58,102-110` | T84 后覆写合并唯一点已下移到 `ProductTransportFactory`，两处“装载期合并委托”当前无调用方；属保留接缝而非活路径 | 登记未修（若确认无后续服务端调参需求则删除；本次不扩大范围） | 登记（非阻塞） |
| C-7 | major | `Assets/LiteGame/App/GameEntry.cs:198`、`Assets/LiteFramework/Runtime/Modules/UI/Anim/UiAnimationClock.cs`、`FadeSlideTransition.cs` | 宿主关停后 UI 时钟静态残留指向已销毁实例；裸建 UIService 的 PlayMode 夹具走 Manual 轨却无驱动器 → 全量 PlayMode 21 个 UI 转场 2s 超时 | `OnDestroy` 清绑时钟并复位 boot 标记；`IsBound` + 未绑定回落 `UpdateType.Normal`；故意错误日志用例加 `LogAssert.ignoreFailingMessages` | 已修复，全量 UI PlayMode 56/56 |
| C-8 | minor | `scripts/gate/sync-and-sim-e2e.py` | 自带 root 推导触发单源守卫红 | 改走 `scripts/_common.repo_root()` | 已修复，单源用例 5/5 |

## 复测记录

- 评审轮次: 第 1 轮（首轮发现 C-1 blocker 与 C-2~C-5；均已修复，进入全量复测）
- 修复后执行的命令与结果：
  - `dotnet test Tests/LiteNet.Tests/LiteNet.Tests.csproj --filter FullyQualifiedName~ServerTableLoadTests --nologo` → 8/8 通过
  - `python -m unittest scripts.tests.test_combatgeom_row -v` → 14/14 通过
  - `python -m unittest scripts.tests.test_single_source_helpers -v` → 5/5 通过
  - `python scripts/gate/domain-matrix.py --check` → OK
  - `python scripts/gate/l2-unity-gate.py`（Unity 后台）→ **PASS**：EditMode 1124/1125（1 例后台忽略）、PlayMode **56/56**
  - `python .agents/skills/staged-delivery/scripts/workflow.py verify --run sync-and-sim-params` → **PASS 64/64**（L1 **1969**、LiteNet **683/683**、LiteSim 412/412、契约 11/11、e2e `20261101-033055` 两端 `be285f5c37e147d9` 全链 OK）；`doc-lint` 0/0、`domain-matrix` OK
- 复评执行与复评范围（主代理）：C-1 复读服务端装载链与新增用例；C-5 复读跨语言守卫；C-7 复读 UI 时钟生命周期 + 全量 L2；C-2~C-4/C-8 为注释/格式/根路径改动，零行为变化。
- 验证证据：`evidence/verify.json`

## 评审结论

- 评审方式: 主代理
- 结论: PASS
- 阻塞问题: 0
- 通用性: PASS
- 扩展性: PASS
- 评审人: 主代理
- 评审时间: 2026-11-01T00:50:00+08:00
