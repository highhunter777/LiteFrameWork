# 方案评审（独立子代理）：Session/Battle 解耦：对局运行归 Battle（A1 端口化 → A2 归位）

> 运行 session-battle-decoupling · 创建于 2026-11-01T18:26:07+08:00
>
> 本文件由主代理根据独立子代理的返回整理落盘；不是主代理的自评。

## 评审输入

- 评审对象：`plan.md`（方案与验收标准）
- 代码/文档基线：`git rev-parse HEAD` = `48ccda6179c9e7369d2ebe6966af1e9167094db4`；工作区含未提交改动（接口自持收口、设计文档更新），评审按工作区现状
- 评审范围：`plan.md`、路线图 §3.5/§4.2、说明 14、说明 02、依赖注入专项设计、地图数据专项设计；`Modules/Session/**`、`Modules/Battle/**`、`Modules/Dev/**`、`Modules/Map/**`、`GameEntry.cs`、`NetworkDebug.cs`、`RoomBridge.cs`、相关 asmdef、`domain-matrix.py`、`LiteNet.Tests`/`LiteFramework.Core.Tests`/EditMode 用例
- 不在范围：未运行 dotnet/Unity（避免写盘与触发重编译）；不评审其他并行线
- 子代理标识：Qoder Agent（general-purpose，独立上下文；本次评审调用）
- 子代理约束：只读评审，不修改生产代码、测试与门禁
- 评审时间：2026-11-01

## 方案检查

- 目标可观察、范围与非目标清晰：基本合格（方向与根因经代码核对成立；范围有缺口，见 P-1～P-8）
- 现状与根因有代码/配置/文档依据：成立。实测 `Session→Battle=30`，具体类型 24（`BattleContext` 12 + `BattleClient` 11 + `GameplayMatchIdentity` 1），落在 4 个实现文件；`LiteGame.Modules.Battle.asmdef` 当前不引 `LiteGame.Modules`
- 候选方案取舍充分，推荐理由回应约束：A/B+/C 取舍成立；A 的“避免 asmdef 环”理由成立
- 设计覆盖接口、数据、关键流程、失败与边界：不足。端口签名、诊断/地图的 L1 可编性、容器窄口、装配依赖声明缺失（P-1/P-2/P-6/P-7）
- 通用性与扩展点明确，非特判式补丁：方向成立，但 `IBattleClient` 面与 `IAccountRuntime` 容器暴露有反例（P-4/P-6）
- 测试策略能先失败后通过，覆盖主路径、边界与错误路径：不足。测试影响面漏 `MatchLoopLeak`/`BattleSession`/`RoomBridge`，A1 红证据未定义（P-5）
- 风险、监控与回滚可执行：部分可执行；A2“纯搬迁”的假设不成立（P-2）；目录搬动六面同步未核对（minor）

## 验收标准可判定性

| 验收标准 | 判定方式 | 是否可观察/可判定 | 说明 |
|---|---|---|---|
| AC1 | `grep -rn "new BattleClient(" Assets/LiteGame` = 1（`BattleClientFactory`） | 可判定（需限定生产代码） | 测试侧多处直构，按字面会假红 |
| AC2 | `SessionBoundaryTests` + grep Session 域具体类型 = 0 | 可判定 | 需写明 A1 过渡白名单与 A2 收紧动作 |
| AC3 | asmdef + 允许表 | 可判定 | 漏 LuaBridge→Battle 反向边（P-3） |
| AC4 | 目录清单断言 | 可判定 | “5 文件”与 4 文件口径冲突；Session 剩余文件未列 |
| AC5 | 端口文件位置 + 装配注册 grep | 可判定 | 需补“SessionComposition 不再注册运行工厂” |
| AC6 | grep `DevHooks` + `MatchLoopLeakEditModeTests` | 可判定 | 用例未点名 |
| AC7 | grep `IMapProvider/BattleScenes` + asmdef | 可判定 | 建议同时断言无 `LiteGame.Modules.Dev` |
| AC8 | `domain-matrix --check` + `SessionBoundaryTests` | 部分不可判定 | 脚本按类型名计数、无端口/具体区分；需落守卫并给机械口径 |
| AC9 | Unity recompile + L1 + EditMode filter + doc-lint | 可判定 | filter 未点名 |
| AC10 | `git diff` + 文档清单 | 部分可判定 | 回写面漏 02/03/依赖注入/地图专项；审计登记路径漂移未列 |

## 发现的问题与处理

| ID | 严重度 | 问题 | 处理 | 状态 |
|---|---|---|---|---|
| P-1 | blocker | A1 让 `AccountSession` 实现 `IAccountRuntime`，而 `IAccountRuntime.Diagnostics` 的 `IBattleDevDiagnostics.Attach(BattleContext)` 引用 `UnityEngine`；`LiteNet.Tests` 源链接 `AccountSessionFactory.cs`，net8 纯工程无法编译。地图若注入 `IMapProvider` 会连带 Map/Config/Luban 源链接 | 诊断移出 `IAccountRuntime`，改由 App 侧 `Func<IBattleDevDiagnostics>` 注入 `BattleComposition`；地图固定为 `Func<int, SimMapData>`/`Func<int,int>`，由 `AccountSessionFactory.Create` 参数传入，工厂不依赖 `IMapProvider` | 已修待复评 |
| P-2 | blocker | 4 个运行端口迁 `Battle/Abstractions` 后签名仍收 `AccountSession`/`MatchSession` 具体类，会形成 `Battle → LiteGame.Modules` 反向引用；`BattleContext` 构造签名变化涉及 11 处 `new BattleContext` 未列入；A2“纯搬迁”假设不成立 | 明确新签名：`IBattleRunFactory.Create(IAccountRuntime)`、`IMatchSessionFactory.Create(IAccountRuntime, MatchTestRules)`、`BattleContext(IBattleClient, ClientScope, ...)`；A1 完成全部消费面切换，A2 为搬迁+装配移交+签名收口；11 处构造点列入 A1 影响面 | 已修待复评 |
| P-3 | blocker | `IRoomSessionFace` 迁 `Battle/Abstractions` 后，`Adapters/LuaBridge/RoomBridge.cs` 需要引用 `LiteGame.Modules.Battle`，方案与允许表均未登记 | 范围加入 LuaBridge asmdef 新增 `LiteGame.Modules.Battle`、`ClientAsmdefAllowlistTests` 允许表登记、路线图允许边补一行 | 已修待复评 |
| P-4 | major | `BattleContext.DebugBattle` 改 `IBattleClient` 后，`NetworkDebug.cs` 的 `Disconnect()`/`BeginReconnect()` 未在接口面；`MapId/HasStartGame/StartGame` 是 `RoomClient` 成员，方案误列为新增转发 | `IBattleClient` 补 `Disconnect`/`LastError`；其余经 `RoomClient Client` 使用；`NetworkDebug.cs` 列入 A1 影响面 | 已修待复评 |
| P-5 | major | 测试影响面漏 `MatchLoopLeakEditModeTests`（诊断附着/清空）、`BattleSessionEditModeTests`（10 处构造 + Account Scope 登记断言）、`RoomBridgeEditModeTests`（FakeSession）；把 `AccountSessionFactoryTests` 误列在 EditMode | 测试面逐条列入；AC6/AC9 点名用例与 filter；`AccountSessionFactoryTests` 只归 `Tests/LiteNet.Tests` | 已修待复评 |
| P-6 | major | `IAccountRuntime` 暴露根 `ServiceContainer` 与“只读使用”自相矛盾（`MatchSessionFactory` 要做 `CreateScope/Register/Seal`）；违反机制化红线 | 容器改窄口：`ClientScope Scope` + `ServiceContainer CreateMatchServices()`（Session 实现内部只调 `CreateChild/CreateScope`），不暴露根容器；在方案写明可执行约束 | 已修待复评 |
| P-7 | major | 装配依赖声明与注册序变更未写：`BattleComposition.DependsOn` 缺 `Identity`；`SessionComposition.DependsOn` 缺 `Battle`（迁移后需取 `IBattleClientFactory`） | A2 任务明确补 `BattleComposition.DependsOn += "Identity"`、`SessionComposition.DependsOn += "Battle"` | 已修待复评 |
| P-8 | major | 文档回写面不全；说明 14 把未落地的 A2 结构写成现状；说明 02/规范 03/依赖注入专项/地图专项未列；`domain-matrix.py` 的允许边文案失真 | 说明 14 撤回计划态描述（A2 收尾再回写）；路线图去掉“施工 A1→A2”进度语；回写清单补 02/03/依赖注入/地图专项；A2 改 `ALLOWED_EDGES` 文案后 `--write` | 已修待复评 |

非阻塞问题（minor）已纳入方案修订：数字口径统一为 30/24（另 6 次为 `IBattleViewGroup` 等端口引用）；“5 文件”改“4 文件 5 类型”；AC1 限定生产代码；AC8 后半落到守卫；AC9 给 filter；A1 允许清单带批次号、A2 收紧；`IBattleClient.Client` 标为过渡逃生口并登记收口批次；`IMatchSessionFactory` 保留理由（与 `IBattleViewGroup` 同型）；地图/诊断 null 检查点放装配点；A1 红证据=守卫文本扫描先红（不依赖被测类型编译）；A2 收尾跑 `toolchain.py check` 并记录“目录搬动六面已核对无命中”；审计登记 `类设计.md:38`/`技术债.md:26` 路径重指或顺手结清；`ClientAsmdefAllowlistTests` 现有正向允许表已能拦 Battle→Modules，不重复新增断言。

## 评审结论（首轮）

- 评审方式: 独立子代理
- 首轮结论: FAIL（首轮）
- 首轮验收标准: 需修订（AC1/AC2/AC3/AC4/AC5/AC6/AC8/AC9/AC10 判定口径）
- 首轮阻塞问题: 8（P-1～P-8）
- 评审人: Qoder Agent（general-purpose，独立上下文）
- 评审时间: 2026-11-01

## 第二轮复评（2026-11-01）

- 评审方式: 独立子代理（Qoder Agent，general-purpose，独立上下文）
- 评审输入: 修订后的 `plan.md`、本文件首轮记录、代码基线 `48ccda61`（工作区现状）；未运行 dotnet/Unity
- 第二轮结论: **FAIL**（P-1～P-6/P-8 已实质解决，P-7 部分；新增 1 个阻塞 N-1）

### P-1～P-8 复评状态

| ID | 状态 | 依据摘要 |
|---|---|---|
| P-1 | 已修 | `IAccountRuntime` 无 Diagnostics/根 Services；地图固定 LiteSim 委托；`LiteNet.Tests` 已含 `LiteFramework.Core`，新增 Compile Include 仅 3 个端口文件 |
| P-2 | 已修（计数修正） | 端口签名已写清；`new BattleContext` 实测 11 处（1 生产 + 9 `BattleSessionEditModeTests` + 1 `BattleRunPortEditModeTests`） |
| P-3 | 已修 | LuaBridge asmdef + 允许表已列入范围/AC |
| P-4 | 已修 | `IBattleClient` 覆盖实际调用面；`LastError` 无消费点 |
| P-5 | 已修 | 测试面逐条；`AccountSessionFactoryTests` 只归 LiteNet.Tests；filter 点名 |
| P-6 | 已修（措辞需收） | 容器收窄为 `ClientScope` + `CreateMatchServices()`；“已从结构上消除根写入”过强，改为“服务容器面收窄 + 资源域约定/守卫” |
| P-7 | **部分修** | A2 已写 `DependsOn`；A1 已让 `SessionComposition` 注入 `IBattleClientFactory` 但未写 `DependsOn += Battle` |
| P-8 | 大部分修 | 说明 14 已保持现状口径；AC10 漏说明 17（`17:543`）与说明 03（`03:95/125`）；路线图 `§4.2:136` “（2026-11-01 归位）”属完成态措辞 |

### 新增阻塞 N-1

地图委托放在 `IAccountSessionFactory.Create` 与“装配点注入/装配点 fail-fast”矛盾；唯一调用方 `ProcedureMatch` 未列入 A1。修法：地图委托改放 `AccountSessionFactory` **构造**（`SessionComposition` 注入并 `?? throw`），`Create` 保持 5 参、`ProcedureMatch` 不动。

### 处理

- N-1 已按上述修法改入方案（构造注入、`Create` 5 参）。
- P-7：A1 范围/关键流程补 `SessionComposition.DependsOn += "Battle"`。
- minor 吸收：AC6 文案改“装配注入端口”；AC4 补 `SessionComposition.cs`；白名单口径统一为“A1 文件+类型白名单带 A2 批次号，A2 空表”；守卫扫描去注释/字符串；构造计数修正；AC10 补说明 17/03；`ALLOWED_EDGES` 文案扩展；`MatchLoopLeak` 注入 `DevBattleDiagnostics` 保留断言强度；`BattleContext.cs:168` 文案改 `IAccountRuntime.Maps`；`IBattleClient.LastError`/`IAccountRuntime.TestRoom` 不上端口；`Client` 逃生口登记。
- 待第三轮独立复评。

## 第三轮复评（2026-11-01）

- 评审方式: 独立子代理（Qoder Agent，general-purpose，独立上下文）
- 评审输入: 修订后的 `plan.md`、本文件前两轮记录、代码基线 `48ccda61`（工作区现状）；静态 Read/Grep/Glob，只读运行 `domain-matrix.py --check`（exit 0）；未运行 dotnet/Unity/L1/EditMode/doc-lint
- 第三轮结论: **FAIL**（P-1～P-8/N-1 已修；新增 2 个阻塞 B-1/B-2）
- 第三轮验收标准: 需修订（AC7 判定口径、AC10 锚点）

| AC | 可判定性 | 依据/问题 |
|---|---|---|
| AC1 | 可 | 生产代码 grep；测试直构已排除 |
| AC2 | 可（受 B-1 影响） | A1 白名单称 2 文件，但 `AccountSession` 仍持 `BattleClient` 具体 |
| AC3 | 可 | asmdef + 允许表；LuaBridge 当前确无 Battle |
| AC4 | 可 | 4 文件 5 类型；Session 余项已列 |
| AC5 | 可 | 端口位置/注册面/ProcedureBoundaryTests |
| AC6 | 可 | 代码区扫描；MatchLoopLeak 已点名 |
| AC7 | 需修订 | 注释与 `BattleContext.cs:168` 错误串仍含 `IMapProvider`，字面 grep 会假红 |
| AC8 | 可（minor） | domain-matrix + 守卫；`Session→Input` 归零后允许边应删 |
| AC9 | 可（缺运行时） | recompile/L1/EditMode/doc-lint；无“进房→开局→离场”冒烟 |
| AC10 | 需修订 | `17:543` 正确；`03:95/125` 错误；`技术债:26` 非路径漂移 |

### 阻塞问题

| ID | 严重度 | 位置 | 问题 | 处理 | 状态 |
|---|---|---|---|---|---|
| B-1 | blocker | plan.md:94,111；`AccountSessionFactory.cs:15-19,45-53`；`BattleContext.cs:115`；`MatchSessionFactory.cs:23,80-93` | A1“全部消费面”未闭合——`AccountSession.Battle`/构造仍为 `BattleClient`，`BattleContext.DebugBattle`、`MatchSession.BattleClient` 仍返回具体类型，`MatchSessionFactory` 仍 `Resolve<BattleClient>/typeof(BattleClient)`；A1 白名单仅 2 文件，AC2 必红；Account 只注册端口时 Match 容器解析会运行期失败 | A1 范围与设计补：`AccountSession.Battle`/构造/服务登记改 `IBattleClient`；`BattleContext` 字段/构造/`DebugBattle` 改接口；`MatchSession.BattleClient`/`BattleSession._battle`/`RoomSessionFace` 改接口；`MatchSessionFactory` 改 `Resolve<IBattleClient>`/`typeof(IBattleClient)`；测试影响补 `MatchLoopLeakEditModeTests:83`、`AccountSessionFactoryTests:76/91`；风险 8 兜底 | 已修待第四轮复评 |
| B-2 | blocker | plan.md:123（AC7）；`BattleContext.cs:20,85,154,168` | AC7“`Battle/**` 无 `IMapProvider`/`BattleScenes`”按字面 grep 会命中注释与错误文案，无法稳定判定 | AC7 明确“去注释/字符串代码区 + asmdef references”机械口径；`:168` 错误文案同批改 `IAccountRuntime.Maps` 并同步注释 | 已修待第四轮复评 |

### 非阻塞问题（third round）

- AC10：`规范 03` 原锚点 `03:95/125` 实际指向工具写盘/§10.2；改为语义锚点 §4「对局流程（Main→Match→Battle）」行（当前 50–51 行）；`技术债.md:26` 是 BattleContext 日志并发债、非路径漂移，改为不动并在技术债表追加 `IBattleClient.Client` 逃生口条目。
- AC9 补 PlayMode `BattleSmokePlayModeTests`（Boot→本地测试房进局→开局/开火→离场→容器计数归零）；真实 Player 冒烟按项目约定需用户显式指令出包，列为交付后联机测试项。
- plan.md:11 目标 1 加“A2 后”时限，避免与 A1 过渡白名单冲突；修订记录补三轮。
- 基线漂移：工作区 `Session/Abstractions` 端口未跟踪、工厂文件有并行改动；施工前固定基线（commit/stash 或记录 `git diff` 快照），A1 首步重跑计数，不吸收并行线。

### 回归面检查

| ID | 状态 | 依据 |
|---|---|---|
| P-1 | 已修 | plan.md:78 无 Diagnostics、LiteSim 委托；`LiteNet.Tests.csproj` 已含 LiteFramework.Core/LiteSim/LiteSync |
| P-2 | 已修 | plan.md:79/108 签名与 11 构造点；A2 非纯搬迁 |
| P-3 | 已修 | plan.md:35/120；LuaBridge 当前 asmdef 无 Battle，A2 登记 |
| P-4 | 已修 | plan.md:76；`BattleClient.cs:35-109` 覆盖调用面；NetworkDebug 142/167 |
| P-5 | 已修（补 B-1） | plan.md:108-109 列四类测试与 filter；补 MatchLoopLeak/AccountSessionFactoryTests |
| P-6 | 已修 | plan.md:24/95/133：只留 `ClientScope` + `CreateMatchServices()` |
| P-7 | 已修 | plan.md:34/84 A1 `SessionComposition += Battle`；plan.md:35/85 A2 `BattleComposition += Identity` |
| P-8 | 部分修→已补 | `17:543` 正确、路线图已改；第三轮修正 03 锚点与审计口径 |
| N-1 | 已修 | plan.md:25/81：地图构造注入、Create 5 参；`ProcedureMatch.cs:35/41` 与 `ProcedureComposition.cs:94` 未改 |

### 未验证/限制

未运行 dotnet 测试、Unity recompile、L1、EditMode、doc-lint、toolchain、move_asset；`SessionBoundaryTests` 尚未存在，未执行；计数基于 HEAD+工作区，untracked 端口与并行改动可能造成基线漂移；本次未修改任何文件。

## 第四轮复评（2026-11-01）

- 评审方式: 独立子代理（Qoder Agent，general-purpose，独立上下文）
- 评审输入: 修订后的 `plan.md`、本文件前三轮记录、代码基线 `48ccda61`（工作区现状）；静态 Read/Grep/Glob；未运行 dotnet/Unity/L1/EditMode/PlayMode/doc-lint/domain-matrix
- 第四轮结论: **FAIL**（B-1/B-2 已修；新增 1 个阻塞 B-3）
- 第四轮验收标准: 需修订（AC9 证据口径）

### B-1/B-2 复核

| ID | 状态 | 依据 |
|---|---|---|
| B-1 | 已修（核心） | A1 消费面已闭：`AccountSession.Battle`/构造/服务登记、`BattleContext.DebugBattle`、`MatchSession.BattleClient`、`BattleSession._battle`、`RoomSessionFace`、`MatchSessionFactory` 的 `Resolve/typeof` 均改 `IBattleClient`；测试残项列 minor |
| B-2 | 已修 | AC7 改为去注释/字符串代码区 + asmdef；`BattleContext.cs:168` 文案清理在案 |

### 新增阻塞

| ID | 严重度 | 位置 | 问题 | 处理 | 状态 |
|---|---|---|---|---|---|
| B-3 | blocker | plan.md:110,127；`BattleSmokePlayModeTests.cs:87-93,153-174` | AC9 声称 PlayMode 冒烟覆盖“容器计数归零”，实际只断言 `LastCreated==null`/VFX/音频计数；TearDown `_container = null` 后断言 `_container == null` 恒真，容器释放证据不成立 | AC9 改为“LastCreated 归零 + VFX/音频归零；根作用域无残留由 `MatchLoopLeakEditModeTests` 的 `rootScope.OwnedCount==0` 覆盖”；PlayMode 定性为装配/流程冒烟，不作红线 13 真机证据；`BattleSmoke.TearDown` 探针短路登记审计（低） | 已修待第五轮复评 |

### 非阻塞问题（fourth round，均已吸收）

- B-1 测试残项：`AccountSessionFactoryTests:76/91/156`、`MatchLoopLeakEditModeTests:83` 与 `FakeMapProvider`/`maps:` 删除、`BattleSession` 诊断构造补参；9 处 `new BattleContext` 因具体类实现接口无需改型。
- A1/A2 边界：诊断注入条已标为 A2，并点名 `GameEntry.RegisterModules`。
- `IAccountRuntime.CreateMatchServices()` 明确返回**未 Seal** 的 Match 子容器，由 `MatchSessionFactory` 注册后 Seal。
- AC10 补说明 03 §5/§6/§7.1；non-goal 与 AC10 审计措辞统一；`类设计.md:38`/`技术债.md:26` 只声明“本批无漂移”。
- AC2 守卫用 `\b` 词界，避免 `IBattleClient`/`IBattleRun` 误命中。
- AC8 明确 `Session→Battle` 仍保留端口引用，只清具体类型计数；归零允许边（`Session→Input`）删除。

### 未验证/限制

未运行 dotnet 测试、Unity recompile、L1、EditMode、PlayMode、doc-lint、domain-matrix、toolchain；`SessionBoundaryTests` 尚不存在；工作区有并行改动与未跟踪 `Session/Abstractions` 端口，计数与行号可能漂移；本次未修改任何文件。

## 第五轮复评（2026-11-01）

- 评审方式: 独立子代理（Qoder Agent，general-purpose，独立上下文）
- 评审输入: 修订后的 `plan.md`、本文件前四轮记录、代码基线 `48ccda61`（工作区现状）；静态 Read/Grep/Glob，只读复算 `Session→Battle=30`（具体 24：`BattleContext`12/`BattleClient`11/`GameplayMatchIdentity`1，落 4 文件）
- 第五轮结论: **FAIL**（B-3 未修净）
- 第五轮验收标准: 需修订（AC9 证据口径）

### 新增阻塞

| ID | 严重度 | 位置 | 问题 | 处理 | 状态 |
|---|---|---|---|---|---|
| B-3 残留 | blocker | plan.md:110（并 127） | 测试策略仍写 PlayMode“离场 → 容器计数归零”，与测试代码矛盾；AC9 新引的 `rootScope.OwnedCount==0` 恒真（root 无直接登记、子作用域不计入父 OwnedCount），不能见证根作用域残留；真实见证是 `MatchLoopLeak:106/107/108` 的 `match.Services/Events.IsDisposed`、`account.Scope.IsDisposed` | `:110` 改为离场断言 `LastCreated==null`、VFX/音频归零（容器探针短路见 AC10 登记）；AC9 改为逐次断言 `account.Scope.IsDisposed`/`match.Services.IsDisposed`/`match.Events.IsDisposed`，`rootScope.OwnedCount==0` 仅作加固 | 已修待第六轮复评 |

### 非阻塞问题（fifth round，均已吸收）

- `11 处 BattleContext 构造` 口径统一为“影响 11 处（1 处生产改型 + 10 处测试无需改型）”。
- `BattleSessionEditModeTests` 不构造 `BattleSession`；诊断补参点改为 `BattleRunPortEditModeTests:88`、`MatchLoopLeakEditModeTests:98`，并标 A1/A2 批次。
- `Scope` 写面“约定 + A2 守卫”过强 → 改为“约定 + 评审；守卫只扫 Session 侧”。
- A2 补代码注释同步点（`ProcedureBattle:57/64/127`、`IBattleRun:26`、`MatchSessionFactory:22`）。
- AC10 补 `17:135` 与 `Docs/施工进度/客户端重构线.md` 第 20 章；登记既有文档漂移（`类设计.md:38` 路径、`技术债.md:26` 行号）为可顺手修正项。

### 未验证/限制

未跑 Unity recompile、L1、EditMode/PlayMode、doc-lint、toolchain、`wf verify --red`；`SessionBoundaryTests` 尚不存在；工作区有并行改动，行号可能漂移；`BattleSmoke.TearDown` 恒真结论为静态核对。

## 第六轮复评（2026-11-01，收口）

- 评审方式: 独立子代理（Qoder Agent，general-purpose，独立上下文）
- 评审输入: `plan.md`（第 6 轮）、本文件五轮记录、代码基线 `48ccda61`（工作区现状）、`artifact-quality.md` 方案评审清单；静态 Read/Grep/Glob，只读复算 `Session→Battle=30`（具体 24：`BattleContext`12/`BattleClient`11/`GameplayMatchIdentity`1，落 4 文件）
- 结论: **PASS**
- 验收标准: **PASS**
- 阻塞问题: **0**

### 逐条 AC 判定

| AC | 可判定性 | 依据/问题 |
|---|---|---|
| AC1 | 可 | 生产代码 grep；当前仅 `AccountSessionFactory.cs:91` 一处，目标迁 `BattleClientFactory`；测试直构已排除 |
| AC2 | 可 | A1 白名单 2 文件（`MatchSessionFactory`/`BattleSession`）带 `A2` 批次号；A2 空表；`\b` 词界口径明确 |
| AC3 | 可 | 端口位置 + Battle asmdef 现无 `LiteGame.Modules`；LuaBridge 现无 Battle，A2 加引用并登记允许表（`ClientAsmdefAllowlistTests:40`） |
| AC4 | 可 | 4 文件 5 类型；Session 剩余文件清单与实际一致（含 `Abstractions/**`） |
| AC5 | 可 | 4 端口迁 `Battle/Abstractions`；Session 不再注册两工厂；`ProcedureBoundaryTests` 现只查 6 个具体件，仍绿 |
| AC6 | 可 | 代码区扫描 + `BattleComposition(Func<IBattleDevDiagnostics>)` + `MatchLoopLeak` 附着/清空断言 |
| AC7 | 可 | 去注释/字符串代码区 + asmdef；`BattleContext.cs:168` 文案改 `IAccountRuntime.Maps` 已列 |
| AC8 | 可 | `domain-matrix --check` + 允许边文案 `--write` + 守卫具体类型计数 0（端口口径明确） |
| AC9 | 可 | recompile/L1/EditMode filter/PlayMode/doc-lint；PlayMode 断言 `LastCreated==null`+VFX/音频归零；根作用域证据改由 `MatchLoopLeak:106-108` 的 `match.Services.IsDisposed`/`match.Events.IsDisposed`/`account.Scope.IsDisposed`，与测试代码一致；`rootScope.OwnedCount==0` 仅加固 |
| AC10 | 可 | 路线图/说明 14·02·17（`17:135`/`17:543`）/03（§5、§6 陈旧路径、§7.1）/规范 03 §4（50–51）/DI/地图专项/进度新章；注释点 `ProcedureBattle:57/64/127`、`IBattleRun:26`、`MatchSessionFactory:22`；审计登记与 `toolchain.py check` 均在案 |

### 非阻塞问题（均为 minor，按“minor 直接收口”规则已吸收）

1. 诊断补参点补 `BattleRunPortEditModeTests:88`（`new BattleSession(match)`）。
2. AC10 改“新增第 20 章并同步章节索引”（原文“第 20 章”易被读成已存在）。
3. 注释同步面补 `IAccountSessionFactory.cs`（“创建 BattleClient”）与 `IRoomSessionFace.cs`（“App 侧适配 BattleClient”）。
4. AC10 审计措辞改“既有漂移，本批顺手修正陈旧路径/行号”（`技术债.md:26` 的 `:174` 实际约 `:190`）。
5. `MatchLoopLeakEditModeTests:17` 测试注释同步“根作用域残留见证”口径。

### 方案可实施性

PASS。`BattleClient` 公开面与 `IBattleClient` 逐项对应，`LastError` 无消费点；`new BattleContext` 实测 11 处（1 生产 + 9 `BattleSessionEditModeTests` + 1 `BattleRunPort`）；`IDevSession` 住 `LiteGame.Core`（Battle 可引），`MatchSessionFactory` 迁 Battle 后所需依赖（Input/Config/LiteFramework/LiteSim/Core）均在 Battle asmdef 允许面；`BattleComposition` 具备 Identity/Input/Config/IReferencePool/DevSession 依赖，注册序 ③④⑦⑧⑪⑭⑯⑰ 自洽；`AccountSessionFactory.Create` 保持 5 参、`ProcedureMatch:101` 不动；LuaBridge 仅需加 Battle 引用并更新允许表；A2 目录搬动与回滚路径明确。

### 验收标准可判定性

PASS。10 条 AC 均映射到 grep/守卫/asmdef/测试过滤器/文档锚点或门禁命令；唯一运行时门（PlayMode）已限定为装配/流程冒烟，并明确不作为红线 13 真机证据；根作用域证据不再依赖 `BattleSmoke` TearDown 的恒真探针。

### 回归面检查

| ID | 状态 | 依据 |
|---|---|---|
| P-1 | 已修 | `IAccountRuntime` 无 Diagnostics/根 Services；地图 LiteSim 委托；`LiteNet.Tests.csproj` 已有 LiteFramework.Core/LiteSim/LiteSync |
| P-2 | 已修 | 端口签名 + 11 构造点口径；A2 明确非纯搬迁 |
| P-3 | 已修 | LuaBridge asmdef/允许表列入范围与 AC3；当前 LuaBridge 确无 Battle |
| P-4 | 已修 | `IBattleClient` 覆盖调用面；`NetworkDebug:142/167` 仅 Disconnect/BeginReconnect；`LastError` 无消费 |
| P-5 | 已修 | 四类测试逐条；filter 点名；`AccountSessionFactoryTests` 归 LiteNet.Tests |
| P-6 | 已修 | `ClientScope` + `CreateMatchServices()`；写面改“约定 + 评审”，守卫只扫 Session |
| P-7 | 已修 | A1 `SessionComposition += Battle`；A2 `BattleComposition += Identity` |
| P-8 | 已修 | 说明 14 现状口径、17:135/543、03:95/108/125、规范 03 §4、DI/地图专项、矩阵文案 |
| N-1 | 已修 | 地图委托置 `AccountSessionFactory` 构造；`Create` 5 参；`ProcedureMatch:101` 未变 |
| B-1 | 已修（核心） | A1 消费面全闭：`AccountSession.Battle`/服务登记、`BattleContext.DebugBattle`、`MatchSession.BattleClient`、`BattleSession._battle`、`RoomSessionFace`、`MatchSessionFactory` 的 `Resolve/typeof` |
| B-2 | 已修 | AC7 改代码区 + asmdef；`BattleContext:168` 文案清理在案 |
| B-3 | 已修 | `plan.md:110` 不再称 PlayMode 容器计数；AC9 与 `MatchLoopLeak:104-109` 一致；`OwnedCount` 仅加固 |

### 未验证/限制

未运行 dotnet 测试、Unity recompile、L1、EditMode/PlayMode、`domain-matrix --check`、`doc-lint`、`toolchain.py check`、`move_asset`；`SessionBoundaryTests` 尚不存在，未执行；行号基于当前工作区，工作区含大量并行改动，施工时可能漂移；本次未修改任何文件。

## 评审结论

- 评审方式: 独立子代理（第六轮收口）
- 结论: PASS
- 验收标准: PASS
- 阻塞问题: 0
- 评审人: Qoder Agent（general-purpose，独立上下文）
- 评审时间: 2026-11-01





