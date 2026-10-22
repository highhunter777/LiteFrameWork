# 方案：统一内核与三投影最小闭环

> 运行 debug-kernel-projections · 创建于 2026-10-22T18:20:19+08:00

## 背景与目标

- 现状：调试能力已分散成三套消费方式——编辑器 GM 页直接解析容器服务/命令中心，DevHUD 直接读 `GameEntry.Stats`/`BattleDiagnostics`/`Log`/`DiagTrace`，开发包 Player 只有只读 HUD、没有运行期操控入口。写口本身（`CommandCenter`、`TestModeOptions`、`TestModeRuntime`）是单源的，但读模型、动作执行与发现协议没有统一门面。
- 要解决的问题：同一调试能力在编辑器与开发包里各写一套接线；新增一个调试动作或读数要在多个投影重复接线；开发包 Player 无法通过命令面执行已注册的 GM 动作。
- 目标（可观察）：
  1. 新增 `DebugKernel`（`LiteGame.App`，开发三宏）作为读模型/动作模型/发现协议的唯一门面，并在装配期注册；
  2. 编辑器命令与审计页、联机诊断页改为消费内核；DevHUD 的读段改为消费内核；
  3. 开发包新增文本控制台（`LiteGame.DevTool`，F8）作为运行期操控投影，命令执行、选项开关、请求桥全部走内核；
  4. 现有只读 HUD 定位不变，release 与测试者分发包零残留。

## 需求澄清与假设

- 需要确认的问题：本次交付范围已在执行前确认——只做“内核 + 三投影最小闭环”，不新增 UI 调试面板。
- 已确认答复：用户选择“内核 + 三投影最小闭环（推荐）”。
- 采用假设：
  - 运行期操控投影的最小载体是**文本控制台**（F8、IMGUI），不引入 UITK 主题资产；面板载体归后续 T1 决策。
  - 内核住 `LiteGame.App` 的 dev 三宏区；投影住 `LiteGame.DevTool`，沿用 DevHUD 的“投影 → App”单向依赖。
  - 场景组件型 `IModuleStats` 发现从 DevTool 迁到 App 内核，避免内核与投影各持一套发现逻辑。
  - 编辑器测试模式页仍直接调用 `TestModeOptions`/`TestModeRuntime`：它们是内核动作模型的静态写点，且页面需要在未进 Play 时显示启动项；不视为第二套实现。
- 假设影响与风险：控制台以 IMGUI 实现，视觉与标准控件能力有限，但零资产、零收集组、可直接在 IL2CPP 开发包工作；后续面板/控制台外壳可替换，不影响内核契约。

## 范围与非目标

- 范围内：
  - `DebugKernel`：命令/审计发现与执行、测试开关读写、pre-Play 请求桥、日志/DiagTrace/对局诊断/模块统计读模型、输入拦截租约。
  - 编辑器投影收口：`GmCommandsPage`、`GmNetDiagPage`。
  - DevHUD 读模型收口：错误/联机/Stats/Log/DiagTrace/日志 sink 六段；性能段保持 HUD 自采样。
  - 运行期控制台：F8、命令解析、内置命令、发现命令执行、输入拦截、输出缓冲、异常隔离。
  - `DevHotkey` 增加 F8 与文本输入所有权（HUD 过滤与控制台互斥）。
  - 编辑模式测试（内核/解析器/选项/请求桥）、L1/Doc 门禁、手工 Play 验证。
- 非目标：
  - 不做 UI 调试面板、页面目录/状态注入/可点性可视化（归后续《开发包 UI 调试面专项设计》U0–U2）。
  - 不做通用 Player 控制台外壳的最终形态裁决（归 T1 决策点）。
  - 不做远程调试协议、世界查看器、弱网注入、多客户端聚合。
  - 不改产品 UI 生命周期、导航单写者、资源收集目录与判定路径。
  - 不做 release 可调试；不新增 release 可见的调试类型/资产。

## 现状与约束

- 相关代码/模块：
  - `Assets/LiteFramework/Scripts/Core/Command/CommandCenter.cs`：注册/发现/审计/权限门/撤销；`GetRegisteredInfos()`/`GetAuditLog()` 在开发三宏内。
  - `Assets/LiteGame/App/Bootstrap/ContainerModule.cs`：装配 `CommandCenter`、GM 场景命令；内核注册点。
  - `Assets/LiteGame/App/Debug/TestModeOptions.cs`、`TestModeRuntime.cs`：开关单表与一次性请求桥。
  - `Assets/LiteGame/DevTool/DevHUD.cs`、`Hud/DevHudSectionSet.cs`、`Hud/DevHudStatsDiscovery.cs`：只读投影与读段实现。
  - `Assets/LiteGame/Editor/Gm/GmCommandsPage.cs`、`GmNetDiagPage.cs`、`GmEditorResolve.cs`：编辑器投影与取件。
  - `Assets/LiteClient/Adapters/Platform.Unity/Dev/DevHotkey.cs`：开发热键/文本输入后端。
  - `Assets/LiteGame/App/GameEntry.cs`：公开只读访问器的落点（与 `Stats` 同口径）。
- 约束：
  - 开发三宏：`UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG`；release 不编译调试类型。
  - 判定路径（伤害/弹药/命中/checksum）禁读任何静态可变值；内核不进入 Sim/网络热路径。
  - 不新增 `public static` 可变状态；内核为装配期注入的实例。
  - 运行期投影不反射私有服务；编辑器取件仍走 `GmEditorResolve` 豁免形态。
  - 控制台打开时必须拦截战斗输入（输入服务 `RegisterBlocker`），关闭/销毁必须释放（含异常路径）。
  - `CommandCenter` 只对无参命令可反射构造；带参命令在发现面标注“需专面板”并由控制台拒绝。
  - UI/Unity 资产不新增；测试不修改真实 prefab/场景。

## 候选方案与取舍

| 方案 | 核心思路 | 优点 | 代价/风险 | 结论 |
|---|---|---|---|---|
| A | 静态 `DebugKernel` 门面，直接调静态读口/写口 | 改动最小，编辑器页易接入 | 静态可变状态/全局单例，测试隔离差，与项目红线冲突 | 否决 |
| B | 装配期注册的内核实例；投影经 `GameEntry` 公开访问器或容器取件 | 单源、可测、依赖方向清晰；编辑器与运行期共用 | 需要新增装配点与 HUD/页面的接线改动 | **采用** |
| C | 建事件总线/远程协议统一所有调试面 | 未来可扩展远程调试 | 超出最小闭环，增加第二套基础设施 | 否决 |

推荐方案与理由：B。内核作为普通实例在 `ContainerModule` 装配，`CommandCenter` 与 `IInputService` 构造注入；`GameEntry` 暴露只读访问器给运行期投影，编辑器页经 `GmEditorResolve` 取同一实例。这样不新增静态状态，也能用现有测试程序集直接测。

## 设计要点

- 接口/数据：
  - `DebugKernel`（`Assets/LiteGame/App/Debug/DebugKernel.cs`，开发三宏）：
    - 读：`Stats`/`RebuildStats`/`PruneStats`/`TrySnapshotStats`、`RecentLogs`/`ErrorCount`、`RecentDiag`/`DiagTotal`、`DescribeBattle`/`AppendExtendedBattle`/`TryTakeBattleSnapshot`、`LogSink`。
    - 动作：`TryExecute(Type)`、`TryExecute(string)`、`Undo`/`Redo`、`Options`/`GetOption`/`TrySetOption`、`RequestEnter/Exit/Teleport/Direct`、`BlockInput`（返回 `IDisposable` 租约）。
    - 发现：`Commands`（按注册序）、`ReadSections`（`stats/log/diag/battle/audit/options/commands`）。
  - `DebugStatsDiscovery`（`Assets/LiteGame/App/Debug/DebugStatsDiscovery.cs`）：从 `DevHudStatsDiscovery` 迁移，内核持有；容器注册件 + 场景组件合并、排序、剔除销毁件。
  - `DebugConsoleCommand.TryParse`（`Assets/LiteGame/App/Debug/DebugConsoleCommand.cs`，纯逻辑）：trim/空白切分/verb 小写；无引号语法，命令名与参数均不含空格。
  - `DebugConsole`（`Assets/LiteGame/DevTool/DebugConsole.cs`）：F8 开关、IMGUI 底部面板、输出环形缓冲（200 行）、输入行、命令执行、输入拦截租约。
- 关键流程：
  1. 装配：`ContainerModule` 创建 `DebugKernel(commands, input)` 并 `context.Put`；`GameEntry` 引导完成后持有只读引用。
  2. 编辑器：`GmCommandsPage` 从容器取内核，列出 `Commands`、显示 `Audit`、执行无参命令；`GmNetDiagPage` 用 `DescribeBattle()`。
  3. HUD：`Start` 取内核并构建段集；场景加载时 `RebuildStats`；每轮 `PruneStats`；六段读内核。
  4. 控制台：F8 打开 → 取内核 → 注册 `debug.console` 输入拦截 + 文本输入所有权 → 逐帧收字符/Backspace/Enter/Escape → 解析 → 调内核 → 输出；关闭/销毁释放租约与文本输入。
  5. 命令执行：控制台/编辑器页都经 `DebugKernel.TryExecute`；内核缓存每种命令的 `Send<T>` 反射委托，只允许无参构造；带参命令返回明确拒绝。
- 失败与边界处理：
  - 内核未装配/未取到：编辑器页与控制台显示空态/原因，不抛、不影响启动。
  - 命令无参构造失败/执行异常：返回 `CommandResult` 或错误文案；审计由 `CommandCenter` 照常记录。
  - 选项 id 非法/值非法：返回 false + 原因，不静默。
  - 输入拦截注册失败/容量满：控制台仍可打开，但输出 Warning；关闭时幂等释放。
  - HUD 段异常：沿用段级 try/catch，单段失败不影响其他段。
  - 控制台输出超限：保留最近 200 行，滚动位置夹取。
- 兼容/性能：
  - 轮询沿用 HUD 0.25s；控制台只在打开时逐帧收字符，不开时不耗。
  - 内核只做读/低频动作，不进入每帧热路径；`Stats` 发现沿用 0.5s 缓存与场景加载重扫。
  - `F8` 与现有 F1–F7 不冲突；`DevHotkey` 文本输入增加 owner，避免 HUD 过滤与控制台同时收字。

## 通用性与扩展性

- 根因/共性问题：问题不是“缺一个控制台”，而是**读模型、动作模型、发现协议没有单一门面**，导致每个载体各自接线。内核把这些收成一份，投影只做渲染/输入。
- 复用与扩展点：
  - 新增命令：注册到 `CommandCenter` 后自动出现在编辑器页与控制台 `commands`/`run`；无需改投影。
  - 新增开关：只改 `TestModeOptions.All`；编辑器页与控制台 `options` 自动跟随。
  - 新增读数：内核加一个只读方法/段名，投影按需消费；不引入第二套读口。
  - 新增投影：实现 F 键/载体即可复用同一内核，不改动作实现。
- 避免硬编码/特判的约束：
  - 不硬编码命令清单/选项清单/统计模块名；全部按发现面渲染。
  - 控制台只做词法解析与输出格式化，不写业务分支；命令执行只有一条路径（内核 → `CommandCenter`）。
  - 不按命令类型名写 if/switch；用注册表 + 名称匹配（精确/唯一后缀）。
  - 不新增静态可变状态；内核实例由装配注入，测试可独立构造。

## 测试策略

- 测试层级与关键用例：
  - EditMode（`Assets/Tests/EditMode/DebugKernelEditModeTests.cs`）：
    - 命令发现按注册序、`TryExecute` 精确名/唯一后缀/未知名/带参命令拒绝；审计记录成功与拒绝。
    - `TrySetOption` 已知/未知 id、on/off 解析（解析器层）、单表写值；测试前后保存/恢复静态选项。
    - 请求桥写入与 `Take*` 读即清；测试后复位。
    - `DescribeBattle()` 无对局返回空；`DebugConsoleCommand.TryParse` 空白/多空格/大小写/参数切分。
  - 手工 Play（开发包/编辑器 Play）：F8 打开控制台 → `help`/`commands`/`option`/`stats`/`log`/`diag`/`battle`/`run`/`audit`；打开时角色不动（输入拦截），关闭后恢复；HUD F1–F7 不受影响；F7 过滤与 F8 控制台不同时抢字。
  - 门禁：`python scripts/gate/test.py --lane L1 --profile PullRequest`、`python scripts/gate/test.py --lane Doc`、Unity EditMode 测试、`unity command recompile` 0 错误。
- 如何先失败后通过：
  - 先落内核/解析器的**签名骨架**（方法体 `throw new NotImplementedException()`）与测试；运行 EditMode 过滤用例，记录断言/异常失败（`evidence/red.json`）。
  - 再实现内核、投影与控制台；同一过滤用例转绿；最后跑 L1/Doc 与手工 Play。
  - 若测试在骨架阶段因编译失败而非断言失败，先补齐最小签名再重跑，确保红证据来自行为缺失而非拼写/环境。

## 验收标准

- [ ] AC1：`DebugKernel` 在装配期注册并可由编辑器/运行期投影取到；命令、审计、选项、请求桥、日志、DiagTrace、对局诊断、模块统计均经内核读取/执行。
- [ ] AC2：`GmCommandsPage` 与 `GmNetDiagPage` 不再直接持有 `CommandCenter`/反射执行逻辑，改为消费内核；页面空态与拒绝原因不回退。
- [ ] AC3：DevHUD 的错误/联机/Stats/Log/DiagTrace/日志 sink 六段经内核读取；HUD 仍只读，性能段仍由 HUD 自采样；场景组件 Stats 发现归内核。
- [ ] AC4：开发包/编辑器 Play 中 F8 打开控制台；内置命令与发现的无参 GM 命令可执行并回显结果/审计；带参命令明确拒绝；打开时输入被拦截、关闭/销毁后释放；F1–F7 不受影响。
- [ ] AC5：新增代码全部在开发三宏内；无新增 `public static` 可变状态、无判定路径读取、无 release 资产/类型残留。
- [ ] AC6：EditMode 新用例、L1、Doc 门禁通过；`unity command recompile` 0 错误；手工 Play 证据（截图/控制台输出）入施工记录。

## 风险与回滚

- 风险：
  - 内核取件时机（GameEntry 引导未完成时 DevHUD Start）导致 HUD 段集空转；用延迟绑定 + 空态守卫，并在 Play 验证。
  - HUD 六段改造引入读数回归；逐段用现有读口对照，保留段级异常隔离。
  - 控制台输入拦截/文本输入所有权泄漏，导致关闭后角色不能动或 HUD 过滤收不到字；用 `IDisposable` 租约 + OnDestroy/OnDisable 双释放，手工验证。
  - 反射执行命令在 IL2CPP 开发包不可用（AOT）；控制台对无参命令的 `Activator`/`MakeGenericMethod` 在 IL2CPP 下可能受限。若实测不可用，控制台回退为“列出命令 + 提示在编辑器执行”，并在进度记录登记；内核接口不变。
  - 修改 `.workflow/config.json` 会影响同仓另一个进行中的运行；本运行只追加验证命令，不改阶段/审批配置，并在收尾说明。
- 监控/发现：Unity Console 的 `[DebugKernel]`/`[DebugConsole]` 日志；控制台 `commands/audit` 自检；DevHUD 六段读数对照；L1/Doc/EditMode 门禁。
- 回滚：新增文件删除/还原，投影改动按文件回退；无数据迁移、无资产 GUID 依赖。若只保留内核而撤回控制台，删除 `DebugConsole.cs` 与 F8/文本所有权改动即可，内核与编辑器/HUD 收口不受影响。
