# Lua 退出专项设计

> **版本**：v1.0（2026-11-08）
> **来源**："Lua 热更废除"裁决（2026-11-08）＋ 全仓 Lua 面实测清点（运行时／适配层／表链／工具链／内容发布／构建门禁／测试／文档八面，逐文件核对行号）。
> **定位**：裁决"Lua 从客户端运行时、内容热更、表链与工具链中整体退出"的目标形态、替代载体、迁移顺序与验收判据。回答"删哪些、按什么顺序删、每步凭什么判定可交"。
> **不做的事**：不裁决 bundle/config 内容热更与 HybridCLR 的形态（归[热更与内容发布专项设计](../客户端/内容/热更与内容发布专项设计.md)）；不裁决 UI 运行时、绑定、导航与生命周期契约（归[UI 系统总设计](../客户端/UI系统/UI系统总设计.md)）；不重划构建哈希闭包的源集（只承受表结构派生出的常量变化，归[规范 02](../../规范/02-代码与修改规范.md) 红线 1）；不记施工状态（归[Docs/施工进度](../../施工进度/README.md)）。
> **冲突裁决**：本文只就"Lua 是否存在于产品面"裁决，其余一律让位。与[施工进度·客户端重构线](../../施工进度/客户端重构线.md)"维持 `Assets/LiteGame/Lua`……从待办改记为终态"的旧裁定**冲突**——以本文为准，旧裁定由后批进度章按"后批修正前批"纪律撤销，不回改前章。与[热更与内容线 §档位表](../../施工进度/热更与内容线.md) 中"Lua 脚本整套激活 / `xLua hotfix` 预埋面"冲突——该行已被 2026-11-08 HybridCLR 改判取代，随 P5 一并修。
> **关联**：[说明 08 UI 与页面逻辑层](../../说明/08-UI与页面逻辑层.md) · [说明 02 架构总览与程序集分层](../../说明/02-架构总览与程序集分层.md) · [说明 09 内容热更与资源管线](../../说明/09-内容热更与资源管线.md) · [说明 14 代码地图](../../说明/14-代码地图与根目录.md) · [UI 编辑器工具链专项设计](../客户端/UI系统/UI编辑器工具链专项设计.md) · [UI 逻辑 API 参考](../客户端/UI系统/UI逻辑API参考.md) · [资源收集目录专项设计](../客户端/内容/资源收集目录专项设计.md) · [规范 05 UI 制作](../../规范/05-UI制作.md)

## 1. 结论

**六条裁决**（下文 D1–D6 即这六条，正文一律按编号引用）：

| # | 裁决 | 一句话 |
|---|---|---|
| **D1** | 页面逻辑回到 C# | 每个产品页一个 `UIBindBase` 派生组件挂在 prefab 根；框架"prefab 自带 C# 逻辑优先"的解析已就位，不再有逻辑工厂与注册表 |
| **D2** | 页面寻址＝`int formId` | 删除字符串页键（`UI.Page_X`）；页面名不是唯一键（`Tmp_Dialog` 占 201/206 两行），`formId` 本就是导航/模态/目录的既有主键 |
| **D3** | 数据面＝强类型 `IUIData` 记录 | 删除 `PageData` 键值树、`IPageDataFactory`、`LuaUIData`；页面数据由定义记录承载，不按键名猜 |
| **D4** | 页面服务＝装配注入 | `Bridge.*` 静态门面全部退出（框架五件 `data/ui/content/text/battle` ＋ 产品三件 `meta/settings/room`）；框架注入 `IConfirmDialog` 与语言服务，产品端口经 `PageContext` 注入 |
| **D5** | 热更边界重划 | `lua/` 前缀、Lua 候选沙箱、Lua 健康探针整段删除；内容热更只余 `bundle/`＋`config/`，**逻辑热更由 HybridCLR 承接**（`LiteGame.Modules.UI` 已在 hot 程序集清单） |
| **D6** | 表与生成链收口 | `#uiform.lua_path`、`#strategy.lua_path`、`#contententry.entry` 三列删除；`LuaKeys` → `PageIds`（页面目录生成器产出，与 `UIPageCatalog` 同源；**P1-0 先出，P4 删旧键**）；Luban 不再产出 Lua 表 |

**责任与依赖**：

| 项 | 值 |
|---|---|
| Owner | UI 域（页面逻辑与寻址、契约校验）＋ 框架域（`ILuaHost`/`UIBindBase`/页面服务注入）；内容与构建面由内容线兼管 |
| 依赖 | [UI 系统总设计](../客户端/UI系统/UI系统总设计.md) 的页面模型；[热更与内容发布专项设计](../客户端/内容/热更与内容发布专项设计.md) 的 `code/`（HybridCLR）与 `bundle/`/`config/` 三段；[资源收集目录专项设计](../客户端/内容/资源收集目录专项设计.md) 的收集组归属 |
| 输入 | `#uiform` / `#strategy` / `#contententry` 三张表；`UIPageManifest.json`；24 份 `Contracts/*.contract.json`；7 个产品页 prefab |
| 输出 | 无 Lua 的客户端运行时与包体；C# 页面逻辑件；`PageIds.g.cs`；无 `lua/` 段的内容清单；重写后的文档面 |
| 失败语义 | 页面开关失败面不变（`UIOpenFailure` 六值）；页面逻辑异常经 `UIForm.SafeCall` 隔离为单页降级，不炸进程（现行语义，不改）；生成物不一致由 `--check` 门禁拦，不在运行期兜 |
| 容量/性能预算 | 包体：不再携带各平台 `xlua` 原生库与 `defaultpackage_assets_litegame_lua*.bundle`（绝对值按平台实测回填）；帧成本：页面 `OnUpdate` 由 `UIForm` 直达 C#，跨语言调用归零（Lua 时代每帧／每滚动帧都要过 VM）；启动：少一次全量 Lua 字节装载与 `main.lua` 执行 |
| 测试层级 | L1（纯逻辑与纪律扫描）＋ L2（EditMode/PlayMode）＋ 出包冒烟与双人对局（[说明 11](../../说明/11-构建与本地联测.md)、[说明 12](../../说明/12-云侧部署与联机测试.md)） |

## 2. 退出前提

"删 Lua"不是在拆一个活着的能力，而是在拆一层**已经空了**的脚手架。八条实测证据（拿掉任何一条，本方案的性价比都要重算）：

| # | 前提 | 证据 | 后果 |
|---|---|---|---|
| P1 | 逻辑桥早已端口化 | `ILuaHost`（`Packages/com.litegame.framework/Runtime/Services/Scripting/Abstractions/ILuaHost.cs`）、`IUILogicFactory`、`IPageDataFactory`、`ILuaRegistryFiller` 四个端口把适配器挡在产品模块外 | 摘除面集中在装配根与适配层，不动业务模块骨架 |
| P2 | 框架已优先认 prefab 自带 C# 逻辑 | `UIFormLogicBinder.Resolve` 先 `root.GetComponentInChildren<IUIFormLogic>(true)`，命中即返回；`LuaPath` 只是第二条路 | **可逐页迁移**：某页挂 C# 组件即生效（不清键、不删文件），其余页不动 |
| P3 | 三个注册表只有 UI 一支有生产消费者 | `IStrategyLuaRegistry` 只出现在 `RegistryFiller`/`LuaRegistryRefillService`/`LuaDevReload`/`AppComposition`；`IContentLuaRegistry` 同型，且 `Bridge.content` 全仓 Lua 零调用 | 内容/策略两支是纯负债，直接删，无行为影响 |
| P4 | 生成的 Lua 表零 require | `Assets/LiteGame/Lua/Cfg/*.lua` 15 份无任何 `require("Cfg…")`；全仓 Lua 只 require `Core.class` | 约 29 KB 死数据当前**随 `lua` 收集组进包** |
| P5 | `Bridge.battle` / `Bridge.content` 零消费 | 7 个页面里 `Bridge.*` 实测分布：Lobby（meta 8／text 6／ui 4／data 1）、Login（meta 5／text 1／ui 2）、Records（meta 3／ui 1）、Result（ui 1）、Room（meta 2／text 1／ui 1／room 12）、Settings（meta 4／text 18／settings 37／ui 2）、ButtonDemo（0）——`battle`/`content` 均 0 | 门面收敛为"页面直接用端口"，无功能损失 |
| P6 | Lua 不在构建哈希闭包内 | `scripts/codegen/gen-build-hash.py` 的 `SHARED_TARGETS` / `SHARED_LINK_FILES` 均不含 `Assets/LiteGame/Lua` 与 `LuaKeys` | 删脚本、删生成物**不**触发两端哈希同步 |
| P7 | **但表结构在闭包内** | 同一脚本的 `SHARED_TARGETS` 含 `Assets/GameData/Generated`（Luban 生成读码面） | 删列＝改读码面＝**两端同步事件**（见 §6 P4） |
| P8 | 服务端零引用 | `Server/**` 对 Lua/XLua 实测 0 处命中 | 无需服务端改造，只有哈希同步动作 |

## 3. 现状清点

| 面 | 规模 | 现位置 | 处置批 |
|---|---|---|---|
| 运行时宿主 | 4 件（`LuaComponent`/`LuaPreloader`/`EventBridge`/`CandidateLuaValidator`）＋ `ILuaHost`＋`LuaScriptsHealthProbe` | `Packages/com.litegame.framework/Runtime/Adapters/XLua/Lua/`、`…/Services/Scripting/Abstractions/` | P2 |
| 注册表骨架 | `ILuaRegistry<T>`/`LuaRegistry<T>`/`RegistryFillReport` | `Packages/…/Runtime/Core/DI/` | P2b |
| 产品适配层 | **20 个 `.cs`**：`Bridge`／`MetaBridge` 门面、三注册表、`RegistryFiller`、`LuaBehaviourAdapter`（257 行）、`LuaListRows`、`LuaPageDataFactory`、`ProductBridges`、`RoomBridge`、`LuaRegistryRefillService`、两个生成文件 | `Assets/LiteGame/Adapters/LuaBridge/**`、`Assets/LiteGame/Modules/Meta/MetaBridge.cs` | P2 |
| 产品端口 | `ILuaProductBridge`、`IUILogicFactory`、`ILuaUIBinding`、`IPageDataFactory`、`ILuaRegistryFiller`、`LuaHostSegment`、`LuaLayout`、`BattleLuaFacts` | `Assets/LiteGame/Core/Lua/`、`Modules/UI/Abstractions/`、`Modules/Procedure/Abstractions/`、`Modules/Composition/`、`Modules/Shared/` | P2 |
| 脚本 | **26 个 `.lua`／215.8 KB**：7 产品页（约 800 行／37 KB）、`class.lua`、`main.lua`、`Tests/UITestPage.lua`、`Cfg/` 15 份（约 29 KB，零 require）、`LuaPanda.lua`（147 KB，零 require、零 C# 引用） | `Assets/LiteGame/Lua/**` | P0（死件）／P1（页） |
| 表与生成物 | 3 个 Lua 列（`uiform.lua_path`——表内注释标为必填、实际 201/206 等模板行为空；`strategy.lua_path`；`contententry.entry`——后两表分别 1 行空值与 0 行）；`LuaKeys.g.cs`（7 个 UI 常量）；`UIApiShim.g.cs`（33 个门面的 Lua 模板内嵌 C# 常量）；`LuaBehaviourAdapter.Dispatch.g.cs` | `Luban/Data/*.xlsx`、`Assets/GameData/Generated/`、`Assets/LiteGame/Modules/Shared/Generated/`、`Adapters/LuaBridge/Generated/` | P0／P4 |
| 工具链 | `gen_luban.py` 四档发布（含 `lua`、`lua-keys`）、`Luban/gen_lua_keys.py`、`gen-ui-api.py` 两档 shim/dispatch、`gen-ui-page.py` 的 `LuaPath`、`_uiform_row.py` 的 `COL_LUA`、`UIPageRegistration` 的 Lua 骨架步、`UIPageScaffold.LuaDir`、`UILuaReferenceScanner`、两条契约规则、两条文本规则、`LuaDevReload`/`LuaDevRefill`、`toolchain.json` 六段、`EditorToolHandlers` 两个 action | `scripts/codegen/`、`Luban/`、`Assets/LiteGame/Editor/UITools/`、`scripts/gate/toolchain.json` | P2／P3 |
| 内容与发布 | `ReleaseLayout.LuaPrefix`/`LuaScripts`、`ICandidateLuaSource`/`FileSystemLuaCandidateSource`、`CandidateScriptSet`、`gen-candidate.py` 的 `lua/**`、YooAsset **Lua 收集组**（`BundleCollectorSetting.asset:56-68`，10 组之一）、`LuaTextAssetImporter`（ScriptedImporter）、`LiteGameIgnoreRule` 的 Lua 放行分支 | `Packages/…/Services/Content/`、`scripts/content/gen-candidate.py`、`Assets/BundleCollectorSetting.asset:56-68`、`Assets/LiteGame/Editor/CollectRules/` | P3 |
| 构建与包体 | xLua 源码 183 文件／1.5 MB＋`Assets/ThirdParty/Plugins/**` 各平台 `xlua` 原生库（`x86_64/xlua.dll`、`xlua.bundle`、Android 三 ABI 等）＋`Tools/XLuaGenerate.exe`/`XLuaHotfixInject.exe`；`GenConfig.cs` 的 `[CSharpCallLua]` 白名单；`HybridCLRSettings.asset` 与 `HybridCLRTool.cs` 的 hot 程序集清单含 `LiteGame.Adapters.LuaBridge`；`restore-check.py` 把 `xlua.bundle` 当**必需件**；`player-smoke.py` 的 Android ABI 理由写死 xLua | 见左列 | P3 |
| 门禁与测试 | `DisciplineScanner` R12 边界表一条（XLua 适配器目录 ＋ LuaBridge 目录）；架构/白名单/作弊边界/作用域/页面定义/任务观测六个守卫测试；`Assets/Tests` **49 个文件带 Lua 字样（约 25 个有真实语义）**；`Tests/**` 的 `LuaRegistryTests`（9 例）与 `CandidateScriptSetTests`（约 20 例）；`scripts/tests` 4 个文件 | 见左列 | 随批 |
| 文档 | **96 篇／1054 行命中**：16 篇主题级、43 篇零星、待办总览 23 行、施工进度 21 卷 513 行、审计 14 文件 31 行 | `Docs/**` | P5 |

## 4. 目标形态

### 4.1 页面逻辑与解析（D1）

- **载体**：`Assets/LiteGame/Modules/UI/Pages/Screens/<域>/Page_<Name>Logic.cs`（域分组，单目录 ≤5 文件），类型 `public sealed class Page_<Name>Logic : PageLogicBase`；组件挂在对应 prefab 根（与 `Canvas` 同层）。`PageLogicBase` 继承框架 `UIBindBase`，只做两件事：把 `PageContext` 转成强类型端口、提供行表构造件的便捷入口。
- **目录名受守卫约束**：业务域子目录不得取 `Logic`/`View`/`Presentation`/`Domain`（`DomainBoundaryTests.业务域子目录必须按职责命名` 拦），且单目录直接代码文件 ≤5（红线 15）——所以按页进子目录，不要平铺。
- **解析**：`UIFormLogicBinder.Resolve` 收敛为"prefab 自带 → 可选的 `resolver`（仅测试用）→ `NullUIFormLogic`"，删除 `LuaPath` 分支；`UISegment` 给 `UIService` 传 `logicResolver: null`。
- **API 面**：页面用的仍是`UIBindBase`/`UIBindIndex` 的受控 API（`GetControl`/`OnButtonClick`/`SetTextKey`/`SetVisible`/`SetList`/`SetDropdownValue`/`OnTabChanged`/`Pulse`…），与 33 个 `self.ui` 门面一一对应，**语义不降级**；区别只是编译期可查、可重构、可断点。
- **模板页**（`Tmp_*`）与测试载具不挂逻辑组件，落 `NullUIFormLogic`——维持现行"无逻辑页可独立打开"的行为。

### 4.2 寻址与目录（D2）

- **寻址分两段收口，顺序是硬的**：C# 侧（`PageFlow` 与预载期模态登记）在 P1-0 先切到 `PageIds` 的 `int`；字符串键 `PageDefinition.LuaPath`、`IPageDirectory.Resolve(string)`、`UIPageResolver` **留到 P2a 才删**——未迁移的 Lua 页面之间仍靠它互相导航（`Bridge.ui.Show("UI.Page_Settings")` 一类共 11 处），先删＝页面链当场断。
- 新增生成物 `Assets/LiteGame/Modules/UI/Generated/PageIds.g.cs`：由 `gen-ui-page.py` 与 `UIPageCatalog.g.cs` **同源同次运行**产出，形如 `public const int Page_Login = 11;`；重名行按 `<名字>_<id>` 消歧（`Tmp_Dialog_201`/`Tmp_Dialog_206`），产品页名保持 `Page_<语义>` 形态可 grep。
- `PageFlow` 的 `LuaKeys.UI.Page_Login` 全量改为 `PageIds.Page_Login`；`OpenLanding`/`ShowLogin`/`CloseRoomIfOpen`/`OnMatchEnded`/`OpenBattleSettings` 的签名与语义**保持不变**（仍是 int 进导航单写者）。
- 删除 `LuaKeys`（`Modules/Shared/Generated/`）与 `gen_lua_keys.py`；`RegistryFiller.KnownUIKey` 这类"启动期路径自检"随 P2a 消失，其价值由 4.7 的"登记页必须有逻辑件"校验承接。

### 4.3 数据面与行表（D3）

- 删除 `Core/UI/PageData.cs`、`Modules/UI/Abstractions/IPageDataFactory.cs`、`LuaPageDataFactory`、`LuaUIData`。
- 页面数据改为**按页定义记录**（`readonly struct`/`sealed class` 实现 `IUIData`），与本页逻辑件同目录（`Pages/<Page>/`）：
  - `ResultPageData(IReadOnlyList<MatchSettlementRow> rows, int localPlayerId)` —— 承载 `PageFlow.ResultData` 现有的 `hasPlayer/kills/deaths/rank/reason/ranks` 语义，改为强类型字段；
  - `SettingsPageData(bool battleExit, string exitKey = null)` —— 承载 `.Set("battleExit", true)`；
  - `RoomPageData(string roomId)` —— 承载换房时传的 `{ roomId }`。
- `PageFlow` 直接构造这些记录并传给 `_nav.GoAsync/ReplaceAsync`；`_data` 依赖与 `ProcedureSegment` 的 `IPageDataFactory` 取件一并删除。
- 列表行：以 `UIListRowSet : IUIListRows`（`Modules/UI/Bind/`）替代 `LuaListRows`，提供 `Row(key).Text(name, text).KeyText(name, key, args…)` 链式构造；行文案 key 与参数个数仍是契约面可对账的数据。

### 4.4 页面服务注入（D4）

页面是 prefab 组件，构造期无 DI；两条注入缝分开：

| 缝 | 承载 | 注入点 | 页面可见面 |
|---|---|---|---|
| 框架缝 | `IConfirmDialog`、`ILocalizationService` | `UIFormLogicBinder` 已有 `localeProvider`/`themeProvider` 同款（新增 `confirmProvider`），`UIForm` 上新增 `Confirm` 属性 | `UIBindBase.ShowConfirmAsync(title, message, ok, cancel)`、`UIBindBase.Locale`（`BindLocale` 时留存） |
| 产品缝 | 联机门面、房会话、地图目录、导航 | `UIForm.PageContext`（`object`，框架不解释类型）由 `UIFormLogicBinder` 从 `Func<PageDefinition, object>` 取 | `PageLogicBase.Ctx`（`IPageContext`）上的 `Meta`/`Room`/`Maps`/`Nav` |

- `IPageContext`（`Modules/UI/Abstractions/`）的成员**全部是端口或延迟工厂**，避免装配段顺序耦合（UI 段早于 Meta/Battle 段）：`Meta`、`Room` 允许为 null（未配置部署/未进房），页面按"能力缺失显性拒绝"处理，与现行 `Bridge.meta.IsConfigured()` 判据同口径。
- `Bridge`、`MetaBridge`、`RoomBridge` 的 Lua 装订方法全部删除；`MetaBridge` 里的业务逻辑（异步收口、失败原因映射、地图名解析、时间格式化）**不删**，迁进 `IMetaSession` 的实现（`MetaLoginService` 侧的适配件），由页面直接 `await`——Lua 时代的"回调 + 恰一次"由 `UniTask` + `CancellationToken` 承接，页面 `OnHide` 取消在途请求。
- `ProductSettings.Current` 是既有单例读口，设置页直接用，不新增静态状态（红线 6：**不新增** `public static` 可变状态；`Bridge` 这类静态门面随本方案消失，不是新增）。

### 4.5 热更与内容面（D5）

| 段 | 现状 | 目标 |
|---|---|---|
| `bundle/` | YooAsset 内容包 | 不变 |
| `config/` | 表 `.bytes` | 不变 |
| `code/` | HybridCLR hot 程序集（规划中） | 不变；**承接原 Lua 的"页面逻辑可热更"承诺**（`LiteGame.Modules.UI` 已在 hot 清单） |
| `lua/` | 脚本整套激活 + 候选沙箱 + 健康探针 | **删除**：前缀、`CandidateScriptSet`/`ICandidateLuaSource`/`FileSystemLuaCandidateSource`、`LuaScriptsHealthProbe`、`gen-candidate.py` 的 `lua/**` 受控内容 |

- `CandidateLuaValidator` 的**沙箱/语法/依赖闭包校验能力**不随 Lua 消失而消失：HybridCLR 接入线已规划把 `code/` 候选校验落在同一位置（"通用候选校验"）。本方案的边界是——**不在本方案里实现程序集校验器**，只保证 Lua 专属部分（`.lua` 语法、`require` 图、Lua 沙箱）随批删除，命名与落点由 HybridCLR 线裁决。

### 4.6 表与生成链（D6）

- 表：`#uiform` 删 `lua_path` 列；`#strategy` 删 `lua_path` 列（唯一行本就为空）；`#contententry` 当前 0 行（表壳仍在）——表可留（内容条目仍是玩法概念），只删 `entry` 列。
- 生成链：
  - `gen_luban.py` 删 `lua` 与 `lua-keys` 两档发布目标与对应两次子进程；
  - `Luban/gen_lua_keys.py` 删；
  - `gen-ui-api.py` 只保留 `UIApiManifest`/`UIBindBase.Api`/`UIBindIndex.Api` 三面，删 `UIApiShim` 与 `LuaBehaviourAdapter.Dispatch` 两档；
  - `gen-ui-page.py` 删 `LuaPath` 列读取与输出，**新增** `PageIds.g.cs` 输出（与 catalog 同一次渲染、同一次 `--check`）；
  - `_uiform_row.py` 删 `COL_LUA` 与"生成 `UI.<page>`"分支，列序号整体左移（**与 xlsx 改列必须同批**）。
- 生成物新鲜度守卫（`scripts/tests/test_generated_artifacts.py`）的产物表同步：`UIApiShim`/`Dispatch`/`LuaKeys` 三行删除，`PageIds.g.cs` 新增。

### 4.7 工具链与校验

| 工具/规则 | 现状 | 目标 |
|---|---|---|
| `ui.page-register` | 步骤④"写 Lua 骨架" | **不生成逻辑文件**（见 §5.3）；步骤收敛为"表 → codegen → 清单 → 契约"，失败面 `UIPageRegisterFailure` 去掉 `Lua` 项 |
| `ui.page-scaffold` | 不写 Lua，但持有 `LuaDir` 常量供扫描器解析 | 删 `LuaDir` |
| `UILuaReferenceScanner` | 从 `.lua` 源提取 `self.ui:Api("名字")` | 改为 `UIPageLogicScanner`：从 C# 页源提取 `OnButtonClick("Btn_X"…)`/`SetTextKey("Txt_X"…)`/`GetControl<T>("X")` 等受控 API 的名字字面量，输出结构不变（`Api/Name/Args/Line`） |
| `ContractReferenceRule` / `ContractTextDoubleWriteRule` / 文本两条规则 | 以 Lua 源为扫描对象 | 同上换扫描对象；判定口径（越界 Error、required 未消费 Warning、静态文案双写 Error）**不变** |
| `codegen.lua-reload` / `codegen.lua-refill` / `codegen.xlua` | 三个工具 id + 菜单项 + handler | 删除（`toolchain.json` 与 `EditorToolHandlers` 同批） |
| 新增守卫 | — | `PageLogicRule`：逻辑件存在 ⇒ prefab 根必须挂实现 `IUIFormLogic` 的组件（否则页面静默落 NullLogic）；重复实现报错；迁移期不要求每页都有件（P2a 收紧）。替代 `LuaKeys` 自检 |
| 迁移期棘轮（仅 P0–P3 生效） | — | `lua-freeze` 检查：产品面新增 `.lua` 或新引用 `XLua`/`LuaTable`/`Bridge.` 即失败；P5 后转为全仓零命中检查 |

### 4.8 构建与包体

- 删 `Assets/ThirdParty/XLua/**`、`Assets/ThirdParty/Plugins/**/xlua.*`、`Assets/ThirdParty/Plugins/Android/libs/*/libxlua.so`、`Tools/XLuaGenerate.exe`/`XLuaHotfixInject.exe`/`XLua.Mini.dll`/`xlua.dll`/`libxlua.so`。
- `GenConfig.cs`（`[CSharpCallLua]` 白名单）删除；`ProjectSettings/HybridCLRSettings.asset` 与 `HybridCLRTool.cs` 的 hot 清单去 `LiteGame.Adapters.LuaBridge`（**两份清单同改**，顺手登记为单源债的收敛点）。
- `restore-check.py` 的 `xlua.bundle` 必需项删除；`player-smoke.py` 的 Android ABI 理由改写（`arm64-v8a` 仍保留为发布 ABI 判据，但不再以 xLua 为理由）。
- 预期收益：包体不再携带 `xlua.dll` 各平台副本与 `defaultpackage_assets_litegame_lua*.bundle`；编辑器侧少一个 ScriptedImporter 与一套 1.5 MB 源码。

## 5. 关键取舍

### 5.1 寻址为什么用 `int` 而不是页面名

`#uiform` 允许**一个 prefab 多行**：`Tmp_Dialog` 同时是 201（Layer 3）与 206（Layer 1），`UIPageCatalog` 里两行 `Name` 都是 `"Tmp_Dialog"`。任何"以页面名为键"的方案都必须先解决这个歧义，而 `formId` 本来就是 `UIService`/`UINavigationController`/`RegisterModal`/`IPageCatalog.Get` 的主键。选 `int` 是**顺着现有主键收口**，不是新增抽象；代价是常量表要重新生成（`PageIds.g.cs`），收益是删掉 `UIPageResolver` 与"字符串拼错才在运行期报错"的失败面。

### 5.2 页面服务：注入，不保留静态门面

`Bridge`/`MetaBridge` 是 `public static` 门面 + 静态字段（容器取件的替代品）。保留它们能给 C# 页面省事，但要新增静态可变状态，与红线 6 冲突，且 PlayMode 测试无法并行装两套。注入方案的成本是框架三处小改（`UIForm` 属性、`UIFormLogicBinder` 一个 provider、`UIBindBase` 两个访问器），收益是页面逻辑可单测、装配顺序不靠"谁先 Bind"。

### 5.3 登记不生成 C# 骨架

Lua 时代登记会生成 `Page_X.lua` 骨架，因为脚本是解释执行的、缺文件只在运行期炸。C# 版本若生成骨架，就要"生成 → 进编译 → 可能与人改冲突 → 需要 `--check` 判新鲜度"，收益很低。改为**不生成 + 一条校验规则**（4.7 `PageLogicRule`）：缺逻辑件在 `ui.validate` 当场报错，比"生成了但忘挂 prefab"更早暴露。

### 5.4 表列：删除，但要接受一次哈希事件

保留空列可以完全避开构建哈希（P6），但会让 `lua_path` 这个已无消费者的列长期存活，还会诱使后人"再挂一层脚本"。选择删除（D6），并把代价显式隔离成 P4：改表 → `Luban/gen.bat` → `toolbox.py build-hash` → 两端按发布顺序同步。若本轮排期不方便动哈希，可把 P4 独立延后，P0–P3 全部不需要它（届时列保留、值留空即可）。

### 5.5 契约对账扫描器换语法，不换判据

`UILuaReferenceScanner` 的价值不在"读 Lua"，而在"把页面源码里的名字字面量与契约面做对账"。迁到 C# 后，受控 API 名单（`UIRuleThresholds.LuaNameFirstApis`）改名为 `PageLogicNameFirstApis`，匹配形态由 `self.ui:Api("名")` 改为 `Api("名"…)`（含 `GetControl<T>("名")` 与 `_index.` 前缀变体），**规则严重度与报文口径不动**——这样 24 份契约与既有例外清单不需要重评。

### 5.6 `lua/` 段删除而不是改名

HybridCLR 的 `code/` 已经是"代码候选"的落点。把 `lua/` 改名为 `script/` 会长期存在两种"代码候选"语义，且 `ReleaseLayout` 的模块名派生规则（`lua/UI/Page_Login.lua → UI.Page_Login`）只服务 Lua 的 require 命名空间，对程序集无意义。直接删段，`code/` 的模块名规则由 HybridCLR 线自行定义。

## 6. 迁移顺序

每批都要求"可独立编译、可独立回滚、门禁全绿"。前四批不依赖 P4（表列可留空）。

### P0 冻结与死重清理（无行为变化，约 1–1.5 人日）

- **目标**：先止血（不再有人往 Lua 里加东西），再删已确认零消费者的件。
- **改动面**：`LuaPanda.lua`、`Cfg/*.lua`（并删 `gen_luban.py` 的 `lua` 档）、`UIPageScaffold.LuaDir`（与 `LuaLayout.LuaDir` 重复的第二个常量）、`IContentLuaRegistry`/`IStrategyLuaRegistry` 及其实现与 `RegistryFiller` 两条 fill 行、`Bridge.content`/`Bridge.battle` 装订与 `BattleLuaFacts`、`LuaRegistryRefillService` 的策略/内容两支、对应 L1 用例；新增 `lua-freeze` 棘轮。
- **保持绿**：`UIApiShim` 与 dispatch 不动（33 门面仍有 UI 页在用）；`LuaKeys` 保留（`PageFlow` 还依赖）。
- **退出条件**：`Assets/LiteGame/Lua` 只剩 `main.lua`/`Core/class.lua`/7 页/`Tests`；全仓 `Bridge.content`/`Bridge.battle` 零命中；L1 绿。

### P1 页面 C# 化（关键路径，约 6–9 人日）

- **目标**：7 个产品页逐个换成 C# 逻辑组件，**每页一次可交付**。
- **每页固定动作**：写 `Pages/<Page>/Page_<Name>Logic.cs` → prefab 根挂组件（走 `unity-pipeline`，红线 4）→ 该页验收点走真实操作路径（红线 13）。
- **目录与守卫**：业务域子目录禁 `Logic`/`View`/`Presentation`/`Domain`（`DomainBoundaryTests`），单目录直接代码文件 ≤5（红线 15）。
- **不删该页 `.lua`、不清 `lua_path`**：解析器「prefab 自带 C# 逻辑优先」，组件一挂即接管，旧 Lua 只是还注册着、不再被执行。反过来，清空键会打断**尚未迁移**的页面——它们仍在 `Bridge.ui.Show('UI.Page_X')` 形态的调用里按字符串跳转。页面 Lua 面的清理由 P2a 一次性完成（见下）。
- **P1-0 前置**（第 1 页之前）：`PageIds.g.cs` 与 `PageFlow` 的 int 切换——**只切 C# 侧，不删 resolver**。
- **P1-1 载体**（与第 1 页同批，避免「待接线」悬空——红线 10）：`PageLogicBase`＋`IPageContext`（4.4）、`UIListRowSet`（4.3）、`UIPageLogicScanner`＋`PageLogicRule`（4.7）。
- **建议顺序（按依赖面由小到大）**：`Page_ButtonDemo`（0 依赖，验证载体）→ `Page_Result`（ui 1）→ `Page_Records`（meta 3）→ `Page_Login`（meta 5）→ `Page_Room`（room 12，接 `IRoomSessionFace` 事件）→ `Page_Lobby`（meta 8 + data 1，轮询 + 列表）→ `Page_Settings`（settings 37 + text 18，最大）。
- **逐页验收点**（须与现状等价）：Login 游客登录三态与版本号；Lobby 房间轮询节拍/在途锁/建房下拉/气泡；Room 席位名单/房主开局/被拒 Toast；Result 名次表与本机摘要；Settings 五个页签/即改即生效/关闭落盘/对局中置灰/退场确认；Records 战绩列表与结束原因文案；ButtonDemo 纯演示。
- **退出条件**：7 页全部 C# 且 `Assets/LiteGame/Lua/UI/` 为空；`Bridge.ui`/`Bridge.meta`/`Bridge.settings`/`Bridge.room` 在页面侧零引用；PlayMode 的"真 Lua 页"用例改写为"真 C# 页"用例（`LuaPagePlayModeTests` 4 例语义保留：冷开/复用不重跑 OnInit/Shutdown 释放租约/在途取消）。

### P2a 页面 Lua 面收口（约 1–2 人日）

- **目标**：把七个页面的脚本面一次性清干净——它必须是**单次原子动作**（没有「一页一清」的中间态）。
- **动作**：删 `Assets/LiteGame/Lua/UI/*.lua` → 清 `#uiform.lua_path` 与 `UIPageManifest.luaPath` → 重生成契约/catalog/`LuaKeys` → 删 `PageDefinition.LuaPath`、`IPageDirectory.Resolve(string)`、`UIPageResolver`、`Bridge.ui`、`Bridge.Data.ResolveUIFormId`、`RegistryFiller` 的页路径填充（此时填 0 行）。
- **退出条件**：编辑期页面链（登录 → 大厅 → 房间 → 结算 → 大厅 → 设置）按真实操作路径走通；`LuaKeys` 零读者；`Bridge` 只剩 `data`/`text`。
- **依赖**：必须 P1 全绿之后，否则未迁移页面无法导航。

### P2b 运行时与框架摘除（约 3–4 人日）

- **目标**：把 `ILuaHost` 往下的整条链抽掉，产品与框架都不再认识 Lua。
- **框架面**：`ILuaHost`、`Adapters/XLua/Lua/**`、`ILuaRegistry`/`LuaRegistry`/`RegistryFillReport`、`LuaScriptsHealthProbe`、`SafeCall` 中指向 xLua 封装的注释（lua 专属封装在 `EventBridge`）；`UIFormLogicBinder` 删 `LuaPath` 分支与 stale/refill 编排（`MarkAllStale`/`ApplyToRecycled`/`SwapIfStale`/`DropAll`/`EnsureAfterReload`）、`UIForm` 删 `NeedsReinit` 的换表语义（**保留池化复用语义**）、`UIService` 删 `MarkLogicStale` 一类入口；新增 4.4 的两条注入缝。
- **产品面**：`Adapters/LuaBridge/**` 20 件、`Core/Lua/**`、`Modules/Composition/LuaHostSegment`、`Modules/UI/Abstractions/{ILuaUIBinding,IUILogicFactory,IPageDataFactory}`、`Modules/Procedure/Abstractions/ILuaRegistryFiller`、`Modules/Meta/MetaBridge`（业务逻辑迁 `IMetaSession` 实现）、`AppComposition` 的六段装订、`ClientEntry` 的 `LuaHostSegment` 段、`ProcedurePreload` 的①②③④ 四步（保留配置装载与模态登记）、`LuaDevReload`/`LuaDevRefill`、`GenConfig`。
- **退出条件**：`rg "XLua|LuaTable|LuaEnv|ILuaHost|LuaPath" Assets/LiteGame Packages/com.litegame.framework` 零命中；`LiteGame.Adapters.LuaBridge` asmdef 与五个引用它的 asmdef 全部去引用；设备启动无 Lua 预载段（启动耗时下降可观测）。

### P3 内容、构建、程序集与门禁（约 3–4 人日）

- **内容**：删 `lua/` 段（`ReleaseLayout`、候选源两件、`CandidateScriptSet`、`gen-candidate.py` 受控项、`LuaScriptsHealthProbe` 装配）；删 YooAsset `Lua` 收集组、`LuaTextAssetImporter`、`LiteGameIgnoreRule` 的放行分支；同步 `arch/units.json` 三处（`collectGroups`/`collectPathExceptions`/`topLevel.liteGame`）与生成镜像 `module-tiers.json`。
- **构建**：删 xLua 源码/原生库/工具链三件；hot 程序集两份清单去 `LiteGame.Adapters.LuaBridge`；`restore-check` 去 `xlua.bundle` 必需项；`player-smoke` 改 ABI 理由；`comment-lint` 去 `/XLua/` 排除。
- **门禁**：`DisciplineScanner` R12 边界表删 `XLua` 那条（含两个目录；规则本身保留，仍管 YooAsset/DOTween）；六个守卫测试改期望；`toolchain.json` 六段与 `EditorToolHandlers` 两个 action 删除；`scripts/tests` 四个文件改期望。
- **退出条件**：`python scripts/toolchain.py check` 无 Lua 条目；`arch-check` 绿；`BundleCollectorSetting` 9 组；出包目录无 `xlua.*` 与 `*_lua*.bundle`。

### P4 表结构与 `PageIds` 收口（哈希事件，约 2–3 人日）

- **目标**：删三列、删 `LuaKeys`（`PageIds` 已在 P1-0 承接）、重生成读码面。
- **动作**：改 `#uiform`/`#strategy`/`#contententry` 列（`_uiform_row.py` 列序号同批）→ `Luban/gen.bat` → `toolbox.py build-hash` → `toolbox.py ui-page`（产出 `PageIds.g.cs`）→ `UIPageManifest` 与 24 份契约重生成（契约 diff 按"UI API 变更"评审）→ 两端按[热更专项 §11](../客户端/内容/热更与内容发布专项设计.md) 发布顺序同步。
- **注意**：这是**唯一会移动 `BuildHash` 的批次**（P7）；出包门禁 `--check-shared` 与 Meta 的 `--check` 都要重跑。
- **退出条件**：三列不存在且无读者；`PageIds.g.cs` 与 catalog 同源同批；两端握手通过、双人一局结算可查。

### P5 文档面收口（约 4–6 人日）

- **主题级重写（16 篇）**：`说明/08`（改题《UI 与页面逻辑层》并重写 §6）、`说明/17` §3.6/§12、`说明/09` §6.3、`说明/02`、`说明/01`、`说明/14`、`说明/19`；`设计文档/客户端/UI系统/{UI系统总设计, UI逻辑API参考, UI编辑器工具链专项设计, 设置系统专项设计}`；`设计文档/客户端/内容/热更与内容发布专项设计`（§10 整章）；`设计文档/架构/{商业级通用客户端框架总设计, 客户端依赖注入专项设计, 客户端架构重构演进路线图, 框架先行建设与业务接入专项设计}`；`设计文档/测试与性能/调试与开发工具链演进路线图`；`规范/06`。
- **机制引用（P1 级）**：`规范/02/03/04/05/08/09`、`说明/03/10/11/13/15/16/工具索引/架构图 README`、三个 README 索引行。
- **进度与审计**：`待办总览` 8 行改判；`施工进度` 两处冲突（`客户端重构线` 的"Lua 为终态"、`热更与内容线` 的 `xLua hotfix` 行）由**后批新章**撤销（不回改前章）；`审计/客户端/登记` 6 条失效债（功能缺口:15、技术债:14、硬编码:27、类设计:23、重复实现:14、不统一路径:12）按规范 07 回写"修复登记"。
- **退出条件**：`python scripts/gate/doc-lint.py` 0/0；全仓 `rg` 文档中"Lua 热更/脚本激活"表述零命中（历史进度章除外，需带注记）。

## 7. 风险与未决点

| # | 风险／未决 | 触发条件 | 缓解 |
|---|---|---|---|
| R1 | **表结构改动＝两端哈希事件** | P4 执行 | 末批隔离；改表与 `build-hash` 同批；按发布顺序同步；不便于动哈希时 P4 可整体延后（列留空） |
| R2 | 24 份契约重生成产生大量 diff | P1 逐页清 `luaPath` + P4 删列 | 把 `ui.contract-diff` 的差异当"UI API 变更"评审；同批只动 `luaPath` 一个字段，避免与真实 UI 变更混在一起 |
| R3 | 开发期"改脚本存盘即生效"的体验消失 | P2b 之后 | 编辑期用 HybridCLR 热重载 / 域重载补偿；这也是本方案把逻辑热更推给 `code/` 的前提，需与 HybridCLR 线排期对齐 |
| R4 | PlayMode 用例以真 Lua 为载体 | P1 起 | `LuaPagePlayModeTests`（4）/`EnvRebuildPlayModeTests`（3）/`NavigationPlayModeTests`（1）改用真 C# 页载具，语义逐条对齐；L2 由 nightly 兜 |
| R5 | `LiteGameIgnoreRule` 的 `DefaultAsset` 放行分支删掉后，可能有别的无导入器文件被误吞 | P3 | 删除前对当前收集清单跑一次"DefaultAsset 命中面"核对；发现再按需白名单 |
| R6 | hot 程序集清单两份（`HybridCLRSettings.asset` 与 `HybridCLRTool.cs`）易漂 | P3 | 同批同改；顺手登记为单源债，后续由生成收口 |
| R7 | 归档与一次性产物路径 | P5 | 按 doc-authoring §5：引用归档目录时只写反引号目录路径，不写文件级链接 |
| R8 | 若 HybridCLR 排期晚于 P2b，页面逻辑会短暂"不可热更" | 排期冲突 | 属可接受回退（`code/` 尚未上线时 Lua 热更本就未在生产使用）；需在 P2b 前与该线确认 |

**未决点（需人裁决，出方案时保留两案）**：

1. `#contententry` 整表去留——保留表壳（未来内容条目另有消费者）还是连表删除？本方案默认保留表壳、只删 `entry` 列。
2. `Tmp_*` 模板页是否补一个"可挂逻辑的模板"示例——本方案默认不补，模板页保持无逻辑。
3. `Bridge.meta` 的失败原因字符串（`"Network"/"HTTP401"/"Internal"` 等）是否改为强类型枚举——本方案默认**先保持字符串**，避免把语义变更混进拆除批。

## 8. 验收判据

**静态（可在 L1 断言）**

1. `Assets/LiteGame/Lua` 目录不存在；`rg "XLua|LuaTable|LuaEnv|LuaFunction|ILuaHost|LuaPath|LuaKeys|Bridge\."` 在产品面与框架面零命中。
2. 无 `LiteGame.Adapters.LuaBridge` asmdef，且无 asmdef 引用 `xLuaMain`；`ProjectSettings/HybridCLRSettings.asset` 与 `HybridCLRTool.cs` 清单一致且不含 LuaBridge。
3. `BundleCollectorSetting.asset` 9 组、无 `Lua` 组；`arch-check` 绿；`toolchain.py check` 无 `lua-*`/`xlua` 条目。
4. `Luban/Data` 三张表不再有 Lua 列；`Assets/LiteGame/Lua` 无 `Cfg/*.lua`；`PageIds.g.cs` 与 `UIPageCatalog.g.cs` 同批新鲜（`--check` 绿）。

**构建（可量）**

5. 出包目录无 `xlua.dll`/`libxlua.*`/`xlua.bundle`，`DefaultPackage` 无 `*_lua*.bundle`；包体与首启耗时相对基线给出前后对照。
6. `build-hash` 两端常量一致，Join 握手通过；`restore-check` 无 xLua 项。

**功能（真实操作路径，红线 13）**

7. 七页逐页等价清单（§6 P1 所列验收点）全部通过，且**每页**至少一次"入口 → 操作 → 副作用断言"的模拟指针验证。
8. 一局完整联机：登录 → 大厅（建房/选图/进房）→ 房间（席位/开局）→ 对局 → 结算 → 回大厅 → 设置页（改音量/帧率后关闭重开保持）→ 退出对局。
9. 页面逻辑异常注入一次：单页降级、其余页面与导航不受影响（`UIForm.SafeCall` 语义未被破坏）。

**文档**

10. `python scripts/gate/test.py --suite Doc` 绿；本方案涉及的设计文档与索引同批更新。

## 9. 工作量量级

| 批 | 内容 | 量级（人日） |
|---|---|---|
| P0 | 冻结 + 死重 | 1–1.5 |
| P1 | 7 页 C# 化（含三件载体与 PlayMode 载具替换） | 6–9 |
| P2a | 页面 Lua 面收口（单次原子） | 1–2 |
| P2b | 运行时与框架摘除 | 3–4 |
| P3 | 内容/构建/程序集/门禁 | 3–4 |
| P4 | 表结构 + 删 LuaKeys + 两端同步 | 2–3 |
| P5 | 文档 96 篇 | 4–6 |
| 合计 | | **20–29.5** |

**关键路径＝P1**（七页迁移，且每页都要走 prefab 与契约的资产管线）。P0 与 P3 的文档预读、P5 的框架文档重写可与 P1 并行；P4 必须排在 P2a/P3 之后（`lua_path` 的读者先死，列才能删）。