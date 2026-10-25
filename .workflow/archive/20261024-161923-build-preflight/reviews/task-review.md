# 任务评审：构建 preflight：变更驱动最小重建集

> 运行 build-preflight · 创建于 2026-10-24
>
> 本阶段由主代理按独立评审者口径检查任务清单；不冒充子代理评审。

## 评审输入

- 任务清单：tasks.md（T1-T7）
- 验收标准：acceptance.md（AC1-AC9，用户已确认）
- 方案：plan.md（修订 6，五轮独立评审后 PASS）

## 覆盖与追溯

| 验收标准 | 覆盖任务 | 判定 |
|---|---|---|
| AC1 无改动提示 | T1/T4/T5 | 覆盖 |
| AC2 共享源双口径 | T2/T3/T5 | 覆盖 |
| AC3 混合双口径+去重 | T2/T5 | 覆盖 |
| AC4 未知路径+漂移 | T2/T3/T5 | 覆盖 |
| AC5 JSON 契约 | T4/T5 | 覆盖 |
| AC6 零写盘+登记 | T5/T6 | 覆盖 |
| AC7 矩阵 6 行 | T5 | 覆盖 |
| AC8 门禁+BuildHash | T7 | 覆盖 |
| AC9 边界用例 | T1/T5 | 覆盖 |

## 检查项

- 任务粒度：T1-T4 为内核分层（采集/分类/漂移/CLI），T5 集成用例，T6 登记与文档，T7 门禁与审计；可独立验证。
- 依赖顺序：T1→T2→T3/T4→T5→T7；T6 依赖 T4，合理。
- 可测试性：每项有 verify 命令；T5 覆盖边界与失败路径。
- 范围外风险：未混入 BuildHash 常量修复（`MapTableProjection` 仅登记与警告，另批）；未改 build-player/CI。
- 硬编码/特判：规则表集中；已知例外仅 `MapTableProjection` 一条并钉住；无路径白名单散落。
- 与并行线：T6 触碰 `scripts/gate/toolchain.json`，按方案 A6 最后做、最小 hunk、立即 check。

## 发现的问题与处理

| ID | 问题 | 处理 | 状态 |
|---|---|---|---|
| Q-1 | T6 与 UI 线 P2-3a 共用 `toolchain.json` | 施工时先读最新文件、只改 build 域条目；编辑后立即 `toolchain.py check`；冲突时后落地方 rebase | 已记录 |
| Q-2 | T7 的审计登记范围 | 只登记 `MapTableProjection` 漏登记条目，不顺手修其他债 | 已记录 |

## 评审结论

- 结论: PASS
- 阻塞问题: 0
- 评审方式: 主代理任务评审（按 artifact-quality 检查）
- 评审人: 主代理
- 评审时间: 2026-10-24
