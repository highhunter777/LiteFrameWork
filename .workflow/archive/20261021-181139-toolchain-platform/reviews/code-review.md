# 代码评审：开发工具链平台统一

> 运行 toolchain-platform · 创建于 2026-10-21T17:43:42+08:00

## 变更范围

- 对照任务与方案：覆盖 T1～T14、AC1～AC10。交付为“清单驱动平台”：`toolchain.json` v2（7 分类/48 工具/5 注册面）、Python CLI + 生成索引、编辑器 Hub + `IEditorToolPage`、GM 六页嵌入、菜单收口、注册面检查与夜间任务修复。
- 变更文件/模块：`scripts/toolchain*.py` 与 `scripts/tests/test_toolchain.py`；`scripts/gate/{toolchain.json,install-nightly-task.ps1,l0-dep-scan.ps1,l2-unity-gate.ps1,restore-check.ps1,test.ps1}`；`Assets/LiteGame/Editor/Toolchain/`（10 个 .cs）、`Assets/Tests/EditMode/EditorToolCatalogEditModeTests.cs`、`LiteGame.Editor.asmdef`、`IGmPage.cs` 与各工具入口文件；`GmPanel.cs` 删除；设计/操作/审计/进度文档。
- 边界说明：工作区同时存在 TPS 相机、诊断回放等并行线改动；本评审只针对本批平台文件，未把并行线 diff 计入结论。

## 正确性与边界

- 主路径：`check` → 清单 schema/唯一性/分类/路径/CI/hook/计划任务 → 生成索引；Hub 按 `page/window/action/cli/docs` 适配；CLI `open` 经 `eval_file --file` 深链；`run/generate/verify/build/doctor` 委托既有脚本并透传退出码。
- 边界与错误路径：清单缺失/非法显示错误页；handler 未注册显示错误行且 EditMode 对账红；未知工具 exit 4；编辑器未运行 exit 5；生成索引 stale 令 `docs --check` 与 Doc 车道红；可选计划任务缺失仅 warning，`--strict` 才升级；破坏性/写资产动作执行前确认。
- 回归风险：旧顶层菜单收口可能改变肌肉记忆；已保留 `LiteGame/工具链/快捷/Prefab 回写` 与 Lua 快捷键，并更新 UI 工作台跨程序集菜单桥常量与守卫用例；`GmPanel` 删除后 `GmLocalChainPage` 重绘改为 Hub，探针复位修复；`BindMenus` 顶层路径迁到统一根，上下文菜单保留。
- 验证边界：全量 L2 未在白天执行；只证明 Unity 编译 0 错与定向页件对账 6/6，不宣称 EditMode/PlayMode 全量通过。

## 测试充分性

- 测试是否真正覆盖验收与失败路径：Python 16 例覆盖清单 v2、重复 id/handler/menu、缺失 docs/命令路径、CI/hook 引用、可选计划任务 warning、`list`/`--help`、未知工具、docs 生成幂等与 `--check`；C# 6 例覆盖清单可加载、handler 1:1、页件可创建、窗口/动作委托、文档存在、id/handler 唯一。测试调用真实 CLI/真实清单，不是桩。
- 是否存在迎合实现的测试：未发现。`open` 深链另用真实 Unity Pipeline 验证；夜间任务用 `schtasks` 回读 action；L1 1801 例含纪律扫描。`EditorToolCatalogEditModeTests` 不渲染 GUI，UI 点击属人工验证边界，已如实记录。

## 通用性与扩展性

- 根因与复用边界：统一的是“发现/登记/执行/文档/注册面”，工具业务实现保留；新增工具只动清单 + handler（编辑器）或 command（CLI），不再新增菜单/文档副本。
- 扩展点与接入方式：`EditorToolRegistry` 三注册面（page/window/action）；`EditorToolCatalog` 按 kind 通用适配；`toolchain.py` 的 manifest/surfaces 模块与检查器；`categories`/`surfaces` 数据驱动。
- 硬编码、特判、魔法值：分类/菜单/命令/写入面来自清单；Hub 不按工具写 switch；C# handler key 与清单 id 一致并由测试对账。唯一跨语言硬编码是 CLI `open` 的目标类型 `LiteGame.Toolchain.LiteGameToolboxWindow`（Unity 入口所需，稳定且已文档化）；`toolchain.py` 的默认清单/索引路径为工程约定常量。
- 重复逻辑与过度抽象：`EditorToolCatalog` 三个 Register 辅助函数形态近似但各自处理 kind 语义，未再抽一层；CLI 不为单工具写专用 switch。`GmLocalChainPage` 仍自管编辑态心跳，作为既有例外记录在设计中，未强行套 Hub 轮询。
- 相关场景复用与回归影响：GM 页件通过 `IGmPage : IEditorToolPage` 复用；烘焙/UI 窗口不改内部；LiteCodeGen 包级入口保留但顶层路径收口；`restore-check`/L0/Doc 复用同一清单与 CLI。
- 命名、结构与复杂度：新增文件按职责拆分（manifest/docs/surfaces/CLI、descriptor/registry/catalog/pages/Hub）；Hub 约 300 行，未出现 god class。

## 安全与性能

- 安全：所有子进程用参数数组（无 shell 字符串拼接）；`open` 的 id 经 JSON 转义写入临时 `.cs`；文档打开仅接受清单内仓库相对路径；计划任务查询使用 `schtasks` 参数数组；清单与命令是版本化仓库数据，属于可信输入。未引入密钥/令牌读写；`unity` CLI 的鉴权仍由其自身处理。
- 性能与资源：Hub 仅在打开/重载时读清单；页面按选择创建，无每帧磁盘 IO；`ActionToolPage` 动作由用户触发，UI 校验/构建等长动作在编辑器主线程执行（与既有工具一致）。`GmLocalChainPage` 探针保持既有后台任务，新增 volatile 与 finally 复位，未新增轮询面。

## 发现的问题与处理

| ID | 严重度 | 位置 | 问题 | 处理 | 状态 |
|---|---|---|---|---|---|
| C-1 | 中 | `scripts/gate/toolchain.json` → `env.nightly-install` | 命令缺少 `-Force`，任务已存在时无法重注册；摘要未说明覆盖 | 命令补 `-Force`，摘要说明覆盖；`doctor --fix-nightly` 原本已传 `-Force` | 已修复 |
| C-2 | 低 | `LiteGameToolboxWindow.DrawContent` | 对保留的快捷键别名也标“旧菜单”，语义误导 | 改为“菜单/别名”中性标签 | 已修复 |
| C-3 | 中 | `EditorToolHandlers` → `codegen.sim-trig` | 生成物在 BuildHash 源集，动作结果未提示重跑生成器 | 结果文案补“需重跑 gen-build-hash.py” | 已修复 |
| C-4 | 低 | 设计文档 §4.1/§4.2 | 文档写“Hub 顶部工具数/Unity 版本、底部全局报告”，实现为清单路径 + 动作页内报告；本地联测页仍自管编辑态心跳 | 按实现修正设计文字，并明确 `GmLocalChainPage` 例外；不扩大代码改动 | 已修复（文档） |
| C-5 | 低 | `scripts/gate/l0-dep-scan.ps1` | L0 新增 `toolchain.py check` 后隐式依赖 Python；缺 Python 时是异常而非清晰违规项 | 环境本身已要求 Python（restore-check/toolchain），本批接受；后续加显式 guard | 接受（后续） |
| C-6 | 中 | 验证面 | 全量 L2 未在白天执行 | 已重注册夜间任务（NextRun 2026-10-22 03:00）；本批只声明编译 0 错 + 定向 6/6，不宣称 L2 通过 | 接受（待夜间） |
| C-7 | 低 | Hub UI | 按钮真实点击未由自动化覆盖 | 已做 CLI 深链/选中/无异常冒烟；点击验证边界如实记录，不做“已点过”表述 | 接受（手工边界） |
| C-8 | 低 | `LiteCodeGen/Editor/BindMenus.cs` | 包级菜单仍硬编码 `LiteGame/工具链` 前缀（既有“零依赖”登记债） | 顶层路径已收口到统一根；包独立形态未改，债继续由原登记跟踪 | 接受（既有债） |

## 复测记录

- 修复后执行的命令与结果：
  - `python .agents/skills/staged-delivery/scripts/workflow.py --run toolchain-platform verify` → **PASS**（L1 exit=0 41450ms；doc 891ms；toolchain-tests 1192ms；toolchain-check 133ms；toolchain-docs-check 107ms）。
  - Unity `recompile_status` → completed / failed=false / 0 `error CS`。
  - 定向 `EditorToolCatalog` EditMode → **6/6**。
  - `python scripts/toolchain.py open gm.test-mode` → Hub 选中 `gm.test-mode`，Console 无异常。
  - `pwsh scripts/gate/test.ps1 -Lane Doc` → doc-lint 0/0 + 生成索引 current（设计文字修正后复跑）。
  - `python scripts/codegen/gen-build-hash.py --check-shared` → OK（459a3145904693b7，110 files）。
- 验证证据：`evidence/verify.json`（2026-10-21T18:10:36+08:00）。

## 评审结论

- 结论: PASS
- 阻塞问题: 0
- 通用性: PASS
- 扩展性: PASS
- 评审人: Qoder（独立代码评审）
- 评审时间: 2026-10-21
