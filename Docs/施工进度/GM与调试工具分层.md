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

## 待办

- L2（EditMode/PlayMode）夜间流水线；GM 面板四页手测（翻页生命周期/开关/滑杆对局实弹/爆头可视化随动）；场景页对 Play 中命令派发实测。
- ~~场景操作/表查询命令化（待议）~~ 已落定（追加批 4）；DevHUD 命令通道待真机需求。
