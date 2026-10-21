# 方案：全量 Python P3a（test 主入口）

> 运行 full-python-p3a · 设计权威：[脚本形态统一专项设计](../../../Docs/设计文档/架构/脚本形态统一专项设计.md) v0.2 §6（P3）

## 背景与目标

- P2 已完成内容链/环境检查；P3 原计划含 test/nightly/l2/install-task。为避免在无 Unity/管理员环境里同时硬切 L2 与计划任务，拆成：
  - **P3a（本批）**：只迁 `scripts/gate/test.ps1` → `test.py`（L1/L3/Doc 主入口，L2 暂转调 `l2-unity-gate.ps1`）。
  - **P3b（后续）**：`nightly-gate` / `l2-unity-gate` / `install-nightly-task` + 计划任务重注册。
- 目标：CI/本地 L1、L3、Doc 全部走 Python `test.py`，行为/过滤/TRX/退出码与 PS 一致；`test.ps1` 删除；`nightly-gate.ps1` 的 L1 调用改 Python，保证夜间链不断。
- 可观察结果：`python scripts/gate/test.py --lane L1 --profile PullRequest` 跑出 1801 通过；`--lane Doc` 通过；CI 四次调用改 Python；nightly PS 调 test.py。

## 需求澄清与假设

- **已确认**：全量 Python 目标；P3 可拆批；L2 本批保留 PS 兼容。
- **假设**：
  1. `dotnet` 在 PATH；`Tests/**/*.Tests.csproj` 发现契约不变；
  2. `test.py` 的 L2 通过既有 `l2-unity-gate.ps1` 运行（P3b 再切换）；
  3. `nightly-gate.ps1` 只需改 L1 调用一行，L2 仍走 PS；
  4. 环境变量（DOTNET_CLI_HOME 等）在子进程期间设置、结束后恢复；
  5. 不跑 L2/Unity；L3 不在本批强制跑（夜间/后续），但入口保持。
- **假设影响**：若要求本批同时迁 L2/计划任务，需 Unity/管理员环境，风险和工作量显著上升。

## 范围与非目标

### 范围内

- 新增 `scripts/gate/test.py`：L1/L2/L3/All/Doc；Profile；dotnet restore/build/test；TRX total 解析；项目发现守卫；L2 转调 PS；Doc 跑 doc-lint + docs --check；环境变量恢复。
- 删除 `scripts/gate/test.ps1`。
- 注册面：`.github/workflows/ci.yml` 4 处；`toolchain.json` quality.l1/l3/doc；`toolchain.py` L1/L3/Doc 分派与 `test` 路径；`nightly-gate.ps1` L1 调用。
- 现行文档/AGENTS/skills 的 `test.ps1 -Lane/-Profile` 改 `test.py --lane/--profile`；历史施工卷/归档不改。
- 测试：help/非法 lane/Doc/discovery guard；真跑 L1 作为验收。

### 非目标

- 不迁 nightly/l2/install-task（P3b）；不改计划任务/runner/surface。
- 不改测试过滤/分类/TRX 名/results 目录语义；不改 dotnet 参数。
- 不跑 L2/Unity；不提交。

## 现状与约束

- `test.ps1` 198 行：保存环境变量；`run_id`；发现 Tests/**/*.Tests.csproj + 非匹配 csproj 守卫；逐项目 restore；构建 Tests.slnx；逐项目 dotnet test（TRX、blame 5m、filter）；解析 TRX total；L1/L3 过滤器；L2 调 l2 PS；Doc 调 doc-lint + toolchain docs；finally 恢复环境。
- 过滤：
  - L1 `Category!=Integration&Category!=EndToEnd&Category!=Performance&Category!=Quarantine&Duration!=LongRunning`
  - L3 `(Category=Integration|Category=EndToEnd)&Category!=Quarantine`；PullRequest 加 `Duration!=LongRunning`；否则 M10_LONGRUN=1 + 12m。
- 约束：dotnet 输出需保留；TRX 文件名/目录与 PS 一致；L2 需 Windows；Doc 需 UTF-8；失败 exit 1、参数错误 exit 2。

## 候选方案与取舍

| 方案 | 核心思路 | 优点 | 代价/风险 | 结论 |
|---|---|---|---|---|
| A 只迁 test.py（推荐） | L2 暂转调 PS，nightly 改一行 | 风险可控；CI 主入口立即 Python；不触发计划任务/Unity | L2/nightly/install 仍未 Python | **采用** |
| B P3 一次全迁 | test+nightly+l2+install | 一次完成 P3 | 无 Unity/管理员环境无法验收；计划任务/surface 风险 | 不采用 |
| C 只改 CI 不改 test | 保留 PS 入口 | 最小改动 | 与全量目标不符，债务后移 | 不采用 |

## 设计要点

- `test.py` 用 `argparse`：`--lane`（L1/L2/L3/All/Doc）、`--profile`（PullRequest/Nightly/Release）、`--project-path`、`--results-directory`。
- 环境：保存/设置 `DOTNET_CLI_HOME`、`DOTNET_CLI_TELEMETRY_OPTOUT`、`DOTNET_PROCESSOR_COUNT=4`、`DOTNET_SKIP_FIRST_TIME_EXPERIENCE`、`LITETEST_ARTIFACTS`、`LITETEST_RUN_ID`、`NUGET_PACKAGES`、`M10_LONGRUN`；finally 恢复。
- `run_id = profile.lower()-YYYYMMDD-HHmmss`；results 目录创建。
- `dotnet restore` 每项目：`--disable-parallel --ignore-failed-sources --verbosity minimal -m:1 -p:BuildInParallel=false -p:NuGetAudit=false`。
- `dotnet build Tests/Tests.slnx --configuration Release --no-restore --verbosity minimal -m:1 -p:BuildInParallel=false`。
- `dotnet test` 参数同 PS；TRX `TestRun/ResultSummary/Counters@total` 用 XML local-name 解析。
- L2：`powershell -NoProfile -File scripts/gate/l2-unity-gate.ps1 -ProjectPath ...`（P3b 再换）。
- Doc：`python doc-lint.py --root` + `python toolchain.py docs --check`，`PYTHONIOENCODING=utf-8`。
- 失败信息与 PS 对齐：restore/build/test/zero tests 明确 exit 1。
- 更新 `toolchain.py` 的 L1/L3/Doc 分派为 `sys.executable test.py --lane ... --profile ...`；`quality.doc` 直接用 test.py？当前 command `test.ps1 -Lane Doc`，改为 `test.py --lane Doc`；保留直跑 doc-lint 的 `quality.doc-lint`。
- `nightly-gate.ps1` L1 行改 `python test.py --lane L1 --profile Nightly`。

## 通用性与扩展性

- 复用 `_common` 根/输出/退出码；不新增通用测试框架。
- 过滤器/TRX 解析集中在 test.py，后续 L2 迁移复用同一 dotnet 车道逻辑。
- 环境变量保存/恢复用字典，避免污染调用方。
- 文档/CI/清单/工具索引同批切换，防注册面漂移。

## 测试策略

- 红测：`test_test.py` 缺失 -> 断言失败；`test_toolchain` 新结构断言（命令/CI/nightly 调用/旧文件）失败。
- 实现后：
  - `--help` 0；非法 lane 2；`--lane Doc` 0；
  - 临时 Tests 目录含 `Bad.csproj` 无 `*.Tests.csproj` -> discovery guard exit 1（不跑 dotnet）；
  - **真跑 L1** `--lane L1 --profile PullRequest` -> 1801 通过；作为验收主证据。
- 基线：Doc lane、toolchain check/docs、BuildHash、L1；不跑 L3/L2。
- 命令：
  ```powershell
  python scripts/gate/test.py --lane L1 --profile PullRequest
  python scripts/gate/test.py --lane Doc
  python -m unittest scripts.tests.test_test_entry -v
  python scripts/toolchain.py check
  python scripts/toolchain.py docs --check
  python scripts/gate/doc-lint.py
  ```

## 验收标准

- [ ] **AC1 车道**：L1/L3/Doc/All 分派、过滤器、Profile、TRX total 与 PS 一致；L2 转调 PS。
- [ ] **AC2 环境**：dotnet 环境变量/run_id/results 目录设置并在 finally 恢复。
- [ ] **AC3 守卫**：非 `*.Tests.csproj` 被发现契约拦截；零 tests 失败；restore/build/test 失败 exit 1。
- [ ] **AC4 注册面**：CI 4 处、清单 l1/l3/doc、toolchain.py、nightly PS L1 调用改 Python；test.ps1 删除；工具索引 current。
- [ ] **AC5 文档**：现行命令/AGENTS/skills 改 `test.py --lane/--profile`；历史卷保留。
- [ ] **AC6 门禁**：L1 1801（经新 test.py）、Doc 0/0、toolchain check/docs、BuildHash 不变。
- [ ] **AC7 范围**：只改 scripts/Docs/AGENTS/CI；不碰 Unity/共享源集/协议/表；不跑 L2/云侧；未提交。

## 风险与回滚

| 风险 | 监控/发现 | 回滚 |
|---|---|---|
| L1 过滤/TRX 解析漂移 | 新 test.py 真跑 L1 1801；TRX total 断言 | 恢复 test.ps1 与旧命令 |
| 环境变量污染 | finally 恢复；子进程测试 | 回退；修复恢复逻辑 |
| CI 命令参数风格错误 | CI YAML 文本检查 + 本地跑 | 回退 CI step |
| L2/夜间断链 | nightly PS 调 test.py；L2 仍 PS；`toolchain check` | 恢复 test.ps1/nightly 行 |
| 文档旧命令残留 | grep（排除历史卷） | 同批补 |

**回滚**：恢复 `test.ps1` 与 CI/清单/toolchain.py/nightly 行；删除 test.py/测试；无数据迁移。
