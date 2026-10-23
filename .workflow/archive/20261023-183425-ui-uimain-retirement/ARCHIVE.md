# 归档：UI 系统 UIMain 退役：注册表锚迁移 · 产品注册摘除 · 测试载体归位

- 运行: ui-uimain-retirement
- 创建时间: 2026-10-23T17:29:30+08:00
- 完成时间: 2026-10-23T18:34:25+08:00
- 归档时间: 2026-10-23T18:34:25+08:00
- 阶段: 8/8
- 任务: 完成 7/7，取消 0
- 验证: PASS（2026-10-23T18:33:46+08:00）

## 交付摘要

UIMain 从产品面退役：`tbuiform` 删 id=1、启动锚迁 `Page_Login`、`main.lua` 去 require、页面清单 11 页/12 行；prefab/Lua 改名 `Tmp_UiTestPage`/`Tests.UiTestPage` 作为 UI 栈测试载体；真实面 Warning 303→302、命名 Warning 0；11 份产品契约不变。门禁异步通道同时修掉 filter 丢失/域重载/陈旧状态三处稳定性坑。

## 产物

- build-log.md
- evidence/red.json
- evidence/verify.json
- plan.md
- reviews/code-review.md
- reviews/task-review.md
- scripts/RetireUimainAsset.cs
- scripts/RetireUimainManifest.cs
- scripts/SyncPageManifest.cs
- state.json
- tasks.md
- test-plan.md
