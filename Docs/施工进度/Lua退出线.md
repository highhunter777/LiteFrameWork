# Lua退出线（Lua 从客户端整体退出）

> **定位**：Lua 退出迁移（[Lua 退出专项设计](../设计文档/架构/Lua退出专项设计.md)）的施工线：P0 冻结与死重清理 → P1 七页 C# 化 → P2 运行时与框架摘除 → P3 内容/构建/程序集/门禁 → P4 表结构与 `PageIds` 收口 → P5 文档面收口。本卷按批成章。
> **章法**：一卷一条线；每章＝一批记录（标题、正文、证据），章首保留 `原文件` 行供旧引用查证（本卷为首建卷，均无旧文件）。
> **状态图例**（全目录统一）：`已完成（附验证证据）` / `进行中` / `未开始` / `阻塞（附原因）`。

## 章节索引

| 章 | 标题 | 原文件 |
|---:|---|---|
| 1 | P0 冻结与死重清理 | — |

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
