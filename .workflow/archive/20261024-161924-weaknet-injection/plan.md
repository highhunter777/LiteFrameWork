# 方案：弱网注入：传输层损伤注入与可视化

> 运行 weaknet-injection · 创建于 2026-10-24 · 修订 6（吸收五轮独立评审）

## 背景与目标

- 现状：客户端传输走窄口 `IClientTransport`（`Assets/LiteNet/Transport/IClientTransport.cs:12-30`），实现 sealed `KcpTransportClient`；无损伤注入面。唯一"丢包模拟"是 `HeadlessRunTests.RunFor(skipEvery)` 的发送侧跳步（`Tests/LiteNet.Tests/HeadlessRunTests.cs:95-107`），无种子不可复现。HUD 已有 RTT/丢包/回滚/输入领先/快照年龄（`BattleDiagnostics.cs:44-96`、`DevHudSectionSet.cs:92-131`），但曲线只画最近 40 样本却标"60s"（`DevHudNetHistory.cs:44-70`），采样每帧而非 4Hz。
- 要解决的问题：无法稳定复现移动端弱网；无法验证预测/回滚/重传在丢包、延迟、抖动、乱序下的行为；路线图 T1 要求固定种子弱网场景可复现并有可视化（`调试与开发工具链演进路线图.md:76-80,219-225`）。
- 目标（可观察）：开发/编辑器环境按固定种子 profile 注入双向延迟（可靠+不可靠，可靠帧固定延迟不改序）与丢包/重复/乱序（仅不可靠）；固定调用序列下决策可复现；可靠信令（Join/StartGame）零丢失；HUD 显示注入配置与实测曲线；L3 固定种子弱网对跑可跑；release/测试者包类型级不编译注入面。

## 需求澄清与假设

- 需要确认的问题：
  1. 只客户端双向注入（本方案）；服务端另批。
  2. 可靠通道不模拟丢包/重复/乱序：装饰器在 KCP 之上，丢可靠帧不会触发重传（Join 只发一次，`RoomClient.cs:223`），会造成永久丢信令；真实 UDP 丢包重传属于 KCP 内部，下移注入点另批。可靠帧只做**固定延迟**（无抖动、单调释放）。
- 已确认答复：无（用户 2026-10-24 点单两线并行）。
- 采用假设：
  - A1：内核 `Assets/LiteNet/Diagnostics/WeakNet/**`，文件级 `#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG`。`LiteNet.csproj` 不定义三宏 → 服务端 dll 不含；Unity release 不含；`Tests/LiteNet.Tests.csproj` **源码链接**该目录并定义 `LITEFRAMEWORK_DEBUG` → L3 可编。只守 WeakNet 子目录，不动 `DiagTrace.cs`。该目录不在 BuildHash 闭包（闭包 = `LiteSim/Core/{Scripts,Systems}`、`LiteNet/{Proto,Protocol,Transport/Security}`、`GameData/Generated` + `SHARED_LINK_FILES`，`gen-build-hash.py:51-71`）。
  - A2：内核不引用 LiteGame：`WeakNetClientTransport(inner, Func<WeakNetProfile> profileProvider, Func<long> nowMs)`；`WeakNetRuntime`（LiteGame.App）负责 profile 持有、工厂包装、统计快照、`IDebugTunableProvider`。
  - A3：装配在 `ContainerModule`（`Assets/LiteGame/App/Bootstrap/ContainerModule.cs:72-99`，开发宏内）：创建 runtime，调用 App 层纯函数 `WeakNetWiring.WrapFactory(factory, runtime)` 包装 `AccountSessionFactory` 委托；`LocalServerTransport`（本地测试房）不包装；`ProcedureMatch` 不改签名。`WrapFactory` 独立可单测，避免依赖私有 `Assemble`/完整 `ClientContext`。
  - A4：读写单源：runtime 注册进容器并作为 provider；`DebugKernel` 构造注入并暴露只读 `WeakNetSnapshot`；HUD 读 `DebugKernel`。不新增 public static 可变；不新增 `TestModeOptions` 开关（关闭 = profile `none`）。
  - A5：预设表 `WeakNetPresets` 只读；低频预设切换走 `CommandCenter` 命令（审计），高频数值走 tunable（路线图 §4.3 第 6 条豁免并写明）；Seed 只由预设/命令设定（`DebugTunable` 仅 Bool/Float，`DebugTunable.cs:24-27`）。
  - A6：确定性口径＝固定假时钟 + 固定调用序列下逐决策一致；上行/下行独立 PRNG；真实 UDP 对跑只断言"跑完、不发散、可重复跑"。
  - A7：验收设施：`test.py` 增可选 `--filter`——**提供时替换该 lane 的默认分类表达式**（空 = 现行为），并追加 `&(Category!=Quarantine)` 保留隔离语义；`nightly-gate.py` 增 L3 弱网定向步骤（`--lane L3 --profile Nightly --filter "FullyQualifiedName~WeakNet&(Category!=Quarantine)"`，写入**独立** `L3-*` 日志，按现有 lane 结果记录）。不改默认 lane 口径。
- 假设影响与风险：A1 通过"测试工程源码链接"绕开宏不传播（子项目宏不会传播到被引用 dll）；A2 避免 LiteNet→LiteGame 循环依赖；A6 避免伪确定性验收。

## 范围与非目标

- 范围内：
  1. 内核 `Assets/LiteNet/Diagnostics/WeakNet/`：
     - `WeakNetProfile`（readonly struct；`Enabled/LatencyMs/JitterMs/LossPermille/DuplicatePermille/ReorderPermille/Seed`，`None`）。
     - `WeakNetRandom`（确定性 PRNG；上下行各一）。
     - `WeakNetClientTransport : IClientTransport`：**可靠帧**固定延迟、单调释放、不丢/不重复/不乱序；**不可靠帧**延迟/抖动/丢/重复/乱序；队列分可靠/不可靠，溢出只丢不可靠最旧；只读统计（含 `RollCount`）；Dispose/断线清队列；事件透传；Send 同步拷贝。
  2. `WeakNetPresets`（只读）与 `WeakNetRuntime`（`Assets/LiteGame/App/Debug/WeakNet/`，开发宏内）：profile、包装工厂、统计快照、tunable provider、`AppendLines` 文本格式化（App 层可测）。
  3. 装配：`ContainerModule` 包装真 KCP 工厂；`DebugKernel` 注入 runtime 并暴露 `WeakNetSnapshot`。
  4. 写口：`SetWeakNetPresetCommand`（`CommandCenter`，审计）；`DebugConsole` 增 `weak <preset>` 转发命令；数值 tunable（latency/jitter/loss/dup/reorder）。
  5. 读数：`WeakNetSnapshot.AppendLines` 为唯一注入文本来源；RTT/丢包曲线逻辑下沉 App 层 `NetHistory`（纯逻辑：4Hz 节流 + 240 环 + 40 列降采样 + 标注真实窗口），`DevTool` 只做渲染与喂样；EditMode 可测。
  5b. 命令：`DebugConsole` 的 `weak <preset>` 动词直接调 `DebugKernel.TrySetWeakNetPreset` → 内部 `CommandCenter.Send(new SetWeakNetPresetCommand(preset))`（有审计）；不走无参 `TryExecute`。
  6. 测试：L3 内核用例；`WeakNetHeadlessRunTests`（类名含 WeakNet，filter 可选中；在 `StartHeadless` 处包装传输）；EditMode 装配/写口/快照文本/剥离守卫；`test.py --filter`；nightly L3 弱网。
  7. 文档：路线图 §3.4 现行形态、`13-调试手册.md`、`Docs/施工进度/GM与调试线.md` 新章。
- 非目标：
  - 不做服务端/云侧注入；不做 KCP 以下 UDP 层注入（可靠 UDP 丢包模拟另批）。
  - 不做带宽限制/场景 DSL/录制回放；不改 `RoomServer/Runtime`（R11）；不改 `Proto/Protocol/Transport/Security`（BuildHash 闭包）。
  - 不做 release/测试者包入口；不替换现有心跳 RTT/丢包读数。

## 现状与约束

- 相关代码/模块：
  - `IClientTransport.cs:12-30`（含 reliable 标志与"Send 同步拷贝"约定）；`KcpTransportClient.cs:11-63`；`SecureEnvelopeClientTransport.cs:8-60`（装饰器范式）。
  - `ContainerModule.cs:72-99`；`ProcedureMatch.cs:163-203`；`AccountSessionFactory.cs:64-67`。
  - `DebugKernel.cs:20-120`；`DebugTunable.cs:24-47`；`DebugTunableDiscovery.cs`；`DebugConsole.cs:210-300`；`CommandCenter.cs`（审计环）。
  - `BattleDiagnostics.cs:44-96`；`DevHudSectionSet.cs:92-131`；`DevHudNetHistory.cs:13-70`。
  - `Tests/LiteNet.Tests/LiteNet.Tests.csproj`（net8.0/xunit，未定义三宏）；`HeadlessRunTests.cs:26-107`；`HeadlessClient.cs` 接收 transport 参数。
  - `scripts/gate/test.py:32-35,293-306`（无 `--filter`）；`nightly-gate.py:66-118`（L1+L2）。
- 约束：
  - 红线 6（无新增 public static 可变）；红线 1（BuildHash 闭包不动）；红线 9/10（HUD 只投影，读口单源）；红线 8（无热路径周期 Warning）。
  - 剥离：类型级三宏 + 装配点开发宏 + release 无入口。
  - 审计：顺带修 HUD 曲线窗口/采样节流（新登记轻量）；`DebugTunable` 仅 Float 为既有边界。
  - 与 UI P2-3a 并行：不改 Editor/Style/toolchain.json；Unity 编译/EditMode 排队；L3 纯 .NET 不占 Editor。

## 候选方案与取舍

| 方案 | 核心思路 | 优点 | 代价/风险 | 结论 |
|---|---|---|---|---|
| A | LiteNet/Diagnostics 内核（三宏）+ 测试工程源码链接 + ContainerModule 包装 + DebugKernel 投影；可靠帧只延迟 | 类型级剥离、L3 可测、可信令不丢、零静态可变 | 可靠 UDP 丢包不模拟（另批）；需 test.py 加 filter | **采用** |
| B | 改 KcpTransportClient/kcp2k 做 UDP 层注入 | 最真实 | 碰传输库/两端、BuildHash 风险、范围大 | 拒绝（后续批） |
| C | 仅测试假传输 | 零生产影响 | 无法 Editor/Player 手动复现，无 HUD | 拒绝 |

推荐 A。

## 设计要点

- 接口/数据：
  - `WeakNetProfile`（immutable）。
  - `WeakNetClientTransport(inner, Func<WeakNetProfile> profileProvider, Func<long> nowMs)`（不引用 LiteGame）。
  - 可靠帧：可靠队列**严格 FIFO**；`due = max(lastDue, now + LatencyMs)`（运行中调 latency 也不会后发先至）；`Jitter/Loss/Duplicate/Reorder` 不作用于可靠帧。
  - 不可靠帧：完整注入；`LossPermille` 丢弃；`DuplicatePermille` 复制；`ReorderPermille` 追加 `1 + U(0, max(1, 2*Jitter))` 延迟（Jitter=0 时也有 ≥1ms 重排）；`Jitter` 均匀分布。
  - 队列：可靠队列独立且**不丢弃**（超过告警阈值只记 `DiagTrace` 一次 + 计数，队列按 KCP 窗口自然有界）；不可靠队列上限 1024，溢出丢最旧。
  - 错误透传：`WeakNetClientTransport` 暴露 `OnError` 并转发内层错误（内层为 `SecureEnvelopeClientTransport`/`KcpTransportClient` 时按既有范式订阅）；`BattleClient` 在开发宏内识别最外层为 `WeakNetClientTransport` 时订阅其 `OnError`（与现有两个具体类型分支并列），不吞 `LastError`。
  - 握手/连接建立（SecureEnvelope 的握手）发生在内层，**不经过 WeakNet 注入**；注入只作用于业务包。
  - Tick 序：`TickOutgoing` = 先按 **due 升序**释放到期上行包（调用 `inner.Send`）→ 再 `inner.TickOutgoing`；`TickIncoming` = 先 `inner.TickIncoming`（新包入队）→ 再按 **due 升序**释放到期下行包给上层。同 due 保持入队顺序（稳定）。
  - 包装顺序：Kcp → SecureEnvelope → WeakNet（最外层）。上行延迟包缓存**明文**（同步拷贝），到期时重走 `inner.Send`（由信封重新加密，不复用密文）；下行在内层解密后到达，延迟/重复只在内存释放；可靠帧只延迟一次、不重复。
  - `WeakNetRuntime`：实例；`Wrap(inner, nowMs)`；`TakeSnapshot()`；`CollectDebugTunables()`；`AppendLines(StringBuilder)`。`WeakNetSnapshot`/`NetHistory` 为 App 命名空间 public（调试面读口，DevTool/EditMode 共用）。
  - `DebugKernel`：构造加 `WeakNetRuntime weakNet = null`；`WeakNetSnapshot WeakNet => _weakNet?.TakeSnapshot()`；注册 `SetWeakNetPresetCommand`。
- 关键流程：
  1. ContainerModule 建 runtime → `factory = testRoom => ProcedureMatch.CreateTransportForFactory(...)`；开发宏内且 `testRoom == false` 时 `weakNet.Wrap(factory(testRoom))`。
  2. 决策：上下行独立 PRNG；`Enabled=false` 不消耗随机数、不排队、直通。
  3. Seed=0：装配期生成一次并记录；固定 seed：可复现。
  4. 运行中切换：`weak <preset>`（审计）或 tunable；注入器每包读 provider。
- 失败与边界：未连接/Dispose 后 Send 不抛；非法 profile 写口拒绝；队列溢出只丢不可靠并计数；可靠帧零丢失/零重复/零乱序。
- 确定性：固定假时钟+固定调用序列逐决策一致；方向独立 PRNG；`RollCount` 可观测；真实对跑只断言推进/不发散/可重复跑。

## 通用性与扩展性

- 根因：网络质量靠真实链路碰运气，缺少可控损伤源。
- 复用：`IClientTransport` 窄口、SecureEnvelope 装饰器范式、`DebugKernel` 调参/命令/审计、`DevHudNetHistory`。
- 避免硬编码：profile/预设表数据驱动；不写死地址/端口/房间；HUD 只读 runtime 快照；未来服务端注入复用 profile/PRNG。

## 测试策略

- L3 xunit（`Tests/LiteNet.Tests/WeakNetTransportTests.cs`，假 inner + 假时钟）：
  1. `Enabled=false` 透传、统计零、`RollCount=0`。
  2. 固定种子固定调用序列：两次决策一致；换种子不同；上下行互不影响。
  3. 可靠帧：严格 FIFO、due 单调、零丢/零重复/零乱序；不可靠帧各损伤生效（乱序用例同 tick 发多包放大 1ms 重排，断言后发先至）。
  4. 队列上限只丢不可靠最旧；Dispose/重连清队列；事件退订。
  5. Send 同步拷贝（调用方改缓冲不影响已发内容）。
- 对跑（`WeakNetHeadlessRunTests`）：把 `HeadlessClient` 构造参数从 sealed `KcpTransportClient` 放宽为 `IClientTransport`（测试夹具改动），在 `StartHeadless` 处按标志包装；**对跑用 loss-only**（上行延迟会让输入帧到服务端时过期，延迟语义由内核用例覆盖），固定种子丢包，注入**共享逻辑时钟**：`RunFor` 每 tick 推进 16.7ms，`PumpAll`/`WaitFor` 的每次泵也推进 1ms（保证可靠帧在赛前等待中到期）；断言 Join/StartGame 成功、帧号推进、输入被消费、不发散、重复跑稳定；类名含 `WeakNet` 供 `--filter` 选中。信封组合（SecureEnvelope+WeakNet+Kcp）用假信封或云测手动覆盖，不在本批 L3 强制。
- EditMode（`WeakNetWiringEditModeTests`）：`WeakNetWiring.WrapFactory` 纯函数（真 KCP 包装/本地服不包装）+ ContainerModule 调用点文本守卫；命令与 tunable 写口；`WeakNetSnapshot.AppendLines` 文本；`NetHistory` 4Hz/240/40 列/真实窗口；三宏剥离守卫（服务端 csproj 无宏；LiteNet.Tests 源码链接 + 宏；仅 WeakNet 子目录）。
- 基础设施：`test.py --filter`（可选）；`nightly-gate.py` 增 L3 `WeakNet` 定向并回读日志。
- 先失败后通过：先落内核/装配测试（骨架缺失），断言失败；再实现。

## 验收标准

- [ ] AC1：固定假时钟+固定调用序列、同 profile+seed 两次决策序列逐项一致；换种子不同；真实 UDP 对跑可重复跑（不要求逐决策一致）。
- [ ] AC2：`Enabled=false` 透传、统计零、`RollCount=0`；与未包装行为一致。
- [ ] AC3：不可靠帧延迟/抖动/丢包/重复/乱序按参数生效；可靠帧零丢失/零重复/零乱序、**严格 FIFO 且 due 单调**（运行中调 latency 也不倒序）；loss 下 Join/StartGame 成功；溢出只丢不可靠并计数。
- [ ] AC4：装配只在开发三宏且只包真 KCP；`LiteNet.csproj`/服务端产物不含类型；release 不可达（守卫测试 + 源码链接/宏检查）。
- [ ] AC5：`WeakNetSnapshot.AppendLines` 与 App 层 `NetHistory` 可测（EditMode）：注入配置/统计文本、4Hz 节流、240 样本≈60s 窗口、40 列降采样与真实窗口标签；HUD 联机段调用同一文本/记录器，无对局隐藏（Play 冒烟确认渲染）。
- [ ] AC6：`weak <preset>` 经 `CommandCenter` 审计；数值 tunable 可读写；无新增 public static 可变（专用反射守卫）。
- [ ] AC7：`WeakNetHeadlessRunTests`（loss-only）通过（`HeadlessClient` 改收 `IClientTransport` + 共享逻辑时钟覆盖 `RunFor/PumpAll/WaitFor`；断言输入被消费）；`test.py --lane L3 --filter "FullyQualifiedName~WeakNet&(Category!=Quarantine)"` 可定向跑（filter 替换默认分类表达式）；`nightly-gate.py` 以 `--profile Nightly` 跑 L3 弱网步骤并可回读独立 `L3-*` 日志与 lane 结果。
- [ ] AC8：BuildHash `--check-shared` 不变；L1、EditMode 定向、L3 定向、doc-lint、toolchain-check 全绿。

## 风险与回滚

- 风险：时序/队列 bug（上限/生命周期用例/统计）；调用序列影响决策（方向 PRNG + trace + 固定序列用例）；装配遗漏/误包本地服（EditMode + 文本守卫）；HUD 修改影响读数（保留口径只改节流/显示窗口 + Play 冒烟）；夜间时长（定向 filter + 超时）；与 UI 抢 Editor（L3 纯 .NET，Unity 排队）。
- 监控：L3 用例、HUD 注入行、统计、`test.py --lane L3 --filter WeakNet`、nightly 日志。
- 回滚：ContainerModule 去掉包装即恢复；内核目录整删；无资产/协议/生成物变更，零迁移。
