# 验收标准确认：UI 系统 P2-2：起页/登记拆分

> 运行 ui-p2-scaffold-register · 方案 v3 · 2026-10-23
> 提审对象：用户本人（staged-delivery 要求逐条确认，未确认不得进入任务拆解与施工）

## 验收标准

| 编号 | 验收内容（用户可判断） | 判定方式 / 验证命令 |
|---|---|---|
| AC1 | 美术在工作台选「起页」，输入新页名并选视觉基线后，得到 `Screens/Page_*.prefab`（自包含、根 Canvas/CanvasScaler 1920×1080 Match0.5/GraphicRaycaster/CanvasGroup 齐备，SafeAreaReceiver 挂在内容容器上），页面清单出现该页草稿（forms 空、状态 Draft）；不产生 Lua、不修改 `#uiform.xlsx` | EditMode 夹具（组件断言、安全区归属、清单条目、真实表字节比对）；按钮实际可点人工确认一次 |
| AC2 | 美术能对草稿页生成“草稿契约”（`formId=0`）；工程选中草稿页填 id/layer/全屏/缓存策略后：无草稿契约被拒；有则写表一行并备份、生成 Lua 骨架、跑 Luban 与 BuildHash、清单状态变 Registered 且带 formId、缓存策略保留；登记后契约重生成，formId 变真实 id；无 BindRoot 的页面不写绑定 hash | EditMode 夹具（临时表/清单/Lua/hash + 假生成链调用序列）；真实 Luban/BuildHash 以命令级验证；`ui.contract.diff` 真实 11 份基线零破坏 |
| AC3 | 工作台里美术不能登记、工程不能起页（按钮置灰并说明原因）；起页/登记不再显示“P2-2 未提供”；两个工具在 Hub/CLI 清单里是可打开的窗口 | Lane/可用性/能力标签夹具；Catalog 夹具；按钮置灰人工确认一次 |
| AC4 | 失败不静默：起页目标已存在或写盘失败时不留新 prefab、清单不变；登记无草稿契约/无草稿/id 冲突/保留 id/layer 越界时零产物；表写失败经核验后回滚；Luban/BuildHash 失败时回滚表、清单仍 Draft 并给恢复指令；契约刷新失败会报红；工作台在清单被外部改动后拒绝覆盖保存 | EditMode 故障注入夹具（断言失败步、磁盘字节、清单状态） |
| AC5 | `ui.page-scaffold`、`ui.page-register` 在工具清单中无错误，`toolchain.py check` 0 error，工具索引重生成且 `docs --check` 通过 | `python scripts/toolchain.py check`、`docs --check` |
| AC6 | 新增夹具与 Python 单测全绿；既有 UI 夹具、L1、Doc、toolchain、`wf verify` 全绿；测试后真实表/清单/hash/契约零残留，BuildHash `--check-shared` 通过，Validation 基线（11 页/12 行、302 Warning）不变 | `wf verify`、`python scripts/gate/test.py --lane L1 --profile PullRequest`、`gen-build-hash.py --check-shared` |
| AC7 | 08 手册、修改指南、技术总结 04、设计 §3.1/§3.5/§3.6/§5.2/§9、契约 schema、施工进度第 9 章、工具索引都同步；文档门禁 0/0 | `python scripts/gate/doc-lint.py`、`python scripts/gate/test.py --lane Doc` |
| AC8 | 能在编辑器里打开起页/登记两个窗口（标题、关键输入存在、Console 无 error）；真实点击/输入流程人工确认一次并记录 | `unity command eval_file` 冒烟 + 人工确认（自动化只证窗口对象，不证可点——如实记录） |

## 已知边界（本批不做）

- C# 骨架生成（登记窗口显示 Lua，C# 归接线/后续批次）。
- 运行时缓存策略消费（`tbuiform` 无缓存列；只写清单与契约）。
- 设备档/预览矩阵、样式令牌（P2-3）。
- 契约评审签字本身（人工 G2 门；工具只机检“无草稿契约不可登记”）。

## 提审记录

- 2026-10-23：用户已裁决 ①登记范围 B（缓存策略写清单、对账保留、不做 C#）；②契约门槛 A（补草稿契约链）。AC 逐条确认见下。

## 确认

- 确认人：用户（会话内直接选择）
- 确认时间：2026-10-23T23:22+08:00
- 确认结论：通过
- 用户原话/选择：选择「确认通过（推荐）」——按 AC1–AC8 进入任务拆解与先红施工；边界（C# 骨架/运行时缓存/预览矩阵）不变。
