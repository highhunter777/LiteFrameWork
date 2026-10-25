# 实施任务清单：弱网注入：传输层损伤注入与可视化

> 本文件由 workflow.py 生成，请使用 `task` 子命令更新，勿手改。

## 任务清单

阶段：已收尾（待归档） ｜ 完成 10/10（取消 0，阻塞 0）

| ID | 任务 | 依赖 | 状态 | 验收标准 | 验证方式 | 证据 |
|---|---|---|---|---|---|---|
| T1 | 内核基础件（Profile/PRNG/Stats） | - | 完成 | WeakNetProfile 不可变且校验；WeakNetRandom 固定种子可复现；WeakNetStats 含 RollCount/队列/丢/重复/重排/溢出；三宏守卫只覆盖 WeakNet 目录 | dotnet build Tests/LiteNet.Tests/LiteNet.Tests.csproj | Profile/PRNG/Stats + 三宏守卫；dotnet build OK |
| T2 | 装饰器双队列与损伤语义 | T1 | 完成 | 可靠帧 FIFO/due 单调/零丢零重零乱序；不可靠帧延迟抖动丢重复乱序；Tick 先释放后驱动；Send 同步拷贝；OnError 转发；Dispose/重连清队列 | dotnet build Tests/LiteNet.Tests/LiteNet.Tests.csproj | 双队列/可靠 FIFO/不可靠损伤/Tick 序/OnError/Dispose；L3 10/10 |
| T3 | L3 内核用例 | T2 | 完成 | 固定种子可复现、零注入 RollCount=0、各损伤生效、可靠 FIFO、队列溢出、Dispose/重连、同步拷贝全绿 | python scripts/gate/test.py --lane L3 --filter FullyQualifiedName~WeakNet | WeakNetTransportTests 10/10 |
| T4 | App 层 runtime/预设/接线/快照/曲线 | T2 | 完成 | WeakNetRuntime 实例与 WrapFactory；预设表；Snapshot.AppendLines 单一文本；NetHistory 4Hz/240/40 列/真实窗口；IDebugTunableProvider 数值旋钮 | dotnet build Tests/LiteNet.Tests/LiteNet.Tests.csproj | Runtime/Presets/Wiring/Snapshot/NetHistory；EditMode 7/7 |
| T5 | 运行时接线（容器/内核/客户端/控制台） | T4 | 完成 | ContainerModule 只在开发宏且 testRoom=false 包装；DebugKernel 注入并暴露快照；BattleClient 订阅 WeakNet.OnError；weak 命令经 CommandCenter 审计 | python scripts/gate/unity-tests.py --mode editor --filter WeakNetWiringEditModeTests | ContainerModule/DebugKernel/BattleClient/DebugConsole 接线；Unity 0 error；EditMode 7/7 |
| T6 | HUD 接线 | T4 | 完成 | 联机段追加注入行（同一 Snapshot 文本）；DevHudNetHistory 改用 App 层 NetHistory（4Hz 节流、240 窗口、40 列降采样、真实窗口标签）；无对局隐藏 | python scripts/gate/unity-tests.py --mode editor --filter WeakNetWiringEditModeTests | HUD 注入行 + DevHudNetHistory 适配 NetHistory；Unity 0 error；渲染 Play 冒烟待人工 |
| T7 | EditMode 装配与守卫 | T5, T6 | 完成 | WrapFactory 纯函数、ContainerModule 调用点、命令/tunable、快照/曲线文本、三宏剥离守卫全绿 | python scripts/gate/unity-tests.py --mode editor --filter WeakNetWiringEditModeTests | WeakNetWiringEditModeTests 7/7 |
| T8 | Headless 固定种子弱网对跑 | T3 | 完成 | HeadlessClient 改收 IClientTransport；StartHeadless 按标志包装；共享逻辑时钟覆盖 RunFor/PumpAll/WaitFor；loss-only 对跑通过且输入被消费 | python scripts/gate/test.py --lane L3 --filter FullyQualifiedName~WeakNet | HeadlessClient 改 IClientTransport；WeakNet_LossOnly 通过；L3 11/11 |
| T9 | 门禁设施（filter 与夜间 L3） | T8 | 完成 | test.py --filter 替换默认表达式并追加 Quarantine 排除；nightly 增 L3 弱网步骤、独立 L3-* 日志、profile Nightly；可回读 | python scripts/gate/test.py --lane L3 --filter FullyQualifiedName~WeakNet; python -m unittest discover -s scripts/tests -p test_*.py -v | test.py --filter + nightly L3；lane filter 4/4；L3 gate 11 PASS |
| T10 | 文档回写与审计登记 | T7, T9 | 完成 | 路线图 §3.4 现行形态、13 调试手册使用说明、施工进度新章；HUD 采样债登记；剥离/手动验证边界如实记录 | python scripts/gate/doc-lint.py; python scripts/gate/test.py --lane Doc | 路线图 §3.4/13 手册/GM 线第23章/审计登记；doc-lint 0/0 |

## 任务详情

### T1 · 内核基础件（Profile/PRNG/Stats）

- 文件: Assets/LiteNet/Diagnostics/WeakNet, Tests/LiteNet.Tests/LiteNet.Tests.csproj

### T2 · 装饰器双队列与损伤语义

- 文件: Assets/LiteNet/Diagnostics/WeakNet

### T3 · L3 内核用例

- 文件: Tests/LiteNet.Tests/WeakNetTransportTests.cs

### T4 · App 层 runtime/预设/接线/快照/曲线

- 文件: Assets/LiteGame/App/Debug/WeakNet

### T5 · 运行时接线（容器/内核/客户端/控制台）

- 文件: Assets/LiteGame/App/Bootstrap/ContainerModule.cs, Assets/LiteGame/App/Debug/DebugKernel.cs, Assets/LiteClient/Runtime/Battle/BattleClient.cs, Assets/LiteGame/DevTool/DebugConsole.cs

### T6 · HUD 接线

- 文件: Assets/LiteGame/DevTool/Hud/DevHudSectionSet.cs, Assets/LiteGame/DevTool/Hud/DevHudNetHistory.cs

### T7 · EditMode 装配与守卫

- 文件: Assets/Tests/EditMode/WeakNetWiringEditModeTests.cs

### T8 · Headless 固定种子弱网对跑

- 文件: Tests/LiteNet.Tests/HeadlessClient.cs, Tests/LiteNet.Tests/WeakNetHeadlessRunTests.cs

### T9 · 门禁设施（filter 与夜间 L3）

- 文件: scripts/gate/test.py, scripts/gate/nightly-gate.py, scripts/tests

### T10 · 文档回写与审计登记

- 文件: Docs/设计文档/测试与性能/调试与开发工具链演进路线图.md, Docs/项目说明/13-调试手册.md, Docs/施工进度/GM与调试线.md, Docs/审计/代码质量审计-客户端.md

