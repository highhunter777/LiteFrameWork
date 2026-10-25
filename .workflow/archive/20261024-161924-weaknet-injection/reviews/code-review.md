# 代码评审（独立子代理）：弱网注入：传输层损伤注入与可视化

> 运行 weaknet-injection · 创建于 2026-10-24
>
> 本文件由主代理根据独立子代理的返回整理落盘；不是主代理的自评。

## 变更范围

- 新增 `Assets/LiteNet/Diagnostics/WeakNet/*`（三宏守卫，纯 .NET 装饰器）与 `Assets/LiteGame/App/Debug/WeakNet/*`（runtime/预设/快照/曲线/装配/命令）。
- 接线：`ContainerModule`、`DebugKernel`、`BattleClient`、`DebugConsole`、`DevHudSectionSet`/`DevHudNetHistory`。
- 测试：`WeakNetTransportTests`（17）+ `WeakNetHeadlessRunTests`（LossOnly）+ `WeakNetWiringEditModeTests`（9）；`test.py --filter`/`nightly-gate.py` L3 步骤。
- 文档：路线图 §3.4、13 调试手册、GM 与调试线第 23 章、客户端审计。

## 正确性与边界

- 两轮代码评审问题已修：Connect/断线/Dispose 清队列；`FlushAll` 先摘队列再回调 + `_dispatching` 防重入；关闭注入只冲同方向队列；Seed=0 不重播种；重排随机窗口（Jitter=0 也生效）；预设表只读；testRoom `Detach`；HUD 无 active 隐藏。
- 最终独立复评：18/18 内核/对跑通过、lane-filter 4/4；无 blocker/major；AC1-AC8 可判。
- 边界如实：只客户端注入；可靠帧不模拟 UDP 丢包；组合信封未进 L3；HUD 运行期渲染 Play 冒烟待人工；本地测试房不包装。

## 测试充分性

- 覆盖：零注入 `RollCount=0`、固定种子、可靠 FIFO/零丢零重零乱序、不可靠上下行丢包/重复/乱序、延迟不早于 due、溢出有界、Connect/Dispose 清队、关闭中跳变冲刷、Seed=0 稳定、Send 同步拷贝、重入安全；EditMode 覆盖装配/写口/快照/曲线/剥离/静态守卫；Headless loss-only 用共享逻辑时钟。
- 最终复评：测试充分性 PASS；剩余 minor（同 seed 两实例决策未直比、组合信封无自动用例、HUD 渲染人工）已记录。

## 通用性与扩展性

- 通用性 PASS：内核只依赖 `IClientTransport` 与时钟委托，无 Unity/LiteGame 反向依赖；预设数据化；未来服务端/UDP 注入可复用 profile/PRNG。
- 扩展性 PASS：走既有工厂口；新增损伤需同步 profile/transport/快照/控制台帮助，边界已记录。

## 安全与性能

- 类型级三宏守卫；`LiteNet.csproj` 服务端不含类型；装配仅开发宏；release 无入口。
- 无判定路径读取；统计只读；队列有界（不可靠 1024）；无热路径周期 Warning；HUD 4Hz 采样。

## 发现的问题与处理

| 轮次 | 问题 | 处理 | 状态 |
|---|---|---|---|
| 方案 1-5 | 可靠帧丢包、类型残留、程序集循环、写口/确定性/HUD/filter | 修订方案 2-6 | 已修 |
| 代码 1 | 重连不清队、Jitter=0 乱序失效、Seed=0 重播、关闭越队、预设数组可写、testRoom 残留 | 逐项修 + 内核 16 例 + EditMode 8 例 | 已修 |
| 代码 2 | Flush 重入可致越界/重复、testRoom 未 Detach、缺入站/上行丢包与重入用例 | 摘队列再回调 + `_dispatching`；`Detach`；补 3 例（18/18、9/9） | 已修 |

## 复测记录

- 最终复评子代理：独立 Explore 子代理（2026-10-24，第三轮）
- 复评命令与结果：`dotnet test ... --filter FullyQualifiedName~WeakNet` → 18/18；`test_test_lane_filter` → 4/4；`gen-build-hash.py --check-shared` → `0a998c8ac6c9de89`（110 files）不变。
- 复评结论：PASS（0 blocker/major；minor 不阻塞）。

## 评审结论

- 评审方式: 独立子代理
- 结论: PASS
- 阻塞问题: 0
- 通用性: PASS
- 扩展性: PASS
- 评审人: 独立 Explore 子代理（第三轮）
- 评审时间: 2026-10-24
