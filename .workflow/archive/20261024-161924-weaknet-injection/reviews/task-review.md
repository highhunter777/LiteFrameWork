# 任务评审：弱网注入：传输层损伤注入与可视化

> 运行 weaknet-injection · 创建于 2026-10-24
>
> 本阶段由主代理按独立评审者口径检查任务清单；不冒充子代理评审。

## 评审输入

- 任务清单：tasks.md（T1-T10）
- 验收标准：acceptance.md（AC1-AC8，用户已确认）
- 方案：plan.md（修订 6，五轮独立评审后 PASS）

## 覆盖与追溯

| 验收标准 | 覆盖任务 | 判定 |
|---|---|---|
| AC1 固定种子可复现 | T1/T3/T8 | 覆盖 |
| AC2 零注入透传 | T2/T3 | 覆盖 |
| AC3 损伤生效+可靠保序 | T2/T3/T8 | 覆盖 |
| AC4 开发宏/仅真 KCP | T5/T7 | 覆盖 |
| AC5 HUD 注入行+曲线 | T4/T6/T7 | 覆盖 |
| AC6 命令审计+无静态可变 | T5/T7 | 覆盖 |
| AC7 L3 对跑+夜间 | T8/T9 | 覆盖 |
| AC8 门禁+BuildHash | T7/T9/T10 | 覆盖 |

## 检查项

- 任务粒度：内核（T1/T2）、内核用例（T3）、App 层（T4）、运行时接线（T5）、HUD（T6）、装配守卫（T7）、对跑（T8）、门禁设施（T9）、文档（T10）；可独立验证。
- 依赖顺序：T1→T2→T3/T4→T5/T6→T7；T8 依赖 T3；T9 依赖 T8；T10 依赖 T7/T9。
- 可测试性：内核用 L3 xunit；装配/曲线用 EditMode；对跑用 L3；夜间可回读。
- 范围外：无服务端/UDP 层注入；无 release 入口；不改 BuildHash 闭包。
- 硬编码/特判：profile/预设表数据驱动；无地址/房间特判；HUD 只读 snapshot。

## 发现的问题与处理

| ID | 问题 | 处理 | 状态 |
|---|---|---|---|
| Q-1 | T6（HUD）verify 引用 T7 才创建的 `WeakNetWiringEditModeTests`，顺序倒置 | T6 验证改为"Unity 编译回归（跑既有 `UiToolchainWiringEditModeTests`）+ Play 冒烟确认联机段渲染"；HUD 文本/曲线断言由 T7 覆盖；施工记录写明 | 已记录 |
| Q-2 | T8 对跑用 loss-only，延迟语义由 T3 内核用例覆盖 | 已在 AC7/方案中写明，任务验收同步 | 已记录 |
| Q-3 | T9 修改 `test.py`/`nightly-gate.py` 增加 filter/L3，影响夜间时长 | 只跑 `FullyQualifiedName~WeakNet&(Category!=Quarantine)`，独立 `L3-*` 日志；失败按 lane 记录；超时沿用现有 | 已记录 |

## 评审结论

- 结论: PASS
- 阻塞问题: 0
- 评审方式: 主代理任务评审（按 artifact-quality 检查）
- 评审人: 主代理
- 评审时间: 2026-10-24
