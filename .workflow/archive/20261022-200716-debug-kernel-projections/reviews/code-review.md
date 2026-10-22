# 代码评审：统一内核与三投影最小闭环

> 运行 debug-kernel-projections · 创建于 2026-10-22T18:20:19+08:00

## 变更范围

- 对照任务与方案：T1–T7 全部完成；范围与 plan.md 的“内核 + 三投影最小闭环”一致，未新增 UI 调试面板。
- 变更文件/模块：
  - 内核：`App/Debug/DebugKernel.cs`、`DebugStatsDiscovery.cs`、`DebugConsoleCommand.cs`、`App/Bootstrap/ContainerModule.cs`、`App/GameEntry.cs`。
  - 投影：`DevTool/DevHUD.cs`、`Hud/DevHudSectionSet.cs`、`DevTool/DebugConsole.cs`、`Editor/Gm/GmCommandsPage.cs`、`Editor/Gm/GmNetDiagPage.cs`；旧 `Hud/DevHudStatsDiscovery.cs` 删除（unity-pipeline `delete_asset`）。
  - 输入：`Platform.Unity/Dev/DevHotkey.cs`（F12 + owner 文本输入）。
  - 测试/门禁：`Tests/EditMode/DebugKernelEditModeTests.cs`、`scripts/gate/unity-tests.py`。
  - 文档：`13-调试手册.md`、`14-客户端开发能力地图.md`、`GM与调试工具分层专项设计.md`、`施工进度/GM与调试线.md` ch14；UI 调试面重复稿改为归档重定向。
  - 外部门禁修复：`scripts/codegen/_uiform_row.py` 的 ROOT 改走 `_common.repo_root()`（该文件为工作区另一个进行中的 UI 工具改动，非本批产物）。

## 正确性与边界

- 主路径：`GameEntry` 引导完成 → `ContainerModule` 注册 `DebugKernel` → HUD/控制台/编辑器页取同一实例；命令执行统一 `TryExecute` → `CommandCenter`（GmOnly/拦截器/审计/撤销），选项统一 `TestModeOptions.Set`，请求桥统一 `TestModeRuntime.Request*`。
- 边界与错误路径：
  - 内核未装配 → HUD 空态、控制台提示、编辑器页 HelpBox；不影响启动。
  - 命令无参构造/注册/执行异常 → 返回错误文案，不抛到投影；审计仍由命令中心记录。
  - 带参命令 → `TryExecute` 明确拒绝“无无参构造（需专面板）”；控制台 `commands` 标注。
  - 控制台关闭/销毁 → `IDisposable` 输入拦截租约 + 文本输入所有权双释放；Play 实测 `BlockerCount 2→1`。
  - HUD 装配时机 → `TryBindKernel` 延迟绑定 + 空态守卫；场景加载重建、每轮 Prune 由内核负责。
- 回归风险：HUD 六段读数已对照（Stats 97 行、Log 10、Perf 2、LogSink 2；无对局时 Net/Diag 隐藏）；性能段未改；编辑器命令页 `cmds=5 audit=1`；控制台 F12 未占用 F8（UI 运行时调试目标）。

## 测试充分性

- 测试真正覆盖验收与失败路径：16 例覆盖命令发现/精确名/唯一后缀/带参拒绝/未知名、审计、选项已知/未知、请求桥读即清、无对局、读段发现、解析器空白/多空格/大小写；红证据为 16/16 `NotImplementedException`，实现后同用例转绿。
- 手工 Play 走真实 Input System 事件（`QueueStateEvent`/`QueueTextEvent`），不是直调控制台私有方法：F12 开合、`help/commands/options/option/run/audit/stats/log/battle`、输入拦截与释放、F8 不打开控制台。
- 不存在迎合实现的测试：用例只依赖公开内核接口；投影行为用真实操作路径补充，未以桩替代“可操作”结论。

## 通用性与扩展性

- 根因与复用边界：命令/选项/读段全部按发现面渲染，未硬编码命令清单、选项清单或统计模块名；控制台只做词法/输出，执行路径唯一。
- 扩展点：新增命令注册到 `CommandCenter` 自动出现在 `commands`/`run`；新增开关只改 `TestModeOptions.All`；新增读数在内核加方法/段名；新增投影可复用内核，不改动作实现。
- 硬编码/特判：F12 是热键常量、控制台动词是 UI 词表，不是业务分支；无页面 ID/命令类型 if-switch；`ReadSections` 是稳定段名表。
- 重复逻辑与过度抽象：删除 DevTool 的 Stats 扫描，迁入内核单份；没有第二套命令总线/读口；未为未来面板预建抽象。
- 相关场景复用与回归影响：DevHUD/编辑器页/控制台三处共用同一实例；产品 UI 生命周期、导航单写者、资源收集目录、判定路径未改。
- 命名、结构与复杂度：内核按读/写/发现分方法；控制台按动词分派，单文件约 400 行（dev-only 工具，未再拆）；HUD 宿主仍只持生命周期/输入/编排。

## 安全与性能

- 安全：`CommandCenter` 的 `GmOnly`/拦截器/审计语义原样保留；控制台不绕过权限门；输入拦截只以租约形式登记/释放；release 与测试者包三宏剥离；文档/产物不含密钥。
- 性能与资源：内核读模型不在每帧路径；HUD 仍 0.25s 轮询、隐藏即停；控制台仅打开时逐帧收字符，输出上限 200 行；命令执行/截图类只在显式操作；未新增 `public static` 可变状态；未进入 Sim/网络判定路径。

## 发现的问题与处理

| ID | 严重度 | 位置 | 问题 | 处理 | 状态 |
|---|---|---|---|---|---|
| C-1 | 中 | `DebugKernel.TryExecute` / `DebugConsole` | IL2CPP 开发包内 `MakeGenericMethod` 反射执行发现命令可能受 AOT 限制；本轮未出包实测 | 编辑器 Play 已验证；内置命令/读数不依赖反射；13 手册 §10.1 #10 与进度 ch14 登记；出包验证需用户授权 | 已知边界，非阻塞 |
| C-2 | 低 | `scripts/codegen/_uiform_row.py` | 工作区既有 UI 工具脚本违反 ROOT 单源，导致 `toolchain-tests` 红；非本批产物 | 一行改为 `_common.repo_root()`，单源测试转绿；建议提交时与该 UI 工具改动一起说明，不与内核批混淆 | 已处理 |
| C-3 | 低 | `.workflow/config.json` | 追加 `debug-kernel-editmode` 会影响同仓另一个进行中运行的 verify 命令集 | 只追加、不改阶段/审批；ch14 与 build-log 已说明 | 已知边界 |
| C-4 | 低 | `DebugKernel.GetSendMethod` | `GetMethod(nameof(Send))` 假定当前只有单一 `Send` 重载；未来新增重载会抛 `AmbiguousMatchException` | 异常被 `TryExecute` 捕获为错误文案，不会炸投影；届时改为按签名筛选即可 | 可接受 |

## 复测记录

- 修复后执行的命令与结果：
  - `python scripts/gate/unity-tests.py --mode editor --filter DebugKernelEditModeTests` → 16/16 pass。
  - `python .agents/skills/staged-delivery/scripts/workflow.py --run debug-kernel-projections verify` → PASS（L1 1819、Doc 0、L0/restore/toolchain/l2-meta 全绿、debug-kernel-editmode 16/16）。
- 验证证据：evidence/verify.json

## 评审结论

- 结论: PASS
- 阻塞问题: 0
- 通用性: PASS
- 扩展性: PASS
- 评审人: 代码评审代理
- 评审时间: 2026-10-22 20:05 +08:00
