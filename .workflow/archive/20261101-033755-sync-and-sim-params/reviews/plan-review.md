# 方案评审（独立子代理）：同步层拆分与判定参数化（LiteSync 去耦 + LiteSim 参数装载）

> 运行 sync-and-sim-params · 创建于 2026-10-30T19:40:29+08:00
>
> 本文件由主代理根据独立子代理（dsh headless）的返回整理落盘；不是主代理的自评。
> 最终结论基于第九轮评审（`evidence/dsh-plan-review-round9.txt`）。

## 评审输入

- 评审对象：`plan.md`（最终版，含 2026-10-31 三项用户裁决）
- 代码/文档基线：git `HEAD=8a8fe0bf`；评审时工作区含 `.workflow` 运行目录与评审产物
- 评审范围：`plan.md` 全文、证据母本、`AGENTS.md`、`references/artifact-quality.md`；按轮次抽查 LiteNet/LiteSim/Editor/Tests/gen-build-hash 关键文件
- 不在范围：不执行 Unity、构建或测试，不修改生产代码
- 评审角色：独立子代理（dsh headless）；最终轮会话 `session-acfe5322-48f2-47bd-811e-7a80a14aa32e`
- 子代理约束：只读评审，不修改生产代码、测试与门禁
- 原始证据：`evidence/dsh-plan-review-round1..9*.txt`（含每轮返回/错误）

## 评审轮次记录

| 轮次 | 结果 | 阻塞数 | 主要问题 | 证据 |
|---|---|---:|---|---|
| 1 | FAIL | 10 | 13/15 件清单、表接入形状、CombatValues 字段数、AOI/SnapshotHz、守卫范围、烘焙消费面、RootMotion、B14 可判定、B/C 归属、守卫根 | `evidence/dsh-plan-review-resume.txt` |
| 2 | FAIL | 4 | 非 LiteSim 消费面、发布闸门、SimMapBaker 值来源、SimParamsOverrides 数据流 | `evidence/dsh-plan-review-round2.txt` |
| 3 | 中断 | - | 传输错误；部分推理发现已并入 | `evidence/dsh-plan-review-round3.stderr.txt` |
| 4 | FAIL | 4 | prefab 不入库守卫、Default 双语义、`_published` 测试隔离、联机覆盖同源 | `evidence/dsh-plan-review-round4.txt` |
| 5 | FAIL | 2 | 联机覆盖可判定门/通道、Default 风险段残留 | `evidence/dsh-plan-review-round5.txt` |
| 6 | FAIL | 1 | 单门/单合并点不统一、旧句残留 | `evidence/dsh-plan-review-round6.txt` |
| 7 | FAIL | 2 | 摘要三套口径、MatchTestRules 几何旁路 | `evidence/dsh-plan-review-round7.txt` |
| 8 | FAIL | 2 | 非几何 dev 覆写边界、B20 “纯表摘要”表述 | `evidence/dsh-plan-review-round8-resume.txt` |
| 9 | **PASS** | **0** | 两项闭合；无阻塞 | `evidence/dsh-plan-review-round9.txt` |

## 方案检查

- 目标可观察、范围与非目标清晰：PASS（15 件清单、8→11/9 参数、五件 `.g.cs`、B/C 归属均写实）
- 现状与根因有代码/配置/文档依据：PASS（65/70 文件、11 个 `using LiteSim`、9 个 System、外部消费者 40 处、构造点 10 处均已核对）
- 候选方案取舍充分：PASS（L-C/P-3/R-1/AOI-A/B-C 顺序均有取舍）
- 设计覆盖接口、数据、关键流程、失败与边界：PASS
- 通用性与扩展点明确：PASS（边界判据、表/数据/协议扩展点）
- 测试策略能先失败后通过：PASS（红测、守卫负例、表值与 checksum、双端联测）
- 风险、监控与回滚可执行：PASS
- 验收标准逐条可观察、可判定：PASS（B1~B24 最终版）

## 验收标准可判定性

| 验收标准 | 判定方式 | 是否可观察/可判定 | 说明 |
|---|---|---|---|
| B1-B8（LiteSync/去耦） | asmdef/csproj、grep、15 件清单、`--check-shared`、L1/联测 | 可判定 | 文件级清单与守卫负例 |
| B9-B14（几何装载） | 11 字段/9 参数、13 列值、候选表校验、`Assets/**` 守卫、表值精确相等 + 黄金样本 | 可判定 | `CombatConfig` 发布三态、`Default` 非权威 |
| B15-B18（AOI/RootMotion） | 服务端配置注入、JoinAck、`rootmotion.bin` 装载/摘要、闭包检查 | 可判定 | `AoiSettings` 落 `LiteSync/Shared` |
| B19-B22（调参/守卫） | 单门 `SimParamsGate.Evaluate` + 单合并点、四条断言、静态可变扫描 | 可判定 | 本地/联机边界明确 |
| B23-B24（门禁/文档） | `sync-and-sim` 命令组、文档章节与 `doc-lint` | 可判定 | 文档目标已点名 |

## 发现的问题与处理

| ID | 严重度 | 问题 | 处理 | 状态 |
|---|---|---|---|---|
| P-1~P-10 | blocker | 首轮 10 项（清单/表接入/字段数/AOI/守卫/烘焙/RootMotion/B14/B-C/守卫根） | 已按轮次整改 | 已关闭 |
| R2-1~4 | blocker | 外部消费面、发布闸门、SimMapBaker、覆盖数据流 | 已整改 | 已关闭 |
| R4-1~4 | blocker | prefab 夹具、Default、测试隔离、联机同源 | 已整改 | 已关闭 |
| R5~R8 | blocker | 单门/合并点、摘要口径、MatchTestRules 旁路、非几何覆写边界 | 已整改 | 已关闭 |
| R9 | - | 无阻塞 | - | PASS |

## 评审结论

- 评审方式: 独立子代理
- 结论: PASS
- 验收标准: PASS
- 阻塞问题: 0
- 评审人: dsh headless（session-acfe5322-48f2-47bd-811e-7a80a14aa32e）
- 评审时间: 2026-10-31T16:54:48+08:00
