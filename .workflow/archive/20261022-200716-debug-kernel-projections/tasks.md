# 实施任务清单：统一内核与三投影最小闭环

> 本文件由 workflow.py 生成，请使用 `task` 子命令更新，勿手改。

## 任务清单

阶段：已收尾（待归档） ｜ 完成 7/7（取消 0，阻塞 0）

| ID | 任务 | 依赖 | 状态 | 验收标准 | 验证方式 | 证据 |
|---|---|---|---|---|---|---|
| T1 | 内核：DebugKernel + DebugStatsDiscovery + 装配注册 | - | 完成 | DebugKernel 可由容器/GameEntry 取到；命令与选项发现来自注册表；TryExecute 精确/唯一后缀/未知/带参命令返回明确结果；选项与请求桥经内核；DebugKernelEditModeTests 全绿。 | unity command recompile; unity command run_tests --mode editor --filter DebugKernelEditModeTests | unity recompile 0 errors; EditMode DebugKernelEditModeTests 16/16 passed |
| T2 | 编辑器投影收口：命令与审计页 / 联机诊断页 | T1 | 完成 | 两页不再直接持有 CommandCenter/页内反射执行；找到内核时命令/审计/对局读数正常，未装配时空态不抛；recompile 0 错误。 | unity command recompile; 手工打开 Hub 两个页面核对 | recompile 0 errors; Play reflection: GmCommandsPage cmds=5 audit=1; GmNetDiagPage now calls kernel.DescribeBattle |
| T3 | 输入层：DevHotkey F8 与文本输入所有权 | - | 完成 | F8 在 Input System 与旧 Input 两条后端都可识别；同一时刻只有一个 owner 收字符；HUD F7 过滤行为不变；recompile 0 错误。 | unity command recompile; 手工 Play 验证 F7/F8 | unity recompile 0 errors; DevHotkey F8/owner API wired to DevHUD |
| T4 | DevHUD 收口：六段读模型改走内核 | T1, T3 | 完成 | 六段读数与改造前一致；场景组件 Stats 仍可见；HUD 不新增写动作；控制器/面板未装配时空态；F1–F7 行为不变。 | unity command recompile; 手工 Play 对照 HUD 七段 | Play: DevHUD kernel=True; sections Stats=97 lines, LogRecent=10, Perf=2, LogSink=2; Net/Diag empty without battle |
| T5 | 运行期控制台：F8 文本控制台 + 命令解析器 | T1, T3 | 完成 | F8 可开合；内置命令与发现的无参 GM 命令可执行并回显；带参命令拒绝；输出有上限且滚动可用；打开时战斗输入被拦、关闭后恢复；异常不炸游戏；EditMode 解析器/内核用例全绿。 | unity command recompile; unity command run_tests --mode editor --filter DebugKernel; 手工 Play 逐条命令验证 | Play real Input System: F8 open/close; help/commands/options/option/run/audit/stats/log/battle; input blockers 2->1 on … |
| T6 | 真实验证与文档回写 | T2, T4, T5 | 完成 | 手工验证证据完整（操作步骤、控制台回显、截图/日志）；13 手册与进度卷回写；Doc lane 通过。 | python scripts/gate/test.py --lane Doc; 手工 Play 证据核对 | 13手册/14地图/施工进度 ch14 已回写；Doc 0 violation; L1 1819/1819; Play 证据见 ch14 |
| T7 | 热键冲突修正：调试控制台 F8 → F12 | T5 | 完成 | F12 可开合控制台；F8 不再被控制台消费；HUD 提示与文档一致；recompile 0 错误。 | unity command recompile; 手工 Play F12/F8 | Play: F12 open/close; after releasing keys F8 does not open console; recompile 0 errors |

## 任务详情

### T1 · 内核：DebugKernel + DebugStatsDiscovery + 装配注册

- 描述: 新增开发三宏内核：读模型（Stats/Log/Diag/Battle/LogSink）、动作（命令执行/审计/撤销/选项/请求桥/输入拦截租约）、发现（命令/读段）；迁移场景 Stats 发现到 App；ContainerModule 注册，GameEntry 暴露只读访问器。

- 文件: Assets/LiteGame/App/Debug/DebugKernel.cs, Assets/LiteGame/App/Debug/DebugStatsDiscovery.cs, Assets/LiteGame/App/Bootstrap/ContainerModule.cs, Assets/LiteGame/App/GameEntry.cs

### T2 · 编辑器投影收口：命令与审计页 / 联机诊断页

- 描述: GmCommandsPage 改为消费 DebugKernel 的 Commands/Audit/TryExecute，删除页内反射执行；GmNetDiagPage 改为消费 DebugKernel.DescribeBattle。保留空态、拒绝原因与 HUD 开关投影。

- 文件: Assets/LiteGame/Editor/Gm/GmCommandsPage.cs, Assets/LiteGame/Editor/Gm/GmNetDiagPage.cs

### T3 · 输入层：DevHotkey F8 与文本输入所有权

- 描述: DevKey 增加 F8；DevHotkey.SetTextInputActive/TakeTypedText 增加 owner 参数，保证 HUD 过滤与控制台不会同时收字；更新现有调用点。

- 文件: Assets/LiteClient/Adapters/Platform.Unity/Dev/DevHotkey.cs, Assets/LiteGame/DevTool/DevHUD.cs

### T4 · DevHUD 收口：六段读模型改走内核

- 描述: DevHUD 从 GameEntry 取内核；SectionSet 的错误/联机/Stats/Log/DiagTrace/日志 sink 六段改读内核；场景加载重建、每轮剔除由内核承担；性能段保持 HUD 自采样；HUD 仍只读。

- 文件: Assets/LiteGame/DevTool/DevHUD.cs, Assets/LiteGame/DevTool/Hud/DevHudSectionSet.cs, Assets/LiteGame/DevTool/Hud/DevHudStatsDiscovery.cs

### T5 · 运行期控制台：F8 文本控制台 + 命令解析器

- 描述: 新增 DebugConsole（DevTool，三宏）：F8 开关、IMGUI 输出/输入、内置命令 help/commands/run/audit/options/option/stats/log/diag/battle/request/undo/redo/clear；打开时注册 debug.console 输入拦截并取得文本输入所有权，关闭/销毁幂等释放；内核缺失时空态。新增纯解析器 DebugConsoleCommand。

- 文件: Assets/LiteGame/App/Debug/DebugConsoleCommand.cs, Assets/LiteGame/DevTool/DebugConsole.cs

### T6 · 真实验证与文档回写

- 描述: 在编辑器 Play/开发包中按真实操作路径验证：F8 控制台命令、输入拦截、HUD 七段、Hub 两个页面；更新 13-调试手册的热键/控制台/内核说明，按章节规范在施工进度/GM与调试线追加批次证据。

- 文件: Docs/项目说明/13-调试手册.md, Docs/施工进度/GM与调试线.md

### T7 · 热键冲突修正：调试控制台 F8 → F12

- 描述: 发现现行《UI 运行时调试工具链专项设计》已把 F8 预留给 ui.runtime-debug；调试控制台改用 F12，避免抢占。同步 DevKey/DevHotkey、控制台帮助/提示、HUD 状态行与文档。

- 文件: Assets/LiteClient/Adapters/Platform.Unity/Dev/DevHotkey.cs, Assets/LiteGame/DevTool/DebugConsole.cs, Assets/LiteGame/DevTool/DevHUD.cs, Docs/项目说明/13-调试手册.md

