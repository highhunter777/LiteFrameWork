# GM 与调试工具分层设计

> 定位：GM/调试面的**分层裁决与形态契约**。
> 配套：《命令中心专项设计》（命令总线/权限门/审计环）、《烘焙工具箱专项设计》（工具页件范式先例）、
> 《资源收集目录专项设计》§5.1（表布局单源 `TableLayout`）。设计裁决只在本文件；进度证据在 `施工进度/GM与调试工具分层.md`。

## 1. 四层模型

| 层 | 载体 | 生命周期 | 职责 | 不做什么 |
|---|---|---|---|---|
| **启动配置** | 场景 `DebugTuner`（Inspector） | 随场景保存 | 进房初值（免死/bot 数/冻结/传送/表现开关）；进房前快照（`ApplySnapshotOrDefaults`）；入口初值随 `CombatValues` 进联机身份摘要 | 不承载局内可调项 |
| **运行态规则** | `MatchTestRules` 每局持有件（LiteSim） | 进房新建（`ProcedureMatch` 按入口初值）、离场丢弃 | 免死/无限子弹/爆头带试验覆写的**运行态单源**；经 `SimStep` 脊柱注入确定性系统；`SimTestRules.ActiveMatch` 登记口供两端绑定与 GM 中局开合 | **判定路径不读任何静态**（修改指南 §1.3，含 dev 宏内） |
| **开关写口** | `TestModeOptions` 单表 + `TestModeRuntime` 快照（LiteGame，开发三宏） | 三宏内 | **唯一开关写口**（Startup/Live 分组，新增开关零面板改动）；请求桥（进/退/传送一次性标志）；爆头覆写跨局待用值 | 不落盘、不进摘要 |
| **投影面** | 编辑器 `LiteGame/工具链` Hub（页件宿主）/ `DevHUD`（只读） | — | 只读读数 + 调用写口/请求桥/命令 | **零业务逻辑**——投影不持规则、不写运行态（写只能经上述两个写口或命令） |

## 2. 动作三分口径（命令化边界）

1. **pre-Play 请求桥**：进入/退出测试模式、传送请求——容器尚不存在，直发一次性请求（写侧 `TestModeRuntime.RequestEnter/RequestExit/RequestTeleport`，消费侧 `TakeEnterRequest/TakeExitRequest/TakeTeleportRequest` **读即清——一次性语义编译期保证**）；传送在对局内的实际派发经 `ProcedureBattle` → `SendQueued(TeleportToAimCommand)` **进审计环且可撤销**。
2. **Play 中低频动作**：命令中心派发（审计全覆盖）——样例 `GmResetClockCommand`（时钟复位）、`TeleportToAimCommand`（传送，IUndoable）、**场景命令四件**（`GmLoadSingleScene/GmLoadAdditiveScene/GmUnloadSingleScene/GmUnloadAdditiveScene`——带参+异步形态首个落地：`IAsyncCommandHandler` 经 `ISceneService` 执行，注册点 `ContainerModule` 装配期〔dev 三宏，不依赖 DebugTuner 是否挂〕，场景页即带参命令的专面板）。新增 GM 动作默认走此形态：命令类型 + 处理器 + `CommandOptions(gmOnly: true, description)`，发现面自动列出。
3. **只读诊断与高频旋钮不命令化**：现场开关（`TestModeOptions.Set`）、爆头/持枪滑杆——审计环 32 条会被滑杆刷屏；连续旋钮不强行命令化（`GmResetClock` 与 `DebugTuner` 连续旋钮并存的既有先例同口径）。**只读投影同口径**（2026-10-12 裁决）：配置诊断页的运行状态/表查询/产物核对为零状态突变的只读面——命令化只刷审计噪音，维持直调。

## 3. 覆写面治理（沙箱件统一口径）

- **爆头带**：`MatchTestRules.HeadHitLineOverride` / `HeadshotRadiusOverride`（判定面）。**不进 `CombatValues` 摘要、不可重放**——连续调参旋钮进摘要会让每次试验抖动联机身份；同"中局开合"裁决。生效值统一经 `MatchTestRules.ResolveHeadHitLine/ResolveHeadshotRadius(rules)` 解析（null ⇒ 恒烘焙导出值，release/常规入口形态）。
- **持枪幅度**：`GripTuning.TorsoWeightDevOverride`（纯表现，LiteView）。
- **统一口径**：仅开发三宏内存在、不落盘、复位即回烘焙/导出值、判定路径读注入实例；定型值经 `HeadHitLineTuner.Export` → `HeadBake.g.cs`（**进 digest 的只有导出常量**）。
- 判定与可视化的同值机制：判定链（`ShootingSystem`/`SimRaycast`）经 SimStep 注入 rules；客户端可视化（激光/瞄准射线/调试绘制）传 `SimTestRules.ActiveMatch`——本地服同进程同实例，滑杆一动实弹即见。

## 4. 编辑器投影：页件契约与宿主

- 契约 `IGmPage : IEditorToolPage`（`Editor/Gm/`）：`OnShow` / `OnHide` / `Draw` / `Poll`——宿主在翻页/开窗时调生命周期，`Poll` **仅活动页**在 Play 中按 0.5s 节拍被调（非活动页不轮询）。
- 宿主 `LiteGame/工具链` Hub（`Editor/Toolchain/`）职责仅三件：页签生命周期、轮询调度、页签记忆（EditorPrefs **统一前缀 `LiteGame.Toolchain.*`**）；GM 六页作为 `page` 工具登记在 `scripts/gate/toolchain.json`。
- 六页：测试模式（请求桥+开关+爆头/持枪调参+**bot 操作**）、命令与审计（发现面/无参执行/审计环）、场景操作（SceneService 加载机制验证——经场景命令 `SendAsync` 派发，忙碌守卫）、配置诊断（运行状态+表查询+产物核对）、本地联测（联测环境页件——`deploy/.env` 单源驱动的两端起停/探针/日志尾随；**自管 `EditorApplication.update` 心跳**，编辑态即刷新——不受宿主 `Poll` 的 Play 门禁约束；被投影物是本地部署环境，不是游戏运行态）、联机诊断（DevHUD 的编辑器投影：HUD 总开关/段开关 + `BattleDiagnostics` 单源读数——读口与包形态启用口径见 §5）。
- **bot 操作（测试模式页）**：补位假人两项——**bot 无限生命**（`TestModeOptions` 现场开关，Live；权威侧宿主钩子 `SimTestRules.BotInfiniteHp` 每帧回满 ⇒ 假人永不死、命中/飘字反馈照常）与**重置假人**（一次性宿主钩子 `SimTestRules.BotResetRequested`：补位席位满血回出生点 + `LifeStartFrame` 生命更替 ⇒ 动画按"新生命"退出死亡叶；进房清残留同 TeleportDispatch 口径）。假人=补位席位（本地玩家不受两项管辖）。**未做（挂账）**：房内**追加**新假人——需放开"固定 2 座"世界规模（客户端定容重建/预测口径），归 G2 8 人基线面另立批。
- 诊断取件单源：`GmEditorResolve.ResolveFromContainer<T>`（`GameEntry.TakeContainer()` 口径——编辑器工具豁免形态，业务侧禁）；未装配按空态显示。
- 产物核对路径 = `TableLayout.TableDataPath` **单源**（领域分类布局——装载/服务端直读/编辑器预检三面共用，禁第二份拼接）。
- 页件范式承自烘焙工具箱：一页一职责、说明→操作→报告自包含；寄宿件（如 `HeadHitLineTuner`）被页与窗口共享。

## 5. 运行时投影：DevHUD

只读统计 HUD（自创建、F1 总开关、0.25s 节流、拉 `GameEntry.Stats`）——**保持只读**。将来真机/开发包若需 GM 动作，加命令通道（§2.2 形态复用），不给 HUD 加直写口。目录 `Assets/LiteGame/DevTool/`、程序集独立 `LiteGame.DevTool` + 三宏剥离；**禁移入 `Editor/` 特殊目录**（代码自创建形态，`MonoBehaviour` 须可 AddComponent——详见其类注释）。

- **段构成**：模块 stats 段（容器注册件 + 场景组件型合并发现）＋联机诊断段（读口 `BattleDiagnostics`——对局上下文的 dev-only 单点登记，对局视图装配/拆视图两处接线；无对局整段不显示，不显示陈旧值）＋性能段（帧间隔环形样本自算：均值 FPS/最差帧/长帧计数）＋日志尾巴/错误行。各段渲染开关 = DevHUD public 字段。
- **对局诊断读口**（`App/Debug/BattleDiagnostics`）：对局上下文是流程私有件——不进容器、非场景组件，HUD 与 GM 页经同一登记口读数（`Attach/Detach` + 快照/描述单源——两处消费不各拼一套文案）；RTT/丢包口径与 `BattleNetworkHudDriver` 同源（千分比 → 一位小数百分比）。
- **编辑器投影**：`LiteGame/工具链` →「联机诊断」页＝DevHUD 开关投影（总开关/段开关，F1 等效）＋读数投影（同一描述单源）。包形态无 Hub，HUD 由 F1 开。
- **开发包启用**：出包脚本消费 BuildProfile `channel`——`development` 通道传 `BuildOptions.Development`（三宏的 `DEVELOPMENT_BUILD` 在包内成立，dev 面随包编译：DevHUD/DebugTuner/GM 命令层）；其余通道维持原样（缺省仅 DetailedBuildReport）。**分发边界**：development 通道产物＝开发侧测试客户端专用，**不进测试者分发包**——测试者出包走 release 通道档（`BuildProfiles/windows-x64-release.json`），各通道产物目录独立（`profile.output` 透传 build 命令），Dist 打包测试者包指向 `Builds/StandaloneWindows64-release`。DevMenuPanel 包形态不开（开出动作 `#if UNITY_EDITOR` 收口 + dev 预制体不进收集组——入口分叉治理，非缺漏）。

## 6. 边界（如实）

- ~~场景操作/表查询未命令化（待议）~~ **已议落定**（2026-10-12）：场景操作四件**已命令化**（带参+异步 GmOnly——加载/卸载进审计环，场景页 SendAsync 派发，L1 源链接直测派发协议）；表查询/运行状态/产物核对**裁决维持直调**（只读投影零状态突变——§2.3）。
- 审计环不覆盖 pre-Play 请求桥与高频旋钮（§2 取舍，非遗漏）。
- `MatchTestRules` 链路依赖"客户端预测与进程内服务器同进程同实例"——**测试房/本地服形态专属**；真 KCP 不提供测试房，在线房间级规则下发未做（`ProcedureMatch` 入口口径）。
- GM 面板对局中导出爆头区会触发 domain reload 中断对局（既有行为，页内 HelpBox 已提示）。
