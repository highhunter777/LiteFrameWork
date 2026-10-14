# 类名归位：CombatNumbers → ServerTableLoader

> 范围：《代码质量审计-服务端》§五[低] 登记条目（2026-10-13 批C-③ 接手顺带登记，改名候选待裁决）——服务端玩法数值表装载器类名与现职能不符。批次类型：重构，行为零变更。
> 状态：已完成（附验证证据，2026-10-13）

## 归属与裁决

`Server/RoomServer/CombatNumbers.cs`（静态类）自"只管 combatnum 一张表"的年代留名，现为**全表装载器**（数值+移动+实体+武器行，产出 `ServerTableLoad` → `HostAssembly.Inputs` 显式传递），与伤害数字族（`DamageNumber*`）及实例名 `CombatValues` 邻近易混。

裁决取候选① **`ServerTableLoader`**——与产物类型 `ServerTableLoad` 配对（装载器 → 装载产物）；候选② `ServerTables` 与 Luban 运行时的 `cfg.Tables` 概念撞车，不取。

## 改动明细（脚本实测计数）

- 文件随类重命名：`Server/RoomServer/CombatNumbers.cs` → `ServerTableLoader.cs`（`git mv` 保历史；未挂任何 csproj 文件清单——SDK 通配自动收，`Tests.slnx` 只按项目引用）。
- 独立标识符替换 **13 文件 32 处**（`\bCombatNumbers\b` 词界匹配 + 字节级写回保行尾/编码）：
  - 服务端 3 文件 8 处：`ServerTableLoader.cs`（类声明）、`Program.cs`（2 调用 + 1 注释）、`RoomServerConfig.cs`（2 cref + 2 注释/错误文案）；
  - 客户端/共享注释 4 文件 5 处：`TableLayout.cs`、`ConfigService.cs`（客户端 cref 与注释）、`LiteSim/Core/Scripts/CombatConfig.cs`、`CombatValues.cs`（共享源集注释）；
  - 测试 6 文件 19 处：LiteNet.Tests 五件（调用点/cref/注释）+ MetaServer.Integration.Tests `CloudLoopbackL3Tests`（2 调用）。
- 活文档 5 份 6 处：`Docs/修改指南.md`（查询表玩法数值行）、`Docs/开发导航/代码地图.md`（RoomServer 行）、`资源收集目录专项设计.md`、`地图数据专项设计.md`（×2）、`玩法数值与Luban配置专项设计.md`。

~~**保留项（有意，非漏网）**：测试类名 `CombatNumbersTests`/`CombatNumbersEditModeTests`（"玩法数值"域名词表，不绑生产类名）；客户端 `ConfigService.ApplyCombatNumbers`/`ApplyMovementNumbers`（指表值装载动作本身，名仍准确）。~~
**（2026-10-14 命名清尾批口径反案）**：保留项**已清**——用户裁决 "Numbers 命名只留在 DamageNumber 族"（见下节）；原"域名词表/装载动作名"理由不敌"生产类名之外也不另立 Numbers 词表"的收敛口径。

**历史记录不改写**：施工进度各批次记录与 `归档记录/2026-10-10/` 中的 `CombatNumbers` 引用为当时证据，循《命名空间对齐》先例不动。

## 红线联动（BuildHash）

`Assets/LiteSim/Core/Scripts/CombatConfig.cs`、`CombatValues.cs` 的注释引用属共享源集（注释参与哈希）→ 重跑 `scripts/codegen/gen-build-hash.py`：shared `ccc7cdb85bc51cba` → **`ab821e1b49cd6a13`**（76 文件）；meta 未动（`6e1192067dc37716`，42 文件）；`--check` 双 OK。**两端产物须重建**以烤入新常量（开发期不校验，守卫在出包门禁 `--check-shared`）。

## 验证（2026-10-13 实测）

| 门禁 | 命令 | 结果 |
|---|---|---|
| L1（dotnet 车道） | `scripts/gate/test.ps1 -Lane L1 -Profile PullRequest` | **1636/1636 全绿**（含 LiteNet.Tests 432、LiteSim.Core.Tests 343、MetaServer.Tests 139、LiteTesting.Core.Tests 7 等，纪律扫描/表守卫/BuildHash 守卫在内） |
| Unity 重编译 | `unity command recompile` → `recompile_status` | completed / `failed:false` / `errors:[]`——**0 错误** |
| BuildHash 复算 | `python scripts/codegen/gen-build-hash.py --check` | 双常量 OK |
| 残留扫描 | `git grep -P '\bCombatNumbers\b' -- '*.cs'` | 零命中（保留项均为词界内子串，不属独立标识符） |

## 说明

- `[Diag]` 哨位：本批文件 0 命中（grep 核）；无接线面变更（纯改名，不涉容器注册/消费者）。
- L2（EditMode/PlayMode）按既定纪律留夜间流水线；Unity 侧本批仅注释与 BuildHash 常量变更，重编译 0 错。

# 续批：Numbers 命名清尾（2026-10-14）

> 触发：用户裁决 "Numbers 命名只留在 DamageNumber 族"——批4 新增的 `MapNumbers` 违反上批"不绑生产类名"口径，连带反案上批保留项，全仓清扫。批次类型：重构，行为零变更。

**改名映射（七件）**：

| 旧名 | 新名 | 面 |
|---|---|---|
| `MapNumbers`（批4 新增，违规） | **`ServerMapLoader`** | 服务端地图装载器（与 `ServerTableLoader` 配对；文件随类 `git mv`） |
| `CombatNumValues` | **`ServerCombatValues`** | 服务端装载载体结构（宿主/测试消费） |
| `ApplyCombatNumbers` / `ApplyMovementNumbers` | **`ApplyCombatValues` / `ApplyMovementValues`** | 客户端 ConfigService 表值装载动作（上批保留项） |
| `CombatNumbersTests` | **`ServerTableLoadTests`** | L1 装载链守卫（对齐生产对象 `ServerTableLoad`） |
| `CombatNumTableTests` | **`CombatValuesTableTests`** | tb_combatnum 表↔兜底↔装载链三面守卫 |
| `CombatNumbersEditModeTests` | **`CombatValuesEditModeTests`** | 客户端数值链路 EditMode 守卫（Assets 内文件经 unity-pipeline `rename_asset`，GUID `a31bf931…` 保持） |

**实施**：`git mv`×3（Server/Tests）+ unity-pipeline `rename_asset`×1（Assets）；标识符替换 19 文件（含注释 cref 六处：`WeaponTableTests`/`ConfigSnapshotEditModeTests`/`LTextWiringEditModeTests`/`RoomServerConfigTests`/`ValuesParameterizationTests`/`ServerTableLoader` 头注）；**全仓 cs 残留扫描 0 命中**（DamageNumber 族除外）。共享源集未触（`gen-build-hash --check` 复算确认）；行为零变更。

**文档回写**：本文件保留项反案（划除+后注）；《地图数据专项设计》名称行 3 处＋修订注③⑯；《修改指南》§3.1；《视野与遮挡》几何行；《代码质量审计-服务端》修复登记行追加反案；《施工进度/README》索引两行。

**后续**：Names 词表收敛口径以本批为准——新增生产类名不得再用 "Numbers"（`DamageNumbersPro`/伤害数字族为领域词例外）。
