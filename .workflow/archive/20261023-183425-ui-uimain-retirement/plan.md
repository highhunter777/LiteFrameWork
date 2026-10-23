# 方案：UI 系统 UIMain 退役：注册表锚迁移 · 产品注册摘除 · 测试载体归位

> 运行 ui-uimain-retirement · 创建于 2026-10-23T17:29:30+08:00

## 背景与目标

- 现状：`UIMain` 是 M3/M4 的样例页（id=1），产品主流程早已不打开它——`ProcedureMain.cs:78` 只开 `OpenLanding`，`PageFlow.cs:59-60` 只开 `Page_Login/Page_Lobby`。但它仍是：
  - `tbuiform` 行 id=1（`Assets/LiteGame/Lua/Cfg/tbuiform.lua:3`）→ 页面清单 12 页/13 行；
  - `RegistryFiller.cs:104` 的唯一 `LuaKeys` 启动校验锚（`LuaKeys.UI.UIMain`）；
  - `main.lua:9-12` 的 require + 冒烟日志；
  - 6 个 UI 测试夹具的真 Lua/prefab 载体（`LuaPagePlayModeTests`、`EnvRebuildPlayModeTests`、`UiU0EditModeTests`、`SafeAreaEditModeTests`、`UiBindingToolsEditModeTests`、`UiToolchainWiringEditModeTests`）与 303 Warning 中的 UIMain 根名债。
- 要解决的问题：UIMain 作为产品页的登记/入口/契约面残留，造成页面清单多一页、启动校验锚停在样例页、校验 Warning 永带一条存量根名债、测试夹具与产品页身份混淆。
- 目标（可观察）：
  1. `tbuiform` 不再有 id=1；页面清单变为 **11 页/12 行**，`UIMain` 不再出现；
  2. 启动期 `RegistryFiller` 校验锚改为真实产品页 `Page_Login`；
  3. `main.lua` 不再 require/暴露 `UI.UIMain`；
  4. UIMain 的测试载体改名为明确的测试夹具 `Tmp_UiTestPage`（prefab）+ `Tests.UiTestPage`（Lua），原 6 个夹具覆盖不降级；
  5. 真实面校验 Warning **303 → 302**（仅消失 UIMain 根名 1 条），Error 仍 0；
  6. 全量门禁（L1 + 全部 UI EditMode + PlayMode + doc）保持绿。

## 需求澄清与假设

- 需要确认的问题：无（用户已拍板“按 staged-delivery 开批”；退役口径采用下述推荐方案 A，若要“连测试载体一起删”见候选 B）。
- 已确认答复：UIMain 可以退役；测试载体的处置由本方案给出推荐并留档。
- 采用假设：
  - **测试载体保留但改名归位**：`Tmp_UiTestPage` 仍在 `Assets/ShareResource/UI/Screens`（YooAsset `UIScreens` 收集组按目录收、PlayMode 测试走 `AssetService.LoadAssetAsync`，移出收集目录会断真资源链路）；Lua 移到 `Assets/LiteGame/Lua/Tests/UiTestPage.lua`，模块名 `Tests.UiTestPage`。
  - `Tmp_`（Screens）按系统面口径校验：Canvas + CanvasGroup，**禁止 CanvasScaler**；因此测试 prefab 需移除 CanvasScaler。
  - 页面清单/契约基线只随 `tbuiform` 变化；11 份页面契约不变。
- 假设影响与风险：测试 prefab 仍在产品内容包（约 1 个小 prefab + 1 个 Lua，收集组按目录打包），属**有意的测试载体残留**；若要求产品包零测试资产，另开“测试资源收集组”批（候选 C），不在本批范围。

## 范围与非目标

- 范围内：
  - `RegistryFiller` 启动校验锚 → `LuaKeys.UI.Page_Login`（常量单源 + 守卫用例）；
  - `main.lua` 去 UIMain require/冒烟；
  - `Luban/Data/#uiform.xlsx` 删 id=1 行 → Luban/LuaKeys/表数据重生成 → `ui.page-manifest` 重生成；
  - UIMain prefab 改名 `Tmp_UiTestPage`、根名/文件名同步、移除 CanvasScaler、BindName 对齐 `Button_Close`/`Txt_Label`（unity-pipeline CLI）；
  - UIMain Lua 改名 `Tests/UiTestPage.lua`、模块/类/viewName/绑定名同步；
  - 6 个测试夹具与 2 个页面清单/校验基线夹具改到新载体/新口径；
  - 校验 Warning 基线 303→302、UIMain 根名债清零；
  - 代码注释/文档/审计/施工进度回写。
- 非目标：
  - 不删除测试载体、不把 PlayMode 夹具迁到 Page_Login（候选 B）；
  - 不新增 YooAsset 测试收集组/不改收集策略（候选 C）；
  - 不做 P2 工作台、起页/登记拆分、令牌/布局基线等后续线；
  - 不改 11 份产品页面契约内容（只改页面清单与基线计数）。

## 现状与约束

- 相关代码/模块：
  - 启动/注册：`Assets/LiteGame/LuaBridge/RegistryFiller.cs`、`Assets/LiteGame/Lua/main.lua`、`Assets/LiteGame/App/Procedure/ProcedurePreload.cs`；
  - 表/生成：`Luban/Data/#uiform.xlsx`、`Luban/gen.bat`、`Assets/LiteGame/Lua/Cfg/tbuiform.lua`、`Assets/LiteGame/LuaBridge/Generated/LuaKeys.g.cs`、`Assets/GameData/**`、`Assets/LiteGame/Editor/UiTools/UiPageManifest.json`；
  - 资产：`Assets/ShareResource/UI/Screens/UIMain.prefab`、`Assets/LiteGame/Lua/UI/UIMain.lua`；
  - 测试：`UiPageManifestEditModeTests`、`UiToolchainWiringEditModeTests`、`UiValidationEditModeTests`、`UiU0EditModeTests`、`SafeAreaEditModeTests`、`UiBindingToolsEditModeTests`、`LuaPagePlayModeTests`、`EnvRebuildPlayModeTests`、`ContentRuntimeAdaptersEditModeTests`/`ReleaseLayoutEditModeTests`（合成夹具路径）。
- 约束：
  - Unity 序列化资产改名/改组件一律走 unity-pipeline CLI（`rename_asset`/`set_component_properties`/`save_prefab_contents`），禁直改 YAML；
  - `tbuiform` 唯一写入面是 `scripts/codegen/_uiform_row.py`（本批为其新增 `--delete-id` 能力，含备份/回滚/确认）；
  - 改表源后必须 `Luban/gen.bat`；生成读码面变化要跑 `python scripts/codegen/gen-build-hash.py`；表数值走运行期摘要；
  - 页面清单由 `ui.page-manifest` 工具重生成，不手改 JSON；
  - 11 份契约不因 UIMain 退役变化（UIMain 本来 `contractPresent=false`）；
  - 测试/门禁沿用上批统一异步通道（`run_tests --async_tests` + `test_status`）。

## 候选方案与取舍

| 方案 | 核心思路 | 优点 | 代价/风险 | 结论 |
|---|---|---|---|---|
| A（推荐） | 摘产品注册/锚点；UIMain 载体改名 `Tmp_UiTestPage` + `Tests.UiTestPage`，保留在 Screens/Lua 收集面供现有测试 | 风险最低；6 个夹具覆盖不降级；页面清单/Warning 干净；不碰 YooAsset 收集策略 | 产品内容包仍留 1 个测试 prefab/Lua（小体量）；Tmp_ 需去 CanvasScaler | 采用 |
| B | 彻底删 prefab/Lua；PlayMode 夹具迁到 `Page_Login`（或新合成测试页） | 产品包零 UIMain 残留 | Page_Login 依赖 `Bridge.meta/text/ui`，测试要补 Bridge 桩；5 个夹具改写，风险与工作量明显更高 | 不采用（可另批） |
| C | 测试载体移到 `Assets/Tests/UI/Fixtures` 并新增测试专用 YooAsset 收集组 | 产品包零测试资产、语义最干净 | 需改收集/构建策略并保证 Player 不打包测试组，基础设施批 | 不采用（后续可选） |

推荐方案与理由：A。退役的本质是**摘除产品注册与启动锚**；测试载体是 UI 栈回归的承重资产，改名归位比删除更能保住现有覆盖，且不触发 YooAsset 收集面与 Player 打包策略的连锁改动。

## 设计要点

- 接口/数据：
  - `RegistryFiller` 增加单源常量 `public const string KnownUiKey = LuaKeys.UI.Page_Login;`，`ValidateKnownKeys` 只用该常量；启动缺失时仍按现有 fail-fast 报「LuaKeys 常量未注册」。
  - `main.lua` 删除 `UI = UI or {}` / `UI.UIMain = require(...)`，冒烟日志改为 `main.lua loaded`；不再依赖样例页。
  - `tbuiform` 删 id=1；`LuaKeys.g.cs` 由 Luban 重生成（UI 常量 7→6）；`tbuiform` 数据字节重生成，BuildHash 复算。
  - 测试载体：`Tmp_UiTestPage.prefab`（根同名）、`Tests.UiTestPage.lua`（`class("UiTestPage")`、`_viewName="UiTestPage"`）；BindName `Button_Close`/`Txt_Label`；prefab 去 CanvasScaler，保留 Canvas/CanvasGroup/GraphicRaycaster/SafeArea。
- 关键流程：
  1. 先改测试到目标口径（页面清单不含 UIMain、11/12、Warning 302/0、夹具指向新载体、Wiring 不再跳过 UIMain）→ 先红；
  2. 改 `RegistryFiller`/`main.lua`；
  3. 用 unity-pipeline 改名 prefab/根、去 CanvasScaler、同步 BindName；移动/改写 Lua；
  4. 用 `_uiform_row.py --delete-id 1` 删表行 → `Luban/gen.bat` → `gen-build-hash.py` → `ui.page-manifest`；
  5. 更新 11 契约的既有基线？**不改**；只重生成页面清单；
  6. 更新设计/注释/文档/审计/进度；
  7. 全量门禁。
- 失败与边界处理：
  - `Page_Login` 行/Lua 缺失时启动锚按现有机制 fail-fast（这是期望的显式失败，不是降级）；
  - 测试载体若移出 Screens 会断 `AssetService` PlayMode 加载——本批保持在收集目录；若改名后 `PageStructureRule` 报 Tmp_ 自带 CanvasScaler，按 Tmp_ 口径移除；
  - 页面清单/警告计数若与预期不符（例如样式债计数随 prefab 改名变化），以真实面探针为准校准，不硬改断言；
  - 表行删除失败可经 `_uiform_row.py --rollback` 回退；资产改名可由 git/Unity 版本控制回退。

## 通用性与扩展性

- 根因/共性问题：样例页兼作启动锚/测试载体，导致“产品身份”与“测试身份”耦合。单源锚点（`KnownUiKey`）与明确的测试载体命名（`Tmp_` + `Tests.`）把两类身份分开。
- 复用与扩展点：`RegistryFiller.KnownUiKey` 是唯一启动锚，未来换首屏只改一处；测试载体命名遵循现有 `Tmp_`/`Tests` 约定，后续可整体迁测试收集组。
- 避免硬编码/特判：不新增第二份“锚 key 列表”；页面清单/警告计数由真实生成物与探针决定；`_uiform_row.py` 删除能力通用（id 参数），不写死 UIMain。

## 测试策略

- 测试层级与关键用例：
  - L1：不涉及（本批无纯逻辑新面）。
  - EditMode：
    - `UiPageManifestEditModeTests.真实页面_基线齐全且零错误`：断言 11 页/12 行且**不含 UIMain**；
    - `UiValidationEditModeTests.门禁_真实资产面...`：Error 0、Warning 302、命名 Warning 0；
    - `UiToolchainWiringEditModeTests`：11 份契约写入、**无 UIMain 跳过**、契约基线/contractPresent 仍全真；
    - `UiContractGeneratorEditModeTests`（或 Wiring）新增 `IsContractPageName("UIMain")==false` 守卫；
    - `RegistryFiller` 锚守卫：`KnownUiKey == LuaKeys.UI.Page_Login`（或等价静态断言）；
    - `SafeAreaEditModeTests`、`UiBindingToolsEditModeTests`、`UiU0EditModeTests` 指向 `Tmp_UiTestPage`/`Tests.UiTestPage`；
    - `ContentRuntimeAdaptersEditModeTests`/`ReleaseLayoutEditModeTests` 合成夹具路径改 `Page_Login`（仅去 UIMain 字样）。
  - PlayMode：`LuaPagePlayModeTests`、`EnvRebuildPlayModeTests` 改用测试载体，覆盖仍为真 prefab + 真 Lua + 真转场。
  - Doc：doc-lint 0/0。
- 如何先失败后通过：先把上述断言/路径改到目标态（UIMain 不存在、11/12、302/0、新载体路径），在现工作区跑定向夹具应红；再按任务实施转绿。`RegistryFiller` 锚守卫在改为 `Page_Login` 前红。

## 验收标准

- [ ] AC1：`tbuiform` 无 id=1；`UiPageManifest.json` 11 页/12 行且无 `UIMain`；`LuaKeys.g.cs` UI 常量 6 个。
- [ ] AC2：`RegistryFiller.KnownUiKey == LuaKeys.UI.Page_Login`，启动校验不再引用 UIMain；`main.lua` 不 require UIMain。
- [ ] AC3：`Tmp_UiTestPage.prefab`（根同名、无 CanvasScaler、BindName `Button_Close`/`Txt_Label`）与 `Tests.UiTestPage.lua` 可被 6 个夹具正常加载；真 prefab + 真 Lua 链路不回退。
- [ ] AC4：真实面 `UiValidator.Run()` Error=0、Warning=302、命名 Warning=0；11 份契约与 `contractPresent` 不变。
- [ ] AC5：L1、全部 UI EditMode 夹具、PlayMode 3/3、doc-lint 全绿；无新增 public static 可变状态/硬编码。
- [ ] AC6：设计/注释/文档/审计/施工进度回写，`Docs/开发导航/代码地图.md` 与 `技术总结` 不再把 UIMain 描述为现行页。

## 风险与回滚

- 风险：
  - 测试载体改名/去 CanvasScaler 触发 `PageStructureRule` 或 `UiStyle` 计数变化；
  - `tbuiform` 删除行导致 Luban 生成器/表读取器回归；
  - `RegistryFiller` 锚切换后 Page_Login 的注册失败会使启动 fail-fast（须确认 Page_Login 行/Lua 完整）；
  - PlayMode 资源加载路径仍指向旧 UIMain（遗漏夹具）。
- 监控/发现：定向夹具先红→绿；真实面校准探针；`UiValidator.Run()` 报告；L1/PlayMode；doc-lint。
- 回滚：`_uiform_row.py --rollback <backup>` 恢复表；unity-pipeline/ git 恢复 prefab/Lua/代码；重新 `Luban/gen.bat` + `gen-build-hash.py` + `ui.page-manifest`；回退测试与文档。
