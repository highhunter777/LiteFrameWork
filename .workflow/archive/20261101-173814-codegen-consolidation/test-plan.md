# 测试计划：生成器收口（toolbox 单入口 + 腐烂路径修复 + 门禁）

> 运行 codegen-consolidation · 创建于 2026-10-30T19:40:30+08:00

## 测试策略

- 测试层级：
  - **单元/契约**（`scripts/tests`，`python -m unittest`）：路径单源、`path_report`、`--check` 语义、头/指纹、toolbox 转发/顺序/短路、doctor 缺失判定、deprecated、守卫分类、rot fixture。全部纯 Python，秒级。
  - **集成**（临时目录/真实 protoc）：luban 暂存比对、proto 重生成/戳、sync-code 行尾归一、`all` 全序、`gen.bat` 等价。protoc 用本机 NuGet 3.36.1（仓库不含）。
  - **Unity 条件集成**（`toolbox trig`）：需要运行中的 Unity Editor/Pipeline；无则 `skipTest` 并记录残余风险。
  - **门禁**：`.workflow/config.json` 的 `codegen-check`（`toolbox all --check`）；改 `[StateLayer]`/`.proto` 不重生成 → 红。
- 关键风险与覆盖重点：
  - 路径腐烂复发 → `path_report` + doctor + hook 源集联锁 + rot fixture。
  - `--check` 假绿/假红 → CRLF 归一、sidecar 缺失判红、luban `reorganize_bin_stage`/prune、sync-code 行尾。
  - 指纹噪音 → sync-code 解析字段模型、LuaKeys 注册表集。
  - 直调逃检 → 逐字面量分类（含 `_run`/`run_gate`/`Path` join/反斜杠）。
  - 门禁自阻塞 → 先本地跑通 `doctor`/`all --check`，再接 `.workflow/config.json`。
- 先写测试后施工：测试子代理只写测试与 fixture；施工子代理另开会话。测试必须因预期行为缺失而失败（断言/期望退出码），不得因导入/环境错误失败。

## 用例清单

| 用例 | 层级 | 关联任务/验收 | 输入或步骤 | 预期结果 |
|---|---|---|---|---|
| UT-1 | 单元 | T1/C1 | 逐个导入生成器，调 `path_report()` | 返回声明输入/输出；路径与常量一致 |
| UT-2 | 单元 | T1/C2 | `gen_proto` 用 `PROTO_REL` 推导目录并 `makedirs`；`gen_sync_code` OUT_FILE 正确 | 产出落 `LiteNet/Core/Proto/Generated`、`LiteSim/Diagnostics/Generated` |
| UT-3 | 单元 | T1/C2 | `refresh_hash`/`local_chain` 在 mock root 下读 `shared_out_file(root)` | 注入 root 时读对应文件；不再硬编码 |
| UT-4 | 单元 | T1/C1 | hook 对 `SHARED_TARGETS` 10 目录各构造事件；文案断言 toolbox | 全部触发红线1；文案含 `toolbox.py build-hash` |
| UT-5 | 单元 | T2/C6 | `luban --check` 在临时 source/target：CRLF 目标、多余 `.bytes`（prune）、`reorganize_bin_stage` | 归一/重组后判新鲜；多余产物判 stale；不删除 |
| UT-6 | 单元 | T2/C6/C4 | `proto --check`：有 protoc 重生成；无 protoc 读 sidecar；CRLF/LF `.proto`；sidecar 缺失/不匹配；`Battle.cs` 缺失 | 有 protoc 归一比对；无 protoc 摘要一致；缺失/不匹配/产物缺失 → 非零 |
| UT-7 | 单元 | T2/C6 | `sync-code --check` 在 LF/CRLF 目标 | 行尾归一后判新鲜；不重写 |
| UT-8 | 单元 | T3/C9 | 10 个产物的签名区块；重复生成 | `toolbox/<tool>` + `input` + `regen`，`>` 收口，≤3 行、无时间戳；逐字节幂等 |
| UT-9 | 单元 | T3/C9 | sync-code 改非 `[StateLayer]` 行；LuaKeys 改非注册表 xlsx | 头指纹不变；生成物不变 |
| UT-10 | 单元 | T4/C3 | `toolbox all` 顺序/短路/缺 `--protoc`；`normalize_exit` | 严格序；失败中止；缺参 fail-fast；退出码归一 |
| UT-11 | 单元 | T4/C1 | doctor 对缺失/旧路径、tracked/untracked 输入、MapTarget 内联路径 | 5 条具体路径红；未入库输入只提示；输出父目录红；读修复后绿 |
| UT-12 | 单元 | T4/C1 | rot fixture 回放 8 条历史腐烂 | 旧声明路径红、实际路径绿 |
| UT-13 | 单元 | T5/C8 | 7 个生成器直调/经 toolbox；ui-page/ui-api TOOL 名；索引/提示文案 | 直调 stderr 有 `[deprecated]`；toolbox 无；前缀无双 `codegen/`；提示无直调 |
| UT-14 | 集成（Unity 条件） | T6/C5 | `toolbox trig --check`；临时改/删产物；`git diff --ignore-cr-at-eol` | 干净时 0；stale 非零；恢复后 0；写盘经 ToolWriteScope |
| UT-15 | 单元 | T8/C8 | 分类守卫：`subprocess`、`_run`、`run_gate`、`Path` join、反斜杠、config/toolchain/bat | 0 未分类/0 非法 invocation；`contract-test`/`compat` 例外 |
| UT-16 | 单元 | T7/C10 | `Luban/gen.bat` vs `toolbox.py luban` | 同脚本、同退出码；deprecated 提示不影响 rc |
| UT-17 | 契约 | T7/C12 | `toolchain.py docs --check`、`toolchain.py check`、`test_toolchain` | 工具索引已再生、三项绿 |
| UT-18 | 门禁 | T7/C7 | 临时改 `[StateLayer]` 不重生成 → `toolbox all --check` | 非零；恢复后零 |
| UT-19 | 文档 | T9/C11 | 当前 Docs（排除归档）/AGENTS/skills/Assets `.cs` grep；`doc-lint` | 0 直调；doc-lint 绿 |
| IT-1 | 集成 | T4/C3 | `toolbox all --protoc <nuget>` 干净工作区 | 全序跑完；`git status --porcelain -- <paths>` 无未预期变更 |
| IT-2 | 集成 | T2/C4 | 首次生成 `Battle.cs.input` + `.meta`，`toolbox all --check` | sidecar 存在、首跑绿 |

## 预期失败证据

- 命令：`python .agents/skills/staged-delivery/scripts/workflow.py --run codegen-consolidation verify --red`
- 预期失败（实现前）：
  - `toolbox-tests`：`all`/`doctor`/`trig` 未实现 → 子命令未知/退出码不符/输出缺失（断言失败）。
  - `gen-path-report-tests`：生成器无 `path_report()` → AttributeError/断言失败（模块可导入，不是导入错误）。
  - `codegen-rot-tests`：doctor 缺失判定/夹具未实现 → 断言失败。
  - `gen-luban-check-tests` / `gen-proto-check-tests`：`--check` 未实现 → 退出码/输出与期望不符。
  - `test_generated_artifacts`（手工跑）：旧署名与新的 `toolbox/<tool>` + `input` 契约不符 → 断言失败。
  - `test_refresh_hash` / `test_hook_reminder`（手工跑）：仍读旧路径/旧文案 → 断言失败。
- 为什么这是正确的失败：测试在导入阶段通过（模块存在），失败发生在行为断言/子进程退出码/产物签名上；不是缺依赖、拼写或环境错误。Unity 条件用例在无 Pipeline 时 skip，不计入红证据。
- 记录：`wf verify --red` 写 `evidence/red.json`；主代理核验失败尾部并确认“预期原因”。

## 运行方式

- 全量测试：`python -m unittest discover -s scripts/tests -p "test_*.py" -v`（`toolchain-tests` 同源）。
- 仅新增测试：`python -m unittest scripts.tests.test_toolbox scripts.tests.test_codegen_rot scripts.tests.test_gen_path_report scripts.tests.test_gen_luban_check scripts.tests.test_gen_proto_check -v`。
- 先行失败证据：`wf verify --red`（上述命令 + 既有 red_commands；Unity 条件用例 skip）。
- 门禁/回归：`python scripts/codegen/toolbox.py all --check`；`python scripts/toolchain.py docs --check`；`python scripts/gate/doc-lint.py`；`python scripts/gate/test.py --suite L1 --profile PullRequest`。
- protoc：`C:/Users/hunter/.nuget/packages/google.protobuf.tools/3.36.1/tools/windows_x64/protoc.exe`（仓库不含；进度卷记录）。

## 豁免与说明

- T7/T9/T10 为主代理任务（门禁/config/文档），不写生产测试；由 `toolchain-docs-check`/`doc-lint`/grep/命令退出码验证。
- T6 的 `trig` 需要 Unity Editor；无 Pipeline 时 skip，残余风险在 build-log 记录（不阻塞纯代码测试）。
- `.meta`（sidecar/SimTrigCli）由主代理走 unity-pipeline；测试只断言文件存在/`git ls-files`，不直接创建序列化资产。
- 裸脚本名（argparse `usage`/`prog`）不在直调 grep 面，保留为程序名；直调命令提示由 `[deprecated]` 覆盖。
