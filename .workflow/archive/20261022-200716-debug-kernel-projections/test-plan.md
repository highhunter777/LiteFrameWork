# 测试计划：统一内核与三投影最小闭环

> 运行 debug-kernel-projections · 创建于 2026-10-22T18:20:19+08:00

## 测试策略

- 测试层级：
  - EditMode（内核/解析器契约）：`Assets/Tests/EditMode/DebugKernelEditModeTests.cs`，16 例（含 5 个参数化 parser 用例）。
  - 手工 Play（投影行为）：编辑器 Play 与控制台 F8；HUD F1–F7 回归；Hub 两个页面。
  - 门禁：L1、Doc、Unity 测试门禁脚本、`unity command recompile`。
- 关键风险与覆盖重点：
  - 命令发现/执行：精确名、唯一后缀、带参拒绝、未知名、审计。
  - 选项单表：已知/未知 id、写入后读回；测试前后恢复静态快照。
  - 请求桥：写侧 → `Take*` 读即清。
  - 读模型发现：`ReadSections` 包含 commands/options/stats/log/diag/battle/audit。
  - 无对局：`HasBattle=false`、`DescribeBattle` 空。
  - 解析器：空白、多空格、大小写、verb/args 切分。
  - 投影侧：控制台打开时输入拦截、关闭释放；HUD 七段读数不回归；Hub 页面空态不抛。

## 用例清单

| 用例 | 层级 | 关联任务/验收 | 输入或步骤 | 预期结果 |
|---|---|---|---|---|
| UT-1 命令发现按注册序 | EditMode | T1/AC1 | 注册一个 GmOnly 无参命令后读 `Commands` | 1 条、类型正确、GmOnly=true |
| UT-2 类型名执行并审计 | EditMode | T1/AC1 | `TryExecute(typeof(ProbeCommand))` | Ok、detail=probe-ok、审计 1 条成功 |
| UT-3 精确名/唯一后缀执行 | EditMode | T1/AC1 | `TryExecute("DebugKernelProbeCommand")`、`"ProbeCommand"` | 两者均 Ok |
| UT-4 带参命令拒绝 | EditMode | T1/AC1 | 注册带参命令后执行 | false，错误含“无参” |
| UT-5 未知命令 | EditMode | T1/AC1 | `TryExecute("NoSuchCommand")` | false，错误含“未找到” |
| UT-6 已知选项写入 | EditMode | T1/AC1 | `TrySetOption("InfiniteAmmo", true)` | true，`GetOption` 与 `TestModeOptions.Get` 均为 true |
| UT-7 未知选项 | EditMode | T1/AC1 | `TrySetOption("NoSuchOption", true)` | false，错误含“未找到” |
| UT-8 请求桥读即清 | EditMode | T1/AC1 | 写 Enter/Teleport 后连续 `Take*` | 第一次 true、第二次 false |
| UT-9 无对局诊断 | EditMode | T1/AC1 | 确保无 BattleContext 后读 | `HasBattle=false`，描述为空 |
| UT-10 读段发现 | EditMode | T1/AC1 | 读 `ReadSections` | 含 7 个段名 |
| UT-11 解析空白 | EditMode | T5/AC4 | `""`、`"   "` | false |
| UT-12 解析大小写/多空格 | EditMode | T5/AC4 | `"  HeLP  a  b "` | true，verb=help，args=[a,b] |
| UT-13 解析选项行 | EditMode | T5/AC4 | `"option InfiniteAmmo on"` | true |
| UT-14 控制台逐命令 | 手工 Play | T5/AC4 | F8 → help/commands/options/option/stats/log/diag/battle/request/run/audit/undo/redo/clear | 每条有回显；发现命令可执行/带参拒绝；输入被拦 |
| UT-15 HUD 回归 | 手工 Play | T4/AC3 | F1 开 HUD，检查七段；F2–F7 | 六段读数正常、性能段正常、仅读不写 |
| UT-16 编辑器页空态/数据 | 手工 Play | T2/AC2 | Hub 命令与审计页、联机诊断页 | 有内核时列命令/审计/对局读数；无内核时空态不抛 |

## 预期失败证据

- 命令：`python scripts/gate/unity-tests.py --mode editor --filter DebugKernelEditModeTests`（工作流 red command：`debug-kernel-editmode`）
- 观察到的失败：16/16 失败，全部为 `System.NotImplementedException`（`DebugKernel`/`DebugConsoleCommand` 签名骨架），例如 `get_Commands`、`TryExecute(Type)`、`TrySetOption`、`RequestEnterTestMode`、`get_ReadSections`、`TryParse`。
- 为什么这是正确的失败：失败来自内核/解析器尚未实现，不是导入/拼写/环境错误；骨架签名已编译通过，测试走真实公开接口。证据见 `evidence/red.json`。

## 运行方式

- 全量门禁：`python scripts/gate/test.py --lane L1 --profile PullRequest` + `python scripts/gate/test.py --lane Doc`
- 仅新增测试：`python scripts/gate/unity-tests.py --mode editor --filter DebugKernelEditModeTests`
- 编译：`unity command recompile` + 轮询 `unity command recompile_status`
- 手工 Play：Unity Editor Play → F1（HUD）/ F8（控制台）；Hub `LiteGame/工具链` → 命令与审计 / 联机诊断。

## 豁免与说明

- HUD 七段、Hub 页面与控制台输入拦截属于 Unity 运行时/输入路径，EditMode 无法覆盖，采用真实 Play 手工验证（红线 13：入口 → 操作 → 副作用断言 → 证据）；T6 记录步骤与结果。
- IL2CPP 开发包内反射执行发现命令可能受 AOT 限制；本批先在编辑器 Play 验证，T6 记录实际结论；若不可用，控制台仍保留内置命令与读数，发现命令明确提示“需编辑器执行”，不影响 AC1–AC3。
