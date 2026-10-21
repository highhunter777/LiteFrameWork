# 方案：全量 Python P0/P1（地基 + hooks/L0）

> 运行 full-python-p0p1 · 设计权威：[脚本形态统一专项设计](../../../Docs/设计文档/架构/脚本形态统一专项设计.md) v0.2 §6

## 背景与目标

- **背景**：用户裁决 `scripts/` 全量 Python，最终零 `.ps1`。现有 18 个 `.ps1`，直接全量重写风险过高；按设计 P0–P5 分批，每批行为等价、可独立回滚。
- **本次目标**：交付 **P0 地基 + P1 低风险**：
  1. 新增 `scripts/_common.py`（根推导、输出三档、退出码、子进程 run、UTF-8 初始化），后续所有 Python 入口复用；
  2. 把 `hooks/shared-source-reminder.ps1`、`gate/l0-dep-scan.ps1` 迁为 Python，删除旧 PS，切换两处 hook 配置、CI、清单、构建器与现行文档；
  3. 用测试钉住 L0 行为等价、hook 双客户端输出、旧路径清零。
- **可观察结果**：`python scripts/gate/l0-dep-scan.py` 在 repo 仅命中**已知 ParrelSync 个人绝对路径**（PS 版同样的存量环境债），在四类违规夹具 exit 1；ZCode/Claude hook 均能返回正确 JSON；`toolchain.py check` 绿；现行文档不再引用两个旧 `.ps1`（历史施工卷除外）。

## 需求澄清与假设

- **已确认**：全量 Python 是最终目标；P0–P5 分批，不一次重写；迁移期允许 `@ps-shim`，但 P1 的两个低风险脚本直接切换并删除旧 PS（调用面均在仓库内，已 grep）。
- **假设**：
  1. 运行环境为仓库既有 Python（当前 3.13），P0/P1 只用标准库，不新增依赖；
  2. ZCode/Claude hook 配置支持 `python` 作为命令（现有 toolchain/生成器已依赖 python）；
  3. L0 的 Python 版保持检查项、排除名单（自身改为 `l0-dep-scan.py` + `JoinAdmissionGateTests.cs`）与退出码 0/1；
  4. 本次不触碰 P2–P5（内容/环境/门禁/构建/云侧），不执行 Unity、L2、SSH 或云侧命令；
  5. 旧 PS 删除后不接受旧路径直调；现行文档/CI/构建器同批切换，历史施工卷不回改。

## 范围与非目标

### 范围内

- `scripts/_common.py`：`repo_root()`、`configure_stdio()`、`info/warn/fail`、`run()`、`EXIT_*`。
- `scripts/hooks/shared-source-reminder.py`：移植 PS 的路径模式、消息、ZCode/Claude 输出 schema、异常静默 0。
- `scripts/gate/l0-dep-scan.py`：移植 manifest/lock、vendored license、secret scan、`toolchain.py check`、`--project-path`。
- 删除 `scripts/hooks/shared-source-reminder.ps1`、`scripts/gate/l0-dep-scan.ps1`。
- 注册面：`.zcode/config.json`、`.claude/settings.json`、`.github/workflows/ci.yml`、`scripts/gate/toolchain.json`、`scripts/toolchain.py` 的 fallback、`scripts/build/build-player.ps1` 的 L0 调用。
- 文档：`scripts/README.md`、`AGENTS.md`、`Docs/项目说明/05`、`13`、`Docs/修改指南.md`、`Docs/工具链/技术支持.md`、`Docs/开发导航/代码地图.md`、`Docs/环境/环境恢复指南.md`、`Docs/待办总览.md`、`.agents/skills/batch-wrapup/SKILL.md`、相关设计文档中的 L0 现行引用。
- 测试：`scripts/tests/test_common.py`、`test_l0_dep_scan.py`、`test_hook_reminder.py`，以及 `test_toolchain.py` 的清单/路径断言。

### 非目标

- 不迁移 `content/*`、`restore-check`、`test/nightly/l2`、`build-player/player-smoke`、`deploy/*`、`local-chain`（P2–P5）。
- 不引入 `cryptography`/HTTP/进程查询等 P2+ 依赖；不建 `_ssh_host.py`/`_local_cdn.py`（分别 P5/P2）。
- 不改 L0 检查口径、不降低 secret 规则、不改 CI 车道/测试分层。
- 不跑 Unity/L2、不发起 SSH/云侧部署；不提交（提交另行确认）。

## 现状与约束

- **hook**：PS 读 stdin JSON，取 `tool_input.file_path`/`notebook_path`；按 `ZCODE_PROJECT_DIR`/`CLAUDE_PROJECT_DIR` 选 JSON schema；未命中/异常静默 0。当前配置两个客户端均调 `pwsh ... shared-source-reminder.ps1`。
- **L0**：PS 106 行，检查 `Packages/manifest.json`/`packages-lock.json` 的个人绝对路径与浮动 git 依赖、tracked vendored 包 LICENSE、git-tracked 文本密钥模式，最后调用 `toolchain.py check`；退出 0/1。CI、`build-player.ps1`、清单、文档多处引用。
- **依赖面**：`scripts/toolchain.py` 有一处硬编码 L0 PS 命令 fallback；`toolchain.json` `quality.l0` command；`Docs/工具链/工具索引.md` 由清单生成。
- **约束**：
  - Python 输出统一 UTF-8；Windows 控制台/重定向一致。
  - L0 不得把自身源码的新密钥模式文本判成 secret；排除名单从 `l0-dep-scan.ps1` 改为 `l0-dep-scan.py`。
  - hook 任何异常必须 exit 0，不阻塞客户端。
  - 删除 PS 前必须确认这些路径不再被现行脚本/CI/文档引用（历史施工记录除外）。
  - 不新增第三方依赖；`_common.py` 只用标准库。

## 候选方案与取舍

| 方案 | 核心思路 | 优点 | 代价/风险 | 结论 |
|---|---|---|---|---|
| A P0+P1（推荐） | 先落地 `_common.py`，再切 hooks/L0，删除两 PS | 风险低、可独立验收；为 P2–P5 建立公共地基；直接减少 2 个 PS | 需同批改 CI/hook/构建器/清单/文档，注册面较多 | **采用** |
| B 只做 P0 | 只建 `_common.py` | 最小风险 | 没有可见迁移收益，下一批仍要重做验证 | 不采用 |
| C P0–P5 一次做完 | 18 个脚本全迁 | 一步到位 | 体量过大、Unity/云侧验证成本高、回滚困难 | 不采用 |

**推荐 A 的理由**：P1 的两个脚本无 Unity/云侧依赖，行为可用夹具精确对照；同时把 CI/hook/清单/文档的迁移套路跑通，后续 P2–P5 可按同一模板复用。

## 设计要点

### 1. `_common.py`

```python
repo_root() -> Path          # scripts/ 的上一级（仓库根）
configure_stdio() -> None    # stdout/stderr UTF-8
info(tool, message)          # stdout [tool] message
warn(tool, message)          # stderr [tool] WARN: ...
fail(tool, message)          # stderr [tool] FAILED: ...; 返回/抛出约定由调用方定
run(cmd, cwd=None, env=None) # subprocess.run，返回 CompletedProcess；不吞退出码
EXIT_OK=0, EXIT_FAIL=1, EXIT_USAGE=2, EXIT_NOT_FOUND=4, EXIT_EDITOR_UNAVAILABLE=5
```

- 只做共享原语，不引入业务逻辑；后续 `_ssh_host.py`/`_local_cdn.py`/各入口复用。
- `run` 默认继承 stdout/stderr，便于 CLI 透传；需要捕获的调用方自行传 `capture_output=True`。
- 不做 `_common.ps1`；迁移期存量 PS 的根推导随 P 批删除。

### 2. Hook 迁移

- 新文件 `scripts/hooks/shared-source-reminder.py`，逐条移植 PS 的路径判定与中文提示。
- 输出：
  - ZCode：`{"additionalContext": "..."}`；
  - Claude：`{"hookSpecificOutput":{"hookEventName":"PostToolUse","additionalContext":"..."}}`。
- 读取 stdin；空输入/无路径/任何异常 → exit 0 且不输出。
- `.zcode/config.json`：`"command": "python"`，args 加 `-X utf8` + 新 `.py` 路径；`.claude/settings.json`：`python -X utf8 ".../shared-source-reminder.py"`。
- 删除旧 PS；`toolchain.py check` 的 hook surface 检查会验证新路径存在。

### 3. L0 迁移

- 新文件 `scripts/gate/l0-dep-scan.py`，argparse `--project-path`（默认 `_common.repo_root()`）。
- 逐项保持：
  1. `Packages/manifest.json` + `packages-lock.json`：`file:[A-Za-z]:`、`.git` 未 pin 40-hex；
  2. `Packages/*/package.json` 且 `git ls-files` 跟踪的 vendored 包必须有 `LICENSE`/`License*`；
  3. `git ls-files` 文本文件 secret 模式（私钥/AKIA/ghp_/硬编码凭据），排除 `l0-dep-scan.py` 与 `JoinAdmissionGateTests.cs`；
  4. 调用 `toolchain.py check`，非 0 记 violation；
  5. 输出 `[gate/l0]` 前缀，PASS/FAIL 与退出码 0/1。
- 删除旧 `l0-dep-scan.ps1`。
- 外部面同批：
  - `toolchain.json` `quality.l0.command` → `["python","scripts/gate/l0-dep-scan.py"]`；
  - `scripts/toolchain.py` fallback → Python；
  - `build-player.ps1` 调 `python ... --project-path ...`；
  - CI L0 step → `python scripts/gate/l0-dep-scan.py --project-path ${{ github.workspace }}`；
  - 现行文档/AGENTS/skills 的 L0 命令改为 Python。

### 4. 测试先行

- 新测试在实现前必须失败：
  - `test_common.py`：`_common` 不存在 → AssertionError；
  - `test_l0_dep_scan.py`：`scripts/gate/l0-dep-scan.py` 不存在 → 断言失败；
  - `test_hook_reminder.py`：`shared-source-reminder.py` 不存在 → 断言失败；
  - `test_toolchain.py`：清单仍指旧 `.ps1`、hook 配置仍指旧 `.ps1`、旧文件仍存在 → 断言失败。
- 实现后覆盖：
  - L0 repo PASS；临时仓库的 personal path/floating git/missing license/secret 四类违规 exit 1；
  - hook ZCode/Claude/未命中/invalid JSON 四类；
  - `toolchain.py check`、`docs --check`、Doc lane、L1。

## 通用性与扩展性

- **根因**：每批迁移重复“根推导/输出/退出码/子进程”原语；`_common.py` 一次收口，后续 P2–P5 不复制。
- **扩展点**：新增 Python 入口只 `import _common`；新增 hook/CI 注册面由 `toolchain.py check` 的 surface 检查自动覆盖。
- **避免硬编码**：L0 的排除名单只在脚本内一处；CI/构建器/清单命令都指向同一路径；不维护第二份 L0 规则表。
- **行为等价**：hook 消息逐条对照 PS；L0 检查项/排除/退出码逐项对照；不做“顺手改规则”。
- **不过度抽象**：`_common.py` 只放四个原语；不建 `_common.ps1`、不做插件框架。

## 测试策略

- **红测**：新测试文件 + `test_toolchain.py` 断言，先运行观察缺失/旧路径失败；记录 `evidence/red.json`。
- **L0 夹具**：
  - repo 实跑 `python scripts/gate/l0-dep-scan.py`：当前树 PASS；若 `Packages/manifest.json` / `packages-lock.json` 的 ParrelSync 个人绝对路径复现，测试只允许该已知项；断言无其他违规类型；
  - 临时 git 仓库：个人绝对路径、浮动 git、缺 LICENSE、secret 各一 → exit 1 且消息命中；
  - `--project-path` 生效。
- **Hook 夹具**：stdin JSON + `ZCODE_PROJECT_DIR`/`CLAUDE_PROJECT_DIR`，断言输出 schema 与消息；空/坏 JSON 静默 0。
- **注册面**：`toolchain.py check`、`docs --check`、CI YAML 文本引用存在、hook 配置路径存在。
- **基线**：`pwsh scripts/gate/test.ps1 -Lane L1 -Profile PullRequest`；Doc lane；不跑 L2/Unity/云侧。
- **命令**：
  ```powershell
  python -m unittest discover -s scripts/tests -p "test_*.py" -v
  python scripts/gate/l0-dep-scan.py
  python scripts/toolchain.py check
  python scripts/toolchain.py docs --check
  python scripts/gate/doc-lint.py
  pwsh -NoProfile -File scripts/gate/test.ps1 -Lane L1 -Profile PullRequest
  ```

## 验收标准

- [ ] **AC1 地基**：`_common.py` 提供 repo_root/configure_stdio/info/warn/fail/run/EXIT_*；单测覆盖；后续入口可直接 import。
- [ ] **AC2 Hook**：ZCode/Claude 两种 schema 正确；未命中/异常静默 0；旧 PS 删除；两个 hook 配置指向 Python。
- [ ] **AC3 L0**：repo PASS；若 ParrelSync 个人路径存量复现，测试只允许该已知项；四类违规夹具 exit 1；`--project-path` 生效；旧 PS 删除。
- [ ] **AC4 注册面**：`toolchain.json`/`toolchain.py` fallback/CI/build-player/现行文档/AGENTS/skills 全部改 Python；`toolchain.py check` 绿；工具索引 current。
- [ ] **AC5 无旧路径**：现行脚本/CI/文档不再引用两个旧 `.ps1`（历史施工卷除外）；旧文件不存在。
- [ ] **AC6 行为等价**：L0 检查项/排除/退出码与 PS 对照；hook 消息与 PS 逐条对照；不改业务口径。
- [ ] **AC7 门禁**：Python 测试全绿；L1 1801；Doc 0/0；BuildHash `--check-shared` 不受影响。
- [ ] **AC8 范围**：只改 scripts/Docs/AGENTS/CI/hook/少量构建器命令；不碰 Unity、共享源集、协议、表、云侧。

## 风险与回滚

| 风险 | 监控/发现 | 回滚 |
|---|---|---|
| Hook 客户端不接受 `python` 命令/参数 | 两个客户端各跑一次夹具；检查 stdout JSON；ZCode 配置 schema | 恢复旧 PS 与配置（单文件），保留新脚本不启用 |
| L0 行为漂移（漏检/误报） | repo PASS + 四类违规夹具 + 与 PS 对照；CI 首次跑 | 恢复 `l0-dep-scan.ps1` 与旧命令；新脚本保留待修 |
| CI matrix 无 python/路径差异 | CI workflow 文本检查 + 本地 `python` 跑；win/ubuntu 语义 | 回退 CI step；L0 仍可本地 PS 运行 |
| 删除旧 PS 后外部文档/调用面遗漏 | grep 旧路径（排除历史卷）；`toolchain.py check` surface | 恢复旧 PS 一版；按批补引用 |
| `_common.py` 过度抽象 | 只允许四个原语；后续入口不复制业务 | 直接删减函数，不影响入口 |
| 与 P2–P5 依赖冲突 | 本次只标准库；不建 `_local_cdn`/`_ssh_host` | 后续批再引入，无回滚数据 |

**回滚**：恢复两个 `.ps1` 与 hook/CI/清单/构建器命令即可回到 P0 前状态；`_common.py` 无消费者时可删除。无数据迁移。
