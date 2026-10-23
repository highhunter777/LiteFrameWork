# 工作流协议

本文件说明 `scripts/workflow.py` 的状态机、门禁语义和扩展方式。脚本是执行层的权威实现；本文件用于判断如何配置和排错。

## 1. 目录与文件

```text
.workflow/
├── config.json                  # 项目级工作流配置，首次 init 创建后不再覆盖
├── templates/                   # 可选：项目模板，覆盖技能自带模板
├── active/
│   └── <slug>/
│       ├── state.json           # 机器状态，不要手改
│       ├── plan.md
│       ├── acceptance.md        # 用户确认验收标准
│       ├── tasks.md             # 由 CLI 生成，不要手改
│       ├── test-plan.md
│       ├── build-log.md
│       ├── reviews/
│       │   ├── plan-review.md   # 独立子代理方案评审
│       │   ├── task-review.md
│       │   └── code-review.md
│       └── evidence/
│           ├── verify.json
│           └── red.json
└── archive/
    ├── INDEX.md
    └── <时间戳>-<slug>/
        ├── ARCHIVE.md
        └── ...（原运行目录内容）
```

配置属于项目而非单次运行。`init` 在 `.workflow/config.json` 不存在时从技能默认配置创建；已存在时原样复用。自定义阶段和命令应在项目内维护。

## 2. 阶段推进

阶段顺序完全由 `config.stages` 数组决定。一个阶段的门禁检查的是**当前阶段的退出条件**：产物与评审是否满足、任务是否完成、验证是否通过、是否需要审批。

- `init`：创建运行，当前阶段为第一个阶段。
- `advance`：检查当前阶段门禁；通过后把当前阶段写入 `completed_stages`，进入下一阶段；最后一个阶段通过后运行进入 `done`。
- `check`：执行相同门禁检查，但不推进；有阻塞项时退出码为 1。
- `status`：报告阶段、门禁、任务、验证和下一步；退出码始终为 0（运行无法解析等错误除外）。
- `rewind`：回到当前阶段之前的某个阶段，需要 `--reason`；写入历史，截断后续完成记录，并使验证证据过期。
- `archive`：只接受 `done` 状态的运行；移动目录、生成 `ARCHIVE.md`、追加归档索引。
- 归档后的运行只读。`done` 但尚未归档的运行可以 `rewind` 重新打开；任务和验证命令在 `done` 状态下不可写。

常见默认阶段：

1. `propose` 提出方案
2. `review-plan` 子代理评审方案
3. `confirm-acceptance` 用户确认验收标准（`approval: true`）
4. `breakdown` 实施任务清单
5. `review-tasks` 评审任务
6. `tests-first` 先写测试
7. `implement` 正式施工
8. `verify` 测试代码
9. `review-code` 子代理评审代码
10. `writeback` 回写与收尾

## 3. 阶段门禁原语

任意阶段可以组合以下字段。除 `id`、`title` 外都可省略。

| 字段 | 语义 |
|---|---|
| `artifacts` | 要求存在并满足章节校验的 Markdown 产物 |
| `verdicts` | 要求产物中存在指定字段，且值命中允许值 |
| `min_tasks` | 任务总数下限 |
| `require_tasks_complete` | 所有任务必须为 `done` 或 `cancelled`；`todo`、`doing`、`blocked` 都会阻塞 |
| `require_verification` | `commands` 非空，最近一次验证通过、证据存在、命令指纹一致且未过期 |
| `require_red_verification` | 配置了 `red_commands` 时，必须有“观察到预期失败”的证据；未配置时自动跳过 |
| `approval` | 为 `true` 时，`advance` 必须携带 `--approve <审批人>` |

审批只应来自用户或明确授权人。`approved_by` 会写入完成记录；不要把 `--approve` 当作自我盖章。默认配置中只有 `confirm-acceptance`（用户确认验收标准）设置了 `approval: true`——这一步的审批人就是用户本人。

“子代理评审”不是脚本原语：CLI 无法替你开会话。它的守卫面是过程留痕——`review-plan` 与 `review-code` 的 `verdicts` 都要求 `评审方式: 独立子代理`，主代理自评无法通过门禁；评审人、子代理标识与评审输入写在评审产物的“评审输入”章节，供用户核对。

## 4. 产物与章节校验

每个 `artifact` 形如：

```json
{
  "path": "plan.md",
  "template": "plan.md",
  "sections": ["背景与目标", "验收标准"]
}
```

- `path` 必须是运行目录内的相对路径，不能包含 `..` 或盘符。
- `sections` 为必须存在的 Markdown 标题文本。匹配时忽略标题层级、编号前缀和末尾冒号；标题必须完全一致。
- 章节正文会去除 HTML 注释后检查：
  - 不能包含 `validation.placeholders` 中的任一占位串（默认 `待填写`）；
  - 去除注释、空白和 Markdown 标记后的可见字符数不能小于 `validation.min_section_chars`（默认 1）。
- 标题解析会跳过围栏代码块中的 `#` 行。
- 同一章节标题出现多次时使用第一次出现的正文。
- `tasks.md` 特殊：由任务数据生成，不应手改。

`verdicts` 形如：

```json
{
  "path": "reviews/task-review.md",
  "fields": {
    "结论": ["PASS", "通过"],
    "阻塞问题": ["0", "无"]
  }
}
```

- 匹配形如 `- 结论: PASS` 或 `结论：PASS` 的行，取第一次匹配。
- 比较前会去除常见 Markdown 标记、引号和尾部标点，并忽略大小写。
- 报告结论时必须使用配置中的允许值；默认评审模板要求 `结论: PASS` 且 `阻塞问题: 0`。
- 默认方案评审（`reviews/plan-review.md`）还要求 `评审方式: 独立子代理` 与 `验收标准: PASS`——验收标准必须逐条可观察、可判定。
- 默认代码评审还要求 `评审方式: 独立子代理`、`通用性: PASS` 与 `扩展性: PASS`；任一字段不通过都会阻止进入回写阶段。

## 5. 任务状态机

| 当前状态 | 操作 | 结果 | 约束 |
|---|---|---|---|
| `todo` | `start` | `doing` | 所有依赖必须为 `done` 或 `cancelled` |
| `todo` / `doing` | `block` | `blocked` | 必须提供理由 |
| `blocked` | `unblock` | `todo` | - |
| `doing` | `done` | `done` | 必须提供非空 `--evidence` |
| `done` / `cancelled` | `reopen` | `todo` | 必须提供理由；清空旧证据 |
| `todo` / `doing` / `blocked` | `drop` | `cancelled` | 必须提供理由；已完成任务不能直接取消 |

附加规则：

- 任务 ID 自动分配为 `T1`、`T2`……，不复用。
- 依赖必须指向已存在任务；不能依赖自身；新增或更新依赖时检测循环。
- `cancelled` 视为依赖已满足，并计入“任务已收尾”；被取消的原因应写入施工记录和交付摘要。
- 任何任务变更（新增、更新、开始、完成、阻塞、解除、重开、取消）都会使普通验证结果标记为过期。红测证据是“实现前曾失败”的历史记录，不因后续任务完成而失效；它只在红测命令配置变化或回退重新打开“先写测试”阶段时过期。

## 6. 命令与验证

配置中的命令形状：

```json
{
  "name": "unit-tests",
  "cmd": ["python", "-m", "pytest", "-q"],
  "timeout": 900,
  "cwd": "services/api"
}
```

- `name` 在同类命令列表中唯一。
- `cmd` 为字符串时经 shell 执行；为数组时不经过 shell，更适合跨平台与精确参数。需要 shell 特性（管道、环境变量前缀、`.cmd` 等）时使用字符串。
- `timeout` 为单条命令超时秒数，默认取 `timeout_seconds`（600）。
- `cwd` 是相对工作区根目录的路径，用于 monorepo。
- 配置命令会在本地执行；不要把不可信内容写入命令配置。

`verify`（普通验证）：

- 顺序执行全部 `commands`，每条命令执行完再执行下一条。
- 全部退出码为 0 才算整体 PASS；超时、启动失败、非零退出均为 FAIL。
- 即使前面的命令失败，也会继续执行后续命令以收集完整信息。
- 写入 `evidence/verify.json`，并在 `build-log.md` 的“验证结果”章节追加一行摘要。

`verify --red`（先行测试）：

- 执行全部 `red_commands`，只要至少一条命令以非零退出（且不是超时）就视为观察到预期失败，整体 PASS。
- 若全部命令退出码为 0，整体 FAIL，提示“未出现预期失败”。
- 失败输出必须是预期断言导致的；若是导入错误、语法错误或环境错误，应修正测试或命令，不能视为有效的红测。

证据记录包含：时间、整体结果、命令指纹、每条命令的退出码、耗时、超时标志、错误、输出尾部。输出只保留末尾有限行和字符，避免污染上下文；仍可能包含敏感信息，配置命令时避免打印密钥。

验证过期条件（普通 `verify`）：

1. 任务发生任何变更；
2. `commands` / `red_commands` 配置发生变化（命令指纹不同）；
3. 证据文件被删除或路径无效；
4. `rewind` 之后。

红测证据作为历史记录保留：不因任务完成而失效，但在 `red_commands` 配置变化或 `rewind` 后需要重新获取。

源文件修改不会被自动检测。任何代码或测试改动后，都必须重新运行 `verify`；评审阶段修复问题后尤其如此。

## 7. 配置结构

```json
{
  "schema_version": 1,
  "validation": {
    "placeholders": ["待填写"],
    "min_section_chars": 1
  },
  "timeout_seconds": 600,
  "commands": [],
  "red_commands": [],
  "stages": [
    {
      "id": "propose",
      "title": "提出方案",
      "artifacts": [
        { "path": "plan.md", "template": "plan.md", "sections": ["背景与目标"] }
      ],
      "verdicts": [],
      "min_tasks": 0,
      "require_tasks_complete": false,
      "require_verification": false,
      "require_red_verification": false,
      "approval": false
    }
  ]
}
```

约束：

- `schema_version` 必须为 1。
- 阶段 `id` 唯一，匹配 `[a-z0-9][a-z0-9-]*`；标题非空。
- 产物与模板路径必须是运行目录内的相对路径。
- 命令、red 命令、章节名、允许值在加载时校验。
- 未识别的配置键会被保留，不影响运行；适合向前扩展。
- 阶段顺序就是数组顺序。进行中的运行调整顺序可能导致已完成阶段错位；优先在 `init` 前定稿，或只把新阶段追加在当前阶段之后。

模板查找顺序：

1. `.workflow/templates/<template>`（项目模板）
2. 技能 `assets/templates/<template>`
3. 若未指定模板，按产物 `sections` 自动生成“待填写”骨架

显式指定的模板如果两处都不存在，`init` 失败并清理未完成的运行目录。

## 8. 状态、历史与归档

`state.json` 主要字段：

- `schema_version`、`slug`、`title`、`status`（`active` / `done` / `archived`）
- `current_stage`、`completed_stages`
- `history`：初始化、推进、回退、归档事件，含时间和审批人
- `tasks`：任务数据与证据
- `verification`、`red_verification`：最近一次验证摘要、证据路径、命令指纹、是否过期
- 时间戳：`created_at`、`updated_at`、`completed_at`、`archived_at`

归档行为：

1. 仅 `done` 可归档；
2. 归档前重新校验配置中全部阶段的产物章节与评审字段，并确认普通验证证据仍然有效；任意缺失、占位、结论不符或验证失效都会拒绝归档；
3. 生成 `ARCHIVE.md`（元数据、任务统计、验证结果、交付摘要、产物列表）；
4. 移动目录到 `.workflow/archive/<YYYYMMDD-HHMMSS>-<slug>/`；
5. 把状态改为 `archived`，追加归档历史；
6. 在 `.workflow/archive/INDEX.md` 追加索引行。

`list` 会同时列出进行中和已归档的运行。`status --run <slug>` 可读取已归档运行；写操作会被拒绝。

## 9. 多运行与根目录

- CLI 默认从当前目录向上查找 `.workflow/config.json`，也可用 `--root <目录>` 指定工作区。
- 只有一个进行中运行时，可省略 `--run`。
- 存在多个进行中运行时，必须用 `--run <slug>` 指定；否则返回错误。
- `--root` 和 `--run` 可以出现在子命令前后。

## 10. 退出码与机器读取

- `0`：成功；`check` 无阻塞；`verify` 达到预期。
- `1`：门禁未通过、配置/状态错误、命令失败、参数语义错误（脚本层）。
- `2`：Argparse 层面的参数用法错误。

`status --json`、`check --json`、`task list --json`、`list --json` 输出稳定 JSON，便于自动化读取。`status` 中的 `blockers` 每项包含 `code` 和 `message`。

## 11. 扩展不变量

- 通过配置增加阶段与门禁，不修改脚本。
- 通过配置增加命令、red 命令、cwd、超时，不写死语言或框架。
- 通过项目模板覆盖产物骨架，不修改技能资产。
- 如果门禁能力不足，优先提出通用门禁原语并在测试中覆盖；禁止按项目写特殊分支。
- 修改脚本后必须运行 `python -m unittest discover -s scripts/tests`，并至少完成一次真实运行演练。
