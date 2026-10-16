# GM 与调试工具分层

> 《GM 与调试工具分层设计》落地批（2026-10-11）。**状态：已完成（附验证证据）**——pipeline 编译 0 错、L1 1572 全绿；L2 夜间、四页手测待本机。

## 一刀：爆头覆写收口（SimTestRules 同型尾巴根治）

`CombatConfig.HeadHitLineDevOverride/HeadshotRadiusDevOverride`（public static 可变浮点，三宏内）被确定性路径直读（`ShootingSystem.cs:194` 爆头判据读 `HeadHitLineLive`、`SimRaycast.cs:80-81` 双柱几何读两个 Live）——与已根治的 SimTestRules 完全同型（§1.3"含 dev 宏内"）。收口：

- `MatchTestRules` 增 `HeadHitLineOverride/HeadshotRadiusOverride` 两字段（**不进 CombatValues 摘要、不可重放**——连续调参进摘要会随试验抖动联机身份）+ 静态解析口 `ResolveHeadHitLine/ResolveHeadshotRadius(rules)`（null ⇒ 恒烘焙导出值）。
- `SimRaycast.RaycastEntities` 三重载加 `MatchTestRules rules = null`（默认参数——L1 用例/既有调用零改动）；`ShootingSystem` 判定与射线判定改注入（:133/:194）。
- 客户端可视化四处传 `SimTestRules.ActiveMatch` 与真实弹道同值：`BattleLaserDriver`×2（激光截停）、`NewInputIntentSource`（瞄准射线）、`SimSandbox`（灰盒沙盒）；`BattleHeadshotDebugDrawer` 黄/红圈同改。
- 写通道：`TestModeRuntime` 增跨局待用值两字段；`ProcedureMatch` 进房种子；GM 面板滑杆/`HeadHitLineTuner`「取当前覆写值/复位」改双落（活动局实例即时生效 + 待用值跨局延续）；`CombatConfig` 静态覆写面整体删除（留单源指向注释）。

## 二刀：GM 工具箱拆页（god panel 闭环）

- `GmPanel`（原 621 行六区一窗，审计"低"条）→ **壳 90 行** + 四页件（`Editor/Gm/`）：`GmTestModePage` / `GmCommandsPage` / `GmScenePage` / `GmConfigDiagnosticsPage`（原③状态+④表查询+⑥产物核对合一），契约 `IGmPage`（OnShow/OnHide/Draw/Poll，轮询仅活动页），取件单源 `GmEditorResolve`（TakeContainer 口径）。
- EditorPrefs 统一前缀 `LiteGame.Gm.*`（旧 `LiteGame.ConfigPanel.*` 键弃用——开发偏好，未迁移）。
- 产物核对路径单源：`TableLayout.TableDataPath(file)` 新增（DataDir+file+".bytes" 拼接口径唯一——装载/服务端/编辑器三面共用），`ConfigService.LoadAsync` 同步改走；GmPanel:570/591 两条"低"债（手拼路径+表名硬编码）随页拆收口（`DefaultTableName` 常量化）。
- 命令化口径如实：传送已走 `ProcedureBattle → SendQueued(TeleportToAimCommand)`（审计+可撤销）——按钮无需改；进/退测试模式是 **pre-Play 请求桥**（容器尚不存在），维持一次性标志；场景操作/表查询未命令化（带参+异步，待议）——三分口径见设计文档 §2。

## 验证

- **pipeline 编译**：unity-pipeline（7801 实例）recompile → 0 错（首轮抓出 `GmScenePage` 缺 `using LiteFramework`，修复后过）。
- **L1 全量**：`test.ps1 -Lane L1 -Profile PullRequest` → **1572/1572 全绿**（含纪律扫描/表守卫）。
- **LiteSim 定向**：335/335（SimRaycast 几何/射击判定/回滚链在第一刀后逐位保持）。
- **BuildHash**：共享源集改动（MatchTestRules/CombatConfig/SimRaycast/ShootingSystem/SimTestRules）已重生成（与资源收集迁移批三的 `04162cb8` 链衔接，终值以收尾复算为准）。

## 追加批：开关写口 L1 接入（2026-10-12）

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

## 追加批 2：无限子弹空仓放行（2026-10-12）

**裁决**：无限子弹语义从"开火不消耗"扩为"空仓亦可开火"——`WeaponSystem.TryConsumeShot` 空仓门加 rules
判断（`MagAmmo <= 0 && !infinite` 才拦，注入规则实例读口不变——R6/R13 口径保持）；此前彻底干涸
（弹匣 0+备弹 0）的枪现场开开关仍打不响（空仓门先于无限分支、零备弹换弹被拒），只能靠切枪到未装备槽
拿满弹新枪恢复。换弹门不受影响（满弹/零备弹仍拒）。

- 语义同步：`MatchTestRules.InfiniteAmmo` 注释、`TestModeOptions` 开关标签（GM 面板显示名）加"空仓也可开火"。
- **用例**：`WeaponSystemTests.无限子弹_空仓放行_干涸弹匣照常开火不扣弹_零备弹换弹仍拒`（旧代码下必红——
  空仓门拦掉全部 Fire）；WeaponSystemTests 定向 13/13。
- **BuildHash**：共享源集（WeaponSystem/MatchTestRules）已重生成——shared `191f1143d32017bc`→
  `3c07d038aadb1e81`（76 文件）。

## 追加批 3：请求桥收口——TestModeRuntime 静态消费协议根治（2026-10-12）

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

## 追加批 4：场景操作命令化——表查询裁决维持直调（2026-10-12）

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

## 追加批 5：本地联测页（第五页）与「依赖与烙入」显示修正（2026-10-16）

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

## 待办

- L2（EditMode/PlayMode）夜间流水线；GM 面板五页手测（翻页生命周期/开关/滑杆对局实弹/爆头可视化随动）；场景页对 Play 中命令派发实测；本地联测页——显示/探针读数与起停运行态已实机核（追加批 5），日志尾随运行态经**续记两修**一并收口（2026-10-16）。
- ~~场景操作/表查询命令化（待议）~~ 已落定（追加批 4）；DevHUD 命令通道待真机需求。
