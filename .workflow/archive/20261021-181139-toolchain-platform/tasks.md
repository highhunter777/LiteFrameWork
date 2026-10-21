# 实施任务清单：开发工具链平台统一

> 本文件由 workflow.py 生成，请使用 `task` 子命令更新，勿手改。

## 任务清单

阶段：已收尾（待归档） ｜ 完成 14/14（取消 0，阻塞 0）

| ID | 任务 | 依赖 | 状态 | 验收标准 | 验证方式 | 证据 |
|---|---|---|---|---|---|---|
| T1 | 先写平台红测：清单/CLI/文档 | - | 完成 | 新增 scripts/tests/test_toolchain.py：清单 v2 字段、list、docs --check、CI/hook surface、docs 幂等；实现前运行得到断言失败而非 import/环境错误；只用标准库与子进程。 | python -m unittest discover -s scripts/tests -p test_toolchain*.py -v | scripts/tests/test_toolchain.py 16 例；红测 exit=1，13 failures，均为 schemaVersion/toolchain.py 缺失的断言失败；evidence/red.json 2026… |
| T2 | 设计文档落档 | T1 | 完成 | 新增 Docs/设计文档/架构/开发工具链平台专项设计.md：元数据、清单 schema、Hub/页件契约、单一 CLI、生成文档、注册面、扩展规则、非目标；引用《调试与开发工具链演进路线图》但不复制其能力优先级；入设计索引；doc-lint 通过。 | python scripts/gate/doc-lint.py | Docs/设计文档/架构/开发工具链平台专项设计.md（清单/Hub/页件/CLI/注册面/扩展约定）；已加入 Docs/设计文档/README.md 架构索引；doc-lint 0/0 |
| T3 | 清单 v2 与 Python 校验器 | T1 | 完成 | toolchain.json 增 schemaVersion/categories/tools/surfaces；generators/diagnostics 迁入 tools；check 校验必填/唯一/分类/路径/docs；list 输出分类与工具；不保留第二份工具列表。 | python scripts/toolchain.py check; python scripts/toolchain.py list --json | scripts/gate/toolchain.json v2（7 分类/45 工具/5 注册面）；scripts/toolchain.py + manifest/docs/surfaces 模块；16/16 Python 契约测试通过；c… |
| T4 | 单一 CLI 命令面 | T3 | 完成 | toolchain.py 提供 list/check/docs/open/run/generate/verify/build/doctor；未知工具/参数/编辑器未运行有独立退出码；open 使用 eval_file --file；子进程退出码透传；--help 可用。 | python scripts/toolchain.py --help; python scripts/toolchain.py open no-such-id; python -m unittest discover -s scripts/tests -p test_toolchain*.py -v | scripts/toolchain.py 九命令面；--help/未知工具 exit4/list 中文输出/UTF-8 重配置通过；16/16 Python 测试通过 |
| T5 | 生成工具索引并接入文档 | T3 | 完成 | docs 生成 Docs/工具链/工具索引.md（固定头、LF、write-if-changed）；--check 对 stale 失败；生成页从技术支持与 Docs/README 链接；Doc lane（test.ps1 -Lane Doc）执行 docs --check；不改手工排障叙事。 | python scripts/toolchain.py docs; python scripts/toolchain.py docs --check; pwsh scripts/gate/test.ps1 -Lane Doc; python scripts/gate/doc-lint.py | Docs/工具链/工具索引.md 生成/幂等/--check；Docs/README 与技术支持已链接；Doc lane 执行 doc-lint + docs --check，均通过 |
| T6 | 注册面检查与夜间任务修复 | T3 | 完成 | check 扫描 CI 中 scripts 路径与 hook 配置引用；Windows 计划任务核对 runner/脚本，可选 surface 默认 warning；install-nightly-task.ps1 用 pwsh 重新注册正确路径（执行前征询用户）；l2 eval_file 带 --file 并用临时探针验证；doctor 汇总。 | python scripts/toolchain.py check; python scripts/toolchain.py doctor; pwsh -NoProfile -File scripts/gate/install-nightly-task.ps1 -WhatIf; schtasks /query /tn LiteGame-Nightly-Gate /xml \| findstr nightly-gate.ps1; unity command eval_file --file Temp/toolchain-probe.cs | surface 检查器（CI/hook/计划任务/可选路径）；install-nightly-task.ps1 干跑+实际重注册（pwsh + scripts/gate/nightly-gate.ps1，NextRun 2026-10-2… |
| T7 | C# 清单加载/目录/页件契约 | T3 | 完成 | 新增 ToolchainManifest/EditorToolDescriptor/EditorToolRegistry/IEditorToolPage/EditorToolPageBase/适配页；LiteGame.Editor 引用 LiteSim.Core.Editor/DisciplineScanner.Editor；未注册 handler 可见错误；编译 0 CS；新文件经 unity-pipeline 导入有 .meta。 | unity command recompile_status; unity command console; Unity Pipeline meta scan; targeted compile probe | Toolchain 目录 10 文件（manifest/catalog/registry/page contract/adapters/Hub utility）；LiteGame.Editor 加 LiteSim.Core.Editor/… |
| T8 | Hub 窗口、登记与目录对账测试 | T7 | 完成 | LiteGame/工具链 打开 Hub；搜索/分类/文档/报告；所有清单 editor 工具可显示；page/window/action 适配；open(id) 深链；清单损坏错误页；EditMode 对账测试全绿。 | unity command run_tests --mode editor --filter EditorToolCatalog --async_tests true; poll test_status; menu LiteGame/工具链 manual smoke | LiteGame/工具链 Hub（搜索/分类/文档/四类适配/清单错误页）；EditorToolCatalogEditModeTests 6/6；CLI open gm.test-mode 深链 SelectedToolId=gm.tes… |
| T9 | GM 页嵌入与窗口 launch | T8 | 完成 | IGmPage : IEditorToolPage；GM 六页由 Hub 宿主并驱动生命周期/轮询；烘焙/UI 窗口 Open 公开并从 Hub 打开；原 GM 菜单移除；GmPanel 无引用则删除，否则保留无菜单薄壳并登记；GmLocalChainPage 探针复位修复（若触碰）；定向测试/冒烟。 | unity command recompile_status; unity command run_tests --mode editor --filter EditorToolCatalog --async_tests true; grep [MenuItem] for GmPanel/Baking/UI; Hub manual | GM 六页由 Hub 直接宿主；GmPanel 经 unity-pipeline delete_asset 退役；烘焙/UI 窗口作为 launch 目标；GmLocalChainPage _probing finally 复位+vola… |
| T10 | 动作/CLI/文档型工具登记 | T9 | 完成 | Lua/SimTrig/IEEE/纪律扫描/Resource/样式/校验等动作与 CLI/门禁/构建工具写入清单并可从 Hub 发现；上下文型 LiteCodeGen 仅说明/文档；无 handler 缺口。 | python scripts/toolchain.py check; EditMode catalog; Hub smoke | 清单 48 工具（15 action/7 cli/7 gate/6 page/4 window/4 docs/3 content/2 build）；handler 1:1；xLua 元数据迁入 codegen.xlua；CLI/门禁/构建… |
| T11 | 顶层菜单收口与白名单 | T10 | 完成 | 仅保留 LiteGame/工具链（含快捷子菜单）、上下文菜单、LiteCodeGen 包级菜单；grep 确认无白名单外顶层菜单；所有退场入口在 Hub 可达。 | grep MenuItem; Hub manual; Unity compile | 顶层剩余：LiteGame/工具链、快捷 Prefab/Lua、BindRoot(工具链/代码生成)、CONTEXT、LiteCodeGen 包级；旧 Gm/Baking/UI/Resource/LiteSim/Tools 顶层菜单已收口… |
| T12 | CLI open/verify/build 与 restore-check/L0 接入 | T4, T5, T6, T8, T11 | 完成 | open 经 Pipeline 打开 Hub 选中；verify/build/generate 委托真实脚本并透传退出码；restore-check 从清单读 Unity 版本/修订并汇报 check；L0 运行 toolchain check（可选 surface 不拦 CI）。 | python scripts/toolchain.py open gm.test-mode; python scripts/toolchain.py verify --lane Doc; pwsh scripts/gate/restore-check.ps1; pwsh scripts/gate/l0-dep-scan.ps1 | CLI open/generate/verify(Doc)/run(L0) 实测；restore-check 读清单 version/revision（OK 2022.3.55f1c1 / 06b99830297d）；L0 追加 tool… |
| T13 | 全量验证与证据 | T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12 | 完成 | Python 单测全绿；L1 全绿；doc-lint 0；Unity 编译 0；定向 EditMode 全绿；全量 L2 必须尝试，若被夜间约定阻断则记录未跑原因与夜间任务安排，不宣称 L2 通过；--check-shared 不变；Hub/CLI 冒烟清单逐项有结果。 | python -m unittest discover -s scripts/tests -p test_toolchain*.py -v; pwsh scripts/gate/test.ps1 -Lane L1 -Profile PullRequest; python scripts/gate/doc-lint.py; python scripts/codegen/gen-build-hash.py --check-shared; unity command recompile_status; unity command run_tests --mode editor --filter EditorToolCatalog --async_tests true | Python 16/16；L1 1801/1801；Doc 0/0+索引 current；Unity 编译 0 CS；EditorToolCatalog 6/6；BuildHash --check-shared OK 459a314590… |
| T14 | 文档回写、审计与收尾 | T13 | 完成 | 设计文档入索引；技术支持/AGENTS/scripts README/代码地图/修改指南/directory-move-sync/质量审计/施工进度同批；doc-lint 0；未决项与回滚记录完整。 | python scripts/gate/doc-lint.py; git status; git diff --check | 设计/Hub/CLI/注册面文档回写；技术支持/AGENTS/scripts README/代码地图/修改指南/directory-move-sync/审计/环境记录/施工进度第10章同批；doc-lint 0/0；生成索引 current |

## 任务详情

### T1 · 先写平台红测：清单/CLI/文档

- 文件: scripts/tests/test_toolchain.py

### T2 · 设计文档落档

- 文件: Docs/设计文档/架构/开发工具链平台专项设计.md, Docs/设计文档/README.md

### T3 · 清单 v2 与 Python 校验器

- 文件: scripts/gate/toolchain.json, scripts/toolchain.py, scripts/tests/test_toolchain.py

### T4 · 单一 CLI 命令面

- 文件: scripts/toolchain.py, scripts/tests/test_toolchain.py

### T5 · 生成工具索引并接入文档

- 文件: scripts/toolchain.py, Docs/工具链/工具索引.md, Docs/工具链/技术支持.md, Docs/README.md, scripts/gate/test.ps1, scripts/tests/test_toolchain.py

### T6 · 注册面检查与夜间任务修复

- 文件: scripts/toolchain.py, scripts/gate/install-nightly-task.ps1, scripts/gate/l2-unity-gate.ps1, scripts/gate/toolchain.json, scripts/tests/test_toolchain.py

### T7 · C# 清单加载/目录/页件契约

- 文件: Assets/LiteGame/Editor/Toolchain, Assets/LiteGame/Editor/LiteGame.Editor.asmdef

### T8 · Hub 窗口、登记与目录对账测试

- 文件: Assets/LiteGame/Editor/Toolchain/LiteGameToolboxWindow.cs, Assets/Tests/EditMode/EditorToolCatalogEditModeTests.cs

### T9 · GM 页嵌入与窗口 launch

- 文件: Assets/LiteGame/Editor/Gm/IGmPage.cs, Assets/LiteGame/Editor/Gm, Assets/LiteGame/Editor/Baking/BakingToolbox.cs, Assets/LiteGame/Editor/UiTools, Assets/LiteGame/Editor/Toolchain

### T10 · 动作/CLI/文档型工具登记

- 文件: scripts/gate/toolchain.json, Assets/LiteGame/Editor/Toolchain

### T11 · 顶层菜单收口与白名单

- 文件: Assets/LiteGame/Editor, Assets/LiteSim/Core/Editor, Assets/Tools/DisciplineScanner/Scripts/Editor

### T12 · CLI open/verify/build 与 restore-check/L0 接入

- 文件: scripts/toolchain.py, scripts/gate/restore-check.ps1, scripts/gate/l0-dep-scan.ps1

### T13 · 全量验证与证据

- 文件: TestResults, .workflow/active/toolchain-platform/evidence

### T14 · 文档回写、审计与收尾

- 文件: Docs/设计文档/架构/开发工具链平台专项设计.md, Docs/设计文档/架构/生成器工具箱专项设计.md, Docs/设计文档/README.md, AGENTS.md, scripts/README.md, Docs/开发导航/代码地图.md, Docs/工具链/技术支持.md, Docs/修改指南.md, .agents/skills/directory-move-sync/SKILL.md, Docs/施工进度/资源与工具链线.md, Docs/审计/代码质量审计-客户端.md, Docs/环境/非作者机恢复与验证记录.md

