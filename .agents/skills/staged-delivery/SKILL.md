---
name: staged-delivery
description: "Turn a feature, refactor, or bugfix request into a gated delivery run: plan, reviewed task breakdown, failing tests first, implementation, real-command verification, code review, writeback, and archived records. Use when the user requests 分阶段交付、方案与任务清单、任务评审、先写测试再施工、施工记录、代码评审、任务归档，or a repeatable plan-first/TDD workflow for any repository or language. Stages, approval gates, templates, and verification commands are configurable through .workflow/config.json."
---

# Staged Delivery

## 概览

把一次非平凡交付拆成可验证的阶段，每个阶段先落盘产物，再通过门禁推进：

`提出方案 → 实施任务清单 → 评审任务 → 先写测试 → 正式施工 → 测试代码 → 评审代码 → 回写与收尾 → 归档`

- 机器状态：`.workflow/active/<slug>/state.json`
- 人读产物：`plan.md`、`tasks.md`、`reviews/task-review.md`、`test-plan.md`、`build-log.md`、`reviews/code-review.md`
- 验证证据：`evidence/verify.json`、`evidence/red.json`
- 归档：`.workflow/archive/<时间戳>-<slug>/` 与 `.workflow/archive/INDEX.md`

`tasks.md` 由 CLI 生成，不要手改；其余 Markdown 由你按产物质量要求填写。

## 何时使用

使用本工作流处理需要多步设计、实现与验证的任务，例如新功能、跨文件重构、缺陷修复、接口变更、迁移或需要留下施工审计记录的交付。

用户明确要求以下内容时必须使用：分阶段实施、先出方案和任务清单、评审任务、先写测试、施工记录、代码评审、归档。

若只是单行修改、纯问答或用户明确要求快速处理，不要强行套用完整流程；先确认是否使用轻量方式。

## 执行契约

- 阶段只能通过 `advance` 推进；不要手改 `state.json`，不要跳过门禁。
- 产物必须写实：方案来自代码与约束，评审来自实际检查，验证来自真实命令输出；禁止伪造证据，禁止删除用例、降低断言强度、跳过测试或改动门禁。
- 先解决根因，再谈实现。不能只针对当前调用方打补丁、堆特判或硬编码；在方案与代码评审中记录通用性与扩展点。
- 保持最小改动。新增抽象必须有当前需求支撑，不为假想需求设计。
- 任务状态与施工记录随做随记；变更要回退或新增任务，不要静默扩大范围。
- 阶段配置、命令、模板、审批均可配置。不要修改脚本去适配某个项目；优先改 `.workflow/config.json`。

## 准备

### 1. 定位工作流脚本

`<skill-dir>` 指本技能目录。用户级安装通常是 `~/.qoder/skills/staged-delivery`，项目级是 `.qoder/skills/staged-delivery`。不确定时先定位 `SKILL.md` 所在目录。

```bash
python "<skill-dir>/scripts/workflow.py" --help
```

后文用 `wf <子命令>` 表示上面的完整调用；执行时替换为实际脚本路径。

### 2. 探测并配置真实命令

先读仓库，优先复用 CI 或项目文档中的命令：

- 项目说明、`AGENTS.md`、CI 配置、构建脚本、包管理清单
- 测试框架与现有测试目录、lint/typecheck/build 命令、monorepo 子目录
- `.workflow/config.json` 是否已存在；存在时保留其阶段与自定义配置

初始化会创建 `.workflow/config.json`。随后编辑其中的 `commands`（完整验证）与 `red_commands`（只运行新增测试、预期失败）。配置为空的 `commands` 会阻止验证阶段通过。具体探测方法和多语言示例见 `references/bootstrap.md`。

```json
{
  "commands": [
    { "name": "tests", "cmd": ["python", "-m", "pytest", "-q"], "timeout": 900 },
    { "name": "lint", "cmd": ["ruff", "check", "."], "timeout": 300, "cwd": "backend" }
  ],
  "red_commands": [
    { "name": "new-tests", "cmd": ["python", "-m", "pytest", "tests/test_feature.py", "-q"] }
  ]
}
```

命令是示例，必须替换为当前仓库实际可用的命令。字符串命令通过 shell 执行，数组命令不经过 shell；`cwd` 用于 monorepo 子目录。

### 3. 初始化运行

```bash
wf init <slug> --title "<交付标题>"
```

`slug` 使用小写字母、数字和连字符。多个进行中的运行必须加 `--run <slug>`；`--run` 和 `--root` 可放在子命令前后。

### 4. 配置项目模板（可选）

`.workflow/templates/<name>` 会覆盖技能自带模板。配置中的产物可写 `"template": "plan.md"`；没有模板时会按配置章节自动生成骨架。建议在 `init` 前放好模板。

## 阶段与门禁

| # | 阶段 | 必须产物 | 通过条件 | 推进命令 |
|---|---|---|---|---|
| 1 | 提出方案 | `plan.md` | 方案章节完整、无占位内容 | `advance` |
| 2 | 实施任务清单 | `tasks.md` | 至少一个任务；字段与依赖合法 | `advance` |
| 3 | 评审任务 | `reviews/task-review.md` | 结论 PASS，阻塞问题 0 | `advance` |
| 4 | 先写测试 | `test-plan.md`；配置了 red_commands 时还需 `evidence/red.json` | 已观察到预期失败；或记录无法先失败的替代验证 | `advance` |
| 5 | 正式施工 | `build-log.md` | 所有任务完成或取消，施工记录完整 | `advance` |
| 6 | 测试代码 | `evidence/verify.json` | 配置的验证命令全部通过且未过期 | `verify` → `advance` |
| 7 | 评审代码 | `reviews/code-review.md` | 结论、通用性、扩展性均 PASS，阻塞问题 0，验证有效 | `advance` |
| 8 | 回写与收尾 | `build-log.md`、`tasks.md` | 交付摘要、验证结果、未决事项、文件清单、后续建议完整 | `advance` |
| - | 归档 | `ARCHIVE.md`、索引 | 运行已处于 done | `archive` |

每次推进前可运行 `check`；查看详情用 `status`，机器读取用 `status --json`。默认阶段不需要人工审批；若配置了 `"approval": true`，必须取得用户批准后运行 `advance --approve <审批人>`，不得自行批准。

## 逐阶段操作

### 1. 提出方案

先探索代码、数据与依赖，不要凭标题直接写实现。信息不足时先自行调查；只有需求意图、优先级、验收口径或外部约束无法从仓库与现有资料确定时，提 1-3 个高影响问题，集中询问并记录答复。不要用提问代替可自行完成的调查。

未获得答复但需要继续时（例如用户要求自主执行），在方案中写出显式假设、影响与风险，不要把猜测写成已确认需求。

填写 `plan.md`：

- 背景、可观察目标、范围与非目标、约束
- 需求澄清与假设：待确认问题、已确认答复、采用假设及其影响
- 至少两个候选方案及取舍，说明推荐理由
- 接口、数据、关键流程、失败与边界
- 通用性与扩展性：根因、复用点、如何避免硬编码与特判
- 测试策略与可观察的验收标准
- 风险、监控方式与回滚路径

阶段内不修改生产代码。运行 `check` 后 `advance`。

### 2. 实施任务清单

用 `task add` 建立任务，禁止手改 `tasks.md`：

```bash
wf task add --title "<任务>" --acceptance "<可验证验收>" --verify "<验证命令或检查>" --deps T1,T2 --files path/a,path/b
```

任务应可独立执行、独立验证；每项都写清验收与验证方式；依赖顺序合理。验收标准必须能追溯到方案中的 AC。任务过大时拆分，不按“改某个文件”切分。运行 `check` 后 `advance`。

### 3. 评审任务

填写 `reviews/task-review.md`，像独立评审者一样检查：

- 方案验收标准是否全部映射到任务
- 任务粒度、依赖、顺序、可测试性、风险处理
- 是否混入范围外改动、硬编码或特判式补丁
- 测试策略是否足以覆盖失败路径与边界

发现问题先修任务与方案，再重写评审结论。禁止未检查就填 `结论: PASS` / `阻塞问题: 0`。需要重大调整时用 `rewind` 回退并记录原因。

### 4. 先写测试

按仓库现有测试框架写测试并运行，确认它们因预期原因失败：

- 测试覆盖验收标准、边界与错误路径，不迎合尚未写出的实现
- 优先使用可只跑新增测试的 `red_commands`，运行 `wf verify --red` 记录证据
- 失败必须是断言失败，而不是导入错误、拼写错误或环境故障
- 无法先写失败测试时（文档、配置、基础设施），在 `test-plan.md` 写明替代验证与理由；仍要实际执行可用的检查
- 填写 `test-plan.md`：测试策略、用例清单、预期失败证据、运行方式

禁止删除用例、降低断言强度或跳过测试。运行 `check` 后 `advance`。

### 5. 正式施工

按依赖顺序执行任务，一次专注一个任务：

```bash
wf task start T1
# 实现、运行相关测试、检查边界
wf task done T1 --evidence "<命令、结果、关键文件>"
```

- 先修根因，复用既有模式与扩展点；不要只对当前输入写特判
- 不顺手重构无关代码；不做任务清单之外的静默扩展
- 新发现的必要工作用 `task add` 加入并记录；目标、验收或方案发生变化时用 `rewind` 回到相应阶段
- 把关键决定、偏差、问题与修复写进 `build-log.md`
- 阻塞时用 `task block`，解决后 `unblock`；任务取消用 `task drop --reason`

阶段门禁要求所有任务为完成或取消。运行 `check` 后 `advance`。

### 6. 测试代码

在 `.workflow/config.json` 配置并运行真实验证命令：

```bash
wf verify
```

`verify` 会顺序执行 `commands`，记录退出码、耗时、输出尾部与指纹，写入 `evidence/verify.json`。全部通过才能推进。失败时阅读证据、修复根因、重新运行；不要修改门禁或伪造证据。

任务变更会使验证结果过期；配置命令变化也会使指纹失效。在评审阶段修改代码后同样必须重新运行 `verify`。不要让输出证据包含密钥、令牌或隐私数据。

### 7. 评审代码

基于实际 diff 与文件内容评审，而不是复述方案。填写 `reviews/code-review.md`：

- 变更是否完整映射任务与验收标准，是否有范围外改动
- 主路径、边界、错误路径、并发/资源风险
- 测试是否真的验证行为，是否被弱化或只覆盖实现细节
- 通用性：是否解决根因而非局部补丁；是否只对当前调用方生效；是否存在硬编码、特判、魔法值或重复逻辑
- 扩展性：新增同类场景时是否只需通过既有扩展点接入；接口、数据结构与依赖边界是否可演进；是否存在过度抽象或为假想需求增加的结构
- 安全：输入边界、注入、权限、敏感信息
- 性能与兼容性

发现问题按严重度记录并修复；阻塞问题必须为 0。`通用性` 与 `扩展性` 结论必须为 PASS；任一 FAIL 都按阻塞处理。修复后重新运行 `wf verify`，并在复测记录中写明命令与结果。全部结论为 PASS 后 `advance`。

### 8. 回写与收尾

补齐 `build-log.md` 的交付摘要、验证结果、未决事项、文件清单与后续建议；确认 `tasks.md` 中完成、取消及证据均准确。必要时用 `task reopen` / `task drop` 修正，再重新验证。运行 `advance` 完成运行。

### 归档

```bash
wf archive
```

仅允许归档已完成的运行。归档前会重新校验配置中全部阶段的产物、评审结论与验证证据，失败则拒绝归档；通过后移动整个运行目录，生成 `ARCHIVE.md` 并追加索引。归档不自动提交 git；是否提交归档目录由用户决定。

## 变更控制

- 范围内的小变更：直接新增任务，在施工记录中说明原因。
- 目标、验收标准、设计或范围发生实质变化：更新方案后 `rewind <阶段> --reason "<原因>"`，重新评审任务与测试策略；`rewind` 会使既有验证过期。
- 发现评审阻塞问题：回到对应阶段修复，不能把 `结论` 改成 PASS 绕过。
- 任何回退、取消范围或未决风险都必须出现在评审或施工记录中。

## 定制与扩展

默认 8 个阶段是配置数据，不是脚本内写死的流程。通过 `.workflow/config.json` 可以：

- 在任意位置新增阶段，组合门禁原语：`artifacts`、`verdicts`、`min_tasks`、`require_tasks_complete`、`require_verification`、`require_red_verification`、`approval`
- 为新阶段指定项目和技能模板，或让初始化按 `sections` 自动生成骨架
- 增加验证命令、`red_commands`、`cwd` 与超时
- 将高风险阶段设为人工审批

示例：在代码评审后加入安全检查阶段：

```json
{
  "id": "security",
  "title": "安全检查",
  "artifacts": [
    { "path": "security.md", "sections": ["检查范围", "检查结果"] }
  ],
  "verdicts": [
    { "path": "security.md", "fields": { "结论": ["PASS"] } }
  ],
  "approval": true
}
```

对进行中的运行修改配置时，优先把新阶段放在当前阶段之后，避免已完成阶段与新顺序错位。配置字段、门禁语义与任务状态机详见 `references/protocol.md`。

## 常用命令

```bash
wf status
wf status --json
wf check
wf advance
wf task list --json
wf task start T1
wf task done T1 --evidence "..."
wf verify
wf verify --red
wf rewind breakdown --reason "..."
wf archive
wf list
wf config show
```

多个运行并存时加 `--run <slug>`。`status` 与 `check` 的 JSON 输出可被其他自动化消费。

## 资源

- `references/protocol.md`：配置 schema、门禁原语、任务状态机、验证指纹、归档与扩展规则
- `references/bootstrap.md`：命令探测、红测配置、多语言示例、项目模板
- `references/artifact-quality.md`：各阶段产物质量标准、反模式与评审清单
- `assets/templates/`：方案、测试计划、施工记录、任务评审、代码评审模板
- `assets/config.default.json`：默认阶段与门禁
- `scripts/workflow.py`：状态机 CLI（Python 3.10+，仅标准库）
- `scripts/tests/test_workflow.py`：CLI 行为测试，可用 `python -m unittest discover -s scripts/tests` 运行
