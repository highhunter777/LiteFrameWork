# Lua退出线（Lua 从客户端整体退出）

> **定位**：Lua 退出迁移（[Lua 退出专项设计](../设计文档/架构/Lua退出专项设计.md)）的施工线：P0 冻结与死重清理 → P1 七页 C# 化 → P2 运行时与框架摘除 → P3 内容/构建/程序集/门禁 → P4 表结构与 `PageIds` 收口 → P5 文档面收口。本卷按批成章。
> **章法**：一卷一条线；每章＝一批记录（标题、正文、证据），章首保留 `原文件` 行供旧引用查证（本卷为首建卷，均无旧文件）。
> **状态图例**（全目录统一）：`已完成（附验证证据）` / `进行中` / `未开始` / `阻塞（附原因）`。

## 章节索引

| 章 | 标题 | 原文件 |
|---:|---|---|
| 1 | P0 冻结与死重清理 | — |
| 2 | P1-0 页面寻址收口（PageIds） | — |
| 3 | P1-1 编辑器面：契约对账改读页面逻辑源 | — |
| 4 | P1-2 七页 C# 化与逐页集成 | — |
| 5 | P2a 页面 Lua 面收口（进行中） | — |
| 6 | P4 数据面收口（已完成） | — |
| 7 | P3 内容、构建、程序集与门禁（已完成） | — |
| 8 | P5 文档面收口（已完成） | — |
| 9 | P5 收尾：manifest 历史名与代码 Lua 注释清账（已完成） | — |
| 10 | P5 收尾：spec `lua` 键改名 `api`（已完成） | — |
| 11 | P5 收尾：删除双驱动（Bound 数据绑定面）（已完成） | — |
| 12 | P5 收尾：`OnButtonClick` 改名与 HUD 模块内抽象（已完成） | — |

---

## 第1章 P0 冻结与死重清理

<a id="ch1-P0冻结与死重清理"></a>

> **批次**：P0（2026-11-08）　**状态**：已完成（附验证证据）
> **依据**：[Lua 退出专项设计](../设计文档/架构/Lua退出专项设计.md) §1 D1–D6、§6 P0。
> **口径**：本批零行为变化——只删"已确认零消费者"的件，并把"不许再往里加"落成棘轮。

### 1.1 改动面

| # | 项 | 落点 | 说明 |
|---:|---|---|---|
| 1 | 删除零 require 的死脚本 | `Assets/LiteGame/Lua/LuaPanda.lua`、`Assets/LiteGame/Lua/Cfg/`（15 份 Luban Lua 表） | LuaPanda 147 KB 零 require 零 C# 引用；`Cfg/*.lua` 约 29 KB 全仓零 `require("Cfg…")`；两者原先都在 `lua` 收集组内随包发布 |
| 2 | 删除零消费者的两支注册表 | `Adapters/LuaBridge/{IContentLuaRegistry,ContentLuaRegistry,IStrategyLuaRegistry,StrategyLuaRegistry}.cs` | 只有 `RegistryFiller` 在填、无任何读取方；`Bridge.content`/`Bridge.battle` 在 Lua 侧零调用 |
| 3 | 删除 battle 只读门面的类型 | `Assets/LiteGame/Core/Lua/BattleLuaFacts.cs`、`BattleStrategyProfile.ToLuaFacts()` | 门面随之退出，投影方法失去唯一消费者 |
| 4 | 收窄 `Bridge` 到 data/ui/text 三件 | `Adapters/LuaBridge/Bridge.Data.cs` | 删 `content`/`battle` 装订、删 `GetProcessor`/`GetContentEntry`、删 Dead 的 `data.GetItem` 与 `s_itemLua` 行缓存（道具表 Lua 侧零调用）；`BindRegistries(ui)` 单参 |
| 5 | 填充器与回填服务收窄 | `RegistryFiller`、`LuaRegistryRefillService`、`AppComposition`、`ProcedureSegment`、`Editor/LuaDevReload.cs` | 三注册表→单 UI 注册表；`RegistryFiller` 不再吃 `IConfigService`（表行来源只剩页面目录）；`RegistryRoots` 收缩为 `{ "UI" }` |
| 6 | 生成链摘掉 Lua 表档 | `scripts/codegen/gen_luban.py`、`scripts/gate/toolchain.json` | 删 `lua` 发布目标与那一次 `Luban -d lua` 调用；工具清单 `codegen.luban` 的 outputs/summary 同步（`LuaKeys` 保留，P4 才动） |
| 7 | 迁移期棘轮 | 新增 `Tests/LiteFramework.Core.Tests/LuaFreezeGuardTests.cs`（3 用例，进 L1） | ① 不得新增 `.lua`（只减不增）② 适配层外不得新增 XLua 类型引用（只减不增）③ 匹配器自检（注释/字符串/近名不误报） |

### 1.2 明确不动的（留给后续批）

| 面 | 归属 | 理由 |
|---|---|---|
| `Bridge.ui`/`Bridge.meta`/`Bridge.settings`/`Bridge.room`、`LuaBehaviourAdapter`、`UIApiShim`、dispatch、`LuaKeys` | P1 → P2 | 七个产品页仍在跑 Lua，门面与适配器必须活到最后一页迁完 |
| 内容/构建面（`lua/` 段、候选沙箱、健康探针、YooAsset `Lua` 收集组、`xlua` 原生库、`GenConfig`） | P3 | 删段要与发布链同批，单独删会让候选/健康检查悬空 |
| 三张表的 Lua 列（`uiform.lua_path` 等） | P4 | 改列＝改 `Assets/GameData/Generated` 读码面＝两端 BuildHash 同步事件，必须独立成批 |

### 1.3 验证证据

| 项 | 命令 | 结果 |
|---|---|---|
| Unity 重编译 | `unity command recompile` → `recompile_status` | `{"status":"completed","failed":false,"errors":[],"compilationFailed":false}` |
| L1 基线 | `python scripts/gate/test.py --suite L1 --profile PullRequest` | **PASS，1988 用例**（基线 1985 ＋ 新增棘轮 3） |
| 表链新鲜度 | `python scripts/codegen/gen_luban.py --check` | **CHECK OK**（code/bin/lua-keys 三档与重生成一致） |
| 棘轮红绿演练 | 临时植入 `Assets/LiteGame/Lua/Tests/__lua_freeze_probe.lua` → 跑 `LuaFreezeGuardTests` | **红**：`不得新增 .lua 脚本（只减不增）：Tests/__lua_freeze_probe.lua`；删除探针后 **3/3 绿** |
| 资产删除方式 | `unity command delete_asset --asset … --confirm true`（红线 4） | 7 处删除全部成功；`Assets/LiteGame/Lua`、`Adapters/LuaBridge`、`Core/Lua` 三处 **0 孤儿 meta** |
| 退出条件（§6 P0） | `ls Assets/LiteGame/Lua` | 只剩 `main.lua` / `Core/class.lua` / `Tests/UITestPage.lua` / `UI/Page_*.lua`（7） |

### 1.4 遗留

- **`UIPageScaffold.LuaDir` 未收口到单源**：`LiteGame.Editor.UITools` 程序集未引用 `LiteGame.Shared`，加引用会改动程序集依赖图（触及 asmdef 硬边守卫），与本批"零行为变化"目标不符；改为在常量上方注释标明单源根与删除时点（P2 随引用扫描器一并消失）。
- **`GenConfig.cs` 与 `MetaBridge.cs` 仍是适配层外的 XLua 例外**：棘轮已把它们登记为存量例外，P2 迁移时必须同步删除，否则棘轮会红——这正是它的作用。
- **`LuaRegistryRefillService` 的 `_config` 字段**在收窄时先漏删、由 Unity 编译暴露（`CS0246`）后补删；记录在此，提醒后续批改构造参数时同步删字段。
---

## 第2章 P1-0 页面寻址收口（PageIds）

<a id="ch2-P1-0页面寻址收口"></a>

> **批次**：P1-0（2026-11-08）　**状态**：已完成（附验证证据）
> **依据**：[Lua 退出专项设计](../设计文档/架构/Lua退出专项设计.md) §4.2、§6 P1-0。
> **口径**：只切 C# 侧的页面寻址（字符串键 → 生成常量 int），Lua 侧寻址原样保留。

### 2.1 计划校正（本批最重要的产出）

原设计 P1-0 ① 写的是「`PageIds` 先出 ⇒ `PageFlow` 切 int ⇒ **删 `UIPageResolver`**」，实施时对账后判定**不可行**，本批按事实改写并回写了设计文档：

- **未迁移的 Lua 页面互相导航仍靠字符串键**：`Bridge.ui.Show/Replace/Close` 在 7 个页面里共 11 处（Lobby→Records/Settings/Room、Settings→Lobby、Room→Lobby、Result→Lobby …）。删 resolver = 这些调用当场反查 -1。
- **逐页清 `lua_path` 同样不可行**：清掉某页的键，会让"还没迁移的页面"跳不到它（不是跳不到自己）。
- 结论：`PageIds` 照出、`PageFlow` 照切，但 **`UIPageResolver`/`IPageDirectory.Resolve(string)`/`PageDefinition.LuaPath` 留到 P2a**；P1 每页只"挂 C# 组件"，**不删 `.lua`、不清键**（解析器本来就把 prefab 自带逻辑排在 `LuaPath` 之前，组件一挂即接管）。
- 连带裁分：**P2 拆成 P2a（页面 Lua 面一次性收口）＋ P2b（运行时与框架摘除）**；`P2a` 必须是单次原子动作，没有"一页一清"的中间态。

### 2.2 改动面

| # | 项 | 落点 |
|---:|---|---|
| 1 | 生成页面 id 常量（与 `UIPageCatalog` 同源同次运行，重名按 `<名字>_<id>` 消歧） | `scripts/codegen/gen-ui-page.py` → `Assets/LiteGame/Modules/UI/Generated/PageIds.g.cs`（13 行定义 / 12 个唯一名，`Tmp_Dialog_201` 与 `Tmp_Dialog_206`） |
| 2 | 页面流切 int（去掉 `IPageDirectory` 依赖与 `TryResolve` 的错误分支——未登记页在编译期即失败） | `Modules/Procedure/PageFlow.cs` |
| 3 | 预载期模态登记用常量（`_pages.Resolve(LuaKeys.UI.Page_Settings)` → `PageIds.Page_Settings`），`ProcedurePreload` 去掉 `IPageDirectory` 依赖 | `ProcedurePreload.cs`、`ProcedureSegment.cs` |
| 4 | 工具清单与产物对账纳入新生成物 | `scripts/gate/toolchain.json`（`codegen.ui-page` outputs/summary）、`scripts/tests/test_generated_artifacts.py`、`Docs/说明/工具索引.md`（重生成） |
| 5 | EditMode 用例改吃生成常量，清掉只为旧反查服务的 `Bridge.Bind`/读表辅助 | `Assets/Tests/EditMode/PageFlowBattleMenuEditModeTests.cs` |

### 2.3 验证证据

| 项 | 命令 | 结果 |
|---|---|---|
| Unity 重编译 | `unity command recompile` → `recompile_status` | `failed=false, errors=[]` |
| 生成物新鲜度 | `python scripts/codegen/toolbox.py ui-page --check` | `CHECK OK: 13 pages`（catalog + PageIds 双档） |
| 页面流行为（EditMode） | `unity command run_tests --mode editmode --filter PageFlowBattleMenu` | **6/6 Passed**（含 `OpenLanding(Lobby)`、`OpenBattleSettings`、`OnMatchEnded` 回大厅） |
| 目录/表一致性（EditMode） | `--filter UIPageDefinition` | **2/2 Passed**（生成目录与表清单逐行一致、未知 Id 抛） |
| L1 基线 | `python scripts/gate/test.py --suite L1 --profile PullRequest` | **PASS，1988 用例** |
| 文档门禁 | `python scripts/gate/test.py --suite Doc` | PASS（doc-lint 0/0；工具索引 current） |
| 目录平铺 | `python scripts/gate/dir-flat-lint.py` | OK（235 目录扫描、66 基线、无增长） |

### 2.4 未做（有意推迟）

- **②`PageLogicBase`＋`IPageContext`、③`UIListRowSet`、④`UIPageLogicScanner`＋`PageLogicRule`**：三项都是"载体"，没有消费者就会被红线 10 判成待接线悬空。改为与第一页（`Page_ButtonDemo`）同批落地，挂 `P1-1`。
- **页面 Lua 面清理**（删 `.lua`/清 `lua_path`/删 resolver）：整体移到 `P2a`，理由见 2.1。

### 2.5 遗留

- `UIPageScaffoldRegister` 一组 EditMode 用例单次运行超过管道 30s 上限（首次 `UIPage` 过滤跑到一半因 `P2` 的 scaffold 建 prefab + Luban 生成耗时过长而被截断）。该组未被本批触及（`UIPageScaffold.LuaDir` 只在 P2a 动），本轮未复跑；下次动起页/登记工具时必须先跑它。
- 新生成物需要 `.meta`：`PageIds.g.cs.meta` 由 Unity 导入生成并随本批入库（否则克隆后 GUID 漂移）。
---

## 第3章 P1-1 编辑器面：契约对账改读页面逻辑源

<a id="ch3-P1-1编辑器面对账"></a>

> **批次**：P1-1 编辑器面（2026-11-08）　**状态**：已完成（附验证证据；L1 红由并行线未提交文件引起，见 3.5）
> **依据**：[Lua 退出专项设计](../设计文档/架构/Lua退出专项设计.md) §4.7、§6 P1-1。
> **分工**：页面逻辑件与 prefab 挂组件归并行线；本批只做**编辑器侧对账面**（扫描器 + 规则 + 双源解析）。

### 3.1 做了什么

| # | 项 | 落点 |
|---:|---|---|
| 1 | 新增 `UIPageLogicScanner`：C# 页源扫描（名字型首参字面量、泛型取控件 `GetControl<T>(名字)`、事件接线 handler 解析）＋ 双源解析（C# 优先 → Lua 回落）＋ 按扩展名分派；无静态可变状态 | `Assets/LiteGame/Editor/UITools/Reference/UIPageLogicScanner.cs` |
| 2 | 调用点/接线 DTO 更名 `UILuaApiCall`/`UILuaEventWiring` → `UIPageApiCall`/`UIPageEventWiring`（两种语法共用同一形状，Lua 专属解析留在原扫描器） | 同上 ＋ 6 处消费方同批改名 |
| 3 | 五个消费方改走双源：引用对账、文本双写、文本 key、占位符、契约 `events[]` 投影（`UIContractBatch`） | `Validation/Rules/{Copy,Structure}`、`Contract/UIContractBatch` |
| 4 | 新增规则 `ui.page-logic`：逻辑件存在 ⇒ prefab 根必须挂实现 `IUIFormLogic` 的组件；重复实现报错；迁移期不要求每页有件 | `Validation/Rules/Page/PageLogicRule.cs` ＋ `UIRuleIds`/`UIValidator` 注册 |
| 5 | 文本占位符对 C# 语义分叉：`SetTextKey(name, key, args…)` 本身带参是合法写法，只核对实参个数（Lua 面维持「无参 API 挂带参条目＝Error」） | `Rules/Copy/TextPlaceholderRule.cs` |
| 6 | API 词表分语言：C# 面 `PageNameFirstApis`（含 `SetDropdownValue`/`GetControl`/`OnTabChanged`）、`PageTextWriteApis`、`PageEventApis` | `UIRuleThresholds.cs` |

### 3.2 计划校正（两处，已回写设计文档）

- **目录名**：原设计写 `Modules/UI/Logic/`，但 `DomainBoundaryTests.业务域子目录必须按职责命名` 明令禁 `Logic`/`View`/`Presentation`/`Domain`。设计文档改为 `Modules/UI/Pages/<Page>/Page_<Name>Logic.cs`，并补「目录名受守卫约束 ＋ 单目录 ≤5 文件」一条。
- **规则落点**：`Rules/Structure/` 已有 10 个直接文件（红线 15 基线 10、只减不增），新规则放进去会记「回涨 1」。改为独立目录 `Rules/Page/`（1 个文件），`dir-flat-lint` 转绿。

### 3.3 验证证据

| 项 | 命令/方式 | 结果 |
|---|---|---|
| Unity 重编译 | `recompile` → `recompile_status` | `failed=false, errors=[]` |
| 新扫描器用例 | EditMode `--filter UIPageLogicScanner` | **6/6 Passed**（字面量提取／变量名与注释不产出／方法组与 lambda／双源分派／真实页源唯一／规则迁移期告警／模板页豁免） |
| Lua 侧回归 | `--filter UILuaReferenceScanner` | **6/6 Passed**（DTO 迁移后行为不变） |
| 文本规则 | `--filter UITextRule` | **7/7 Passed** |
| 契约对账 | `--filter UIContractReconciliation` | **8/9**；唯一红＝`漂移_契约与prefab一致零误报`——工作区里 `Page_Login.prefab` 被并行线改动而契约未重生成（`git status` 可见），与本批无关 |
| 目录平铺 | `dir-flat-lint.py` | OK（无增长） |
| 文档门禁 | `--suite Doc` | PASS（doc-lint 0/0） |

### 3.4 已知边界

- 跨行调用（把控件名字面量折到下一行）不产出调用点——与 Lua 扫描器同口径（单行）；写页逻辑时保持单行调用即可。
- `PageLogicRule` 的「未挂载体」在迁移期是 **Warning**（`RequireLogicForEveryPage=false`）；P2a 翻成 Error，只改一处常量。

### 3.5 如实记录：本批 L1 为红（归并行线）

`--suite L1` 5 条失败全部指向并行线**未提交**的新文件，与本批改动无关（逐条证据）：

| 失败守卫 | 指向 |
|---|---|
| 模块接口必须住 Abstractions | `Modules/UI/Ports/{IMapCatalog,IMetaSession,IRoomView}.cs` |
| Forget 必须带观测回调 | `Logic/Screens/{Auth,Hub,Settings}/Page_*Logic.cs` 7 处 |
| 跨模块类型必须住 Abstractions 或 Core | `Logic/Screens/{Match/ResultPageData,Settings/SettingsPageData}.cs` |
| 模块产物与依赖声明必须可静态解析 | UI Require 的 `IMetaSession`/`IMapCatalog`/`IRoomView` 无产出方登记 |
| 业务域子目录必须按职责命名 | `Modules/UI/Logic`（守卫禁 `Logic` 这一级名字） |

本批自身的验证面改用：Unity 编译 0 错 ＋ 上面四个 EditMode 分组 ＋ Doc/dir-flat。并行线收口后应重跑 L1 并把本卷结论校正。

---

## 第4章 P1-2 七页 C# 化与逐页集成

<a id="ch4-P1-2七页C化"></a>

> **批次**：P1-2（2026-11-08）　**状态**：已完成（附验证证据）
> **依据**：[Lua 退出专项设计](../设计文档/架构/Lua退出专项设计.md) §4.1–§4.4、§6 P1。
> **分工**：编辑器对账面（扫描器/规则/双源）归 P1-1（第3章）；本批做**页面载体、七页逻辑与逐页集成**。

### 4.1 改动面

| # | 项 | 落点 | 说明 |
|---:|---|---|---|
| 1 | 页面基类与上下文 | `Modules/UI/Pages/{PageLogicBase,PageContext}.cs`、`Abstractions/IPageContext.cs` | `OnHide` 由基类封口：先跑页面收尾再 `UnbindAll`（等价 Lua 适配器解绑纪律）；`CloseSelf` 走 UI 服务统一关闭 |
| 2 | 行表构造 | `Modules/UI/Bind/UIListRowSet.cs` | `Row(key).Text(节点, 值).KeyText(节点, 键, 参数…)` 链式；整表替换语义 |
| 3 | 框架注入缝 | `UIForm.PageContext`、`UIFormLogicBinder` provider、`UIService` 构造参数、`UIBindBase.Index/Locale` | 页面上下文由组合根产出、UI 段构造注入延迟取件；框架不解释上下文类型 |
| 4 | 页面端口与实现 | `Abstractions/Ports/{IMetaSession,IMapCatalog,IRoomView}.cs`；`App/PageServices/{MetaEntrySession,ConfigMapCatalog}.cs` | 联机/地图/房间三面端口；`RoomBridge` 同实例实现 `IRoomView`（与 Lua 门面同快照同事件） |
| 5 | 七页逻辑 | `Pages/Screens/{Auth,Demo,Hub,Match,Settings}/Page_*Logic.cs` | Login/Lobby/Room/Result/Settings/Records/ButtonDemo；控件名与文案键逐条对齐原 Lua 页 |
| 6 | 数据面强类型 | `Abstractions/Pages/{ResultPageData,SettingsPageData}.cs`、`Pages/Screens/Hub/RoomPageData.cs` | `PageFlow` 不再吃 `IPageDataFactory`（构造签名与对战菜单用例同批） |
| 7 | prefab 挂件 | 7 个 `Content/Screens/Page_*.prefab` 根 | 走 unity-pipeline `PrefabUtility.LoadPrefabContents/AddComponent/SaveAsPrefabAsset`（非手改 YAML） |
| 8 | 表/清单/契约/catalog | `Luban/Data/#uiform.xlsx`、`UIPageManifest.json`、`Editor/UITools/Contracts/Page_*.contract.json`、`Generated/{UIPageCatalog,PageIds}.g.cs` | 7 行清 `lua_path`；契约重生成（`luaPath=null`、`events[].handler` 改取 C# 方法名）；manifest 与 catalog 同源重生成 |
| 9 | 删除页面脚本 | `Assets/LiteGame/Lua/UI/Page_*.lua`（7 份，走 `delete_asset`） | 剩 `main.lua`/`Core/class.lua`/`Tests/UITestPage.lua` |
| 10 | 守卫修正 | 目录与类型归位、`ForgetLogged`、allowlist、UI asmdef | 见 4.2 |
| 11 | 过渡判据收口 | `Adapters/LuaBridge/RegistryFiller.cs` | 无 `lua_path` 页面行时跳过 `LuaKeys` 启动锚校验（否则全部迁移后每次启动必报"常量未注册"） |

### 4.2 计划校正（已回写设计文档）

- **页面目录**：设计原写 `Pages/<Page>/`；实际按域分组 `Pages/Screens/<域>/`（单目录 ≤5 文件，域内页共享上下文）。§4.1 载体行已按实际改写。
- **页面上下文注入方式**：不在 `UISegment` 里 `context.Get<IMetaSession>()` 取端口——那会同时触发「模块依赖声明」守卫（UI 段早于 Meta/Battle 装订）与装配序耦合。改为组合根构造 `PageContext`、经 `UISegment` 构造参数注入工厂，首次开页时才解析。
- **`ProductSettings` 跨模块**：设计 §4.4 明文允许设置页直读；本批登记 `CrossModuleContractLocationTests` allowlist 一条（附"P2 后评估注入化"去处），未改动其本体。
- **集成时点与 P1-0 §2.1 的关系**：P1-0 把「清 `lua_path`/删 `.lua`/删 resolver」推到 P2a，前提是"还有 Lua 页靠字符串键互相导航"。七页全部 C# 后该前提消失（`Bridge.ui` 已无产品页消费者），故本批直接完成表键、契约与脚本删除；**`UIPageResolver`/`IPageDirectory.Resolve`/`LuaKeys`/`Bridge.*` 仍留 P2a**。
- **`luaPath` 字段**：契约与清单里保留为 `null`（列/字段删除归 P4 的哈希事件）。

### 4.3 验证证据

| 项 | 命令/方式 | 结果 |
|---|---|---|
| Unity 重编译 | `recompile` → `recompile_status` | `{"status":"up_to_date","failed":false,"errors":[]}` |
| L1 基线 | `python scripts/gate/test.py --suite L1 --profile PullRequest` | **PASS，1988 用例**（并行线第3章记录的 5 条红已随本批修复） |
| 生成物新鲜度 | `python scripts/codegen/toolbox.py ui-page --check` | `CHECK OK: 13 pages` |
| ButtonDemo 逻辑 | 编辑器冒烟（真 prefab ＋ 真 `UIBindIndex`） | 点击计数 `0 → 1 → 2`，文本随点更新 |
| Result 逻辑 | 同上 | 标题/击杀/名次按 key 写入；无数据 → `NoMatchData` 且本机摘要隐藏 |
| Login 逻辑 | 同上 | 未配置 → `UI.Login.NoServer`；登录失败 → `UI.Login.Failed` |
| Records 逻辑 | 同上 | 有数据时空态隐藏 |
| Lobby 逻辑 | 同上 | 空房 → `UI.Lobby.RoomEmpty`；账号名写入；轮询 tick 不抛 |
| Room 逻辑 | 同上 | 房主开局钮可见、等待人数、点请求、被拒复位、房主迁移后隐藏 |
| 契约 diff 核对 | `git diff -- Contracts` | 7 页 `luaPath: null`；`events[].handler` 由 `_Ping/_Login` 改为 `Ping/Login`（改读 C# 页源） |
| 脚本退出条件 | `Get-ChildItem Assets/LiteGame/Lua -Filter *.lua` | 仅 `main.lua`/`class.lua`/`Tests/UITestPage.lua` |
| UI 校验（`ui.validate`） | 编辑器内 `UIValidator.Run()` | **errors=83**：77 `ui.binding-naming`（Lobby/Settings 装饰子节点）＋ 6 `ui.page-structure`（缺 `SafeAreaReceiver`）——**全部为存量**；本批引入的 1 条 `ui.text.static-key`（`UI.Result.Reason.` 字面前缀）改为局部变量后清零 |
| 页面流 EditMode | `--filter PageFlowBattleMenu` | **6/6 Passed**（含 `OpenBattleSettings` 带 `SettingsPageData`） |
| 契约对账 EditMode | `--filter UIContractReconciliation` | **9/9 Passed**（误报红源于用例夹具仍写 `LuaPath="UI.Page_Login"`，改为 `null` 后转绿） |
| 扫描/文本/页面 EditMode | `UIPageLogicScanner` / `UITextRule` / `UIPageDefinition` | **6/6、7/7、2/2 Passed** |
| 架构/平铺/注释 | `arch-check`、`dir-flat-lint`、`comment-lint` | 全绿 |

### 4.4 遗留

- **存量校验红 83 条**（装饰节点命名 77、缺安全区接收器 6）：与 Lua 迁移无关，需单独成批处理（改名会动 prefab 与契约，按 UI 线纪律走）。
- **Settings 页未做运行时冒烟**：其数据面是 `ProductSettings` 静态单例（需四件服务绑定），本批以编译 ＋ prefab 挂件 ＋ `ui.page-logic` 覆盖；P2a 前补一条 PlayMode。
- **`LuaKeys`/`Bridge.*`/`UIPageResolver`/`IPageDataFactory`/`UILuaReferenceScanner` 仍在**：其中 `RegistryFiller.KnownUIKey` 还引用 `LuaKeys.UI.Page_Login`，随 P2a 一次性摘除。
- **并行线搬动补偿**：`IBattleDevDiagnostics`/`IBattleViewAttachment` 已移回 `Modules/Battle/` 根（与 `ModuleInterfaceOwnershipTests` 的 `SeamExceptions` 路径一致）；`Tests/LiteNet.Tests`、`Server/RoomServer`、`Server/MetaServer` 共 **37 条**源链接按新目录重指（`Config/Table/*`、`Meta/WireSubModule/{Core,Login,Join,Lobby,Match,Instance}/*`）；`toolbox.py build-hash --check` 两端常量未变（摘要按内容归一）。

---

## 第5章 P2a 页面 Lua 面收口（进行中）

<a id="ch5-P2a页面Lua面收口"></a>

> **批次**：P2a（2026-11-08）　**状态**：P2a 与 P2b 已完成；仅剩 P4 数据面（见 5.3）
> **依据**：[Lua 退出专项设计](../设计文档/架构/Lua退出专项设计.md) §4.2、§4.7、§6 P2。
> **口径**：页面逻辑、页面注册表与 Lua 门面一次性摘除；运行时宿主（`ILuaHost`/Env/`main.lua`）留 P2b。

### 5.1 已完成（页面逻辑 / 注册表 / 门面 / 寻址 / 扫描面）

| # | 项 | 落点 |
|---:|---|---|
| 1 | 页面逻辑适配链删除 | `LuaBehaviourAdapter`（含 dispatch 生成物）、`LuaUILogicFactory`、`LuaUIBinding`、`ILuaUIBinding`、`IUILogicFactory`、`LuaUIData`、`LuaPageDataFactory`、`IPageDataFactory`、`LuaListRows`、`UIApiShim` |
| 2 | 页面注册表链删除 | `ILuaRegistry`/`RegistryFillReport`/`LuaRegistry`、`IUILuaRegistry`/`UILuaRegistry`/`RegistryFiller`/`LuaRegistryRefillService`、`ILuaRegistryFiller` |
| 3 | Lua 门面删除 | `Bridge`（Bridge.Data.cs）、`ProductBridges`、`ILuaProductBridge`、`LuaDevReload`/`LuaDevRefill`、toolchain 的 `codegen.lua-reload`/`codegen.lua-refill` 与两个编辑器动作 |
| 4 | 框架换表语义收口 | `UIFormLogicBinder` 去 LuaPath 分支与 stale/DropAll/EnsureAfterReload/SwapIfStale/Forget；`UIService` 去 `MarkLogicStale`/`ApplyStaleLogic`/`StaleLogicCount`/`DropAllLogic`；`UIForm` 去 `NeedsReinit` |
| 5 | 装配面收口 | `UISegment` 去 `ILuaUIBinding`/`ILuaHost` 依赖（DependsOn 去 LuaHost）；`AppComposition` 去 `LuaUIBindingFactory`/`LuaRegistryFillerFactory`/`BindLuaFacades`/`BindRegistryRefill`；`ProcedureSegment` 只留房间会话装订；`ProcedurePreload` 去 filler/productBridge（保留 `main.lua` 启动锚） |
| 6 | 生成链收口 | `gen-ui-api.py` 删 `UIApiShim`/`Dispatch` 两产物与 toolchain outputs；API 清单（UIBindBase/UIBindIndex/Manifest/Doc）保留 |
| 7 | 用例迁移 | 真 Lua 页 PlayMode → `CSharpPagePlayModeTests`（4 用例语义保留：冷开/复用/Shutdown/在途取消）；删 `UIU0`/`UIConfirmApi`/`UIControlListProtocol`（Lua API 面）/ `LuaRegistry`/`RegistryFillReport` 测试；修剪 LText/Retirement/MetaEntry/UIApiSpec 的 Lua 门面断言；删 env 重建用例 |
| 8 | 寻址收口 | `PageDefinition.LuaPath` 删除、`UIPageResolver` 与 `IPageDirectory.Resolve(string)` 删除、`gen-ui-page.py` 停发射该字段、catalog 重生成、20 个用例构造清理 |
| 9 | 门面尾部 | `MetaBridge` 删除（`BindMetaEntry` 只留流程绑定）、`RoomBridge` 去 Lua 全局装订（`OnOwner`/`OnMembers`/`StartMatch`/`BindGlobals`）、4 个守卫用例（M3/Architecture/LuaFreeze/TaskObservation）同步 |
| 10 | 编辑器扫描面 | `UILuaReferenceScanner` 删除、`UIPageLogicScanner` 收敛 C#-only（`ResolveFile(name)`/`EnumerateSources(root)`）、文本两规则单根、名字面守卫改扫 C# 并白名单行字段、Lua 版扫描用例改写 |
| 11 | 起页/登记工具去 Lua | `UIPageScaffold.LuaDir`/`BuildLuaSkeleton` 删除、`UIPageRegistration` 去 Lua 骨架步与 `UIPageRegisterFailure.Lua`、用例同步（登记正常路径/id 冲突已验） |
| 12 | P2b 运行时宿主摘除 | `ILuaHost`/`LuaHostSegment`/`LuaComponent`/`LuaPreloader`/`EventBridge` 删除；`ClientEntry` 去 ⑩ 段；`LuaLayout`/`LuaTextAssetImporter`/`main.lua`/`class.lua`/`UITestPage.lua` 删除；`ProcedurePreload`/`ProcedureSegment` 去 `main.lua` 预载与 `ILuaHost` 依赖（`ListLuaAssetPaths` 一并删除） |
| 13 | 守卫同步 | `SegmentBudgetTests` 段数 18→17、`SegmentScopeAdoptionTests`/`TaskObservationGuardTests` 去宿主条目、`LiteGameIgnoreRule` 去 Lua 放行、`SafeCall` 注释修正 |

### 5.2 验证

| 项 | 结果 |
|---|---|
| Unity 重编译 | `completed / failed:false / errors:[]` |
| L1 基线 | **PASS，1976 用例**（较 1988 少 12：删掉的 Lua 注册表/报告用例） |
| EditMode | 页面流 **6/6**、契约 **9/9**、页面目录 **2/2**、文本 **7/7**、扫描器 **5/5**、名字面 **1/1**、UIU1 **19/19**、UIApiSpec **2/2** |
| 代码面 | 全仓 `LuaBehaviourAdapter`/`RegistryFiller`/`UIApiShim`/`ProductBridges`/`Bridge.Bind` 仅余注释（0 实质引用） |
| 文档 | `--suite Doc` PASS（工具索引已随 toolchain 变更重生成） |

### 5.3 后续（已由第6章完成）

- **表列**：`#uiform.xlsx` 删 `lua_path` 列（`_uiform_row.py` 五列化）→ Luban 重生成 → `build-hash` 复算。
- **契约/清单字段**：`UIPageContract.Page.LuaPath` 与 `UIPageManifestEntry.LuaPath` 删除（生成器/diff/同步/哈希/工作台同步），7 份契约与清单重生成。
- **`LuaKeys`**：`Modules/Shared/Generated/LuaKeys.g.cs` 与 `gen_lua_keys.py`、toolchain `codegen.lua-keys` 一并删除。
- **收尾**：受影响用例（约 20 文件）更新 + 全量门禁 + 文档 96 篇口径回写。

### 5.4 发现（非本批，如实记录）

- `scripts/codegen/gen-ui-api.py` 的 `OUT_*` 指向 `Assets/LiteFramework/...`，而框架当前实际住 `Packages/com.litegame.framework`（HEAD 即如此）。本次误跑 `ui-api` 会在 `Assets/` 生成同名生成物（**已删除**）；框架搬迁归属另有其线，本批不裁决。

---

## 第6章 P4 数据面收口（已完成）

<a id="ch6-P4数据面收口"></a>

> **批次**：P4（2026-11-08）　**状态**：已完成
> **依据**：[Lua 退出专项设计](../设计文档/架构/Lua退出专项设计.md) §4.6～§4.8、§6 P4。
> **口径**：表列、契约/清单字段与 `LuaKeys` 三面同批退出；生成链与摘要随内容一起归一。

| # | 项 | 落点 |
|---:|---|---|
| 1 | 表列删除 | `Luban/Data/#uiform.xlsx` 删 `lua_path` 列（五列：id/prefab/layer/full_screen）；`_uiform_row.py` 五列化（校验/写入/列表去 lua）；`gen-ui-page.py` 去列读与一致性告警 |
| 2 | 表链重生成 | `Luban/gen.bat` → `uiform.cs` 无 `LuaPath` 字段、`UI/tbuiform.bytes` 同步；`gen_luban.py` 修 `TableLayout.cs` 新路径、删 `LuaKeys` 产出步 |
| 3 | 契约/清单字段 | `UIPageMetadata.LuaPath`/`UIContractPage.LuaPath`/`UIPageManifestEntry.LuaPath` 删除；生成器/diff/批量/同步/指纹/工作台/校验文案同步；12 份契约与页面清单重生成（无 luaPath） |
| 4 | `LuaKeys` | `Modules/Shared/Generated/LuaKeys.g.cs`、`Luban/gen_lua_keys.py`、toolchain `luban` 的 LuaKeys 产物与 `codegen.lua-keys` 引用删除 |
| 5 | 用例与脚本 | 生成物测试去掉三类已删产物与 LuaKeys 用例；清单基线改 12 页/13 行；模板依赖用例改指 `Tmp_Dialog`；契约生成/对账/清单关键用例逐条绿 |

### 6.1 验证

| 项 | 结果 |
|---|---|
| Luban 重生成 | `uiform.cs` 无 `LuaPath`；`UI/tbuiform.bytes` 已发布 |
| BuildHash | `build-hash` 写新常量（shared `4a5ef836e06a8b0f` / 120 文件；meta `229b5d862b158fb3`），`--check-shared` OK |
| L1 | **PASS，1976 用例** |
| EditMode | 契约对账 **9/9**、契约生成 **10/10**、页面目录 **2/2**、页面流 **6/6**、清单基线/首次生成、登记正常路径 **1/1** |
| 生成物 | `ui-page --check`：13 pages |
| 文档 | `--suite Doc` PASS（工具索引已随 toolchain 变更重生成） |

### 6.2 遗留（非 Lua 退出线阻断项）

- **内容侧 P3**：`LuaScriptsHealthProbe`、`CandidateLuaValidator`、`FileSystemLuaCandidateSource`/`ICandidateLuaSource`、`lua` 收集组与空的 `Assets/LiteGame/Lua` 目录仍在（收集配置引用未清，随内容链在 P3 退）。
- **`GenConfig.cs`**：XLua 导出配置仍留历史门面委托表（需随 P3 包体退出重生成）。
- **文档口径**：`Docs/说明` 中表列/清单字段的旧描述（`lua_path`）尚未全量扫尾，按 96 篇口径回写另批。
- **全量套件**：`UIPageScaffoldRegister`/`UIPageManifest` 整包受 CLI 30s 上限，本轮按关键用例逐条验证（其余用例编译通过、逻辑未改）。

---

## 第7章 P3 内容、构建、程序集与门禁

<a id="ch7-P3内容构建程序集与门禁"></a>

> **批次**：P3（2026-11-08）　**状态**：已完成（附验证证据）
> **依据**：[Lua 退出专项设计](../设计文档/架构/Lua退出专项设计.md) §4.5、§4.8、§6 P3。
> **口径**：内容链、收集组、构建包体、程序集与门禁一次性去 Lua；并补做 P4 遗留的两列（`#strategy.lua_path`、`#contententry.entry`），本批因此附带一次 BuildHash 事件。

### 7.1 改动面

| # | 面 | 项 | 落点 |
|---:|---|---|---|
| 1 | 内容链 | 发布布局去 Lua 前缀/脚本映射 | `ReleaseLayout` 删 `LuaPrefix`/`LuaScripts`/`LuaModuleOf` |
| 2 | 内容链 | 候选脚本链整套删除 | `ICandidateLuaSource`/`FileSystemLuaCandidateSource`、`CandidateScriptSet`（含 `ScriptEntry`/`ScriptSetVerdict`）、`LuaScriptsHealthProbe`、`CandidateLuaValidator` |
| 3 | 内容链 | 装配去 Lua/Bridge 探针 | `ContentSegment` 去两工厂与 `ICandidateLuaSource` 登记；`AppComposition`/`ClientEntry` 同步（构造参数 6→4） |
| 4 | 内容链 | 候选脚本生成面 | `gen-candidate.py` 受控内容只认 `config/**` 与 `bundle/**`；`test_gen_candidate.py` 用例同步 |
| 5 | 收集组 | YooAsset 收集组 10→9 | `BundleCollectorSetting.asset` 删 `Lua` 组（CollectPath `Assets/LiteGame/Lua`） |
| 6 | 资产 | Lua 空目录 | `Assets/LiteGame/Lua`（3 空子目录＋meta）删除 |
| 7 | 资产 | xLua 源码与原生库 | `Assets/ThirdParty/XLua/**`（183 件）、`Plugins/**/xlua.*`（14 件）、`xlua.bundle` 删除 |
| 8 | 资产 | xLua 工具 | `Tools/XLuaGenerate.exe`、`XLuaHotfixInject.exe`、`XLua.Mini.dll/pdb`、`xlua.dll`、`libxlua.so`（8 件）删除 |
| 9 | 资产 | 导出配置与 Bridge 能力声明 | `Editor/GenConfig/GenConfig.cs`、`BridgeCapabilityHealthProbe`、`BridgeCapabilitySet`（API 版本改读 `UIApiManifest.ApiVersion` 单源） |
| 10 | 程序集 | LuaBridge 退场 | `RoomBridge` 迁 `Assets/LiteGame/App/PageServices/`（`move_asset`，GUID 保留）；`LiteGame.Adapters.LuaBridge.asmdef` 随目录删除 |
| 11 | 程序集 | asmdef 引用面 | 6 处去 `xLuaMain`（框架 Adapters/Dev/Editor/Meta/EditModeTests/UI.PlayModeTests）；5 处去 `LiteGame.Adapters.LuaBridge`（App/Dev/Editor/EditModeTests/UI.PlayModeTests） |
| 12 | 程序集 | HybridCLR hot 清单 | `HybridCLRSettings.asset`＋`HybridCLRTool.cs` 去 `LiteGame.Adapters.LuaBridge`（走工具自身 `MarkHotAssemblies` 单源刷新） |
| 13 | 程序集 | 架构模型 | `arch/units.json` 去 `xLuaMain`、`LuaBridge` 单元、`Lua` 收集组/收集例外/变更矩阵行/扫描根/`topLevel.liteGame`；`arch-check --write` 重生成 `ScanTargets.Generated.cs` 与 `module-tiers.json` |
| 14 | 门禁 | 纪律扫描 | `DisciplineScanner` R12 去 `XLua` 规则与两目录、扫描目标去 XLua 根、排除去 `/XLua/` |
| 15 | 门禁 | 工具清单 | `toolchain.json` 删 `codegen.xlua`、`ui.page-register` 的 Lua 产物与两处 summary、`codegen.ui-api` 摘要、`luban` 的 `gen_lua_keys` 引用 |
| 16 | 门禁 | 脚本门前置 | `restore-check.py` 去 `xlua.bundle` 必需项；`player-smoke.py` ABI 理由改写（arm64 为发布判据）；`comment-lint.py` 去 `XLua` 排除 |
| 17 | 门禁 | 生成器死码 | `gen-ui-api.py` 删 `render_shim`/`render_dispatch`/`shim_*`/`expr_of`/`call_exprs`（产物已在 P2a 删） |
| 18 | 用例 | Lua 专属载荷删除 | `CandidateLuaValidatorEditModeTests`、`ContentCandidateWiringEditModeTests`、`MetaPagesEditModeTests`、`CandidateScriptSetTests`（L1） |
| 19 | 用例 | 引用面改写 | `NavigationPlayModeTests` 去 Lua env 载体；`UIStatePlayModeTests`/`MetaEntryEditModeTests` 去 `using XLua`；`UIBindingReferenceScan` 默认根改 `Modules/UI/Pages`；`UIToolchainWiring` 页数 11→12、试点页断言改事件接线；六个守卫（LuaFreeze/ClientAsmdef/CheatBoundary/PageDefinition/DisciplineScanner/StaticMutable/TaskObservation/CrossModule）期望同步 |
| 20 | 数据面 | 补 P4 遗留两列 | `#strategy.lua_path`、`#contententry.entry` 删列 → `Luban` 重生成 → `build-hash` 重写（shared `4a5ef836e06a8b0f` → `5937ac5161377bac`） |

### 7.2 计划校正与判断

- **P4 有两列漏做**：设计 §4.6 要求三表删 Lua 列，第 6 章实际只删了 `#uiform.lua_path`；`strategy.cs` 仍带 `LuaPath`、`contententry.cs` 仍带 `Entry`（Lua 处理器路径）。本批补做，代价是 P3 也动了一次 BuildHash——发布侧需按[热更专项 §11](../设计文档/客户端/内容/热更与内容发布专项设计.md) 的两端顺序同步。
- **`BridgeCapabilitySet` 一并退出**：其能力词表全是已删除的 `bridge.*` 门面，唯一还活着的语义是 `ApiVersion`（`UIApiManifest` 单源）。本批把 `IdentitySegment` 直连 `UIApiManifest.ApiVersion`，删该件与其静态可变允许项；`ReleaseManifest.RequiredCapabilities`/`BridgeApiVersion` 作为协议字段保留（注释标注历史字段）。
- **`BridgeCapabilityHealthProbe` 删除而非迁移**：它的判据是"候选要求的 `bridge.*` 能力本包是否具备"，Bridge 门面删除后能力集已无事实来源；`MetaPagesEditModeTests` 中依赖它的用例同批删除。
- **`RoomBridge` 是迁出不是删除**：它已是页面端口 `IRoomView` 的生产实现（与 `IRoomSessionBridge` 同实例），随 LuaBridge 程序集退出去 `App/PageServices/`；`ProcedureSegment` 只经端口取件。
- **`UIPageManifest` 注释修正**：第 53 行注释原写"Lua 逻辑路径"却挂在 `CacheStrategy` 字段上（P4 删字段时漏改），本批改为字段真义。
- **`UIBindingReferenceScan` 默认根换位**：原默认根含 `Assets/LiteGame/Lua`，Lua 目录删除后改为 `Assets/LiteGame/Modules/UI/Pages`（C# 页逻辑），并把"真实引用面命中"用例改为扫 `Txt_Status`。
- **`UIValidation` 三红不属本批**：真实资产面仍有 84 Error（77 命名 + 6 缺 `SafeAreaReceiver` + 1 页面清单漂移），与第 4 章 §4.4 登记的"存量校验红 83 条另批"同源；另 `报告项_五字段齐全` 因规则族扩展后合成上下文未隔离全局清单（6 vs 1），亦非本批引入——统一留给 UI 校验存量批。

### 7.3 验证证据

| 项 | 命令/方式 | 结果 |
|---|---|---|
| Unity 重编译 | `recompile` → `recompile_status` | `completed / failed:false / errors:[]`（删 XLua、asmdef、BridgeCapabilitySet 后各复跑一次） |
| L1 基线 | `python scripts/gate/test.py --suite L1 --profile PullRequest` | **PASS，1931 用例**（较 P4 的 1976 少 45：Lua 专属用例删除） |
| EditMode 页面/契约 | `PageFlowBattleMenu` / `UIContractReconciliation` / `UIPageDefinition` / `UIPageLogicScanner` / `UITextRule` | **6/6、9/9、2/2、5/5、7/7** |
| EditMode 工具面 | `UIBindingTools` / `UIToolchainWiring` / `ReleaseLayout` / `UIApiSpecGuard` | **12/12、7/7、5/5、2/2** |
| EditMode 内容/联机 | `MetaEntry` / `Candidate` / `ContentRuntimeAdapters` / `BundleDevIsolation` | **14/14、36/36、30/30、2/2** |
| PlayMode | `CSharpPage` / `Navigation` / `UIState` | **4/4、4/4、3/3** |
| 架构/门禁 | `arch-check --check` / `toolchain.py check` / `dir-flat-lint` / `comment-lint` | OK：33 units / 9 collect groups / 18 scan targets；manifest OK（2 存量 warning）；平铺无增长；注释 0 违规（725 文件） |
| 生成物 | `gen_luban.py --check` / `toolbox.py ui-page --check` / `build-hash --check` | CHECK OK；13 pages；shared `5937ac5161377bac`（120 文件）/ meta `229b5d862b158fb3`（62 文件）均 OK |
| 环境可复现 | `restore-check.py` | PASSED（`xlua.bundle` 必需项已移除，无缺件） |
| 验收 #1 静态面 | `rg "XLua\|LuaTable\|LuaEnv\|LuaFunction\|ILuaHost\|LuaPath\|LuaKeys\|Bridge\."`（`Assets/LiteGame` + `Assets/GameData/Generated` + 四个 Packages） | **0 命中**（区分大小写；此前 `strategy.cs:LuaPath` 已随 7.1-20 清零） |
| 验收 #3 包体面 | `BundleCollectorSetting.asset` 组名枚举 | **9 组**，无 `Lua` |

### 7.4 遗留与发现

- **`UIValidation` 三红为存量**（84 Error：命名 77 / SafeArea 6 / 清单漂移 1；合成上下文报告项 6 vs 1）——见 7.2 末条，归 UI 校验存量批。
- **`MetaPagesEditModeTests` 删除后无 C# 等价物**：它逐页断言 Lua 页行为（登录三态/大厅轮询/房间席位…），这些语义已由 C# 页逻辑承接但未建逐页自动化；当前覆盖只有 P1-2 §4.3 的编辑器冒烟与 `CSharpPagePlayModeTests` 生命周期面。登记为**功能缺口**，另批补 C# 页逻辑单测。
- **`UIPageScaffoldRegister`/`UIPageManifest` 整包仍受 CLI 30s 上限**（第 6 章 6.2 同款），本批按关键用例逐条验证。
- **`gen-ui-api.py` 的 `OUT_*` 仍指 `Assets/LiteFramework`**（第 5 章 5.4 已记），本批只删死码，路径归属仍待框架搬迁线裁决。
- **文档口径未扫**：`Docs/说明/09` 的候选 Lua 探针、`说明/08` 的 Lua 脚本章节等留 P5（96 篇口径回写）。
- **发布侧同步**：本批移动了 BuildHash（shared），客户端与云侧 Room 产物需按发布顺序同步后再联机；未在本批做云端发布。






## 第8章 P5 文档面收口

<a id="ch8-P5文档面收口"></a>

> **批次**：P5（2026-11-08）　**状态**：已完成（附验证证据）
> **依据**：[Lua 退出专项设计](../设计文档/架构/Lua退出专项设计.md) §6 P5。
> **口径**：设计/说明/规范改为现行形态（C# 页面逻辑、HybridCLR `code/`）；历史进度章不回改，由两处后批修正章撤销冲突结论；审计登记失效条目归档。

### 8.1 改动面

| # | 面 | 落点 |
|---:|---|---|
| 1 | 说明（主题级） | `08-UI与Lua脚本层` → **`08-UI与页面逻辑层`**（改名 + §6 整节重写：Lua 层→C# 页面逻辑层）；`01/02/09/19` 同步框架/内容/发行口径 |
| 2 | 说明（机制引用） | `03/10/11/12/13/14/15/16/17`、说明 README、架构图 README：删 LuaBridge/`Lua/` 目录/LuaKeys/LuaPanda/xLua 原生件/xLua 适配生成，Preload 五步序与 UI/内容数据流改 C# |
| 3 | 设计·UI 系统 | `UI系统总设计`（`PageDefinition` 六项、删 env 重建/shim/dispatch 两条生成物、§5.12 改 HybridCLR）、`UI逻辑API参考`（**整篇改 C# 参考**：`PageLogicBase`/`UIBindIndex`/`UIListRowSet`，删 `self.ui`/`Bridge.ui`/`luaPath`）、`UI编辑器工具链专项设计`（删 `luaPath` JSON 示例与"生成 Lua 骨架"步）、`UI运行时调试工具链专项设计`、`设置系统专项设计`（§6 `Bridge.settings`→`ProductSettings`）、UI 系统 README |
| 4 | 设计·内容 | `热更与内容发布专项设计`（发布布局 `config/code/bundle`、探针族、§10 整节改"内容发布与能力兼容"）、`资源收集目录专项设计`（tag 只余 `ui`、L2 样板改 `Modules/UI/Content`） |
| 5 | 设计·架构 | `商业级通用客户端框架总设计`、`客户端架构重构演进路线图`、`客户端依赖注入专项设计`、`客户端与服务端共享代码范围专项设计`、`框架先行建设与业务接入专项设计`、`生成器工具箱专项设计`、`开发工具链平台专项设计`、`命令总线专项设计`、`GM与调试工具分层专项设计`、`工具链域形态基线专项设计`、`商业级通用服务端框架总设计`、`Meta服务专项设计` |
| 6 | 设计·测试与性能 / 索引 | `调试与开发工具链演进路线图`、`测试开发框架总设计`、设计文档 README 索引描述 |
| 7 | 设计·玩法/网络/动画 | `游戏业务系统总设计`、`战斗美术资源参考`、`玩法数值与Luban配置专项设计`（去 `.lua` 产物与 `lua_path` 列）、`地图数据专项设计`、`动作与特效专项设计`、`角色状态与动作专项设计`、`局外背包客户端专项设计`、`联机战斗演示专项设计`、`命中反馈与伤害数字专项设计`、`Sim组织专项设计`、`状态同步专项设计`、`联机同步策略选型`、`动画模块专项设计` |
| 8 | 规范 | `02`（Lua shim 单源→受控 API 生成面、删 `Bridge.Data` 反例、目录计数去 `.lua`）、`03`（受控 API 改 `UIBindBase` 三产物、页面逻辑 C#、新增页面接入）、`04`（UI API Spec 三产物、Bridge 词条改"页面逻辑桥接件"、适配器示例改 `LiteGame.Adapters.Unity`）、`05`（RGB 双写/逐帧跨层判据改页面逻辑）、`06`（所有权表 `Lua/UI/**`→`Modules/UI/Pages/**`、登记状态/评审清单）、`08`（目录分层样板）、`09`（适配器命名示例） |
| 9 | 待办总览 | 顶部新增「Lua 退出口径（2026-11-08）」注记；8 行现行口径改判（UI/表现、热更与发布、UI 基本正确性、反馈面、真实客户端闭环、资源准入、候选/内容代次、构建面） |
| 10 | 进度修正 | `客户端重构线` 第 55 章（撤销"`Assets/LiteGame/Lua` 为终态"旧裁定）；`热更与内容线` 第 5 章（作废档位表 Lua/xLua 行）；进度索引两行描述加注 |
| 11 | 审计登记 | `功能缺口`（候选 Lua 运行时覆盖）、`技术债`（LuaDevReload/LuaDevRefill 两行）、`硬编码`（Lua shim 默认值）、`不统一路径`（Lua key 两约定）、`类设计`（Bridge.Data/RoomBridge）、`重复实现`（五页骨架）共 6 类失效条目归档；`文档债`/`工具链`/`目录平铺债` 文字校正 |
| 12 | 索引 | `Docs/README`、说明 README、设计 README、进度 README（08 改名后全仓链接同步 15 文件） |
| 13 | 随批代码收口 | `gen-ui-api.py` 的 `OUT_*` 改指包路径、删除 DOC 生成（`UI逻辑API参考.md` 改手写导读）；`LogTag` 删 `Lua`/`LuaRefill`；页面/框架 8 处 Lua 注释改为现行语义；`UIPageContract/Manifest.schema.json` 删 `luaPath` |
| 14 | 根入口 | `AGENTS.md` 资源目录样板 `LiteGame/Lua`→`Modules/UI/Content`、硬编码红线"Lua shim"→"生成器/工具" |

### 8.2 验证证据

| 项 | 命令/方式 | 结果 |
|---|---|---|
| 文档门禁 | `python scripts/gate/doc-lint.py`（改后多次） | **0 violation / 0 warning** |
| Doc 套件 | `python scripts/gate/test.py --suite Doc` | **PASS**（doc-lint + 工具索引 + comment-lint） |
| UI API 生成 | `python scripts/codegen/toolbox.py ui-api --check` | **CHECK OK：38 methods / 36 lua entries**（修 `OUT_*` 后由 FAIL 转绿） |
| 生成链 | `gen_luban --check` / `ui-page --check` / `build-hash --check` | 均 OK（本批未改表与共享源集） |
| 禁用语扫描 | 非历史进度/审计：`Lua 热更\|脚本激活\|Lua 预载\|Lua 脚本整套\|xLua hotfix\|self.ui\|Bridge.ui` | **0 现行表述命中**；仅 `UI逻辑API参考` 的否定句（"没有 `self.ui`/`Bridge.ui`"）与 `待办总览` 两处历史交付标记（已注"已随 Lua 退出删除"/"旧裁决作废"） |
| 改名链接 | 全仓 `08-UI与Lua脚本层.md` 引用 | 15 文件同步为 `08-UI与页面逻辑层.md`，doc-lint D4 无死链 |

### 8.3 遗留（非本批阻断项）

- **历史进度卷**（22 卷、约 650 行 Lua 字样）按"不回改前章"保留；`施工进度/README` 两行加注，冲突结论由第 55 章/第 5 章撤销。
- **审计归档与 `批次计划.md`** 保留历史字样（归档按规范不重写）。
- **`Lua退出专项设计.md`** 保留为迁移裁决记录（P5 后为历史参考，不删）。
- **`UIApiManifest` 历史属性名**与**代码内历史 Lua 注释**已在第 9 章清账（`SpecMethods`/`GeneratedMethods` + 代码/测试注释清零）。
- **`arch/units.json` tier `script`** 仍存在但无工具消费者（HybridCLR `code/` 承接后的语义待模型批裁决）。
- **发布侧**未做云端同步（P3 的 BuildHash 事件仍需按发布顺序处理）。


## 第9章 P5 收尾：manifest 历史名与代码 Lua 注释清账

<a id="ch9-P5注释与manifest清账"></a>

> **批次**：P5 收尾（2026-11-08）　**状态**：已完成（附验证证据）
> **触发**：第 8 章 8.3 的两项遗留——`UIApiManifest` 历史属性名、代码/测试内历史 Lua 注释。

### 9.1 `UIApiManifest` 属性改名

| 旧名 | 新名 | 语义 |
|---|---|---|
| `LuaMethods` | `SpecMethods` | spec `methods[].lua[].name` 的 lowerCamel 名清单（规格声明面） |
| `ShimMethods` | `GeneratedMethods` | 由 spec 生成的 PascalCase 受控 API 名清单 |
| `ManualMethods` / `IndexMethods` | 不变 | 手工处理器 / `UIBindIndex` 公开方法清单 |

- 改动面：`scripts/codegen/gen-ui-api.py`（块名 + 内部变量 `lua_names`/`shim_names`→`spec_names`/`generated_names`，`--check` 提示改 "spec entries"）、`Assets/Tests/EditMode/UI/Api/UIApiSpecGuardTests.cs`、`Docs/设计文档/客户端/UI系统/UI系统总设计.md` §5.14 生成物说明；重跑 `python scripts/codegen/toolbox.py ui-api` 重生成三产物。
- 保留：spec JSON 的 `lua` 键是数据格式名（改它要动 schema 与全部条目），另批处理。

### 9.2 代码/测试/脚本 Lua 注释清账

共约 78 处行级替换（含测试报文与脚本提示），全部改为现行语义或删除已无生产者的历史描述：

- **页面/UI**：`PageLogicBase`、`IUIFormLogic`、`IConfirmDialog`、`NullUIFormLogic`、`UIFxDefaults`、`UIService`（3 处）、`UIFormLogicBinder`、`UIBindIndex`（4 处）、`IUIListRows`、`IVirtualListSource`、`UIListField`、`UIFx`、`TransitionId`、`UITransitionRunner`、`UISegment`、`UIListRowSet`、`Page_ButtonDemoLogic`。
- **内容/构建/宿主**：`IContentService`、`IContentCacheMaintenance`（候选根 `config/code/bundle`）、`IContentActivator`、`ReleaseManifest`、`ICandidateHealthCheck`、`IHealthProbe`、`BuildIdentity`（UI API 版本单源改 `UIApiManifest.ApiVersion`）。
- **Dev/编辑器**：`DevHud`（删 "Lua" stats 段）、`UIPreviewDriver`、`UIBindingEditor`、`UIContractGenerator`、`LiteGameIgnoreRule`、`RoomBridge`、`LaunchLoading`、`ProcedurePreload`、`ProcedureSegment`、`IRoomSessionFace`、`MatchSettlementRow`、`BattleRankPolicy`、`PageData`（4 处）。
- **测试**：`UIContractReconciliationEditModeTests`（Lua fixture 改 C# 调用，`WritePageLua`→`WritePageSource`、`luaPath`→`pageSource`）、`BattleHudMenuEditModeTests`（4 条报文）、`BundleDevIsolationEditModeTests`（删 Lua 路径空断言）、`ContentRuntimeAdaptersEditModeTests`（签名夹具历史路径保留数据、只改注释）、`UIToolchainWiringEditModeTests`、`LTextWiringEditModeTests`（测试方法改名）、`PageFlowBattleMenuEditModeTests`、`UIPageDefinitionEditModeTests`、`UITextRuleEditModeTests`、`UIValidationEditModeTests`、PlayMode 6 处（`CSharpPage`/`Navigation`/`List`/`UIBehavior`/`AnimationResource`/`FeedbackSurface`）、`PlayModeTestScope`。
- **脚本**：`build-plan.py`（删 `Assets/LiteGame/Lua/` 内容判定）、`restore-check.py`（去掉 `gen_lua_keys.py` 提示）、`dir-flat-lint.py`（计数后缀删 `.lua`）、`domain-matrix.py`（示例改 `BattleViewPaths.UI`）、`gen-ui-page.py`、`gen_luban.py`、`tests/test_toolbox.py`。
- **有意保留**：3 处 `《Lua 退出专项设计》§4.7` 溯源引用（设计文档名，不是现行机制声明）；`ContentRuntimeAdapters` 的 `lua/ui/UIMain.lua` 签名夹具路径字面量（与签名数据的摘要绑定，改名即断验签，只改注释说明）。

### 9.3 验证证据

| 项 | 命令/方式 | 结果 |
|---|---|---|
| 文档门禁 | `python scripts/gate/doc-lint.py` | **0 violation / 0 warning** |
| UI API 生成 | `python scripts/codegen/toolbox.py ui-api --check` | **CHECK OK：38 methods / 36 spec entries** |
| 生成物 | `UIApiManifest.g.cs` | `SpecMethods`/`GeneratedMethods`/`ManualMethods`/`IndexMethods` 四组，无旧名 |
| Unity 编译 | `recompile_status` | `failed:false`，0 error |
| EditMode（受影响类） | `run_tests --mode EditMode --filter <类>` | Contract **9/9**、ContentRuntimeAdapters **30/30**、ToolchainWiring **7/7**、LTextWiring **5/5**、PageFlowBattleMenu **6/6**、UIPageDefinition **2/2**、UITextRule **7/7**、UIApiSpecGuard **2/2**、BundleDevIsolation **2/2** |
| EditMode 存量红 | `BattleHudMenuEditModeTests` | **6/10**：4 红均为 prefab 节点缺失（改前同红，与注释批无关；归 UI 校验存量批） |
| L1 | `test.py --suite L1 --profile PullRequest` | **PASS 1961 tests** |

### 9.4 遗留

- `BattleHudMenuEditModeTests` 的 4 红（`Page_Lobby`/`Page_Room`/`Page_Settings` 的钮节点缺失）归 UI 校验存量批（与 `UIValidationEditModeTests` 的 84 条同源），不在本批范围。
- spec `lua` 键名仍是数据格式名（schema + 全条目迁移，另批）。
- 历史进度卷与审计归档保留 Lua 字样（按纪律不回改）。


## 第10章 P5 收尾：spec `lua` 键改名 `api`

<a id="ch10-spec键改名"></a>

> **批次**：P5 收尾（2026-11-08）　**状态**：已完成（附验证证据）
> **触发**：第 9 章 9.4 遗留的 spec 数据键名（`lua` 是旧脚本面的格式名，已无机制含义）。

- **spec**：`Assets/LiteGame/Editor/UITools/UIApi/UIApiSpec.json` 的 35 处 `"lua": [` → `"api": [`（36 条 API 名，内容不变）；`note` 改为“生成 `UIBindIndex` 声明 / `UIBindBase` 转发 / 清单”，去掉 shim/dispatch。
- **schema**：`UIApiSpec.schema.json` 的 `"lua"` 属性改 `"api"`；说明改“一条 method = 一个 C# 方法；api 数组 = 该方法暴露的受控 API 名”；`index:false` 描述由“Lua 专属”改“仅基类转发/手工面”。
- **生成器**：`scripts/codegen/gen-ui-api.py` 全部 `method.get("lua")` 改 `method.get("api")`，循环变量 `lua` → `api`，重复名报错改 `duplicate api name`；重生成三产物（input sha256 变化，名集与 `ApiVersion` 不变）。
- **文档**：`Docs/设计文档/客户端/UI系统/UI系统总设计.md` §5.14 的 spec 描述同步为 `api` 数组。

### 10.1 验证证据

| 项 | 命令/方式 | 结果 |
|---|---|---|
| spec/schema JSON | `json.load` | 两份均可解析 |
| UI API 生成 | `python scripts/codegen/toolbox.py ui-api --check` | **CHECK OK：38 methods / 36 spec entries** |
| Unity 编译 | `recompile_status` | `failed:false`，0 error |
| EditMode 守卫 | `run_tests --mode EditMode --filter UIApiSpecGuardTests` | **2/2** |
| L1 | `test.py --suite L1 --profile PullRequest` | **PASS 1961 tests** |
| 文档门禁 | `python scripts/gate/doc-lint.py` | **0 violation / 0 warning** |
| 旧键残留 | 全仓 `"lua":` / `get("lua")` / `methods[].lua` | **0**（历史进度卷、审计归档与迁移设计按纪律保留） |

### 10.2 遗留

- 历史进度/审计归档、`Lua退出专项设计.md` 内仍含 Lua 字样（迁移记录，不回改）。
- 第 9 章 9.4 的“spec `lua` 键名”遗留自本章起关闭。


## 第11章 P5 收尾：删除双驱动（Bound 数据绑定面）

<a id="ch11-删除双驱动"></a>

> **批次**：P5 收尾（2026-11-08）　**状态**：已完成（附验证证据）
> **触发**：代码审计确认 `Bound` 驱动无生产消费者——`BindText<` 在全仓生产代码 0 调用（仅 `UIBindIndex` 声明 + 1 条 EditMode 用例），`MarkDriver(Bound)` 只由 `BindText` 自己调用；其余 30+ 处全是 `Command`。

### 11.1 删除面

| 项 | 处理 |
|---|---|
| `ControlDriver` 枚举（`None/Bound/Command`） | 删除 |
| `UIBindIndex._drivers` 字典 + `MarkDriver` | 删除（30+ 处调用一并删） |
| `UIBindIndex.BindText<T>` | 删除 |
| `UIDataBinder<T>`（`UIWidget.cs` 内） | 删除 |
| `UIBindIndex.WriteTextRaw` | 删除（唯一消费者是 `BindText`） |
| `LTextTypewriterEditModeTests.绑定路径BindText_恒瞬时_不走打字机` | 删除（功能已不存在） |
| 文档 | `UI系统总设计` §“所有权互斥”改为“单一驱动路径 + 文本单写者”；说明 08 §5 同步；`UI逻辑API参考` §3.1 删 `BindText` 行；审计 `类设计` 的 `UIDataBinder` 条目归档 |

**保留**：文本单写者规则（静态 `LTextKeyRef` vs 动态 `SetTextKey`，`ui.contract.text-double-write`）；按钮替换式绑定与 `UnbindAll` 生命周期纪律；`UIBindIndex` 的 `SetText/SetTextKey/SetList/OnButton` 等命令式 API 全部不动。

### 11.2 验证证据

| 项 | 命令/方式 | 结果 |
|---|---|---|
| 残余引用 | 代码/脚本 `BindText<` / `UIDataBinder` / `ControlDriver` / `MarkDriver` | **0** |
| Unity 编译 | `recompile_status` | `failed:false`，0 error |
| EditMode | `run_tests --mode EditMode --filter LTextTypewriterEditModeTests` | **9/9** |
| L1 | `test.py --suite L1 --profile PullRequest` | **PASS 1961 tests** |
| 文档门禁 | `python scripts/gate/doc-lint.py` | **0 violation / 0 warning** |

### 11.3 说明

- 现在控件写入口只有一条：页面逻辑显式调命令式 API；值变化由页面逻辑自己判断（列表用整表替换，文本用 `SetTextKey`/`SetText`）。
- 若未来需要高频值自动刷，载体应是 HUD/表现层的读模型 + 控件级 binder，不恢复到页面索引里做双驱动。


## 第12章 P5 收尾：`OnButtonClick` 改名与 HUD 模块内抽象

<a id="ch12-click改名与HUD抽象"></a>

> **批次**：P5 收尾（2026-11-08）　**状态**：已完成（附验证证据）

### 12.1 API 改名 `OnButton` → `OnButtonClick`

| 旧 | 新 |
|---|---|
| 页面 API `OnButton` | `OnButtonClick` |
| 索引 `BindButton` / `UnbindButton` | `BindButtonClick` / `UnbindButtonClick` |
| spec `api` 名 `onButton` / `offButton` | `onButtonClick` / `offButtonClick` |
| `apiVersion` | 7 → **8** |

- 改动面：`UIApiSpec.json`（baseForward/方法/api 名）、重生成三产物、25 个生产调用点、`UIRuleThresholds.PageNameFirstApis`/`PageEventApis`、`UIPageLogicScanner` 注释、契约对账测试与 fixture、页面/规范/设计/说明文档。
- 改名理由：与 `OnListClick`/`OnTabChanged` 事件族一致；避免与 `UIWidget.OnClick()` 重名；`OnButtonClick` 明确“按钮点击”，不限制未来点击源。
- 契约事件名仍为 `onClick`（不变），只有 C# 受控 API 名/索引名变化。

### 12.2 HUD 模块内抽象

目录 `Modules/Battle/SubModules/HudSubModule/Abstractions/` 新增三件：

| 抽象 | 作用 |
|---|---|
| `LiteFramework.UI.IHudDriver` | 统一 `Tick(float)`；各 HUD 族驱动实现，装配器按列表顺序推进 |
| `LiteFramework.UI.IHudView` | 只约定根件 `RootTransform`；`IBattleHudView : IHudView` 声明战斗件契约 |
| `LiteFramework.UI.HudAssemblerBase<TView>` | 视图绑定 + 驱动列表 + 同帧派发；各族装配器继承 |
| `BattleHudModel` | 战斗读源聚合：`Predicted`/`Public`/`Respawn` + `LocalEntityId`/`Quality`/`Feed`；由 `BattleViewRig` 在装配点构造 |

- `BattleHudAssembler : HudAssemblerBase<IBattleHudView>`，只依赖 `IBattleHudView` + `BattleHudModel`；`AttachRespawnHud` 走 `BattleHudModel.Respawn`。
- 各驱动（准心/面板/排行/击杀/网络/阵亡）实现 `IHudDriver`；`Dispose*` 同步从列表移除。
- 通用层只放驱动拍点/视图根/装配器生命周期；`BattleHudModel` 保持战斗域私有普通类，不抽通用 Model 接口。
- 边界不变：HUD 仍是战斗表现域，不登记 `#uiform`、不走 `UIService` 导航；共用 UI 原语（prefab/Canvas/TMP/UIFx/LText），不共用页面生命周期。

### 12.3 验证证据

| 项 | 命令/方式 | 结果 |
|---|---|---|
| UI API 生成 | `python scripts/codegen/toolbox.py ui-api --check` | **CHECK OK：38 methods / 36 spec entries** |
| Unity 编译 | `recompile_status` | `failed:false`，0 error |
| HUD EditMode | `BattleHud` 5/5、`BattleKillFeed` 6/6、`BattleRankboard` 8/8、`BattleNetworkHud` 2/2、`BattleRespawnHud` 4/4、`BattleCrosshair` 8/8 | 全绿 |
| UI 改名面 EditMode | `UIPageLogicScanner` 5/5、`UIContractReconciliation` 9/9、`UIBindingTools` 12/12、`UIApiSpecGuard` 2/2、`UIToolchainWiring` 7/7 | 全绿 |
| L1 | `test.py --suite L1 --profile PullRequest` | 本批改动前 **PASS 1961**；当前工作区被并行 Meta/Procedure 重构的两条架构守卫红阻断（`MetaCall`/`MetaFailure` 等搬出 `Abstractions`），非本批改动 |
| 结构门禁 | `python scripts/gate/dir-flat-lint.py` | 411 dirs / 20 baselined / no growth |
| 文档门禁 | `python scripts/gate/doc-lint.py` | **0 violation / 0 warning** |
