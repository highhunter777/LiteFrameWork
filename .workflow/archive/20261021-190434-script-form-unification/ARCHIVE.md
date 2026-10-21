# 归档：脚本形态统一

- 运行: script-form-unification
- 创建时间: 2026-10-21T18:48:37+08:00
- 完成时间: 2026-10-21T19:04:31+08:00
- 归档时间: 2026-10-21T19:04:34+08:00
- 阶段: 8/8
- 任务: 完成 7/7，取消 0
- 验证: PASS（2026-10-21T19:03:39+08:00）

## 交付摘要

- 用户面脚本登记补齐：新增 6 个 canonical 工具，生成索引与技术支持同步；`toolchain.py check` 现在能发现孤儿脚本、编码与 EOL 问题。
- 内部面可机械识别：`_toolchain_*.py`、`_local-cdn.ps1`、`@oneshot`；旧模块名/旧 dot-source 在现行脚本与操作文档中清零。
- 新入口契约基线：帮助可用、缺参/未知命令 exit 2、顶层输出前缀、非交互验证；未执行任何云侧或破坏性动作。
- 文件形态：`.gitattributes` 固定 `scripts/**` 为 LF；本次触碰文件按编码规则归一，转换前后去 EOL/BOM 内容 hash 一致。
- 施工范围严格限定在 scripts/Docs/.gitattributes/.workflow；未改 Unity 资产、共享源集、协议、表或 BuildHash。

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
