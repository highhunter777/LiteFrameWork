# scripts/

按职责分类的工程脚本（可从任意 CWD 运行；CI 入口见 `.github/workflows/ci.yml`）。工具清单的机器单源是 [`gate/toolchain.json`](gate/toolchain.json)，生成索引是 [`Docs/工具链/工具索引.md`](../Docs/工具链/工具索引.md)；本文件只保留目录职责、角色规则与 canonical 入口。

## 目录与职责

| 目录 | 职责 | 内容 |
|---|---|---|
| 根 | 开发工具链平台统一入口 | `toolchain.py`：`list / check / docs / open / run / generate / verify / build / doctor`；`check` 同时校验清单、注册面、孤儿脚本、编码与 EOL、域形态规则（`_toolchain_domains.py`）；编辑器侧入口 `LiteGame/工具链` |
| `gate/` | 质量门禁（CI 调用面） | `test.py`（L1/L3 dotnet 车道、Doc 车道含生成索引新鲜度）、`doc-lint.py`（文档纪律；canonical `quality.doc-lint`）、`l0-dep-scan.py`、`l2-unity-gate.py`、`nightly-gate.py`、`restore-check.py`、`install-nightly-task.py`、`toolchain.json` |
| `deploy/` | 云上发布与运维（Windows/SSH） | `bootstrap-cloud-python.py`（云上 embedded Python + runner 上传）、`deploy-room.py`（房间服发布）、`gm.py`（GM 端点；动作/字段由 `Server/RoomServer/gm-contract.json` 派生）、`wg-peer.py`（测试者隧道）、`_ssh_host.py`（SSH 选路）、`_remote.py`（远程调用内部库）；payload 在 [`deploy/remote_runner.py`](../deploy/remote_runner.py) |
| `content/` | 候选发布与本地 CDN | `gen-candidate.py`（签名信封生成）、`publish-candidate.py`（CDN 布局 + 签名一条龙）、`serve-cdn.py`（本地 CDN 起服）、`scan-missing-guids.py`（缺失 GUID 扫描；canonical `quality.scan-missing-guids`）、`local-cdn.json` + `_local_cdn.py`（配置数据 + 内部加载器） |
| `build/` | Player 构建与冒烟 | `build-player.py`（内含 buildHash `--check` 出包门禁，不可跳过）、`player-smoke.py`（`--player-arg` 透传部署参数） |
| `local/` | 本地联测一键链（密钥机器私有） | `local-chain.py`：`init` 生成 `deploy/.env` + 编辑器 `deploy.args`；`meta`/`room`/`up` 按密钥起 Meta/RoomServer；`status` 一屏总览；`keys` 脱敏汇总。canonical `env.local-chain`；图形界面为 Hub 的「本地联测」页 |
| `codegen/` | 派生物生成（生成器工具箱） | 统一入口 `toolbox.py`（build-hash / proto / luban / sync-code / refresh-hash）+ 共享单源 `_codegen.py`（五工具全 Python）；也可经 `python scripts/toolchain.py generate <tool>`。工具形态与操作权威见 [生成器工具箱专项设计](../Docs/设计文档/架构/生成器工具箱专项设计.md) |
| `pipeline/` | Unity Pipeline 一次性补丁 | `patch_p1_plugin_metas.py`（文件头 `@oneshot`；不进入日常流程） |
| `tests/` | 脚本层契约测试 | `test_toolchain*.py`（清单、检查器、生成索引、新入口契约） |

## 角色与入口

**canonical 入口 = 清单 `command`**。文档、CI、Agent 指令与计划任务只写这一种；单脚本直调保留给既有消费面，但不在操作文档里作为首选。

| 角色 | 判据/命名 | 登记 |
|---|---|---|
| 全局入口 | `toolchain.py` | CLI 自带；孤儿检查显式放行 |
| 域入口 | `codegen/toolbox.py`、`gate/nightly-gate.py`、`local/local-chain.py` | 必登记 |
| 叶子工具 | 动词开头 kebab-case（`deploy-room.py`、`doc-lint.py`） | 用户面/CI 面必登记；仅被已登记入口注册的可豁免 |
| 内部库 | `_` 前缀（`_codegen.py`、`_ssh_host.py`、`_local_cdn.py`） | 不登记；不得含独立入口 |
| 测试 | `tests/test_*.py` | 不登记 |
| 一次性运维 | 文件头前 20 行 `@oneshot` | 不登记；`toolchain.py check` 仅告警 |

新增脚本的形态：

- **语言**：`scripts/` 全 **Python**（3.10+），零 `.ps1`；新增脚本一律 Python，不再引入 PowerShell 例外。作者面规则（角色分层/入口/调用契约/文件形态）见[开发工具链平台专项设计 §9](../Docs/设计文档/架构/开发工具链平台专项设计.md)；迁移批次证据见[归档记录](../Docs/归档记录/2026-10-21/脚本形态统一专项设计.md) §6。
- **编码/行尾**：Python UTF-8 无 BOM；迁移期 PS 存量含中文在被触碰时转 UTF-8 BOM；`scripts/**` 统一 LF（`.gitattributes`）。
- **头注释**：入口四段式（用途/入口/契约/消费点）；生成器沿用五段式；内部库两段式。
- **退出码**：本地入口只用 0/1/2（0 成功、1 预期失败、2 用法错误）；域入口用 `_common.normalize_exit()` 把子进程非 0/1 码归一为 1，原始码写 stderr。
- **帮助**：所有入口支持原生 `--help`；未知参数/子命令 exit 2，不静默吞掉。
- **输出**：状态/错误行统一走 `info/warn/fail`（`[域/工具]` 前缀）；help/usage、JSON payload、缩进明细保持原样。
- **检查**：新增/改名后跑 `python scripts/toolchain.py check`；用户面入口补清单，内部库加 `_`，一次性脚本加 `@oneshot`。

迁移期 `.ps1` 角色表的现行文件名保留（如 `nightly-gate.py`）；所属 P 批切换后目标名为 `.py`，旧名只允许 `@ps-shim` 兼容壳。迁移完成判据是 `scripts/**/*.ps1` 归零，而不是“新脚本默认 Python”。

**CLI `run` 契约**：

- 清单 `args` 是参数 schema 的单源；`python scripts/toolchain.py run <id> --help` 渲染参数与枚举，不启动子进程。
- 工具参数一律放在 `--` 之后：`python scripts/toolchain.py run <id> [--dry-run] -- <tool args>`；`run --dry-run` 只对 `safety.dryRun=supported` 开放，未声明时明确拒绝（工具自身开关仍可直传）。
- 缺必填/类型/枚举错误在 CLI 层退出 2，不落到子进程；成功与否以 `[toolchain] <id> exit=<code>` 与子进程退出码为准。

## 新登记入口（canonical）

| 工具 ID | canonical 命令 | 说明 |
|---|---|---|
| `deploy.cloud-python` | `python scripts/toolchain.py run deploy.cloud-python -- --dry-run` | 校验/安装云上 embedded Python；部署前置（`--what-if` 兼容别名） |
| `deploy.room` | `python scripts/toolchain.py run deploy.room` | 发布云上房间服；破坏性，执行前确认 |
| `deploy.gm` | `python scripts/toolchain.py run deploy.gm -- --action <动作>` | 云端 GM 动作；破坏性 |
| `deploy.wg-peer` | `python scripts/toolchain.py run deploy.wg-peer -- --name <测试者>` | 开通测试者隧道；破坏性 |
| `env.local-chain` | `python scripts/toolchain.py run env.local-chain -- <init|meta|room|up|status|keys>` | 本地联测一键链 |
| `quality.doc-lint` | `python scripts/toolchain.py run quality.doc-lint` | 文档纪律扫描（Doc 车道同源） |
| `quality.scan-missing-guids` | `python scripts/toolchain.py run quality.scan-missing-guids` | 缺失 GUID 扫描 |

## 约定

- 工程根统一由 `scripts/_common.py` 的 `repo_root()` 推导；`_codegen`/`_toolchain_manifest` 委托它，叶子脚本不得复制推导片段。各目录的 `sys.path.insert(...)` 只是导入引导，不是根推导；显式 `--project-path` 可覆盖。
- 含中文的 `.ps1` 无 BOM 会被 PS 5.1 按 GBK 误解析——新脚本一律 ASCII-only；存量含中文脚本由 `pwsh` 启动，触碰时转 UTF-8 BOM 或去中文。
- 本地 CDN 端到端：`content/publish-candidate.py` 发布 → `content/serve-cdn.py` 起服 → Player 带 `-content.cdnUrl` 启动（目录/地址见 `content/local-cdn.json`）。
- 施工记录里引用的历史脚本路径是该次执行时的现状证据，不随本目录结构调整改写；活性入口以本 README、清单与生成索引为准。
