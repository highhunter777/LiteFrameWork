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
| L1 全量 | `test.py --lane L1 --profile PullRequest` → **1816/1816**（原 1801 + 本批 15：CrashCapture 3 · GmHttpDiag 5 · RoomMismatchFlush 4 · RoomRunWorldRing 3，ReplayToolCli 扩世界环面） |
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
| L1 | `test.py --lane L1 --profile PullRequest` → **1818/1818 PASS**（原 1816 + 新增 sink 2 例） |
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
