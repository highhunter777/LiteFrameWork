# 测试计划：同步层拆分与判定参数化（LiteSync 去耦 + LiteSim 参数装载）

> 运行 sync-and-sim-params · 创建于 2026-10-30T19:40:29+08:00

## 测试策略

- **测试层级**：xUnit 契约测试（`Tests/LiteSim.Core.Tests`、`Tests/LiteFramework.Core.Tests`）。行为/回放/黄金样本测试随 T71~T82 施工补齐，不阻塞本阶段红测。
- **红测写法**：对**已存在类型**用反射与直接调用（`CombatValues`/`CombatConfig`/`MatchTestRules`）；对**尚不存在类型/文件**用文件存在性与文本断言（`LiteSync`、`AoiSettings`、`SimParamsGate`、`.bin`、夹具）。避免把“新类型未定义导致的编译错误”当红测。
- **先失败后通过**：本阶段用例在当前基线 `HEAD=8a8fe0bf` 上应当**断言失败**（缺字段/方法/文件、旧常量仍在、`Loaded` 不抛、表名未接入）。施工后同一批用例转绿；不得以删用例或弱断言换绿。
- **替代验证说明**：`LiteSync`、`CombatGeometry` 是新类型/新程序集，不能先写会编译失败的强类型测试。用文件存在性 + 反射占位，待 T43/T71 的最小 API 桩落地后再补强类型断言。

## 用例清单

| 用例 | 层级 | 关联任务/验收 | 输入或步骤 | 预期结果（当前红） |
|---|---|---|---|---|
| UT-01 `LiteSync_SharedDirsAndAsmdefExist` | xUnit / 文件 | T43 / B1 | 检查 `Assets/LiteSync/`、`LiteSync.asmdef`、`LiteSync.csproj` | 当前不存在 → 断言失败 |
| UT-02 `SharedTargets_ContainLiteSyncSharedNotClient` | xUnit / 文本 | T44 / B4 | 读 `scripts/codegen/gen-build-hash.py` 的 `SHARED_TARGETS` | 缺 `LiteSync/{Shared,Snapshot,Input,Events}`，含 `Client` → 断言失败 |
| UT-03 `ClientAsmdefAllowlist_ContainsLiteSync` | xUnit / 文本 | T44 / B5、B22 | 读 `ClientAsmdefAllowlistTests.cs` 的 `Allowed`/`ClientRoots` | 缺 LiteSync 条目与根 → 断言失败 |
| UT-04 `LiteNet_NoUsingLiteSim` | xUnit / 文本 | T44 / B2 | 扫 `Assets/LiteNet/**/*.cs` | 仍有 `using LiteSim` → 断言失败 |
| UT-05 `CombatValues_HasGeometryFieldAndNineParamCtor` | xUnit / 反射 | T71 / B9 | `typeof(CombatValues).GetField("Geometry")`、构造参数数 | 当前无 Geometry、8 参数 → 断言失败 |
| UT-06 `CombatConfig_Loaded_ThrowsBeforePublish` | xUnit / 直接调用 | T71 / B9 | 未 Publish 时读 `CombatConfig.Loaded` | 当前返回 Default、不抛 → 断言失败 |
| UT-07 `CombatConfig_GeometryPropertyExists` | xUnit / 反射 | T71 / B9、B10 | `typeof(CombatConfig).GetProperty("Geometry")` | 当前无此属性 → 断言失败 |
| UT-08 `CombatConfig_OldGeometryConstantsRemoved` | xUnit / 反射 | T73 / B10 | `GetField`/`GetMethod` 检查 10 个几何常量与 `MuzzleOrigin` | 当前仍存在 → 断言失败 |
| UT-09 `MatchTestRules_GeometryOverrideRemoved` | xUnit / 反射 | T73、T80 / B19 | 四几何覆写项应为 null；`DamageOverride`/`RpmOverride` 保留非 null | 当前四几何项仍在 → 断言失败 |
| UT-10 `TableLayout_ContainsCombatGeom` | xUnit / 文本 | T72 / B11、B13 | 读 `TableLayout.cs` 的 `Files` | 缺 `"Combat/tbcombatgeom"` → 断言失败 |
| UT-11 `SimConfigMapper_HasCombatGeomParameter` | xUnit / 文本 | T72 / B11 | 读 `SimConfigMapper.cs` 的 `BuildCombatValues` 签名 | 无 `tbcombatgeom` 参数 → 断言失败 |
| UT-12 `GeometryStaticReadGuard_NoOldNames` | xUnit / 文本 | T73 / B10 | 扫 `Assets/LiteSim/Scripts`、`Systems` 的 `CombatConfig.<几何名>` | 仍有静态读 → 断言失败 |
| UT-13 `OldBakeArtifactsRemoved` | xUnit / 文件 | T75~T78 / B12、B16 | 五件 `.g.cs` 不存在 | 当前部分存在 → 断言失败 |
| UT-14 `RootMotionAndGripBinariesExist` | xUnit / 文件 | T75~T78 / B12、B16-B18 | `rootmotion.bin`、`grip.bin`、`Tests/EditMode/Fixtures/BodyCc.json` 存在 | 当前不存在 → 断言失败 |
| UT-15 `WriteGeneratedRemoved` | xUnit / 文本 | T78 / B12 | 读 `BakeTargets.cs` | 仍有 `WriteGenerated` → 断言失败 |
| UT-16 `SimView_SnapshotIntervalIsInstance` | xUnit / 文本 | T79 / B15 | 读 `SimView.cs` 的 `SnapshotInterval` | 当前 `public static` → 断言失败 |
| UT-17 `AoiFilter_NoStaticReadonlyCache` | xUnit / 文本 | T79 / B15 | 读 `AoiFilter.cs` | 仍有 `static readonly` 派生缓存 → 断言失败 |
| UT-18 `AoiSettings_InLiteSyncShared` | xUnit / 文件 | T79 / B15 | `Assets/LiteSync/Shared/AoiSettings.cs` 存在 | 当前不存在 → 断言失败 |
| UT-19 `ProtocolConstants_ThrottleAoiRadiusRemoved` | xUnit / 文本 | T79 / B15 | 读 `ProtocolConstants.cs` | 仍有 `ThrottleAoiRadius` → 断言失败 |
| UT-20 `SimParamsGateAndOverridesExist` | xUnit / 文件 | T80 / B19-B21 | `SimParamsGate.cs`、`SimParamsOverrides.cs`、`SimParamsDebugTunables.cs`、`SimParamsExporter.cs` 存在 | 当前不存在 → 断言失败 |

## 预期失败证据

- 命令（已加入 `.workflow/config.json` 的 `red_commands`）：
  1. `dotnet test Tests/LiteSim.Core.Tests/LiteSim.Core.Tests.csproj --filter FullyQualifiedName~SyncAndSimGeometryContract --nologo`
  2. `dotnet test Tests/LiteFramework.Core.Tests/LiteFramework.Core.Tests.csproj --filter FullyQualifiedName~SyncAndSimContract --nologo`
- 观察到的失败：上述用例中，文件缺失、反射返回 null、旧标识符仍存在、文本未命中、`CombatConfig.Loaded` 不抛异常等**断言失败**；不是导入错误、拼写错误或环境故障。
- 为什么这是正确的失败：它们直接对应 B1~B24 的验收断言，当前基线尚未实现这些行为，因此应当红；施工完成后同批用例应转绿。
- 新类型/新程序集用文件存在性替代强类型断言，避免把“未定义类型导致的编译失败”误当红测；T43/T71 的最小 API 桩落地后，反射/行为断言接管。

## 运行方式

- 红测（本阶段）：上述两条 `dotnet test` 命令；`wf verify --red` 记录到 `evidence/red.json`。
- 全量回归：`python scripts/gate/test.py --suite L1 --profile PullRequest`（施工后）；`python scripts/gate/unity-tests.py --mode editor --filter <定向>`（施工后）。
- 门禁与联测：`python scripts/codegen/gen-build-hash.py --check-shared`、`python scripts/gate/l2-unity-gate.py`、`scripts/gate/sync-and-sim-e2e.py`（T81 后）。

## 文件范围（施工子代理）

- 仅允许新增/修改：
  - `Tests/LiteSim.Core.Tests/SyncAndSimGeometryContractTests.cs`
  - `Tests/LiteFramework.Core.Tests/SyncAndSimContractTests.cs`
- 禁止修改生产代码、`.workflow/**`、门禁脚本与配置；禁止删除既有用例或降低断言强度。
