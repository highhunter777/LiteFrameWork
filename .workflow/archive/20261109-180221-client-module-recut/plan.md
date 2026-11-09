# 方案：Settings 页面端口 + 守卫棘轮（v6，最小可交付切片）

> 运行 client-module-recut · v6 · 2026-11-09（Asia/Hong_Kong）
> 五轮独立评审（v1-v5 FAIL）证明：在当前代码状态下，能一次落地且可验收的只有 **Settings 页面端口 + friend/零使用边守卫/棘轮**；Battle 内部收口（窄口落点/27 基线/HitFeedback 写面）和 Account/Lobby（Meta 生命周期/读口/DependsOn/HCLR 顺序）都需要各自的设计裁决。v6 据此只做最小可交付切片；另两条见 §7。
> 评审记录见 `reviews/plan-review.md`。基线：`78cf7578e9947769c4be88debca3a376d57e40ac`（main）；工作区以开工时 `git status` 快照为准（当前实测 15 改 + 5 未跟踪）。

## 背景与目标

- 现状：客户端 12 域/33 单元/6 层/28 条登记边；`arch-check`/`domain-matrix` 绿；L1 1972 全绿；BuildHash **shared 绿、meta 红**（HEAD 基线 stale，非工作区 WIP 造成）。
- 可独立修且不依赖未决设计的三个洞：
  1. `ProductSettings.Current` 是产品运行期最后一个静态门面；设置页 10 处直取，页面与能力之间没有可替身端口。
  2. friend 边 55 条、9 份 AssemblyInfo 复制、无允许集守卫。
  3. 零使用 asmdef 边存在，允许表只增不减；跨模块白名单 15 条。
- 目标（可观察）：
  1. 设置页不再引用 `ProductSettings` 静态门面；UI asmdef 不再引 Settings；页面可注入 `ISettingsPagePort` 替身；设置页 PlayMode 用例绿。
  2. friend 允许集单一来源，未登记即红；零使用边有基线/命令，只减不增；跨模块白名单 15→14（删 `ProductSettings`）。
  3. L1/arch/domain/doc 绿；`build-hash --check-shared` 绿；meta 红记录为已知基线债（不在本批重烘）。
  4. **不新增模块、不改域数、不拆 asmdef、不动 Battle 内容/收集组/HCLR 分区。**

## 需求澄清与假设

- 需要确认的问题：
  1. 接受“v6 只做 Settings 端口 + 守卫/棘轮，Battle/Account/Lobby 另立设计”吗？五轮评审证明后两者需要先裁决窄口落点/27 基线/写面/Meta 生命周期/读口/DependsOn/HCLR 顺序。
  2. 接受“BuildHash meta 红记录为已知基线债，本批只守 shared”吗？重烘 Meta 会写 `Server/MetaServer/Host/BuildHash.g.cs`，超出本批非目标。
  3. 接受“friend 允许集进 `arch/units.json` 的 `friends` 字段 + L1 用例”吗？（备选：`arch/friends.json`。）
- 已确认答复：用户明确“有很多时间资本去重构，可以重切”；本批只做方案与独立评审，不写生产代码。
- 采用假设：
  - A1：v6 先交付可验证收益；Battle/Account/Lobby 另开设计。
  - A2：1 名全职工程师；**Phase 0 → Phase 1 顺序执行**（两阶段都改 `arch/units.json`/`Tests/LiteFramework.Core.Tests`，Phase 1 的 Ui→Settings 删除会扰动零使用边基线，不并行）。
  - A3：不改协议/Sim/Net/Sync/表源/服务端；不动 prefab/收集组；不做 HCLR 分区。
- 假设影响与风险：A1 若被否决，先补 §7 设计再改方案；A2 若并行，Phase 0 守卫与 Phase 1 页面端口互不依赖。

## 范围与非目标

- 范围内：
  1. **Phase 0 基线与守卫**：
     - 以开工时 `git status` 快照为准，生产树（`Assets/Packages/Tests/Server`）干净；`.workflow` 方案目录先提交/排除。
     - friend 允许集：作用域写死 **9 个 `Modules/*/AssemblyInfo.cs`**（Battle/Composition/Config/Dev/Map/Meta/Procedure/Session/UI），当前 **55** 条；`AssemblyInfo 归属 = 最近 asmdef 目录`，不递归进子单元（App/Editor/LiteNet 不在本批；全单元口径 63 留后续）。`arch/units.json` 每 unit 增 `friends: []`（备选 `arch/friends.json`），L1 用例读 AssemblyInfo 比对；未登记即红。
     - 零使用边报告：新增 `scripts/gate/zero-use-edges.py`，**复用 `domain-matrix.py` 口径**（去注释/字符串、depth=1 顶层类型、按出现次数）；同名类型任一 surviving 单元声明都算命中（过近似，减少假零）；外部程序集不在 unit→unit 范围；`--check` = 当前集合是 `arch/zero-use-edges.json` 基线的子集；登记 `quality.zero-use-edges` 到 `scripts/gate/toolchain.json`，挂 `.workflow/config.json`（不挂 L1，扫描面太宽）；先 report-only 一轮，再 hard fail。
     - 记录 L1/arch/domain/doc/`build-hash --check-shared` 基线；meta 红登记到 `Docs/审计/客户端/登记/技术债.md` + 施工进度卷（可 grep `BuildHash meta stale`）。
  2. **Phase 1 Settings 页面端口**：
     - `ISettingsPagePort` 放 `Modules/UI/Abstractions`，按页面实际使用的 **28 个原语成员**列全：15 读（`LastTab`、`FrameRateOptionCount`、`FrameRateOption`、`DisplayMode`、`ResolutionCount`、`ResolutionLabel`、`ResolutionIndex`、`LookSensitivity`、`AdsSensitivity`、`Volume`、`SoundEnabled`、`Quality`、`FrameRate`、`VSync`、`InvertLookY`）+ 13 写/动作（`SetLastTab`、`SetVolume`、`SetSoundEnabled`、`SetQuality`、`SetDisplayMode`、`SetResolutionIndex`、`SetFrameRate`、`SetVSync`、`SetLookSensitivity`、`SetAdsSensitivity`、`SetInvertLookY`、`ResetDefaults`、`Save`）。没有 `Apply`（setter 即生效）。
     - `ProductSettingsAdapter` 放 `App/PageServices`；**创建点在 `BindProductSettings` 之后**（或 `Func` 内 `context.Require<ProductSettings>()`）；`PageContext` 构造加 `Func<ISettingsPagePort>`；不要在 `BindPageServices` 里当场 Require `ProductSettings`（时序会抛）。
     - `IPageContext` 增 `ISettingsPagePort Settings { get; }`；`PageLogicBase` 增 `protected ISettingsPagePort Settings => Ctx?.Settings;`。
     - `Page_SettingsLogic`：10 处 `ProductSettings.Current` 改 `Ctx.Settings`；`FrameRateIndex()` 改 `private int FrameRateIndex(ISettingsPagePort s)`；空端口时 **OnShow 早退** + 10 处全部 `?.`（点名 `SetLastTab`/`ResetDefaults`/`Save`/`OnPageHide` 的 `Save`），不抛。
     - UI asmdef 去 `LiteGame.Modules.Settings`；`arch/units.json` UI refs 去 Settings；`domain-matrix --write` 去 Ui→Settings 边；`CrossModuleContractLocationTests.Allowed` **本 Phase 删** `ProductSettings` 行（否则 L1 红）；`ProductSettings.Bind/Current` 无消费者后删除，`AppComposition.BindProductSettings` 只 `Put` 实例。
     - 新增 `Assets/Tests/UI/PlayMode/UI/SettingsPagePlayModeTests.cs`：按 `CSharpPagePlayModeTests` 的 UIService 模式；注入 fake `ILocalizationService`（避免 `SetTextKey` 未 BindLocale 抛）；加载真 `Page_Settings.prefab`；驱动 Dropdown/Slider 事件；覆盖打开/改画质/改音量/Back 保存/空端口禁用（控件 interactable/visible + 不抛）。
  3. **Phase 2 棘轮**：friend 55 基线；零使用边基线只减不增；作为独立批次。跨模块白名单 15→14 已在 Phase 1 完成（删 `ProductSettings`），Phase 2 不重复。
- 非目标：
  - **不做 Battle 内部收口**（窄口落点、27 基线、HitFeedback 写面、SeamExceptions 另立设计，见 §7.2）。
  - **不做 Account/Lobby 重切**（Meta 生命周期/读口/DependsOn/HCLR 顺序另立设计，见 §7.1）。
  - 不拆 Battle asmdef；不搬 `Modules/Battle/Content`；不改收集组；不做 HCLR 分区。
  - 不动 `Packages/com.litegame.{sim,net,sync}`、`Assets/GameData/Generated`、`Modules/Config/Table/{TableLayout,SimConfigMapper,BattleStrategyTableReader}.cs`（BuildHash 红线）；`MapTableProjection.cs` 已知漂移也不动。
  - 不动框架四件切分；不新增 public static 可变状态/第二套 UI 队列；不改协议/Sim/服务端/表源/页面布局/UI 契约。
  - **不重烘 BuildHash meta**（会写 `Server/MetaServer/Host/BuildHash.g.cs`，超出本批）；meta 红登记为已知债。

## 现状与约束

- 相关代码/模块（实测）：
  - `Page_SettingsLogic` 10 处 `ProductSettings.Current`（:56,137,171,186,206,216,232,263,295,301）；页面需要 28 个原语成员（见 §1 Phase 1）；`FrameRateIndex()` static（:230-237），唯一调用点 `RefreshControls` :222。
  - `IPageContext` 在 `Modules/UI/Abstractions`；`PageContext` 唯一实现在 UI，唯一构造点 `AppComposition.cs:114`；`BindPageServices`（:90）早于 `BindProductSettings`（:94）；`SegmentContext.Require/Get` seal 后仍可读。
  - `UIService` 有 `pageContextProvider`/`localeProvider`（:77-89）；`Page_Settings.prefab` 带 `Page_SettingsLogic`；`UIBindLocale` 未 Bind 时 `SetTextKey` 抛（:39-43）。
  - UI 除 `Page_SettingsLogic` 外无 Settings 类型引用；UI asmdef 现引 `LiteGame.Modules.Settings`；`arch/units.json` UI refs + Ui→Settings 域边；`CrossModuleContractLocationTests.Allowed` 有 `ProductSettings` 行。
  - friend：9 个 `AssemblyInfo.cs` 共 55 条；6 个域同款 7 条；无守卫。
  - 零使用边：按目标程序集顶层类型名扫代码区可做；口径需定（同名类型/外部程序集/是否 gate/基线存放）。
  - BuildHash：`--check-shared` 绿（5937ac51…，120 文件）；meta baked 229b5d86… vs recomputed b4ec1d69…，HEAD 基线 stale。
  - L1 1972 全绿；arch 33 units/6 layers/12 domains/28 edges；domain-matrix 12 域；doc-lint 0 violation；hot-boundary 23/66。
- 约束：
  - `Assets/` 下 .cs/.meta 搬动走 unity-pipeline；本方案不搬页/不搬 Battle 内容。
  - `arch/units.json` 单源；Phase 1 移除 Ui→Settings；`arch-check --write/--check` 复核 `ScanTargets.Generated.cs`/`module-tiers.json`；`domain-matrix --write/--check` 复核说明 18（本批不加域，脚本“12 域”不变）。
  - friend 允许集若进 `units.json`，需确认 `arch-check` 忽略未知字段/或加校验；备选独立 `arch/friends.json`。
  - 零使用边脚本/基线放 `scripts/gate/` 与 `arch/`；先 report-only，再 hard fail；基线只减不增。
  - HCLR：本方案不新增 hot 模块；`HotAssemblies`/asset 保持 23 条；`hot-boundary-check.py` 作为门禁（加到 L1 或 `.workflow/config.json`，Phase 0 定）。
  - 基线：当前 15 改 + 5 未跟踪；AC0 的“干净”指 `Assets/Packages/Tests/Server`（`.workflow` 先提交/排除）。

## 候选方案与取舍

| 方案 | 核心思路 | 优点 | 代价/风险 | 结论 |
|---|---|---|---|---|
| A 只加守卫 | friend/零使用边，不动 Settings | 1-2 周；最低风险 | 静态门面仍在 | Phase 0 子集 |
| B Settings 端口 + 守卫/棘轮（推荐） | 本方案 | 可独立验证；不动模块/内容/HCLR；Phase 0 → 1 顺序 | 1-1.5 个月/一人；不解决 Battle/Account/Lobby | **推荐** |
| C 加 Battle 内部收口 | 在 B 上加窄口/子包图 | Battle 内部解耦 | 五轮评审 N26/N27/N29-N31 未决；27 基线不可复现 | 另立设计 |
| D 加 Account/Lobby | 在 B 上加功能切片 | 功能局部性最好 | N13-N19 未决；边设计边施工会红 | 另立设计 |

推荐方案与理由：**B**。五轮评审证明 C/D 需要先做设计裁决；B 是当前唯一可一次落地且可验收的切片，先拿“去静态门面 + 守卫/棘轮”的收益。

## 设计要点

### 1. Settings 页面端口（Phase 1）

- 端口面：28 个原语成员（15 读 + 13 写/动作），不暴露 `ProductSettings` 类型；无 `Apply`。
- 适配器：`ProductSettingsAdapter` 在 App，包 `ProductSettings`；创建点在 `BindProductSettings` 之后；`PageContext` 用 `Func<ISettingsPagePort>` 惰性取，不调序。
- 页面：10 处替换；`FrameRateIndex(ISettingsPagePort)`；空端口禁用/收起；`Ctx.Meta` 仍用于 Version/AccountId/ServerUrl/LeaveMatch。
- 测试：`SettingsPagePlayModeTests`（fake `ILocalizationService`、真 prefab、Dropdown/Slider 事件、空端口断言）。
- 模型/文档：UI asmdef 去 Settings；units.json UI refs 去 Settings；domain-matrix 去 Ui→Settings；allowlist 删 `ProductSettings`；`ProductSettings.Bind/Current` 删除；说明 03/08/14/17/18/19、UI API、设置专项回写。

### 2. friend 允许集（Phase 0）

- 单一来源：`arch/units.json` 每 unit `friends: []`（备选 `arch/friends.json`）。
- L1 用例：读每个 `AssemblyInfo.cs` 的 `InternalsVisibleTo`，与模型比对；未登记即红；新增 friend 必须改模型。
- 当前 55 条建基线；先记录，再 hard fail。

### 3. 零使用边报告（Phase 0）

- 脚本 `scripts/gate/zero-use-edges.py`：源 unit 代码区扫目标 unit 顶层类型名；排除测试/生成物；输出 `arch/zero-use-edges.json`。
- `--check`：当前集合必须是基线的子集（只减不增）；新增即红；先 report-only 一轮，再接 L1/工作流。
- 口径写进脚本注释与基线文件；同名类型/外部程序集/泛型约束的处理写清。

### 4. 棘轮（Phase 2）

- 跨模块白名单 15→14（删 `ProductSettings`）；friend 55；零使用边基线；只减不增。
- 行数不作为 AC；趋势只记进度文档。

### 5. 关键流程与失败边界

- Settings：页面打开 → `Ctx.Settings`（adapter）→ 读写/Save；空端口 → 控件禁用/收起，不抛；UIPreview 不跑页面逻辑。
- 失败边界：adapter 创建早于 `ProductSettings` `Put` 会抛 → 用惰性 Func/创建点后移；页面 prefab 引用丢失 → unity-pipeline 回滚（本方案不搬页，风险低）。

### 6. 同批同步面（每 Phase 必查）

- `arch/units.json`：Phase 1 UI refs 去 Settings；Phase 0 friends 字段；`arch-check --write/--check` 复核 `ScanTargets.Generated.cs`/`module-tiers.json`。
- `domain-matrix.py --write/--check`：Phase 1 去 Ui→Settings；本批不加域，脚本“12 域”不变。
- asmdef/测试：UI asmdef 去 Settings；`ClientAsmdefAllowlistTests`、`CrossModuleContractLocationTests`、`DomainBoundaryTests`、`SegmentDependencyDeclTests`、`SegmentBudgetTests`；新增 `SettingsPagePlayModeTests`、friend 守卫、零使用边脚本/基线。
- HCLR：`hot-boundary-check.py` 加到 L1 或 `.workflow/config.json`；`HotAssemblies`/asset 保持 23。
- 文档：说明 03/08/10/14/15/17/18/19/20；UI 系统总设计/UI 编辑器工具链专项；UI 逻辑 API 参考；DI 专项；设置系统专项；Lua 退出专项；路线图；技术债；施工进度卷；审计登记路径核销。
- 工具注册：新脚本/门禁登记 `scripts/gate/toolchain.json`，跑 `python scripts/toolchain.py docs` 重生成；`toolchain.py docs --check` 绿。
- 门禁：`test.py --suite L1 --profile PullRequest`、`arch-check.py --check`、`domain-matrix.py --check`、`doc-lint.py`、`toolbox.py build-hash --check-shared`、`hot-boundary-check.py`、`unity-tests.py` 定向；**不吸收 `toolchain-tests`**（`scripts/tests/test_arch_check.py:23` 存量 34 vs 33 红，与本批无关）。

## 通用性与扩展性

- 根因：页面与能力之间缺少可替身端口；friend/零使用边没有允许集/基线。
- 复用与扩展点：页面端口按功能自持；friend 模型进 `units.json`；零使用边脚本可复用到其他模块。
- 避免硬编码/特判：不写“如果是 Settings 就……”；端口面按页面实际调用列；基线文件/命令固定。

## 测试策略

- 结构门禁：arch/domain/asmdef/跨模块/接口自持/段依赖/段预算/hot-boundary/friend/零使用边。
- L1：`test.py --suite L1 --profile PullRequest`。
- Unity 定向：`SettingsPagePlayModeTests`（新增）；现有 UI 套件回归。
- BuildHash：`toolbox.py build-hash --check-shared`（shared 红线）；meta 红记录为已知债。
- 先失败后通过：Settings 先加“UI asmdef 不得引用 Settings”“页面不得 `ProductSettings.Current`”断言（当前红）；friend/零使用边先对合成负例红；再改生产树转绿。

## 验收标准

- [ ] AC0（Phase 0 基线）：开工时记录 `git status --short -- Assets Packages Tests Server` 快照（当前 8 M + 5 ?? 并行 WIP）；**相对快照不得新增改动**（WIP 提交后为空）；`.workflow` 方案目录已提交/排除；L1/arch/domain/doc/`build-hash --check-shared` 基线记录；meta 红登记到 `Docs/审计/客户端/登记/技术债.md` + 施工进度卷（可 grep `BuildHash meta stale`）。
- [ ] AC1（Phase 0 守卫）：friend 允许集（作用域 9 个 `Modules/*`、55 基线、units.json friends + L1 用例）与零使用边脚本/基线（`arch/zero-use-edges.json`，复用 domain-matrix 口径，`quality.zero-use-edges` 登记 toolchain.json，只减不增）落地；对合成负例变红；生产基线记录。
- [ ] AC2（Phase 1 Settings）：UI asmdef 不再引 Settings；页面代码 `ProductSettings.Current` = 0；`IPageContext`/`PageLogicBase` 有 `Settings`；`ISettingsPagePort` 28 成员；adapter 创建点在 `BindProductSettings` 之后/惰性；`SettingsPagePlayModeTests`（fake localization、真 prefab、空端口 10 处 `?.` + OnShow 早退）绿；allowlist 删 `ProductSettings`；units/domain-matrix 去 Ui→Settings；L1/arch/domain/doc 绿。
- [ ] AC3（Phase 2 棘轮）：friend 55；零使用边基线只减不增（记录数字；不承诺一步到 0/≤5）；白名单 15→14 已在 Phase 1，Phase 2 不重复。
- [ ] AC4（全程）：L1/arch/domain/doc/`build-hash --check-shared`/hot-boundary 全绿；BuildHash shared 常量不变；meta 红为已知债；不跑 `toolchain-tests`（存量 `test_arch_check` 34 vs 33 红与本批无关）。
- [ ] AC5（全程）：说明 03/08/10/14/15/17/18/19/20、UI 系统总设计/UI 编辑器工具链专项/UI 逻辑 API/DI/设置/技术债/施工进度卷做**事实性回写**；路线图/Lua 退出专项等章程类只记录“不变/待另立设计”，不预占结论；审计登记路径核销；无死链/孤儿。

## 风险与回滚

- 风险：
  1. Settings 端口 28 成员/10 处替换 + static `FrameRateIndex`：先改实例方法/参数，再逐处替换；编译失败即回退。
  2. 页面端口空值/测试 harness：fake localization/真 prefab/事件驱动；空端口断言写具体。
  3. friend 模型进 `units.json` 可能触发 `arch-check` schema 未知字段；备选独立 `arch/friends.json`。
  4. 零使用边口径（同名类型/外部程序集）导致误报；先 report-only，基线只减不增。
  5. BuildHash meta 红：本批不重烘；AC4 用 `--check-shared`；meta 另案。
- 监控：friend/零使用边/allowlist 数字；Settings 页 PlayMode；L1/arch/domain/doc。
- 回滚：每 Phase 独立分支/提交；Settings 失败回退 UI/Settings/App 改动；守卫失败回退脚本/基线；共享源集误动即停并重算 BuildHash。

## 7. 另立设计（本方案不做）

### 7.1 Account/Lobby 重切

五轮评审的 N13-N19 是设计裁决，必须先定：
1. 无 Meta 路径：`HasMeta=false` 时 `IMetaClient` 为 null；`Get<IMetaClient>` 可空 + `AccountDomain` 容忍 null，或 `NotConfiguredMetaClient`；补 no-meta 用例。
2. `MetaLoginService`/`IMetaConfigFacts` 归属：保留 Meta，或拆 `MetaDeployFacts` + `MetaLoginService` 迁 Account；同步 App/Procedure Require 面。
2b. `IAccountDomain`/`IMetaAccountSession` 注册归属：现在 `MetaSegment` 注册 `AccountDomain`/`MetaAccountSession`；若迁 Account，`AccountSegment` 接管注册与关闭，`MetaSegment` 只留 client/wire。
2c. `AccountTicketSource`/`IRoomTicketSource` 归属：现在 `MetaSegment` 注册、`AccountTicketSource` 实现、`Procedure` `Require<IRoomTicketSource>`；若归 Lobby，`LobbySegment` 接管注册，Procedure 改依赖 Lobby。
3. Lobby 读口：`RoomBridge` 同时实现 Session 50 的 `IRoomSessionBridge`；保留 `RoomBridge` 在 App，Lobby 只消费 `IRoomView`；`ILobbyPagePort` 若需，先定方法/消费者/数据源。
3b. `IMetaSession`/`MetaSessionFacade` 归属：现为 UI 页面端口（混合 account+room），App 实现；Account/Lobby 重切必经项，先定拆不拆/拆成哪两个端口。
4. Session/Procedure 边：Session `Get<IAccountDomain>`、Procedure `Require<IAccountDomain>`/`Require<IRoomTicketSource>`；补 DependsOn/域边/测试基线。
5. 测试/装配同步：`M3WiringGuardTests` 硬编码路径、MetaEntry 只装 MetaSegment 的断言、RoomFlowRig/RoomFlowTicketTests、GMTestModePage/Editor、App/Session/Procedure asmdef/units。
6. HCLR 顺序：Phase 2 23→24，Phase 3 24→25；每阶段 MarkHotAssemblies 写 asset；C#==asset 守卫。
7. AC 断言：改为可观察变化（Meta 不含 AccountDomain、UI 不含 Page_LobbyLogic 等），Phase 0 记录 grep 基线。

### 7.2 Battle 内部收口

五轮评审的 N26/N27/N29-N31 是设计裁决：
1. 窄口落点：`IBattleRunContext` 35；`IBattleViewHost` 40 或 35 加 `ICombatFeed` 读口并改 `BattleHudModel`；`IBattleDevContext` 40 或 35 加 Hosting 引用 + `IBattleDebugControl` 写面；列出消费者/成员面。
2. tier map/计数：Arena 是 leaf 还是 shared、Setup 是 assembler；计数单位（文件→子包对 vs 出现次数）；基线文件；三档模型下基线不是 27。
3. HitFeedback：`HitFeedbackDispatcher` 用 SimView/UnityEngine，不能上 35；35 定义 `IHitFeedbackSink` 供 Hud 消费，实现留 Combat；或登记 root→Arena 例外并更新说明 14。
4. SeamExceptions：`IBattleViewAttachment` 例外 stale 可删；`IBattleDevDiagnostics` 迁 35 则删 allowlist 行，保留则改理由。
5. BuildHash meta：HEAD stale；重烘 `Server/MetaServer/Host/BuildHash.g.cs` 另案，或记录为已知债。

> 建议：v6 先落 Phase 0/1/2；Account/Lobby 与 Battle 内部各开一份设计，评审通过后再施工。
