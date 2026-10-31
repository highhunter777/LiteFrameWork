# 验收标准确认：客户端模块化（竖向切分 + 装配就地 + 边界机械化）

> 运行 client-modularization · 创建于 2026-10-30
>
> 本阶段把方案验收标准逐条提审用户；未经用户确认不得进入任务拆解与施工。

## 验收标准

判定方式一栏均可由代码/命令/用例机械核对（不依赖主观判断）。

| 编号 | 验收标准 | 判定方式/验证命令 | 对应任务 |
|---|---|---|---|
| A1（批 1） | `App/Bootstrap/Modules/` 不存在；`Modules/**` 有 13 个 `<域>Composition.cs`（Framework 8 = 七个迁移 + Events，Ui/Config/Presentation/Input/Settings 各 1）；`ContainerModule.cs` 移到 `App/` 且仍 `: IClientModule`；迁移段所在 asmdef 引用集补齐并同步允许表 | 目录列举 + `rg "<域>Composition"` + asmdef/allowlist 断言 | 待任务化 |
| A2（批 1） | `App/Bootstrap` 只剩 `Probes/`、`Fallbacks/` 且内容零改动 | `git diff` 为空 + 目录列举 | 待任务化 |
| A3（批 1） | `LiteFramework` 提供 `FrameworkInfrastructure.Create`/`Clocks.Create`/`Events.Create`；`Modules/**Composition.cs` 不再出现 `new ReferencePoolService(`/`new LogService(`/`new FileSystem(`/`new WorldClock(`/`new EventBus(` | 符号 grep + 编译 | 待任务化 |
| A4（批 1） | 生产代码里三个适配器 `new` 只在 `App/AppComposition.cs`（方法体内）；`Modules/**` 无这三个 `new`、无 `is CinemachineCameraService`/`is NewInputIntentSource`；走"工厂委托注入 + 段内调用"；`InputComposition.DependsOn` 去 Presentation 留 DevSession；瞄准相机 `ComposeAfter` 迟绑定（批 1–3 落 `ContainerModule.Assemble` 的 `Seal()` 前），判据"首采样前 `Camera == null`、`ComposeAfter` 后 `== Camera.main`" | grep + 启动冒烟断言 | 待任务化 |
| A5（批 1） | `EventsComposition` 独立产 `EventBus`；`ClocksComposition` 不再 `new EventBus` | grep + 单测 | 待任务化 |
| A6（批 1/2） | 6 文件 7 处守卫路径更新（`ModuleDependencyDeclTests`/`ModuleScopeAdoptionTests`/`ArchitectureBudgetTests`/`M3WiringGuardTests`×2/`IdentityEntryGuardTests`/`DisciplineScanner`）；`ModuleScopeAdoptionTests` 双根基+键映射（批 2 改 `BattleComposition.cs`、批 4 删 `ContainerModule.cs` 键）；扫描面含过渡期 `App/ContainerModule.cs`、排除 `App/AppComposition.cs` 的 `DependsOn`；身份改由类声明提取 + 解析器自测；批 2 随迁 3 个预算键（`CharacterLocomotionDriver` 类/`Tick`、`BattleDamageNumberDriver.Tick`），拆方法后删 152 行键；批末 L1 全绿（取红单独 `wf verify --red`） | L1 全量 + 定向过滤器 | 待任务化 |
| A7（批 2） | `Core/Battle/` 不存在；`Modules/Battle/Abstractions/` 含 6 个端口；`LiteGame.Core` 只剩 `Dev/`+`View/` 且 asmdef 不变 | 目录/文件断言 | 待任务化 |
| A8（批 2） | `Modules/View`、`Modules/Presentation` 不存在；内容按职责落 `Modules/Battle/{Match,Arena,Characters,Combat,Aim,Hud,Settlement,Setup,Commands}`；两 asmdef 删除；`Modules → Presentation` 边保持删除 | 目录 + asmdef 断言 | 待任务化 |
| A8b（批 2） | `Camera/CameraComposition.cs` 新建（主相机+`ICameraService`，适配器由工厂注入），位于 `Ui`/`Input` 之前；`PresentationComposition` 改名 `BattleComposition` 并保留表现内核 | 文件/顺序断言 | 待任务化 |
| A9（批 2） | `Modules/Battle/` 无 `View`/`Presentation`/`Domain` 目录名；`Modules/**` 不再有这三词作域/职责目录名 | `DomainBoundaryTests` | 待任务化 |
| A10（批 2） | `LiteGame.Modules.Battle` 为战斗域唯一程序集；`ClientAsmdefAllowlistTests` 全绿 | L1 | 待任务化 |
| A10a（批 2） | 合并后引用集 13 条逐条登记；删 View/Presentation 两行并改所有引用者；PlayMode asmdef 加 `LiteGame.Modules.Battle`；新建 `Modules/Battle/AssemblyInfo.cs`（既有 4 条 friend + `LiteGame.UI.PlayModeTests`）并删 Presentation 那份；硬边 case 改为"Battle 仅放行 `LiteGame.Adapters.Unity`" | L1 + 文件断言 | 待任务化 |
| A11（批 2） | 战斗域内单向 `Hud/表现 → 业务 → Abstractions` 无回环 | `DomainBoundaryTests` | 待任务化 |
| A12（批 2） | `BattleSmokePlayModeTests` 入 `commands`；harness=`Boot.unity` 起服 + `Started && Current == Main`（经 `TakeContainer` 解析）+ `TestModeRuntime.RequestEnter/RequestExit`；工厂 `LastCreated` 装饰器（存真 rig，Dispose 置空）；观测面 `IsBuilt`/`Hud`/`View`/`Root`/`Crosshair`/`ShotPulseCount`/`VoiceCount`/`VfxService.ActiveCount`/`BattleDamageNumberDriver.ActiveCount`/越肩几何容差；离场只看容器级（`LastCreated==null`、计数 ≤2s 轮询归零） | PlayMode 用例 | 待任务化 |
| A13（批 3） | `ModuleDependencyDeclTests.Baseline` 空集且通过；`UiComposition` 不再 `Require<FeedbackService>` | L1 | 待任务化 |
| A14（批 3） | 确认框可正常返回；未绑定场景只记一条 Warning 不抛 | Play 冒烟 + 单测 | 待任务化 |
| A15（批 4） | `ContainerModule` 不再是 `IClientModule`；合计 18 个 `<域>Composition.cs`（14 − Container + Camera + Procedure/Session/Meta + Map）；注册序为 18 段全序；`AppComposition` 由 `ComposeRoot` 驱动，窗口内 `Put` 成功、窗口外抛 | 文件计数 + 单测 | 待任务化 |
| A15a（批 4） | Container 注册项按归属表全部落位：`Require` 全可用 + Tick 序回读等于 13 项期望序 + 释放探针关停归零 + `LookPreferences` 单实例（改灵敏度后 Battle 侧同步） | 装配用例 + 单测 | 待任务化 |
| A16（批 4） | `ComposeRoot` 成功/失败回滚/取消三路径用例；`AppComposition` 类 ≤400 行、方法 ≤80 行；`ContainerModule.Assemble` 批 1 拆 4 个 ≤80 方法并删 152 键；`ClientHost` 454/83/86 净增 ≤0（超出须记录理由） | 用例 + 预算守卫 | 待任务化 |
| A17（批 4） | 手测"登录 → 开房 → 进对局 → 退出"全链通过，无新增 Error 日志 | 手测记录 + 日志 | 待任务化 |
| A18（批 5） | `Modules/Session/` 含 `AccountSession(Factory)`/`MatchSession(Factory)`/`RoomSessionFace` + 2 契约 + `SessionComposition`（⑰）；`Modules/Room/`、`Procedure/Session/` 清空 | 目录 + 注册序断言 | 待任务化 |
| A19（批 5） | `MatchSessionFactory` 引用类型集不含 `Modules/Procedure/**` 下的类型（现状两处：`BattleScenes`、`IMapProvider`，随 A20 迁 `Map`） | 类型→文件反查用例 | 待任务化 |
| A20（批 5） | `Modules/Map/` 收编 `BattleScenes`、`MapProvider`；`MapBoundsMarker` 移动后仍被 `SimMapBaker` 读到；`TrainingGroundLayout` 反射名 = 实际 asmdef 名且有 EditMode 用例 | 路径断言 + 用例 | 待任务化 |
| A21（批 5） | `Modules/Ui/` 只含 `UiComposition`/`UiPageResolver`/`LuaUIData`/`Generated/`；`ResultRanking`→`Battle`、`PageFlow`→`Procedure` | 目录断言 | 待任务化 |
| A22（批 5） | `Meta/Abstractions/` 含 `IAccountSessionControl`、`ILoginSurface`；`Meta → Procedure` 经 `IAppFlow` 端口化 | 文件 + 引用断言 | 待任务化 |
| A23（批 5） | 错位件归位：`DeployBake`→`Framework/`（去可变静态）；`ProductSettings`→`Settings`（三处同批：Settings asmdef 已有 `LiteFramework.Runtime`、`LuaBridge.asmdef` 加 `Modules.Settings` + 允许表一行、Settings `AssemblyInfo` 加 friend；`BindGlobals` 留 LuaBridge）；`LTextCatalog`→`Config`；`LuaLayout`→`Framework/`；`TeleportToAimCommand` 与 handler 同域 | 文件 + asmdef 断言 | 待任务化 |
| A24（批 5） | `Procedure` 不再直接引用 `BattleClient`/`BattleContext`/`EndReason`/`TeleportToAimCommand`/`RoomSessionFace`/`MetaEntryService`/`MetaEntryConfig` | 类型引用反查 | 待任务化 |
| A25（批 5） | 8 个共享域各自命名空间；`DomainBoundaryTests` 先红后绿；纪律扫描 0 违规；`Room → BattleClient` 允许边显式登记 | 守卫 + 扫描 | 待任务化 |
| A26（批 5，用户裁决项） | 排序（击杀降序→EntityId 升序）与玩家序号单源，`ResultRanking` 与局内排行榜同实现，一致性用例 | 用例 | 待任务化 |
| A27（批 5，用户裁决项） | 瞄准两事实改名区分，`0.3s` 常量归相机侧；黄金值取生产路径（`ICameraService.SetAiming` 翻转时刻），改名后同一断言仍绿 | 用例 | 待任务化 |
| A28（批 5） | `Docs/说明/18-域间引用矩阵.md`（12 域 × 12）+ `scripts/gate/domain-matrix.py` + `commands` 条目同批落地 | `--check` 门禁 | 待任务化 |
| A29（全批） | `.workflow/config.json` 的 `commands` 全绿（含新增 `battle-smoke-playmode`、`domain-matrix`）；Unity 重编译 0 error | `wf verify` | 待任务化 |
| A30（全批） | `test-plan.md` 写明新增业务域步骤（含"需引 `Adapters.*` 时改硬边 case"与"跨程序集门面三处同步"例外） | 文档检查 | 待任务化 |
| A31（全批） | 文档回写清单逐项完成（02/17/18/03/框架总设计/设置专项/热更专项/技术债/进度卷）；文档门禁全绿 | doc 门禁 | 待任务化 |

## 提审记录

- 提审时间：2026-10-30
- 提审方式：对话中列出批次结构与关键判据 + 结构化提问（先确认"原样 A1–A31"，评审后方案有结构性调整，再次提审增量）
- 用户原话或选择：
  - 首轮（评审前）：方案评审＝"开子代理评审（推荐）"；验收标准＝"原样确认全部 A1–A31"；开工基线＝"在这批 WIP 上继续（推荐）"。
  - 增量提审（评审 13 轮后方案结构变化）：见"用户确认"栏。

## 用户确认

- 确认结论: 通过
- 确认人: 用户（对话确认，2026-10-30）
- 确认时间: 2026-10-30
- 变更要求：无（用户选择"确认修订后的全部验收"；提交节奏＝每批收口提交一次，逐文件 `git add`、禁 `-A`）

## 变更记录

- 2026-10-30：方案经 13 轮独立子代理评审，结构发生如下实质变化（相对用户首轮"原样确认"的版本）：
  1. 段数由 16/17 收敛为 **18**（新增 `Camera` 薄段 ②、`Map` 成段 ⑫；`Container` 拆 Procedure/Session/Meta）。
  2. 新增 A8b（Camera 薄段）、A10a（合并后引用集 + friend + 硬边 case）、A15a（注册归属 + Tick 13 项 + `LookPreferences` 单实例）。
  3. A4 改"工厂委托注入 + 段内调用"（避免 `Modules.*→App` 环并保住 `matchRules` 读口），判据改"首采样前 `Camera == null`"。
  4. A6 明确 6 文件 7 处 + 3 个预算键 + 身份改类声明提取 + 取红/取绿口径。
  5. A12 明确 harness / 存取口 / 观测面 / 离场口径 / 几何容差。
  6. A19/A23/A28 判据按实测重写（类型反查、三处同批、12 域矩阵接门禁）。
