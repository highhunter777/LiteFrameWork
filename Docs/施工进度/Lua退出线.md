# Lua退出线（Lua 从客户端整体退出）

> **定位**：Lua 退出迁移（[Lua 退出专项设计](../设计文档/架构/Lua退出专项设计.md)）的施工线：P0 冻结与死重清理 → P1 七页 C# 化 → P2 运行时与框架摘除 → P3 内容/构建/程序集/门禁 → P4 表结构与 `PageIds` 收口 → P5 文档面收口。本卷按批成章。
> **章法**：一卷一条线；每章＝一批记录（标题、正文、证据），章首保留 `原文件` 行供旧引用查证（本卷为首建卷，均无旧文件）。
> **状态图例**（全目录统一）：`已完成（附验证证据）` / `进行中` / `未开始` / `阻塞（附原因）`。

## 章节索引

| 章 | 标题 | 原文件 |
|---:|---|---|
| 1 | P0 冻结与死重清理 | — |
| 2 | P1-0 页面寻址收口（PageIds） | — |

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
