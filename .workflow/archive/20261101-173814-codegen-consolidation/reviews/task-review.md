# 任务评审：生成器收口（toolbox 单入口 + 腐烂路径修复 + 门禁）

> 运行 codegen-consolidation · 创建于 2026-10-30T19:40:30+08:00
>
> 本文件由主代理根据独立子代理的返回整理落盘；结论来自子代理，不是主代理自评。

## 评审输入

- 方案：plan.md（v8）
- 任务清单：tasks.md（T1–T10，CLI 生成）
- 验收标准：acceptance.md（13 条，用户已确认）
- 代码/文档基线：HEAD `48ccda61`；关键文件见 plan「现状与约束」
- 子代理标识：`task-reviewer-1`（首轮）、`task-reviewer-2`（复评）、`task-reviewer-3`（最终确认轮）；Qoder `general-purpose`，独立上下文、只读
- 子代理约束：只读评审，不修改生产代码、测试与门禁
- 评审范围：任务粒度、依赖、验收追溯、测试可执行性、主代理/施工子代理边界、范围外改动

## 覆盖与追溯

| 验收标准 | 对应任务 | 是否覆盖 | 说明 |
|---|---|---|---|
| C1 腐烂路径/doctor | T1,T4 | 是 | T1 提供所有工具的 `path_report()`；T4 的 doctor 消费并含 rot fixture |
| C2 proto/sync-code 实跑 | T1 | 是 | 实跑命令 + `local-chain.build_hash()` |
| C3 all 顺序/短路 | T4 | 是 | toolbox all/doctor |
| C4 all --check/sidecar | T2,T4 | 是 | T2 首次生成 + `.meta`；T4 all --check |
| C5 trig/CRLF | T6 | 是 | SimTrigCli/ToolWriteScope + IsUpToDate |
| C6 各 --check | T2,T4 | 是 | T2 三个新 --check；T4 all --check |
| C7 门禁接入 | T7 | 是 | 主代理 config command |
| C8 单入口/守卫/文案 | T5,T8,T9 | 是 | T5 deprecated/提示；T8 分类守卫；T9 文档/`.cs` grep |
| C9 生成物头/指纹 | T3 | 是 | 10 产物 + LuaKeys/sync-code 指纹 |
| C10 gen.bat 等价 | T7 | 是 | 主代理退出码对比 |
| C11 文档回写 | T9 | 是 | Docs/AGENTS/skills/设计文档/README/进度卷 |
| C12 既有套件 | T7 + 阶段 8 | 是 | T7 toolchain 两项；阶段 8 L1/hash check |
| C13 wf verify | 阶段 8 | 是 | 工作流验证阶段 |

## 检查项

- [x] 任务粒度可独立执行与验证（T1 路径/helper；T2 --check；T3 头/指纹；T4 all/doctor；T5 run_tool；T6 trig；T7 主代理门禁；T8 守卫；T9 文档；T10 red_commands）
- [x] 依赖关系无循环，顺序合理（T1→T2→T3→T4→{T5,T6,T7}→T8→T9；T10 独立、阶段 6 前完成）
- [x] 每项任务有明确验收标准与验证方式（命令/断言/临时 stale 场景）
- [x] 测试策略足以覆盖验收标准（test_toolbox/test_codegen_rot/test_gen_luban_check/test_gen_proto_check/test_generated_artifacts/test_refresh_hash/test_hook_reminder/test_gen_path_report）
- [x] 方案中的通用性与扩展性要求已落到任务（TOOLS/all/path_report/分类清单）
- [x] 不存在硬编码、特判式补丁或范围外改动（路径单源、字段模型指纹、注册表 xlsx 集）
- [x] 回滚与风险处理已安排（sidecar/luban 耗时/门禁自阻塞/CRLF/第三方输入/工具索引/文案面）
- [x] 主代理/施工子代理边界清晰：`.workflow/**`、门禁脚本/配置、文档与 `.cs` 提示归 T7/T9/T10 主代理；施工子代理只做 T1–T6/T8 的代码与测试

## 发现的问题与处理

| ID | 轮次 | 严重度 | 问题 | 处理 | 状态 |
|---|---|---|---|---|---|
| B1 | 1 | blocker | doctor 的 `path_report()` 未覆盖 gen_proto/gen_sync_code/gen_luban/refresh-hash/local-chain | T1 覆盖全部 Python 生成器 + local-chain；T4 消费 | 已修复 |
| B2 | 1 | blocker | T3 漏 5 个 UI API 输出路径 | T3 列全 10 个产物精确路径 | 已修复 |
| B3 | 1 | blocker | `.workflow/config.json` 与门禁脚本/配置被放进施工子代理任务 | 拆出 T7（主代理：toolchain.json/build-player/sync-e2e/config）与 T10（主代理 red_commands） | 已修复 |
| M1–M9 | 1 | major | 共享 test_toolbox/SimTrigToolActions 依赖、sidecar/`.meta`、rot fixture、C10 验证、文案面归属、red_commands、SimTrigCli `.meta`、hook 文案耦合、C12/C13 追溯 | T1–T10 逐项补依赖/文件/验收/主代理边界 | 已修复 |
| 1 | 2 | major | 生成器工具箱设计 §2/§3 未回写 | T9 明确设计文档 v1.2 与 §2/§3/§5/§6 | 已修复 |
| 2 | 2 | major | 生成器错误/重跑提示仍直调 | T5 点名 gen-build-hash/gen-ui-page/gen-ui-api 提示改 toolbox | 已修复 |
| 3 | 2 | major | red_commands 在 T7 implement 才登记，错过阶段 6 | 新增 T10（主代理，无依赖，阶段 6 前完成） | 已修复 |
| minor | 3 | minor | 设计文档 §3 措辞/裸脚本 usage/prog；acceptance 对应任务列占位 | T9 补「§2 不再出现单脚本直调保留」；acceptance 已回填任务映射；裸脚本名保留为 argparse 程序名 | 已处理/明确接受 |

无遗留阻塞问题。

## 评审结论

- 评审方式: 独立子代理
- 评审轮次: 3（首轮 FAIL、复评 FAIL、最终确认 PASS；每轮 blocker/major 均留痕）
- 复评范围与结果: 最终轮核 B1–B3、M1–M9 与前轮 3 个 major；结论 PASS，无新 blocker/major；minor 已处理
- 结论: PASS
- 阻塞问题: 0
- 评审人: task-reviewer-3（最终轮；前序 task-reviewer-1/2）
- 评审时间: 2026-11-01
