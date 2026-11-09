# 测试计划：Settings 页面端口 + 守卫棘轮（v6.1）

> 运行 client-module-recut · 创建于 2026-11-09T11:50:08+08:00

## 测试策略

- 测试层级：
  1. **L1 结构用例**（xunit，读仓库文件，不依赖 Unity）：friend 允许集、Settings 迁移源码断言。
  2. **Python unittest**：零使用边脚本/基线（合成 fixture + 生产基线子集）。
  3. **Unity 重编译 + PlayMode**：Settings 页端口（T7，post-interface）。
  4. **门禁**：L1/arch/domain/doc/`build-hash --check-shared`/hot-boundary。
- 关键风险与覆盖重点：
  - friend 作用域/归属（9 个 `Modules/*`、55 条、最近 asmdef、不递归子单元）。
  - 零使用边口径（复用 domain-matrix：去注释/字符串、depth=1 顶层类型、出现次数；同名类型任一声明单元算命中；外部程序集不在范围）。
  - Settings 页面 10 处静态门面替换 + `FrameRateIndex` static + 空端口 10 处 `?.` + OnShow 早退。
  - adapter 创建点在 `BindProductSettings` 之后/惰性（评审核对，不做单测）。
  - PlayMode 真 prefab 的 `SetTextKey` 需 fake `ILocalizationService`。

## 用例清单

| 用例 | 层级 | 关联任务/验收 | 输入或步骤 | 预期结果 |
|---|---|---|---|---|
| UT-FRIEND-1 | L1 xunit | T2 / AC1 | 读 9 个 `Modules/*/AssemblyInfo.cs` 与 `arch/units.json` 的 `friends` | 55 条逐条一致；无未登记 friend |
| UT-FRIEND-2 | L1 xunit | T2 / AC1 | 合成一个未登记 friend 的 fixture | 断言失败（红） |
| UT-FRIEND-3 | L1 xunit | T2 / AC1 | 检查 `LiteNet/Adapters/kcp2k`、`Editor/UITools` 嵌套 AssemblyInfo | 不被递归吞入；作用域仍 9 个 Modules |
| UT-ZERO-1 | Python unittest | T3 / AC1 | `arch/zero-use-edges.json` 存在；当前集合 ⊆ 基线 | 通过 |
| UT-ZERO-2 | Python unittest | T3 / AC1 | 合成一条新零使用边 | `--check` 失败（红） |
| UT-ZERO-3 | Python unittest | T3 / AC1 | 基线中一条边消失 | 通过（只减不增） |
| UT-SET-1 | L1 xunit | T6 / AC2 | 读 `LiteGame.Modules.UI.asmdef` | 不含 `LiteGame.Modules.Settings` |
| UT-SET-2 | L1 xunit | T5 / AC2 | 扫 `Page_SettingsLogic.cs` | `ProductSettings.Current` = 0 |
| UT-SET-3 | L1 xunit | T4 / AC2 | 读 `IPageContext`/`ISettingsPagePort`/`ProductSettingsAdapter` | `Settings` 属性存在；端口 28 成员；adapter 文件存在 |
| PM-SET-1 | PlayMode | T7 / AC2 | 打开设置页 | 不抛；控件初始化 |
| PM-SET-2 | PlayMode | T7 / AC2 | 改画质 Dropdown | 值写入端口 |
| PM-SET-3 | PlayMode | T7 / AC2 | 改音量 Slider | 值写入端口 |
| PM-SET-4 | PlayMode | T7 / AC2 | Back 关闭 | `Save` 调用一次 |
| PM-SET-5 | PlayMode | T7 / AC2 | `Ctx.Settings` 为空 | 控件禁用/收起；10 处 `?.` + OnShow 早退；不抛 |

## 预期失败证据

- 命令：
  1. `dotnet test Tests/LiteFramework.Core.Tests/LiteFramework.Core.Tests.csproj --filter "FullyQualifiedName~ModuleFriendAllowlistTests|FullyQualifiedName~SettingsPortMigrationTests" --nologo`
  2. `python -m unittest scripts.tests.test_zero_use_edges -v`
- 观察到的失败（预期）：
  1. friend 用例：`arch/units.json` 缺 `friends` 字段（或 55 条未登记）。
  2. Settings 用例：`Page_SettingsLogic.cs` 仍含 10 处 `ProductSettings.Current`；UI asmdef 仍引 `LiteGame.Modules.Settings`。
  3. 零使用边用例：`arch/zero-use-edges.json` 不存在/脚本不存在（断言失败）。
- 为什么这是正确的失败：三条都针对“迁移前”的真实缺口；不是导入/拼写/环境错误。PlayMode 用例在 `ISettingsPagePort` 存在前无法编译，属“无法先失败”的豁免项（见下），由 UT-SET-1/2/3 作为替代红测。

## 运行方式

- 全量测试：`python scripts/gate/test.py --suite L1 --profile PullRequest`；`python scripts/gate/arch-check.py --check`；`python scripts/gate/domain-matrix.py --check`；`python scripts/gate/doc-lint.py`；`python scripts/codegen/toolbox.py build-hash --check-shared`；`python scripts/gate/hot-boundary-check.py`。
- 仅运行新增测试：上述两条 red 命令；Settings PlayMode：`python scripts/gate/unity-tests.py --mode playmode --filter SettingsPagePlayModeTests`（T7）。

## 豁免与说明

- **PlayMode 用例无法先红**：`SettingsPagePlayModeTests` 引用 `ISettingsPagePort`（T4 才创建），在 T4 前是编译错误而非断言失败。替代红测：UT-SET-1/2/3（源码/asmdef 断言，当前必红）。PlayMode 用例在 T7 写并跑绿。
- **adapter 惰性时序**：属评审核对项（`AppComposition` 的 `Require<ProductSettings>` 不得早于 `BindProductSettings`），不写单测。
- **T1/T9** 为基线/文档任务，无新增单测；由命令与清单核对。
