# 施工记录：生成器收口（toolbox 单入口 + 腐烂路径修复 + 门禁）

> 运行 codegen-consolidation · 创建于 2026-10-30T19:40:30+08:00

## 施工记录

<!-- 任务开始、完成、阻塞、取消等事件由 workflow.py 自动追加。 -->
- 2026-11-01T13:22:24+08:00 · 新增任务 T1「路径单源与腐烂修复」
- 2026-11-01T13:22:29+08:00 · 新增任务 T2「补齐 luban/proto/sync-code 的 --check 与 proto sidecar」
- 2026-11-01T13:22:33+08:00 · 新增任务 T3「生成物头与输入指纹统一（10 个 toolbox 文本产物）」
- 2026-11-01T13:22:36+08:00 · 新增任务 T4「toolbox all/doctor 与 run_tool 收口/deprecated」
- 2026-11-01T13:22:40+08:00 · 新增任务 T5「toolbox trig 与 Unity 写盘契约」
- 2026-11-01T13:22:43+08:00 · 新增任务 T6「直调改道与 codegen-call-sites 分类守卫」
- 2026-11-01T13:22:46+08:00 · 新增任务 T7「门禁接入与工具索引再生」
- 2026-11-01T13:22:49+08:00 · 新增任务 T8「文档与文案面收口」
- 2026-11-01T13:28:39+08:00 · 更新任务 T1（acceptance, description, files, title, verify）
- 2026-11-01T13:28:44+08:00 · 更新任务 T2（acceptance, deps, description, files, title, verify）
- 2026-11-01T13:28:48+08:00 · 更新任务 T3（acceptance, deps, description, files, title, verify）
- 2026-11-01T13:28:52+08:00 · 更新任务 T4（acceptance, deps, description, files, title, verify）
- 2026-11-01T13:28:55+08:00 · 更新任务 T5（acceptance, deps, description, files, title, verify）
- 2026-11-01T13:28:59+08:00 · 更新任务 T6（acceptance, deps, description, files, title, verify）
- 2026-11-01T13:29:02+08:00 · 更新任务 T7（acceptance, deps, description, files, title, verify）
- 2026-11-01T13:29:06+08:00 · 更新任务 T8（acceptance, deps, description, files, title, verify）
- 2026-11-01T13:29:10+08:00 · 新增任务 T9「（主代理）文档与文案面收口」
- 2026-11-01T13:37:27+08:00 · 更新任务 T3（acceptance, description）
- 2026-11-01T13:37:30+08:00 · 更新任务 T5（acceptance, description, files）
- 2026-11-01T13:37:33+08:00 · 更新任务 T6（acceptance, description, files）
- 2026-11-01T13:37:36+08:00 · 更新任务 T7（acceptance, description）
- 2026-11-01T13:37:41+08:00 · 新增任务 T10「（主代理）red_commands 预登记」
- 2026-11-01T13:37:46+08:00 · 更新任务 T9（description, files）
- 2026-11-01T13:37:49+08:00 · 更新任务 T4（description）
- 2026-11-01T13:37:53+08:00 · 更新任务 T1（verify）
- 2026-11-01T13:37:54+08:00 · 更新任务 T2（verify）
- 2026-11-01T13:40:18+08:00 · 更新任务 T9（description）
- 2026-11-01T13:40:39+08:00 · 开始任务 T10「（主代理）red_commands 预登记」
- 2026-11-01T13:40:47+08:00 · 完成任务 T10 · 证据: .workflow/config.json red_commands 增加 toolbox-tests/codegen-rot-tests/gen-path-report-tests/gen-luban-check-tests/gen-proto-check-tests；JSON 解析通过，阶段 6 前完成。
- 2026-11-01T13:56:26+08:00 · 开始任务 T1「路径单源、path_report 与 hook 源集」
- 2026-11-01T14:01:46+08:00 · 完成任务 T1 · 证据: 改动 11 文件（_codegen/gen-*/refresh-hash/local-chain/hook/SimTrigTableGenerator）；test_gen_path_report+test_refresh_hash+test_hook_reminder 31 tests OK；gen-build-ha…
- 2026-11-01T14:01:46+08:00 · 开始任务 T2「补齐 luban/proto/sync-code 的 --check 与 proto sidecar」
- 2026-11-01T14:06:20+08:00 · 完成任务 T2 · 证据: gen_luban/gen_proto/gen_sync_code 实现 --check；sidecar Battle.cs.input（sha256:1344da04）+ .meta 已生成；test_gen_luban_check/test_gen_proto_check 18 tests OK；toolbox …
- 2026-11-01T14:06:20+08:00 · 开始任务 T3「生成物头与输入指纹统一（10 个 toolbox 文本产物）」
- 2026-11-01T14:12:23+08:00 · 完成任务 T3 · 证据: 10 产物统一 3 行头（toolbox/<tool>+input sha256+regen）；test_generated_artifacts 11 tests OK；build-hash/sync-code/ui-page/ui-api/luban/proto --check 全 0；BuildHash 常量 b…
- 2026-11-01T14:12:23+08:00 · 开始任务 T4「toolbox all/doctor 与 rot 测试」
- 2026-11-01T14:18:13+08:00 · 完成任务 T4 · 证据: toolbox.py 增 ALL_ORDER/all/doctor；all --check 6/6 真实绿、doctor 0（8 provider + call sites 51）、缺 protoc/未知参数 exit 2；test_toolbox AllOrderContractTests+DoctorTests+…
- 2026-11-01T14:18:13+08:00 · 开始任务 T5「run_tool 收口与 deprecated」
- 2026-11-01T14:24:54+08:00 · 完成任务 T5 · 证据: IndexAndForwardingTests+DeprecatedContractTests+AllOrder+Doctor 17 tests OK；7 生成器直调 [deprecated]、经 toolbox 无；ui-page/ui-api 单前缀；hints 改 toolbox；all --check/doc…
- 2026-11-01T14:24:54+08:00 · 开始任务 T6「toolbox trig 与 Unity 写盘契约」
- 2026-11-01T14:33:18+08:00 · 开始任务 T7「（主代理）门禁/config 改道与工具索引再生」
- 2026-11-01T14:33:18+08:00 · 完成任务 T7 · 证据: build-player/sync-e2e/toolchain.json/.workflow config 改走 toolbox；codegen-check 加入 commands；toolchain docs 再生+docs-check 0；test_toolchain+test_build_player+test…
- 2026-11-01T14:33:22+08:00 · 完成任务 T6 · 证据: SimTrigCli.cs 新建（public CLI→ToolRequest allowed outputs→SimTrigToolActions.Run/ToolWriteScope）；SimTrigTableGenerator IsUpToDate/TableSize 单源；toolbox trig eval …
- 2026-11-01T14:33:22+08:00 · 开始任务 T8「直调改道（非门禁）与分类守卫」
- 2026-11-01T14:41:57+08:00 · 完成任务 T8 · 证据: refresh-hash 内部改 toolbox；codegen-call-sites.json 实扫重建 55 条（invocation 14/contract-test 10/doc 8/text 7/import 6/toolbox 5/signature 3/compat 1/analysis 1）；full…
- 2026-11-01T14:42:02+08:00 · 开始任务 T9「（主代理）文档与文案面收口」
- 2026-11-01T14:42:02+08:00 · 完成任务 T9 · 证据: 当前 Docs(排除归档)/AGENTS/skills/Assets .cs 非 toolbox 生成器路径批量改 toolbox（54 文件）；设计文档 v1.2、说明 10/11、规范 02/03、skills；进度卷第35章+索引；doc-lint 0、docs-check 0、Doc suite PASS、a…
- 2026-11-01T14:45:04+08:00 · 重新打开任务 T9：代码评审发现批量替换改写历史进度卷；恢复原批次记录，仅保留本批第35章，并收紧命令面 grep
- 2026-11-01T14:45:04+08:00 · 开始任务 T9「（主代理）文档与文案面收口」
- 2026-11-01T14:45:04+08:00 · 完成任务 T9 · 证据: 当前命令面（说明/规范/设计文档/审计 + AGENTS/skills/Assets .cs）非 toolbox 生成器路径 grep 0；历史进度卷按 doc-authoring 纪律恢复原批次记录，仅新增第35章（toolbox 命令）；设计文档 v1.2、说明 10/11、规范 02/03、skills；代码位…
- 2026-11-01T15:31:18+08:00 · 重新打开任务 T7：verify 的 sync-and-sim-e2e 在 PlayMode 测试后失败（build-player bundle 构建在 Play 模式只返回 starting 并重试）；在 e2e build-player 前加幂等 editor_stop
- 2026-11-01T15:31:18+08:00 · 开始任务 T7「（主代理）门禁/config 改道与工具索引再生」
- 2026-11-01T16:55:40+08:00 · 完成任务 T7 · 证据: 门禁/config 改走 toolbox + codegen-check + toolchain docs 再生；sync-and-sim-e2e.py 加 editor_stop，build-player.py 加 editor_focus/set_autotick（bundle 前启用、finally 关闭）；e…
- 2026-11-01T17:20:32+08:00 · 重新打开任务 T8：T7 的 editor_stop/focus/autotick 改动使 build-player/sync-e2e 的 toolbox 调用点行号漂移；同步重生成 codegen-call-sites.json
- 2026-11-01T17:20:32+08:00 · 开始任务 T8「直调改道（非门禁）与分类守卫」
- 2026-11-01T17:20:32+08:00 · 完成任务 T8 · 证据: codegen-call-sites.json 更新 build-player.py:287 / sync-and-sim-e2e.py:83 两处 invocation；full test_toolbox 26 tests OK；scripts/tests 全量 discover exit 0；all --chec…

## 关键决定

- T1：`gen-build-hash.py` 以 `SHARED_OUT_REL` + `shared_out_file(root)` 作为客户端常量路径唯一来源；`refresh-hash`/`local-chain` 经 `_codegen.load_module('gen-build-hash')` 调函数，`test_refresh_hash` 的 mock root 继续有效。
- T1：`_codegen` 新增 `normalize_eol`/`input_digest`/`load_module`，指纹规则（排序 + 相对路径 + NUL + 行尾归一 + 前 8 位）单源；各工具 `path_report()` 用自身常量派生，不维护第二份路径清单。
- T1：hook 源集前缀固定为当前 10 个 SHARED_TARGETS 目录，文案改 `toolbox.py build-hash`/`toolbox.py proto`；与源集联锁由 `test_hook_reminder` 覆盖，不让 hook 动态 import 生成器。
- T1：`SimTrigTableGenerator.OutputPath`/类头路径修正；`TableSize`/`Generate`/`IsUpToDate` 留 T6，避免与 `trig` 写盘契约混批。
- T2：`gen_luban --check` 的 `check_publish(source_dir,target_dir,extension)` 复用 `publish` 的行尾归一；prune 候选只查 `.bytes`（生成路径也只对 `.bytes` 调 `prune_stale`；把 `.cs` 多余文件判 stale 会误伤共享 `Modules/Ui/Generated`）。
- T2：`gen_proto` 增 `SIDECAR_REL` + `check(root, protoc='')`；sidecar `battle.proto sha256:1344da04`；有 protoc 做归一文本重生成比对，无 protoc 做摘要戳；生成成功才写 sidecar，check 不写盘。
- T2：`gen_sync_code --check` 按目标行尾归一比对，不重写；生成仍 CRLF。
- T2：sidecar 放在 Assets 内，`.meta` 由主代理用 Unity `AssetDatabase.Refresh`（unity-pipeline CLI）生成，不手写 `.meta`。
- T3：`_codegen.signature()`/`digest_text()` 单源生成 3 行头（`toolbox/<tool>` + `input: … sha256:<8>` + `regen: …>`）；各生成器不再各写头。
- T3：指纹分层——build-hash 用源集摘要前 8 位，sync-code 用解析后的 `[StateLayer]` 字段模型，LuaKeys 只用注册表 xlsx，ui-page 用 table+manifest+contracts，ui-api 用 spec；BuildHash/Meta 模板正文直调提示改 toolbox，常量不变。
- T3：10 个产物重生成；两次生成逐文件 sha256 相同（幂等）；旧 `UiPageCatalog` 头部的说明语义移到类摘要/设计文档（T9），不保留在 3 行头里。
- T4：`toolbox.ALL_ORDER` 单源（luban→ui-page→ui-api→sync-code→proto→build-hash，不含 trig/refresh-hash）；`all` 缺 `--protoc` 在任何工具前 fail-fast，`--check` 逐 tool 传 `--check`，生成模式只给 proto 传 protoc，首个失败即中止并归一退出码。
- T4：`doctor` 消费各工具 `path_report()` + `local-chain`；按 git 分层：tracked 缺失红、git-ignored 缺失提示、未跟踪且未忽略缺失红（历史腐烂同型才可报红）；解析 SimTrig/BakeTargets 路径型字符串；打印 8 provider + `codegen-call-sites.json` 条目数。
- T6：`toolbox trig` 经 `unity command eval` 调 `LiteGame.Toolchain.SimTrigCli`；`SimTrigCli` 从 `toolchain.json` 的 `codegen.sim-trig.outputs` 组装 `ToolRequest.AllowedOutputs`，转调 `SimTrigToolActions.Run`（`ToolWriteScope`），不调 `Generate()`；`--check` 调 `IsUpToDate()`（行尾归一）只读。
- T6：`SimTrigTableGenerator.TableSize` 引用 `SimTrig.TableSize`（同值零 diff），`IsUpToDate` 归一比较；`Generate()` 保留为无调用兼容方法，不作为 CLI 写入口；`SimTrigCli.cs.meta` 由 Unity 自动导入生成。
- T7（主代理）：`build-player.py`/`sync-and-sim-e2e.py` 的 buildHash 门禁步改 `toolbox.py build-hash --check-shared`；`toolchain.json` 的 build-hash/proto/ui-page/ui-api 命令改 toolbox；`.workflow/config.json` 的 `sync-and-sim-check-shared` 改 toolbox 并新增 `codegen-check`（`toolbox all --check`）；`python scripts/toolchain.py docs` 再生 `Docs/说明/工具索引.md`。
- T7：C7 红/绿实证——临时在 `EntitySlot` 加 `[StateLayer] GateProbe`，`toolbox all --check` 在 4/6 sync-code 步失败；恢复源码后 6/6 绿（源码已还原，`git diff` 干净）。
- T7：C10 `Luban/gen.bat --help` 与 `toolbox.py luban --help` 均 exit 0（同一脚本；gen.bat 打印 deprecated 不影响退出码）。
- T7：e2e 单跑通过（`summary ok=true`；bundles OK、Player PASSED、player-smoke OK、client/server hash `be285f5c37e147d9`）；`test_build_player` 19 tests OK。
- T8：`refresh-hash` 内部 build-hash 步改 toolbox；`test_generated_artifacts` 已是 toolbox；`codegen-call-sites.json` 以 `scan_call_sites(ROOT)` 实扫重建 55 条（invocation 14 / contract-test 10 / doc 8 / text 7 / import 6 / toolbox 5 / signature 3 / compat 1 / analysis 1）；full `test_toolbox` 26 tests 全绿。
- T9（主代理）：当前命令面（说明/规范/设计文档/审计）+ AGENTS/skills/`Assets/**/*.cs` 的非 toolbox 生成器路径批量改 `toolbox.py <tool>`；历史进度卷按 doc-authoring 纪律**不回写**（仅新增第 35 章）。设计文档 v1.2（入口/头指纹/all/doctor/trig/check/豁免/扩展）；说明 10 增 `codegen-check`；说明 11 删除已修的 local-chain/refresh-hash 缺口；规范 02/03 与 skills 命令改 toolbox；进度卷新增第 35 章并登记索引；审计登记表的代码位置改为“toolbox → 内部 gen-* 脚本”指向，不冒充已修。
- T7：最终 verify 暴露 e2e 在 PlayMode 测试后留在 Play 模式，build-player 的 bundle 构建只返回 starting 并重试；在 `sync-and-sim-e2e.py` 的 build-player 前加幂等 `unity command editor_stop`，并在 `build-player.py` 构建 bundle 前加 `editor_focus`（最小化/后台编辑器会让异步 bundle job 停在 starting）；重跑 e2e/verify。
- T7：e2e 失败根因进一步定位为 **Unity Editor update 循环挂起**（`EditorApplication.delayCall` 探针不执行，`set_autotick`/前台聚焦/recompile 均无效）；清空积压回调后由用户重启编辑器窗口，delayCall 探针恢复 true；e2e 单跑通过（summary ok=true，bundles OK / Player PASSED / player-smoke OK）。该失败与生成器代码无关，属运行环境状态。
- T9：`doc-lint` 0 违规、工具索引 current、`all --check` 6/6、Doc suite PASS、当前命令面非 toolbox 路径 grep 0（排除历史 `Docs/施工进度/**` 与 `Docs/归档记录/**`）；Assets `.cs` 0。

## 偏差与处理

- T1 与方案对应：`gen-ui-page`/`gen-ui-api` 只新增 `path_report()`（TOOL/run_tool 留 T5）；`refresh-hash` 内部 subprocess 仍直调（留 T8）；`SimTrigTableGenerator` 只改路径/注释（TableSize/IsUpToDate 留 T6）。均为计划内的任务切分，不是范围扩张。
- T2 与方案对应：`check_publish` 的 prune 只覆盖 `.bytes`，与 `gen_luban` 生成路径的实际 `prune_stale` 调用面一致；`.cs` 目标（共享 UiPageCatalog）不做 prune 判定，避免假 stale。
- T3：`gen-ui-page` 对仓库外输入退化为绝对路径入指纹（本地确定、不可跨机复用）；默认路径全在仓内，`--check` 不受影响，记录为残余说明。
- T4：doctor 对“未跟踪且未 git-ignored 的缺失路径”判红（比字面“未入库只提示”更严）；第三方 ignored 资源（prefab/controller）仍只提示，clean clone 不红；这是让 5 条历史腐烂同型可报红的必要裁决。
- T6：`trig` 首次把工作区 CRLF 的 `SimTrigTables.cs` 写成 LF（`BuildContent` 产 LF），git blob 未变（`git diff` 0），工作区 stat 一度显示 ` M`，由 `git update-index --refresh` 清除；C5 判据仍用 `git diff --ignore-cr-at-eol`。
- T7：`toolchain.py check` 仍报两条既有 warning（`sync-and-sim-e2e.py`/`unity-tests.py` 未登记为可执行脚本），非本批引入；`sync-and-sim-e2e.py` 顶部说明仍是裸脚本名（非直调命令，T8/T9 分类/文案处理）。
- `gen_proto` 重生成后 Git 提示 `LF will be replaced by CRLF`，但 `git diff --exit-code` 为 0（行尾归一由 git 处理），不构成产物变更。

## 问题与修复

- `gen_proto` 旧 `proto_dir` 硬编码 + 不建输出目录 → 用 `PROTO_REL` 推导并 `makedirs`；实跑成功，`Battle.cs` 无 diff。
- `gen_sync_code` OUT_FILE 旧 `LiteSim/Core/Diagnostics/Generated` → 修为 `LiteSim/Diagnostics/Generated`；实跑 `up to date`，无 diff。
- `refresh-hash`/`local-chain` 读旧 `Assets/LiteNet/Protocol/BuildHash.g.cs` → 改 `shared_out_file(root)`；`test_refresh_hash` 的 mock root 用例绿。
- hook 旧前缀导致共享源提醒不触发 → 10 个当前前缀 + `test_hook_reminder` 源集联锁绿。
- `gen-build-hash` 模板注释旧 `LiteSim.Core` → 修为 `LiteSim/{Scripts,Systems}`；`--check` 仍绿（shared=`be285f5c37e147d9`、meta=`75e0e85fe3c33cf2`），常量未变。
- sidecar 初始缺失 → T2 首次生成 `Battle.cs.input`（29 B）；Unity 导入后生成 `Battle.cs.input.meta`（155 B），未手写 `.meta`。
- `generator.check` 缺 `battle.proto` 时走通用 FAILED（exit 1），不是专门的 STALE 行；不影响非零判定，记录为残余提示。
- T3：旧签名（各生成器各写）被 `signature()` 替换；`gen-ui-api` 的 `HEADER` 改为惰性 `header()`，避免 import/`--help` 读 spec；`gen_lua_keys` 拆 `scan()` 保留唯一扫描面。
- T4：`doctor` 的 untracked-non-ignored 判红规则（见偏差）解决了历史腐烂同型；`all --check` 真实跑 6/6；缺 protoc/未知参数 exit 2 且不触发子进程。
- T6：`toolbox trig` 的 eval JSON 结构为 `data.result.{success,result,diagnostics}`；编译失败时顶层 `success=true`，按内层判失败；`unity`/Pipeline 缺失给出单行 `[codegen/toolbox] FAILED`。
- T7：门禁/config 改道后 `toolchain.py docs` 再生索引；C7 临时字段探针红/绿；`test_toolchain`+`test_build_player`+`test_script_contracts_sb` 69 tests OK。

## 验证结果

- 最终 `wf verify`：PASS 65/65（2026-11-01T17:37:24+08:00，evidence/verify.json）；先行测试 PASS（5 个新红测按预期失败）；L1 1969、toolchain-tests 435、doc-lint 0、codegen-check 2304ms、sync-and-sim-e2e 152345ms（summary ok=true）。
- 2026-11-01T13:56:17+08:00 · 先行测试 PASS · 5/18 · 证据 evidence/red.json
- T1：`python -m unittest scripts.tests.test_gen_path_report scripts.tests.test_refresh_hash scripts.tests.test_hook_reminder` → 31 tests OK（exit 0）。
- T1：`python scripts/codegen/gen-build-hash.py --check` → shared/meta 两常量 OK；`gen_proto --protoc ...` + `git diff --exit-code -- Battle.cs` → 0；`gen_sync_code.py` → up to date + diff 0。
- T2：`python -m unittest scripts.tests.test_gen_luban_check scripts.tests.test_gen_proto_check` → 18 tests OK；`toolbox luban/proto/sync-code --check` → 0/0/0；`gen-build-hash --check` → 0；Battle.cs/EntitySlot.Sync diff 0；sidecar + `.meta` 已生成。
- T3：`python -m unittest scripts.tests.test_generated_artifacts` → 11 tests OK；`toolbox build-hash/sync-code/ui-page/ui-api/luban/proto --check` → 全 0；`gen-build-hash --check` 常量不变；Battle.cs/SimTrigTables diff 0；两次生成 sha256 幂等；L1 PullRequest 1969 tests PASS。
- T4：`python -m unittest scripts.tests.test_toolbox.AllOrderContractTests scripts.tests.test_toolbox.DoctorTests scripts.tests.test_codegen_rot` → 14 tests OK；`toolbox all --check` → 0（6/6）；`toolbox doctor` → 0（8 provider + call sites 51）；缺 `--protoc`/未知参数 → exit 2。
- T5：`test_toolbox.IndexAndForwardingTests+DeprecatedContractTests` 10 tests OK；T4 两类 7 tests 保持绿；7 生成器直调 deprecated、经 toolbox 无；ui 单前缀；all/doctor/hash 仍 0。
- T6：`test_toolbox.TrigContractTests` 3 tests OK（live Unity 跑）；`toolbox trig --check` → 0；`toolbox trig` → 0（ToolWriteScope，重生成 sha256 `5252bc25…` 一致）；`SimTrigTables.cs` diff 0；`gen-build-hash --check` 0；`SimTrigCli.cs.meta` 已由 Unity 生成。
- T7：`toolchain.py docs` 再生 + `docs --check` 0；`toolchain.py check` 0（2 既有 warning）；`test_toolchain`+`test_build_player`+`test_script_contracts_sb` 69 tests OK；`toolbox all --check` 0；C7 临时 StateLayer 字段 → 4/6 sync-code 红，恢复后 6/6 绿；`Luban/gen.bat --help`/`toolbox luban --help` 均 0。
- T8：`python -m unittest scripts.tests.test_toolbox` → 26 tests OK（含 live trig）；`test_generated_artifacts`+`test_script_contracts_sb` 23 tests OK；`test_hook_reminder`+`test_refresh_hash` 17 tests OK；`toolbox all --check` 0；`doctor` 55 call sites。
- T9：`doc-lint` 0 违规/0 warning；`toolchain.py docs --check` 0；`test.py --suite Doc` PASS；`test_toolchain`+`test_script_contracts_sb` 50 tests OK；`all --check` 6/6；当前命令面 grep（说明/规范/设计/审计 + AGENTS/skills/Assets `.cs`）0 命中；历史进度卷/归档不改写。
- 代码评审整改：批量替换误改了 `调试与开发工具链演进路线图.md` 的 Markdown 链接目标（`toolbox.py build-hash` 带空格）；恢复为 `toolbox.py` 文件链接。批量替换也改写了历史进度卷，已按 doc-authoring 恢复 HEAD 原批次记录，只保留本批第 35 章。
- 2026-11-01T15:20:07+08:00 · 验证 FAIL · 64/65 · 证据 evidence/verify.json
- 2026-11-01T17:18:45+08:00 · 验证 FAIL · 64/65 · 证据 evidence/verify.json
- 2026-11-01T17:37:24+08:00 · 验证 PASS · 65/65 · 证据 evidence/verify.json

## 交付摘要

- 7 个生成器统一 `toolbox.py` 单入口：`all`（严格序）、`all --check`、`doctor`（path_report + tracked/untracked + 调用点清单）、`trig`（Unity eval → ToolWriteScope）；门禁/配置/编排/提醒/单测直调改道，人工直调打印 `[deprecated]`，`Luban/gen.bat` 兼容别名。
- 修 6 处路径腐烂 + 2 处注释腐烂；新增 `_codegen.normalize_eol/input_digest/signature/load_module`、`shared_out_file(root)`、各工具 `path_report()`；hook 覆盖 SHARED_TARGETS 全 10 目录。
- `luban`/`proto`/`sync-code` 补 `--check`（luban staging+CRLF+prune；proto 双模式 + sidecar；sync-code 行尾归一）；10 个 toolbox 文本产物统一 3 行头/指纹；`Battle.cs.input` sidecar（含 `.meta`）。
- `.workflow/config.json` 增 `codegen-check`（`toolbox all --check`）；`scripts/gate/toolchain.json`、`build-player`、`sync-and-sim-e2e`、`refresh-hash` 改走 toolbox；工具索引再生；`codegen-call-sites.json` 55 条分类守卫。
- 文档/文案面：当前命令面（说明/规范/设计/审计 + AGENTS/skills/Assets `.cs`）直调改 toolbox；设计文档 v1.2；进度卷第 35 章；历史进度/归档不改写。
- 最终证据：`wf verify` PASS 65/65；e2e summary ok=true；BuildHash 常量未变（shared=`be285f5c37e147d9`、meta=`75e0e85fe3c33cf2`）；`Battle.cs`/`SimTrigTables.cs` diff 0。

## 未决事项

- `toolbox trig` 依赖运行中的 Unity Editor（不进 `all`/门禁，无 Pipeline 时测试 skip；CI 不覆盖）。
- `doctor` 对目录型输出（luban 四个发布目录）只检查父目录，未检查目录本身；`luban --check`/`codegen-check` 仍覆盖（minor，未扩范围）。
- `codegen-call-sites.json` 行号与调用点强绑定：移动调用点必须同批重生成（守卫会红）；本轮 T7 保活改动已同步一次。
- `gen-ui-page` 对仓库外 `--table/--manifest/--contracts` 输入退化为绝对路径入指纹（默认输入全在仓内，`--check` 不受影响）。
- e2e/Unity 环境坑：本次遇到 Editor `delayCall/update` 循环挂起（`set_autotick`/前台聚焦/recompile 无效），需完整重启编辑器；`build-player` 已加 `editor_stop`/`editor_focus`/`set_autotick` 保活，仍挂起时按 memory 的 Unity Pipeline Traps 重启。
- 本批尚未提交；工作区并行线 dirty 文件（4 个 Assets 文件 + `.agents/skills/staged-delivery/*`）不属于本批，提交时剔除。

## 文件清单

- codegen：`scripts/codegen/{_codegen,toolbox,gen-build-hash,gen_proto,gen_luban,gen_sync_code,gen-ui-page,gen-ui-api,refresh-hash}.py`。
- 测试：`scripts/tests/{test_toolbox,test_codegen_rot,test_gen_path_report,test_gen_luban_check,test_gen_proto_check}.py`；更新 `test_generated_artifacts/test_refresh_hash/test_hook_reminder`；`scripts/tests/fixtures/{codegen-call-sites,codegen-rot-paths}.json`。
- 接线：`scripts/local/local-chain.py`、`scripts/hooks/shared-source-reminder.py`、`scripts/build/build-player.py`、`scripts/gate/sync-and-sim-e2e.py`、`scripts/gate/toolchain.json`、`.workflow/config.json`、`Luban/gen_lua_keys.py`。
- Assets：`Assets/LiteSim/Editor/SimTrigTableGenerator.cs`、`Assets/LiteGame/Editor/Toolchain/Actions/SimTrigCli.cs(+.meta)`、`SimTrigToolActions.cs`、10 个生成物、`Assets/LiteNet/Core/Proto/Generated/Battle.cs.input(+.meta)`、Baking/GM 提示文案。
- 文档：`Docs/设计文档/架构/生成器工具箱专项设计.md`、说明 10/11/15/02/04/05/06/07/08/09/12/14/17/工具索引、规范 02/03、`AGENTS.md`、`.agents/skills/{build-hash-sync,batch-wrapup,litegame-online-test-release}`、`Docs/施工进度/资源与工具链线.md` 第35章 + README、`Docs/审计/服务端/登记/{工具链,文档债}.md`。
- 证据：`.workflow/active/codegen-consolidation/evidence/{red.json,verify.json}`。

## 后续建议

- `ci.yml` 恢复自动触发后把 `codegen-check`（`toolbox.py all --check`）接入 CI；当前门禁在 `.workflow/config.json` 的 `wf verify` 中生效。
- 新增生成器按 `TOOLS`/`ALL_ORDER`/`path_report()`/`--check`/`signature()` 登记；移动调用点同批更新 `codegen-call-sites.json`。
- 合并/提交时只包含本批文件，剔除并行线 dirty 文件；`Battle.cs.input(.meta)` 与 `SimTrigCli.cs(.meta)` 一并提交，生成物头随源提交。
- 后续可补 `doctor` 对目录型输出的自身存在检查（当前 minor）；`toolbox trig` 在 CI 无 Editor 时保持 skip。

## 阶段流转

<!-- 阶段推进与回退记录由 workflow.py 自动追加。 -->
- 2026-10-30T19:40:30+08:00 · 初始化运行，当前阶段「提出方案」
- 2026-11-01T12:07:18+08:00 · 完成「提出方案」→ 「子代理评审方案」
- 2026-11-01T13:14:19+08:00 · 完成「子代理评审方案」→ 「用户确认验收标准」
- 2026-11-01T13:22:10+08:00 · 完成「用户确认验收标准」→ 「实施任务清单」
- 2026-11-01T13:22:52+08:00 · 完成「实施任务清单」→ 「子代理评审任务」
- 2026-11-01T13:41:03+08:00 · 完成「子代理评审任务」→ 「子代理先写测试」
- 2026-11-01T13:56:26+08:00 · 完成「子代理先写测试」→ 「子代理正式施工」
- 2026-11-01T14:42:02+08:00 · 完成「子代理正式施工」→ 「测试代码」
- 2026-11-01T17:37:44+08:00 · 完成「测试代码」→ 「主代理评审代码」
- 2026-11-01T17:37:52+08:00 · 完成「主代理评审代码」→ 「回写与收尾」
- 2026-11-01T17:38:08+08:00 · 完成「回写与收尾」→ 完成
