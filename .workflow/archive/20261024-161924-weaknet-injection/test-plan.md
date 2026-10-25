# 测试计划：弱网注入：传输层损伤注入与可视化

> 运行 weaknet-injection · 创建于 2026-10-24

## 测试策略

- L3 xunit（`Tests/LiteNet.Tests/WeakNetTransportTests.cs`）：假 inner transport + 可推进时钟，覆盖内核确定性、可靠/不可靠语义、队列与生命周期。
- L3 对跑（`WeakNetHeadlessRunTests`，施工阶段）：真 UDP/KCP + 固定种子 loss-only + 共享逻辑时钟，覆盖 Join/StartGame 与输入消费。
- EditMode（`WeakNetWiringEditModeTests`，施工阶段）：装配（只包真 KCP）、命令/tunable、App 层快照/曲线文本、三宏剥离守卫。
- AC 映射：AC1/AC2/AC3 → 内核用例；AC4/AC6 → EditMode 装配/守卫；AC5 → App 层文本/曲线；AC7 → 对跑 + filter/nightly；AC8 → 全量门禁。

## 用例清单

1. 关闭注入 → 透传、`RollCount=0`（AC2）。
2. 固定种子序列一致、换种子不同（AC1）。
3. 可靠帧零丢/零重复/零乱序且 FIFO（AC3）。
4. 不可靠丢包/重复/乱序生效；乱序断言后发先至（AC3）。
5. 下行延迟不早于 due（AC3）。
6. 队列溢出只丢不可靠最旧且队列有界（AC3）。
7. Connect/Dispose 清队列；关闭注入先冲刷队列再直发（AC3）。
8. Send 同步拷贝；Seed=0 跨包稳定（AC1/AC3）。
9. 运行中改 latency 可靠帧仍保序（AC3）。
10. 对跑：共享逻辑时钟推进、Join/StartGame 成功、客户端预测/输入释放/丢包生效（AC7）。
11. 装配/写口/HUD 文本/曲线/剥离守卫（AC4/AC5/AC6）。

## 预期失败证据

- `wf verify --red` → `evidence/red.json`：`weaknet-red` exit=1（7166 ms）。
- 骨架阶段：9 failed / 1 passed（Dispose 空实现碰巧通过）；编译成功证明源码链接 + `LITEFRAMEWORK_DEBUG` 方案可行。
- 代码评审补测后：内核 **18/18**、EditMode **9/9**。
- 红因：`Send` 未转发、队列/延迟/统计未实现，断言失败；非导入/环境故障。

## 运行方式

```bash
dotnet test Tests/LiteNet.Tests/LiteNet.Tests.csproj --filter "FullyQualifiedName~WeakNet" --nologo
python scripts/gate/test.py --lane L3 --filter "FullyQualifiedName~WeakNet&(Category!=Quarantine)"
python scripts/gate/unity-tests.py --mode editor --filter WeakNetWiringEditModeTests
```
