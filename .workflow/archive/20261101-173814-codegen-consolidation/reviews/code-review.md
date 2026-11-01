# 代码评审（主代理）：生成器收口（toolbox 单入口 + 腐烂路径修复 + 门禁）

> 运行 codegen-consolidation · 创建于 2026-10-30T19:40:30+08:00
>
> 代码由施工子代理撰写；本文件由主代理基于实际 diff 与验证证据评审落盘。

## 评审输入

- 代码基线：HEAD `48ccda61`；工作区 tracked diff 101 文件（+1877/−442），其中本批约 80 文件（4 个并行线改动：`IntentVectorLimit.cs`/`NewInputIntentSource.cs`/`WeaponAimConstraint.cs`/`DisciplineScanner.cs` 与 `.agents/skills/staged-delivery/*` 不计入本批评审）；未跟踪本批件：`.workflow/active/codegen-consolidation/`、`scripts/tests/{test_toolbox,test_codegen_rot,test_gen_*}.py`、`scripts/tests/fixtures/`、`Assets/LiteNet/Core/Proto/Generated/Battle.cs.input(.meta)`、`Assets/LiteGame/Editor/Toolchain/Actions/SimTrigCli.cs(.meta)`。
- 评审角色：主代理（代码作者为施工子代理 T1–T9）
- 评审范围：`scripts/codegen/**`、`scripts/tests/**`、`scripts/{build,gate,hooks,local}` 的直调改道、`Luban/gen_lua_keys.py`、`Assets` 的 SimTrig/生成物/`.meta`、`.workflow/config.json`、`scripts/gate/toolchain.json`、文档/文案面；不评审并行线 4 个 Assets 改动与 staged-delivery skill 自身改动。

## 变更范围

- 对照任务与方案：T1 路径单源/path_report/hook；T2 三个 `--check` + sidecar；T3 10 产物头/指纹；T4 all/doctor；T5 run_tool/deprecated；T6 trig/ToolWriteScope；T7 主代理门禁/config/索引；T8 直调改道/分类守卫；T9 文档/文案面；T10 red_commands。范围与 AC 一致。
- 变更文件/模块：`toolbox.py`（all/doctor/trig/转发）、`_codegen.py`（指纹/签名/加载/deprecated）、7 个生成器、`refresh-hash.py`/`local-chain.py`/hook、`build-player.py`/`sync-and-sim-e2e.py`/`toolchain.json`/`.workflow/config.json`、`gen_lua_keys.py`、`SimTrigTableGenerator.cs`/`SimTrigCli.cs`/`SimTrigToolActions.cs`、10 个生成物、5 个新测试 + 3 个既有测试更新、`codegen-call-sites.json`/`codegen-rot-paths.json`、Docs/AGENTS/skills/进度卷/审计登记表。

## 正确性与边界

- 主路径：`toolbox all --check` 真实 6/6（luban/ui-page/ui-api/sync-code/proto/build-hash）；`doctor` 8 provider + 55 call sites；`trig --check` live Unity 0；`wf verify --red` 先失败后实现全绿；最终 `wf verify` 65/65 命令 exit 0（codegen-check 2.3s、sync-and-sim-e2e 152s，e2e summary ok=true）。
- 边界与错误路径：缺 `--protoc` 在开跑前 exit 2；未知 tool/参数 exit 2；`all` 首个失败中止且归一 1；`--check` 不写仓库（luban 临时 staging、proto 临时目录、sync-code 只读、BuildHash 只读）；proto sidecar 缺失/不匹配/`Battle.cs` 缺失均非零；luban 文本行尾归一、`.bytes` 逐字节、prune 仅判 `.bytes`；sync-code/trig 行尾归一；`SimTrigTables.cs` 不改头；`Battle.cs` 不改本体。
- 回归风险：BuildHash 常量未变（shared=`be285f5c37e147d9`、meta=`75e0e85fe3c33cf2`）；`Battle.cs`/`SimTrigTables.cs` diff 0；`gen-build-hash --check`/`toolchain docs --check`/doc-lint 绿；并行线 Assets 改动未触碰。

## 测试充分性

- 测试真正覆盖验收与失败路径：`test_toolbox`（26，含 fake all 顺序/短路、doctor、deprecated、live trig、call-site 守卫）、`test_gen_path_report`（14，path_report 单一来源/tracked 分层）、`test_gen_luban_check`（8，CRLF/prune/只读）、`test_gen_proto_check`（10，戳/CRLF/缺失/只读/仓库侧）、`test_generated_artifacts`（11，10 产物签名/指纹/幂等）、`test_codegen_rot`（7，8 条历史腐烂回放 + 真实 doctor）、`test_refresh_hash`（7，mock root + `shared_out_file`）、`test_hook_reminder`（10，10 源集联锁 + toolbox 文案）。
- 未发现迎合实现的测试：测试先于实现落盘，红证据 `wf verify --red` 5 个新命令按预期失败，施工后转绿；未删除/弱化既有用例；`test_script_contracts_sb` 保持 `contract-test` 例外。
- 残余：trig live 用例依赖 Editor（无 Pipeline skip）；UT-16/17/18/19 由主代理命令/门禁验证，已在 test-plan 豁免说明。

## 通用性与扩展性

- 根因与复用边界：路径腐烂由各工具 `path_report()` + `shared_out_file(root)` + doctor 统一；`--check`/头/指纹/模块加载收口到 `_codegen`；`all` 顺序单源 `ALL_ORDER`；直调守卫用 `(file,line,literal)` 分类清单而非特判。
- 扩展点与接入方式：新增生成器 = `TOOLS` 一行 + `ALL_ORDER` 位置 + `--check` + `path_report()`；签名/指纹复用 `_codegen.signature/input_digest`；新增调用点同批更新 `codegen-call-sites.json`。
- 硬编码/特判/魔法值：无新增硬编码路径（`shared_out_file`/常量派生）；`protoc` 路径只出现在测试/验收命令（仓库不含 protoc，进度卷记录）；`LOCAL_CHAIN_REL`/Unity declarers 是稳定的登记面。
- 重复逻辑与过度抽象：头/指纹/加载三处共享实现；未引入无消费者的抽象；`Generate()` 保留为兼容方法，不作为 CLI 写入口。
- 相关场景复用与回归影响：UI API/BuildHash/sync-code/LuaKeys 全部走同一签名；`ui-api --check` 含 MD 生成区；`luban --check` 覆盖四类发布目标；`trig` 不进 all/门禁，边界清晰。
- 命名、结构与复杂度：`toolbox.py` 聚合 all/doctor/trig/转发，620 行但职责按块注释分区；`_codegen` 共享面小；生成器各自 `main()` + `run_tool` 收口。

## 安全与性能

- 安全：所有外部命令用参数列表（无 shell 拼接）；`toolbox trig` 的 eval 代码为静态常量；`doctor` 只读 `git ls-files`/`check-ignore`；无密钥/令牌；未上传任何外部内容；`Battle.cs.input`/`.meta` 无敏感信息。
- 性能与资源：`all --check` 的 luban 步为最重（staging + Luban + LuaKeys），900s 超时内；`doctor` 对少量缺失路径逐条 `git check-ignore`；`trig` 300s eval 超时；`all` 不并行，避免共享源集/生成物竞争。

## 发现的问题与处理

| ID | 严重度 | 位置 | 问题 | 处理 | 状态 |
|---|---|---|---|---|---|
| C-1 | major | `Docs/施工进度/**`、`Docs/设计文档/测试与性能/调试与开发工具链演进路线图.md` | 批量替换把历史进度卷（承诺不改写原批次记录）的直调命令改写，并把一个 `.py` 代码链接目标改成带空格的 `toolbox.py build-hash` | 历史进度卷恢复 HEAD，仅保留本批第 35 章；路线图链接改回 `toolbox.py` 文件链接；重跑 doc-lint/grep | 已修复 |
| C-2 | minor | `scripts/codegen/toolbox.py` doctor | 目录型输出（`gen_luban` 的四个发布目录）只检查父目录，未检查目录本身；`luban --check`/`codegen-check` 仍覆盖 | 记录为残余；未改（避免扩大范围） | 接受 |
| C-3 | minor | `gen-ui-page.py` | 仓库外 `--table/--manifest/--contracts` 输入用绝对路径入指纹（不可跨机复用）；默认输入全在仓内 | 记录为残余 | 接受 |
| C-4 | minor | `codegen-call-sites.json` | 行号与调用点强绑定；移动调用点需同批重生成 | 守卫 `test_fixture_has_no_stale_entries` 会红；T7 e2e 保活改动导致两处行号漂移，已同批重生成 fixture，full test_toolbox 26 tests OK | 已处理 |
| C-5 | minor | `SimTrigTables.cs` | `trig` 首次把工作区 CRLF 写成 LF，git blob/归一文本不变，`git diff` 0 | 行尾归一与 C5 判据；`git update-index --refresh` 清 stat | 接受 |
| C-6 | minor | `SimTrigTableGenerator.cs` | `Generate()` 保留为无调用兼容方法 | 不作为 CLI 写入口；`SimTrigCli` 走 `ToolWriteScope` | 接受 |
| C-7 | minor | `gen_proto.py` | 缺 `battle.proto` 时走通用 FAILED（非专门 STALE 行） | 非零判定正确；记录为提示残余 | 接受 |
| C-8 | minor | `gen-ui-api.py` | `header()` 模块级缓存（同进程多次不同 spec 不会刷新） | 生成器单进程单 spec；`--check`/生成各一次；记录 | 接受 |
| C-9 | major | `scripts/gate/sync-and-sim-e2e.py` / `scripts/build/build-player.py` | 最终 `wf verify` 的 e2e 步失败：PlayMode 测试后 Editor 留在 Play，build-player 的 bundle 构建只返回 `starting` 并重试 20 分钟 | e2e build-player 前 `editor_stop`，build-player bundle 构建前 `editor_focus`（最小化/后台编辑器会让异步 bundle job 停在 starting）；清空积压 delayCall 后由用户重启编辑器窗口，delayCall 探针恢复 true；e2e build-player 前 editor_stop、bundle 前 editor_focus+set_autotick；e2e 单跑 summary ok=true（Player PASSED / smoke OK） | 已修复（e2e 通过） |

无 blocker；C-1 已修复。C-2–C-8 为 minor/残余，未改变 AC 判定。

## 复测记录

- 评审轮次: 第 1 轮
- 修复后执行的命令与结果：C-1 修复后 `python scripts/gate/doc-lint.py` → 0 违规/0 warning；`python scripts/toolchain.py docs --check` → 0；当前命令面 grep（说明/规范/设计/审计 + AGENTS/skills/Assets `.cs`）0；历史进度/归档不改写。最终 `wf verify` → 65/65 exit 0（含 codegen-check 2304ms、sync-and-sim-e2e 152345ms，证据 `evidence/verify.json` ok=true）。
- 复评执行与复评范围（主代理）：复核 C-1/C-4/C-9 修复、`toolbox all --check`、`test_toolbox` 26、`doctor`、`trig --check`、`test_generated_artifacts`、`test_gen_*`、`test_hook_reminder`、`test_refresh_hash`、`toolchain docs --check`、doc-lint；最终全量 `wf verify` 65/65；未触并行线。
- 验证证据：evidence/verify.json（ok=true，65/65 命令 exit 0；codegen-check 2304ms、sync-and-sim-e2e 152345ms）。

## 评审结论

- 评审方式: 主代理
- 结论: PASS
- 阻塞问题: 0
- 通用性: PASS
- 扩展性: PASS
- 评审人: 主代理（Qoder）
- 评审时间: 2026-11-01
