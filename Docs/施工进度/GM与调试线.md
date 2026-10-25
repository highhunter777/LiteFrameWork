# GM 与调试线（GM 与调试工具分层 · 日志标签收敛）

> **定位**：GM/调试面的分层落地与日志标签收敛。

> **章法**：一卷一条线；每章＝原一份批次记录（标题、正文、证据原样并入，仅降一级标题）。
> **状态图例**（全目录统一）：`已完成（附验证证据）` / `进行中` / `未开始` / `阻塞（附原因）`。
> 每章开头的引用块是该批的原始状态与证据；本卷只做归并与章节化，未改写任何批次结论。

## 章节索引

| 章 | 标题 | 原文件 |
|---:|---|---|
| 1 | GM 与调试工具分层 | `GM与调试工具分层.md` |
| 2 | 日志标签收敛（LogTag 枚举） | `日志标签收敛.md` |
| 3 | 联机诊断 HUD（DevHUD 段＋GM 第六页＋测试包 Development 化） | — |
| 4 | 房间回放与现场包（工具链 v2 第一刀） | — |
| 5 | 现场闭环 T0-2（世界环字段级分歧 · mismatch 即时 flush · 崩溃捕获 · 符号归档 · GM 下载） | — |
| 6 | DevHUD 可用性修复与日志关联（T1 第一刀） | — |
| 7 | 结构化日志 sink 与检索（T1 第二刀） | — |
| 8 | DevHUD 的 UI Toolkit 渲染后端（文本桥） | — |
| 9 | DevHUD P0-A：段化/行色调/折叠/DevHotkey | — |
| 10 | DevHUD P0-A 剩余：过滤/JSON/Diag/Sink/拖动缩放/持久化 | — |
| 11 | DevHUD P0-B：性能/联机/Stats 读数 | — |
| 12 | 清 P0-B 遗留债：DevHUD 拆分 | — |
| 13 | 调试组件职责拆分（DebugTuner → DevStartupConfig） | — |
| 14 | 统一调试内核与运行期控制台（F12） | — |
| 15 | 开发页退役与开发控制台 UI Toolkit 化 | — |
| 16 | 测试房语义统一与普通房结算 | — |
| 17 | 测试路径收口：退役直连对局（Room-A / TestToken） | — |
| 18 | 常用调试面板（F11，UI Toolkit） | — |
| 19 | 通用调参注册表与编辑器「调试参数」页 | — |
| 20 | 运行期调参面（F12 tune + F11 白名单）与 scope 策略 | — |
| 21 | 产品参数定档（准心与视角 8 项） | — |
| 22 | 武器试验参数运行期调参（伤害/射速） | — |
| 23 | 弱网注入（传输层损伤 + HUD 可视化） | — |
| 24 | 调试内核收口（投影旁路修复 + R14 纪律守卫） | — |

---

## 第1章 GM 与调试工具分层

<a id="ch1-GM与调试工具分层"></a>

> **原文件**：`Docs/施工进度/GM与调试工具分层.md`（归并前独立成文，内容整段并入本章）

> 《GM 与调试工具分层设计》落地批（2026-10-11）。**状态：已完成（附验证证据）**——pipeline 编译 0 错、L1 1572 全绿；L2 夜间、四页手测待本机。

### 一刀：爆头覆写收口（SimTestRules 同型尾巴根治）

`CombatConfig.HeadHitLineDevOverride/HeadshotRadiusDevOverride`（public static 可变浮点，三宏内）被确定性路径直读（`ShootingSystem.cs:194` 爆头判据读 `HeadHitLineLive`、`SimRaycast.cs:80-81` 双柱几何读两个 Live）——与已根治的 SimTestRules 完全同型（§1.3"含 dev 宏内"）。收口：

- `MatchTestRules` 增 `HeadHitLineOverride/HeadshotRadiusOverride` 两字段（**不进 CombatValues 摘要、不可重放**——连续调参进摘要会随试验抖动联机身份）+ 静态解析口 `ResolveHeadHitLine/ResolveHeadshotRadius(rules)`（null ⇒ 恒烘焙导出值）。
- `SimRaycast.RaycastEntities` 三重载加 `MatchTestRules rules = null`（默认参数——L1 用例/既有调用零改动）；`ShootingSystem` 判定与射线判定改注入（:133/:194）。
- 客户端可视化四处传 `SimTestRules.ActiveMatch` 与真实弹道同值：`BattleLaserDriver`×2（激光截停）、`NewInputIntentSource`（瞄准射线）、`SimSandbox`（灰盒沙盒）；`BattleHeadshotDebugDrawer` 黄/红圈同改。
- 写通道：`TestModeRuntime` 增跨局待用值两字段；`ProcedureMatch` 进房种子；GM 面板滑杆/`HeadHitLineTuner`「取当前覆写值/复位」改双落（活动局实例即时生效 + 待用值跨局延续）；`CombatConfig` 静态覆写面整体删除（留单源指向注释）。

### 二刀：GM 工具箱拆页（god panel 闭环）

- `GmPanel`（原 621 行六区一窗，审计"低"条）→ **壳 90 行** + 四页件（`Editor/Gm/`）：`GmTestModePage` / `GmCommandsPage` / `GmScenePage` / `GmConfigDiagnosticsPage`（原③状态+④表查询+⑥产物核对合一），契约 `IGmPage`（OnShow/OnHide/Draw/Poll，轮询仅活动页），取件单源 `GmEditorResolve`（TakeContainer 口径）。
- EditorPrefs 统一前缀 `LiteGame.Gm.*`（旧 `LiteGame.ConfigPanel.*` 键弃用——开发偏好，未迁移）。
- 产物核对路径单源：`TableLayout.TableDataPath(file)` 新增（DataDir+file+".bytes" 拼接口径唯一——装载/服务端/编辑器三面共用），`ConfigService.LoadAsync` 同步改走；GmPanel:570/591 两条"低"债（手拼路径+表名硬编码）随页拆收口（`DefaultTableName` 常量化）。
- 命令化口径如实：传送已走 `ProcedureBattle → SendQueued(TeleportToAimCommand)`（审计+可撤销）——按钮无需改；进/退测试模式是 **pre-Play 请求桥**（容器尚不存在），维持一次性标志；场景操作/表查询未命令化（带参+异步，待议）——三分口径见设计文档 §2。

### 验证

- **pipeline 编译**：unity-pipeline（7801 实例）recompile → 0 错（首轮抓出 `GmScenePage` 缺 `using LiteFramework`，修复后过）。
- **L1 全量**：`test.ps1 -Lane L1 -Profile PullRequest` → **1572/1572 全绿**（含纪律扫描/表守卫）。
- **LiteSim 定向**：335/335（SimRaycast 几何/射击判定/回滚链在第一刀后逐位保持）。
- **BuildHash**：共享源集改动（MatchTestRules/CombatConfig/SimRaycast/ShootingSystem/SimTestRules）已重生成（与资源收集迁移批三的 `04162cb8` 链衔接，终值以收尾复算为准）。

### 追加批：开关写口 L1 接入（2026-10-12）

`TestModeOptions`/`TestModeRuntime`（§1 开关写口/请求桥层——零引擎依赖只引 SimTestRules，住 LiteGame Unity
程序集无 dotnet csproj）按 ViewTransform/Input **源链接先例**纳入 `Tests/LiteSim.Core.Tests`；三宏守卫经
`LITEFRAMEWORK_DEBUG` 激活（宏影响面已核查：LiteSim.Core 走 ProjectReference 不受影响，既有链接件无三宏段）。
新增 `TestModeOptionsTests` 六用例：开关表完整性（覆盖全部 Id/无重复/标签非空/两组齐备）、单一写点正反向
经 Get 同读、`ApplyDefaults` 默认口径（逐开关套表值 + BotCount 回 1）、InfiniteAmmo 副作用三态（非测试模式
不武装/无活动局只记快照/有活动局双落）、BotInfiniteHp 宿主钩子=测试模式与开关的与门、请求桥与爆头待用值
"套默认不代清"（一次性语义归消费方）。

- **定向**：TestModeOptionsTests 6/6 绿；LiteSim.Core.Tests 全量 342/342（零回归）。
- **审计债关系**：`App/TestModeRuntime.cs:13-36` 静态消费协议中债当时不销账——本批以 L1 行为面钉住作缓解，
  **已由追加批 3 销账（见下）**；登记见《代码质量审计-客户端》。
- **环境备注**：本机无 PowerShell 7（仅 Windows PowerShell 5.1），`test.ps1` 门禁入口未跑——L1 等价件
  （各测试工程 dotnet 全量 + 纪律扫描器直跑）见本机验证记录；有 PS7 的机器上仍应跑一次正式门禁。

### 追加批 2：无限子弹空仓放行（2026-10-12）

**裁决**：无限子弹语义从"开火不消耗"扩为"空仓亦可开火"——`WeaponSystem.TryConsumeShot` 空仓门加 rules
判断（`MagAmmo <= 0 && !infinite` 才拦，注入规则实例读口不变——R6/R13 口径保持）；此前彻底干涸
（弹匣 0+备弹 0）的枪现场开开关仍打不响（空仓门先于无限分支、零备弹换弹被拒），只能靠切枪到未装备槽
拿满弹新枪恢复。换弹门不受影响（满弹/零备弹仍拒）。

- 语义同步：`MatchTestRules.InfiniteAmmo` 注释、`TestModeOptions` 开关标签（GM 面板显示名）加"空仓也可开火"。
- **用例**：`WeaponSystemTests.无限子弹_空仓放行_干涸弹匣照常开火不扣弹_零备弹换弹仍拒`（旧代码下必红——
  空仓门拦掉全部 Fire）；WeaponSystemTests 定向 13/13。
- **BuildHash**：共享源集（WeaponSystem/MatchTestRules）已重生成——shared `191f1143d32017bc`→
  `3c07d038aadb1e81`（76 文件）。

### 追加批 3：请求桥收口——TestModeRuntime 静态消费协议根治（2026-10-12）

**根治**（追加批 1 登记的中债销账）：请求桥三标志（EnterRequested/ExitRequested/TeleportRequested）转私有，
收口为类型化读写对——写侧 `RequestEnter/RequestExit/RequestTeleport`（GM 面板按钮、F10/T 热键），消费侧
`TakeEnterRequest/TakeExitRequest/TakeTeleportRequest` **读即清**：忘清零/二次消费在编译期不可能
（原"跨类约定消费后清零"协议退役）。调用点同步：`ProcedureMain`/`ProcedureBattle`/`GmTestModePage`
（Unity 侧盲改——全仓 grep 旧标志名零残留兜底）；快照字段维持公开＝dev-only 三宏豁免面
（开关单一写点 `TestModeOptions.Set` 不变）。

- **用例**：`TestModeOptionsTests.请求桥与爆头待用值_套默认不代清_消费即清二次为假`——Request* 落请求 →
  套默认不代清 → Take* 见 true → 二次 Take* 恒假（消费即清的行为面）；测试复位改经 Take* 吸收残留。
- **验证**：定向 6/6；L1 等价全量 **1602 全绿**（LiteSim 343 · LiteFramework 711 · LiteNet 406 ·
  LiteTesting 7 · MetaServer 135）；BuildHash `--check` 双常量不变（LiteGame/App 不在共享源集）；
  设计文档 §2.1 措辞同步；审计修复登记销账。

### 追加批 4：场景操作命令化——表查询裁决维持直调（2026-10-12）

**场景操作命令化**（§2 三分口径第 2 形态的**带参+异步首例**）：四件 GmOnly 命令
（`GmLoadSingleSceneCommand`/`GmLoadAdditiveSceneCommand`/`GmUnloadSingleSceneCommand`/
`GmUnloadAdditiveSceneCommand`——三件带 location 参）+ `GmSceneCommandHandler`
（`IAsyncCommandHandler`×4 显式实现，构造注入 `ISceneService`；处理器异常归 SendAsync 隔离契约不自裹 try）
+ `GmSceneCommands.RegisterAll`（SubscriptionBag 合并注销器）。注册点 `ContainerModule` 装配期 dev 三宏
（app 生命周期——**不依赖场景 DebugTuner 是否挂**，GM 发现面在 dev 构建恒可用），Shutdown 对称注销。
`GmScenePage` 四按钮改 `SendAsync` 派发（权限门/拦截器/审计链全覆盖——加载/卸载进审计环；忙碌守卫/
结果落面板/异常不外泄保留；状态展示仍直读服务=只读面）。

**表查询裁决**：配置诊断页运行状态/表查询/产物核对**维持直调**——只读投影零状态突变，命令化只刷
审计噪音（与"高频旋钮不命令化"同口径，设计文档 §2.3/§6 边界落定）。

- **L1 接入（首例 GM 命令处理器直测）**：`ISceneService.cs` + `GmSceneCommands.cs` 源链接进
  `LiteFramework.Core.Tests`（UniTask 经 LiteFramework.Async 双轨；`LITEFRAMEWORK_DEBUG` 宏激活——
  既有测试源无条件编译段，影响面已核查）。
- **用例**：`GmSceneCommandTests` 4 件——四命令派发到对应操作+审计进环 / 空 location 拒绝且零派发 /
  GmOnly 在 release 语义（gmEnabled:false）下拦截零调用 / 发现面四命令齐备+注销器对称可重注册。
- **验证**：定向 4/4；L1 等价全量 **1606 全绿**（LiteFramework 715 · LiteSim 343 · LiteNet 406 ·
  LiteTesting 7 · MetaServer 135）；BuildHash `--check` 双常量不变（LiteGame/App 不在共享源集）；
  Unity 侧（`ContainerModule`/`GmScenePage`）盲改归 L2 编译验证。

### 追加批 5：本地联测页（第五页）与「依赖与烙入」显示修正（2026-10-16）

**页件交付补记**：第五页 `GmLocalChainPage`（`4a40b485`）先前未入进度记录，本批补记——`deploy/.env` 单源驱动的联测环境面：两端状态探针 / Start-Stop / 日志尾随 / init，自管 `EditorApplication.update` 1s 心跳（编辑态可用，不依赖 Play 门禁）。

**本批修正**（用户实机回报「依赖与烙入」块显示异常，逐项实机对照）：
- **状态标记恒被裁**：`Marker()` 返 `"[ON ]"`（5 字符）而列宽 24px ⇒ 右括号裁掉（截图实证）；改四字符 `"[ON]"/"[--]"` + `MarkerWidth=32` 常量（Meta/Room/Mongo 三行同治）。
- **多行值裁半行**：`deploy.args` 值走 `EditorGUILayout.LabelField(label, value)`（按单行算高）——两行值第二行只画出上半且压到按钮行；改 `DrawWrappedValue`（`EditorStyles.textArea.CalcHeight` 按宽算高 + `GUILayout.Height` 的 TextArea）。实测：窗宽 780 两行完整；宽 200 时 `CalcHeight`=61.4（4 行）——窄窗折行不裁（`wordWrap=True` 已验证）。
- **依赖行语义**：`MongoDB` 裸标签 → 带端点与状态一行（`localhost:27017 TCP 可达` / `不可达——Meta 起不来` / `未配（持久化关——Meta 内存态可起）`）；原实现在持久化关时同样亮绿灯，读者无从区分“可达”与“无依赖”。
- **同文件小债顺手收口**：`PortOf()` 每重绘重读 `.env`（端口已进快照，函数删除）；`17777`×3 → `DefaultRoomPort`（引 `ProcedureMatch.TestPort` 单源）；日志目录/文件名/尾块字节单源（原 `LogDirRelative` 死常量启用）；`27017`/`4096` 常量化。

**验证**：recompile 0 错；OS 级窗口截图对照修复前后（窗宽 780 + 最小 460 两态）；L1 **1700/1700 全绿**（首跑挂 `MSB3027` 文件锁——昨晚 extloop 回环批遗留的两个 Release 服务进程占着 `Server/*/bin`，停掉后重跑即绿；本批未触共享源集，BuildHash 无需重算）。

**边界（如实）**：页内探针按**进程名**识别两端——页内 Start 的 apphost 与 `local-chain.ps1` 的 `dotnet run`（实测进程名 `MetaServer`/`RoomServer`）均可见；`dotnet <dll>` 直起形态（extloop 回环脚本用法）进程名为 `dotnet`，页上显示 not running（非本页管辖路径，不修只记）。

**续记：联测两修（2026-10-16 同日）**——用户实机联测回报两处页面缺陷，当场修复：

- **房间服状态误报**：`UdpBound()` 的"绑得动 = 空闲"判据在 Windows 不成立（UDP 多重绑定默认允许、Mono 的 `UdpClient` 不设排他——实测服务器在跑时 `new UdpClient(17777)` 仍绑得上）⇒ Room 行恒显 `[--] process alive, port not bound`（进程在跑却报"端口未绑定"）。改**枚举 `System.Net.NetworkInformation.IPGlobalProperties.GetActiveUdpListeners()` 判端口占用**；Unity 运行时反射实测 `UdpBound(17777)=True / (19999)=False`。
- **日志区滚不动**：两个日志块用 `TextArea`（会接管滚轮）＋滚动区仅 `MinHeight`（无高度上限，内容一长即撑开不可滚）。改 **`SelectableLabel`（不接管滚轮、仍可选中复制）＋ `GUILayout.Height(240)` 限定滚动区**，内容全量展开、滚动交外层；另按用户回报加 **`meta.log`/`room.log` 分块标题**（两端日志连排不可辨）。
- **顺带清同文件已登记小债**："日志尾随主线程同步读" → 并入后台探针快照（1s 节拍；`Draw` 不再做文件 IO）。客户端审计表同步登记/回写。

**验证**：recompile 0 错（`errors:[]`，双判据）；`UdpBound` 反射实证（在跑 = True / 空闲 = False）；页面观感（滚动/状态行）归用户面检。

**续记 2：登录页点击排障（2026-10-16 同日）**——用户实机"登录页点了没反应"，pipeline 探针（编辑态配置链 + Play 态按钮可达性/射线/模拟点击）定位两件：

- **根因（客户端侧）**：五个产品页 prefab 的根 Canvas **缺 `GraphicRaycaster`**——EventSystem 射线命中为空（实测 `hitsTopDown=[]`、canvas `gr=NONE`；正控：全项目仅 `Hud_Battle` 有射线器，为退场链批当年补的）⇒ **所有页面按钮都点不动**。修复：五页各补 `GraphicRaycaster`（幂等修复器）；Play 实测射线命中恢复 → 模拟点击 → **真 Meta 游客登录成功 → 换页大厅**（console `[Nav] Replace id=12` + 截图）。已登记客户端审计（含"EditMode 结构守卫"建议）。
- **面板捕获随域重载断裂（挂账）**：`LaunchCaptured` 的 stdout 管道读取端住编辑器托管域——**任何 recompile（域重载）即断**，子进程照常运行但 `deploy/logs/*.log` 停更（实证：meta.log 冻结于一次重编译的秒级时刻，同时刻 `/live` 与请求应答均正常）。候选修法＝起进程改 OS 级重定向（`cmd /c "... > log 2>&1"`）；短期口径＝重编译后面板 Stop/Start 重启两端。
- **另发现（待核）**：206/201 的 `Tmp_Dialog` 为空占位（单节点）——设置页退出的确认弹窗当前是空表单；文档称"预留槽"，语义待用户核。

### 待办

- L2（EditMode/PlayMode）夜间流水线；GM 面板五页手测（翻页生命周期/开关/滑杆对局实弹/爆头可视化随动）；场景页对 Play 中命令派发实测；本地联测页——显示/探针读数与起停运行态已实机核（追加批 5），日志尾随运行态经**续记两修**一并收口（2026-10-16）。
- ~~场景操作/表查询命令化（待议）~~ 已落定（追加批 4）；DevHUD 命令通道待真机需求。

---

## 第2章 日志标签收敛（LogTag 枚举）

<a id="ch2-日志标签收敛"></a>

> **原文件**：`Docs/施工进度/日志标签收敛.md`（归并前独立成文，内容整段并入本章）

《[日志标签收敛专项设计（已归档）](../归档记录/2026-10-22/日志标签收敛专项设计.md)》实施批（2026-10-16，用户点单"实施日志收敛专项设计"）：客户端 `Log` 门面 tag 从裸字符串收敛为**编译期封闭的枚举词表**（`LogTag`）——原形态 33 个散值 / 197 处调用点，只靠 `Log.cs` 一句注释约束（红线 5"必须与 X 一致只靠注释"违规形态），且 `"Setting"`/`"SettingService"` 同域双值漂移实锤。

> 本记录只保留最新进度与证据；设计裁决在各自设计文档，本文件不裁决设计。

### 状态

**已完成（附验证证据）**——2026-10-16。

### 交付

| 面 | 落点 |
| --- | --- |
| 词表（单源） | 新件 `Assets/LiteFramework/Scripts/Core/Logging/LogTag.cs`：`enum LogTag`（`None = 0` + 32 项）＋ `Log.TagName(LogTag)` 显示名单源（switch 表达式、无 default 臂、`None => ""`）；`Log` 类同批改 `partial`（枚举与显示名同文件，加枚举项与加显示名在同一处评审） |
| 门面（`Log.cs`） | `Info/Warning/Error/Fatal(string message, LogTag tag = LogTag.None)`；`Error/Fatal(Exception ex, string context = null, LogTag tag = LogTag.None)`；**string tag 重载同批全删**（不设过渡期——封闭性即本批全部价值）；`LogEntry.Tag`：`string` → `LogTag`（`null` → `None` 语义）；契约注释重写为"词表＝枚举单源；动态信息进 message；新增 tag = 加枚举项"；顺手修 `Log.cs:12` 审计债（TickCount 回绕注释 49.7 天 → 有符号 int 约 24.9 天变负） |
| 零变化面（逐项保持） | 级别过滤 `_minOutput`、环缓冲容量 32 与记录/滚出行为、`ILogHelper` 接口签名、`ErrorCount`、输出拼接格式（`[tag] ` 前缀；None 无前缀——显示面行为与迁移前逐字一致） |
| 迁移（调用点） | **191 处**（＝设计盘点全集）：189 处 tag 位字面量 → `LogTag.X`（脚本按偏移精确替换，保编码/行尾）；2 处「模块名落 context 位」（`SettingService.cs:43`、`MainThreadDispatcher.cs:21`）→ 归位 tag 位（`Log.Error(ex, null, LogTag.X)`）。另 `VfxService` 私有常量 `Tag`（`"VFX"`，6 处调用点**经常量转传**、设计盘点口径漏项）改 `private const LogTag Tag = LogTag.Vfx` |
| 漂移收敛 | `"Setting"`（`Setting.cs:43`）与 `"SettingService"`（`SettingService.cs:43/:128`）→ 单一 `LogTag.Setting`（显示名取域短名，与 `FileSys`/`Event` 同风格） |
| 消费面 | `DevHUD.cs:106`：`e.Tag != null` → `!= LogTag.None`；`[{e.Tag}]` → `[{Log.TagName(e.Tag)}]`（输出格式不回退：`[tag]` 前缀、None 无前缀） |
| Lua 桥 | `LuaComponent.BindLog` 三行 + `Shutdown` 传 `LogTag.Lua`（不向 Lua 侧暴露 tag 参数的契约不变） |
| 测试面 | `LogTests` 断言改枚举 ＋ **新增 2 例**：① 词表穷尽守卫——遍历 `Enum.GetValues<LogTag>()` 全量调 `TagName`（非 None 断言显示名非空；漏臂即抛/即红）② 输出前缀格式——伪 `ILogHelper` 捕获（None 无前缀、非 None `[tag] ` 前缀）；`UiU0EditModeTests` 3 处 ＋ PlayMode 3 文件 × 3 处（logTable）改 `LogTag.Lua`（这 5 个文件无 `using LiteFramework;`——沿用其限定名风格写 `LiteFramework.LogTag.X`） |

**改动面**：60 个既有源文件 + 1 个新件（`LogTag.cs` 及其 `.meta`，Unity 刷新生成）；不改 message、不改级别、不改调用点位置。

### 与设计稿的偏差（如实登记）

| 项 | 偏差 | 处置 |
| --- | --- | --- |
| 现值全集 | 设计盘点口径（"tag 位为标识符形态字符串字面量"）漏了 `VfxService` **经常量转传**的 `"VFX"`（6 处）——实际现值 33 个（非 32） | 词表补 `Vfx`（32 项）；**显示名 `VFX` → `Vfx`**（枚举名即显示名；与 `SettingService`→`Setting` 同款收敛先例）。若要保留全大写，改 `TagName` 单源一行即可 |
| 两处 context 位 | 设计把 `Log.Error(ex, "SettingService")` / `Log.Error(ex, "MainThreadDispatcher")` 计为 tag 位；按现行签名它们是 **context 位**（迁移前显示面**无** `[tag]` 前缀） | 按设计迁移表归位为 tag——模块名从 message 前缀改为 tag 前缀，符合"模块名进 tag"的 tag 契约；2 处显示形态变化已在设计稿 §4 备注 |
| 计数 | 设计稿 191 处＝189 字面量＋2 context 位；另 6 处常量转传调用点随常量声明一次收敛 | 设计稿 §1/§3.1/§4 同批回写（补 `Vfx` 行与说明） |

### 验证证据

| 项 | 结果 |
| --- | --- |
| L1 门禁 | **[PASS] L1 completed 1702 tests**（含本批新增 2 例；总数含并行会话增量）——`pwsh scripts/gate/test.ps1 -Lane L1 -Profile PullRequest` |
| UnityEngine 重编译 | **0 错误**（双判据：`recompile_status` = `up_to_date`/failed=false/errors 空 ＋ `console_status` 地面真值 `compilationFailed=false`）。*过程如实*：首两轮曾报 CS0246×2/×4，逐条核在**并行会话在途文件**（`LaunchLoading.cs`/`ProcedureMatch.cs` 缺 `using LiteGame.UI;`），本批改动文件零条目；并行线补齐 using 后复编译清零 |
| 定向复跑（Unity） | `UiU0EditModeTests` **6/6 通过**（覆盖改动的 Lua logTable 桥接 3 行）；3 个 PlayMode 文件同款改动**留夜间 L2**（白天不跑 PlayMode 口径） |
| 定向复跑（dotnet） | LiteNet.Tests 单跑 578/578（首轮全 lane 一次时序抖动失败，复跑不复现，非本批面） |
| BuildHash | **不涉及**——改动面（`LiteFramework/Scripts/Core`、`LiteClient` 适配、`LiteGame`、`LiteView`、`Tests`）均不在共享闭包；`Serialization.Luban` 仅 `SimConfigMapper.cs`/`TableLayout.cs` 在册，本批未动；无协议/表源改动 |
| doc-lint | **OK**（0 violation / 0 warning）——`python scripts/gate/doc-lint.py` |
| 终扫 | 全仓 tag 位**字面量 0 处**（脚本全量解析；验收"无 string 第二参"以编译期封闭达成） |
| 质量审计表回写 | 客户端卷：新登记节 2 条 + 修复登记 2 行（tag 契约机制化、TickCount 注释）；服务端卷：`HostLog` area 形态登记（范围外，归属 R4 OTel） |

### 裁决与边界（如实）

- **无过渡期**：string tag 重载同批删除是本批封闭性的唯一来源；类型变更强制全量同批（设计 §2 被否备选记录）。
- **`TagName` 含 `None => ""` 臂**：不是 default 臂——全 33 项显式覆盖（零 CS8509 告警），漏臂仍由编译告警 + 穷尽守卫测试双拦；调用方要不要出前缀仍按 `!= LogTag.None` 判定（`Log.Emit` 与 `DevHUD` 同口径）。
- **服务端 `HostLog` area 不在本批**：待 R4 OTel 结构化日志接线统一收敛（`Server/RoomServer/HostLog.cs` 已声明归属）；两端参数位不一致（客户端 `message,tag` / 服务端 `area,message`）随该次对齐。
- **tag 过滤/开关功能**：枚举身份已备，本批不预置（设计 §5）。

### 并行会话说明（环境事实，非本批内容）

执行期间树内有并行在途改动（加载页批B：`LaunchLoading.cs` 新建、`FeedbackService` 扩 `LoadingScope`、五页 prefab/Boot.unity 等），期间 Unity 编译一度为其在途文件报 CS0246（本批文件零条目）；其补齐后本批复编译 0 错。**提交状态**：并行线提交 `68c3feb0` 扫入本批两处迁移件（`ProcedureError.cs`/`ProcedurePreload.cs`）；其余 58 件随同一提交入库（`249c1ac1`，与并行线 WIP 合流提交——文件面未解耦，属用户裁决"提交本地全部改动"）。

---

## 第3章 联机诊断 HUD（DevHUD 段＋GM 第六页＋测试包 Development 化）

<a id="ch3-联机诊断HUD"></a>

> 用户点单"GM 面板增加显示联机诊断数据的局内 HUD，通过 GM 面板打开"，并问测试客户端能否打开。**状态：已完成（附验证证据）**。

### 现状核证（回答"测试客户端能打开吗"的三层事实）

- **GM 面板**＝`EditorWindow`（`Assets/LiteGame/Editor/`）——编辑器专属**形态**，任何包都进不去（与宏无关）。
- **开发页**（`DevMenuPanel`）：开出动作 `EnsureDevMenu()` 整段 `#if UNITY_EDITOR` 收口＋dev 预制体不进收集组——**连 Development 包都不出现**（入口分叉治理既定裁决，非缺漏）。
- **出包链三宏全灭**：`build-player.ps1` → 管线 `build` 不传 `--options`（缺省仅 `DetailedBuildReport`），profile 的 `channel:"development"`/`defines:[]` 均为**死字段无消费方**——现有测试包是净 release 形态，连 DevHUD 都被剥离。
- 结论：**现状开不了**；载体与数据源是现成的（§5 DevHUD 自创建形态＋`RoomClient.NetworkQuality` 探针——类注释原文"供 HUD 展示"）。用户裁决：启用路径取 **A（Development 包）**，数据面全选（相位/重连、和解计数、帧率/掉帧、既有 Stats 段保持）。

### 交付

| 面 | 落点 |
| --- | --- |
| 对局诊断读口 | 新件 `Assets/LiteGame/App/Debug/BattleDiagnostics.cs`（三宏整件剥离）：`Current` 登记口（`Attach/Detach`——只注销自己登记的实例）＋只读快照 `Snapshot`（相位/网络质量/和解计数）＋描述单源 `DescribeLines/DescribeCurrent`（RTT/丢包口径与 `BattleNetworkHudDriver` 同源；无对局 = null ⇒ 消费方整组隐藏）。**为什么静态登记**：对局上下文是流程私有件（不进容器、非场景组件），HUD 两发现面都够不到——同 DebugTuner 静态镜像口径 |
| 接线 | `BattleViewRig.Attach`（既有三宏块内 `BattleDiagnostics.Attach(context)`）＋ `Dispose`（`Detach`）——装配/拆视图两处，单写点 |
| DevHUD 段 | 新段"联机诊断"（读口单源行，两行缩进同 HUD 风格）＋新段"性能"（帧间隔环形样本 240 帧自算：均值 FPS/最差帧/长帧(>100ms) 计数——阈值推导注释在件内）；逐帧采样先于 0.25s 渲染节流（长帧计数依赖连续样本）；`Visible` 公开属性（GM 页投影位，F1 等效）；段开关 public 字段形态不变 |
| GM 第六页 | 新件 `Editor/Gm/GmNetDiagPage.cs`（IGmPage 契约）：HUD 总开关/段开关投影（编辑器工具豁免直改 DevHUD 实例字段）＋读数投影（同一描述单源，页零拼装零状态）；`GmPanel` Tabs/_pages 补位（六页）＋壳注释页数清单同步；`LiteGame.Editor.asmdef` 补 `LiteGame.DevTool` 引用 |
| 出包链接活 | `build-player.ps1`：`channel == 'development'` → build/dry_run 经 `Invoke-EvalFile` 直调 `Unity.Pipeline...BuildCommand.Build(options: ["Development","DetailedBuildReport"], …)`——**eval_file 缘由**：`options` 是 string[]、CLI 令牌形态为 JSON 数组，PS 5.1 原生传参剥引号（同文件后端切换步骤既有先例，CommandLineBinder 实读核证"no repeated-flag-becomes-array"）；顺手补 dry_run 的 `valid=false` 不拦截缺口（原只查 status/success） |
| 守卫 | 新件 `Assets/Tests/EditMode/BattleDiagnosticsEditModeTests.cs`：描述单源三用例（千分比→一位小数百分比与生产 HUD 同口径、无样本占位、无对局 null 空态） |
| 清污（连带发现） | `Boot.unity` GameEntry 上**残留场景序列化 DevHUD 组件**（`showStats:0` 等存量值——自创建幂等守卫被劫持为"收养旧实例"，HUD 段默认值被场景旧值覆盖；且踩 DevHUD 类注释明写禁项：release 包三宏剥离后场景组件成 missing script 隐患）。编辑器 API 摘除＋场景保存；盘面 GUID `8714eaad…` 零残留核证 |

### 验证证据

| 项 | 结果 |
| --- | --- |
| Unity 编译 | **0 错**（bridge refresh，16 警告全为既有：Odin 过时 API/老测试件 CS0628） |
| EditMode 定向 | **47/47**（新件 3 用例＋BattleNetworkHud/PresentationLifecycle/PresentationOwnership/BattleSession/BattleHudMenu 回归） |
| L1 等价件全量 | **零失败**：LiteFramework 720 · LiteSim 369 · LiteNet 619 · LiteTesting 7 · MetaServer 178 · MetaIntegration 37（36 skip 环境位）——含纪律扫描（新注释过卫生守卫） |
| PS 5.1 Parser | **PARSE-OK**（`build-player.ps1` 逐文件静态解析 0 错；代码行非 ASCII 零——既有横幅装饰注释不在此列） |
| build 命令契约 | 反射直调 `BuildCommand.Build(dryRun, options:["Development","DetailedBuildReport"])` → `status=dry_run valid=True`（FQN/参数契约/校验全通，未真出包） |
| 运行态冒烟 | Play（Boot）：`[DevHUD]` 自创建（DontDestroyOnLoad）、性能段样本窗 240 帧滚动自愈（FPS 172/最差 13ms/长帧 0）、联机诊断段无对局正确隐藏、`DescribeCurrent=null` 空态 |
| 清污回归 | 摘件后重进 Play：全新 `[DevHUD]` 实例全开关 true、**stats 段＋日志尾巴＋性能段全渲染**（cacheLen 1931）；录屏分析确认 HUD 与登录页叠加正常、无报错 |
| BuildHash | **不涉及**——改动面（LiteGame/Tests/scripts/Boot.unity）不在共享源集闭包 |

### 裁决与边界（如实）

- **DevMenuPanel 包形态不开＝维持既定收口**（本批不改动）——包形态 HUD 唯一入口是 F1；GM 面板"打开"只在编辑器形态成立。
- **挂账**：Dist 测试者分发包是否随 channel 字段一并 Development 化（包体/泄漏面权衡）——待用户实机裁决后接 Dist 线；GM 联机诊断页与开关投影的面检归用户手测；DevHUD 命令通道仍待真机需求（第1章待办延续）。
- **BattleViewRig.cs 文件头双 BOM→单 BOM**：文件写入顺带的字节规整副产物（原文件头异常），随本批入册。
- **环境备注**：本会话 shell 工具故障（`C:\Windows\system32\powershell` 路径异常，三试三败）——PS Parser/dotnet 全量/git 盘点均经 Codely bridge 编辑器子进程等价执行（本机无 PS7 的 L1 等价件先例见第1章）。

### 裁决续记（2026-10-18 同日）：Dist 测试者分发包不随 Development 化

用户裁决落定：**Development 包仅限开发侧测试客户端，不进测试者分发包**。机械面：本批后 `windows-x64.json`（channel=development）出的包全是 Development 形态，而 `Dist/New-Package.ps1` 默认从 `Builds/StandaloneWindows64` 取包——测试者包若无独立通道档必然继承 dev 面。落法：

- 新增 release 通道档 `BuildProfiles/windows-x64-release.json`（channel=release，产物独立目录 `Builds/StandaloneWindows64-release`）。测试者出包流程＝先 release 档出包 → Dist 打包传 `-GameBuild Builds/StandaloneWindows64-release`（Dist 脚本本体归并行上云线在改，本批不碰——接线归该线收口时引用本档）。
- `build-player.ps1` eval 调用补 `outputPath` 透传（`profile.output`＋包名编辑器侧解析）——两通道产物目录独立、互不覆盖：development 包永不出现在测试者包源目录，边界由目录隔离兜底而非流程自觉。
- 验证：PS Parser PARSE-OK · 代码行纯 ASCII · release 档 JSON 可解析（channel/output/platform 核对）· build 命令 dry run 两形态均 valid=True（dev：Development 选项＋默认目录；release：无选项＋release 目录）。

---

## 第4章 房间回放与现场包（工具链 v2 第一刀）

<a id="ch4-房间回放与现场包"></a>

> 工具链 v2 第一刀（2026-10-21）。**状态：已完成（附验证证据）**——L1 1801/1801；定向 16/16；Unity 编译 0 错；EditMode 1/1；BuildHash `--check-shared` OK。

### 为什么不是纯输入回放

在线权威链在 `SimStep` 之后执行延迟补偿（`LagCompensator.CompensateFire`）与 `MatchSystem.Tick`，且开火输入携带 `viewFrame/ackSnapshot`——这些上下文不在 `SimInputFrame` 里。纯输入回放会漏掉延迟补偿/席位/规则上下文，必然假分歧。本刀改为**房间命令日志 + 真实 `RoomRuntime` 重放**：录制有序命令、逐帧 checksum、规则事件；重放构造同配置房间按序喂命令，不复制 Step 编排。`SimRunRecord/SimReplayRunner` 保留给离线纯 Sim/批跑，不接在线。

### 交付面

| 面 | 落点 |
| --- | --- |
| 内核记录 | `Assets/RoomServer/Runtime/Diagnostics/RoomRunRecord.cs`（记录/事件/DTO）、`RoomRunRecorder.cs`（深拷贝输入、命令/步进/规则上限、截断）；`RoomRuntime.cs` 在 `Execute`/`SetTestRules`/`StartMatch`/`StepFrame` 接线并暴露 `TakeRunRecord()`。纯内存、无 IO/proto/LiteNet，R11 通过 |
| 宿主信封 | `Server/RoomServer/Diagnostics/RoomRunEnvelope.cs`：JSON+GZip（magic+schema）、配置/地图/规则 DTO、`buildHash/fixedConfigDigest/mapDigest` 身份闸 |
| 存储 | `RoomRunStore.cs`：同卷原子写（tmp→Move）、文件数/总字节保留上限、失败只计数返回 null |
| 重放 | `RoomReplayRunner.cs`：重建同配置 `RoomRuntime`（MatchId/Seed/gameplayDigestBase 同源），逐步比对全量 checksum；输出首分歧帧/事件序号/命令序号；截断与身份不符显式报告 |
| 服务端现场包 | `RoomDiagnosticsService.cs`：房间终态静默后取记录，补地图快照、`HostLog` 尾、最近 mismatch 摘要并落盘；`RoomServerConfig` 可选 `diagnostics` 分区；`HostLog` 增 256 行环形尾 |
| CLI | `Server/ReplayTool/`：默认加载仓库真实表并复算 `fixedConfigDigest`；`--defaults` 只用于默认表/离线记录；退出码 0/2/3/4/5 |
| 客户端现场包 | `LiteFramework/Scripts/Core/Diagnostics/DiagnosticsBundleWriter.cs`（零引擎、限频/限量/原子目录写出）＋`LiteClient/Runtime/Diagnostics/ClientDiagnosticsBundle.cs`（Unity 请求组装）＋`BattleContext` 在 checksum mismatch / `StartGame.ConfigHash` 不符时触发 |
| 测试 | `RoomRunReplayTests`(5) · `RoomRunCodecStoreTests`(2) · `RoomDiagnosticsServiceTests`(3，含配置解析) · `ClientBundleTests`(2) · `ReplayToolCliTests`(4，含真实仓库表路径) · EditMode `ClientDiagnosticsBundleTests`(1) |

### 验证证据

| 项 | 结果 |
| --- | --- |
| L1 全量 | `test.ps1 -Lane L1 -Profile PullRequest` → **1801/1801 通过**（含纪律扫描/表守卫） |
| 定向 | 回放/编码/存储/服务端/客户端/CLI → **16/16** |
| Unity 编译 | pipeline refresh + recompile → **0 错**（`compilationFailed=false`） |
| EditMode | `ClientDiagnosticsBundleTests` → **1/1** |
| BuildHash | `gen-build-hash.py --check-shared` → **OK**，shared `459a3145904693b7`（110 文件）；本批未改共享源集 |
| CLI | `ReplayTool` 构建 0 警告/0 错误；退出码 0（完整/真实表）、2（篡改）、3（身份不符）均有 L1 用例 |
| 落盘 | 服务端/客户端现场包均有临时目录用例：文件存在、可解码、原子写无 `.tmp`、保留上限生效 |
| Doc | `python scripts/gate/doc-lint.py` → **0 violation / 0 warning** |

### 边界与未决（如实）

- 服务端记录只在**房间终态静默后**取走并落盘；mismatch 当场不写（避免跨线程读半截记录），其帧号随终态记录落盘。即时 flush 需“房间线程封印 → 宿主异步写”的通道，归后续。
- 本刀只给首分歧帧 + checksum + 命令序号；字段级 world-ring diff 未落盘，归后续。客户端包不含完整重放（权威重放以服务端记录为准）。
- 崩溃捕获/minidump、IL2CPP 符号归档、产物上传/GM 下载端点未做；均属路线图 T0 后续退出项。
- CLI 默认按仓库表目录装载；部署在非仓库目录或表集不同时，需后续加 `--tables <dir>`；当前身份不符会以退出码 3 拒绝，不会静默跑偏差。
- `diagnostics` 缺省关闭、行为零变化；`deploy/roomserver.windows.json` 已启用并指向 `C:/litegame/room/diagnostics`，**下次云侧发布/重启才生效**（本批未部署）。

---

## 第5章 现场闭环 T0-2（世界环字段级分歧 · mismatch 即时 flush · 崩溃捕获 · 符号归档 · GM 下载）

<a id="ch5-现场闭环t0-2"></a>

> 工具链 v2 第二刀（2026-10-21，接手批）。**状态：已完成（附验证证据）**——L1 1816/1816；Unity 编译 0 错；EditMode 定向 4/4；BuildHash 双常量 OK。

第4章留下的 T0 后续退出项在本章收口：字段级 world-ring diff、mismatch 即时落盘、崩溃捕获/minidump、IL2CPP 符号归档、GM 诊断下载通道。

### 交付面

| 面 | 落点 |
| --- | --- |
| 世界存档环 | `RoomRunRecord` 增可选世界环（`SimWorldState` 按帧深拷、容量有界、槽帧号成对）；`RoomRunRecorder` 按 `diagnostics.world_ring_frames` 启用；分歧帧早于环窗口时只有帧号（与 `SimRunRecord.BeginWithWorlds` 同一取舍） |
| 字段级分歧报告 | `RoomRunEnvelope` 增 `RoomRunWorldDto`（只承载进 `SimChecksum` 口径的字段，覆盖契约由 `SimWorldDiffCoverageTests` 守卫）；`RoomReplayRunner` 在分歧帧 `SimWorldDiff.Compare(录制世界, 重放世界)` 并把报告附进 `DivergenceReport`/`Message` |
| mismatch 即时 flush | `RoomDiagnosticsService` 增后台写线程 + 工作队列：客户端和解上报 → `ShouldRequestFlush`（同房 10s 冷却）→ 房间线程下一 `StepFrame` 末封印快照记录（身份面此刻捕获、线程上零 IO）→ `EnqueueMismatchFlush` 入队 → 后台组装信封落盘（EndReason=`mismatch-flush`）；`Dispose` 排干队列；终态记录照常兜底 |
| 客户端异常落包 | `BattleContext` 挂 `logMessageReceivedThreaded`：Exception/Assert 级 → `ClientDiagnosticsBundle.CaptureUnityException`（异常文案+堆栈尾进 `extra.txt`；Log/Warning/Error 不捕获防刷爆限频）；写盘与 mismatch 路径共锁 `_diagnosticsGate`；`Capture` 补传 `ExtraJson`（writer 侧 `extra.txt`/manifest 清单通道已就位） |
| 崩溃捕获 | `Server/RoomServer/Diagnostics/CrashCapture.cs`：进程级 `UnhandledException` → `crash-<时间戳>-<随机>.json`（异常/堆栈 + HostLog 尾 + buildHash + 运行时身份），Windows 尽力附 minidump（dbghelp）；只装一次、崩溃路径零抛、不捕房间记录（跨线程封印不安全，即时 flush 与终态路径兜底）；诊断启用时由组合根安装，缺参 = 不装 |
| 符号归档 | `Assets/LiteGame/Editor/Toolchain/SymbolArchiver.cs`：构建产物内 `*.pdb/.dbg/.sym` 按 buildHash 归档 `Builds/symbols/<hash>-<时间戳>/`（相对结构保留 + `manifest.json` 文件/大小/SHA256）；`SymbolArchivePostBuild`（`IPostprocessBuildWithReport`）构建后自动归档，失败只告警不反噬构建；符号不入库 |
| GM 下载通道 | `GmHttpListener` 新增 `POST /gm/diag-list`（文件名/大小/修改时刻）与 `POST /gm/diag-fetch`（`{"file":"name"}` → octet-stream 回传）；文件名只按目录直下解析（分隔符/穿越 = 400）、不存在 = 404、超 64 MB = 413、端点未开 = 404；读面由 `RoomDiagnosticsService.ListFileSummaries/ResolveFile` 提供（名字即能力边界） |
| 接线 | `ServerHost`/`Program` 组合根串起 `RoomDiagnosticsService`→`RoomInstance`→`RoomRuntime`（`onSnapshot` 回调房间线程只入队）；`CrashCapture.Install` 与 GM 诊断读面挂接 |

### 验证证据

| 项 | 结果 |
| --- | --- |
| L1 全量 | `test.py --suite L1 --profile PullRequest` → **1816/1816**（原 1801 + 本批 15：CrashCapture 3 · GmHttpDiag 5 · RoomMismatchFlush 4 · RoomRunWorldRing 3，ReplayToolCli 扩世界环面） |
| Unity 编译 | pipeline recompile → **0 错**（修接手时存量错误：`SymbolArchiver` 误用不存在的 `IPostprocessBuildCallbacks` → 改 `IPostprocessBuildWithReport.OnPostprocessBuild(BuildReport)`） |
| EditMode | `SymbolArchiverEditModeTests` 2/2 · `ClientDiagnosticsBundleTests` 2/2（修接手时存量红：`Capture` 未把 `extraJson` 传进请求对象，`extra.txt` 缺件——一行修复） |
| BuildHash | `--check` 双常量 OK：shared `459a3145904693b7`（110 files）· meta `a149cc4b1906e9f7`（46 files） |
| Doc / [Diag] | doc-lint **0/0**；本批触碰文件 grep `[Diag]` **零命中** |
| 顺手修 | 审计服务端表债①（`GmHttpListener.ReadBody` 先 `ReadToEnd` 再判 4KB）补 `ContentLength64` 前置检查；新 `ReadDiagFileName` 原样复刻该同型问题，改为复用 `ReadBody`（见修复登记） |

### 边界与未决（如实）

- 世界环容量由 `diagnostics.world_ring_frames` 配置（0 = 关）；分歧帧早于窗口时仍只有帧号，报告会提示调大容量。
- 崩溃件不含房间记录（跨线程封印不安全）；mismatch 即时 flush 的身份面在房间线程捕获，写盘全部在后台线程——服务崩溃时未落盘的 flush 队列项会丢失（终态路径兜底的前提是进程还活着）。
- minidump 用 `dumpType=0`（普通级）；需要堆细节时再议 `MiniDumpWithIndirectlyReferencedMemory` 等枚举位。
- `diag-fetch` 上限 64 MB 是回传上限而非存储上限；存储侧保留策略归 `RoomRunStore` 既有上限。
- 符号归档只在真实出包时验证了单元面（EditMode 临时目录），IL2CPP 全量出包链路未实跑（属机器侧验收，见资源与工具链线第 22 章边界）。
- 客户端 `logMessageReceivedThreaded` 在 IL2CPP Player 的线程语义与 Editor 一致性未实测。

### 回滚

- 还原 `RoomRunRecord/RoomRunRecorder/RoomRuntime`（世界环与 onSnapshot 回调）、`RoomDiagnosticsService`（后台写线程与 GM 读面）、`GmHttpListener`（两端点）、`CrashCapture.cs`/`SymbolArchiver.cs`（删除）、`BattleContext`（异常钩子）、`DiagnosticsBundleWriter/ClientDiagnosticsBundle`（ExtraJson 通道）；无数据迁移、无云侧状态变更。

## 第6章 DevHUD 可用性修复与日志关联（T1 第一刀）

<a id="ch6-devhud可用性与日志关联"></a>

> 2026-10-21。**状态：已完成（附验证证据）**——Unity 重编译 0 错；EditMode 定向 8/8；Play 冒烟通过。

范围：DevHUD 四项低成本修复（采样解耦、缓冲复用、场景重扫 + 销毁件剔除、换行/滚动/字号）；日志条目加 session/match/frame/player 关联字段，HUD 增级别/本局过滤、复制与现场包落盘。

### 交付面

| 面 | 落点 |
| --- | --- |
| 日志关联字段 | `LogEntry` 增 `SessionId/MatchId/Frame/PlayerId`（记录时快照，零分配）；`Log.SetMatchContext/SetFrame/ClearMatchContext` + 只读 getter；`ResetForTesting` 复位 |
| 接线 | `BattleContext.OnStartGame` 登记 `(MatchId, PlayerId)`；`Tick` 每帧 `SetFrame`（int 赋值）；`Dispose` 只清本局（防旧局晚拆清新局） |
| HUD 采样 | `SampleFrameTime()` 前置于可见性判断（隐藏期间性能窗不冻结） |
| 缓冲复用 | `_sb` 复用；`BattleDiagnostics.AppendCurrent(StringBuilder)`（`DescribeLines` 改由同一 `AppendLines` 生成，文案逐字不变）；net-diag 不再建数组 |
| 场景重扫 | `SceneManager.sceneLoaded` → `RebuildStats()`；每轮 `PruneStats()` 剔除已销毁组件（对局场景 Animation/Lua 组件才进得来） |
| 可读性 | `wordWrap` + 滚动 + 字号/宽度 public 字段；布局高度按缓存内容缓存（不每帧 CalcHeight） |
| 过滤/复制 | F3 日志级别（全部→警告+→错误+）、F4 仅本局、F2 复制（`GUIUtility.systemCopyBuffer`） |
| 现场包 | F5 走 `ClientDiagnosticsBundle.Capture`（reason=`hud-snapshot`，与 mismatch 同目录/同信封）；`RecentLogs` 行带 `[m/f/p]` |

### 验证证据

| 项 | 结果 |
| --- | --- |
| Unity 编译 | pipeline recompile → 0 错 |
| EditMode | `LogContextEditModeTests` 3/3 · `BattleDiagnosticsEditModeTests` 3/3（旧文案断言不变） · `ClientDiagnosticsBundleTests` 2/2 |
| Play 冒烟 | 自创建 HUD 在场；`session=d6d22b51 stats=16 cacheLen=1816`；F2 复制后剪贴板 == cache；隐藏期间 `_perfHead` 73→221（采样未停）；`PassLogFilter` 警告+ 真 / 错误+ 假；F5 产出 `.../diagnostics/diag-*-hud-snapshot-*` |
| BuildHash | 未触共享源集/协议/表（LiteFramework/LiteClient/LiteGame 不在闭包）→ 无需重算 |

### 边界与未决（如实）

- 关联字段是“记录时快照”层；结构化 sink、服务端 `HostLog` 对齐、按 match/frame 的日志检索面仍归 T1 后续。
- 热键仍走旧 `Input`（工程 `activeInputHandler=Both`）；切 New only 前需兼容层。
- F5 依赖 `BattleDiagnostics.Current` 在册；无对局只提示不落盘。
- IMGUI 仍是电脑测试形态（不做安全区/DPI 适配）；长文本靠滚动。

### 回滚

- 还原 `Log.cs`（LogEntry 字段 + 上下文静态）、`BattleContext`（三处接线 + 两个只读属性）、`BattleDiagnostics`（快照字段 + AppendCurrent）、`DevHUD.cs`、`ClientDiagnosticsBundle`（日志行上下文）、新增 `LogContextEditModeTests`；无数据迁移、无生成物重跑。

## 第7章 结构化日志 sink 与检索（T1 第二刀）

<a id="ch7-结构化日志sink与检索"></a>

> 2026-10-21。**状态：已完成（附验证证据）**——Unity 编译 0 错；L1 全量 PASS；Python 检索用例 7/7；Play 冒烟：JSONL 落盘、运行中可读、字段可过滤。

范围：日志结构化出口（JSONL）+ 检索 CLI；同批收口“诊断现场包限频逐次 Warning”刷屏。

### 交付面

| 面 | 落点 |
| --- | --- |
| `ILogSink` | `LiteFramework/ILogSink`（`Write(in LogEntry)`，非阻塞契约）；`Log.SetSink`，`Record` 内防御性调用（异常不反噬业务） |
| JSONL sink | `LiteFramework/Diagnostics/StructuredLogSink`：有界环 1024（满丢最旧 + `Dropped`）、后台写线程、StringBuilder + char[] 缓冲（不逐行 ToString）、JSON 转义、8MB 轮转/保留 8 件、`Flush(TimeSpan)`、`Written/Dropped/LastError/CurrentPath` |
| 安装 | `LiteClient/Diagnostics/StructuredLogInstaller`（dev 三宏）挂 `persistentDataPath/logs`，`Application.quitting` 收口；`PlatformInfrastructureModule` 启动即装 |
| 检索 | `scripts/logs/query.py`（清单 `logs.query`，category=gm）：`--match/--player/--tag/--level/--frame-min/--frame-max/--contains/--since`、`--limit`、`--json`、`--stats`；目录默认最新、`--all` 合并 |
| 刷屏修复 | `BattleContext.CaptureDiagnosticsBundle` 先 `CanAccept` 判限频：每局首次 Info、后续静默；真 IO 失败仍 Warning |
| 测试 | L1 `StructuredLogSinkTests` 2 例（字段/转义/丢最旧）；`scripts/tests/test_log_query.py` 7 例 |

### 验证证据

| 项 | 结果 |
| --- | --- |
| Unity 编译 | 0 错 |
| L1 | `test.py --suite L1 --profile PullRequest` → **1818/1818 PASS**（原 1816 + 新增 sink 2 例） |
| Python | `test_log_query.py` 7/7 |
| 工具链 | `toolchain.py docs` 重生成索引（含 `logs.query`）；`check` 0 warning；Doc lane 0/0 |
| Play 冒烟 | `log-87d8f268-...jsonl`：80 条（Info 60 / Warning 20）；`--match Room-Test --level warning --frame-min 100` 命中；`--stats` 对局 `{'':40, 'Room-Test':40}`；**游戏运行中直接读最新文件成功** |
| 刷屏修复 | 现场包相关仅 2 条（1 次落盘 + 1 次限频 Info），无逐次 Warning |

### 边界与未决（如实）

- 只覆盖客户端 `Log`；服务端 `HostLog` 未接同一 sink（T1 后续）。
- JSONL 只落本机；取回靠文件拷贝/现场包（GM `diag-*` 端点是服务端目录）。
- 上限：队列 1024、单文件 8MB、目录 8 件；丢弃量由 `Dropped` 可观测。
- release 不装 sink（零开销）；发布包若要日志文件，需另立开关与预算。
- 检索面是 CLI，未做编辑器页/GM 页；`--json` 供脚本消费。

### 回滚

- 删 `ILogSink.cs`/`StructuredLogSink.cs`/`StructuredLogInstaller.cs`/`scripts/logs/query.py`/两个测试件；还原 `Log.cs`（`SetSink` 与 `Record` 调用）、`PlatformInfrastructureModule`（安装调用）、`BattleContext`（限频提示）、清单条目与生成索引；无数据迁移。

## 第8章 DevHUD 的 UI Toolkit 渲染后端（文本桥）

<a id="ch8-devhud-uitoolkit"></a>

> 2026-10-21。**状态：骨架完成（附冒烟证据）**——Unity 编译 0 错；Play 截图：中文可读、盖住 uGUI、`ui=ok`；IMGUI 回退保留。

范围：DevHUD 渲染从 IMGUI 切到 UI Toolkit（整段文本桥），先解决字体/层级/无资产三个前置问题；段化/折叠/过滤/JSON 导出仍归 P0-A 后续。

### 交付面

| 面 | 落点 |
| --- | --- |
| 宿主 | `Assets/LiteGame/DevTool/Hud/DevHudUiToolkitHost.cs`：运行时 `PanelSettings` + `UIDocument`，`sortingOrder=10000`，半透明底板 + Label + 滚轮滚动；三宏包裹 |
| 字体 | 系统 TTF → TextCore SDF（`FontAsset.CreateFontAsset(path, face, 90, 9, SDFAA, 1024, 1024)`）；Windows 候选 `msyh.ttc`/`simhei.ttf`/`simsun.ttc`，macOS PingFang，Linux Noto/文泉驿 |
| 接线 | `DevHUD.renderWithUiToolkit`（默认 true）、`hudTop`（默认 88）、`_ui` 创建/销毁/`Visible`/`Sync`；`OnGUI` 在后端存在时直接返回 |
| 回退 | 构造失败（无可用字体/初始化异常）→ `_ui=null` + `Log.Warning`，IMGUI 原路径继续；`renderWithUiToolkit=false` 可强制 IMGUI |

### 验证证据

| 项 | 结果 |
| --- | --- |
| Unity 编译 | 0 错 |
| Play 冒烟 | `ui=ok`；`[DevHUD-UI]` rootH=1080、panelH=980；截图：中英文读数清晰，面板盖在「开发」钮与结算页之上；合成 `WheelEvent`：下滚 `scrollY=72`、上滚回 `0`；`Visible=false→display None`、`true→Flex` |
| 字体 | `simhei.ttf` 创建的 TextCore face `family=SimHei`；`CreateFontAsset(family, style, 90)` 实测 face 不可用，已弃用 |
| 层级 | `PanelSettings.sortingOrder=10000` > 产品 canvas 最高 6000；结算页上可见（实测） |
| 回退 | 本轮字体存在，未触发构造失败路径；开关回退保留，无字体环境待补验 |

### 边界与未决

- **整段文本桥**（第9章已拆段）：一个 Label 渲染全部读数，尚无 per-line 色调、折叠、文本过滤、F6 JSON；归 P0-A 后续。
- **无主题资产**：内置控件无样式（滚动条不可见），当前只支持滚轮滚动；折叠/输入要自实现或引入 dev-only 主题资产。
- **字体依赖 OS**：Windows/macOS/Linux 候选链；无字体回退 IMGUI（中文由 IMGUI 动态字体保证）。
- **排序耦合**：`10000 > 6000` 是产品 UI 现状；新增更高排序 canvas 时需同步并回归。
- **启动开销**：创建 TextCore SDF（1024×1024）一次性开销未量化；P0-B 计入 HUD 自身开销。
- 无资产，release 由三宏剥离；未新增 UXML/USS/PanelSettings。

### 回滚

- `renderWithUiToolkit=false` 立即回 IMGUI；彻底回滚删 `Hud/DevHudUiToolkitHost.cs`、还原 `DevHUD.cs`（字段/`Start`/`OnGUI`/`Visible`/`LateUpdate`/`OnDestroy` 接线）、删 `hudTop`/`renderWithUiToolkit`；无资产/数据迁移。

## 第9章 DevHUD P0-A：段化/行色调/折叠/DevHotkey

<a id="ch9-devhud-p0a"></a>

> 2026-10-21。**状态：已完成（附验证证据）**——Unity 编译 0 错；Play：五段按 `IDevHudSection` 产出、行色调分级、段标题点击折叠、`DevHotkey` Input System 注入验证通过。

范围：按 [DevHUD 演进路线图 §5.5](../设计文档/测试与性能/DevHUD演进路线图.md) 的 P0-A 第一步，把“整段文本桥”拆成段件 + 行色调 + 折叠 + 热键后端；F6 JSON、过滤、`DiagTrace`/`LogSink` 段仍为 P0-A 剩余。

### 交付面

| 面 | 落点 |
| --- | --- |
| 模型 | `Hud/DevHudModel.cs`：`HudTone`、`HudLine`、`HudContext`、`IHudTextSink`、`IDevHudSection`、`DevHudSectionData`（宿主复用） |
| 段化 | `DevHUD`：`Awake` 注册五段（错误/联机诊断/性能/模块 Stats/Log Recent）；每段 `Append(sink, ctx)` 只产出行；段异常由宿主 try/catch 隔离 |
| 行色调 | UITK 后端每行一个 `Label`：白=正常、灰=次要、黄=警告、红=错误；性能 FPS<30 黄/<15 红、长帧>0 黄；日志按级别 |
| 折叠 | 段标题 `[-]/[+]` 点击（`PointerDownEvent`，普通 Label 不产生 `ClickEvent`）；折叠态仅本会话 |
| 热键 | `LiteClient/Adapters/Platform.Unity/Dev/DevHotkey.cs`：`ENABLE_INPUT_SYSTEM` 下走 `Keyboard.current[...]`，否则旧 `Input`；放平台适配层是因为 App 层禁 import InputSystem（R12），DevTool 仅引用该程序集 |
| 回退 | IMGUI 路径仍用 `_cache` 纯文本（无色调/折叠）；`renderWithUiToolkit=false` 或字体失败即回退 |

### 验证证据

| 项 | 结果 |
| --- | --- |
| Unity 编译 | 0 错（asmdef 新增 Input System 引用后） |
| 段结构 | Play：`contentChildren=6`（状态行 + 5 段）；无对局时联机段 `display=None`，错误段无数据隐藏 |
| 折叠 | 合成 `PointerDownEvent`（带 pointerId/button/position）：`[-] 性能 | Flex` → `[+] 性能 | None`；再点回 `[-] | Flex` |
| 行色调 | 采样：正常行 `RGBA(0.92,0.92,0.92)`、模块名 `RGBA(0.62,0.65,0.68)`、FSM Warning `RGBA(1,0.82,0.4)` |
| DevHotkey | `KeyboardState(Key.F1)` 注入 + `InputSystem.Update()` → `DevHotkey.Pressed(F1)=True` |
| 截图 | 五段标题、性能/联机/Stats/日志行分色；面板盖在游戏画面上 |

### 边界与未决

- **未做**：F6 JSON 导出、文本/tag 过滤、`DiagTrace`/`LogSink` 段、DPI/拖拽/持久化、折叠态记忆（均 P0-A 剩余）。
- **IMGUI 回退**：纯文本白字，无色调/折叠（回退仅保可用）。
- **折叠事件**：用 `PointerDownEvent` 而非 `ClickEvent`——普通 `Label` 不会自动产生 `ClickEvent`（那是 `Clickable` 的行为）。
- **输入后端**：Input System 路径在 `Keyboard.current == null` 时返回 false（New only 下不能回退旧 Input）；旧 Input 路径仅在 `ENABLE_INPUT_SYSTEM` 关闭时编译。
- **开销**：每行一个 Label（Stats 约 80 行）4Hz 更新，UI Toolkit 布局/文本开销尚未量化，归 P0-B“HUD 自身开销”。
- **体量**：`DevHUD.cs` 约 790 行（第10章后），段件已抽出但宿主仍持生命周期/stats 发现/日志过滤/性能采样/过滤/导出/持久化；P0-B 第一批按“发现 / 采样 / 渲染”再拆，避免继续膨胀。
- 无主题资产：标准控件仍无样式；自定义折叠/滚动已可用。

### 回滚

- 还原 `DevHUD.cs` 到“整段 `BuildText`”版本；删 `Hud/DevHudModel.cs`、`LiteClient/Adapters/Platform.Unity/Dev/DevHotkey.cs`；把 `DevHudUiToolkitHost.Render` 还原为 `SetText`；移除 DevTool 对 `LiteClient.Platform.Unity` 的引用（若仅为热键加）；无资产/数据迁移。

## 第10章 DevHUD P0-A 剩余：过滤/JSON/Diag/Sink/拖动缩放/持久化

<a id="ch10-devhud-p0a-rest"></a>

> 2026-10-21。**状态：已完成（附验证证据）**——Unity 编译 0 错；L1 1819/1819 PASS；Play：七段、`@Fsm` 过滤、F6 JSON、拖动/缩放/重启恢复逐项验证。

范围：P0-A 收口（`DevHUD 演进路线图 §5.5` 的“P0-A 剩余”全部落地）：文本/`@tag` 过滤、F6 JSON、`DiagTrace` 段、日志 sink 状态段、拖动/缩放/持久化、sink 轮转测试。

### 交付面

| 面 | 落点 |
| --- | --- |
| 过滤 | F7 进入过滤模式；`DevHotkey` 文本输入（Input System `onTextInput` / 旧 `inputString`）；`@tag` OR + 文本 AND；Enter 应用、Esc 清除；过滤词持久化 |
| F6 JSON | `ExportJson`：`persistentDataPath/diagnostics/hud-<ts>.json`，schema `devhud/1`，含会话/后端/位置/过滤/七段逐行 `tone/tag/text`；手写转义 |
| DiagTrace 段 | 读 `DiagTrace.Recent` 最近 8 条 + `Total`；`Code` 含 `Fail` 红色；DevTool 新增 `LiteNet` 引用 |
| 日志 sink 段 | `StructuredLogInstaller.Current`（新增只读口）→ `Written/Dropped/LastError/CurrentPath`；掉日志黄、错误红 |
| 拖动/缩放 | 状态行 `PointerDown/Move/Up` 拖动面板（夹屏内）；`hudScale` 只缩放字号；`hudLeft/hudTop` 新字段 |
| 持久化 | `Hud/DevHudPrefs.cs`：宽度/字号/左/顶/缩放/级别/仅本局/过滤词/折叠段；EditorPrefs / PlayerPrefs |
| 测试 | `StructuredLogSinkTests` 新增“单文件上限→轮转且只留 maxFiles 件” |

### 验证证据

| 项 | 结果 |
| --- | --- |
| Unity 编译 | 0 错 |
| L1 | **1819/1819 PASS**（原 1818 + 轮转用例 1） |
| 段结构 | `错误 v=False`、`联机诊断 v=False`（无对局）、`性能 n=2`、`模块 Stats n=97`、`Log Recent n=10`、`DiagTrace n=3`、`日志 sink n=2` |
| 过滤 | 设 `@Fsm` 后 `Log Recent visible=True n=5 firstTag=Fsm`；文本输入路径 `@Fsm` → Enter 退出并写 `DevHUD.filter=@Fsm` |
| F6 JSON | 导出文件可 `json.load`：`schema=devhud/1`、`backend=uitk`、7 段；日志 sink 行 `written=74 dropped=0` |
| 拖动 | 状态行拖动 `8,88 → 48,118`；prefs `left=48 top=118` |
| 缩放 | `hudScale=1.5` → 行字号 18（12×1.5） |
| 重启恢复 | 停/启 Play 后 `left=48 top=118 scale=1.5 filter=@Fsm collapsed=1`；随后复位默认 `8/88/1/空` |
| 轮转 | `Sink_单文件达到上限_轮转并只保留上限文件数` PASS；L1 3 例 sink 全绿 |

### 边界与未决

- **tag 过滤是文本 token**（`@tag`，多个 OR），不是多选控件；需要控件时再评估主题资产。
- **过滤模式不暂停游戏**：输入字符同时进游戏按键（WASD 会动）；这是开发态取舍，暂不抢输入。
- **DPI 只缩放字号**（`hudScale`），不缩放布局/间距。
- **无主题**：标准控件仍无样式；折叠/拖动/滚动/过滤均为自实现。
- **输入后端**：文本输入只验证了 Input System 路径；旧 `inputString` 分支仅编译覆盖。
- **体量**：`DevHUD.cs` 约 790 行（宿主 + 段 + 过滤/导出/持久化），P0-B 第一批先按“发现 / 采样 / 渲染”拆分。

### 回滚

- 过滤/JSON：删 `_filter*` 字段与 `HandleFilterInput/ParseFilter/ApplyFilter/ExportJson/AppendJsonString`，`IHudTextSink` 去掉 `tag`，移除 `HudLine.Tag`；
- Diag/Sink：删 `AppendDiagTrace/AppendLogSink` 与两段注册，移除 `StructuredLogInstaller.Current` 与 DevTool 的 `LiteNet` 引用；
- 拖动/缩放/持久化：删 `Hud/DevHudPrefs.cs`、`hudLeft/hudScale`、宿主拖动回调与 `_collapsed`，还原 `Render` 签名；
- 测试：删轮转用例；无资产/数据迁移。

## 第11章 DevHUD P0-B：性能/联机/Stats 读数

<a id="ch11-devhud-p0b"></a>

> 2026-10-21。**状态：已完成（附验证证据）**——Unity 编译 0 错；L1 1819/1819 PASS；EditMode 4/4；Play：p50/p95/p99 + GC/Draw + HUD 耗时、回滚/输入领先、sparkline 格式化、Stats 排序/阈值逐项验证。

范围：按 ADR P0-B 段能力：帧统计升级、GC/Draw、HUD 自报耗时、联机 sparkline 与回滚/快照年龄、Stats 排序/阈值；同时把性能采样/网络历史/过滤抽成独立件。

### 交付面

| 面 | 落点 |
| --- | --- |
| 帧统计 | `Hud/DevHudPerfSampler.cs`：240 帧环 + 30 帧暖机跳过；p50/p95/p99/最差/长帧；HUD 自报 `HudBuildMs` |
| GC/Draw | `ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame")` / `(Render, "Draw Calls Count")`；invalid → `n/a` |
| 联机扩展 | `BattleDiagnostics.Snapshot` 增 `RollbackCount/DeferredRollbacks/InputLeadFrames/SnapshotAgeFrames`；`AppendExtendedCurrent` 三行（GM 页仍两行） |
| sparkline | `Hud/DevHudNetHistory.cs`：240 采样（60s@4Hz），RTT/丢包两行 + min/avg/max；换局 Reset |
| Stats | 名称排序；`StatsTone`：Error/Failed → 红，Dropped/Warning/Pending/Overflow/Rejected 非零 → 黄 |
| 过滤抽取 | `Hud/DevHudFilter.cs`：Raw/Needle/Tags/Matches/Apply 独立；DevHUD 只留输入态与持久化 |
| 缓存 | UITK 后端 `_cache` 懒构建（F2 才拼整串），IMGUI 回退仍每轮构建 |

### 验证证据

| 项 | 结果 |
| --- | --- |
| Unity 编译 | 0 错 |
| L1 | 1819/1819 PASS |
| EditMode | `BattleDiagnosticsEditModeTests` 4/4（含扩展快照字段、GM 两行口径不变） |
| Play：性能 | `FPS: 95 p50: 10.1 p95: 15.0 p99: 20.1 ms`；`最差: 24 ms 长帧: 0 GC: 16.8KB/帧 Draw: 139 Batches: 129 HUD: 1.18 ms`（不同帧负载下 0.5–1.2ms/轮） |
| Play：联机 | 本地对局：`回滚: 0（延后 0） 输入领先: 3 帧`；无 RTT 样本（本地进程内） |
| sparkline | 合成 30 采样：`RTT 60s +******#... min/avg/max 40/43/47ms`、`丢包 60s ... avg/max 4/25‰` |
| Stats | `_stats` 按名称排序（Audio/Clock.UI/Clock.World/...）；`StatsTone("QueuedDropped","3")=Warn`、`("DispatchErrors","1")=Error`、`("DispatchErrors","0")=Normal` |
| 暖机 | 跳过前 30 帧后不再被启动 9s 长帧支配 p50 |

### 边界与未决

- **对账**：GC/Draw 与 Unity Profiler 来自同一 `ProfilerRecorder` marker；**未做独立 Profiler 窗口抽样比对**，marker 名变化时显示 `n/a`。
- **HUD 开销**：0.5–1.2ms/轮（4Hz ≈0.13–0.30ms/帧；Stats 约 97 行，采样已降到 2Hz、4Hz 只复制缓存行）；高于 ADR 的 0.1ms/帧目标线，当前作为基线，P2 再降。
- **联机样本**：本地进程内对局无 RTT 样本，sparkline 用合成样本验证；真 KCP 对局待联测复核。
- **快照年龄**：本地/无快照时 `-1`，整段省略；在线快照才有值。
- **体量**：`DevHUD.cs` 820 行（第12章已拆为 455 行宿主 + 六个单责件）；拆分债已清。

### 回滚

- 删 `Hud/DevHudPerfSampler.cs`、`Hud/DevHudNetHistory.cs`、`Hud/DevHudFilter.cs`；`BattleDiagnostics` 回退两字段/两行；还原 `AppendPerf/AppendNet/AppendStats` 与 `_perf/_net` 字段；移除 `ProfilerRecorder` 依赖；无资产/数据迁移。

## 第12章 清 P0-B 遗留债：DevHUD 拆分

<a id="ch12-devhud-split"></a>

> 2026-10-21。**状态：已完成（附验证证据）**——Unity 编译 0 错；L1 1819/1819 PASS；EditMode 4/4；Play：七段读数、过滤、F6 JSON 在拆分后仍等价。

范围：清审计登记“`DevHUD.cs` 约 820 行、宿主多职责”，按“发现 / 采样 / 渲染 / 导出”拆件，宿主只留生命周期、输入、持久化与编排。

### 交付面

| 件 | 行数量级 | 职责 |
| --- | --- | --- |
| `DevHUD.cs` | 455 | 生命周期、热键/过滤输入、Prefs、Toast、IMGUI 回退、编排 |
| `Hud/DevHudSectionSet.cs` | ~400 | 七段构建（Errors/Net/Perf/Stats/Log/Diag/LogSink）+ Stats 2Hz 缓存 |
| `Hud/DevHudStatsDiscovery.cs` | ~60 | 容器 + 场景组件 Stats 发现、去重、剔除、排序 |
| `Hud/DevHudFilter.cs` | ~90 | 过滤词解析/匹配/原地裁剪 |
| `Hud/DevHudPerfSampler.cs` | ~120 | 帧环/分位/ProfilerRecorder/HUD 耗时 |
| `Hud/DevHudNetHistory.cs` | ~90 | 60s RTT/丢包环 + sparkline |
| `Hud/DevHudJsonExporter.cs` | ~130 | F6 JSON（schema devhud/1、转义、落盘） |
| `Hud/DevHudUiToolkitHost.cs` | 392 | UI Toolkit 宿主（未改） |

### 验证证据

| 项 | 结果 |
| --- | --- |
| Unity 编译 | 0 错 |
| L1 | 1819/1819 PASS |
| EditMode | `BattleDiagnosticsEditModeTests` 4/4（重启编辑器后复跑） |
| Play（拆分后） | `data=7`；性能段 2 行（p50/p95/p99、GC/Draw/Batches、HUD）；`@Fsm` → Log 6 行 tag=Fsm；F6 JSON 7 段、filter 空、left/top=8/88 |
| 重启恢复 | 拆分不影响 Prefs/折叠/过滤/位置恢复 |

### 边界与未决

- `DevHudSectionSet.cs` ~400 行仍是“段构建”单责；若段继续增加（世界/回放），再按 Stats/Net/Perf 拆子构建器。
- `DevHUD.cs` 455 行 >400，但已只持宿主职责；不再按“多职责”登记。
- 拆分后 HUD 自身耗时仍在 0.5–1.2ms/轮（Stats 97 行、4Hz）；P2 再优化。
- 无资产/场景改动；仅新增 `Hud/*.cs`（.meta 由 Unity 生成）。

### 回滚

- 还原 `DevHUD.cs` 到拆分前版本（含段构建/导出），删 `Hud/DevHudSectionSet.cs`、`DevHudStatsDiscovery.cs`、`DevHudFilter.cs`、`DevHudJsonExporter.cs`；保留 perf/net/host 件；无资产/数据迁移。

---

<a id="ch13-debug-component-split"></a>

## 第13章 调试组件职责拆分（`DebugTuner` → `DevStartupConfig`）

> 2026-10-22。**状态：已完成（附验证证据）**——Unity 编译 0 错；L1 1819/1819 PASS；EditMode 2/2（工作台资产守卫）+ 13/13（激光回归）。

范围：清审计登记的“`DebugTuner` 单件混合启动配置 / 运行态时钟 / 开发页与工作台生命周期 / GM 命令注册”。拆分后：

| 件 | 职责 |
|---|---|
| `App/Debug/DevStartupConfig.cs` | 启动落点开关（`UseLocalServer` / `DirectToTraining` / `WorkbenchMode` / `DevMenu`）+ 测试模式启动初值 + `ApplySnapshotOrDefaults` + 静态镜像；无服务引用 |
| `App/Debug/DebugRuntimeTuner.cs` | 世界时钟 / UI 时钟 / 事件 StrictMode 连续旋钮 + `Inject` + GM「重置时钟」命令注册 |
| `App/Debug/DevMenuLauncher.cs` | 开发页 `DevMenuUI` 生成、工作台场景 `UiWorkbench` 加性加载/卸载、工作台请求桥与路径常量 |

- **改名保 GUID**：`Assets/LiteGame/App/Debug/DebugTuner.cs` 经 unity-pipeline `rename_asset` → `DevStartupConfig.cs`（GUID `bdd7002c…` 不变），Boot 场景原组件引用保持；同 GameObject 新增 `DebugRuntimeTuner` / `DevMenuLauncher` 两件（pipeline `attach_script`），场景已保存。
- **代码消费者同步**：`GameEntry` 改注入 `DebugRuntimeTuner`；`ProcedureMain` / `ProcedureMatch` / `DevMenuPanel` / `UIWorkbenchDriver` / `GmTestModePage` / `GmClockCommands` / 测试守卫全部改新类型。
- **行为保持**：Boot 序列化值保持 `UseLocalServer=1`、`DirectToTraining=1`、`DevMenu=1`、`WorkbenchMode=0`；时钟默认 `Scale=1`、不暂停、`StrictMode=1`。
- **门禁边界**：本次仅 `Assets/LiteGame` 客户端侧，不触共享源集/BuildHash。Play 冒烟：`DevMenuLauncher` 已开出开发页；首轮启动被工作区既有的 `Assets/ShareResource/SampleContent` 缺失拦住，随后按用户裁决退役样例①（见[资源与工具链线 ch33](资源与工具链线.md#ch33-sample-content-retire)），复跑 Boot Play 启动通过（`entry-assets ok checked=1`、配置 12 表装载、UI 大厅页打开）。

## 第14章 统一调试内核与运行期控制台（F12）

<a id="ch14-debug-kernel-console"></a>

> 2026-10-22。**状态：已完成（附验证证据）**——Unity 编译 0 错；EditMode 16/16；L1 1819/1819；Doc 0 violation；Play 已走真实 Input System 路径验证 F12 控制台、输入拦截、HUD 六段与编辑器命令页。

范围：执行[《GM 与调试工具分层专项设计》§7](../设计文档/架构/GM与调试工具分层专项设计.md#7-统一内核与三投影)的“统一内核 + 三投影”最小闭环——内核（读模型/动作/发现）、编辑器投影收口、DevHUD 收口、运行期文本控制台。**不新增 UI 调试面板**；F8 仍归[《UI 运行时调试工具链专项设计》](../设计文档/客户端/UI系统/UI运行时调试工具链专项设计.md)的目标入口。

### 交付面

| 件 | 职责 |
|---|---|
| `Assets/LiteGame/App/Debug/DebugKernel.cs` | 读模型（Stats/Log/Diag/Battle/LogSink）、动作（命令执行/审计/撤销/选项/请求桥/输入拦截租约）、发现（命令/读段）；实例装配注入，dev 三宏 |
| `Assets/LiteGame/App/Debug/DebugStatsDiscovery.cs` | 从 DevTool 迁入内核：容器 + 场景组件 Stats 合并/排序/剔除 |
| `Assets/LiteGame/App/Debug/DebugConsoleCommand.cs` | 控制台命令行纯词法解析（EditMode 可直测） |
| `Assets/LiteGame/App/Bootstrap/ContainerModule.cs`、`App/GameEntry.cs` | 装配期注册内核；`GameEntry.DebugKernel` 只读访问器（与 `Stats` 同口径） |
| `Assets/LiteClient/Adapters/Platform.Unity/Dev/DevHotkey.cs` | 新增 F12；文本输入改为 owner 所有权（HUD 过滤 / 控制台互斥） |
| `Assets/LiteGame/DevTool/DevHUD.cs`、`Hud/DevHudSectionSet.cs` | 延迟绑定内核；六段读数改走内核；场景加载重建/每轮剔除由内核承担；HUD 仍只读 |
| `Assets/LiteGame/DevTool/DebugConsole.cs` | F12 运行期控制台：IMGUI、200 行输出、输入拦截、命令派发、异常隔离 |
| `Assets/LiteGame/Editor/Gm/GmCommandsPage.cs`、`GmNetDiagPage.cs` | 编辑器投影改消费内核；页内删除 `CommandCenter`/反射执行 |
| `Assets/Tests/EditMode/DebugKernelEditModeTests.cs` | 内核/解析器 16 例（发现、执行、审计、选项、请求桥、无对局、读段、解析） |
| `scripts/gate/unity-tests.py` | Unity 测试门禁包装：`run_tests` 失败时 CLI 仍 exit 0，必须解析 `Summary.Failed` |
| `Docs/项目说明/13-调试手册.md`、`14-客户端开发能力地图.md` | 增补 F12 控制台、内核读段、F8 目标入口边界与已知不足 |

### 验证证据

| 项 | 结果 |
|---|---|
| Unity 编译 | `unity command recompile` → 0 错 |
| EditMode | `DebugKernelEditModeTests` 16/16 PASS（含带参命令拒绝、未知命令、选项/请求桥、解析器） |
| L1 | `test.py --suite L1 --profile PullRequest` → **1819/1819 PASS** |
| Doc | `test.py --suite Doc` → 0 violation / 0 warning |
| Play：控制台开合 | 真实 Input System 事件：F12 开/关；释放按键后 F8 不再打开控制台（F8 保留给 UI 运行时调试目标） |
| Play：命令 | 键入 `help`（16 行）、`commands`（5 条 GM 命令）、`options`（9 项）、`option InfiniteAmmo on`（回读 True，随后 off）、`run GmResetClockCommand`（OK：时钟已重置）、`audit 4`（#1 OK）、`stats`/`log 3`/`battle` 均有输出 |
| Play：输入拦截 | 控制台打开时 `BlockerCount 1→2`、租约非空；关闭后回 1、`_textOwned=false` |
| Play：HUD | HUD 内核非空；段读数 `Stats=97 行`、`Log Recent=10`、`Perf=2`、`日志 sink=2`；无对局时联机/DiagTrace 段隐藏 |
| Play：编辑器页 | 反射调用 `GmCommandsPage.RefreshCommands` → `cmds=5 audit=1`；`GmNetDiagPage` 改读 `kernel.DescribeBattle()` |
| 红测 | 先落签名骨架 + 16 用例，`evidence/red.json` 记录 16/16 `NotImplementedException`；实现后同用例转绿 |

### 边界与未决

- **F8/F12 分工**：F8 是 UI 运行时调试面板的目标入口（未落地），本批控制台改用 **F12**，不抢占；后续 UI 面板/通用控制台外壳归 T1 决策点。
- **IL2CPP 反射**：控制台对发现命令的 `Activator` + `MakeGenericMethod` 在编辑器 Play 已过；开发包 IL2CPP 出包未实测（本轮不出包），AOT 下可能不可用。内置命令/读数不依赖反射；若真机不可用，控制台应明确提示“需编辑器执行”，内核接口不变。
- **编辑器测试模式页**仍直接调 `TestModeOptions`/`TestModeRuntime`：它们是内核动作模型的静态写点，且页面需在未进 Play 时显示启动项；不视为第二套实现。
- **工作流配置**：为让 Unity 测试失败可见，`.workflow/config.json` 追加了 `debug-kernel-editmode`（`scripts/gate/unity-tests.py`）；同仓另一个进行中的运行后续 verify 会多跑这一项。
- **UI 调试面文档**：`Docs/设计文档/测试与性能/开发包UI调试面专项设计.md` 改为归档重定向指针，现行权威是 `UI运行时调试工具链专项设计`；未复制第二套设计。

### 回滚

- 删除 `DebugKernel.cs`/`DebugStatsDiscovery.cs`/`DebugConsoleCommand.cs`/`DebugConsole.cs`，还原 `ContainerModule`/`GameEntry`/`DevHotkey`/`DevHUD`/`DevHudSectionSet`/两个 GM 页；旧 `DevHudStatsDiscovery.cs` 由 git 恢复。
- 无资产/数据迁移；内核与控制台可分别撤回（先撤控制台不影响编辑器/HUD 收口）。

## 第15章 开发页退役与开发控制台 UI Toolkit 化

<a id="ch15-dev-console-uitoolkit"></a>

> 2026-10-23。**状态：已完成（附验证证据）**——代码/退役/离线门禁/最终 Play 实测均完成；Unity 编辑器期间曾被并行会话的 PlayMode/TestRunner 状态短暂卡住，恢复后补跑了最终复编译与全宽截图。

范围：用户裁决“开发页直接退役，相关功能接到开发控制台；纯命令；开发控制台改走 UI Toolkit”。

### 交付面

| 件 | 变更 |
|---|---|
| `Assets/LiteGame/DevTool/DevMenuPanel.cs`、`DevMenuUI.prefab`、`Assets/LiteGame/App/Debug/DevMenuLauncher.cs` | **删除**（含 `.meta`，经 unity-pipeline `delete_asset`） |
| `Assets/LiteGame/App/Debug/DevStartupConfig.cs` | 删除 `DevMenu` 字段、`DevMenuEnabled` 静态镜像与发布行；保留启动落点/测试模式初值 |
| `Assets/Scenes/Boot.unity` | `DevMenuLauncher` 组件经 pipeline 移除；`DevMenu` 序列化字段清除（重存后 14 行删除） |
| `Assets/LiteGame/App/Procedure/ProcedureMain.cs` | `TakeEnterRequest` 消费点单点调用 `ApplySnapshotOrDefaults`；直入块不再预应用 |
| `Assets/LiteGame/Editor/Gm/GmTestModePage.cs` | 「进入测试模式」只发请求，快照统一由消费点落值 |
| `Assets/LiteGame/DevTool/DebugConsole.cs` | 删 `OnGUI`/`GUIStyle`；UI Toolkit 宿主渲染；保留 `DevHotkey` 输入与 `DebugKernel` 命令面 |
| `Assets/LiteGame/DevTool/DebugConsoleUiToolkitHost.cs` | 新增：运行时 `PanelSettings`（`sortingOrder=10100`）+ 内联样式 + 系统 SDF 字体；输出行/输入行/滚轮滚动 |
| `Assets/LiteGame/DevTool/DevUiToolkitFont.cs` | 新增：DevHUD 与控制台共用的系统 SDF 字体候选链 |
| `Assets/LiteGame/DevTool/Hud/DevHudUiToolkitHost.cs` | 字体创建改走共享单源，删除重复候选链 |
| `Assets/Tests/EditMode/UiPreviewEditModeTests.cs` | 新增 `开发页_已退役`（字段/类型/预制体三面断言）；工作台守卫去掉 `DevMenuLauncher` 依赖 |

### 能力映射（纯命令）

| 原开发页 | 控制台 |
|---|---|
| 测试模式（训练场） | `request enter`（`ProcedureMain` 消费时套 `DevStartupConfig` 快照） |
| 直连对局（真 KCP/本机服） | `request direct` |
| 关闭 | `F12` / `Esc` |
| 形态/状态读数 | `options` / `battle` / `stats` |

### 验证证据

| 项 | 结果 |
|---|---|
| Unity 编译 | 首次改造后 `unity command recompile` → 0 错；最终 Play 实际运行宽度修复后的代码，`recompile_status` = `up_to_date`、`compilationFailed=false` |
| 离线编译校验 | 三个控制台文件（`DebugConsole` / `DebugConsoleUiToolkitHost` / `DevUiToolkitFont`）用 Unity 2022.3 Roslyn 引用集单独编译 → 0 错（含宽度修复与调试名） |
| EditMode | `UiPreviewEditModeTests` 6/6 PASS（含 `开发页_已退役`）；`DebugKernelEditModeTests` 16/16 PASS |
| L1 | `python scripts/gate/test.py --suite L1 --profile PullRequest` → **1819/1819 PASS** |
| Play：UI Toolkit | 真实 Input System：F12 开面板；`[DebugConsole-UI]` 在场，`panelSettings.sortingOrder=10100`；`help` 输出正常 |
| Play：全宽布局 | 运行时探针：`Screen=1920x1080`，panel `layout/world = 1920x486`、`style.width=1920px`、`bg=RGBA(0,0,0,0.82)`；截图 `AgentScripts/Artifacts/dev-console-uitoolkit-final.png`（1280×720）与 `dev-console-uitoolkit-native.png`（1920×1080） |
| Play：功能链 | 键入 `request enter` → 输出“已请求进入测试模式”，`TestModeRuntime.Active=True`、`Hud_Battle` 在场、场景 `Boot,BattleRig,TrainingGround` |
| Play：启动快照 | `request enter` 后在控制台执行 `options`：`NoDeath = 关`（Boot 序列化 0）、`LaserSight = 开`（Boot 序列化 1）——证明快照由 `ProcedureMain` 消费点落值，而非沿用旧运行时值 |

### 边界

- 控制台 UI Toolkit 面板位于产品 uGUI Overlay 之下：全屏产品页（设置等）打开时会盖住底部面板，命令仍可执行；已在设计文档与调试手册登记。
- 无资产/数据迁移；退役面可从 git 还原三个文件与 Boot 场景两处序列化内容。

## 第16章 测试房语义统一与普通房结算

<a id="ch16-test-room-semantics"></a>

> 2026-10-23。**状态：进行中**——代码/客户端门禁完成；云侧配置尚未上传重启，GM 页实际建房需在云环境手测（本机无 Meta 登录/口令，不伪造结论）。

范围：用户裁决“普通房验结算、`sandbox` 显式建测试房；测试房由开发者选图；统一测试模式/测试房相关语义”。

### 交付面

| 件 | 变更 |
|---|---|
| `deploy/roomserver.windows.json` | `default_template` 由 `sandbox` 改为 `standard`（普通房恢复结算）；`rooms.sandbox` 保留为云端测试房模板 |
| `Assets/LiteGame/App/Meta/MetaEntryService.cs` | 新增 `TestRoomTemplate = "sandbox"` 与显式 `CreateRoomAsync(mapId, expectedPlayers, template, gmKey)` 重载；普通建房空模板/口令归一为 null（请求体与旧行为一致） |
| `Assets/LiteGame/App/Bootstrap/ContainerModule.cs` | 注册 `MetaEntryService` 供编辑器 GM 页取件（主消费者仍是 `MetaBridge`） |
| `Assets/LiteGame/Editor/Gm/GmTestModePage.cs` | 页标题改“本地测试模式”；新增「云端测试房（sandbox）」块：地图下拉（`tbmap` 全行）+「创建并进入云端测试房」；成功后房号回填 GM 规则区；GM 规则块仅对云端测试房 |
| `Assets/LiteGame/DevTool/DebugConsole.cs`、`TestModeRuntime.cs`、`DevStartupConfig.cs` 等 | 术语统一：`request enter`/GM 按钮 = 本地测试模式（Room-Test）；云端测试房 = sandbox 隐藏房；直连对局 = 本地 Room-A |
| `scripts/gate/toolchain.json` | GM 页标题改「本地测试模式」、摘要补云端测试房创建/选图 |

### 语义口径（现行）

| 名称 | 形态 | 结算 | 入口 |
|---|---|---|---|
| **本地测试模式** | 本进程内本地服 `Room-Test` | 不产结算 | F12 `request enter` / GM 页按钮 / `DirectToTraining` |
| **云端测试房** | 云端隐藏房 `template=sandbox`，真 KCP | 不产结算 | GM 页选图 → 创建并进入（开发者专属） |
| **普通房** | 云端可见房 `default_template=standard` | **会结算** | 大厅选图/人数 → 创建房间 |
| **直连对局** | 本地 `Room-A`，真 KCP（无 Meta） | 视本地服配置 | F12 `request direct` |

### 验证证据

| 项 | 结果 |
|---|---|
| Unity 编译 | `recompile_status` → completed / 0 错 |
| EditMode | `MetaEntryEditModeTests` **17/17 PASS**（新增「显式模板建房_不依赖部署注入_携带地图与口令」；原“未配置请求体不变”回归通过） |
| L1 | `python scripts/gate/test.py --suite L1 --profile PullRequest` → **1819/1819 PASS**（并行 UI 会话夹具清理后复跑通过） |
| Play：数据面 | 真实 Play 探针：`tbmap rows=2`（地图下拉可用）、`MetaEntryService=in-container`、`IsConfigured=True`、未登录时创建按钮按空态禁用 |
| Play：建房 | 待云环境手测（需 Meta 登录 + `room.gmkey`；本机未登录，不伪造结论） |
| Doc | `doc-lint.py` → 0 violation / 0 warning |

### 边界与待办

- **云上配置未发布**：仓库已改 `default=standard`，但云上实例仍按旧 `default=sandbox` 运行；需在用户明确授权后上传 `deploy/roomserver.windows.json` 并重启 `LiteGame-Room`，横幅变为 `default=standard` 才生效。
- 云端测试房每次加入都要求 `template=sandbox` + `room.gmkey`；测试者包不带口令 ⇒ 测试者不能创建/加入云端测试房，日常联机走普通房。
- `sandbox` 永不自然结束且不产结算；测试房用完需 GM abort / 离线清理，`max_rooms=8`。

## 第17章 测试路径收口：退役直连对局（Room-A / TestToken）

<a id="ch17-retire-direct-match"></a>

> 2026-10-23。**状态：已完成（附验证证据）**——直连入口/TestToken/UseLocalServer 已删除；本地测试模式 Play 已复验。本地全链需起 Meta/Mongo，本批未执行。

范围：用户裁决“退役 `request direct` 和 `TestToken` 直连链，只留本地测试模式（进程内）、本地全链（Meta+Room）、云端普通房/测试房”。

### 交付面

| 件 | 变更 |
|---|---|
| `Assets/LiteGame/DevTool/DebugConsole.cs` | 删除 `request direct` 帮助/派发/usage；`request` 只接受 `enter/exit/teleport` |
| `Assets/LiteGame/App/Debug/DebugKernel.cs` | 删除 `RequestDirect()` |
| `Assets/LiteGame/App/Debug/TestModeRuntime.cs` | 删除 `_directRequested` / `RequestDirect` / `TakeDirectRequest` |
| `Assets/LiteGame/App/Procedure/ProcedureMain.cs` | 删除 `TakeDirectRequest` 消费块；启动落点不再有“本地服务器→大厅”分支 |
| `Assets/LiteGame/App/Procedure/ProcedureMatch.cs` | 删除 `Room-A`/`TestToken`；免票据入口只允许 `testRoom:true`（进程内 Room-Test，空 token）；非 Meta 的非测试进房显式失败；`CreateTransportForFactory` 只把 `testRoom` 映射到 `LocalServerTransport`，其余一律真 KCP |
| `Assets/LiteGame/App/Debug/DevStartupConfig.cs` + `Boot.unity` | 删除 `UseLocalServer` 字段/静态镜像/发布行与场景序列化值（直连链的进程内回退随之退役） |
| `Assets/Tests/EditMode/DebugKernelEditModeTests.cs` | 删除 `TakeDirectRequest` 夹具清理 |
| 文档 | 07 把直连路径 B 标记退役、本地全链改为 B；09/12/13/01/README/代码地图/修改指南同步；审计登记追加退役条目 |

### 验证证据

| 项 | 结果 |
|---|---|
| Unity 编译 | `recompile_status` → completed / 0 错 |
| L1 | `python scripts/gate/test.py --suite L1 --profile PullRequest` → **1819/1819 PASS** |
| DebugKernel EditMode | **17/17 PASS**（含新增 `直连测试身份_已退役` 守卫） |
| Play：本地测试模式 | F12 → `request enter` → `TestModeRuntime.Active=True`、`Hud_Battle` 在场、场景 `Boot,BattleRig,TrainingGround`（空 token 的进程内 Room-Test 进房正常） |
| Play：本地全链 | 未执行（需 `local-chain.py` 起 Meta/Mongo/Room 并登录） |
| Doc | `doc-lint.py` → 0 violation / 0 warning |

### 边界

- 本地测试模式仍使用 `TestHost/TestPort` 作为**占位端点**（`LocalServerTransport` 不建 Socket），token 传空字符串；服务端 token 非空校验不适用于进程内形态。
- 本地真 KCP 只走 Meta 票据（本地全链/云端）；服务端带票据门禁后不会再接受测试身份。
- `Boot.unity` 的 `DirectToTraining` 保持不变；`UseLocalServer` 序列化值已清除。

## 第18章 常用调试面板（F11，UI Toolkit）

<a id="ch18-debug-panel"></a>

> 2026-10-23。**状态：已完成（附验证证据）**——用户裁决“控制台交互麻烦，只把常用调试项放到面板”；本批不建通用调参 schema。

范围：新增 F11 常用调试面板，动作与即时开关全部经 `DebugKernel`；任意命令仍走 F12 控制台。

### 交付面

| 件 | 变更 |
|---|---|
| `Assets/LiteGame/DevTool/DebugPanel.cs` | 新增：F11 自创建覆盖件；本地测试模式进入/退出/传送 + `TestModeOptions` Live 开关；输入拦截 `debug.panel`；内核延迟绑定；release 三宏剥离 |
| `Assets/LiteGame/DevTool/DebugPanelUiToolkitHost.cs` | 新增：UI Toolkit 顶部右侧面板；运行时 `PanelSettings`（`sortingOrder=10200`）+ 内联样式；选项行短名 + 完整说明 tooltip；无 ThemeStyleSheet 依赖 |
| `Assets/LiteClient/Adapters/Platform.Unity/Dev/DevHotkey.cs` | 新增 F11（Input System 与旧 Input 两路映射） |
| 文档 | 13 手册 §1.1/§2.8、14 能力地图、GM 分层设计 §5/§7、01/README、代码地图同步 |

### 设计口径

- **只放常用项**：面板动作=本地测试模式进入/退出/传送到准心；开关=`TestModeOptions` 的 `Live` 组（不展示 Startup 项，进房初值仍在 `DevStartupConfig`）。
- **单一内核**：按钮点击 → `DebugKernel.Request*` / `TrySetOption`；不直调业务服务、不另拼读数。
- **无通用 schema**：选项列表来自 `TestModeOptions.All`；无需新增调参注册表；F12 仍负责任意命令/带参命令的发现与专面板提示。

### 验证证据

| 项 | 结果 |
|---|---|
| Unity 编译 | `recompile_status` → completed / 0 错 |
| L1 | `python scripts/gate/test.py --suite L1 --profile PullRequest` → **1819/1819 PASS** |
| DebugKernel EditMode | **17/17 PASS**（含直连退役守卫） |
| Play：F11 | 真实 Input System F11 打开；`[DebugPanel-UI]` 在场；按钮数 9（3 动作 + 6 个 Live 开关） |
| Play：开关联动 | UI Toolkit `Clickable.Invoke` 点击「无限子弹」：`InfiniteAmmo 0 → 1`，再次点击恢复 0；状态行显示 `本地测试模式：关 · 即时开关走本进程` |
| 截图 | `AgentScripts/Artifacts/debug-panel.png`（顶部右侧面板；选项短名不重叠） |
| 未做 | OS 级鼠标坐标注入在 Editor 未命中（InputSystem→UI Toolkit 坐标偏移）；真机/Player 的可点性归运行期验证；本地测试模式 Play 另见第17章 |

### 边界

- 面板只做常用动作/即时开关；不带参命令、云侧测试房创建仍在 GM 页/F12；F8 UI 运行时调试面仍是独立目标。
- 面板与 DevHUD/控制台共用 UI Toolkit 运行时面板与字体单源；未引入 dev-only ThemeStyleSheet，因此不用标准 Toggle/ScrollView 控件。

## 第19章 通用调参注册表与编辑器「调试参数」页

<a id="ch19-debug-tunables"></a>

> 2026-10-23。**状态：已完成（附验证证据）**——用户裁决“抽通用注册表；编辑器面板”；本批把现有手写调参块迁入描述驱动页面。

范围：`DebugTunable` 描述 + `IDebugTunableProvider` 发现（容器 + 场景）+ `DebugKernel.Tunables/TrySetTunable/ResetTunable` + Hub 第七页「调试参数」；迁移爆头区/持枪幅度/准心/视角灵敏度/时间事件。

### 交付面

| 件 | 变更 |
|---|---|
| `Assets/LiteGame/App/Debug/DebugTunable.cs` | 描述符（Id/Group/Label/Kind/Scope/Default/Min/Max/Get/Set/Reset）+ provider 接口 |
| `Assets/LiteGame/App/Debug/DebugTunableDiscovery.cs` | 容器 + 场景 provider 合并、Id 去重、Group/Label 排序、销毁件剪枝 |
| `Assets/LiteGame/App/Debug/FeatureDebugTunables.cs` | 静态功能 provider：爆头/持枪/准心/视角；默认值来自烘焙/表/机制常量 |
| `Assets/LiteGame/App/Debug/DebugRuntimeTuner.cs` | 实现 provider：时间同步/世界倍率/暂停/UI 暂停/事件严格模式 |
| `Assets/LiteGame/App/Debug/DebugKernel.cs` | 新增 `Tunables` / `RebuildTunables` / `PruneTunables` / `TrySetTunable` / `ResetTunable`；读段 `tunables` |
| `Assets/LiteGame/App/Bootstrap/ContainerModule.cs` | 注入 `FeatureDebugTunables` 到内核 |
| `Assets/LiteGame/Editor/Gm/GmDebugTunablesPage.cs` | 新 GM 页：分组/过滤/开关/滑杆/复位/scope 标记；默认值来自描述 |
| `Assets/LiteGame/Editor/Gm/GmTestModePage.cs` | 移除已迁移的爆头/持枪/准心/视角滑杆；保留爆头导出与 FOV 编辑器独占块 |
| `Assets/LiteGame/Editor/Toolchain/EditorToolHandlers.cs`、`scripts/gate/toolchain.json` | 注册 `debug.tunables` 页（第七页） |
| 文档 | 13 手册 §3.4、GM 分层设计 §4/§7、14 能力地图、工具索引同步 |

### 迁移口径

- **爆头区/持枪幅度/准心/视角灵敏度与 ADS 收敛/时间事件** → 「调试参数」页；`Reset` 清除覆写回源值。
- **保留本地测试模式页**：爆头区导出 `HeadBake.g.cs`（编辑器独占动作）、FOV 试调（场景 rig 值）。
- 连续/开关参数直调 provider setter；不强制命令化（不刷审计）。`Product` scope 目前无条目，供未来转正参数使用。

### 验证证据

| 项 | 结果 |
|---|---|
| Unity 编译 | `recompile_status` → completed / 0 错 |
| DebugKernel EditMode | **18/18 PASS**（新增 `调参发现_按描述读写与复位`） |
| L1 | `python scripts/gate/test.py --suite L1 --profile PullRequest` → **1819/1819 PASS** |
| Play：注册表 | 真实 Play 探针：**16 项**（准心 6/时间事件 5/爆头 2/视角 2/持枪 1）；`time.world-scale` set 0.5 → reset 成功 |
| Play：编辑器页 | Hub 选中「调试参数」页，显示 provider 自动发现说明、过滤、准心参数分组与滑杆行；截图 `AgentScripts/Artifacts/tunables-hub.png` |
| Doc | `doc-lint.py` → 0 violation / 0 warning |

### 边界

- 默认值单源在功能 provider（烘焙/表/机制常量）；页面不写字面量默认值。
- 参数只在本机运行时覆写，不落盘、不进摘要；release 三宏剥离。
- FOV/爆头导出仍留本地测试模式页（编辑器独占/场景值），不强行塞进通用调参模型。

## 第20章 运行期调参面（F12 tune + F11 白名单）与 scope 策略

<a id="ch20-runtime-tunables"></a>

> 2026-10-23。**状态：已完成（附验证证据）**——F12 `tune` 全量可调；F11 只放 `RuntimeVisible` 白名单；Product 默认不进运行期。

范围：把第19章的调参注册表接到运行期；不新增注册表/不复制默认值。

### 交付面

| 件 | 变更 |
|---|---|
| `Assets/LiteGame/App/Debug/DebugTunable.cs` | 增 `Tags`、`Decimals`、`RuntimeVisible`；`Scope` 作为可见性/写入策略 |
| `Assets/LiteGame/App/Debug/DebugTunableText.cs` | Bool/Float 文本格式与解析单源（InvariantCulture） |
| `Assets/LiteGame/App/Debug/DebugTunableDiscovery.cs` | 范围/默认值/Bool 0..1/重复 Id 校验；`Decimals` 夹取 |
| `Assets/LiteGame/App/Debug/DebugKernel.cs` | `RuntimeTunables`（白名单 ∩ 非 Product）；`PruneStats` 连带剪枝 |
| `Assets/LiteGame/DevTool/DebugConsole.cs` | `tune` / `tune list [scope]` / `tune <id> [value|reset]` / `tune resetall` |
| `Assets/LiteGame/DevTool/DebugPanel.cs`、`DebugPanelUiToolkitHost.cs` | F11「快捷调参」区：自定义滑杆/开关/复位；只读 `RuntimeTunables` |
| 参数标记 | 6 项运行期白名单：世界时间倍率/世界暂停/爆头线下沿/持枪跟随强度/准心静态满张/视角灵敏度 |
| 文档 | 13 手册 §2.7/§2.8/§3.4、14 能力地图、GM 分层设计 §5/§7 |

### scope 策略（现行）

| scope | 编辑器页 | F11 | F12 tune | 持久化/摘要 |
|---|---|---|---|---|
| Sandbox | ✅ | 需 `RuntimeVisible` | ✅ | 否/否 |
| Presentation | ✅ | 需 `RuntimeVisible` | ✅ | 否/否 |
| Product | ✅（警告） | ❌ | 显式 `tune list product` 可看 | 否（定档后迁表/配置） |

### 参数规模管理（约定）

- Id：`group.item`（如 `head.line`、`crosshair.static`）；Group 分组、Tags 检索。
- 功能各自 `IDebugTunableProvider`，不维护中央手写清单；重复 Id 由 discovery 告警并忽略后者。
- F11 只放现场能解释的少量参数（当前 6 项）；长尾/未定型参数只进编辑器页。
- `Product` 默认不进运行期，避免临时值变成事实默认值；定档后把默认值迁到表/配置。

### 验证证据

| 项 | 结果 |
|---|---|
| Unity 编译 | `recompile_status` → completed / 0 错 |
| DebugKernel EditMode | **20/20 PASS**（新增运行期白名单/Product 排除、Bool/Float 文本解析） |
| Play：注册表 | `16` 项总表，`RuntimeTunables=6` |
| Play：F12 | 真实键盘：`tune list` 含 `time.world-scale`/scope；`tune time.world-scale 0.5` → 0.5；`tune time.world-scale reset` → 1 |
| Play：F11 | 面板 17 个按钮（3 动作 + 快捷调参 header/6 行 + 6 开关）；Bool「世界暂停」点击翻转并恢复；截图 `AgentScripts/Artifacts/debug-panel-tunables.png` |
| L1 | `python scripts/gate/test.py --suite L1 --profile PullRequest` → **1819/1819 PASS** |
| Doc | `doc-lint.py` → 0 violation / 0 warning |

### 边界

- F11 滑杆为自绘控件（无 ThemeStyleSheet）；OS 级指针坐标注入在 Editor 未命中，真机/Player 可点性待验；Bool 按钮已用 `Clickable` 验证。
- `tune` 不落盘、不进摘要；`Product` 参数显式可见但仍无持久化/命令审计。
- 参数很多时靠 Id/Group/Tags + 运行期白名单管理，不做“全部参数都上 F11”。

## 第21章 产品参数定档（准心与视角 8 项）

<a id="ch21-product-tunables"></a>

> 2026-10-23。**状态：已完成（附验证证据）**——用户追问“目前有产品参数吗”后裁决“现在定一些”：把默认值仍写在代码常量里的准心/视角参数标为 `Product`，作为待迁表/配置的定档项。修正第 19 章“Product scope 目前无条目”与第 20 章“Product 0 项”的结论。

范围：只改 provider 的 scope 标记与 `RuntimeVisible`，不新增注册表、不改读写链路。

### 定档口径

判据（回写设计 §7）：**默认值仍写在代码常量、定档后应迁表/配置** 的参数标 `Product`；默认值已由烘焙产物提供的参数不标——其 tunable 只是运行态试验覆写。

| scope | 项数 | 参数 | 判据 |
|---|---:|---|---|
| Sandbox | 7 | 爆头区 2（`head.line`/`head.radius`）、时间/事件 5 | 调试期试验；爆头默认已来自 `HeadBake` 烘焙 |
| Presentation | 1 | 持枪幅度（`grip.torso`） | 纯表现；默认已来自 `GripBake` 烘焙 |
| Product | 8 | 准心 6（`crosshair.*`）、视角 2（`look.sensitivity`/`look.ads-scale`） | 默认值仍为代码常量（`CrosshairFeel.Default`、`BattleViewRig` 常量），定档后迁表/配置 |

- `Product` 项不再声明 `RuntimeVisible`：F11 快捷调参由 6 项收为 4 项（换弹跟随强度/世界时间倍率/世界暂停/爆头线下沿）。
- F12 `tune` 仍可按 Id 读写全部 16 项；`tune list product` 显式列出 8 项，写入仍受范围校验。
- 编辑器「调试参数」页照常全量渲染，Product 项带 `[产品]` 前缀且页尾给迁回表/配置警告。

### 交付面

| 件 | 变更 |
|---|---|
| `Assets/LiteGame/App/Debug/FeatureDebugTunables.cs` | 准心 6 项 + 视角 2 项 `Scope=Product`；撤销 `crosshair.static`/`look.sensitivity` 的 `RuntimeVisible` |
| `Assets/Tests/EditMode/DebugKernelEditModeTests.cs` | 新增 `产品参数_准心与视角已定档且不进运行期白名单`（Product 集合/无 RuntimeVisible/计数守卫） |
| `AgentScripts/TunableProbe.cs`、`PanelCloseProbe.cs` | Play 探针补 RuntimeTunables/白名单输出与 F11 关闭收尾（非入库脚本） |
| 文档 | 13 手册 §3.4、GM 分层设计 §5/§7 现行口径回写 |

### 验证证据

| 项 | 结果 |
|---|---|
| Unity 编译 | `recompile_status` → completed / 0 错 |
| DebugKernel EditMode | **21/21 PASS**（新增产品参数守卫） |
| L1 | `python scripts/gate/test.py --suite L1 --profile PullRequest` → **1819/1819 PASS** |
| Play：注册表 | `Tunables=16` / `RuntimeTunables=4`；Product 8 项全部 `visible=False`；`time.world-scale` set 0.5 → reset 成功 |
| Play：F11 | 面板 15 个按钮（3 动作 + 快捷调参 header/4 行 + 6 开关）；Bool「无限子弹」Clickable 翻转并恢复；截图 `AgentScripts/Artifacts/debug-panel-tunables4.png` |
| Doc | `python scripts/gate/doc-lint.py` → 0 violation / 0 warning |

### 边界

- 定档只改标记与运行期可见性；F12/编辑器仍可临时覆写，覆写不落盘、不进摘要、release 三宏剥离。
- **迁表/配置尚未实施**：届时把准心/视角默认值单源移到表/配置，并同步改造 provider 默认值来源；本批只完成“标注 Product + 隔离运行期”。
- F11 不再显示准心/视角参数；运行期调试这两组走 F12（`tune <id>`）或编辑器页。
- F11 滑杆的 OS 级指针可点性仍待真机/Player 验证（同第 20 章边界）。

## 第22章 武器试验参数运行期调参（伤害/射速）

<a id="ch22-weapon-tunables"></a>

> 2026-10-23。**状态：已完成（附验证证据）**——用户裁决“按第二条开工”：枪械数值本体不进调参注册表（已是 `#weapon.xlsx` 表数据），但给本地测试模式加**运行期试验覆写**（伤害/射速起步）。修正第 21 章“18 项未含武器参数”时的基数（16→18）与 F11 白名单（4→6）。

范围：`MatchTestRules` 实例覆写（R13 口径）+ 武器试验 provider；**动共享源（`Assets/LiteSim/Core`）→ BuildHash 重算**。

### 交付面

| 件 | 变更 |
|---|---|
| `Assets/LiteSim/Core/Scripts/MatchTestRules.cs` | 增 `DamageOverride`/`RpmOverride`（不进摘要、不可重放）+ 解析口 `ResolveWeaponDamage`/`ResolveFireIntervalFrames`（null/未覆写恒表值） |
| `Assets/LiteSim/Core/Systems/WeaponSystem.cs` | `TryConsumeShot` 击发节拍走解析口（rpm 覆写按 `ceil(TickRate×60/rpm)` 重算，不改表定义） |
| `Assets/LiteSim/Core/Systems/ShootingSystem.cs` | `baseDamage`（已装备武器）走解析口 |
| `Assets/LiteGame/App/Debug/TestModeRuntime.cs` | 增 `WeaponDamageOverride`/`WeaponRpmOverride` 跨局待用值 |
| `Assets/LiteGame/App/Debug/WeaponDebugTunables.cs`（新） | `weapon.damage`/`weapon.rpm`（Sandbox、RuntimeVisible、默认值来自武器表 `WeaponConfig.Default`，复位清 -1 回表值；活动局 + 待用值双落） |
| `Assets/LiteGame/App/Bootstrap/ContainerModule.cs` | 注入 `WeaponDebugTunables` 到内核 |
| `Assets/LiteGame/App/Procedure/ProcedureMatch.cs` | 进房种子（TestModeRuntime 待用值 → MatchTestRules，同爆头带覆写链） |
| 测试 | `WeaponSystemTests` +2（解析口语义 / 开火链消费覆写节拍）；`DebugKernelEditModeTests` +1（发现/写口/待用值双落） |
| 文档 | 13 手册 §3.4（18 项 / Sandbox 9 / F11 6 项） |

### 口径

- 覆写**只对本地测试模式生效**（同爆头带覆写：待用值 → 进房种子；真服务器无此链——房间级在线下发未做）。
- 覆写不进 `CombatValues` 摘要、不可重放；机制只读注入实例（R13），静态待用值仅调参工具读写。
- 定档仍走 `Luban/Data/#weapon.xlsx`（改表 → `Luban/gen.bat` → 数据面运行期摘要比对）；本覆写只作运行期试验。
- 覆写对**全武器**生效（不分武器行）；标签“默认步枪”指默认值/范围的显示来源。

### 验证证据

| 项 | 结果 |
|---|---|
| BuildHash | `gen-build-hash.py` → shared=`0a998c8ac6c9de89`（110 files）/ meta=`a149cc4b1906e9f7`；Play 主菜单版本号同值 |
| Unity 编译 | `recompile_status` → completed / 0 错 |
| DebugKernel EditMode | **22/22 PASS**（新增 `武器试验参数_发现写口与待用值双落`） |
| L1 | `python scripts/gate/test.py --suite L1 --profile PullRequest` → **1821/1821 PASS**（+2 武器覆写用例） |
| Play：注册表 | `Tunables=18` / `RuntimeTunables=6`；`weapon.damage=16`、`weapon.rpm=600`（Sandbox）入白名单；`time.world-scale` set/reset 正常 |
| Play：F11 | 面板 17 按钮（3 动作 + header/6 行 + 6 开关），武器两行在列；Bool「无限子弹」Clickable 翻转并恢复；截图 `AgentScripts/Artifacts/debug-panel-weapon-tunables.png` |
| Doc | `python scripts/gate/doc-lint.py` → 0 violation / 0 warning |

### 边界

- 只做伤害/射速两项；散布/弹匣/换弹时长等如需运行期试验，再扩 `MatchTestRules` 字段与 provider（同模式）。
- 房间级 GM 下发未做（同爆头带覆写“在线下发未做”挂账）——云端普通房/测试房无此覆写链。
- F11 滑杆 OS 级指针可点性仍待真机/Player 验证（同第 20/21 章边界）。
- 共享源改动已重算 BuildHash；两端构建需同哈希重建后才可联机（出包门禁 `--check` 兜底）。

## 第23章 弱网注入（传输层损伤 + HUD 可视化）

<a id="ch23-weaknet-injection"></a>

> 2026-10-24。**状态：已完成（附验证证据）**——L3 定向 **18/18**；WeakNet EditMode **9/9**；L1 **1838/1838**；BuildHash `--check-shared` 不变。

范围：客户端双向延迟/抖动（可靠帧固定延迟、严格 FIFO 保序）与不可靠帧丢包/重复/乱序；固定种子上下行独立 PRNG；注入行与 RTT/丢包曲线；L3 固定种子 loss-only 对跑；夜间定向步骤。

### 交付面

| 件 | 变更 |
|---|---|
| `Assets/LiteNet/Diagnostics/WeakNet/`（新） | `WeakNetProfile`/`WeakNetRandom`/`WeakNetStats`/`WeakNetClientTransport`；整目录三宏守卫（服务端 .NET 无宏 → 不含类型；release 不编译） |
| `Assets/LiteGame/App/Debug/WeakNet/`（新） | `WeakNetRuntime`（实例，profile/包装/统计/`IDebugTunableProvider`）、`WeakNetPresets`、`WeakNetSnapshot`、`NetHistory`（4Hz/240/降采样）、`WeakNetWiring`、`SetWeakNetPreset` 命令 |
| 装配 | `ContainerModule` 开发宏内建 runtime、包装真 KCP 工厂（`testRoom` 不包）；`DebugKernel` 注入并暴露快照；`BattleClient` 转发 `WeakNet OnError` |
| 读数/写口 | DevHUD 联机段注入行；F12 `weak <preset>` 经命令中心审计；F11/`tune` 数值旋钮 |
| 门禁 | `unity-tests` 定向；`test.py --filter`（替换分类表达式 + Quarantine 排除）；`nightly-gate.py` L3 WeakNet 步骤（独立 `L3-*` 日志） |
| 测试 | `WeakNetTransportTests` 17 例 + `WeakNet_LossOnly_StillCompletes` 对跑；`WeakNetWiringEditModeTests` 9 例 |

### 验证证据

| 项 | 结果 |
|---|---|
| L3 内核/对跑 | `dotnet test --filter FullyQualifiedName~WeakNet` → **18/18**；`test.py --suite L3 --filter ...` → **18 tests PASS** |
| EditMode | `unity-tests.py --mode editor --filter WeakNetWiringEditModeTests` → **8/8** |
| L1 | **1838/1838 PASS** |
| BuildHash | `--check-shared` OK（`0a998c8ac6c9de89`，110 files；本批不动闭包） |
| Unity 编译 | 0 error |

### 边界（如实）

- 只做客户端注入；服务端/云侧与 KCP 以下 UDP 层（可靠帧丢包）另批。
- 对跑用 loss-only（上行延迟会让输入帧在服务端过期；延迟语义由内核用例覆盖）。
- 本地测试房（进程内服）不包装；HUD 注入行只在真 KCP 房出现。
- 组合信封（SecureEnvelope+WeakNet+Kcp）未在 L3 强制，留云测手动覆盖。
- HUD 注入行的**运行期渲染** Play 冒烟未执行（需真 KCP 对局）：本批由 App 层 `WeakNetSnapshot`/`NetHistory` EditMode 用例 + Unity 编译覆盖，联机时按 13 手册 `weak` 命令人工确认一次。

## 第24章 调试内核收口（投影旁路修复 + R14 纪律守卫）

<a id="ch24-kernel-gate-r14"></a>

> 2026-10-25。**状态：已完成（已提交 3b50f245——与在途并行线同快照入库，见边界）**——L1 **1843/1843**（含 R14 新用例 5 例与「真实源码_全部目标_零违规」含两新受守根）；EditMode `DebugKernelEditModeTests` **26/26**（含新增 4 例）；Unity 编译 0 错；L0 依赖/秘密扫描 PASS；BuildHash `--check-shared` 不变（`0a998c8ac6c9de89`，110 files）。

范围：两个投影程序集根（`LiteGame.DevTool` + `Editor/Gm`）的调试**读数/动作/发现面**全部收口经 `DebugKernel` 门面，并新增纪律扫描 R14 防复辟（设计 §7.1 裁决落成机制）。

### 交付面

| 件 | 变更 |
|---|---|
| `DebugKernel` | 新读口：`IsTestModeActive`/`CurrentMatchId`/`CurrentSessionId`/`StartupBotCount`；新动作口：`RequestBotReset()`（`SimTestRules.BotResetRequested` 宿主钩子收口）；性能读模型三口：`SampleFrame(dt)`/`RecordHudBuildMs(ms)`/`TryTakePerf(out PerfReading)` |
| `App/Debug/DebugPerfSampler.cs`（新） | 性能读模型入住内核（§7.2 读模型"性能样本"行落实）：帧环 240 + p50/p95/p99（MathF 纯逻辑）+ GC/Draw/Batches ProfilerRecorder + HUD 耗时平滑；投影侧 `DevHudPerfSampler` 退役删除 |
| `GmTestModePage` | 测试模式段（进入/退出/传送/现场开关/启动项一览）、bot 段（重置假人/无限生命读数）、爆头导出段（`head.line`/`head.radius` 当前值经调参发现面）全部改经内核；非 Play 空态提示（与兄弟页同口径） |
| `DevHUD`/`DevHudSectionSet` | F5 现场包改 `TryTakeBattleSnapshot`（原直读 `BattleDiagnostics.Current/Take`）；日志过滤 `Log.CurrentMatchId`、导出会话键 `Log.CurrentSessionId` 改经内核；性能段改 `TryTakePerf`；构造器去 perf 参数 |
| `DebugPanel`/`DebugPanelUiToolkitHost` | Live 开关清单改经 `kernel.Options` 发现面（BindKernel 成功后填充，早建 UI 重建一次补按钮）；状态行改 `IsTestModeActive`；Host `FindInfo` 改查构造清单（原兜底直读 `TestModeOptions.All`） |
| `GmConfigDiagnosticsPage` | 状态行 `ErrorCount` 改经内核（未装配显示 `—`） |
| `DisciplineScanner`/`ScanTargets` | 新规则 **R14 投影禁直调调试源**（成员级禁令表 + `StripStrings` 防页面文案误报 + `lint-allow R14` 豁免机制）；`ScanTarget.IncludeEditorDirs` 让 Editor/Gm 受守根不被默认排除吞掉；两受守根登记进 `Default` |
| 测试 | L1 `DisciplineScannerTests` +5 例（命中/不误报/字面量/受守根登记/Editor 子树覆盖）；EditMode `DebugKernelEditModeTests` +4 例（读口/重置钩子/性能读模型×2） |

### 验证证据

| 项 | 结果 |
|---|---|
| L1 | `test.py --suite L1 --profile PullRequest` → **1843/1843 PASS**（定向 DisciplineScannerTests 48/48 先行） |
| EditMode | `unity command run_tests --mode editor --filter DebugKernelEditModeTests` → **26/26 PASS** |
| 全仓纪律 | 「真实源码_全部目标_零违规」含 `Assets/LiteGame/DevTool` 与 `Assets/LiteGame/Editor/Gm`（includeEditorDirs）两新受守根 → 0 违规 |
| Unity 编译 | 0 error（console_status `compilationFailed:false`；新 meta `DebugPerfSampler.cs.meta` 由管线刷新生成） |
| BuildHash | `gen-build-hash.py --check-shared` OK（本批不动共享闭包） |
| L0 | `l0-dep-scan.py` PASS（零浮动依赖/零秘密命中） |

### 边界（如实）

- **GmScenePage＝带参命令专面板**（§2.2 既有裁决）：`CommandCenter.SendAsync` 直用为全仓唯一 `lint-allow R14` 判例（权限门/拦截器/审计链全量生效，非旁路）。
- GmTestModePage 相关段在非 Play（内核未装配）时空态提示——编辑态直改静态表的旧路径退役（静态表态随 domain reload 归零，该路径本就不可靠）。
- DevHUD 性能段采样起点改为内核装配后（原为 HUD 自创建即采样；装配前空态，与 §7.5 失败语义一致）。
- `ClientDiagnosticsBundle`（业务诊断包）与 Runtime Pipeline/云端 GM 通道不在 R14 辖区（§7.8 非目标维持）。
- **入库形态**：与并行线（弱网注入批〔第23章〕、UI P2 令牌引用线、准心手感线、build-preflight 线）同入并行线快照 **3b50f245**——本批与弱网批共享 `DebugKernel.cs`/`DebugConsole.cs`/`DevHudSectionSet.cs`/施工进度卷 4 个文件，文件级暂存无法拆分两批 hunks，按仓库并行线快照惯例一并入库。
