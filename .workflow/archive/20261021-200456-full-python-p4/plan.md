# 方案：全量 Python P4（构建/冒烟）

> 运行 full-python-p4 · 设计权威：[脚本形态统一专项设计](../../../Docs/设计文档/架构/脚本形态统一专项设计.md) v0.2 §6（P4）

## 背景与目标

- P3b 已完成，scripts 只剩 build/deploy 的 PS；P4 迁移 `build-player` 与 `player-smoke`。
- 目标：两个 Python 入口保持门禁/Profile/Pipeline 构建/产物新鲜度/启动标记/MemLoop/InputModal 行为；清单/CI/文档/skills 同批切 Python；删除两 PS。
- 完整 IL2CPP 构建与 Player 冒烟需要 Unity/Player，本批做代码移植 + 离线 help/profile/exe 校验，完整链留机器侧。

## 需求澄清与假设

- 已确认：全量 Python；P4 仅 build/smoke。
- 假设：Unity CLI/Pipeline 契约不变；BuildProfiles JSON 字段不变；Player.log 路径/标记不变；本机无 Unity 会话，不跑完整构建。
- 影响：完整 build/smoke 未跑，机器侧验收待办。

## 范围与非目标

- 新增 `scripts/build/build-player.py`、`player-smoke.py`。
- 删除对应 `.ps1`。
- 清单 build.player/build.smoke、CI L2 构建/冒烟命令、build profile 文档、AGENTS/脚本 README/项目说明/技术支持/代码地图/待办/skills 切 Python。
- 非目标：不改 BuildProfiles/构建参数/标记/内存断言；不跑完整 IL2CPP/Player；不提交。

## 现状与约束

- build-player PS 324 行：restore/L0/buildHash 门禁、Profile、Pipeline 切 target/backend/scenes、bake deploy.args、builtin bundles、dry_run/confirm、build_status 轮询、产物/新鲜度。
- player-smoke PS 201 行：清旧进程/log、启动 exe、标记轮询、MemLoop/InputModal 断言。
- 约束：Pipeline CLI 输出表格的 tab-field 3 为 Result JSON；eval_file 写 UTF-8 无 BOM；构建 IL2CPP 10-30min；smoke Windows Player。

## 候选方案与取舍

| 方案 | 思路 | 取舍 |
|---|---|---|
| A 两脚本 + 离线验收（采用） | 完整移植，help/profile/exe 离线测试；完整链机器侧 | 代码全迁，构建风险明确待验 |
| B 只迁 smoke | build 后置 | 构建仍 PS，收益低 |
| C 不迁 | 保持 PS | 与全量目标不符 |

## 设计要点

- build-player：argparse `--project-path/--profile/--skip-restore-check/--skip-bundles`；Pipeline 封装含 Result JSON 提取；eval_file 写 Temp；门禁调 Python restore/L0/buildHash；Profile 驱动；bundle/构建轮询/产物新鲜度按 PS 移植。
- player-smoke：argparse `--project-path/--exe-path/--timeout-sec/--player-arg`（append）/`--mem-loop/--mem-loop-cycles/--mem-loop-max-growth-mb/--input-modal`；清旧进程、Player.log、标记轮询、MemLoop/InputModal 断言。
- 错误路径：profile/exe 缺失、Pipeline 失败、bundle 失败、dry_run/构建拒绝、产物缺失/陈旧、标记超时均 exit 1。
- 注册面：build.player/build.smoke Python；CI L2 命令；工具索引；文档/skills。
- 过渡：P4 完成后 scripts 仅 deploy/local-chain 仍 PS，P5 清零。

## 通用性与扩展性

- 复用 `_common`；Pipeline 封装集中 build-player，不另写一套；标记/内存断言集中在 smoke。
- 不引入构建框架；Profile 字段仍是唯一配置源。
- 测试用临时 profile/exe 夹具，不依赖 Unity。

## 测试策略

- 红测：build/smoke 缺失 -> 断言失败；结构清单/CI 旧命令失败。
- 实现后：两脚本 `--help`；build-player 缺 profile exit 1；player-smoke 缺 exe exit 1；py_compile；全量 Python/L1/Doc/toolchain/BuildHash。
- 完整 IL2CPP/Player smoke 不跑，机器侧验收。

## 验收标准

- [ ] build-player.py 完整移植门禁/Profile/Pipeline/bundle/build/产物校验；help + 缺 profile 可离线验。
- [ ] player-smoke.py 完整移植启动/标记/MemLoop/InputModal；help + 缺 exe 可离线验。
- [ ] 清单/CI/文档/skills 切 Python；两 PS 删除；工具索引 current。
- [ ] Python 全测、L1 1801、Doc 0/0、toolchain check、BuildHash。
- [ ] 完整构建/冒烟未跑需如实记录；未改 Unity/共享源集；未提交。

## 风险与回滚

| 风险 | 处理 |
|---|---|
| Pipeline/构建差异 | 保留 PS 一版可回退；完整链机器侧对照 |
| Player.log/标记/内存断言差异 | 逐条移植；离线不宣称通过 |
| CI 命令错误 | 文本检查 + 本地 help |
| 回滚 | 恢复两 PS/清单/CI/文档；无数据迁移 |
