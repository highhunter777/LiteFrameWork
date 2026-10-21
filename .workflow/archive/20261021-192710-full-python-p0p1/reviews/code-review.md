# 代码评审：全量 Python P0/P1

> 运行 full-python-p0p1 · 创建于 2026-10-21

## 变更范围

- 本次覆盖 P0 地基 + P1（hooks / L0），P2–P5 未做。
- 新增：`scripts/_common.py`、`scripts/hooks/shared-source-reminder.py`、`scripts/gate/l0-dep-scan.py`、三组测试。
- 删除：`scripts/hooks/shared-source-reminder.ps1`、`scripts/gate/l0-dep-scan.ps1`。
- 修改：`toolchain.json`/`toolchain.py` fallback/`build-player.ps1`/CI/两个 hook 配置/AGENTS/scripts README/现行文档/skills。
- 范围审计：改动仅 scripts、Docs、AGENTS、CI、hook 配置与构建器的一行命令；未改 Assets/Server/Luban/Tests/ProjectSettings。

## 正确性与边界

- **`_common.py`**：只提供 repo_root/UTF-8/stdout-stderr 前缀/退出码/run；无业务逻辑、无状态。
- **Hook**：逐条移植 PS 的路径模式、消息与双客户端 schema；空输入/坏 JSON/无路径异常均 exit 0；根路径剥离与大小写归一与 PS 一致。
- **L0**：manifest/lock 个人路径与浮动 git、tracked vendored LICENSE、secret 模式、toolchain check 五项；排除名单更新为 `l0-dep-scan.py`；`--project-path` 生效；退出 0/1。
- **注册面**：清单 command、`toolchain.py` fallback、`build-player.ps1`、CI、两个 hook 配置、现行文档全部切 Python；`toolchain.py check` 绿。
- **边界**：L0 真实仓库当前 PASS；若 ParrelSync 个人路径复现，测试只接受该已知项。未跑 L2/Unity/SSH/云侧。
- **回归风险**：hook 配置命令改动可能受客户端 Python 路径影响；已在本地用 Python 直接验证两种 schema，但未在真实 ZCode/Claude 会话中触发一次编辑事件（见未决）。

## 测试充分性

- 红测 18 个断言失败（缺失模块/旧路径），最终 Python 62/62。
- L0 六例：repo 基线、clean fixture、四类违规；hook 五例：ZCode/Claude/资产/未命中/坏 JSON；`_common` 五例。
- 测试未迎合实现：L0 夹具独立建临时 git 仓库；hook 通过 stdin/环境变量驱动；结构测试检查实际路径/命令。
- 未覆盖：真实 CI runner、真实 ZCode/Claude hook 会话、Windows 控制台编码观感；L0 raw 在 CI 的首次运行需观察。

## 通用性与扩展性

- **根因**：共享原语重复 → `_common.py`；hook/L0 是低风险迁移模板，后续 P2–P5 复用同样的“基线/兼容/注册面/测试”流程。
- **扩展点**：新增 Python 入口 import `_common`；注册面由 `toolchain.py check` 校验；hook 消息集中 `_message`；L0 规则集中 `SECRET_PATTERNS/EXCLUDES`。
- **硬编码/特判**：L0 排除仅两处（自身与测试夹具）；hook 路径模式是业务规则本身；未为单次调用加特判。
- **重复/过度抽象**：`_common` 仅四个原语；没有引入 `_common.ps1` 或插件框架。
- **命名/结构**：kebab-case 入口、`_` 内部库符合设计；文件规模小、职责单一。

## 安全与性能

- 安全：hook 不打印密钥；L0 只读 git-tracked 文本；未执行 SSH/云侧；secret 扫描保留原模式。
- 性能：L0 对 repo 的 git ls-files + 文本扫描为秒级；hook 单次启动 Python 约百毫秒，超时 10s 足够。
- 残余：hook 依赖客户端能启动 `python`；若 PATH 不同，需在配置中写绝对解释器（后续按实际客户端验证）。

## 发现的问题与处理

| ID | 严重度 | 位置 | 问题 | 处理 | 状态 |
|---|---|---|---|---|---|
| C-1 | 次要 | `hooks/shared-source-reminder.py` | `_common` 导入在 try 外，若缺失会以非 0 退出，违反 hook“异常静默 0”契约 | 把导入放进 try，并提供空 `configure_stdio` 兜底；hook 5/5 复测 | 已修复 |
| C-2 | 信息 | L0 repo 基线 | 首次实跑命中 ParrelSync 个人绝对路径（存量环境债，PS 同款） | 测试改为 PASS 或仅允许该已知项；随后该路径消失，raw L0 PASS | 已处理 |
| C-3 | 信息 | 工作流 l0 命令 | raw L0 曾因环境债不能作为门禁命令 | `l0` 验证改跑 `test_l0_dep_scan`；raw L0 在 T5 手工复核 PASS | 已处理 |
| C-4 | 信息 | 存量 PS | `check` 仍有 8 条未触碰旧 PS 编码/EOL 告警 | 按 P0–P5“触碰即归一”保留，P2–P5 继续清理 | 遗留（非本次阻塞） |

无阻塞问题。

## 复测记录

- 修复 C-1 后：
  - `python -m unittest scripts.tests.test_hook_reminder scripts.tests.test_common scripts.tests.test_l0_dep_scan -v` → 16/16 OK
  - `workflow.py verify` → PASS（L1 1801、Doc 0/0、L0 测试、Python 62/62、toolchain check、docs current）
- 附加：raw L0 PASS；BuildHash `--check-shared` OK；CI YAML/JSON/PS AST OK。
- 验证证据：`evidence/verify.json`。

## 评审结论

- 结论: PASS
- 阻塞问题: 0
- 通用性: PASS
- 扩展性: PASS
- 评审人: Qoder（agent）
- 评审时间: 2026-10-21
