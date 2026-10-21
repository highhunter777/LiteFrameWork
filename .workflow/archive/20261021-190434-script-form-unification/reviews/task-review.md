# 任务评审：脚本形态统一

> 运行 script-form-unification · 创建于 2026-10-21T18:48:37+08:00

## 评审输入

- 方案：`plan.md`
- 任务清单：`tasks.md`
- 设计权威：`Docs/设计文档/架构/脚本形态统一专项设计.md`（v0.1）
- 评审范围：本次运行的 7 个任务（T1～T7）；对应 S-A 登记与检查地基、S-C 触碰文件形态、新入口 S-B 基线。S-B 全量、L2-D1、语言迁移不在本次范围。
- 评审依据：实际读取 `toolchain.json`、`toolchain.py` / `toolchain_surfaces.py`、未登记脚本头部与参数、`.workflow/config.json`、`scripts/README.md`、`.gitattributes` 缺失与工作区 EOL/BOM 实测；并修正了设计中关于 `wg-peer.ps1` 退出码与 deploy 帮助覆盖的两处口径（远端哨兵非本地退出码；deploy 三件已有 comment-based help）。

## 覆盖与追溯

| 验收标准 | 对应任务 | 是否覆盖 | 说明 |
|---|---|---|---|
| AC1 登记完整 | T1 | 是 | 新增 `deploy` 分类与 6 条工具；command/docs 路径由清单校验与测试钉住；索引由 `docs --check` 校验 |
| AC2 孤儿检查有效 | T3 | 是 | 检查器 + 五类豁免 + strict 夹具测试；repo 非 strict 通过并列出存量告警 |
| AC3 内部面归类 | T2 | 是 | 三个 `toolchain_*` 模块、`local-cdn` 加载器、`@oneshot` 补丁脚本；旧路径 grep 为零 |
| AC4 文件形态 | T5 | 是 | `.gitattributes`、触碰文件 LF、PS 编码规则、去 EOL 内容 hash 校验 |
| AC5 新入口契约 | T4 | 是 | 帮助、退出码 2、输出前缀、非交互 caller 检查；契约测试 |
| AC6 业务零变化 | T7 | 是 | 范围审计 + diff 面限定；不改业务逻辑/Unity/共享源集 |
| AC7 文档回写 | T6 | 是 | scripts/README、工具索引、技术支持/README 指针、旧路径清理 |
| AC8 门禁 | T7 | 是 | Python 契约测试、`check`、`docs --check`、Doc lane、L1 |

## 检查项

- [x] 任务粒度可独立执行与验证：每个任务有单一交付面与命令级验收。
- [x] 依赖关系无循环，顺序合理：T1/T2 并行 → T3（依赖二者）；T4 依赖 T1；T5 依赖 T1/T2/T4；T6 依赖前四项；T7 收口。
- [x] 每项任务有明确验收标准与验证方式：7 项均有 acceptance 与 verify。
- [x] 测试策略足以覆盖验收标准：孤儿/编码/EOL 用临时夹具做正反例；清单与契约测试；Doc/L1 收口。
- [x] 通用性与扩展性要求已落到任务：检查器只消费清单/目录/文件头标记，不维护第二份白名单；新入口通过登记接入。
- [x] 不存在硬编码、特判式补丁或范围外改动：唯一显式放行是全局入口 `toolchain.py` 与 hook surface 引用；未把 deploy 脚本改成 Python，未伪造 dry-run。
- [x] 回滚与风险处理已安排：plan §风险与回滚覆盖改名、EOL、清单/索引、调用方、并行 diff、云侧安全。

## 发现的问题与处理

| ID | 严重度 | 问题 | 处理 | 状态 |
|---|---|---|---|---|
| R-1 | 次要 | 检查器 `--strict` 会把存量未触碰文件的 CRLF/编码告警升级为错误，若把“strict 绿”写成本次验收会与“按触碰归一”冲突 | 已在 T3 acceptance 明确：strict 行为用临时夹具验证；repo 只要求非 strict `check` 通过并如实列出存量告警 | 已处理 |
| R-2 | 次要 | 新登记的 `quality.doc-lint` / `quality.scan-missing-guids` 若只进清单，操作文档仍可能找不到 canonical 用法 | T6 acceptance 已补“技术支持或 README 有指针” | 已处理 |
| R-3 | 次要 | `gm.ps1` / `wg-peer.ps1` 当前依赖 Mandatory 参数，非交互调用可能弹提示；改为显式校验前需确认没有调用面依赖交互提示 | T4 acceptance 已补“grep 确认无调用面依赖交互式 Mandatory 提示” | 已处理 |
| R-4 | 信息 | 设计原文把 `wg-peer.ps1` 的远端哨兵 2/3/4 误写成脚本本地退出码，并称 PS 多数无 comment-based help | 已在施工前修正设计 §2/§4.3/§7；本评审以此修正版为准 | 已处理 |
| R-5 | 次要 | T5 初版文件清单把未触碰的 gate 脚本也列入 EOL 归一，违反设计“按触碰归一、不做全仓格式化” | 已收窄 T5 为仅归一 T1/T2/T4 触碰的 deploy/local/scan 文件；其余存量只告警 | 已处理 |

无阻塞问题。

## 评审结论

- 结论: PASS
- 阻塞问题: 0
- 评审人: Qoder（agent）
- 评审时间: 2026-10-21
