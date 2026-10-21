# 测试计划：开发工具链平台统一

## 测试策略

- **Python 契约测试（先行红测）**：`scripts/tests/test_toolchain.py`，只用标准库；覆盖清单 v2、CLI 命令面、生成文档、CI/hook surface。先于实现运行，验证失败原因是契约缺失而不是环境/导入错误。
- **C# EditMode 对账测试**：`Assets/Tests/EditMode/EditorToolCatalogEditModeTests.cs`；在 C# 目录/页件骨架落地后加入，先观察“handler 缺失/页工厂为空”的断言失败，再补登记；通过 Unity Pipeline 定向运行。
- **门禁回归**：L1（纯逻辑/纪律扫描）、Doc（链接/索引/状态纪律）、Unity 编译、定向 EditMode、全量 L2（夜间窗口允许时）；BuildHash `--check-shared` 作为不变量。
- **真实入口冒烟**：Hub 打开、搜索、窗口 launch、GM 页、只读动作、CLI 命令复制；CLI `open` 深链；计划任务注册查询。
- **不替代真实 UI 点击**：编辑器窗口内部按钮无法用当前 Pipeline 稳定模拟鼠标；以 Pipeline 打开/选中 + 人工视觉检查为准，无法执行时在证据中明确记录，不宣称“点过”。

## 用例清单

| ID | 层级 | 用例 | 预期 |
|---|---|---|---|
| P1 | Python | 清单含 `schemaVersion/categories/tools/surfaces` 且非空 | 通过 |
| P2 | Python | tool id/handler/menu 唯一；category 已声明；kind 枚举与必填字段完整 | 通过 |
| P3 | Python | 每个工具 docs 路径存在；command 中仓库相对脚本存在 | 通过 |
| P4 | Python | surfaces 声明 ci/nightly-task/zcode-hook/claude-hook；nightly 脚本路径正确 | 通过 |
| P5 | Python | `list --json` 返回分类/工具且包含 baking/codegen 已知 id | 通过 |
| P6 | Python | `--help` 列出 list/check/docs/open/run/verify/build/doctor | 通过 |
| P7 | Python | 未知工具 `open no.such.tool` 非 0 | 通过 |
| P8 | Python | `check` 对仓库清单 0 退出；缺失 command 脚本的非 0；可选计划任务缺失仅 warning（0 退出） | 通过 |
| P9 | Python | `docs --check` 通过；生成页含 DO NOT EDIT/工具 id/surface；二次生成字节不变 | 通过 |
| P10 | Python | CI workflow 中 `scripts/**.ps1|py` 引用都存在；hook 配置引用脚本都存在 | 通过 |
| E1 | EditMode | 清单可读；每个 page/window/action 有 handler；每个 handler 有清单条目；page 工厂非空 | 通过 |
| E2 | EditMode | id/handler/menu 唯一；分类合法；docs 路径存在 | 通过 |
| E3 | EditMode | `LiteGameToolboxWindow.Open(id)` 能选中目标；清单缺失走错误页 | 通过 |
| G1 | L1 | 现有 dotnet 车道全绿（含 DisciplineScanner） | 通过 |
| G2 | Doc | doc-lint 0；生成索引新鲜 | 通过 |
| G3 | Unity | 编译 0 CS；meta GUID 扫描通过 | 通过 |
| G4 | L2 | 定向 `EditorToolCatalog` EditMode 全绿；全量 EditMode/PlayMode 尝试执行 | 通过或记录待夜间 |
| G5 | BuildHash | `gen-build-hash.py --check-shared` 不变 | 通过 |
| M1 | 手工 | Hub 打开/搜索/窗口 launch/GM 页/只读动作/CLI 命令复制 | 通过 |
| M2 | 手工 | `python scripts/toolchain.py open gm.test-mode` 打开 Hub 并选中 | 通过 |
| M3 | 手工 | 夜间任务重注册后 `schtasks` 显示 pwsh + `scripts/gate/nightly-gate.ps1` | 通过 |

## 预期失败证据

- 命令：`python -m unittest discover -s scripts/tests -p "test_toolchain*.py" -v`
- 结果：exit 1，16 个用例中 13 个失败；失败信息为 `manifest v2 field missing: schemaVersion` 与 `can't open file 'scripts/toolchain.py'`，均为契约缺失导致的断言失败，没有导入错误、语法错误或环境故障。
- 证据：`.workflow/active/toolchain-platform/evidence/red.json`（2026-10-21T17:47:22+08:00，exit=1，337ms）。
- C# 红测：在 T7 骨架落地后加入 `EditorToolCatalogEditModeTests`，先断言“清单工具必须有 handler”，在登记未完成时失败；再用 Unity Pipeline 定向运行并记录。此顺序避免在类型不存在时制造编译错误当作“红”。

## 运行方式

```powershell
python -m unittest discover -s scripts/tests -p "test_toolchain*.py" -v
python scripts/toolchain.py check
python scripts/toolchain.py docs --check
pwsh scripts/gate/test.ps1 -Lane L1 -Profile PullRequest
pwsh scripts/gate/test.ps1 -Lane Doc
python scripts/codegen/gen-build-hash.py --check-shared
```

Unity 定向用例（编辑器在跑）：

```powershell
unity command recompile_status --project-path E:\unityProject\Test --proxy-disable
unity command run_tests --mode editor --filter EditorToolCatalog --async_tests true --project-path E:\unityProject\Test --proxy-disable
unity command test_status --project-path E:\unityProject\Test --proxy-disable
```

全量 L2：`pwsh scripts/gate/l2-unity-gate.ps1 -ProjectPath E:\unityProject\Test`（白天不默认触发；按项目夜间约定执行或记录待夜间）。
