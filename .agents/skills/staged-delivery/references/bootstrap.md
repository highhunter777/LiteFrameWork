# 命令探测与启动配置

工作流不假设语言、框架或包管理器。启动阶段的核心任务，是为当前仓库选出真实、可重复、能被 `verify` 执行的命令。

## 1. 只读探测

按以下顺序收集信息，不要先猜命令：

1. 项目文档：`README`、`CONTRIBUTING`、`AGENTS.md`、开发环境说明。
2. CI/CD：`.github/workflows`、`.gitlab-ci.yml`、`Jenkinsfile`、Azure Pipelines、Buildkite 等。CI 使用的命令通常最接近真实门禁。
3. 构建与依赖：`package.json` scripts、`pyproject.toml`、`tox.ini`、`Makefile`、`justfile`、`go.mod`、`Cargo.toml`、`pom.xml`、`build.gradle`、`*.csproj`。
4. 测试资产：测试目录、测试配置、fixture、快照、现有命名约定。
5. 环境约束：包管理器与锁文件、虚拟环境、Node/Python/JDK 版本、monorepo 子包、必要环境变量。
6. 现有工作流配置：`.workflow/config.json` 是否已存在，是否有自定义阶段、模板或命令；存在时保留，不要用默认配置覆盖。

不要擅自引入新的测试框架或构建工具。优先使用仓库已有能力；确实缺失时，先说明并取得用户同意。

## 2. 选择两类命令

### commands：完整验证

用于阶段“测试代码”。选择能够证明交付未回归的组合：

- 测试：全量或与改动相关的测试套件
- 静态检查：lint、format check、typecheck
- 构建：编译、打包或迁移校验

建议 1-4 条，全部必须退出 0。命令应当可重复执行，不依赖交互输入。

### red_commands：先行测试

用于阶段“先写测试”，只运行新增或修改的测试，并预期在实现前失败。选择粒度越精确越好：

- 单测试文件
- 单个测试类或用例
- 带过滤条件的测试命令

如果无法只跑新增测试，也可以使用全量测试，但必须在 `test-plan.md` 中说明预期失败的用例与原因。若改动无法产生有意义的失败测试（纯文档、配置或基础设施），将 `red_commands` 留空，并在测试计划中写明替代验证；不要伪造失败。

## 3. 写入配置

编辑项目根目录的 `.workflow/config.json`：

```json
{
  "commands": [
    {
      "name": "tests",
      "cmd": ["python", "-m", "pytest", "-q"],
      "timeout": 900
    },
    {
      "name": "lint",
      "cmd": ["ruff", "check", "."],
      "timeout": 300,
      "cwd": "backend"
    }
  ],
  "red_commands": [
    {
      "name": "new-tests",
      "cmd": ["python", "-m", "pytest", "tests/test_feature.py", "-q"],
      "timeout": 300
    }
  ]
}
```

- `cmd` 为数组时不经 shell；为字符串时经 shell。需要管道、环境变量前缀或 Windows `.cmd` 时可使用字符串。
- `cwd` 相对工作区根目录，用于 monorepo 子项目。
- `timeout` 是单条命令超时，默认 600 秒。
- 命令名在同类列表中必须唯一。

只修改命令相关字段；除非明确需要，不要重写默认阶段。配置完整语义见 `protocol.md`。

## 4. 生态探测提示（示例，不是默认值）

| 生态 | 常见完整验证 | 红测粒度 |
|---|---|---|
| Node / TypeScript | 读取 `package.json` scripts；常见为 `npm test -- --run`、`pnpm test`、`pnpm lint`、`tsc --noEmit` | 单测试文件或测试名过滤 |
| Python | `python -m pytest -q`、`python -m unittest discover`、`ruff check .`、`mypy .` | 指定测试文件或 `-k` 表达式 |
| Go | `go test ./...`、`go vet ./...` | `go test ./pkg -run TestName` |
| Rust | `cargo test`、`cargo clippy --all-targets -- -D warnings` | `cargo test test_name` |
| JVM | `./gradlew test`、`mvn -q test` | Gradle `--tests`、Maven `-Dtest=` |
| .NET | `dotnet test` | 指定测试项目或过滤条件 |
| Make / just | `make test`、`make lint`、`just test` | 项目自定义目标 |

这些只是探测线索。必须打开清单或 CI 确认实际命令、工作目录和参数；不要直接照抄。

## 5. 红测执行与判断

1. 写测试后，先手动运行 red 命令，确认失败。
2. 查看失败内容：应当是断言失败或明确的行为差异，不应是导入错误、语法错误、缺依赖或环境故障。
3. 如果失败原因不对，先修测试或命令，再运行 `wf verify --red`。
4. 红测证据由 `evidence/red.json` 记录；阶段 4 会要求它存在且“观察到预期失败”（仅在配置了 `red_commands` 时）。
5. 实现完成后，同一批测试必须通过；如果测试无需修改就通过，检查它是否真的覆盖了目标行为。

## 6. 项目模板

项目级模板放在 `.workflow/templates/`，可覆盖技能自带模板：

```text
.workflow/templates/
├── plan.md
├── test-plan.md
├── build-log.md
└── reviews/task-review.md
```

配置中的产物通过 `"template": "plan.md"` 引用；查找时先项目模板，后技能模板。没有模板的产物会按配置章节自动生成骨架。模板中的 `{{title}}`、`{{slug}}`、`{{created_at}}` 会被替换。

## 7. 常见问题

- **命令在工作区内找不到**：检查包管理器、虚拟环境、Node/JDK 版本与 `cwd`。
- **命令数组在 Windows 上无法启动 npm/pnpm**：使用字符串命令让 shell 解析，或显式使用可执行文件路径。
- **全量测试太慢**：完整验证仍应保留；可在日常任务中使用更小的检查，但阶段 6 必须运行配置的完整命令。必要时调整 `timeout`。
- **命令需要环境变量或服务**：先确认本地可复现；不要把密钥写入配置或证据输出。无法本地运行时，选择替代验证并在交付摘要中说明。
- **红测整批失败**：确认失败是否来自新增用例；无关失败会污染判断，应先隔离。
- **证据包含敏感信息**：尽量避免会打印密钥的命令；`verify` 只保留输出尾部，但仍需人工检查。
- **配置被修改后验证突然过期**：这是命令指纹变化导致的保护，重新运行 `verify` 即可。
