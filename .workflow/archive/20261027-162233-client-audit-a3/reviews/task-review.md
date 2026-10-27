# 任务评审：客户端审计 A3

## 评审输入

- 任务清单：`.workflow/active/client-audit-a3/tasks.md`（T1–T6，CLI 生成）
- 方案：`plan.md`；验收标准：`acceptance.md`
- 依赖图：T1 → T2；T3、T4 独立；T1/T2/T3/T4 → T5 → T6
- 评审方式：主代理逐条检查（本阶段不要求独立子代理）

## 覆盖与追溯

| AC | 任务 | 验证方式 |
|---|---|---|
| AC1 结构收敛 | T1（取件域）、T2（装配域）、T3（UIBind 域）、T4（树表示法） | 结构探针 + 代码评审 |
| AC2 公共 API | T2、T3、T4 | `git diff` + 派生类 grep + 编译 |
| AC3 编译 | T1–T4（各自编译）、T5（统一复验） | `recompile_status failed=false` + console |
| AC4 L1/定向 | T2（战斗表现）、T3（UIBind）、T4（FSM）、T5（全量） | L1 + `unity-tests.py` 定向过滤 + pristine 对照 |
| AC5 行为零变化 | T1（降级文案）、T2（相位表 + Play）、T5（Play 复验） | 相位表逐行 + `editor_play`/`RequestExit`/`find_gameobjects` |
| AC6 回写 | T6 | doc-lint + git diff |
| AC7 子批互斥 | T1–T4 记录快照；T6 汇总差集 | `evidence/a3-N-diff.txt` |

无未映射 AC；无无验收的任务。

## 检查项

- **粒度/顺序**：T1 取件先行（T2 依赖其资产门面），T2 装配与相位化一个编译单元；T3/T4 可与 T1/T2 并行（文件集不相交）；T5 全量收口；T6 文档回写。依赖无环。
- **可测试性**：每个实现任务都有专属定向验证；T5 有全量门禁与 Play 冒烟；T6 有 doc-lint 与台账 diff。
- **范围控制**：任务仅覆盖 A3 三条高 + 一条重复 + 一条低；未混入 A4/A5/A6 条目；未改共享源集/资产。
- **风险处理**：T2 明确相位表与 `FeatureDebugTunables`/`LookTuning` 引用点；T4 明确构造序与初始化守卫；T6 明确 127→122 计数与 A5 删项。
- **证据纪律**：每子批记录 `git status` 差集；结构探针红/绿证据分列；Play 冒烟留截图与对象销毁正证据。
- **并行线隔离**：服务端审计文档改动不进入任务文件集；A3 文档回写只触碰本批相关文件。

## 发现的问题与处理

| 问题 | 严重度 | 处理 |
|---|---|---|
| T2 体量偏大（5 新件 + rig 重写） | minor | 相位表把行为拆成 6+20 步，代码评审可按相位逐段核对；若施工中出现跨域纠缠，按方案回退拆任务（新增 T2a/T2b）并记录 |
| 每子批差集快照未写入 T1–T4 的 verify 字段 | minor | 施工时在 `build-log.md` 记录约定：每任务 `task done` 时附 `git status` 差集命令与结果；T6 汇总核对 |
| T5 的 `wf verify` 固定集不含 27 定向类 | minor | 已明确另跑定向命令并以 `evidence/targeted-*.txt` 留证，二者并列 |

无阻塞问题。

## 评审结论

- 结论: PASS
- 阻塞问题: 0
- 任务清单可执行；范围、依赖、验证与回滚均能追溯到 AC。
