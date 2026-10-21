# 代码评审：脚本形态统一

> 运行 script-form-unification · 创建于 2026-10-21T18:48:37+08:00

## 变更范围

- 对照任务与方案：覆盖 T1～T7（S-A 登记与检查地基、S-C 触碰文件形态、新入口 S-B 基线）。S-B 全量、L2-D1、S-D 语言迁移不在本次范围，已在方案与非目标中声明。
- 变更文件/模块：
  - 清单/CLI：`scripts/gate/toolchain.json`、`scripts/toolchain.py`、`scripts/_toolchain_scripts.py`、`scripts/_toolchain_manifest.py`、`scripts/_toolchain_docs.py`、`scripts/_toolchain_surfaces.py`。
  - 脚本：`scripts/deploy/{gm,wg-peer,deploy-room}.ps1`、`scripts/local/local-chain.ps1`、`scripts/content/{scan_missing_guids.py,publish-candidate.ps1,serve-cdn.ps1,_local-cdn.ps1}`、`scripts/gate/doc-lint.py`、`scripts/pipeline/patch_p1_plugin_metas.py`、`scripts/README.md`。
  - 测试：`scripts/tests/test_toolchain.py`、`test_toolchain_scripts.py`、`test_toolchain_contracts.py`。
  - 文档：`Docs/工具链/工具索引.md`（生成）、`Docs/工具链/技术支持.md`、`Docs/开发导航/代码地图.md`、`Docs/项目说明/05-验证与门禁.md`、`Docs/设计文档/架构/脚本形态统一专项设计.md`、`Docs/设计文档/README.md`。
- 范围审计：本次改动仅 `scripts/`、`Docs/`、`.gitattributes`、`.workflow/`；未改 `Assets/`、`Server/`、`Luban/`、`Tests/`、`.github/`。工作区中 Assets/Server 的未提交改动来自并行线，未由本次引入。

## 正确性与边界

- **主路径**：清单新增 `deploy` 分类与六条工具；`toolchain.py check` 读取清单与注册面，再调用 `check_scripts` 做孤儿/编码/EOL 扫描；`toolchain.py run <id>` 复用清单 command。实测 `run quality.doc-lint` 成功，`run deploy.gm -- -Action bogus` 退出 2 且未发起 SSH。
- **孤儿判定**：覆盖 registered command、全局入口、`_` 前缀、`tests/`、`@oneshot`、已登记入口文本引用、CI/hook/scheduled-task surface 七类来源；repo 当前 0 error。临时夹具覆盖五类豁免与 strict 升级。
- **编码/EOL**：Python BOM、PS 非 ASCII 无 BOM、CRLF 均能报告；本次触碰文件已归一；未触碰存量仅告警，符合“按触碰归一”。
- **入口契约**：`local-chain` 未知命令、`gm`/`wg-peer` 缺参均返回 2；deploy 三件 `Get-Help` 可读；`scan_missing_guids.py --help` 可用；`doc-lint.py` 保持 argparse 退出码。
- **边界/错误路径**：`gm.ps1` 去掉 Mandatory/ValidateSet 后显式校验 Action；`wg-peer.ps1` 在解析 SSH/Endpoint 前校验 Name；`local-chain` 的 Meta 构建失败由透传子进程码归一为 1；`_local-cdn.ps1` 加 BOM 后实测 dot-source 返回 Directory/Port。
- **回归风险**：输出前缀/帮助块/退出码变更可能影响人工脚本消费；已 grep 调用面，deploy/local 均为人工命令，Doc lane 不解析 doc-lint 文本；L1 1801 通过。

## 测试充分性

- **是否覆盖验收与失败路径**：是。红测 26 个断言失败覆盖缺清单/缺检查器/退出码/缺 `.gitattributes`；最终 44/44 Python 契约测试通过。检查器正反例包含未登记、五类豁免、BOM、非 ASCII PS、CRLF、干净文件、真实仓库。
- **是否存在迎合实现的测试**：未发现。孤儿/编码测试用临时夹具独立于实现；契约测试通过 `Get-Help`/`--help`/退出码验证行为，不读取实现内部状态。`test_repo_has_no_script_form_errors` 只断言无 error，不掩盖存量 warning。
- **未覆盖面**：未真实执行 deploy 主体（安全约束）；未跑 Unity/EditMode（本次不触碰 Unity 面）；strict 全仓绿未作为验收（存量 10 条告警按触碰归一）。

## 通用性与扩展性

- **根因与复用边界**：形态差异来自缺少登记判据、默认语言/文件规则与检查器；本次用“清单 command + 文件标记 + 静态扫描”解决共性，不逐脚本特判。
- **扩展点与接入方式**：新增脚本只需选角色（全局/域/叶子/内部/测试/一次性）、按命名与头部规则落盘、补清单或加 `_`/`@oneshot`；新增分类只改清单 `categories`，Hub/索引自动跟随。
- **硬编码、特判、魔法值**：检查器不维护第二份脚本白名单；`GLOBAL_ENTRY` 是唯一显式入口特判，hook/CI 引用从 manifest surface 读取。deploy 的 Action 列表在脚本内校验，与 help 文本同处，未引入跨文件魔法值。
- **重复逻辑与过度抽象**：未新增 `_common` 抽象（归 S-C2）；本次只保留必要的检查器模块。重复的根推导仍是存量债，已在未决事项登记。
- **相关场景复用与回归影响**：`toolchain.py check` 成为所有脚本的共同守卫；`scripts/README` 的 canonical 表为后续工具提供模板；不影响 Unity Hub 的分类数据驱动行为。
- **命名、结构与复杂度**：`_toolchain_scripts.py` 约 120 行，职责单一；`toolchain.py` 仅增加一个调用点；脚本改动集中在参数/输出/帮助，未拆业务逻辑。

## 安全与性能

- **安全**：检查器只读文件，不执行脚本/Unity/SSH；本次未读取密钥、未打印 `.conf`/私钥；`gm`/`wg-peer` 缺参路径在密钥解析前退出；云侧验证仅 `Get-Help`/usage。`toolchain.py run` 对 manifest 标为 destructive 的 deploy 工具不额外弹确认，这是既有 CLI 行为；用户在文档中被告知破坏性，后续 L2-D 可补统一确认。
- **性能与资源**：检查器一次遍历 `scripts/`，文件读取量小；`check` 实测约 0.2s；Python 契约测试约 5s；未引入运行时开销或新静态可变状态。

## 发现的问题与处理

| ID | 严重度 | 位置 | 问题 | 处理 | 状态 |
|---|---|---|---|---|---|
| C-1 | 次要 | `scripts/_toolchain_scripts.py` `_normalize` | 原实现 `lstrip("./")` 会把 `../scripts/foo.py` 归一成 `scripts/foo.py`，可能把越界路径误判为已登记 | 改为只剥离显式 `./` 前缀；测试与 repo check 复测通过 | 已修复 |
| C-2 | 次要 | `scripts/_toolchain_scripts.py` `_is_exempt` | 原用子串判断“叶子脚本被登记入口引用”，文件名短时可能误豁免 | 改为带词边界的 basename 正则匹配；14/14 检查器测试通过 | 已修复 |
| C-3 | 信息 | `scripts/toolchain.py run` | CLI 对 manifest `destructive=true` 不额外确认；新增 deploy 工具因此可被直接执行 | 本次文档明示破坏性、验证不执行主体；统一确认归 L2-D/CLI 后续批次 | 遗留（非本次阻塞） |
| C-4 | 信息 | 存量脚本 | `check --strict` 仍因 10 条未触碰旧脚本的编码/EOL 告警失败 | 按设计“触碰即归一”保留告警，未做全仓格式化；后续触碰批清理 | 遗留（非本次阻塞） |

无阻塞问题。

## 复测记录

- 修复 C-1/C-2 后：
  - `python -m unittest scripts.tests.test_toolchain_scripts -v` → 14/14 OK
  - `python scripts/toolchain.py check` → OK（0 error / 10 存量 warning）
  - `python .agents/skills/staged-delivery/scripts/workflow.py --run script-form-unification verify` → PASS（L1 1801、doc 0/0、toolchain-tests 44/44、check、docs current）
- 附加：BuildHash `--check-shared` OK；7 个触碰 PS 文件 AST 解析 OK；`_local-cdn.ps1` 实际加载返回 `LocalCDN`/`18090`；`run deploy.gm -- -Action bogus` 退出 2 且无 SSH。
- 验证证据：`evidence/verify.json`。

## 评审结论

- 结论: PASS
- 阻塞问题: 0
- 通用性: PASS
- 扩展性: PASS
- 评审人: Qoder（agent）
- 评审时间: 2026-10-21
