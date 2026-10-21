# 任务评审：全量 Python P3b

> 运行 full-python-p3b · 创建于 2026-10-21

## 评审输入

- 方案：`plan.md`
- 任务：T1 L2、T2 nightly、T3 install、T4 注册面、T5 验证
- 设计：`脚本形态统一专项设计` v0.2 §6 P3b
- 约束：L2/计划任务需 Unity/机器侧；本批可离线验证 meta/help/what-if。

## 覆盖与追溯

| 验收 | 任务 | 覆盖 |
|---|---|---|
| L2 meta/help/参数 + 代码路径 | T1 | 是（meta 可离线；Pipeline/batchmode 未跑） |
| nightly 编排/日志/PlayMode | T2 | 是（PS shim 过渡） |
| install what-if/XML | T3 | 是（实际注册机器侧） |
| 注册面/文档 | T4 | 是 |
| 门禁/证据 | T5 | 是 |

## 检查项

- [x] 任务粒度独立可验证；T2 依赖 T1。
- [x] 验收/验证明确；L2 完整运行明确标注未跑。
- [x] 测试覆盖离线失败路径（非法 meta/help/what-if）。
- [x] 通用性：`_common` 复用；Unity 命令集中；install 读 manifest。
- [x] 风险/回滚：保留 PS 一版；nightly shim；任务注册失败仍可运行。
- [x] 无范围外改动：不跑 batchmode/不实际注册（若 what-if）。

## 发现的问题与处理

| ID | 严重度 | 问题 | 处理 | 状态 |
|---|---|---|---|---|
| R-1 | 高 | L2 完整 Pipeline/batchmode 无法在无 Unity 环境验证 | 本批只验 meta/help；完整 L2 留机器侧，保留 PS 回滚 | 已明确（非阻塞代码） |
| R-2 | 中 | 计划任务重注册是机器侧动作，未做前 surface 与 manifest 可能不一致 | nightly PS shim 保留；install `--what-if`；实际注册另行确认 | 已明确 |
| R-3 | 中 | L2 默认 UnityExe 仍是机器路径 | 保留旧默认 + env/参数覆盖；不改变行为，登记为后续债 | 已明确 |
| R-4 | 低 | nightly shim 删除时机依赖任务注册成功 | 注册成功后再删；失败保留 shim | 已明确 |

无阻塞问题（代码可离线验收；机器侧验收明确待办）。

## 评审结论

- 结论: PASS
- 阻塞问题: 0
- 评审人: Qoder（agent）
- 评审时间: 2026-10-21
