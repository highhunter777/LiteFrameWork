# 实施任务清单：构建 preflight：变更驱动最小重建集

> 本文件由 workflow.py 生成，请使用 `task` 子命令更新，勿手改。

## 任务清单

阶段：已收尾（待归档） ｜ 完成 7/7（取消 0，阻塞 0）

| ID | 任务 | 依赖 | 状态 | 验收标准 | 验证方式 | 证据 |
|---|---|---|---|---|---|---|
| T1 | 变更采集与路径归一 | - | 完成 | git base/工作区/未跟踪三源采集；忽略目录过滤与计数；--project-path 非本仓库 fail-fast；路径归一与去重有测试 | python -m unittest scripts.tests.test_build_plan -v | 采集/归一实现；Python 31/31 |
| T2 | 分类内核与双口径规则表 | T1 | 完成 | 15 条规则按优先级唯一 area；Finding 含 inner/delivery 动作；动作按语义序去重；unknown 保守升级并列入 unknownPaths | python -m unittest scripts.tests.test_build_plan -v | 15 规则/双口径/去重；Python 31/31 |
| T3 | 共享/Meta 漂移对账 | T2 | 完成 | 复用 gen-build-hash.collect；parse_csproj 分别对账 RoomServer/MetaServer；MapTableProjection known=true；新增单文件/未登记通配 known=false | python -m unittest scripts.tests.test_build_plan -v | collect + parse_csproj 双 csproj；MapTableProjection known=true；漂移注入用例 |
| T4 | CLI 与 JSON 契约 | T2 | 完成 | --base/--json/--changed-file 可用；exit 0/1/2 语义正确；[build/plan] 前缀；schemaVersion=1 键集稳定 | python scripts/build/build-plan.py --help; python scripts/build/build-plan.py --base HEAD --json | --base/--changed-file/--json/--project-path 实测；跨仓库 exit 1 |
| T5 | AC1-AC9 测试套件 | T1, T2, T3, T4 | 完成 | 矩阵 6 行固定用例、双口径、漂移注入、只读、JSON、路径归一/忽略计数/base 失败全绿 | python -m unittest scripts.tests.test_build_plan -v | test_build_plan.py 31/31 OK |
| T6 | 工具登记与文档回写 | T4 | 完成 | build.plan 清单条目（kind/plane/purpose/docs/writes）登记；toolchain check 与 docs --check 通过；11 流程/05 门禁/施工进度回写 | python scripts/toolchain.py check; python scripts/toolchain.py docs --check; python scripts/gate/doc-lint.py | toolchain.json build.plan；check OK；docs current；05/11/进度回写 |
| T7 | 门禁与审计登记 | T5, T6 | 完成 | L1/doc/toolchain-check 全绿；BuildHash --check-shared 不变；MapTableProjection 漏登记入客户端审计表 | python scripts/gate/test.py --lane L1 --profile PullRequest; python scripts/codegen/gen-build-hash.py --check-shared | L1 1831/1831；BuildHash 0a998c8ac6c9de89 不变；审计登记 |

## 任务详情

### T1 · 变更采集与路径归一

- 文件: scripts/build/build-plan.py, scripts/tests/test_build_plan.py

### T2 · 分类内核与双口径规则表

- 文件: scripts/build/build-plan.py, scripts/tests/test_build_plan.py

### T3 · 共享/Meta 漂移对账

- 文件: scripts/build/build-plan.py, scripts/tests/test_build_plan.py

### T4 · CLI 与 JSON 契约

- 文件: scripts/build/build-plan.py

### T5 · AC1-AC9 测试套件

- 文件: scripts/tests/test_build_plan.py

### T6 · 工具登记与文档回写

- 文件: scripts/gate/toolchain.json, Docs/项目说明/11-联机测试流程.md, Docs/项目说明/05-验证与门禁.md, Docs/施工进度/资源与工具链线.md

### T7 · 门禁与审计登记

- 文件: Docs/审计/代码质量审计-客户端.md

