# GM 与调试工具分层设计

> 版本：1.6
> 更新日期：2026-10-25
> 定位：GM/调试面的**分层裁决与形态契约**——回答“调试能力的内核放在哪、投影怎么长、写口与读口归谁”。
> 不做的事：不裁决具体调试工具的能力清单与优先级（归[《调试与开发工具链演进路线图》](../测试与性能/调试与开发工具链演进路线图.md)）；不裁决工具平台形态（归[《开发工具链平台专项设计》](开发工具链平台专项设计.md)）；不登记施工批次（归 [施工进度/GM与调试线](../../施工进度/GM与调试线.md)）。
> 冲突裁决：内核/投影职责与写口纪律以本文为准；能力优先级以调试路线图 T0～T3 为准；平台形态以平台专项为准；UI 专用投影以[《UI 运行时调试工具链专项设计》](../客户端/UI系统/UI运行时调试工具链专项设计.md)为准。
> 配套：《命令中心专项设计》（命令总线/权限门/审计环）、《烘焙工具箱专项设计》（工具页件范式先例）、
> 《资源收集目录专项设计》§5.1（表布局单源 `TableLayout`）。设计裁决只在本文件；进度证据在 [施工进度/GM与调试线](../../施工进度/GM与调试线.md)。

## 1. 四层模型

| 层 | 载体 | 生命周期 | 职责 | 不做什么 |
|---|---|---|---|---|
| **启动配置** | 场景 `DevStartupConfig`（Inspector） | 随场景保存 | 进房初值（免死/bot 数/冻结/传送/表现开关）；进房前快照（`ApplySnapshotOrDefaults`）；入口初值随 `CombatValues` 进联机身份摘要 | 不承载局内可调项 |
| **运行态规则** | `MatchTestRules` 每局持有件（LiteSim） | 进房新建（`ProcedureMatch` 按入口初值）、离场丢弃 | 免死/无限子弹/爆头带试验覆写的**运行态单源**；经 `SimStep` 脊柱注入确定性系统；`SimTestRules.ActiveMatch` 登记口供两端绑定与 GM 中局开合 | **判定路径不读任何静态**（修改指南 §1.3，含 dev 宏内） |
| **开关写口** | `TestModeOptions` 单表 + `TestModeRuntime` 快照（LiteGame，开发三宏） | 三宏内 | **唯一开关写口**（Startup/Live 分组，新增开关零面板改动）；请求桥（进/退/传送一次性标志）；爆头覆写跨局待用值 | 不落盘、不进摘要 |
| **投影面** | 编辑器 `LiteGame/工具链` Hub（页件宿主）/ `DevHUD`（只读）/ `DebugConsole`（F12，运行期操控）/ `DebugPanel`（F11，常用动作/即时开关） | — | 只读读数 + 调用写口/请求桥/命令 | **零业务逻辑**——投影不持规则、不写运行态（写只能经上述两个写口或命令） |

> 组件拆分：`DevStartupConfig` 只保留启动开关与本地测试模式初值；时钟/事件连续旋钮在 `DebugRuntimeTuner`；运行期开发入口为 `DebugConsole`（F12，UI Toolkit），不设独立开发页覆盖件。三者同挂 `GameEntry`，均随开发三宏剥离。

## 2. 动作三分口径（命令化边界）

1. **pre-Play 请求桥**：进入/退出本地测试模式、传送请求——容器尚不存在，直发一次性请求（写侧 `TestModeRuntime.RequestEnter/RequestExit/RequestTeleport`，消费侧 `TakeEnterRequest/TakeExitRequest/TakeTeleportRequest` **读即清——一次性语义编译期保证**）；传送在对局内的实际派发经 `ProcedureBattle` → `SendQueued(TeleportToAimCommand)` **进审计环且可撤销**。
2. **Play 中低频动作**：命令中心派发（审计全覆盖）——样例 `GmResetClockCommand`（时钟复位）、`TeleportToAimCommand`（传送，IUndoable）、**场景命令四件**（`GmLoadSingleScene/GmLoadAdditiveScene/GmUnloadSingleScene/GmUnloadAdditiveScene`——带参+异步形态首个落地：`IAsyncCommandHandler` 经 `ISceneService` 执行，注册点 `ContainerModule` 装配期〔dev 三宏，不依赖 DevStartupConfig 是否挂〕，场景页即带参命令的专面板）。新增 GM 动作默认走此形态：命令类型 + 处理器 + `CommandOptions(gmOnly: true, description)`，发现面自动列出。
3. **只读诊断与高频旋钮不命令化**：现场开关（`TestModeOptions.Set`）、爆头/持枪滑杆——审计环 32 条会被滑杆刷屏；连续旋钮不强行命令化（`GmResetClock` 与 `DebugRuntimeTuner` 连续旋钮并存的既有先例同口径）。**只读投影同口径**（2026-10-12 裁决）：配置诊断页的运行状态/表查询/产物核对为零状态突变的只读面——命令化只刷审计噪音，维持直调。

## 3. 覆写面治理（沙箱件统一口径）

- **爆头带**：`MatchTestRules.HeadHitLineOverride` / `HeadshotRadiusOverride`（判定面）。**不进 `CombatValues` 摘要、不可重放**——连续调参旋钮进摘要会让每次试验抖动联机身份；同"中局开合"裁决。生效值统一经 `MatchTestRules.ResolveHeadHitLine/ResolveHeadshotRadius(rules)` 解析（null ⇒ 恒烘焙导出值，release/常规入口形态）。
- **持枪幅度**：`GripTuning.TorsoWeightDevOverride`（纯表现，LiteView）。
- **统一口径**：仅开发三宏内存在、不落盘、复位即回烘焙/导出值、判定路径读注入实例；定型值经 `HeadHitLineTuner.Export` → `HeadBake.g.cs`（**进 digest 的只有导出常量**）。
- 判定与可视化的同值机制：判定链（`ShootingSystem`/`SimRaycast`）经 SimStep 注入 rules；客户端可视化（激光/瞄准射线/调试绘制）传 `SimTestRules.ActiveMatch`——本地服同进程同实例，滑杆一动实弹即见。

## 4. 编辑器投影：页件契约与宿主

- 契约 `IGmPage : IEditorToolPage`（`Editor/Gm/`）：`OnShow` / `OnHide` / `Draw` / `Poll`——宿主在翻页/开窗时调生命周期，`Poll` **仅活动页**在 Play 中按 0.5s 节拍被调（非活动页不轮询）。
- 宿主 `LiteGame/工具链` Hub（`Editor/Toolchain/`）职责仅三件：页签生命周期、轮询调度、页签记忆（EditorPrefs **统一前缀 `LiteGame.Toolchain.*`**）；GM 七页作为 `page` 工具登记在 `scripts/gate/toolchain.json`。
- 七页：本地测试模式（请求桥+开关+**bot 操作**+**云端测试房创建/选图与 GM 规则**+爆头导出/FOV 试调）、调试参数（`IDebugTunableProvider` 描述自动渲染，见 §7）、命令与审计（发现面/无参执行/审计环）、场景操作（SceneService 加载机制验证——经场景命令 `SendAsync` 派发，忙碌守卫）、配置诊断（运行状态+表查询+产物核对）、本地联测（联测环境页件——`deploy/.env` 单源驱动的两端起停/探针/日志尾随；**自管 `EditorApplication.update` 心跳**，编辑态即刷新——不受宿主 `Poll` 的 Play 门禁约束；被投影物是本地部署环境，不是游戏运行态）、联机诊断（DevHUD 的编辑器投影：HUD 总开关/段开关 + `BattleDiagnostics` 单源读数——读口与包形态启用口径见 §5）。
- **bot 操作（本地测试模式页）**：补位假人两项——**bot 无限生命**（`TestModeOptions` 现场开关，Live；权威侧宿主钩子 `SimTestRules.BotInfiniteHp` 每帧回满 ⇒ 假人永不死、命中/飘字反馈照常）与**重置假人**（一次性宿主钩子 `SimTestRules.BotResetRequested`：补位席位满血回出生点 + `LifeStartFrame` 生命更替 ⇒ 动画按"新生命"退出死亡叶；进房清残留同 TeleportDispatch 口径）。假人=补位席位（本地玩家不受两项管辖）。**未做（挂账）**：房内**追加**新假人——需放开"固定 2 座"世界规模（客户端定容重建/预测口径），归 G2 8 人基线面另立批。
- 诊断取件单源：`GmEditorResolve.ResolveFromContainer<T>`（`GameEntry.TakeContainer()` 口径——编辑器工具豁免形态，业务侧禁）；未装配按空态显示。
- 产物核对路径 = `TableLayout.TableDataPath` **单源**（领域分类布局——装载/服务端直读/编辑器预检三面共用，禁第二份拼接）。
- 页件范式承自烘焙工具箱：一页一职责、说明→操作→报告自包含；寄宿件（如 `HeadHitLineTuner`）被页与窗口共享。

## 5. 运行时投影：DevHUD、开发控制台与常用面板

只读统计 HUD（自创建、F1 总开关、0.25s 节流、经 `GameEntry.DebugKernel` 读内核读模型）——**保持只读**。将来真机/开发包若需 GM 动作，加命令通道（§2.2 形态复用），不给 HUD 加直写口。目录 `Assets/LiteGame/DevTool/`、程序集独立 `LiteGame.DevTool` + 三宏剥离；**禁移入 `Editor/` 特殊目录**（代码自创建形态，`MonoBehaviour` 须可 AddComponent——详见其类注释）。

- **段构成**：模块 stats 段（容器注册件 + 场景组件型合并发现）＋联机诊断段（读口 `BattleDiagnostics`——对局上下文的 dev-only 单点登记，对局视图装配/拆视图两处接线；无对局整段不显示，不显示陈旧值）＋性能段（内核性能读模型：FPS/p50/p95/p99/最差帧/长帧 + GC/Draw/Batches recorder——投影只喂时钟差并读 `DebugKernel` 读口）＋日志尾巴/错误行。各段渲染开关 = DevHUD public 字段。
- **对局诊断读口**（`App/Debug/BattleDiagnostics`）：对局上下文是流程私有件——不进容器、非场景组件，HUD 与 GM 页经同一登记口读数（`Attach/Detach` + 快照/描述单源——两处消费不各拼一套文案）；RTT/丢包口径与 `BattleNetworkHudDriver` 同源（千分比 → 一位小数百分比）。
- **编辑器投影**：`LiteGame/工具链` →「联机诊断」页＝DevHUD 开关投影（总开关/段开关，F1 等效）＋读数投影（同一描述单源）。包形态无 Hub，HUD 由 F1 开。
- **开发包启用**：出包脚本消费 BuildProfile `channel`——`development` 通道传 `BuildOptions.Development`（三宏的 `DEVELOPMENT_BUILD` 在包内成立，dev 面随包编译：DevHUD/DebugConsole/DebugPanel/DevStartupConfig/DebugRuntimeTuner/GM 命令层）；其余通道维持原样（缺省仅 DetailedBuildReport）。**分发边界**：development 通道产物＝开发侧测试客户端专用，**不进测试者分发包**——测试者出包走 release 通道档（`BuildProfiles/windows-x64-release.json`），各通道产物目录独立（`profile.output` 透传 build 命令），Dist 打包测试者包指向 `Builds/StandaloneWindows64-release`。开发控制台随 `DebugConsole` 自创建（`RuntimeInitializeOnLoadMethod`），不需要 dev 预制体；运行期开发入口不随包通道分叉。
- **演进与渲染后端**：分批、退出条件与 **UI Toolkit 决策**（已迁移 UI Toolkit；IMGUI 仅自动回退，只换渲染后端不换读口）见[《DevHUD 演进路线图》](../测试与性能/DevHUD演进路线图.md) §5。
- **开发控制台**（`DebugConsole`，F12）：运行期操控投影（不设独立开发页覆盖件）。UI Toolkit 渲染（运行时 `PanelSettings` + 内联样式 + 系统 SDF 字体；运行期无 `ThemeStyleSheet`，只用 `Label`/`VisualElement` 自绘），输入经 `DevHotkey` 收口；命令/选项/请求桥/读数全经内核。本地测试模式由 `request enter` 触发；真 KCP（本地全链/云端）只走 Meta 票据，不再有直连测试身份入口；进房启动快照由 `ProcedureMain` 在消费进房请求时单点落值。面板位于产品 uGUI Overlay 之下，全屏产品页打开时会盖住底部面板，但命令仍可执行——这是当前渲染后端的已知边界。
- **常用调试面板**（`DebugPanel`，F11）：同一个内核的轻量 UI Toolkit 投影——本地测试模式进入/退出/传送 + `TestModeOptions` 的 `Live` 开关 + `RuntimeVisible` 的 Sandbox/Presentation 快捷调参（滑杆/开关/复位）。`Product` 参数默认只在编辑器页出现（当前为准心 6 项与视角 2 项，默认值仍写在代码常量、定档后迁表/配置）；长尾参数也只进编辑器。打开时注册 `debug.panel` 输入拦截；任意命令仍走 F12 控制台。
- **统一内核与投影**：DevHUD 只是运行期只读投影；写动作由运行期操控投影经内核命令通道执行，见 §7。UI 专用投影见[《UI 运行时调试工具链专项设计》](../客户端/UI系统/UI运行时调试工具链专项设计.md)。

## 6. 边界（如实）

- ~~场景操作/表查询未命令化（待议）~~ **已议落定**（2026-10-12）：场景操作四件**已命令化**（带参+异步 GmOnly——加载/卸载进审计环，场景页 SendAsync 派发，L1 源链接直测派发协议）；表查询/运行状态/产物核对**裁决维持直调**（只读投影零状态突变——§2.3）。
- 审计环不覆盖 pre-Play 请求桥与高频旋钮（§2 取舍，非遗漏）。
- `MatchTestRules` 链路依赖"客户端预测与进程内服务器同进程同实例"——**本地测试模式/本地服形态专属**；真 KCP 的云端测试房只提供 `gm_flags`（免死/无限弹药）在线下发，爆头带/持枪幅度等覆写仍只在本地测试模式验（`ProcedureMatch` 入口口径）。
- GM 面板对局中导出爆头区会触发 domain reload 中断对局（既有行为，页内 HelpBox 已提示）。
- **投影直调禁令（R14）**：两个投影根（`LiteGame.DevTool` + `Editor/Gm`）的调试读数/动作/发现面一律经内核；直调底层调试源由纪律扫描把守。唯一 lint-allow 判例＝场景页（带参命令专面板——`CommandCenter.SendAsync` 直用，全量审计）；编辑器页在非 Play（内核未装配）时相关节空态提示。

## 7. 统一内核与三投影

### 7.1 裁决

调试能力按**内核 + 投影**组织，不按载体各写一套逻辑：

- **内核**：与 UI/载体无关的读模型、动作模型与发现协议——一项能力只在这里实现一次；
- **投影**：编辑器 Hub 页、运行期只读面、运行期操控面——只做读取、渲染、输入与编排，不持规则、不写运行态、不另拼读数；
- **投影禁直调**：读数、动作、发现面一律经内核门面——直调底层调试源（读口/写口/发现表）由纪律扫描 **R14** 把守（受守根＝`Assets/LiteGame/DevTool` 与 `Assets/LiteGame/Editor/Gm`；豁免＝契约类型引用（`TestModeOptions.Id/Info`、`BattleDiagnostics.Snapshot` 等类型位置）、纯格式化助手（`DiagTrace.Line`/`Log.TagName`）、`Log` 门面写口；例外行 `lint-allow R14` 注明理由）；
- 新增投影不改内核；新增内核能力由所有投影按发现面自动获得，或由投影显式声明不支持。

这条裁决把“编辑器工具能做的，开发包也能做”变成结构性结果，而不是每批再补一套面板。

### 7.2 内核边界

| 子面 | 内容 | 现行承载 |
| --- | --- | --- |
| 读模型 | 模块统计、对局诊断、日志/结构化诊断、页面状态与焦点、配置/内容身份、性能样本、测试模式/日志关联上下文（`IsTestModeActive`/`CurrentMatchId`/`CurrentSessionId`/`StartupBotCount`） | `GameEntry.Stats`、`BattleDiagnostics`、`Log`/`DiagTrace`、`UIService`/`UIModalStack.IUIFormQuery` 只读查询、`DebugPerfSampler`（帧统计 + GC/Draw/Batches recorder——经 `DebugKernel.SampleFrame/TryTakePerf/RecordHudBuildMs`） |
| 动作模型 | 低频离散动作（命令化 + 审计 + 可撤销）、测试开关单表、pre-Play 一次性请求桥、一次性宿主钩子（bot 重置 `RequestBotReset`） | `CommandCenter`、`TestModeOptions`、`TestModeRuntime`、`SimTestRules.BotResetRequested`（经内核收口） |
| 发现协议 | 命令注册表（类型/描述/GmOnly/注册时序）、选项表、读模型段表、调参描述表 | `CommandCenter.GetRegisteredInfos()`、`TestModeOptions.All`、`IDevHudSection`、`IDebugTunableProvider` |
| 调参模型 | `DebugTunable` 描述（Id/Group/Tags/Scope/Default/Min/Max/Decimals/RuntimeVisible/Get/Set/Reset）；默认值来自功能数据源 | `DebugKernel.Tunables/RuntimeTunables/TrySetTunable/ResetTunable` + `DebugTunableDiscovery`（容器 + 场景 provider） |

> 调参描述不是业务接口开放：功能自己提供 get/set 并保证不变量；编辑器「调试参数」页按全量渲染，F11 只按 `RuntimeVisible` 白名单渲染 Sandbox/Presentation；`Product` 默认不进运行期。F12 `tune` 可显式访问全量。连续/开关参数不强制命令化（避免审计刷屏），离散动作仍走 `CommandCenter`；`Product` scope 只作临时试验，定档后迁回表/配置；release 三宏剥离。
> `Product` 用于“默认值仍写在代码常量、尚未迁入表/配置”的参数（准心/视角）；默认值已由烘焙产物提供的参数（爆头区/持枪幅度）不标 Product，其覆写按调试维度归 Sandbox/Presentation。
> 参数规模管理：Id 采用 `group.item` 命名、Group 分组、Tags 检索；功能各自 provider，不建中央手写清单；运行期白名单只放现场能解释的少量参数，长尾/未定型参数留在编辑器。

内核在开发三宏内、运行时可链接；**不引用 `UnityEditor`，不引用任何投影类型**。

### 7.3 三个投影

| 投影 | 载体 | 职责 | 约束 |
| --- | --- | --- | --- |
| 编辑器投影 | Hub 内 `IEditorToolPage` | 富交互：表格/滑杆/多页；承载编辑器独占动作（起停本地服、静态检查、导出 `.g.cs`） | 可引用 `UnityEditor`；不进包 |
| 运行期只读投影 | `DevHUD`（F1） | 展示读模型；不写运行态 | 保持只读；不加直写口 |
| 运行期操控投影 | `DebugConsole`（F12，UI Toolkit 文本控制台）/ `DebugPanel`（F11，常用动作/即时开关）/ 专用调试面板（如 UI 调试面） | 解析调用名/按钮 → 内核命令或选项表；渲染读模型；面板可有自己的输入形态 | 只经内核写；不直调业务服务 |

专用调试面板不是第四套逻辑：它是运行期操控投影的一个模块，同一命令集既可被文本控制台调用，也可被面板按钮调用。UI 专用投影见[《UI 运行时调试工具链专项设计》](../客户端/UI系统/UI运行时调试工具链专项设计.md)。

投影与内核之下的单源写口没有第二写路径（开关单表/请求桥仍由内核包装调用，R14 拦投影直调）；场景操作页是唯一 `lint-allow R14` 判例——带参/异步命令经 `CommandCenter.SendAsync` 派发（权限门/拦截器/审计链全量生效），内核 `TryExecute` 只覆盖无参形态。

### 7.4 取件与依赖方向

```text
投影 → 内核 → 业务/服务
```

- 内核在装配期由容器注入（与产品服务同一装配根）；**不反射私有字段**，不把编辑器的 `TakeContainer` 豁免形态搬进运行期；
- 编辑器投影的取件仍走 `GmEditorResolve`（编辑器工具豁免）；运行期投影只引用运行时可链接件；
- 内核不得引用投影程序集；投影不得反向依赖另一个投影。

### 7.5 失败语义

| 情形 | 契约 |
| --- | --- |
| 内核未装配或被三宏剥离 | 投影显示空态与原因；不影响启动与产品流程 |
| 命令被权限门拒绝 | 返回 `CommandResult` 的拒绝原因；投影只显示，不重试 |
| 读模型单段异常 | 只影响该段输出（同 DevHUD 段隔离），不拖垮其他段 |
| 采集（截图/现场包/dump）失败 | `Log.Warning`，不反噬业务、不阻塞操作 |
| 投影自身异常 | 不影响产品 UI/流程；释放时恢复到打开前状态 |

### 7.6 容量与性能

- 投影轮询沿用现有口径：编辑器页 0.5s（仅活动页）、DevHUD 0.25s；运行期命令面按需，不常驻轮询；
- 读模型隐藏即停；采集只在显式操作时执行；
- 不新增 `public static` 可变状态；内核与投影均不得进入判定路径；调试读数不得写入 Sim/网络热路径。

### 7.7 验收层级

| 层级 | 验证内容 |
| --- | --- |
| L1 | 内核纯逻辑：发现表、命令映射、读模型投影、选项表；不依赖 Unity 运行时 |
| L2 PlayMode | 投影在真实 `UIService`/`CommandCenter` 下的空态、拒绝、读模型与清理 |
| 开发包人工验证 | 真实操作路径（红线 13）：入口 → 操作 → 副作用断言 → 证据 |
| release | 类型与资产零残留；调试命令不可达 |
| Doc | 设计与索引一致（`--suite Doc`） |

### 7.8 非目标

- 不统一投影的外观与布局；
- 不把编辑器独占能力塞进 Player；
- 不建第二套命令总线、第二套读口或第二套执行器；
- 不改变 DevHUD 的只读定位；
- 不定义远程调试协议（Runtime Pipeline 是另一条通道，不参与内核）。
