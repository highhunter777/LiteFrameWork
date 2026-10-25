# 方案评审（独立子代理）：弱网注入：传输层损伤注入与可视化

> 运行 weaknet-injection · 创建于 2026-10-24
>
> 本文件由主代理根据独立子代理的返回整理落盘；不是主代理的自评。

## 评审输入

- 评审对象：plan.md（方案与验收标准），修订 1
- 代码/文档基线：HEAD `0574da90`；工作区另有 UI 线在途改动，与本方案无关
- 评审范围：落点与剥离、传输语义、写口接线、确定性、HUD、L3/夜间、验收可判定性
- 子代理标识：独立 Explore 子代理（方案评审，2026-10-24）
- 子代理约束：只读评审，不修改生产代码、测试与门禁

## 方案检查

- 目标可观察、范围与非目标清晰：成立
- 现状与根因有代码/配置/文档依据：基本属实；BuildHash 闭包表述不完整（P-8）
- 候选方案取舍充分：A/B/C 取舍成立
- 设计覆盖接口、数据、关键流程、失败与边界：可靠帧语义/写口接线未闭环（P-1/P-3）
- 通用性与扩展点明确：装饰器方向成立
- 测试策略能先失败后通过：L3 入口/HUD 窗口/静态守卫不成立（P-5/P-6）
- 风险、监控与回滚可执行：基本成立

## 验收标准可判定性

| 验收标准 | 判定方式 | 是否可观察/可判定 | 说明 |
|---|---|---|---|
| AC1 | 固定种子两次运行 | 否 | 无观测口；真实 UDP 不可复现 |
| AC2 | 零注入透传 | 部分 | 随机数消耗不可观测 |
| AC3 | 各损伤参数 | 部分 | 阈值未定；可靠帧未区分 |
| AC4 | 剥离守卫 | 部分 | 只查装配点，类型仍残留 |
| AC5 | HUD 曲线 | 否 | 40 样本标 60s |
| AC6 | 写口/静态 | 否 | 扫描无规则；tune 无审计 |
| AC7 | L3/夜间 | 否 | 无 filter、夜间无 L3、or 条件 |
| AC8 | 门禁 | 部分 | L3 定向不实 |

## 发现的问题与处理

| ID | 严重度 | 问题 | 处理 | 状态 |
|---|---|---|---|---|
| P-1 | blocker | 装饰器在 KCP 之上，丢可靠帧（Join/StartGame）不会重传；乱序可让 StartGame 先于 JoinAck | 丢包/重复/乱序仅作用于 unreliable；延迟/抖动两类；AC 加"可靠通道零丢失 + loss 下 Join/StartGame 成功" | 已修（修订 2） |
| P-2 | blocker | `LiteNet.csproj` 无条件编译 Diagnostics，release 类型残留；AC4 只查装配点 | 内核整目录三宏守卫；服务端 csproj 无宏 → 不含类型；LiteNet.Tests 定义 `LITEFRAMEWORK_DEBUG`；守卫测试 | 已修（修订 2） |
| P-3 | blocker | `ProcedureMatch` static 无 controller；HUD/运行中切换无数据源；TestModeOptions 需静态字段；tunable 只 float；命令语法/审计未闭环 | ContainerModule 建 `WeakNetRuntime` 实例、包装工厂；DebugKernel 注入并投影；不新增 TestModeOptions/静态可变；seed 不入 tunable；低频预设走 CommandCenter 审计，高频 tunable 豁免写明 | 已修（修订 2） |
| P-4 | major | 确定性口径模糊：单 PRNG 收发交错改变序列；AC 要求真实 UDP 逐决策一致不可判 | 方向独立 PRNG；AC1 限定假时钟+固定调用序列；真实对跑只断言推进/不发散/可重复跑；提供 trace/统计 | 已修（修订 2） |
| P-5 | major | `test.py` 无 `--filter`；nightly 仅 L1+L2；or 条件是逃生口 | test.py 增可选 `--filter`；nightly 增 L3 WeakNet 定向步骤；AC7 改为可回读 | 已修（修订 2） |
| P-6 | major | `DisciplineScanner` 无 public static 规则；HUD 40 样本标 60s | 新增专用反射守卫测试；HUD 改用 240 样本窗口降采样 40 列并标注真实窗口；AC6/AC5 相应改写 | 已修（修订 2） |
| P-7 | major | AC 缺重连清队列/Dispose | L3 用例与 AC3/AC6 补充 | 已修（修订 2） |
| P-8 | minor | BuildHash 闭包表述不完整 | 修正为 LiteSim/Core + LiteNet Proto/Protocol/Transport/Security + GameData/Generated | 已修（修订 2） |
| P-9 | minor | 本地服不包装导致 Room-Test 无注入，手动路径需说明 | 文档/使用说明写明：HUD 注入行仅在真 KCP 房显示 | 已修（修订 2） |

## 评审结论

（最终轮：第六轮）

- 评审方式: 独立子代理
- 结论: PASS
- 验收标准: PASS
- 阻塞问题: 0
- 评审人: 独立 Explore 子代理（第六轮）
- 评审时间: 2026-10-24
- 历史：首轮 FAIL（P-1/P-2/P-3）、第二轮 FAIL（R-1/R-2/R-3）、第三轮 FAIL（T-1/T-2）、第四轮 FAIL（V-1..V-4）、第五轮 FAIL（X-1..X-3）；各轮问题均已在修订 2-6 处理，记录见上。
- 第六轮 minor（已吸收）：对跑改 loss-only（延迟语义归内核用例）；nightly 显式 `--profile Nightly`；reorder 用固定种子+同 tick 多包；逻辑时钟在 `PumpAll/WaitFor` 每泵 +1ms。

## 复评记录（修订 2 → 修订 3）

- 复评子代理：独立 Explore 子代理（2026-10-24，第二轮）
- 复评输入：修订 2 的 `plan.md`、首轮问题清单、代码基线 `0574da90`
- 第二轮结论：FAIL；剩余 blocker 3、major 4、minor 1：
  | ID | 严重度 | 问题 | 修订 3 处理 |
  |---|---|---|---|
  | R-1 | blocker | 三宏不传播到被引用 dll：LiteNet.csproj 无宏 → 类型不存在，L3 编不过 | `LiteNet.Tests.csproj` **源码链接** WeakNet 目录 + 定义 `LITEFRAMEWORK_DEBUG`；LiteNet.csproj 保持无宏 |
  | R-2 | blocker | 装饰器构造依赖 LiteGame `WeakNetRuntime` → LiteNet↔LiteGame 循环 | 装饰器只收 `Func<WeakNetProfile>`；Runtime 留在 LiteGame 负责包装/统计 |
  | R-3 | blocker | 可靠帧仍吃抖动、队列溢出丢最旧会丢/乱序可靠帧 | 可靠帧固定延迟、单调 FIFO、不丢/不重复/不乱序；可靠队列独立，溢出只作用于不可靠 |
  | R-4 | major | `HeadlessClient` 固定 new KcpTransportClient，弱网变体未接线；`--filter WeakNet` 选不中 | 在 `StartHeadless` 处按标志包装；新增 `WeakNetHeadlessRunTests`（类名含 WeakNet） |
  | R-5 | major | HUD 投影不可测（DevTool 未被 EditModeTests 引用） | 文本格式化下沉到 App 层 `WeakNetSnapshot.AppendLines`；HUD 只调用；EditMode 测 App |
  | R-6 | major | AC2 随机数消耗不可观测；`--filter` 零匹配风险；AC4 仅文本守卫 | 统计增 `RollCount`；filter 用类名匹配；剥离守卫加源码链接/宏检查 |
  | R-7 | major | A1"整目录"会误伤 `DiagTrace.cs` | 只守 `Diagnostics/WeakNet/**` |
  | R-8 | minor | Send 同步拷贝契约未写；BuildHash 闭包表述漏 `SHARED_LINK_FILES` | 补契约与完整闭包表述 |
- 第三轮复评：FAIL；剩余 blocker 2、major 4：
  | ID | 严重度 | 问题 | 修订 4 处理 |
  |---|---|---|---|
  | T-1 | blocker | `HeadlessClient` 构造只收 sealed `KcpTransportClient`，装饰器无法接入 | 构造参数放宽为 `IClientTransport`；`StartHeadless` 包装 |
  | T-2 | blocker | `--filter` 不存在；`--filter WeakNet` 非合法 VSTest 表达式；AND 会漏内核用例 | filter 提供时**替换**默认分类表达式；用 `FullyQualifiedName~WeakNet` |
  | T-3 | major | HUD 曲线在 DevTool，EditModeTests 不可见 | AC5 拆分：App 层注入文本可测；曲线降采样以 Play 冒烟+人工验收并如实登记 |
  | T-4 | major | ContainerModule.Assemble 私有，装配无测试接缝 | 抽 App 层 `WeakNetWiring.WrapFactory` 纯函数单测 |
  | T-5 | major | 内核 `Log.Error` 无 LiteFramework 引用 | 改用 `DiagTrace` |
  | T-6 | major | 外层续发密文会被 SecureEnvelope `CanAccept` 拒绝；Reorder 在 Jitter=0 恒 0 | 缓存明文、到期重走 `inner.Send`；重排额外延迟最小 1ms |
- 第四轮复评：FAIL（无 blocker；major 4、新问题 6）：
  | ID | 严重度 | 问题 | 修订 5 处理 |
  |---|---|---|---|
  | V-1 | major | 曲线仅 Play 冒烟，无法门禁判定 | 曲线逻辑下沉 App 层 `NetHistory`（4Hz/240/40 列/真实窗口），EditMode 可测 |
  | V-2 | major | `HeadlessRunTests` 快跑无实时泵，延迟按 `nowMs` 不释放 | 对跑注入逻辑时钟（每 tick 16.7ms），断言输入被消费 |
  | V-3 | major | 重排 +1ms 若 FIFO/同 tick 释放不生效；可靠队列溢出策略未定义 | 按 due 升序稳定释放；可靠队列不丢，仅告警计数 |
  | V-4 | major | `WrapFactory` 单测不覆盖 ContainerModule 实际接线 | 加 ContainerModule 调用点文本守卫 |
  | V-5 | major | WeakNet 最外层吞 `OnError`；握手绕过注入；AppendLines 口径不一致 | 暴露并转发 `OnError`；写明握手不受注入；文本单一来源 Snapshot |
  | V-6 | major | filter 替换丢 Quarantine；nightly 日志仍 `L2-*`；`weak <preset>` 与无参 `TryExecute` 不接 | filter 自动追加 `Category!=Quarantine`；日志 `L3-*`；`weak` 直接调 runtime→CommandCenter 命令 |
- 第五轮复评：FAIL（无 blocker；major 3）：
  | ID | 严重度 | 问题 | 修订 6 处理 |
  |---|---|---|---|
  | X-1 | major | `BattleClient` 只订阅两个具体类型，WeakNet 在最外层时 `LastError` 恒空 | `WeakNetClientTransport` 暴露 `OnError`；`BattleClient` 开发宏内识别 WeakNet 分支订阅 |
  | X-2 | major | 逻辑时钟若只在 `RunFor` 推进，赛前 `WaitFor/PumpAll` 的可靠帧永不到期 | 共享逻辑时钟：`RunFor` 每 tick +16.7ms，`PumpAll/WaitFor` 每次泵 +1ms |
  | X-3 | major | 运行中调 latency 会让可靠帧 due 倒序 | 可靠队列严格 FIFO，`due=max(lastDue, now+latency)` |
  | X-4 | minor | 可见性/filter 括号/nightly 日志/reorder 测试 | 读口 public；filter 加括号；独立 `L3-*`；同 tick 多包放大重排 |
- 第六轮复评：进行中（见下）
