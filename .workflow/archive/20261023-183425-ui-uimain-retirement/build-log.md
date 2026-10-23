# 施工记录：UI 系统 UIMain 退役：注册表锚迁移 · 产品注册摘除 · 测试载体归位

> 运行 ui-uimain-retirement · 创建于 2026-10-23T17:29:30+08:00

## 施工记录

<!-- 任务开始、完成、阻塞、取消等事件由 workflow.py 自动追加。 -->
- 2026-10-23T17:31:56+08:00 · 新增任务 T1「先红：退役守卫与既有夹具目标态」
- 2026-10-23T17:31:56+08:00 · 新增任务 T2「启动锚迁移：RegistryFiller KnownUiKey + main.lua 去 UIMain」
- 2026-10-23T17:31:56+08:00 · 新增任务 T3「测试载体归位：Tmp_UiTestPage prefab + Tests.UiTestPage Lua」
- 2026-10-23T17:31:57+08:00 · 新增任务 T4「表与生成物：tbuiform 删 id=1 + Luban/LuaKeys/页面清单/BuildHash」
- 2026-10-23T17:31:57+08:00 · 新增任务 T5「真实面校准与基线收口：11/12、302/0、Wiring 无跳过」
- 2026-10-23T17:31:57+08:00 · 新增任务 T6「文档与审计回写：UIMain 退役事实+代码地图/技术总结/审计/进度」
- 2026-10-23T17:38:30+08:00 · 新增任务 T7「gate 异步通道：过滤参数透传与陈旧状态防护」
- 2026-10-23T17:38:30+08:00 · 开始任务 T7「gate 异步通道：过滤参数透传与陈旧状态防护」
- 2026-10-23T17:54:33+08:00 · 完成任务 T7 · 证据: 异步发射去掉 filter 后的工具 --timeout（实测会丢 filter 跑全集）；发射前 wait_editor_ready 等域重载/编译/Play 结束；陈旧 completed 按 mtime 忽略；testName 结果必须命中 filter。red 全套经此通道完成
- 2026-10-23T17:54:38+08:00 · 开始任务 T1「先红：退役守卫与既有夹具目标态」
- 2026-10-23T17:54:38+08:00 · 完成任务 T1 · 证据: wf verify --red PASS（19 命令；4 条新失败 + 既有 14 条通过；退役守卫 5/6 断言失败）
- 2026-10-23T17:54:42+08:00 · 开始任务 T2「启动锚迁移：RegistryFiller KnownUiKey + main.lua 去 UIMain」
- 2026-10-23T17:55:48+08:00 · 开始任务 T3「测试载体归位：Tmp_UiTestPage prefab + Tests.UiTestPage Lua」
- 2026-10-23T17:55:48+08:00 · 完成任务 T3 · 证据: unity-pipeline rename_asset UIMain→Tmp_UiTestPage（保 GUID）；run_script 根名/去 CanvasScaler/BindName 2 处（ok bindRenamed=2）；create_folder+move_asset Lua→Lua/Tests/Ui…
- 2026-10-23T17:55:51+08:00 · 完成任务 T2 · 证据: RegistryFiller 增 KnownUiKey=LuaKeys.UI.Page_Login 并用于 ValidateKnownKeys；main.lua 去 UI.UIMain require/全局，冒烟改 main.lua loaded
- 2026-10-23T17:55:52+08:00 · 开始任务 T4「表与生成物：tbuiform 删 id=1 + Luban/LuaKeys/页面清单/BuildHash」
- 2026-10-23T17:58:45+08:00 · 完成任务 T4 · 证据: _uiform_row.py 增 --delete-id（备份/确认/回滚）删 id=1；Luban/gen.bat 发布 tbuiform.lua/LuaKeys.g.cs（6 UI 路径）；UiPageManifestEditor.Remove 显式退役；SyncToDisk 后 11 页/12 行、无 UIMa…
- 2026-10-23T18:17:38+08:00 · 开始任务 T5「真实面校准与基线收口：11/12、302/0、Wiring 无跳过」
- 2026-10-23T18:17:38+08:00 · 完成任务 T5 · 证据: PageManifest 16/16（11页12行）· Wiring 8/8（无跳过）· Validation 14/14（Warning 302/命名0）· ValidationRules 10/10 · SafeArea 6/6 · BindingTools 12/12 · U0 7/7 · LuaPage 4/…
- 2026-10-23T18:17:38+08:00 · 开始任务 T6「文档与审计回写：UIMain 退役事实+代码地图/技术总结/审计/进度」
- 2026-10-23T18:17:38+08:00 · 完成任务 T6 · 证据: 代码地图/技术总结04·11/项目说明03/UI系统总设计/工具链设计/schema/审计/进度第7章回写；doc-lint 0/0

## 关键决定

1. **测试载体保留在 Screens**：PlayMode 夹具走 `AssetService.LoadAssetAsync`（YooAsset `UIScreens` 按目录收集）；移到 `Assets/Tests` 会断真资源链路。故改名 `Tmp_UiTestPage` 留在 Screens，Lua 移到 `Lua/Tests/UiTestPage.lua`；产品注册/锚点/清单先摘除，测试收集组另批。
2. **启动锚取真实产品首屏**：`RegistryFiller.KnownUiKey = LuaKeys.UI.Page_Login`，不在启动代码写第二份 key；锚缺失仍按现有 fail-fast 报「LuaKeys 常量未注册」。
3. **退役是显式编辑**：页面清单对账的孤儿策略会保留非 Draft 条目（防静默丢数据），故新增 `UiPageManifestEditor.Remove` 并配 `_uiform_row.py --delete-id`，不削弱对账策略。
4. **Tmp_ 系统面口径**：测试 prefab 去掉 CanvasScaler（Screens `Tmp_` 禁第二套缩放），保留 Canvas/CanvasGroup/GraphicRaycaster/SafeArea；`PageStructureRule` 真实面零 Error。
5. **门禁通道加固**：异步发射不再把工具 `--timeout` 放在 `--filter` 后（实测丢 filter 跑全集）；发射前等 `domainReloadInProgress=false/compiling=false/playMode=stopped`；陈旧 completed 按 mtime 忽略；testName 结果必须命中 filter。

## 偏差与处理

- **范围增补**：全仓 grep 发现 `NavigationPlayModeTests` 也把 UIMain 当真 Lua 载体，已纳入 T1/T5（改 `Tmp_UiTestPage`/`Tests.UiTestPage`）。
- **U0 配置投影覆盖**：两个真 Lua 用例改用 `FakeCatalog`（不再依赖 tbuiform 行 1）；另加 `真配置投影_UIMain行已移除_PageLogin仍在` 保留 `ConfigService → UIFormCatalog` 的真实投影覆盖。
- **合成签名夹具未改**：`signed-candidate*.json` 的 `lua/ui/UIMain.lua` 是签名固定样例，改路径会破坏签名；保留并在边界登记（非产品引用）。
- **工具入口**：`python scripts/toolchain.py open ui.page-manifest` 只打开 Hub 不执行 action；本批用 `UiPageManifestSync.SyncToDisk` 直接执行同一 handler 内核（未复制逻辑）。
- **生成链**：Luban 只发布 `tbuiform.lua`/`LuaKeys.g.cs`/表 bytes；`UiPageManifest` 由显式 Remove + Sync 更新（对账不会自动删非 Draft 孤儿）。
- **环境**：Unity 窗口最小化会挂起主循环；本轮多次恢复窗口后继续。`UiValidation` 夹具曾两次因域重载/late relaunch 留下孤儿 request，已 cancel/清理后重跑；gate 已加发射前域重载等待，未改 Unity 包。

## 问题与修复

| 问题 | 修复 |
|---|---|
| UIMain 删表行后仍被页面清单保留（孤儿策略） | 新增 `UiPageManifestEditor.Remove`；一次性脚本 Remove("UIMain") + SyncToDisk，得到 11 页/12 行 |
| `run_script` 读不到刚落盘的 `Remove` API（程序集陈旧） | 先跑一次退役守卫触发 recompile，再执行退役脚本 |
| 异步 `run_tests` 丢 filter（跑 841 全集） | 根因：工具 `--timeout` 放在 filter 后；移除该参数，filter 透传恢复（实测 6 例） |
| `UiValidation` 600s running / request 孤儿 | 域重载期间发射；`wait_editor_ready` 前置等待 + cancel/清理后复跑 14/14；gate 加陈旧 completed 忽略 |
| Unity 主窗口最小化导致命令 30s 超时 | 恢复窗口（ShowWindow/SetWindowPos/前台）后继续；已沉淀到管线坑位记忆 |
| `toolchain.py open` 不执行 action | 改用同一 handler 内核的 `run_script`（SyncPageManifest/RetireUimainManifest） |

## 验证结果

- 2026-10-23T17:51:51+08:00 · 先行测试 PASS · 4/19 · 证据 evidence/red.json（退役守卫 5/6 断言失败，既有 14 条通过）
- 定向：PageManifest 16/16 · Wiring 8/8 · Validation 14/14（Warning 302/命名 0）· ValidationRules 10/10 · SafeArea 6/6 · BindingTools 12/12 · U0 7/7 · LuaPage 4/4 · EnvRebuild 3/3 · Navigation 4/4
- 2026-10-23T18:33:46+08:00 · 验证 PASS · 30/30 · 证据 evidence/verify.json（L1 1819、PlayMode 3/3、退役守卫 6/6）
- 2026-10-23T18:33:46+08:00 · 验证 PASS · 30/30 · 证据 evidence/verify.json

## 交付摘要

UIMain 从产品面退役：`tbuiform` 删 id=1、启动锚迁 `Page_Login`、`main.lua` 去 require、页面清单 11 页/12 行；prefab/Lua 改名 `Tmp_UiTestPage`/`Tests.UiTestPage` 作为 UI 栈测试载体；真实面 Warning 303→302、命名 Warning 0；11 份产品契约不变。门禁异步通道同时修掉 filter 丢失/域重载/陈旧状态三处稳定性坑。

## 未决事项

- `Tmp_UiTestPage`/`Tests.UiTestPage` 仍在产品收集目录（一个测试 prefab + Lua 的包内残留）；若要求产品包零测试资产，另开测试收集组批。
- 合成签名夹具 `signed-candidate*.json` 仍以 `lua/ui/UIMain.lua` 为固定样例路径（签名绑定，不改）。
- 全量 `wf verify` 与归档待完成。
- 工作区未提交；是否提交由用户决定。

## 文件清单

- 表/生成：`Luban/Data/#uiform.xlsx`、`scripts/codegen/_uiform_row.py`、`Assets/LiteGame/Lua/Cfg/tbuiform.lua`、`Assets/LiteGame/LuaBridge/Generated/LuaKeys.g.cs`、`Assets/GameData/Config/UI/tbuiform.bytes`、`Assets/LiteNet/Protocol/BuildHash.g.cs`、`Assets/LiteGame/Editor/UiTools/UiPageManifest.json`
- 启动/注册：`Assets/LiteGame/LuaBridge/RegistryFiller.cs`、`Assets/LiteGame/Lua/main.lua`、`Assets/LiteGame/Editor/UiTools/Page/UiPageManifestEditor.cs`
- 载体：`Assets/ShareResource/UI/Screens/Tmp_UiTestPage.prefab`（原 UIMain.prefab，保 GUID）、`Assets/LiteGame/Lua/Tests/UiTestPage.lua`（原 UIMain.lua）
- 测试：`Assets/Tests/EditMode/UiUimainRetirementEditModeTests.cs`、`UiPageManifestEditModeTests`、`UiToolchainWiringEditModeTests`、`UiValidationEditModeTests`、`SafeAreaEditModeTests`、`UiBindingToolsEditModeTests`、`UiU0EditModeTests`、`ReleaseLayoutEditModeTests`、`ContentRuntimeAdaptersEditModeTests`、`Assets/Tests/UI/PlayMode/{LuaPage,EnvRebuild,Navigation}PlayModeTests`
- 门禁：`scripts/gate/unity-tests.py`、`.workflow/config.json`
- 文档：`Docs/开发导航/代码地图.md`、`Docs/技术总结/{04,11}*.md`、`Docs/项目说明/03-技术细节-客户端.md`、`Docs/设计文档/客户端/UI系统/{UI系统总设计,UI编辑器工具链专项设计}.md`、`UiPageManifest.schema.json`、`Docs/审计/代码质量审计-客户端.md`、`Docs/施工进度/UI系统线.md`

## 后续建议

- 先跑全量 `wf verify` 并归档本批；再按批收口纪律提交（当前工作区另有并行线改动，需挑范围）。
- 若用户要求包内零测试资产，开候选 C：测试收集组/构建期排除（需 YooAsset 策略改动）。
- 保持 `RegistryFiller.KnownUiKey` 单源：未来换首屏只改该常量。


## 阶段流转

<!-- 阶段推进与回退记录由 workflow.py 自动追加。 -->
- 2026-10-23T17:29:30+08:00 · 初始化运行，当前阶段「提出方案」
- 2026-10-23T17:31:35+08:00 · 完成「提出方案」→ 「实施任务清单」
- 2026-10-23T17:32:17+08:00 · 完成「实施任务清单」→ 「评审任务」
- 2026-10-23T17:32:17+08:00 · 完成「评审任务」→ 「先写测试」
- 2026-10-23T17:54:33+08:00 · 完成「先写测试」→ 「正式施工」
- 2026-10-23T18:18:01+08:00 · 完成「正式施工」→ 「测试代码」
- 2026-10-23T18:34:18+08:00 · 完成「测试代码」→ 「评审代码」
- 2026-10-23T18:34:21+08:00 · 完成「评审代码」→ 「回写与收尾」
- 2026-10-23T18:34:25+08:00 · 完成「回写与收尾」→ 完成
