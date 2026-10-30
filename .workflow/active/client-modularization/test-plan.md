# 测试计划：客户端模块化（竖向切分 + 装配就地 + 边界机械化）

> 运行 client-modularization · 创建于 2026-10-30

## 测试策略

分三层，逐批推进（每批先红后绿；红证据由 `wf verify --red` 记录，批末门禁只跑全量绿）：

1. **L1 结构守卫**（`dotnet test Tests/LiteFramework.Core.Tests`）
   - `CompositionBudgetTests`（新增）：`App/Bootstrap/Modules` 不存在、`Modules/**` 装配段数量与命名、装配段行数预算（类 ≤400 / 方法 ≤80）。
   - `DomainBoundaryTests`（新增）：横向表现域不存在、业务端口不在 Core、`Modules/Battle/Abstractions` 齐备、业务域子目录命名规则。
   - `ModuleDependencyDeclTests`（改写）：扫描面 `Modules/**/*Composition.cs` ∪ `App/**/*Composition.cs` ∪ 过渡期 `App/ContainerModule.cs`；**身份改由类声明提取**；`AppComposition` 排除 `DependsOn` 覆盖；`Baseline` 批 3 清空。
   - `ModuleScopeAdoptionTests`（双根基 + 键映射）、`ArchitectureBudgetTests`（152 键删除 + 批 2 三键随迁）、`ClientAsmdefAllowlistTests`（引用集 + 硬边 case）、`ArchitectureGuardTests`。
2. **Unity EditMode / PlayMode**
   - `BattleSmokePlayModeTests`（新增）：harness（`Boot.unity` 起服 → `Started && Current == Main` → `TestModeRuntime.RequestEnter/RequestExit`）+ 观测面（`IsBuilt`/`Root.activeInHierarchy`/`Crosshair`/`ShotPulseCount`/`VoiceCount`/`VfxService.ActiveCount`/`DamageNumber.ActiveCount`/越肩几何容差）+ 离场容器级归零（≤2s 轮询）。
   - 既有 HUD/相机/动画/设置面用例全绿（能力端口替换不得改变行为）。
3. **门禁**：`wf verify` 的 `commands` 全绿（新增 `battle-smoke-playmode`、`domain-matrix`）+ Unity 重编译 0 error。

## 用例清单

| 用例 | 覆盖验收 | 期望（批 1/2 前） | 期望（对应批完成后） |
|---|---|---|---|
| `CompositionBudgetTests.模块目录不得残留于Bootstrap` | A1 | 红 | 绿 |
| `CompositionBudgetTests.装配段必须就地且数量达标` | A1 | 红（0 个） | 绿（≥13） |
| `CompositionBudgetTests.装配段必须保持薄` | A6/A16 | 绿（无文件） | 绿（预算内） |
| `DomainBoundaryTests.横向表现域不得存在` | A8 | 红 | 绿（批 2） |
| `DomainBoundaryTests.业务端口不得留在Core` | A7 | 红 | 绿（批 2） |
| `DomainBoundaryTests.战斗域Abstractions必须齐备` | A7 | 红（1/6） | 绿（批 2） |
| `DomainBoundaryTests.业务域子目录必须按职责命名` | A9 | 绿 | 绿 |
| `ModuleDependencyDeclTests.模块DependsOn必须覆盖Require面` | A6/A13 | 绿（Container 在 App 前仍被扫） | 绿（Baseline 空） |
| `ModuleScopeAdoptionTests.持有资源的模块必须登记模块作用域` | A6 | 绿 | 绿（键随改名） |
| `ArchitectureBudgetTests` | A6/A16 | 绿 | 绿（键删除/随迁） |
| `ClientAsmdefAllowlistTests` | A10/A10a | 绿 | 绿（引用集 + 硬边） |
| `BattleSmokePlayModeTests` | A12 | 未编写（批 2 写） | 绿（批 2 末） |

## 预期失败证据

2026-10-30 实测（`dotnet test --filter "FullyQualifiedName~CompositionBudgetTests|FullyQualifiedName~DomainBoundaryTests"`）：

```
失败 装配段必须就地且数量达标
失败 战斗域Abstractions必须齐备
失败 横向表现域不得存在
失败 业务端口不得留在Core
失败 模块目录不得残留于Bootstrap
失败!  - 失败: 5，通过: 2，已跳过: 0，总计: 7
```

失败原因均为**断言失败**（结构尚未迁移），非编译错误或环境故障；两个通过的用例是"无装配段所以无预算违规"与"业务域子目录暂无禁用名"的合法空绿。

`wf verify --red` 证据：`evidence/red.json`（命令集含 `composition-budget-red` / `domain-boundary-red` / `module-dependency-decl-red`）。

## 运行方式

```bash
# 结构守卫（快速）
dotnet test Tests/LiteFramework.Core.Tests/LiteFramework.Core.Tests.csproj \
  --filter "FullyQualifiedName~CompositionBudgetTests|FullyQualifiedName~DomainBoundaryTests" --nologo

# 先行测试证据
python .agents/skills/staged-delivery/scripts/workflow.py --run client-modularization verify --red

# 批末门禁（全量）
python .agents/skills/staged-delivery/scripts/workflow.py --run client-modularization verify
```

执行窗口注意：`config.json` 的命令集变更会使既有证据按指纹失效——**先改配置、再取红/取绿**；Unity 相关命令经后台进程执行，避免在编辑器脚本内同步等待。

## 新增业务域需要的步骤（A30 交付物）

1. `Assets/LiteGame/Modules/<域>/` 目录：`Abstractions/` + 按职责命名的子目录（禁 `View`/`Presentation`/`Domain`/`Logic`）。
2. （按需）独立 asmdef：改 `LiteGame.Modules.asmdef` 引用 + `ClientAsmdefAllowlistTests` 一行 + `Modules/AssemblyInfo.cs` 的 friend 清单；**若需引 `LiteGame.Adapters.*`，还要改该用例的硬边 case 并登记例外**。
3. `<域>Composition.cs`（薄装配：`Require`/`Put`/`Scope.Register`/`DependsOn`，无循环、无业务算法、不 `new` 适配器）。
4. `GameEntry.RegisterModules` 一行 + 在更早段的产品上加 `DependsOn`。
5. 端口只经 `Abstractions/`；跨程序集门面/装订需"类型落域 + 装订留调用方 + asmdef 引用与允许表一行"三处同步。

**不需要改任何共享能力域代码**——但会改两处守卫表（asmdef 允许表、模块依赖声明）；需要全局服务（场景/命令中心/流程机/桥装订）的域，注册仍落 `AppComposition` 末段。
