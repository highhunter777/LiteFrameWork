# 任务评审：UI 系统 P1-5/P1-6

## 评审输入

- 方案：`.workflow/active/ui-p1-hub-pilot/plan.md`
- 任务：`.workflow/active/ui-p1-hub-pilot/tasks.md`（T1～T7）
- 对照依据：[UI 编辑器工具链专项设计](../../../../Docs/设计文档/客户端/UI系统/UI编辑器工具链专项设计.md) §5/§6/§8.3/§9 · [UI 系统总设计](../../../../Docs/设计文档/客户端/UI系统/UI系统总设计.md) §12 · 前批施工记录（P1-0～P1-4）

## 覆盖与追溯

| 验收标准（plan） | 任务 | 覆盖判断 |
| --- | --- | --- |
| AC1 四动作在 Hub 可打开执行；check/docs 通过；无孤儿 | T2 + T7 | 覆盖：T2 落 handler/清单/索引，T7 跑 check/docs |
| AC2 生成 11 份契约（UIMain 跳过）、重复零 diff；diff 有/无基线行为 | T1 + T2 + T5 | 覆盖：T1 批处理与 diff 语义，T2 暴露动作，T5 落盘入库 |
| AC3 试点契约 text[] 三条；静态文案夹具全绿 | T4 + T6 | 覆盖：T4 改 prefab/Lua，T6 断言契约与绑定 |
| AC4 运行期中/英文案正确，动态节点不被覆盖 | T4 + T6 | 覆盖：T6 含运行期验证项 |
| AC5 起页后清单自动出现条目 | T3 | 覆盖：钩子 + 可注入路径供测试 |
| AC6 生成物入库、contractPresent=true、ui.validate 零新增 Error | T5 + T7 | 覆盖：T5 落盘与清单同步，T7 跑校验/门禁 |
| AC7 L1/Doc 全绿、定向夹具全绿 | T6 + T7 | 覆盖 |

## 检查项

- **粒度**：每个任务可独立完成与验证；T1（服务）与 T2（接线）分离，避免"一边写逻辑一边跑工具"；T5 只做落盘与同步，不改逻辑。
- **依赖**：T2→T1；T3→T1（复用行读取口径）；T5→T1,T2；T6→T1～T4；T7→全部。顺序与"先测后实现"的门禁一致（T6 在测试阶段先写、施工阶段转绿）。
- **可测试性**：T1 输出目录可注入；T3 清单路径可注入；T2 走 `EditorToolCatalog`/manifest 对账（纯读）；T4 走真实 prefab（只读断言契约结果）。
- **风险处理**：根名分类（UIMain）与多行登记提示都在 T1；生成物基线在 T5；运行期兜底在 T4 的风险表。
- **范围**：未包含 P2 工作台、五族校验、例外版本到期、HUD/UIMain 根契约；未触碰工作区 P2 在途未提交文件（`UiComposeWindow`/`UiBindingEditor`/`UiPageWizardWindow`/`Preview`/`Templates`）。
- **单源**：批处理复用 `UiBindSurfaceWalker` 与既有生成器；工具注册仍只经 `scripts/gate/toolchain.json`；不新增第二份页面清单/契约定义。

## 发现的问题与处理

| # | 问题 | 处理 |
| --- | --- | --- |
| 1 | T7 原验收漏了把新夹具登记进 `.workflow/config.json` 的 commands/red_commands——否则红测与验证阶段跑不到新用例 | 已用 `task update T7` 补 acceptance 与 files |
| 2 | T5 与 T2 都触碰页面清单 JSON（T2 只改工具索引，T5 才写清单），并行执行会互相覆盖 | 已设 T5 依赖 T1,T2，串行执行 |
| 3 | T3 若无注入面就只能靠真实表测试（慢且污染清单） | acceptance 明确要求清单路径可注入，测试用临时路径 |
| 4 | `ui.contract` 若把 UIMain 判失败，P1 出口条件永远不满足 | T1 明确根名分类为"跳过 + 警告"，并在 T6 断言 |
| 5 | 契约 diff 无基线时若判失败，会与新仓库/清理后状态冲突 | plan/T1 明确"无基线只告警" |

## 评审结论

- 结论: PASS
- 阻塞问题: 0
- 评审人: Codely CLI
- 评审时间: 2026-10-23
