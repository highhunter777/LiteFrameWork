# 归档：UI 系统 P1-2：控件规格 WidgetSpec 与控件清单 UiWidgetManifest

- 运行: ui-p1-widget-manifest
- 创建时间: 2026-10-22T22:40:50+08:00
- 完成时间: 2026-10-22T23:18:19+08:00
- 归档时间: 2026-10-22T23:18:24+08:00
- 阶段: 8/8
- 任务: 完成 6/6，取消 0
- 验证: PASS（2026-10-22T23:17:21+08:00）

## 交付摘要

- **控件能力声明机制**：`WidgetSpec`（Owner/Summary/Slots/States/Events/SizeBehavior/Constraints）+ `WidgetSlotSpec` + `UiWidgetState`（设计 §6.5 词表）+ `UiWidgetSizeBehavior`（Fixed/Expand/FitContent），只声明不驱动、运行时零依赖。
- **作者面**：`WidgetSpecAuthoring`（插槽发现复用 `UiBindSurfaceWalker`，排除 `Clone_`/模板内部件/嵌套 `BindRoot` 子树；幂等并入保留作者填写）+ `WidgetSpecInspector`（Undo + 一键发现）。
- **控件清单**：`UiWidgetManifest` 模型/生成器/JSON 存储/schema；确定性（无时间戳/稳定排序/LF/无 BOM）、`sourceHash`（不含路径/GUID）、缺 spec 告警、声明不一致 Error 且不写盘、输出限定 `Assets/`。
- **30 件内容落盘**：按《UI 命名与控件词表参考》§3 能力表补全；`AgentScripts/BuildWidgetSpecs.cs` + `widget-specs.json` 幂等执行（applied=30 / missing=[] / errors=[]）；prefab 仅新增组件（+627/−0）。
- **序列化单源**：抽出 `UiDeterministicJson`，页面契约与控件清单共用；契约用例 10/10 保持逐字节一致。
- **测试**：14 例（`WidgetSpec` 3 + `UiWidgetManifest` 11），先红后绿；真实目录 30/30 spec、0 告警。
- **门禁**：`wf verify` **PASS 15/15**（L1 1819 · doc · l2-meta · 4 个 UI 夹具 · 其余）。

## 产物

- build-log.md
- evidence/red.json
- evidence/verify.json
- plan.md
- reviews/code-review.md
- reviews/task-review.md
- state.json
- tasks.md
- test-plan.md
