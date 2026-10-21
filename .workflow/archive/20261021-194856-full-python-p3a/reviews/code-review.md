# 代码评审：全量 Python P3a（test 主入口）

> 运行 full-python-p3a · 创建于 2026-10-21

## 变更范围

- 新增 `test.py` + `test_test_entry.py`；删除 `test.ps1`。
- 修改 CI 4 处、清单 quality.l1/l3/doc、`toolchain.py` 分派、`nightly-gate.ps1` L1 调用、工具索引、现行文档/skills。
- P3b（nightly/l2/install-task）未做；L2 暂由 test.py 转调 PS。

## 正确性与边界

- L1/L3/All 的 dotnet 车道、过滤器、TRX 命名/解析、零 tests/失败退出码与 PS 对齐；L1 真跑 1801。
- Doc 车道跑 doc-lint + `toolchain.py docs --check`；不依赖 dotnet。
- L2 在 Windows 上优先 Python l2（未存在则 PS）；非 Windows 明确失败。
- 发现契约守卫在 dotnet 前拦截非 `*.Tests.csproj`；临时夹具验证。
- 环境变量保存/恢复；run_id/results 目录与 PS 一致。
- 夜间 PS 只改 L1 调用一行，L2/surface 不变；回滚简单。

## 测试充分性

- 红测 5；最终 `test_entry` 4/4、全量 Python 79/79、L1 1801、Doc PASS。
- 未覆盖：L3 实跑、L2/Unity、夜间任务真实执行；本批明确不跑。
- 测试未迎合实现：发现守卫独立夹具；Doc 实际跑；L1 真跑。

## 通用性与扩展性

- 复用 `_common`；dotnet 车道集中，P3b/P4 可复用。
- L2 的 Python/PS 选择是迁移期扩展点，P3b 切换无需再改 test.py。
- 无第二套过滤器/TRX 解析；All 有少量重复调用代码，但可读性优先，未过度抽象。

## 安全与性能

- 不执行云侧/Unity；只跑本地 dotnet/Doc。
- 性能：L1 约 35s，与 PS 同量级；环境变量恢复避免污染。
- 残余：`info` 输出与 dotnet stdout 缓冲顺序可能交错，不影响结果；`All` 重复代码可后续收敛。

## 发现的问题与处理

| ID | 严重度 | 位置 | 问题 | 处理 | 状态 |
|---|---|---|---|---|---|
| C-1 | 信息 | test.py L2 | L2 仍转调 PS，不是全 Python | P3a 刻意保留；P3b 切换 l2 后自动走 Python | 接受 |
| C-2 | 信息 | nightly | nightly 仍是 PS，仅 L1 调用改 Python | 避免计划任务 surface 在无管理员环境断链；P3b 一并迁 | 接受 |
| C-3 | 信息 | test.py 输出 | `info` 与 dotnet 子进程 stdout 缓冲顺序交错 | 退出码/测试结果不受影响；未为本批引入刷新逻辑 | 接受 |
| C-4 | 次要 | 文档批量替换 | 全局 `-Lane/-Profile` 误伤 `R3-Profile` 锚点与 `build-player -Profile` | doc-lint 抓到并回退；skill 两处修正；复测 0/0 | 已修复 |

无阻塞问题。

## 复测记录

- 修复后 `workflow.py verify` → PASS（L1 1801、Doc、restore、L0、Python 79/79、check 0 error/5 warning、docs current、Doc lane）。
- 附加：CI YAML/清单 JSON/nightly PS AST OK；BuildHash `--check-shared` OK。
- 验证证据：`evidence/verify.json`。

## 评审结论

- 结论: PASS
- 阻塞问题: 0
- 通用性: PASS
- 扩展性: PASS
- 评审人: Qoder（agent）
- 评审时间: 2026-10-21
