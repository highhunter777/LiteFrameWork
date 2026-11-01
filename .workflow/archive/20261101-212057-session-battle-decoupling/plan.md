# 方案：Session/Battle 解耦：对局运行归 Battle（A1 端口化 → A2 归位）

> 运行 session-battle-decoupling · 创建于 2026-11-01T18:26:07+08:00
> 修订：2026-11-01（按独立子代理三轮评审修订：首轮 P-1～P-8、第二轮 N-1、第三轮 B-1/B-2 及 minor）

## 背景与目标

- 现状：`Assets/LiteGame/Modules/Session/` 持有对局运行的构造与所有权——`AccountSessionFactory` 直接 `new BattleClient`，`MatchSessionFactory` 直接 `new BattleContext`，`BattleSession` 直接驱动 `BattleContext`，`RoomSessionFace` 直接包装 `BattleClient`。`BattleComposition` 只装表现内核。域矩阵 `Session → Battle` 直引 30 次，其中具体类型 24 次（`BattleContext` 12、`BattleClient` 11、`GameplayMatchIdentity` 1），全部落在 4 个实现文件；另 6 次为 `IBattleViewGroup` 等端口引用。Procedure/App 对战斗侧已零具体类型（只走 `IBattleRun`；仍持 `AccountSession`/`IRoomSessionBridge` 等会话/端口件，口径同 `ProcedureBoundaryTests`）。
- 要解决的问题：会话域的职责（账号、连接、Account scope、传输）与对局实现的所有权混在一起，导致 `Session → Battle` 是具体类型依赖；`Modules/Session` 名义上是生命周期容器，实际承担了对局运行聚合。
- 目标（可观察）：
  1. `BattleClient` 的 `new` 只出现在 Battle 域的 `BattleClientFactory`（A1 达成）；A2 后 Session 域不再 `new` 任何 Battle 具体件（含 `BattleContext`）。
  2. A2 完成后，`Modules/Session/**` 中 `BattleContext`/`BattleClient`/`GameplayMatchIdentity` 具体类型引用为 0；跨域只引 `Battle/Abstractions` 端口。
  3. `BattleSession`/`MatchSession`/`MatchSessionFactory`/`BattleRunFactory`/`RoomSessionFace` 住 `Modules/Battle/Session/`，由 `BattleComposition` 装配。
  4. Battle 程序集不引用 `LiteGame.Modules`；地图/诊断经 Battle 自持端口或装配根注入，不直引 Map/Dev 域。
  5. 门禁：Unity 0 错、L1、`domain-matrix --check`、`doc-lint` 全绿；新增 Session 边界守卫先红后绿。

## 需求澄清与假设

- 需要确认的问题：无（用户已确认方案 A：对局运行归 Battle，Session 只留账号/连接；A1 端口化 → A2 归位）。
- 已确认答复：2026-11-01 用户：“把 Session 和 Battle 的解耦方案定一下” → 方案 A；“按 A1 → A2 两批落设计文档并开批，按工作流来”。
- 采用假设：
  1. 程序集边界保持现状（`LiteGame.Modules` / `LiteGame.Modules.Battle` 两个 asmdef）；解域级具体依赖，不拆程序集、不引入中立契约程序集。
  2. `IAccountRuntime`/`IBattleClient`/`IBattleClientFactory` 住 `Battle/Abstractions`（Battle 的消费端口，Session 实现/注入），避免 `Battle → LiteGame.Modules` 反向引用；`IBattleRun`/`IBattleRunFactory`/`IMatchSessionFactory`/`IRoomSessionFace` 随实现迁 `Battle/Abstractions`（端口与实现同侧）。
  3. `IAccountRuntime` 不暴露根 `ServiceContainer`，改窄口 `ClientScope Scope` + `ServiceContainer CreateMatchServices()`；Battle 只创建 Match scope。服务容器面结构性收窄；`ClientScope` 资源域写入靠约定 + 评审（A2 `SessionBoundaryTests` 只扫 Session 侧，不构成对 `ClientScope` 写面的机械守卫；本批不做容器写口拦截）。
  4. 地图以 `Func<int, SimMapData>` / `Func<int, int>` 传递（LiteSim 类型），放在 `AccountSessionFactory` **构造**（`SessionComposition` 注入并 `?? throw`）；`IAccountSessionFactory.Create` 保持 5 参、`ProcedureMatch` 不动；工厂不依赖 Map/Config/Luban。
  5. 诊断从 `IAccountRuntime` 移出；`IBattleDevDiagnostics` 由 App 侧 `Func<IBattleDevDiagnostics>` 注入 `BattleComposition`（App 引 `LiteGame.Modules` 的 `DevHooks`，Battle 不引 Modules）。
  6. `BattleContext`/`BattleInputPipeline` 改吃 `IBattleClient`；`BattleClient` 具体仅由 `BattleClientFactory` 构造。
  7. `IBattleClient.Client`（`RoomClient`）是过渡逃生口：Session/Battle 现有调用面需要；登记收口批次，后续把 Session 真正需要的成员上端口。
- 假设影响与风险：A1 完成全部消费面切换（含 `BattleContext` 构造签名，影响 11 处构造点：1 处生产 + 10 处测试；具体类实现接口，测试构造无需改型），A2 为搬迁 + 装配移交 + 守卫收紧；A1 结束后 Session 域仍允许 `BattleContext` 具体（`new` 在 `MatchSessionFactory`），A2 清零。

## 范围与非目标

- 范围内：
  - A1：`IBattleClient`/`IBattleClientFactory`/`IAccountRuntime` 端口与实现；`BattleClientFactory`；`AccountSession`（实现 `IAccountRuntime`；`Battle` 属性/构造改 `IBattleClient`，Account 服务域按 `IBattleClient` 登记）/`AccountSessionFactory`（经工厂创建客户端，构造注入地图委托）；`BattleContext`（构造/字段/`DebugBattle` 改 `IBattleClient`）/`BattleInputPipeline`/`MatchSession`（`BattleClient` 读口改 `IBattleClient`）/`BattleSession`（`_battle` 改接口）/`RoomSessionFace`（构造/字段改接口）/`MatchSessionFactory`（`Resolve<IBattleClient>`＋BattleContext 注册类型 `typeof(IBattleClient)`）/`BattleRunFactory`/`NetworkDebug` 消费面切换；`SessionComposition` 补 `DependsOn += "Battle"` 并注入 `IBattleClientFactory`/地图委托；`LiteNet.Tests` 源链接同步；`SessionBoundaryTests`（A1 断言）先红后绿。
  - A2：4 文件 5 类型迁 `Modules/Battle/Session/`（`BattleSession`、`MatchSession`+`MatchSessionFactory`、`BattleRunFactory`、`RoomSessionFace`）；端口迁 `Battle/Abstractions`；`BattleComposition` 注册客户端/Match/运行工厂并补 `DependsOn += Identity`；`SessionComposition` 只留 Account/传输并补 `DependsOn += Battle`；诊断改注入（`GameEntry.RegisterModules` 传 `() => DevHooks.Diagnostics`）；LuaBridge asmdef/允许表加 `LiteGame.Modules.Battle`；`domain-matrix.py` 允许边文案与 `--write`；测试链接/用例同步；代码注释同步（`ProcedureBattle.cs:57/64/127`、`IBattleRun.cs:26`、`MatchSessionFactory.cs:22`、`IAccountSessionFactory.cs`“创建 BattleClient”、`IRoomSessionFace.cs`“App 侧适配 BattleClient”的归属文案）；守卫收紧（Session 域零具体类型）。
  - 文档：路线图 §3.5/§4.2（设计目标，已更新）；说明 14/02、规范 03、依赖注入专项、地图数据专项在 A2 收尾回写；施工进度新章。
- 非目标：
  - 不拆 `LiteGame.Modules` / `LiteGame.Modules.Battle` 程序集；不新增第 13 域（方案 C 不采用）。
  - 不改 `LiteSync`/`LiteNet`/`LiteSim`/BuildHash 共享源集；不改协议。
  - 不改 Match/Account scope 的框架语义（总设计 §6.2 保持）。
  - 不顺手结清 `BattleClient` 默认传输上移（仅修正登记路径/行号，不扩大本批）；不收口 `IBattleClient.Client` 逃生口（登记技术债，后续网络端口化批次）；不做 IVFXService 命名、Editor 半改名、`Runtime/Framework` 第四段等登记债。

## 现状与约束

- 相关代码：
  - `Modules/Session/AccountSessionFactory.cs`：`new BattleClient`，Account scope 登记；`LiteNet.Tests` 源链接。
  - `Modules/Session/MatchSessionFactory.cs`：`new BattleContext`、`BattleScenes.ResolveMapId`、`IMapProvider`、`GameplayMatchIdentity`。
  - `Modules/Session/BattleSession.cs`：持 `BattleContext`/`BattleClient`、`DevHooks.Diagnostics`。
  - `Modules/Session/BattleRunFactory.cs`、`RoomSessionFace.cs`、`SessionComposition.cs`。
  - `Modules/Battle/BattleContext.cs`（构造收 `BattleClient`/`ClientScope`）、`BattleInputPipeline.cs`、`BattleClient.cs`、`BattleComposition.cs`。
  - `Modules/Dev/DevHooks.cs`（静态诊断）、`Modules/Map/{Abstractions/IMapProvider.cs,BattleScenes.cs}`。
  - `Adapters/Dev/Debug/NetDebug/NetworkDebug.cs`（`DebugBattle.Disconnect/BeginReconnect`）、`Adapters/LuaBridge/RoomBridge.cs`（`IRoomSessionFace`）。
  - `App/GameEntry.cs`（注册序 ⑯Battle → ⑰Session）。
- 约束：
  - `LiteGame.Modules.Battle.asmdef` 不引 `LiteGame.Modules`；A2 必须保持。Battle 可引 `LiteGame.Core`、`LiteFramework.Runtime`、`LiteSim`、`LiteNet`、`LiteSync`、`LiteGame.Modules.{Config,Settings,Input,Camera}`、`Adapters.Unity`、Luban、UniTask。
  - `IMapProvider`/`BattleScenes` 住 `LiteGame.Modules` 总程序集，Battle 不可直接引；地图以 LiteSim 类型经 `IAccountRuntime` 传入。
  - `DevHooks` 住 `LiteGame.Modules` 总程序集；诊断由 App 注入 `Func<IBattleDevDiagnostics>`。
  - 注册序：Map ⑫ → Input ⑭ → Battle ⑯ → Session ⑰；`BattleComposition` 需补 `Identity` 依赖，`SessionComposition` 需补 `Battle` 依赖（声明图与注册序一致）。
  - `LiteNet.Tests` 源链接 `AccountSessionFactory.cs`；端口文件需新增 Compile Include；若缺 `LiteFramework.Core` 引用需补 ProjectReference。
  - Assets 下移动走 unity-pipeline `move_asset`；目录搬动按 `directory-move-sync` 六面核对（`toolchain.py check`）。
  - 说明卷描述现状，A2 落地前不得写成“已归位”。

## 候选方案与取舍

| 方案 | 核心思路 | 优点 | 代价/风险 | 结论 |
|---|---|---|---|---|
| A（采用） | 对局运行归 Battle；A1 端口化 + 消费面切换，A2 搬迁 + 装配移交；端口随实现迁 Battle/Abstractions | Session→Battle 具体引用清零；职责归位；Battle 不反向引 Modules；程序集无环 | 搬 ~360 行 + 3 个新端口 + 装配/矩阵/测试链接；A1 影响 11 处 `BattleContext` 构造点（仅 1 处生产构造改型） | **采用** |
| B+ | 不搬所有权，仅把具体引用收进 4 个实现文件并加例外守卫 | 改动小 | 具体引用仍 24 次，矩阵计数不降；只是登记例外 | 否决 |
| C | 新建 `Modules/Match` 对局运行域，Session 只留账号/连接 | 职责最清晰 | 新增第 13 域、asmdef、装配、矩阵、文档全套；成本最高 | 备选，不在本次 |

推荐理由：24 次具体引用全部落在 4 个“运行/工厂”文件；这些文件的职责本就是 Battle，搬走后跨域具体引用归零，且 `IAccountRuntime` 放 Battle/Abstractions 由 Session 实现，依赖倒置避免 asmdef 环。B+ 只登记例外；C 成本高于收益。

## 设计要点

- 接口/数据（A1 落地；诊断注入为 A2）：
  - `IBattleClient`（`Modules/Battle/Abstractions/IBattleClient.cs`）：`RoomClient Client`、`ClientSessionPhase Phase`、`bool Connected`、`NetworkQuality Quality`、`bool IsOwner`、`void TickIncoming()`、`void TickOutgoing()`、`void SendStartMatchRequest()`、`bool BeginReconnect()`、`void Disconnect()`、`IDisposable`。`BattleClient` 实现（`LastError` 无消费点，保留在具体类、不上端口）。`Client` 是过渡逃生口，登记收口批次。
  - `IBattleClientFactory`（Battle/Abstractions）：`IBattleClient Create(string host, int port, string roomId, string token, string buildHash, IClientTransport transport)`；实现 `BattleClientFactory`（Battle 域，唯一 `new BattleClient`）。
  - A1 具体消费面全部改接口：`AccountSession.Battle`/构造 → `IBattleClient`，Account 服务域按 `IBattleClient` 登记；`BattleContext` 字段/构造/`DebugBattle` → `IBattleClient`；`MatchSession.BattleClient`、`BattleSession._battle`、`RoomSessionFace` 构造/字段 → `IBattleClient`；`MatchSessionFactory` 改 `Resolve<IBattleClient>` 与 `typeof(IBattleClient)`（不再按具体类型解析，否则 Match 域运行期解析失败）。
  - `IAccountRuntime`（Battle/Abstractions）：`ClientScope Scope`、`ServiceContainer CreateMatchServices()`、`IBattleClient Client`、`MatchTestRules Rules`、`Func<int, SimMapData> Maps`、`Func<int, int> ResolveMapId`。`AccountSession` 实现（`Battle` 属性保留具体名，`IAccountRuntime.Client` 显式实现返回同一实例；地图委托由工厂构造传入并持有；`CreateMatchServices()` 返回**未 Seal** 的 Match 子容器，由 `MatchSessionFactory` 注册后 Seal；`TestRoom` 无消费点，不上端口；`AccountSession.TestRoom` 具体属性保留；`Services` 具体属性保留给测试/装配，不进端口）。`AccountSession` 构造签名相应补地图委托两参。
  - `BattleContext(IBattleClient battle, ClientScope accountScope, IInputService inputProvider = null, SimMapData map = null, uint gameplayDigestBase = 0, MatchTestRules rules = null, IEventBus events = null, IMatchIdentity matchIdentity = null, DiagnosticIdentity? diagnosticsIdentity = null)`；`BattleInputPipeline(IBattleClient battle, ...)`。
  - `IBattleRunFactory.Create(IAccountRuntime account)`；`IMatchSessionFactory.Create(IAccountRuntime account, MatchTestRules rules = null)`；`IBattleRun`/`IBattleRunFactory`/`IMatchSessionFactory`/`IRoomSessionFace` 迁 `Battle/Abstractions`（A2）；`IAccountSessionFactory`/`IMatchJoin`/`ITransportFactory`/`ILocalTransportFactory`/`IRoomSessionBridge` 留 `Session/Abstractions`。
  - `AccountSessionFactory` 新签名：`(ClientScope rootScope, ServiceContainer rootServices, Func<bool, MatchTransport> transportFactory, HandshakeIdentity handshake, IBattleClientFactory battleClients, Func<int, SimMapData> maps, Func<int, int> resolveMapId)`；`Create(host, port, room, token, testRoom)` 保持 5 参（`ProcedureMatch` 不动）；地图委托由 `SessionComposition` 从 `IMapProvider`/`BattleScenes` 取并 `?? throw`，工厂不依赖 Map 类型。
  - A2 诊断：`BattleComposition(Func<IBattleDevDiagnostics> devDiagnostics = null)`（App `GameEntry.RegisterModules` 传 `() => DevHooks.Diagnostics`）；`BattleRunFactory`/`BattleSession` 构造注入；`BattleSession` 不再调 `DevHooks`。
- 关键流程：
  1. A1：`BattleComposition` 注册 `IBattleClientFactory`；`SessionComposition` 补 `DependsOn += "Battle"`，构造 `IAccountSessionFactory` 时注入 `IBattleClientFactory` 与地图委托（`?? throw`）；`AccountSessionFactory` 创建 Account scope/services/transport → 调客户端工厂 → **按 `IBattleClient` 登记** → 构造 `AccountSession`；`MatchSessionFactory` 解析 `IBattleClient` 并以 `typeof(IBattleClient)` 注册 `BattleContext` 工厂，`MatchSessionFactory`/`BattleRunFactory` 改吃 `IAccountRuntime`（仍住 Session，A2 搬）。
  2. A2：`BattleComposition` 注册 `IBattleClientFactory`/`IMatchSessionFactory`/`IBattleRunFactory` 并补 Identity；`SessionComposition` 只注册 `ITransportFactory`/`IAccountSessionFactory` 并补 Battle 依赖；运行 4 文件 5 类型迁 `Modules/Battle/Session/`；端口迁 Battle/Abstractions。
  3. 流程：`ProcedureMatch` 建 `AccountSession`（实现 `IAccountRuntime`/`IMatchJoin`），`ProcedureBattle` 调 `IBattleRunFactory.Create(accountRuntime)` 得 `IBattleRun`；无具体 Battle 件。
- 失败与边界：
  - `IBattleClientFactory.Create` 失败：`AccountSessionFactory` 保持 `accountScope.Dispose()` 回滚。
  - `MatchSessionFactory.Create` 失败：保持 try/catch 释放 Match scope。
  - 地图委托在 `SessionComposition` 构造 `AccountSessionFactory` 时 `?? throw`，缺地图 fail-fast；诊断 `Func<IBattleDevDiagnostics>` 可为 null（发布形态）。
  - `BattleClient` 的 KCP 安全信封仍由 transport 注入；工厂不改变 fail-closed。
  - A1 过渡：Session 域允许 `BattleContext` 具体（`MatchSessionFactory`/`BattleSession`），守卫白名单（文件+类型）带批次号 `A2`；A2 删除白名单、空表清零。
- 机制化约束：
  - `SessionBoundaryTests` 按“具体类型清单”扫描代码区（去注释/字符串；`BattleContext`/`BattleClient`/`GameplayMatchIdentity`），A1 白名单 2 文件 + 批次号，A2 空表；端口判定口径＝`Modules/Battle/Abstractions/**` 顶层声明的类型。
  - `IAccountRuntime` 不暴露根容器；`CreateMatchServices()` 是 Battle 唯一容器入口。

## 通用性与扩展性

- 根因/共性问题：业务实现的所有者住进了生命周期容器域；修法是“端口随实现归位 + 消费端口依赖倒置”，不是对当前调用方加特判。
- 复用与扩展点：`IAccountRuntime` 是 Battle 对会话运行时材料的唯一消费口；新增对局能力只扩展该端口与 `IBattleClient`，不新增 Session→Battle 具体依赖。`IBattleClient.Client`（`RoomClient`）是过渡逃生口，登记收口批次；后续把 Session 真正需要的成员上端口。
- 避免硬编码/特判：地图/诊断/规则一律经端口；守卫按具体类型判红，不按文件名白名单（A1 白名单带 A2 批次号，A2 删除）。

## 测试策略

- 测试层级与关键用例：
  - L1 `Tests/LiteFramework.Core.Tests`：新增 `SessionBoundaryTests`（文本扫描，不依赖被测类型，可先红）；`ClientAsmdefAllowlistTests` 现有正向允许表已覆盖 Battle 不引 Modules，A2 只需登记 LuaBridge→Battle 边，不重复新增断言。
  - L1 `Tests/LiteNet.Tests`：`AccountSessionFactoryTests` 改用 `IBattleClientFactory`/`IAccountRuntime` 替身，验证 Account scope 登记/释放、进房事件透传、规则/地图委托注入；`:76` 工厂构造补新参数、`:91` 的 `Resolve<BattleClient>` 改 `Resolve<IBattleClient>`、`:156` 的 `BattleClient firstBattle = first.Battle` 改 `IBattleClient`/`var`；源链接新增 `IAccountRuntime.cs`/`IBattleClient.cs`/`IBattleClientFactory.cs`，必要时补 `LiteFramework.Core` 引用。
  - EditMode：`BattleSessionEditModeTests`（9 处 `new BattleContext` 因具体类实现接口无需改型）、`BattleRunPortEditModeTests`（1 处构造同理；A2 `new BattleSession` 诊断补参 `:88`）、`MatchLoopLeakEditModeTests`（A1：`:83` 工厂构造补 `IBattleClientFactory`/地图委托，`MatchSessionFactory` 去掉 `IMapProvider` 后删除 `FakeMapProvider`/`maps:` 参数；A2：`:98` 注入 `DevBattleDiagnostics` 生产实现；保留静态读口附着/清空断言与 `match.Services.IsDisposed`/`account.Scope.IsDisposed`，不降级；`:17` 测试注释同步“根作用域残留见证”口径）、`RoomBridgeEditModeTests`（FakeSession；`LiteGame.EditModeTests.asmdef` 已含 Battle，核对即可）。
  - PlayMode（A2 回归门）：`BattleSmokePlayModeTests`（Boot 起服 → 本地测试房进局 → 开局/开火 → 离场；断言 `LastCreated == null`、VFX/音频计数归零）。其 TearDown 容器探针被 `_container = null` 短路，不能作为释放证据（AC10 登记）。
  - 门禁：`python scripts/gate/test.py --suite L1 --profile PullRequest`、`python scripts/gate/domain-matrix.py --check`、`python scripts/gate/doc-lint.py`、Unity recompile 0 错、`python scripts/toolchain.py check`（A2 收尾，记录目录搬动六面核对结果）。
- 如何先失败后通过：
  - `SessionBoundaryTests` A1 断言 `AccountSessionFactory.cs` 不含 `new BattleClient(` 且 Session 域除白名单外无 `BattleClient` 具体；当前必红（现文件有 `new BattleClient`），施工后绿。A2 删除白名单后同一用例再次先红（若搬迁不彻底）再绿。
  - `AccountSessionFactoryTests` 先改到端口替身（编译失败/断言失败均为红证据），施工后通过；`SessionBoundaryTests` 用去注释/字符串的代码区扫描，避免仓库注释中的关键字造成假红。
  - 红证据由 `wf verify --red` 记录；不得以删除用例/降低断言替代。

## 验收标准

- [ ] AC1：`Assets/LiteGame/**` 生产代码中 `new BattleClient(` 只出现在 Battle 域的 `BattleClientFactory`（测试直构不在此限）；`AccountSessionFactory` 无 `new BattleClient`。
- [ ] AC2：A1 后 `SessionBoundaryTests`（A1 版）绿；A2 后 `Modules/Session/**` 不含 `BattleContext`/`BattleClient`/`GameplayMatchIdentity` 具体类型引用（A2 版无白名单绿；扫描用 `\b` 词界，避免 `IBattleClient`/`IBattleRun` 误命中）。
- [ ] AC3：`IAccountRuntime`/`IBattleClient`/`IBattleClientFactory` 住 `Battle/Abstractions`；`LiteGame.Modules.Battle.asmdef` references 不含 `LiteGame.Modules`；LuaBridge asmdef 新增 `LiteGame.Modules.Battle` 并登记 `ClientAsmdefAllowlistTests` 允许表。
- [ ] AC4：`BattleSession.cs`/`MatchSessionFactory.cs`（含 `MatchSession`）/`BattleRunFactory.cs`/`RoomSessionFace.cs` 位于 `Modules/Battle/Session/`（4 文件 5 类型）；`Modules/Session/` 只剩 `AccountSessionFactory.cs`、`SessionComposition.cs`、`ProductTransportFactory.cs`、`MatchTransport.cs`、`Setup/MatchRulesSeeder.cs`、`Transport/LocalTransportRegistry.cs`、`Abstractions/**`。
- [ ] AC5：4 个运行端口住 `Battle/Abstractions`；`SessionComposition` 不再注册 `IMatchSessionFactory`/`IBattleRunFactory`；Procedure 只经端口消费（`ProcedureBoundaryTests` 绿）。
- [ ] AC6：`Assets/LiteGame/Modules/Battle/**` 代码区无 `DevHooks` 引用；诊断经 `BattleComposition(Func<IBattleDevDiagnostics>)` 装配注入（App 传 `() => DevHooks.Diagnostics`）；`MatchLoopLeakEditModeTests` 的附着/清空断言仍绿。
- [ ] AC7：`Assets/LiteGame/Modules/Battle/**` 的**去注释/字符串代码区**无 `IMapProvider`/`BattleScenes`/`LiteGame.Modules.Dev` 引用，且 asmdef references 不含 `LiteGame.Modules`/`LiteGame.Modules.Dev`；地图经 `IAccountRuntime.Maps/ResolveMapId`（LiteSim 类型）；`BattleContext.cs:168` 错误文案同批改为 `IAccountRuntime.Maps`（相关注释同步）。
- [ ] AC8：`domain-matrix --check` 绿；`ALLOWED_EDGES` 中所有失真文案（`Session→Battle`、`Procedure→Battle`、`Procedure→Session`、`Session→Input`）更新后 `--write` 一致；A2 后归零的允许边（如 `Session→Input`）删除；`SessionBoundaryTests` 具体类型计数为 0（机械口径：端口＝`Battle/Abstractions/**` 顶层声明）。
- [ ] AC9：Unity recompile 0 错；L1（PullRequest）全绿；EditMode filter `BattleSessionEditModeTests|BattleRunPortEditModeTests|MatchLoopLeakEditModeTests|RoomBridgeEditModeTests` 绿；PlayMode `BattleSmokePlayModeTests` 绿（Boot→本地测试房进局→开局/开火→离场，断言 `LastCreated == null` 与 VFX/音频计数归零；根作用域残留由 `MatchLoopLeakEditModeTests` 逐次断言 `account.Scope.IsDisposed`/`match.Services.IsDisposed`/`match.Events.IsDisposed` 覆盖，`rootScope.OwnedCount==0` 仅作加固）；`doc-lint` 0 violation。PlayMode 冒烟走 `TestModeRuntime` 请求桥，属装配/流程冒烟，不作为红线 13 真机操作证据；真实 Player 冒烟按项目约定需用户显式指令出包，列为交付后联机测试项。
- [ ] AC10：A2 收尾回写路线图 §3.5/§4.2、说明 14/02/17（`17:135` 的 ⑰ 产物与 `17:543` 会话锚点均按迁后归属改指 `Modules/Battle/Session/`）、说明 03（§5 会话装配拆两半工厂、§6 `BattleContext.Tick` 路径应指 `Assets/LiteGame/Modules/Battle/BattleContext.cs`、§7.1 `MatchSessionFactory.Create` 归属）、规范 03 §4「对局流程（Main→Match→Battle）」行（语义锚点，当前 50–51 行）、依赖注入专项、地图数据专项、`Docs/施工进度/客户端重构线.md` **新增第 20 章并同步章节索引**；同步代码注释（`ProcedureBattle.cs:57/64/127`、`IBattleRun.cs:26`、`MatchSessionFactory.cs:22`、`IAccountSessionFactory.cs`、`IRoomSessionFace.cs`）；`BattleContext.cs:168` 错误文案改 `IAccountRuntime.Maps`；审计登记 `类设计.md:38`/`技术债.md:26` **既有漂移，本批顺手修正陈旧路径/行号（低）**，在 `Docs/审计/客户端/登记/技术债.md` 追加 `IBattleClient.Client` 逃生口条目（收口批次）与 `BattleSmokePlayModeTests.TearDown` 容器探针短路（测试面，低）；`toolchain.py check` 通过并记录目录搬动核对。

## 风险与回滚

- 风险：
  1. `IBattleClient` 面与 `BattleContext`/`BattleInputPipeline`/`NetworkDebug` 调用面不一致 → A1 编译失败；先 grep 全量调用面，逐文件切换，Unity 编译逐轮确认。
  2. `LiteNet.Tests` 源链接缺端口/框架引用 → L1 编译红；A1 任务同步补 Compile Include/ProjectReference。
  3. `IAccountRuntime` 的 `CreateMatchServices()` 仍返回容器，Battle 可在 Match scope 内做任意注册；服务容器面已结构性收窄（不暴露 Account/根容器），`ClientScope` 资源域写入靠约定与评审（`SessionBoundaryTests` 只扫 Session 侧，不构成写面机械守卫；不在本批做拦截）。
  4. LuaBridge→Battle 新边未登记 → 允许表红；A2 同批登记。
  5. 搬迁 `.meta`/GUID 丢失 → 全部 `move_asset`；`toolchain.py check`/目录搬动六面核对。
  6. A2 非纯搬迁（签名/装配/诊断注入同步），回滚需同时撤销搬迁与注册移交；A1 可独立回滚。
  7. A1 过渡期 Session 仍持 `BattleContext` 具体，守卫白名单带批次号；A2 删除白名单，防止白名单长期化。
  8. Account 服务域只登记 `IBattleClient` 后，遗漏的 `Resolve<BattleClient>` 会在运行期解析失败 → A1 首步 grep 全部解析点（`MatchSessionFactory` 与测试），以编译 + `MatchLoopLeakEditModeTests` 兜底。
- 监控/发现：Unity recompile、L1、EditMode filter、PlayMode `BattleSmokePlayModeTests`、`domain-matrix`、`doc-lint`、`toolchain.py check`；守卫先红后绿。
- 回滚：A1 单独回滚（端口新增 + 构造点替换，不动文件位置）；A2 回滚用 git revert + `move_asset` 反向搬迁，并恢复 `DependsOn`/allowlist/矩阵脚本；两批均不改共享源集/BuildHash。
