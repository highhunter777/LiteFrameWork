# 方案评审：安卓平台适配（android-adaptation）

## 评审输入

- 评审对象：`.workflow/active/android-adaptation/plan.md`
- 代码基线：git HEAD=`c8adb9df030ba4dafecb7290138b3ce214bca39a`（工作区另有 GM/DevHUD 并行线 WIP 约 18 文件——含 `ProcedureBattle.cs`/`Boot.unity`/`ClientHost.cs`——与未跟踪 `.workflow/active/` 目录；评审只针对方案文本与仓库事实的一致性）
- 评审方式: 独立子代理
- 轮次形态：每轮新会话复评，主代理不参与评审结论、只按子代理返回落盘。
- 子代理标识与轮次结论：
  - 第一轮：explore（task-8538fe9e-3a5e-4a39-9989-292250b2ca7c），2026-10-26，判定 FAIL
  - 第二轮：explore（task-2d90c479-3983-44ff-8737-3453f1b0a1e8），2026-10-26，判定 FAIL
  - 第三轮：explore（task-2d5dffd8-1a7f-44a7-8f50-62da5d369ee6），2026-10-26，判定 PASS（附一项 major 文本修订）
- 评审清单：staged-delivery `references/artifact-quality.md`「方案评审」节（目标可观察 / 现状与根因有据 / 取舍充分 / 设计覆盖失败与边界 / 通用性与扩展点 / 测试策略先失败后通过 / 风险与回滚可执行 / 验收标准逐条可判定）
- **过程留痕（如实记录）**：首轮报告到达前，一条 `[agent-auto]` 先行摘要误报"PASS with some concerns"，主代理据摘要先落盘了错误 PASS 评审记录；随后 bg_task_done 全文到达，实际判定为 FAIL（两个 blocker 被摘要吞掉）——评审记录已按全文推倒重写，该教训已入记忆（子代理评审结论以 bg_task_done 全文为准）。

## 方案检查

- **第一轮（方案 v2）——判定 FAIL**：现状事实抽查全部属实（构建链三处硬编码、DeployBake 安卓失效注释、Player.inputactions/生成类、出包档、SimMath/SimTrig/IeeeProbe、AppLifetime、libxlua.so ABI、ProjectSettings、BuiltinBundleBuildInvoker、player-smoke、PointerLockService、IIntentSource 扩展点口径）；但发现 2 blocker＋4 major＋3 minor（见下表），验收标准 4 条判定不足。
- **第二轮（方案 v3）——判定 FAIL**：首轮 B2/B3/B5/B6/m1~m3 与全部建议核验**已正确落笔**且与仓库事实一致；但修订引入 1 个新 blocker（预载步 await 与 `GameEntry` 引导结构冲突）＋1 个 major（`LookDeltaOf` 工厂硬绑 `NewInputIntentSource`，方案未定触屏 delta 契约）＋4 minor。
- **第三轮（方案 v4→v5）——判定 PASS**：二轮全部修复核验通过（行级事实逐一对上：`ContainerModule.cs:94`/`ContentModule.cs:45`/`MetaDeployConfig.cs:112`、`build-player.py:113/170/219/268`、`toolchain.py:440`、`BattleViewRig.cs:436-464/890-895`、`IIntentSource.cs`、`IAimWorldSink`〔L51〕、`player-smoke.py:107`、`HealthEndpoints.cs:32`、`channel: development`）；新设计（同步读 WaitForCompletion＋`ILookDeltaSource`）方向正确、不触碰引导链；附 1 项 major（接口落位与 L1 源链接集冲突）＋5 minor——均已在 v5 以文本修订闭合（见下表"三轮"列），按"复评仅剩非 blocker 问题时修复落盘即收口"规则不再开第四轮。

## 验收标准可判定性

第三轮逐条判定（8/8 可观察、可判定，无豁免、无"实现批定"开口）：

| AC | 判定 | 判定方式 |
|---|---|---|
| AC1 构建链贯通 | PASS | 构建命令＋`Builds/android/Test.apk` 点名＋`lib/arm64-v8a` 解包断言＋`toolchain.py check`＋Windows 回归命令点名＋预检①顺序断言 |
| AC2 模拟器启动（含前置预检） | PASS | 预检两段式（abilist 快检＋首个产物即探针）＋`player-smoke.py --platform android --adb-serial` PASSED＋真标记字面量（`[Asset] ready`/`BootstrapError`，与 `player-smoke.py:107` 同源）＋会话日志工件固化 |
| AC3 安卓烙入生效 | PASS | `meta.url` 实际值（日志/GM 页）＋`/live` HTTP 200 含 `live`（`HealthEndpoints.cs:32`）＋游客登录＋无烙入 fail-closed 回归 |
| AC4.1~4.7 触屏逐项 | PASS | 每项操作＋期望观测量；AC4.2 含模态/死亡段不转镜头（一轮 B2 回归项）；AC4.4 已删"观感"改 FOV/优先级日志或 GM 读数；AC4.5 判换弹完成态 |
| AC5 联测一局 | PASS | 结算可查通道点名（`/matches/result`/`/matches`）＋切后台≥30s 回前台 30s 内恢复（硬指标）＋"回大厅重进"显式排除为通过路径 |
| AC6 确定性对账 | PASS | development 构建（批②确认映射）＋GM 动作字面名 `ieeeprobe`＋工件通道导出＋逐位一致（不一致＝AC 不成立） |
| AC7 门禁与纪律 | PASS | 全部字面命令（L1/L2/Doc/test --suite 与 --profile 均在 `test.py:32-33` 枚举内、`toolchain.py:157 cmd_check`）＋defines 校验面与 `#if UNITY_ANDROID` 守卫均钉批①、无豁免句 |
| AC8 回写与挂账 | PASS | 六处文档＋两份质量登记表清单具体（含 MAGICACLOTH2 残留宏、profile.defines 死字段清理登记） |

## 发现的问题与处理

| 轮次 | # | 严重度 | 位置 | 问题 | 处理（落进方案的修订） |
|---|---|---|---|---|---|
| 一 | B1 | blocker | DeployBake 安卓通道 | 三处同步消费点要求具名就绪接缝，不允许"实现批定就绪时序" | v3 钉死预载步＋快照；**二轮发现该形态与引导结构冲突（见二轮 #1），v4 改为同步读方案** |
| 一 | B2 | blocker | 触屏输入面·视角门 | 原设计"`Locked` ∨ 触屏激活"因果倒置且丢模态/死亡门（`pointerLocked` 实为对局控制中谓词） | v3 起改为"门零改动＋按平台换 `_lookDeltaOf` 喂源"；二轮补充工厂泛化契约（见二轮 #2） |
| 一 | B3 | major | 假设/风险 | `libxlua.so` 无 x86_64 ABI，模拟器 ARM 翻译无预检门 | v3 补预检门＋已知边界登记 |
| 一 | B4 | major | 工程设置批 | 宏守卫层级不成立（L1 读不到 ProjectSettings.asset）＋`profile.defines` 死字段 | v3 守卫落 gate 侧、死字段删除；三轮复验 AC7 曾留"实现批定"口子，v5 钉死 `toolchain.py check` defines 校验面随批①落地 |
| 一 | B5 | major | UI 触控范围 | CanvasScaler 分档/重排开口无上限 | v3 移入非目标＋AC4.6 限定三页（二轮核验"已修复"） |
| 一 | B6 | major | 验收标准 | AC3/AC4/AC5/AC6 判定不足或含豁免 | v3 全量改写（AC4 拆 4.1~4.7、AC5 固定 30s、AC6 去豁免）；二轮核验通过 |
| 一 | m1~m3 | minor | AC1/冒烟 | toolchain check 与 `--check-shared` 未入 AC、`-Profile` 修复断言、adb serial 参数化 | v3 采纳（二轮核验通过；三轮补 AC2 `adb connect` 双形态） |
| 一 | 建议 | — | 批次/契约/回滚 | 批次重排、HUD prefab 归属、endpoints 测后清理、方案 B 双范式代价、回滚成对 | v3 全部采纳 |
| 二 | #1 | blocker | DeployBake 时序（v3 引入） | 预载步 await 与 `GameEntry` 引导结构冲突：模块装配全在 `RunBootstrapAsync` 的 await 内、`Start()` 同步断言（`TakeContainer()` 空即抛），真 yield 必进错误态 | v4 改**同步读**：安卓分支 `UnityWebRequest.WaitForCompletion()`（与 task `.Wait()` 死锁形态不同；本地 jar 小文件规范用例）——三处消费点零改动、无预载步/快照/时序契约、不触碰引导链；引导重构降为备选方案 B（实测受阻才启用） |
| 二 | #2 | major | 触屏镜头缝 | `BattleViewRig.LookDeltaOf`（L890-895）硬绑 `is NewInputIntentSource`、`IIntentSource` 无视角成员——触屏源上线后工厂返回 null，镜头永不生效 | v4 新增可选接口 `ILookDeltaSource`（仿 `IAimWorldSink` 先例）泛化工厂，两源双实现；装配点澄清（缝在 `LookDeltaOf` 非 InputModule） |
| 二 | #3 | minor | DeployBake 文本 | 日志语义自相矛盾＋2s 超时裸数字 | v4 拆分 `DeployBake missing`/`read failed`＋常量推导 |
| 二 | #4 | minor | AC7 | 宏守卫"实现批定具体入口"仍非可执行命令 | v4/v5 钉 `toolchain.py check` 新增 defines 校验面（批①落地）＋`#if UNITY_ANDROID` 纪律守卫（正/负例）入 AC7、删豁免句 |
| 二 | #5 | minor | AC6 | 触发通道依赖 dev 构建（profile 无显式 development 标志） | v4 补 development 映射批②确认＋GM 动作字面名 `ieeeprobe` |
| 二 | #6 | minor | AC2 | 未覆盖 `adb connect` 形态 | v4 补双形态＋默认值文档化 |
| 三 | #1 | major | `ILookDeltaSource` 落位（v4 缺口） | 接口若照 `IAimWorldSink` 先例放 Runtime `IInputService.cs`，`UnityEngine.Vector2` 会随 `Tests/LiteSim.Core.Tests` 的 Runtime 源链接集进纯逻辑工程→L1 编译红；`TouchIntentSource` 纯逻辑与 `Vector2` 成员互斥 | v5 钉死分层：接口住 `Platform.Unity`（与 `NewInputIntentSource` 同程序集）；手势状态 `TouchGestureState` 纯值（float 对）＋`TouchIntentSource` 纯逻辑住 Runtime 进 L1 链接集；镜头增量经 `TouchLookAdapter : ILookDeltaSource`（Platform.Unity）读同一状态；**L1 链接集不新增引擎类型**写入测试策略 |
| 三 | #2 | minor | 超时推导 | "2s 防挂死"因果在 jar 本地读取路径上不成立（timeout 语义不保证、WaitForCompletion 不可中断） | v5 降级为"期望上界非保证"＋读前后 `Stopwatch` 耗时日志（批②判据可量）＋"不可中断长阻塞＝触发备选 B 的客观信号" |
| 三 | #3 | minor | 备选 B 理由 | "ClientHost 并行线冲突"为首因依据不足；硬依据＝`Start()` 的 `TakeContainer()` 空容器直接抛 `InvalidOperationException`（早于 `_fsm` 断言、无兜底） | v5 理由改为"Start() 语义改写面＋全帧启动时序变化"，并行线冲突降为附带因素 |
| 三 | #4 | minor | 预检顺序 | "探针包"来源未定义，与"预检先于首包构建"互为鸡生蛋 | v5 预检门改两段式：①abilist 快检（秒级，先于首包——AC1 顺序断言指此段）；②首个出包产物自身即探针（失败即换镜像重跑） |
| 三 | #5 | minor | 引导期读次数 | 无快照后两处装配各读一次（阻塞×2）未量化；"一次性"日志语义未写清 | v5 补进程内 memo（同步缓存文本——与被否决的预载步快照无关）→读次数恒＝1；"一次性"＝进程级静态 flag；批②判据含读次数/耗时 |
| 三 | #6 | minor | 评审产物 | `reviews/plan-review.md` 当时只含首轮错误 PASS 记录、缺二轮记录（交叉引用失效） | 本文件全文重写为三轮完整记录（含首轮摘要误报过程留痕） |
| 三 | 意见 | minor | AC4.4"观感"不可判定；自瞄验证件实现 `ILookDeltaSource` 的语义提示；AC5/AC6 共享 logcat 工件；风险 1 Burst 口径合并 | v5 全部采纳：AC4.4 改 FOV/相机优先级日志或 GM 读数；缝变宽提示入通用性节；AC2 固化会话日志工件导出路径供 AC5/AC6 引用；风险 1 合并为"Burst 使用面先核清单，无消费者即移除" |

## 评审结论

- 结论: PASS
- 验收标准: PASS
- 阻塞问题: 0
- 补充说明：第三轮判定"方案可实施"、验收标准 8/8 逐条可判定；第一/二轮的 blocker 与 major 均已修复并留痕（见上表）。
- 复评记录：共三轮（FAIL → FAIL → PASS）。第三轮 PASS 附 1 项 major＋5 项 minor，均为文本级修订且已逐项落进方案 v5（处理见上表），按"复评仅剩非 blocker 问题时修复、落盘、留痕即收口"规则不再开第四轮；若施工中发现方案与事实的新冲突，按变更控制回方案阶段重走评审。三轮子代理返回原文见各自任务记录（`.../tasks/51ab11c9-7613-4c92-93fd-730b6969164c/task-{8538fe9e…,2d90c479…,2d5dffd8…}.jsonl`）。
