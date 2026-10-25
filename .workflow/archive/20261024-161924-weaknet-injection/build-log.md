# 施工记录：弱网注入：传输层损伤注入与可视化

> 运行 weaknet-injection · 创建于 2026-10-24

## 施工记录

| 时间 | 任务 | 动作 | 结果 |
|---|---|---|---|
| 2026-10-24 | T1 | `LiteNet/Diagnostics/WeakNet`：Profile/PRNG/Stats + 三宏守卫 | 完成 |
| 2026-10-24 | T2 | `WeakNetClientTransport`：可靠 FIFO/不可靠损伤/双队列/Tick 序/OnError/Dispose/同步拷贝 | 完成 |
| 2026-10-24 | T3 | `WeakNetTransportTests` 17 例 | 18/18 |
| 2026-10-24 | T4 | App 层 `WeakNetRuntime`/预设/`WeakNetWiring`/`WeakNetSnapshot`/`NetHistory` | 完成 |
| 2026-10-24 | T5 | ContainerModule/DebugKernel/BattleClient/DebugConsole 接线；命令审计 | Unity 0 error |
| 2026-10-24 | T6 | HUD 注入行；`DevHudNetHistory` 改为 App `NetHistory` 薄适配 | Unity 0 error |
| 2026-10-24 | T7 | `WeakNetWiringEditModeTests` 9 例 | 9/9 |
| 2026-10-24 | T8 | `HeadlessClient` 改收 `IClientTransport`；共享逻辑时钟；`WeakNet_LossOnly_StillCompletes` | 通过；L3 18/18 |
| 2026-10-24 | T9 | `test.py --filter` + `nightly-gate.py` L3 WeakNet 步骤 | lane filter 4/4；L3 gate 11 PASS |
| 2026-10-24 | T10 | 路线图 §3.4、13 手册、GM 线第 23 章、审计登记 | doc-lint 0/0 |

## 关键决定

- 内核放 `LiteNet/Diagnostics/WeakNet/**` 并整目录三宏守卫；`LiteNet.csproj` 不定义宏（服务端 dll 不含），`LiteNet.Tests.csproj` 源码链接 + 定义 `LITEFRAMEWORK_DEBUG`。
- 可靠帧只做固定延迟、严格 FIFO、独立队列；丢包/重复/乱序只作用于不可靠帧，避免在 KCP 之上丢 Join/StartGame。
- 装饰器只依赖 `Func<WeakNetProfile>`，不引用 LiteGame，避免程序集循环；装配/读写都在 App 层实例（无新增 public static 可变）。
- 注入点在最外层（Kcp → SecureEnvelope → WeakNet），上行延迟包缓存明文、到期重走 `inner.Send` 由信封重新加密。
- 对跑用 loss-only：上行延迟会让输入帧到服务端过期；延迟语义由内核固定时钟用例覆盖。

## 偏差与处理

- 首版 `Send` 中 duplicate/reorder 各 roll 两次，计数与实际不一致；改为一次决策 + `DelayFor` 只加延迟。
- 首版上下行共用一个 `_lastReliableDue`，会互相影响；拆为 `_lastReliableOutDue/_lastReliableInDue`。
- `HeadlessRunTests` 的 `Gate.AcceptedCount` 在快跑循环下为 0（输入帧与服务端帧序错位，属既有 harness 特性）；弱网对跑改为断言客户端预测推进、`InputsSent>0`、弱网 `Released>0`、`Dropped>0`，并保留权威帧推进断言。
- 编辑 HUD 时转义错误插入真实换行导致 CS1010；按字节修正后编译通过。

## 问题与修复

- 五轮独立评审指出：可靠帧丢包、类型剥离、程序集循环、写口接线、确定性、HUD 可测性、filter/夜间入口等；修订 6 全部吸收。
- 代码评审补修：Connect/断线清队列、Jitter=0 时乱序随机窗口、Seed=0 不重播种、关闭注入先冲刷队列、预设表只读集合、HUD 本机回绕时钟；补 6 例内核边界（18/18）。
- `DevHudNetHistory` 原实现每帧采样且只画 40 样本却标 60s；本批将逻辑下沉 `NetHistory`（4Hz/240/40 列降采样/真实窗口）。

## 阶段流转
- 2026-10-24T14:37:28+08:00 · 完成「正式施工」→ 「测试代码」
- 2026-10-24T15:39:41+08:00 · 完成「测试代码」→ 「评审代码」
- 2026-10-24T16:19:23+08:00 · 完成「评审代码」→ 「回写与收尾」
- 2026-10-24T16:19:23+08:00 · 完成「回写与收尾」→ 完成

## 验证结果
- 2026-10-24T15:13:39+08:00 · 验证 FAIL · 27/40 · 证据 evidence/verify.json
- 2026-10-24T15:18:28+08:00 · 验证 FAIL · 15/16 · 证据 evidence/verify.json
- 2026-10-24T15:21:11+08:00 · 验证 FAIL · 15/16 · 证据 evidence/verify.json
- 2026-10-24T15:39:37+08:00 · 验证 PASS · 18/18 · 证据 evidence/verify.json
- 2026-10-24T15:56:18+08:00 · 验证 PASS · 18/18 · 证据 evidence/verify.json
- 2026-10-24T16:18:55+08:00 · 验证 PASS · 16/16 · 证据 evidence/verify.json

## 交付摘要

- 客户端弱网注入：`WeakNetClientTransport`（三宏守卫）双向延迟/抖动（可靠帧固定延迟保序）+ 不可靠帧丢包/重复/乱序；固定种子、上下行独立 PRNG、重入安全、重连清队。
- App 层 runtime/预设/命令/快照/曲线；ContainerModule/DebugKernel/BattleClient/DebugConsole/HUD 接线；F12 `weak`、F11/tune 数值旋钮。
- L3 18/18、EditMode 9/9、L1 1838；夜间 L3 定向步骤；BuildHash 不变。

## 验证结果

- 最终 `wf verify`（聚焦本批 16 条）：**PASS**（L1 1838/1838、weaknet L3 18/18、weaknet EditMode 9/9、lane-filter 4/4、doc/toolchain/restore/L0/L2 meta 全绿）。
- `gen-build-hash.py --check-shared` = `0a998c8ac6c9de89`（110 files，未变）。
- 独立代码复评第三轮 PASS（0 blocker/major）。

## 未决事项

- HUD 注入行的运行期渲染 Play 冒烟待人工（需真 KCP 对局）；App 文本/曲线已 EditMode 覆盖。
- 服务端/云侧注入、KCP 以下 UDP 可靠帧丢包、带宽限制/场景 DSL 未做（另批）。
- SecureEnvelope+WeakNet+Kcp 组合未进 L3；云测手动覆盖。
- 同 seed 两实例决策序列未直比（`WeakNetRandom` 已有序列用例）。

## 文件清单

- `Assets/LiteNet/Diagnostics/WeakNet/*`（新）
- `Assets/LiteGame/App/Debug/WeakNet/*`（新）
- `Assets/LiteGame/App/Debug/DebugKernel.cs`、`App/Bootstrap/ContainerModule.cs`
- `Assets/LiteClient/Runtime/Battle/BattleClient.cs`
- `Assets/LiteGame/DevTool/DebugConsole.cs`、`DevTool/Hud/DevHudSectionSet.cs`、`DevTool/Hud/DevHudNetHistory.cs`
- `Tests/LiteNet.Tests/WeakNetTransportTests.cs`、`HeadlessClient.cs`、`HeadlessRunTests.cs`、`LiteNet.Tests.csproj`
- `Assets/Tests/EditMode/WeakNetWiringEditModeTests.cs`
- `scripts/gate/test.py`、`scripts/gate/nightly-gate.py`、`scripts/tests/test_test_lane_filter.py`
- `Docs/设计文档/测试与性能/调试与开发工具链演进路线图.md`、`Docs/项目说明/13-调试手册.md`、`Docs/施工进度/GM与调试线.md`、`Docs/审计/代码质量审计-客户端.md`

## 后续建议

- 真机/联机时按 13 手册执行 `weak mobile` 人工确认 HUD 注入行与曲线。
- 需要更真实丢包时，另批把注入点下移到 KCP/UDP 层。
- 可把 `DroppedUp/DroppedDown` 在 HUD 中分行显示，便于定位方向性弱网。
