# 归档：UI 系统 P1-0/P1-1：口径冻结 + 静态文案 + 页面契约内核

- 运行: ui-p1-contract
- 创建时间: 2026-10-22T21:01:41+08:00
- 完成时间: 2026-10-22T22:20:39+08:00
- 归档时间: 2026-10-22T22:21:19+08:00
- 阶段: 8/8
- 任务: 完成 6/6，取消 0
- 验证: PASS（2026-10-22T22:19:22+08:00）

## 交付摘要

- **P1-0 口径冻结**：`sourceHash` 五域输入、`kind`/`required` 推断、模板边界、`cacheStrategy` 默认（词表真相 `UICacheStrategy`）、无时间戳/稳定排序，全部写入《UI 编辑器工具链专项设计》§5.2.1 与《UI 美术协作与交接规范》§3；schema 与设计样例的 `generatedAt` 漂移修正；《UI 系统总设计》§4.1/§4.2 登记 `UIForm` 静态文案接缝与绑定面遍历单源。
- **P1-1 静态文案**：`LTextKeyRef`（只存 Key）+ `UiStaticTextBinder`（TMP 优先/UGUI 回退、缺目标与空 key 告警一次）+ `UIForm` 生命周期接缝（打开写入、语言刷新、关闭退订、销毁释放）+ `LTextKeyRefInspector`（tbltext 下拉与中英预览）。
- **P1-1 遍历与契约**：`UiBindSurfaceWalker` 成为 hash/契约共同遍历面（既有 hash 值不变）；`UiPageContract`/`UiPageMetadata`/`UiContractJson`/`UiContractPaths`/`UiContractStore`/`UiContractGenerator`/`UiContractDiff` 落地，确定性 JSON、`sourceHash`、模板边界、负例不写盘。
- **测试**：新增 21 例（静态文案 6 / 契约生成 10 / 契约 diff 5），先红后绿；hash 回归 12/12。
- **工装**：`scripts/gate/unity-tests.py` 修两个门禁真空（CLI 30s 默认超时、编译失败仍跑旧程序集）。

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
