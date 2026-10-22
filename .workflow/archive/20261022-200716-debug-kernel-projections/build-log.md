# 施工记录：统一内核与三投影最小闭环

> 运行 debug-kernel-projections · 创建于 2026-10-22T18:20:19+08:00

## 施工记录

<!-- 任务开始、完成、阻塞、取消等事件由 workflow.py 自动追加。 -->
- 2026-10-22T18:20:58+08:00 · 新增任务 T1「内核：DebugKernel + DebugStatsDiscovery + 装配注册」
- 2026-10-22T18:21:01+08:00 · 新增任务 T2「编辑器投影收口：命令与审计页 / 联机诊断页」
- 2026-10-22T18:21:04+08:00 · 新增任务 T3「输入层：DevHotkey F8 与文本输入所有权」
- 2026-10-22T18:21:07+08:00 · 新增任务 T4「DevHUD 收口：六段读模型改走内核」
- 2026-10-22T18:21:11+08:00 · 新增任务 T5「运行期控制台：F8 文本控制台 + 命令解析器」
- 2026-10-22T18:21:13+08:00 · 新增任务 T6「真实验证与文档回写」
- 2026-10-22T18:24:51+08:00 · 开始任务 T1「内核：DebugKernel + DebugStatsDiscovery + 装配注册」
- 2026-10-22T18:26:07+08:00 · 完成任务 T1 · 证据: unity recompile 0 errors; EditMode DebugKernelEditModeTests 16/16 passed
- 2026-10-22T18:26:07+08:00 · 开始任务 T3「输入层：DevHotkey F8 与文本输入所有权」
- 2026-10-22T18:27:01+08:00 · 完成任务 T3 · 证据: unity recompile 0 errors; DevHotkey F8/owner API wired to DevHUD
- 2026-10-22T18:27:01+08:00 · 开始任务 T4「DevHUD 收口：六段读模型改走内核」
- 2026-10-22T18:28:07+08:00 · 开始任务 T2「编辑器投影收口：命令与审计页 / 联机诊断页」
- 2026-10-22T18:28:45+08:00 · 开始任务 T5「运行期控制台：F8 文本控制台 + 命令解析器」
- 2026-10-22T18:33:01+08:00 · 完成任务 T2 · 证据: recompile 0 errors; Play reflection: GmCommandsPage cmds=5 audit=1; GmNetDiagPage now calls kernel.DescribeBattle
- 2026-10-22T18:33:01+08:00 · 完成任务 T4 · 证据: Play: DevHUD kernel=True; sections Stats=97 lines, LogRecent=10, Perf=2, LogSink=2; Net/Diag empty without battle
- 2026-10-22T18:33:01+08:00 · 完成任务 T5 · 证据: Play real Input System: F8 open/close; help/commands/options/option/run/audit/stats/log/battle; input blockers 2->1 on close
- 2026-10-22T18:33:01+08:00 · 开始任务 T6「真实验证与文档回写」
- 2026-10-22T18:34:55+08:00 · 新增任务 T7「热键冲突修正：调试控制台 F8 → F12」
- 2026-10-22T18:36:05+08:00 · 开始任务 T7「热键冲突修正：调试控制台 F8 → F12」
- 2026-10-22T18:36:05+08:00 · 完成任务 T7 · 证据: Play: F12 open/close; after releasing keys F8 does not open console; recompile 0 errors
- 2026-10-22T18:38:23+08:00 · 完成任务 T6 · 证据: 13手册/14地图/施工进度 ch14 已回写；Doc 0 violation; L1 1819/1819; Play 证据见 ch14

## 关键决定

- 内核住 `LiteGame.App` 的 dev 三宏区，装配期由 `ContainerModule` 注册；`GameEntry.DebugKernel` 作为运行期投影的只读访问器（与 `Stats` 同口径），编辑器页仍走 `GmEditorResolve`。
- 场景组件 Stats 发现从 DevTool 迁到 `App/Debug/DebugStatsDiscovery.cs`；旧 `DevHudStatsDiscovery.cs` 经 unity-pipeline `delete_asset` 删除，避免两套扫描逻辑。
- 命令执行统一收进 `DebugKernel.TryExecute`：内部缓存 `Send<T>` 的 `MethodInfo`；页内反射逻辑删除，控制台与编辑器页共用同一条路径。带参命令明确拒绝并标注“需专面板”。
- 运行期控制台用 IMGUI（零资产、IL2CPP 开发包可直接编译），不做 UITK 主题资产；最终控制台/面板外壳仍归 T1 决策点。
- 文本输入改为 owner 所有权；控制台打开时注册 `debug.console` 输入拦截（`IntentGate.BlockerKey`），关闭/销毁用 `IDisposable` 租约释放。

## 偏差与处理

- **F8 → F12**：实现中发现工作区已有现行《UI 运行时调试工具链专项设计》，F8 预留给 `ui.runtime-debug`；控制台改用 F12（T7），并在 13 手册/能力地图中写明分工。F8 未落地前不构成现行能力。
- **新增测试门禁脚本**：`unity command run_tests` 在测试失败时仍 exit 0；为让先行/验证门禁真实失败，新增 `scripts/gate/unity-tests.py` 解析 `Summary.Failed`，并接入 `.workflow/config.json` 的 `debug-kernel-editmode`。这是计划外但必要的门禁修复。
- **UI 调试面文档**：原计划的《开发包 UI 调试面专项设计》与现行 `UI运行时调试工具链专项设计` 重复；按“不复制第二份设计”改为归档重定向指针，索引仍保留“已归档”指向。
- **HUD 六段**：错误/联机/Stats/Log/DiagTrace/日志 sink 全部经内核；性能段按计划保留 HUD 自采样。场景加载重建/每轮剔除由内核承担，HUD 只用延迟绑定 + 空态守卫处理装配时机。

## 问题与修复

- `TrySnapshotStats` 签名与 `IModuleStats.Snapshot(Dictionary<...>)` 不一致 → 改用 `Dictionary<string,string>`，编译修复。
- `DevHotkey.SetTextInputActive` 两处 `keyboard` 局部变量作用域冲突 → 释放分支改名 `releaseKeyboard`。
- `DebugConsole` 缺 `using LiteClient;`，`DevHotkey/DevKey` 不可见 → 补 using。
- 测试用例误用 `StringAssert.IsEmpty`（当前 NUnit 版本无此 API）→ 改 `Assert.IsEmpty`。
- Play 中首次 `eval` 偶发 5s 主线程超时 → 等一帧/重试即可，非代码缺陷；后续验证未复现。
- IL2CPP 开发包内发现命令的反射执行未实测（本轮不出包）；已登记为已知边界，内置命令/读数不受影响。

## 验证结果

- 最终 `wf verify` PASS 10/10：L1 1819、Doc 0 violation、restore/l0/toolchain-check/toolchain-docs/doc-lane/l2-meta 全绿、`debug-kernel-editmode` 16/16。
- 手工 Play（真实 Input System 事件）：F12 开/关控制台；`help/commands/options/option/run/audit/stats/log/battle` 均有回显；`run GmResetClockCommand` 成功并进审计；输入拦截租约 1→2→1；F8 不再打开控制台；HUD 六段经内核（Stats 97 行/Log 10/Perf 2/LogSink 2）；编辑器命令页 `cmds=5 audit=1`。
- 过程事件：验证中途 Unity 被 “Scene(s) Have Been Modified” 模态框卡住；按用户裁决保存场景后仍未恢复，经用户授权重启 Unity；重启后测试与全量 verify 通过。
- 2026-10-22T18:23:32+08:00 · 先行测试 PASS · 1/2 · 证据 evidence/red.json
- 2026-10-22T18:24:23+08:00 · 先行测试 PASS · 2/2 · 证据 evidence/red.json
- 2026-10-22T18:40:05+08:00 · 验证 FAIL · 8/10 · 证据 evidence/verify.json
- 2026-10-22T19:52:13+08:00 · 验证 FAIL · 8/10 · 证据 evidence/verify.json
- 2026-10-22T20:04:06+08:00 · 验证 PASS · 10/10 · 证据 evidence/verify.json
- 2026-10-22T20:06:29+08:00 · 验证 PASS · 10/10 · 证据 evidence/verify.json

## 交付摘要

统一调试内核落地：`DebugKernel` 收拢读模型、动作模型与发现协议；编辑器命令/联机诊断页、DevHUD 六段与运行期 F12 控制台三投影共用同一内核。命令执行、审计、测试开关、请求桥与输入拦截均有单一入口；旧的 DevTool Stats 扫描迁入内核并删除。F8 被现行 UI 运行时调试设计占用，控制台改用 F12。未新增 UI 调试面板；UI 调试面重复稿改为归档重定向。

## 未决事项

- **IL2CPP 开发包内发现命令的反射执行未实测**：需出包授权；内置命令/读数不依赖反射，已在 13 手册 §10.1 #10 与进度 ch14 登记。
- **F8 UI 运行时调试面板仍是目标形态**（未落地）；本批只做 F12 通用控制台，不替代该专项。
- `.workflow/config.json` 追加了 `debug-kernel-editmode`，同仓另一个进行中运行的 verify 会多跑这一项；未改阶段/审批。
- `scripts/codegen/_uiform_row.py` 的 ROOT 单源修复来自工作区既有 UI 工具改动（非本批产物）；提交时应与该改动分开说明。
- 编辑器重启前后的场景保存/模态框事件只影响本次会话，不是代码缺陷；Unity 重启后已恢复。

## 文件清单

- 新增：`Assets/LiteGame/App/Debug/DebugKernel.cs`、`DebugStatsDiscovery.cs`、`DebugConsoleCommand.cs`、`Assets/LiteGame/DevTool/DebugConsole.cs`、`Assets/Tests/EditMode/DebugKernelEditModeTests.cs`、`scripts/gate/unity-tests.py`。
- 修改：`Assets/LiteGame/App/Bootstrap/ContainerModule.cs`、`App/GameEntry.cs`、`Assets/LiteClient/Adapters/Platform.Unity/Dev/DevHotkey.cs`、`Assets/LiteGame/DevTool/DevHUD.cs`、`Hud/DevHudSectionSet.cs`、`Assets/LiteGame/Editor/Gm/GmCommandsPage.cs`、`GmNetDiagPage.cs`、`.workflow/config.json`、`scripts/codegen/_uiform_row.py`（外部门禁修复）。
- 删除：`Assets/LiteGame/DevTool/Hud/DevHudStatsDiscovery.cs`（迁入内核）。
- 文档：`Docs/项目说明/13-调试手册.md`、`14-客户端开发能力地图.md`、`Docs/设计文档/架构/GM与调试工具分层专项设计.md` §7、`Docs/设计文档/测试与性能/开发包UI调试面专项设计.md`（归档重定向）、`Docs/施工进度/GM与调试线.md` ch14。

## 后续建议

- 给 `CommandOptions` 增加稳定调用名/参数描述，让控制台不再依赖类型名与“唯一后缀”；届时 `DebugKernel` 发现表可直接复用。
- `unity-tests.py` 对长测试改用 `--async_tests true` + `test_status` 轮询，避免 Pipeline 30s 请求超时；当前 16 例 ~5s，暂不需要。
- 出一次 development Player（需用户授权）验证 IL2CPP 下 `run` 的反射路径；若失败，控制台改为“列出 + 提示编辑器执行”，不影响内置命令。
- 按现行《UI 运行时调试工具链专项设计》推进 F8 面板时，直接复用 `DebugKernel` 的读模型/动作/发现，不另建执行器。

## 阶段流转

<!-- 阶段推进与回退记录由 workflow.py 自动追加。 -->
- 2026-10-22T18:20:19+08:00 · 初始化运行，当前阶段「提出方案」
- 2026-10-22T18:20:48+08:00 · 完成「提出方案」→ 「实施任务清单」
- 2026-10-22T18:21:16+08:00 · 完成「实施任务清单」→ 「评审任务」
- 2026-10-22T18:24:37+08:00 · 完成「评审任务」→ 「先写测试」
- 2026-10-22T18:24:40+08:00 · 完成「先写测试」→ 「正式施工」
- 2026-10-22T18:38:24+08:00 · 完成「正式施工」→ 「测试代码」
- 2026-10-22T20:04:10+08:00 · 完成「测试代码」→ 「评审代码」
- 2026-10-22T20:07:10+08:00 · 完成「评审代码」→ 「回写与收尾」
- 2026-10-22T20:07:13+08:00 · 完成「回写与收尾」→ 完成
