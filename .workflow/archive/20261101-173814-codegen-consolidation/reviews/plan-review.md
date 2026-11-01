# 方案评审（独立子代理）：生成器收口（toolbox 单入口 + 腐烂路径修复 + 门禁）

> 运行 codegen-consolidation · 创建于 2026-10-30T19:40:30+08:00
>
> 本文件由主代理根据独立子代理的返回整理落盘；不是主代理的自评。

## 评审输入

- 评审对象：`plan.md`（v8；v1→v8 共 8 轮独立评审）
- 代码/文档基线：HEAD `48ccda61`（`git rev-parse HEAD`）；关键文件：`scripts/codegen/{toolbox,_codegen,gen-build-hash,gen_proto,gen_sync_code,gen_luban,gen-ui-page,gen-ui-api,refresh-hash}.py`、`Luban/{gen.bat,gen_lua_keys.py}`、`.workflow/config.json`、`scripts/gate/toolchain.json`、`scripts/build/{build-player,build-plan}.py`、`scripts/gate/sync-and-sim-e2e.py`、`scripts/hooks/shared-source-reminder.py`、`scripts/local/local-chain.py`、`scripts/tests/{test_generated_artifacts,test_script_contracts_sb,test_refresh_hash,test_hook_reminder,test_toolchain}.py`、`Assets/LiteSim/Editor/SimTrigTableGenerator.cs`、`Assets/LiteGame/Editor/Toolchain/{Actions,Contracts}/*.cs`、`Assets/LiteGame/Editor/Baking/BakeTargets.cs`、设计文档《生成器工具箱专项设计》与说明 10/11/15。
- 评审范围：方案的目标/范围/现状/取舍/设计/测试/验收/风险；不评审 B 已归档的 codegen 源集实现细节，只验证其接口与路径。
- 子代理标识：`plan-reviewer-1` 至 `plan-reviewer-8`（Qoder `general-purpose`，独立上下文、只读）；最终确认轮 `plan-reviewer-8`。
- 子代理约束：只读评审，不修改生产代码、测试与门禁。

## 方案检查

- 目标可观察、范围与非目标清晰：PASS。入口/序/头/门禁/守卫均有可观察判据；非目标明确排除 Battle.cs/.bytes/Luban 模板/SimTrigTables.cs 头、5 个烘焙器搬迁与产物形态收敛。
- 现状与根因有代码/配置/文档依据：PASS。8 处腐烂（6 路径 + 2 注释）均实测；B 已修的 BakeTargets 不再认领；直调清单含 config/toolchain/build-player/sync-e2e/refresh-hash/hook/测试面。
- 候选方案取舍充分：PASS。复用 toolbox 对比新建 CLI、只修路径两案，推荐理由回应约束。
- 设计覆盖接口、数据、关键流程、失败与边界：PASS。`all` 顺序/短路/退出码归一、`--check` 不写跟踪面、luban 的 `reorganize_bin_stage`/CRLF/prune、proto 的 protoc/戳双模式与 sidecar、ui-api MD 区块、trig 的 ToolWriteScope 写盘契约、doctor 的 tracked/untracked 分层与调用点清单、守卫的逐字面量分类。
- 通用性与扩展点明确：PASS。新增生成器只需 `toolbox.TOOLS` 一行 + `all` 序 + `--check` + `path_report()`；路径改由生成器常量/`shared_out_file(root)` 单源读取，禁止第二份手抄。
- 测试策略能先失败后通过：PASS。`test_toolbox`/`test_codegen_rot`/`test_gen_luban_check`/`test_gen_proto_check`/`test_refresh_hash`/`test_hook_reminder` 覆盖主路径、CRLF、sidecar、stale、deprecated、守卫与分类；门禁自检（改 `[StateLayer]`/`.proto` 不重生成）可复现红。
- 风险、监控与回滚可执行：PASS。覆盖 Luban 耗时、门禁自阻塞、CRLF、第三方未入库输入、sidecar `.meta`、工具索引再生、文案面与 trig 写盘契约。

## 验收标准可判定性

| 验收标准 | 判定方式 | 是否可观察/可判定 | 说明 |
|---|---|---|---|
| C1 腐烂路径 | `toolbox.py doctor` 退出码与逐路径输出；`test_hook_reminder` 源集联锁；rot fixture 回放 | 是 | 5 条具体路径 + hook 第 6 面；2 条注释由契约测试/人工核对 |
| C2 proto/sync-code 实跑 | `toolbox proto --protoc <nuget>`、`toolbox sync-code` 后查目录；`local-chain.build_hash()` | 是 | protoc 需本机 NuGet 路径，进度卷记录 |
| C3 all 顺序/短路 | `toolbox all --protoc` 日志序 + 失败退出码；缺参 exit≠0 | 是 | 7 个 toolbox 子命令 |
| C4 all --check 新鲜度 | 退出码 + 本批路径 `git status --porcelain`；sidecar 存在 | 是 | 不写跟踪面；Luban 临时 staging 在 ignored 目录 |
| C5 trig/CRLF | `toolbox trig --check`；`git diff --ignore-cr-at-eol --exit-code`；临时 stale 场景 | 是（需 Unity） | 母本 AC68 按归一文本/git blob 解释 |
| C6 各 --check | 逐 tool 改输入不重生成 → 红；恢复 → 绿 | 是 | 含 luban CRLF/prune、proto 双模式、sync-code 行尾归一 |
| C7 门禁 | `.workflow/config.json` commands + `wf verify` + 改 `[StateLayer]` 红测 | 是 | 覆盖 shared+meta 两常量；PR 档由 commands 承载 |
| C8 单入口/文案面 | `codegen-call-sites.json` 0 未分类/0 非法 invocation；Docs/AGENTS/skills/Assets 的 `.cs` grep 0；deprecated 用例 | 是 | 例外仅 `contract-test`/`compat`/helper |
| C9 生成物头/指纹 | 10 产物签名断言 + 幂等重跑 + sidecar + LuaKeys/sync-code 指纹随动 | 是 | 签名区块 ≤3 行、`>` 收口、无时间戳 |
| C10 gen.bat | `Luban/gen.bat` 与 `toolbox luban` 退出码对比 | 是 | deprecated 提示不影响退出码 |
| C11 文档回写 | 当前 Docs（排除归档）/AGENTS/skills/设计文档命令表改 toolbox；工具索引再生 | 是 | `doc-lint` + grep 0 |
| C12 既有套件 | `toolchain-tests`、`toolchain-docs-check`、L1 PullRequest、`gen-build-hash --check` | 是 | BuildHash 常量不因头/注释变化而变 |
| C13 wf verify | `wf verify` 全命令 | 是（环境依赖） | 含新 `codegen-check` 与再生索引 |

## 发现的问题与处理

| ID | 轮次 | 严重度 | 问题 | 处理 | 状态 |
|---|---|---|---|---|---|
| B1/B2 | 1 | blocker/major | 直调清单漏 `.workflow/config.json` 与 CLI 契约测试；守卫漏 `run_gate` 变量化调用 | v2 补清单；守卫改可执行调用点 + 分类 | 已修复（v8 复核） |
| M1–M5, m1–m7 | 1 | major/minor | hook 旧前缀、local-chain 路径、refresh-hash mock、sidecar 缺失语义、头 `>`、门禁载体、BakeTargets 表述等 | v2 逐项补 | 已修复 |
| N1–N13 | 2 | blocker+major | hook 未覆盖 LiteSync/GameData/Generated、toolbox 自身 deprecated、SimTrig CRLF、doctor 未入库输入、`_run` 漏检、LuaKeys 接口等 | v3/v4 逐项补 | 已修复 |
| R1–R11 | 3 | blocker+minor | proto 行尾归一、守卫路径拼接、三个生成器未走 run_tool、LuaKeys 指纹单源、`.meta` 硬前置等 | v4/v5 逐项补 | 已修复 |
| N1–N11 | 4 | minor | sync-code 整文件指纹、env 泄漏、toolbox 索引文案、`.cs` 提示、戳模式产物存在性等 | v5/v6 补 | 已修复 |
| B1–B10 | 5 | blocker+major | 工具索引未再生、文件级 `doc` 逃检、文档/技能/`.cs` 文案面 | v6/v7/v8 补 | 已修复 |
| N1/N2（末轮） | 7 | major | 文案面文件集不全（`SimTrigToolActions.cs`、说明 05/08/09/12/14/17）；trig 仍走 `Generate()` 绕过 ToolWriteScope | v8 改为封闭 grep 判据；trig 改 public CLI→`SimTrigToolActions.Run` | 已修复；最终轮 PASS |
| minor（skills/AGENTS grep） | 8 | minor | `.agents/skills/**` 与 `AGENTS.md` 不在 Docs grep 面 | v8 已把二者并入 C8/C11/risk13 的 grep 面 | 已修复 |

无遗留阻塞问题；minor 均已处理或明确接受（`Generate()` 保留为无调用兼容方法、不作为写盘入口）。

## 评审结论

- 评审方式: 独立子代理
- 结论: PASS
- 验收标准: PASS
- 阻塞问题: 0
- 评审人: plan-reviewer-8（最终确认轮；前序 plan-reviewer-1..7）
- 评审时间: 2026-11-01
- 复评范围与结果: 第 8 轮仅核 N1 文案面闭环与 N2 trig 写盘契约；结论 PASS，无新 blocker；minor 已并入 v8。
