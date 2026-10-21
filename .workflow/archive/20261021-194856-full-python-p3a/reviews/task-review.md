# 任务评审：全量 Python P3a（test 主入口）

> 运行 full-python-p3a · 创建于 2026-10-21

## 评审输入

- 方案：`plan.md`
- 任务清单：`tasks.md`
- 设计权威：`脚本形态统一专项设计` v0.2 §6（P3，拆 P3a/P3b）
- 范围：只迁 `test.py`；nightly/l2/install-task 保留 PS（P3b）。

## 覆盖与追溯

| 验收标准 | 对应任务 | 是否覆盖 | 说明 |
|---|---|---|---|
| AC1 车道 | T1 | 是 | L1/L2/L3/All/Doc + Profile；L2 转调 PS |
| AC2 环境 | T1 | 是 | 环境变量设置/finally 恢复、run_id/results |
| AC3 守卫 | T1 | 是 | 发现契约/零 tests/失败 exit 1 |
| AC4 注册面 | T2 | 是 | CI/清单/toolchain/nightly 行/索引 |
| AC5 文档 | T2 | 是 | AGENTS/现行文档/skills |
| AC6 门禁 | T1、T3 | 是 | 新 test.py 真跑 L1 1801、Doc、check |
| AC7 范围 | T3 | 是 | 不改 Unity/共享源集；不跑 L2/云侧 |

## 检查项

- [x] 任务粒度独立可验证：T1 含 test.py + 单测 + L1 真跑；T2 文档/注册面；T3 收口。
- [x] 依赖顺序：T1 → T2 → T3。
- [x] 验收/验证明确。
- [x] 测试覆盖失败路径：非法 lane、发现契约、零 tests、Doc。
- [x] 通用性与扩展：复用 `_common`；dotnet 车道集中，L2 后续复用。
- [x] 无范围外改动：不迁 nightly/l2/install；不改过滤器/TRX 名。
- [x] 回滚：恢复 test.ps1/旧命令即可。

## 发现的问题与处理

| ID | 严重度 | 问题 | 处理 | 状态 |
|---|---|---|---|---|
| R-1 | 次要 | L2 仍 PS，test.py 需明确转调而非假装已迁 | T1 保留 `l2-unity-gate.ps1` 调用；P3b 再切 | 已明确 |
| R-2 | 次要 | TRX XML 有命名空间 | T1 用 local-name 查找 Counters；测试用真实 L1 TRX | 已明确 |
| R-3 | 次要 | 环境变量必须恢复，避免污染后续 process | T1 finally 恢复；T3 跑全测 | 已明确 |
| R-4 | 信息 | `test.py --lane L1` 会在验证中跑全量 1801，耗时约 35s | 接受；这是主入口验收证据 | 已明确 |

无阻塞问题。

## 评审结论

- 结论: PASS
- 阻塞问题: 0
- 评审人: Qoder（agent）
- 评审时间: 2026-10-21
