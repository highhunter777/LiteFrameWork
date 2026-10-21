# 施工记录：开发工具链平台统一

> 运行 toolchain-platform · 创建于 2026-10-21T17:43:42+08:00

## 施工记录

<!-- 任务开始、完成、阻塞、取消等事件由 workflow.py 自动追加。 -->
- 2026-10-21T17:46:02+08:00 · 新增任务 T1「先写平台红测：清单/CLI/文档」
- 2026-10-21T17:46:02+08:00 · 新增任务 T2「设计文档落档」
- 2026-10-21T17:46:02+08:00 · 新增任务 T3「清单 v2 与 Python 校验器」
- 2026-10-21T17:46:02+08:00 · 新增任务 T4「单一 CLI 命令面」
- 2026-10-21T17:46:02+08:00 · 新增任务 T5「生成工具索引并接入文档」
- 2026-10-21T17:46:02+08:00 · 新增任务 T6「注册面检查与夜间任务修复」
- 2026-10-21T17:46:02+08:00 · 新增任务 T7「C# 清单加载/目录/页件契约」
- 2026-10-21T17:46:03+08:00 · 新增任务 T8「Hub 窗口、登记与目录对账测试」
- 2026-10-21T17:46:03+08:00 · 新增任务 T9「GM 页嵌入与窗口 launch」
- 2026-10-21T17:46:03+08:00 · 新增任务 T10「动作/CLI/文档型工具登记」
- 2026-10-21T17:46:03+08:00 · 新增任务 T11「顶层菜单收口与白名单」
- 2026-10-21T17:46:03+08:00 · 新增任务 T12「CLI open/verify/build 与 restore-check/L0 接入」
- 2026-10-21T17:46:03+08:00 · 新增任务 T13「全量验证与证据」
- 2026-10-21T17:46:03+08:00 · 新增任务 T14「文档回写、审计与收尾」
- 2026-10-21T17:46:29+08:00 · 更新任务 T5（acceptance, files, verify）
- 2026-10-21T17:46:29+08:00 · 更新任务 T6（acceptance, files, verify）
- 2026-10-21T17:46:29+08:00 · 更新任务 T7（acceptance, files, verify）
- 2026-10-21T17:46:29+08:00 · 更新任务 T8（acceptance, files, verify）
- 2026-10-21T17:46:29+08:00 · 更新任务 T9（acceptance, files, verify）
- 2026-10-21T17:46:29+08:00 · 更新任务 T12（deps）
- 2026-10-21T17:46:29+08:00 · 更新任务 T13（acceptance, verify）
- 2026-10-21T17:46:37+08:00 · 更新任务 T2（acceptance, files）
- 2026-10-21T17:46:37+08:00 · 更新任务 T14（files）
- 2026-10-21T17:48:16+08:00 · 开始任务 T1「先写平台红测：清单/CLI/文档」
- 2026-10-21T17:48:17+08:00 · 完成任务 T1 · 证据: scripts/tests/test_toolchain.py 16 例；红测 exit=1，13 failures，均为 schemaVersion/toolchain.py 缺失的断言失败；evidence/red.json 2026-10-21T17:47:22+08:00
- 2026-10-21T17:48:17+08:00 · 开始任务 T2「设计文档落档」
- 2026-10-21T17:49:22+08:00 · 完成任务 T2 · 证据: Docs/设计文档/架构/开发工具链平台专项设计.md（清单/Hub/页件/CLI/注册面/扩展约定）；已加入 Docs/设计文档/README.md 架构索引；doc-lint 0/0
- 2026-10-21T17:49:22+08:00 · 开始任务 T3「清单 v2 与 Python 校验器」
- 2026-10-21T17:52:07+08:00 · 完成任务 T3 · 证据: scripts/gate/toolchain.json v2（7 分类/45 工具/5 注册面）；scripts/toolchain.py + manifest/docs/surfaces 模块；16/16 Python 契约测试通过；check 仅剩夜间任务 2 条预期 warning
- 2026-10-21T17:52:07+08:00 · 开始任务 T4「单一 CLI 命令面」
- 2026-10-21T17:52:07+08:00 · 开始任务 T5「生成工具索引并接入文档」
- 2026-10-21T17:52:44+08:00 · 完成任务 T4 · 证据: scripts/toolchain.py 九命令面；--help/未知工具 exit4/list 中文输出/UTF-8 重配置通过；16/16 Python 测试通过
- 2026-10-21T17:52:44+08:00 · 完成任务 T5 · 证据: Docs/工具链/工具索引.md 生成/幂等/--check；Docs/README 与技术支持已链接；Doc lane 执行 doc-lint + docs --check，均通过
- 2026-10-21T17:52:44+08:00 · 开始任务 T6「注册面检查与夜间任务修复」
- 2026-10-21T17:54:33+08:00 · 完成任务 T6 · 证据: surface 检查器（CI/hook/计划任务/可选路径）；install-nightly-task.ps1 干跑+实际重注册（pwsh + scripts/gate/nightly-gate.ps1，NextRun 2026-10-22 03:00）；toolchain check 0 warning；l2 ev…
- 2026-10-21T17:54:33+08:00 · 开始任务 T7「C# 清单加载/目录/页件契约」
- 2026-10-21T18:07:46+08:00 · 完成任务 T7 · 证据: Toolchain 目录 10 文件（manifest/catalog/registry/page contract/adapters/Hub utility）；LiteGame.Editor 加 LiteSim.Core.Editor/DisciplineScanner.Editor；Unity 编译 0 CS；新…
- 2026-10-21T18:07:46+08:00 · 开始任务 T8「Hub 窗口、登记与目录对账测试」
- 2026-10-21T18:07:46+08:00 · 完成任务 T8 · 证据: LiteGame/工具链 Hub（搜索/分类/文档/四类适配/清单错误页）；EditorToolCatalogEditModeTests 6/6；CLI open gm.test-mode 深链 SelectedToolId=gm.test-mode，Console 无异常
- 2026-10-21T18:07:46+08:00 · 开始任务 T9「GM 页嵌入与窗口 launch」
- 2026-10-21T18:07:47+08:00 · 完成任务 T9 · 证据: GM 六页由 Hub 直接宿主；GmPanel 经 unity-pipeline delete_asset 退役；烘焙/UI 窗口作为 launch 目标；GmLocalChainPage _probing finally 复位+volatile+Hub 重绘；Unity 编译 0；定向 EditMode 6/6
- 2026-10-21T18:07:47+08:00 · 开始任务 T10「动作/CLI/文档型工具登记」
- 2026-10-21T18:07:47+08:00 · 完成任务 T10 · 证据: 清单 48 工具（15 action/7 cli/7 gate/6 page/4 window/4 docs/3 content/2 build）；handler 1:1；xLua 元数据迁入 codegen.xlua；CLI/门禁/构建/文档型工具均可在 Hub 发现
- 2026-10-21T18:07:47+08:00 · 开始任务 T11「顶层菜单收口与白名单」
- 2026-10-21T18:07:47+08:00 · 完成任务 T11 · 证据: 顶层剩余：LiteGame/工具链、快捷 Prefab/Lua、BindRoot(工具链/代码生成)、CONTEXT、LiteCodeGen 包级；旧 Gm/Baking/UI/Resource/LiteSim/Tools 顶层菜单已收口；Unity 编译 0
- 2026-10-21T18:07:48+08:00 · 开始任务 T12「CLI open/verify/build 与 restore-check/L0 接入」
- 2026-10-21T18:07:48+08:00 · 完成任务 T12 · 证据: CLI open/generate/verify(Doc)/run(L0) 实测；restore-check 读清单 version/revision（OK 2022.3.55f1c1 / 06b99830297d）；L0 追加 toolchain check 通过；L0/restore-check 仍因预存 Par…
- 2026-10-21T18:07:48+08:00 · 开始任务 T13「全量验证与证据」
- 2026-10-21T18:07:48+08:00 · 完成任务 T13 · 证据: Python 16/16；L1 1801/1801；Doc 0/0+索引 current；Unity 编译 0 CS；EditorToolCatalog 6/6；BuildHash --check-shared OK 459a3145904693b7；全量 L2 未跑（白天约定，留今夜夜间任务）
- 2026-10-21T18:07:48+08:00 · 开始任务 T14「文档回写、审计与收尾」
- 2026-10-21T18:07:48+08:00 · 完成任务 T14 · 证据: 设计/Hub/CLI/注册面文档回写；技术支持/AGENTS/scripts README/代码地图/修改指南/directory-move-sync/审计/环境记录/施工进度第10章同批；doc-lint 0/0；生成索引 current

## 关键决定

- 清单保持在 `scripts/gate/toolchain.json`（不搬入 Assets）；Hub 从工程根读取，避免资源导入与 Player 闭包。
- 清单保存元数据/命令，C# `EditorToolRegistry` 保存行为；EditMode 对账测试强制 handler 与清单 1:1。
- 页件采用混合迁移：GM 六页内嵌（`IGmPage : IEditorToolPage`），烘焙/UI 窗口保持独立窗口作为 launch 目标；不重写高风险 UI。
- `GmPanel` 壳退役，由 Hub 接管生命周期/轮询/选中记忆；`Prefab 回写` 与 Lua 快捷键保留在 `LiteGame/工具链/快捷/`，以维持 UI 工作台跨程序集菜单桥与肌肉记忆。
- LiteCodeGen 顶层菜单收口到 `LiteGame/工具链/代码生成/BindRoot/*`；上下文菜单保留。
- 单一 CLI 为 Python facade，既有 pwsh 脚本仍是执行后端；L0 追加 `toolchain.py check`，Doc 车道追加 `docs --check`。
- 夜间任务重注册为 pwsh + 正确脚本；安装脚本从清单读取参数并支持 `-WhatIf`。
- `doctor` 如实继承 `restore-check` 的阻塞项，不提供绕过。

## 偏差与处理

- UI 三个窗口未嵌入 Hub：按方案保持 launch 目标，入口已统一；内部重写留后续。
- `GmPanel` 按方案“无引用则删除”执行，删除后发现 `GmLocalChainPage` 的重绘目标仍指向它，同批改为 Hub 并修复探针复位。
- 全量 L2 未在白天执行：遵守项目昼夜测试节律，留今夜 `LiteGame-Nightly-Gate` 03:00；不宣称 L2 通过。
- L0/restore-check 仍因**预存** `com.veriorpies.parrelsync` 本机绝对路径失败；该环境债不在本批范围，`toolchain.py check` 本身通过。
- 实施期间 `scripts/gate/toolchain.json` 被并行线追加 `diagnostics`；本批将其内容并入 `env.room-replay` 工具条目，未丢失登记。
- 定向 EditMode 实际为 6 例（比计划多一条窗口/动作委托非空断言），用于钉住 launch/action 接线。

## 问题与修复

- `ToolchainEditorUtility` 同时引 `System.Diagnostics` 与 `UnityEngine` 导致 `Debug` 二义；改 `Process`/`ProcessStartInfo` 别名并补 `EditorToolDescriptor.Summary`。
- L2 `eval_file` 少 `--file` 参数：修正请求/状态两处，临时探针 `result=1` 验证通过。
- 夜间计划任务仍指向已删除的 `scripts/nightly-gate.ps1` 且 runner 为 PS 5.1：安装脚本改 pwsh + `scripts/gate/nightly-gate.ps1`，实测重注册，NextRun 2026-10-22 03:00。
- `GmLocalChainPage._probing` 置 true 后不复位：Task 体 try/finally 复位，`_probing/_dirty/_snap` 加 volatile，异常写 `LastError`，重绘目标改 Hub。
- 删除 `GmPanel` 后 `GmLocalChainPage` 重绘断链：先改引用再走 unity-pipeline `delete_asset`；Unity 编译 0 错。
- `PrefabWriteBackWindow.MenuPath` 改动会影响 UI 工作台 `ExecuteMenuItem`：同批更新 `UIWorkbenchDriver.WriteBackMenuPath` 与守卫用例期望；实测菜单桥返回 true。
- `toolchain.json` 采用 `writes:["none"]`：校验器 WRITE_SCOPES 补 `none`，避免把“无写入”误判为非法。
- 生成工具索引在清单 xLua 迁移后过期：重新 `toolchain.py docs`，Doc 车道保持 0/0。

## 验证结果

- Python 契约测试：16/16 OK。
- L1：PASS 1801 tests（`TestResults/toolchain-platform-l1.log`）。
- Doc：doc-lint 0 violation / 0 warning；生成索引 current。
- Unity：recompile completed / failed=false / 0 CS；EditorToolCatalog EditMode 6/6。
- Hub 深链：`open gm.test-mode` → `SelectedToolId=gm.test-mode`，Console 无异常。
- BuildHash：`--check-shared` OK（459a3145904693b7，110 files）。
- 夜间任务：action 回读 pwsh + 正确脚本，NextRun 2026-10-22 03:00。
- 全量 L2：未跑（白天约定），留夜间任务。
- L0/restore-check：toolchain check 通过；因预存 ParrelSync 绝对路径仍阻塞。
- 2026-10-21T17:47:22+08:00 · 先行测试 PASS · 1/1 · 证据 evidence/red.json
- 2026-10-21T17:47:57+08:00 · 先行测试 PASS · 1/1 · 证据 evidence/red.json
- 2026-10-21T18:09:07+08:00 · 验证 PASS · 5/5 · 证据 evidence/verify.json
- 2026-10-21T18:10:36+08:00 · 验证 PASS · 5/5 · 证据 evidence/verify.json

## 交付摘要

- 交付了一套清单驱动的开发工具链平台：`scripts/gate/toolchain.json` v2（7 分类/48 工具/5 注册面）、`scripts/toolchain.py` 单一 CLI、`Docs/工具链/工具索引.md` 生成索引、`LiteGame/工具链` 编辑器 Hub、`IEditorToolPage` 页件契约、GM 六页内嵌与旧菜单收口、CI/hook/计划任务注册面检查、夜间任务修复。
- 现有工具实现保留；新增工具 = 清单一条 + handler 一个（编辑器工具），入口/文档/巡检自动跟随。

## 未决事项

- 全量 L2 待今夜 `LiteGame-Nightly-Gate`（03:00）或手动 `l2-unity-gate.ps1`；本批不宣称 L2 通过。
- L0/restore-check 仍被预存 ParrelSync 本机绝对路径阻塞；`doctor` 会如实失败，修它需另批裁决本地 ParrelSync 来源。
- `Packages/com.unity.pipeline` 的 9 处本地补丁仍无 checksum/apply 自动校验；本批只登记可选路径。
- 已登记未处理：`BindGenerator` 覆写手写主文件、`SimTrigTableGenerator` 无条件写盘、`refresh_hash` 探测失败返回 0。
- UI 绑定/编排/回写三窗口未嵌入 Hub（本批为 launch 目标）；Hub 内按钮的真实点击未由自动化覆盖。

## 文件清单

- 清单/CLI/文档：`scripts/gate/toolchain.json`、`scripts/toolchain.py`、`scripts/toolchain_manifest.py`、`scripts/toolchain_docs.py`、`scripts/toolchain_surfaces.py`、`scripts/tests/test_toolchain.py`、`Docs/工具链/工具索引.md`。
- 门禁/环境：`scripts/gate/install-nightly-task.ps1`、`scripts/gate/l2-unity-gate.ps1`、`scripts/gate/restore-check.ps1`、`scripts/gate/l0-dep-scan.ps1`、`scripts/gate/test.ps1`。
- 编辑器 Hub/契约：`Assets/LiteGame/Editor/Toolchain/`（10 个 .cs + meta）、`Assets/Tests/EditMode/EditorToolCatalogEditModeTests.cs`、`Assets/LiteGame/Editor/LiteGame.Editor.asmdef`、`Assets/LiteGame/Editor/Gm/IGmPage.cs`。
- 入口收口：`BakingToolbox`/`TrainingGroundLayout`/`BuiltinBundleBuildInvoker`/`ContentSampleBuilder`/`UiStyleTool`/`UiTextStyleTool`/`UiBindGenerationHash`/`UiBindingWindow`/`UiComposeWindow`/`UiValidator`/`SimTrigTableGenerator`/`IeeeBaselineChecker`/`DisciplineScanMenu`/`DevReload`/`LuaDevRefill`/`PrefabWriteBackWindow`/`UIWorkbenchDriver`/`BindMenus`；删除 `GmPanel.cs` 与 `.meta`。
- 文档：`Docs/设计文档/架构/开发工具链平台专项设计.md`、`Docs/设计文档/README.md`、`Docs/README.md`、`AGENTS.md`、`scripts/README.md`、`Docs/工具链/技术支持.md`、`Docs/开发导航/代码地图.md`、`Docs/修改指南.md`、`.agents/skills/directory-move-sync/SKILL.md`、`Docs/审计/代码质量审计-客户端.md`、`Docs/环境/非作者机恢复与验证记录.md`、`Docs/施工进度/资源与工具链线.md`、`Docs/项目说明/05/07/08/13` 与相关专项设计。

## 后续建议

- 今夜跑全量 L2，确认 Hub 变更未影响既有 EditMode/PlayMode。
- 另批处理 ParrelSync 绝对路径（改为可复现依赖或清理本地包），让 L0/restore-check/doctor 完整转绿。
- 把 UI 三窗口逐步迁移为 `IEditorToolPage`（或嵌入式容器），同时补 Hub 按钮的真实操作验证。
- 给 pipeline 9 处补丁加 checksum/apply/verify，纳入 `doctor`。
- 修 `BindGenerator` 主文件只创建不覆盖、`SimTrigTableGenerator` write-if-changed、`refresh_hash` 严格失败；这三项仍是工具链可信度的下一层。

## 阶段流转

<!-- 阶段推进与回退记录由 workflow.py 自动追加。 -->
- 2026-10-21T17:43:42+08:00 · 初始化运行，当前阶段「提出方案」
- 2026-10-21T17:45:34+08:00 · 完成「提出方案」→ 「实施任务清单」
- 2026-10-21T17:46:06+08:00 · 完成「实施任务清单」→ 「评审任务」
- 2026-10-21T17:47:01+08:00 · 完成「评审任务」→ 「先写测试」
- 2026-10-21T17:48:09+08:00 · 完成「先写测试」→ 「正式施工」
- 2026-10-21T18:08:27+08:00 · 完成「正式施工」→ 「测试代码」
- 2026-10-21T18:11:09+08:00 · 完成「测试代码」→ 「评审代码」
- 2026-10-21T18:11:31+08:00 · 完成「评审代码」→ 「回写与收尾」
- 2026-10-21T18:11:39+08:00 · 完成「回写与收尾」→ 完成
