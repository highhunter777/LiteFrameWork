# 验收标准确认：生成器收口（toolbox 单入口 + 腐烂路径修复 + 门禁）

> 运行 codegen-consolidation · 创建于 2026-10-30T19:40:30+08:00
>
> 本阶段把方案验收标准逐条提审用户；未经用户确认不得进入任务拆解与施工。

## 验收标准

| 编号 | 验收标准 | 判定方式/验证命令 | 对应任务 |
|---|---|---|---|
| C1 | `toolbox.py doctor` 对 5 处具体路径腐烂（`gen_proto`/`gen_sync_code`/`SimTrigTableGenerator.OutputPath`/`refresh-hash`/`local-chain`）报红，修复后全绿；hook 旧前缀由源集联锁测试报红；`BakeTargets` 现状 tracked 路径绿（未入库 prefab/controller 只提示）；2 处注释腐烂由契约测试/人工核对。 | `python scripts/codegen/toolbox.py doctor` 退出码与逐路径输出；`test_hook_reminder`；`scripts/tests/fixtures/codegen-rot-paths.json` 历史红回放。 | T1,T4 |
| C2 | `gen_proto.py`/`gen_sync_code.py` 实跑并产出到 `LiteNet/Core/Proto/Generated`、`LiteSim/Diagnostics/Generated`；`refresh-hash`/`local-chain` 读常量路径不再崩（`local-chain.build_hash()` 返回哈希）。 | `toolbox proto --protoc <nuget protoc>`、`toolbox sync-code` 后查目录；直接调用 `local-chain.build_hash()`。 | T1 |
| C3 | `toolbox.py all --protoc` 按 `luban → ui-page → ui-api → sync-code → proto → build-hash` 跑完；任一失败归一非零并中止；缺 `--protoc` 开跑前 fail-fast。 | 日志顺序 + 退出码；缺参用例。 | T4 |
| C4 | `toolbox.py all --check` 过期非零、全新鲜零、不写仓库跟踪面；sidecar（含 `.meta`）已提交且首跑绿。 | 退出码；本批生成物路径 `git status --porcelain`；sidecar 存在。 | T2,T4 |
| C5 | `toolbox.py trig` 可用；`trig --check` 返回 0，`git diff --ignore-cr-at-eol --exit-code -- Assets/LiteSim/Scripts/Map/SimTrigTables.cs` 干净；临时 stale 场景非零；`TableSize` 单源引用后产物零 diff。 | `toolbox trig`/`--check`；git diff；临时改/删文件后恢复。 | T6 |
| C6 | `--check` 覆盖 `luban`/`sync-code`/`proto`/`build-hash`/`ui-page`/`ui-api`；proto 的 protoc 与戳双模式可判 stale；luban 覆盖 `reorganize_bin_stage`/CRLF/prune；sync-code 行尾归一。 | 逐 tool 改输入不重生成 → 红；恢复 → 绿。 | T2,T4 |
| C7 | `.workflow/config.json` 的 `commands` 含 `codegen-check` 并出现在 `wf verify` 清单；改 `[StateLayer]` 不重生成 → 该门禁红；`sync-and-sim-check-shared` 改走 toolbox。 | config 检查 + `wf verify` + 改字段红测。 | T7 |
| C8 | 门禁/配置/清单/编排/hook/单测不再直调 codegen 单脚本；`codegen-call-sites.json` 0 未分类/0 非法 invocation；当前 `Docs/**/*.md`（排除归档）、`AGENTS.md`、`.agents/skills/**/*.md`、`Assets/**/*.cs` 的生成器直调 grep 0；7 个 toolbox 子命令直调打印 `[deprecated]`、经 toolbox 不打印。 | 守卫测试 + grep + deprecated 用例。 | T5,T8,T9 |
| C9 | 10 个 toolbox 自产文本产物的签名区块含 `toolbox/<tool>`、`input: ... sha256:<8>`、`regen:`，`>` 收口、≤3 行、无时间戳；重复生成逐字节一致；`Battle.cs` sidecar 可判 stale；LuaKeys 指纹只随注册表 xlsx；sync-code 指纹只随 `[StateLayer]` 字段模型。 | 签名断言 + 幂等重跑 + sidecar + 指纹随动用例。 | T3 |
| C10 | `Luban/gen.bat` 仍可用且与 `toolbox.py luban` 等价（同脚本、同退出码；deprecated 提示不影响退出码）。 | 两者退出码对比。 | T7 |
| C11 | 当前 Docs（排除归档）/AGENTS/skills/设计文档命令表的直调示例改 toolbox；工具索引由 `toolchain.py docs` 再生；说明 11 §9 第二条缺口删除；进度卷新章登记。 | `doc-lint` + grep + 工具索引 `--check`。 | T9 |
| C12 | 既有套件绿（`toolchain-tests`、`toolchain-docs-check`、L1 PullRequest、`gen-build-hash --check`）；BuildHash 常量不因头/注释变化而变。 | 命令退出码与常量比对。 | T7 + 阶段 8 |
| C13 | `wf verify` 全部命令通过（含新 `codegen-check` 与再生后的工具索引）。 | `wf verify`。 | 阶段 8 |

## 与母本字面不同的 4 处裁决（请重点确认）

1. **头/指纹范围收窄**：只覆盖 10 个 toolbox 自产文本产物；`Battle.cs` 用 sidecar 戳、`SimTrigTables.cs` 不改头（在 BuildHash 闭包内，改注释=两端换号）、`.bytes`/Luban 模板不写头。母本 AC71 的“所有生成物”按此收窄，避免与“不动 Battle.cs”冲突。
2. **proto 门禁用无 protoc 的 sidecar 戳模式**：`protoc` 不在仓库；有 protoc 时做重生成比对，无 protoc 时比对输入摘要；sidecar 缺失判红。门禁不依赖本机 protoc 路径。
3. **trig 走 Unity eval + ToolWriteScope，不进 `all`/门禁**：保持“运行时同程序集常量”单源；生成经 `SimTrigToolActions.Run`，`--check` 只读；依赖运行中的 Unity Editor。
4. **文案面按封闭 grep 收口**：当前 Docs（排除归档）、`AGENTS.md`、`.agents/skills`、`Assets/**/*.cs` 的生成器直调 0 命中；归档文档保留历史命令，不在扫描面。

## 提审记录

- 提审时间：2026-11-01
- 提审方式：对话逐条列出 + 结构化提问（含 4 处与母本字面不同的裁决）
- 用户原话或选择：确认通过，开始拆任务

## 用户确认

- 确认结论: 通过
- 确认人: highhunter777
- 确认时间: 2026-11-01
- 变更要求：无

## 变更记录

- 方案 v1→v8 经 8 轮独立子代理评审；主要整改：补两处直调面、守卫改逐字面量分类（覆盖 `_run`/`run_gate`/Path 拼接/反斜杠）、补 hook/local-chain 腐烂、sidecar 缺失判红、`refresh-hash`/`local-chain` 改 `shared_out_file(root)`、头 `>` 闭合、门禁载体改 `.workflow/config.json` commands、工具索引再生、trig 改 ToolWriteScope、文案面改封闭 grep、sync-code 指纹改字段模型、CRLF 归一。
- 用户已确认 4 处“与母本字面不同”的裁决：①头/指纹只覆盖 10 个 toolbox 文本产物；②proto 门禁走无 protoc 的 sidecar 戳；③trig 走 Unity eval + ToolWriteScope、不进 all/门禁；④文案面按当前 Docs+AGENTS+skills+Assets 的 grep 0 收口。
