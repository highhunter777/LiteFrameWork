# 实施任务清单：生成器收口（toolbox 单入口 + 腐烂路径修复 + 门禁）

> 本文件由 workflow.py 生成，请使用 `task` 子命令更新，勿手改。

## 任务清单

阶段：已收尾（待归档） ｜ 完成 10/10（取消 0，阻塞 0）

| ID | 任务 | 依赖 | 状态 | 验收标准 | 验证方式 | 证据 |
|---|---|---|---|---|---|---|
| T1 | 路径单源、path_report 与 hook 源集 | - | 完成 | C1 路径面/C2：gen_proto 与 gen_sync_code 实跑落正确目录；local-chain.build_hash() 返回哈希；hook 对 10 个源集目录均触发且文案为 toolbox；所有工具 path_report() 可用；test_refresh_hash/test_hook_reminder 绿。 | python -m unittest scripts.tests.test_refresh_hash scripts.tests.test_hook_reminder -v；python scripts/codegen/gen_proto.py --protoc C:/Users/hunter/.nuget/packages/google.protobuf.tools/3.36.1/tools/windows_x64/protoc.exe；python scripts/codegen/gen_sync_code.py；python -m unittest scripts.tests.test_gen_path_report -v | 改动 11 文件（_codegen/gen-*/refresh-hash/local-chain/hook/SimTrigTableGenerator）；test_gen_path_report+test_refresh_hash+tes… |
| T2 | 补齐 luban/proto/sync-code 的 --check 与 proto sidecar | T1 | 完成 | C6：三个 --check 可判 stale/新鲜；C4：sidecar 首次生成 + .meta 已提交且首跑绿；luban 不写仓库、不删除文件；proto CRLF/LF 摘要一致。 | python scripts/codegen/toolbox.py luban --check；python scripts/codegen/toolbox.py proto --protoc C:/Users/hunter/.nuget/packages/google.protobuf.tools/3.36.1/tools/windows_x64/protoc.exe（首次生成）；python scripts/codegen/toolbox.py proto --check；python scripts/codegen/toolbox.py sync-code --check；python -m unittest scripts.tests.test_gen_luban_check scripts.tests.test_gen_proto_check -v | gen_luban/gen_proto/gen_sync_code 实现 --check；sidecar Battle.cs.input（sha256:1344da04）+ .meta 已生成；test_gen_luban_check/t… |
| T3 | 生成物头与输入指纹统一（10 个 toolbox 文本产物） | T1, T2 | 完成 | C9：10 产物签名/指纹/幂等；sync-code 非 StateLayer 编辑不改头；LuaKeys 非注册表表不改头；Battle.cs 本体不改、SimTrigTables.cs 不改；生成物正文无 scripts/codegen 直调。 | python -m unittest scripts.tests.test_generated_artifacts -v；重复生成后 git diff 仅预期头变化；grep 10 个签名区块逐条核对 | 10 产物统一 3 行头（toolbox/<tool>+input sha256+regen）；test_generated_artifacts 11 tests OK；build-hash/sync-code/ui-page/ui-ap… |
| T4 | toolbox all/doctor 与 rot 测试 | T1, T2, T3 | 完成 | C3：all 顺序/短路/缺 protoc fail-fast；C1：doctor 5 条具体路径 + hook 联锁 + rot fixture 回放。 | python scripts/codegen/toolbox.py all --check；python scripts/codegen/toolbox.py doctor；python -m unittest scripts.tests.test_toolbox scripts.tests.test_codegen_rot -v；python -m unittest scripts.tests.test_hook_reminder -v | toolbox.py 增 ALL_ORDER/all/doctor；all --check 6/6 真实绿、doctor 0（8 provider + call sites 51）、缺 protoc/未知参数 exit 2；test_to… |
| T5 | run_tool 收口与 deprecated | T4 | 完成 | C8（部分）：7 个生成器直调打印 [deprecated]、经 toolbox（含 toolbox 自身）不打印；ui-page/ui-api 输出前缀为 [codegen/ui-page\|ui-api]；错误/重跑提示无 scripts/codegen 直调。 | python -m unittest scripts.tests.test_toolbox -v；逐个直调 7 个生成器验 stderr；toolbox.py build-hash --help 无 deprecated | IndexAndForwardingTests+DeprecatedContractTests+AllOrder+Doctor 17 tests OK；7 生成器直调 [deprecated]、经 toolbox 无；ui-page/ui… |
| T6 | toolbox trig 与 Unity 写盘契约 | T4, T5 | 完成 | C5：trig 可生成，产物一致（git blob/归一文本），stale 场景非零；写盘经 ToolWriteScope 且 outputs 匹配；TableSize 单源零 diff；SimTrigCli .meta 已提交。 | python scripts/codegen/toolbox.py trig --check；python scripts/codegen/toolbox.py trig；git diff --ignore-cr-at-eol --exit-code -- Assets/LiteSim/Scripts/Map/SimTrigTables.cs；临时改/删文件后 --check 非零 | SimTrigCli.cs 新建（public CLI→ToolRequest allowed outputs→SimTrigToolActions.Run/ToolWriteScope）；SimTrigTableGenerator Is… |
| T7 | （主代理）门禁/config 改道与工具索引再生 | T4, T5 | 完成 | C7：codegen-check 在 commands 且能红；C10：gen.bat 与 toolbox luban 退出码等价；C12（toolchain 两项）：toolchain docs/check/test_toolchain 绿；C13 由阶段 8 verify 承接。 | python scripts/toolchain.py docs --check；python scripts/toolchain.py check；python -m unittest scripts.tests.test_toolchain -v；Luban/gen.bat 与 toolbox.py luban 退出码对比；临时改 [StateLayer] 后 toolbox.py all --check 非零 | 门禁/config 改走 toolbox + codegen-check + toolchain docs 再生；sync-and-sim-e2e.py 加 editor_stop，build-player.py 加 editor_foc… |
| T8 | 直调改道（非门禁）与分类守卫 | T6, T7 | 完成 | C8（代码/守卫面）：0 未分类/0 非法 invocation；7 个生成器 deprecated 用例绿；contract-test/compat 例外；不包含 Docs/AGENTS/skills/Assets .cs 的 grep（T9）。 | python -m unittest scripts.tests.test_toolbox -v；python -m unittest scripts.tests.test_script_contracts_sb -v；python -m unittest scripts.tests.test_generated_artifacts -v | codegen-call-sites.json 更新 build-player.py:287 / sync-and-sim-e2e.py:83 两处 invocation；full test_toolbox 26 tests OK；scr… |
| T9 | （主代理）文档与文案面收口 | T8 | 完成 | C11：文档/文案面 grep 0；doc-lint 绿；工具索引已再生；C8 文案面闭环。 | python scripts/gate/doc-lint.py；python scripts/toolchain.py docs --check；grep 生成器直调（Docs 排除归档、AGENTS、.agents/skills、Assets/**/*.cs）0 命中 | 当前命令面（说明/规范/设计文档/审计 + AGENTS/skills/Assets .cs）非 toolbox 生成器路径 grep 0；历史进度卷按 doc-authoring 纪律恢复原批次记录，仅新增第35章（toolbox 命令… |
| T10 | （主代理）red_commands 预登记 | - | 完成 | 阶段 6 的 wf verify --red 能运行新增测试并观察到预期失败（断言失败，非导入/环境错误）；T10 在阶段 6 前完成。 | python .agents/skills/staged-delivery/scripts/workflow.py --run codegen-consolidation verify --red（阶段 6 执行）；python -m unittest scripts.tests.test_toolbox -v（当前应因功能未实现而失败） | .workflow/config.json red_commands 增加 toolbox-tests/codegen-rot-tests/gen-path-report-tests/gen-luban-check-tests/gen-p… |

## 任务详情

### T1 · 路径单源、path_report 与 hook 源集

- 描述: 修 gen_proto/gen_sync_code/SimTrig/refresh-hash/local-chain/hook 的腐烂路径与注释；新增 _codegen.normalize_eol/input_digest/load_module；gen-build-hash 暴露 shared_out_file(root)；所有 Python 生成器（含 ui-page/ui-api）与 local-chain 暴露 path_report()；hook 前缀覆盖 SHARED_TARGETS 全 10 目录且文案改 toolbox；更新 test_refresh_hash/test_hook_reminder。

- 文件: scripts/codegen/_codegen.py, scripts/codegen/gen_proto.py, scripts/codegen/gen_sync_code.py, scripts/codegen/gen-build-hash.py, scripts/codegen/gen_luban.py, scripts/codegen/gen-ui-page.py, scripts/codegen/gen-ui-api.py, scripts/codegen/refresh-hash.py, scripts/local/local-chain.py, scripts/hooks/shared-source-reminder.py, scripts/tests/test_refresh_hash.py, scripts/tests/test_hook_reminder.py, scripts/tests/test_gen_path_report.py, Assets/LiteSim/Editor/SimTrigTableGenerator.cs

### T2 · 补齐 luban/proto/sync-code 的 --check 与 proto sidecar

- 描述: luban --check：临时 staging + reorganize_bin_stage + CRLF 归一 + prune stale，不 publish/prune；proto --check：有 protoc 归一文本重生成比对、无 protoc 读 sidecar 摘要并先查 Battle.cs 存在；sync-code --check：行尾归一比对；首次生成并提交 Battle.cs.input（主代理用 unity-pipeline 生成 .meta）。

- 文件: scripts/codegen/gen_luban.py, scripts/codegen/gen_proto.py, scripts/codegen/gen_sync_code.py, scripts/tests/test_gen_luban_check.py, scripts/tests/test_gen_proto_check.py, Assets/LiteNet/Core/Proto/Generated/Battle.cs.input

### T3 · 生成物头与输入指纹统一（10 个 toolbox 文本产物）

- 描述: 按 3 行签名（toolbox/<tool> + input sha256:<8> + regen，末行 >）改造 BuildHash×2、EntitySlot.Sync、UiPageCatalog、5 个 UI API、LuaKeys；_codegen.input_digest 行尾归一；sync-code 指纹用解析后的 [StateLayer] 字段模型；LuaKeys 指纹由 gen_lua_keys.collect() 注册表集自算；更新 BuildHash/Meta 模板正文的直调提示为 toolbox；升级 test_generated_artifacts 签名契约。设计文档 v1.2 由 T9 承接（T3 只保证代码/产物/测试）。

- 文件: scripts/codegen/_codegen.py, scripts/codegen/gen-build-hash.py, scripts/codegen/gen_sync_code.py, scripts/codegen/gen-ui-page.py, scripts/codegen/gen-ui-api.py, Luban/gen_lua_keys.py, scripts/tests/test_generated_artifacts.py, Assets/LiteNet/Core/Protocol/BuildHash.g.cs, Server/MetaServer/Host/BuildHash.g.cs, Assets/LiteSim/Diagnostics/Generated/EntitySlot.Sync.g.cs, Assets/LiteGame/Modules/Ui/Generated/UiPageCatalog.g.cs, Assets/LiteFramework/Runtime/Modules/UI/Bind/Generated/UIBindIndex.Api.g.cs, Assets/LiteFramework/Runtime/Modules/UI/Bind/Generated/UIBindBase.Api.g.cs, Assets/LiteFramework/Runtime/Modules/UI/Bind/Generated/UiApiManifest.g.cs, Assets/LiteGame/Adapters/LuaBridge/Generated/UiApiShim.g.cs, Assets/LiteGame/Adapters/LuaBridge/Generated/LuaBehaviourAdapter.Dispatch.g.cs, Assets/LiteGame/Modules/Ui/Generated/LuaKeys.g.cs

### T4 · toolbox all/doctor 与 rot 测试

- 描述: toolbox.py 增 all（严格序、--protoc fail-fast、退出码归一、all --check）与 doctor（消费 T1 的 path_report、git tracked/untracked 分层、Unity C# 常量/MapTarget 解析、调用点清单；分类 fixture 缺失时打印未分类清单）；新增 test_codegen_rot + codegen-rot-paths.json。不含 run_tool/deprecated（T5）与最终分类清单（T8）。

- 文件: scripts/codegen/toolbox.py, scripts/tests/test_toolbox.py, scripts/tests/test_codegen_rot.py, scripts/tests/fixtures/codegen-rot-paths.json

### T5 · run_tool 收口与 deprecated

- 描述: gen-build-hash/ui-page/ui-api 转 _codegen.run_tool；_codegen.run_tool 加 deprecated（transit env，不设模块级 env，toolbox 豁免）；ui-page/ui-api TOOL 名改 ui-page/ui-api 去双前缀；toolbox 无参索引文案改 deprecated；生成器错误/重跑提示（gen-build-hash、gen-ui-page、gen-ui-api）改 toolbox；补 test_toolbox 的 deprecated/提示文案用例。

- 文件: scripts/codegen/_codegen.py, scripts/codegen/gen-build-hash.py, scripts/codegen/gen-ui-page.py, scripts/codegen/gen-ui-api.py, scripts/codegen/toolbox.py, scripts/tests/test_toolbox.py

### T6 · toolbox trig 与 Unity 写盘契约

- 描述: toolbox.py trig [--check] 经 unity command eval 调 LiteGame.Editor 程序集内 LiteGame.Toolchain.SimTrigCli.Run（public；构造 ToolRequest 时 AllowedOutputs 从 toolchain.json codegen.sim-trig outputs 读取）→ SimTrigToolActions.Run → ToolWriteScope；SimTrigTableGenerator 增 IsUpToDate（行尾归一）并修 TableSize 引用/注释；--check 只读；不进 all/门禁；SimTrigCli.cs 的 .meta 由主代理走 unity-pipeline 生成并提交。

- 文件: scripts/codegen/toolbox.py, Assets/LiteGame/Editor/Toolchain/Actions/SimTrigCli.cs, Assets/LiteGame/Editor/Toolchain/Actions/SimTrigToolActions.cs, Assets/LiteSim/Editor/SimTrigTableGenerator.cs, scripts/tests/test_toolbox.py

### T7 · （主代理）门禁/config 改道与工具索引再生

- 描述: 主代理执行，施工子代理禁碰：build-player/sync-e2e 改 toolbox；toolchain.json 命令改 toolbox；.workflow/config.json 增 codegen-check（sync-and-sim-check-shared 改 toolbox）；跑 python scripts/toolchain.py docs 再生 Docs/说明/工具索引.md；验证 gen.bat 与 toolbox luban 等价。red_commands 由 T10 在阶段 6 前单独登记；C12 的 L1/gen-build-hash --check 由阶段 8 verify 承接（T7 只验 toolchain 两项）。

- 文件: scripts/build/build-player.py, scripts/gate/sync-and-sim-e2e.py, scripts/gate/toolchain.json, .workflow/config.json, Docs/说明/工具索引.md

### T8 · 直调改道（非门禁）与分类守卫

- 描述: refresh-hash 内部调用改 toolbox；test_generated_artifacts subprocess 改 toolbox；生成 codegen-call-sites.json（逐 file:line:literal 分类：invocation/import/analysis/signature/doc/text/contract-test/compat/toolbox）；test_toolbox 守卫覆盖 subprocess/_run/run_gate/Path join/反斜杠；test_script_contracts_sb 登记 contract-test。文档/文案 grep 归 T9，不在本任务。

- 文件: scripts/codegen/refresh-hash.py, scripts/tests/test_generated_artifacts.py, scripts/tests/test_toolbox.py, scripts/tests/fixtures/codegen-call-sites.json

### T9 · （主代理）文档与文案面收口

- 描述: 主代理执行：当前 Docs/**/*.md（排除归档）、AGENTS.md、.agents/skills/**.md、设计文档命令表的直调示例改 toolbox；生成器工具箱设计 v1.2（§2 不再出现「单脚本直调保留/不改写任何调用」、§3 消费面、§5 SimTrig 与烘焙登记、§6 工作流）；Assets/**/*.cs 提示改 toolbox（SimTrigToolActions/Baking/GM）；scripts/README、进度卷资源与工具链线新章；说明 11 §9 第二条删除；doc-lint 与 grep 0。裸脚本名（argparse usage/prog）不在直调 grep 面，保留为程序名。

- 文件: Docs/**, Docs/设计文档/架构/生成器工具箱专项设计.md, AGENTS.md, .agents/skills/build-hash-sync, .agents/skills/batch-wrapup, .agents/skills/litegame-online-test-release, scripts/README.md, Assets/LiteGame/Editor/Baking/**, Assets/LiteGame/Editor/Gm/GmTestModePage.cs, Assets/LiteGame/Editor/Toolchain/Actions/SimTrigToolActions.cs, Docs/施工进度/资源与工具链线.md

### T10 · （主代理）red_commands 预登记

- 描述: 主代理在阶段 6 前于 .workflow/config.json 的 red_commands 登记 toolbox-tests（test_toolbox）与 codegen-rot-tests（test_codegen_rot）等新增测试命令；不含生产/测试代码；施工子代理不得改 .workflow/**。

- 文件: .workflow/config.json

