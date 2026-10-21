# 任务评审：工具链 v2：回放落盘与现场包

> 运行 toolchain-v2-replay · 创建于 2026-10-21T16:33:05+08:00

## 评审输入

- 方案：plan.md（含评审后补充：meta 流程、工具清单同步、HostLog 尾单源）
- 任务清单：tasks.md（T1～T9）
- 评审范围：任务对 AC1～AC8 的覆盖、依赖顺序、可验证性、测试先行、直接替换边界、R11/Unity 资产纪律。

## 覆盖与追溯

| 验收标准 | 对应任务 | 是否覆盖 | 说明 |
|---|---|---|---|
| AC1 录制 | T1, T9 | 是 | 内核录制器 + 命令/步进/规则捕获；红测先验 |
| AC2 重放 | T2, T9 | 是 | 真实 RoomRuntime 重放并逐步比对 checksum |
| AC3 定位 | T2, T9 | 是 | 篡改输入用例锁定首分歧帧/命令序号 |
| AC4 落盘 | T3, T4, T9 | 是 | Envelope/codec/store + 终态触发 + 临时目录断言 |
| AC5 客户端 | T5, T9 | 是 | BattleContext 触发 + manifest/log 尾 + 限频/上限 |
| AC6 身份 | T2, T3, T9 | 是 | buildHash/config/map 三校验，fail-fast |
| AC7 门禁 | T7 | 是 | L1/doc-lint/R11/buildHash --check-shared |
| AC8 文档 | T8 | 是 | 路线图/调试手册/进度卷/工具清单/代码地图 |

## 检查项

- [x] 任务粒度可独立执行与验证
- [x] 依赖关系无循环，顺序合理（T9 红测先于 T1～T6；T7 汇总验证；T8 收尾回写）
- [x] 每项任务有明确验收标准与验证方式
- [x] 测试策略足以覆盖验收标准（含篡改、截断、身份不符、客户端限频/上限）
- [x] 方案中的通用性与扩展性要求已落到任务（单一 RoomRuntime 执行链、版本化 DTO、配置化容量/目录）
- [x] 不存在硬编码、特判式补丁或范围外改动（容量/目录/路径进配置；本刀不碰热更/构建/CI）
- [x] 回滚与风险处理已安排（配置开关 + recorder 注入 null + 独立新增文件）
- [x] 直接替换边界明确（在线重放唯一实现 = RoomReplayRunner；SimRunRecord 仅离线）
- [x] Unity 资产纪律已纳入（新增 Assets 下 .cs 经 unity-pipeline 导入；L2 meta 检查）
- [x] R11 纯度已纳入（内核记录器无 IO/proto/LiteNet/系统时钟）

## 发现的问题与处理

| ID | 严重度 | 问题 | 处理 | 状态 |
|---|---|---|---|---|
| R-1 | 中 | 任务清单缺“先写红测”任务，测试先行只靠阶段顺序 | 新增 T9；T1～T6 依赖 T9；T7 依赖 T9 | 已修复 |
| R-2 | 中 | 新增 `Assets/RoomServer/Runtime/Diagnostics/` 与 `Assets/LiteClient/Runtime/Diagnostics/` 的 `.cs` 未纳入 Unity meta 流程 | T1/T5 验收补“unity-pipeline 导入 + .meta”；verify 补 L2 meta/导入回读；plan 范围补 | 已修复 |
| R-3 | 低 | 新增 CLI/目录未登记工具链清单与代码地图 | T8 增加 `scripts/gate/toolchain.json`、`Docs/工具链/技术支持.md`、`Docs/开发导航/代码地图.md` | 已修复 |
| R-4 | 低 | `HostLog` 是否已有可读尾未确认，T4 可能落空 | T4 改为“有界环或配置日志文件尾（单源二选一）”，实施前先核实；plan 同步 | 已修复 |
| R-5 | 低 | T2 未明确 MatchId/Seed/gameplayDigestBase 的复现义务 | T2 验收补三字段与记录一致 | 已修复 |
| R-6 | 低 | 全量 checksum 若含瞬态 Events/Cmds，捕获点可能不稳定 | T1 验收补“StepFrame 末尾稳定，以 SimChecksum 定义为准”；实施时加稳定性断言 | 已修复 |

## 评审结论

- 结论: PASS
- 阻塞问题: 0
- 评审人: 工具链 v2 交付（自评审）
- 评审时间: 2026-10-21

评审依据：AC1～AC8 均有任务与验证承接；红测任务前置；身份校验与 R11/Unity 纪律进入验收；范围严格限定在服务端记录/重放与双端现场包，未混入热更、构建、CI 或开发 Player 改造。
