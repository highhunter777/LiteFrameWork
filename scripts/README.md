# scripts/

按职责分类的工程脚本（可从任意 CWD 运行；CI 入口见 `.github/workflows/ci.yml`）。工具清单的机器单源是 [`gate/toolchain.json`](gate/toolchain.json)，生成索引是 [`Docs/工具链/工具索引.md`](../Docs/工具链/工具索引.md)；本文件只保留目录职责、角色规则与 canonical 入口。

## 目录与职责

| 目录 | 职责 | 内容 |
|---|---|---|
| 根 | 开发工具链平台统一入口 | `toolchain.py`：`list / check / docs / open / run / generate / verify / build / doctor`；`check` 同时校验清单、注册面、孤儿脚本、编码与 EOL；编辑器侧入口 `LiteGame/工具链` |
| `gate/` | 质量门禁（CI 调用面） | `test.ps1`（L1/L3 dotnet 车道、Doc 车道含生成索引新鲜度）、`doc-lint.py`（文档纪律；canonical `quality.doc-lint`）、`l0-dep-scan.ps1`、`l2-unity-gate.ps1`、`nightly-gate.ps1`、`restore-check.ps1`、`install-nightly-task.ps1`、`toolchain.json` |
| `deploy/` | 云上发布与运维（Windows/SSH） | `deploy-room.ps1`（房间服发布）、`gm.ps1`（GM 端点）、`wg-peer.ps1`（测试者隧道）、`_ssh-host.ps1`（SSH 选路内部库） |
| `content/` | 候选发布与本地 CDN | `gen-candidate.ps1`（签名信封生成）、`publish-candidate.ps1`（CDN 布局 + 签名一条龙）、`serve-cdn.ps1`（本地 CDN 起服）、`scan_missing_guids.py`（缺失 GUID 扫描；canonical `quality.scan-missing-guids`）、`local-cdn.json` + `_local-cdn.ps1`（配置数据 + 内部加载器） |
| `build/` | Player 构建与冒烟 | `build-player.ps1`（内含 buildHash `--check` 出包门禁，不可跳过）、`player-smoke.ps1`（`-PlayerArgs` 透传部署参数） |
| `local/` | 本地联测一键链（密钥机器私有） | `local-chain.ps1`：`init` 生成 `deploy/.env` + 编辑器 `deploy.args`；`meta`/`room`/`up` 按密钥起 Meta/RoomServer；`status` 一屏总览；`keys` 脱敏汇总。canonical `env.local-chain`；图形界面为 Hub 的「本地联测」页 |
| `codegen/` | 派生物生成（生成器工具箱） | 统一入口 `toolbox.py`（build-hash / proto / luban / sync-code / refresh-hash）+ 共享单源 `_codegen.py`（五工具全 Python）；也可经 `python scripts/toolchain.py generate <tool>`。工具形态与操作权威见 [生成器工具箱专项设计](../Docs/设计文档/架构/生成器工具箱专项设计.md) |
| `pipeline/` | Unity Pipeline 一次性补丁 | `patch_p1_plugin_metas.py`（文件头 `@oneshot`；不进入日常流程） |
| `tests/` | 脚本层契约测试 | `test_toolchain*.py`（清单、检查器、生成索引、新入口契约） |

## 角色与入口

**canonical 入口 = 清单 `command`**。文档、CI、Agent 指令与计划任务只写这一种；单脚本直调保留给既有消费面，但不在操作文档里作为首选。

| 角色 | 判据/命名 | 登记 |
|---|---|---|
| 全局入口 | `toolchain.py` | CLI 自带；孤儿检查显式放行 |
| 域入口 | `codegen/toolbox.py`、`gate/nightly-gate.ps1`、`local/local-chain.ps1` | 必登记 |
| 叶子工具 | 动词开头 kebab-case（`deploy-room.ps1`、`doc-lint.py`） | 用户面/CI 面必登记；仅被已登记入口注册的可豁免 |
| 内部库 | `_` 前缀（`_codegen.py`、`_ssh-host.ps1`、`_local-cdn.ps1`） | 不登记；不得含独立入口 |
| 测试 | `tests/test_*.py` | 不登记 |
| 一次性运维 | 文件头前 20 行 `@oneshot` | 不登记；`toolchain.py check` 仅告警 |

新增脚本的默认形态：

- **语言**：默认 Python；PowerShell 只用于 Windows 原生编排（计划任务、SSH/隧道、进程宿主、Unity 构建）或薄壳转发。`build-player.ps1` / `l2-unity-gate.ps1` / `local-chain.ps1` 等大型绑定脚本不因“统一”重写。
- **编码/行尾**：Python UTF-8 无 BOM；新 PowerShell 脚本 ASCII-only，存量含中文在被触碰时转 UTF-8 BOM；`scripts/**` 统一 LF（`.gitattributes`）。
- **头注释**：入口四段式（用途/入口/契约/消费点）；生成器沿用五段式；内部库两段式。
- **退出码**：本地入口只用 0/1/2（0 成功、1 预期失败、2 用法错误）；子进程非 0/1/2 归一为 1，原始码写 stderr。
- **检查**：新增/改名后跑 `python scripts/toolchain.py check`；用户面入口补清单，内部库加 `_`，一次性脚本加 `@oneshot`。

## 新登记入口（canonical）

| 工具 ID | canonical 命令 | 说明 |
|---|---|---|
| `deploy.room` | `python scripts/toolchain.py run deploy.room` | 发布云上房间服；破坏性，执行前确认 |
| `deploy.gm` | `python scripts/toolchain.py run deploy.gm -- -Action <动作>` | 云端 GM 动作；破坏性 |
| `deploy.wg-peer` | `python scripts/toolchain.py run deploy.wg-peer -- -Name <测试者>` | 开通测试者隧道；破坏性 |
| `env.local-chain` | `python scripts/toolchain.py run env.local-chain -- <init|meta|room|up|status|keys>` | 本地联测一键链 |
| `quality.doc-lint` | `python scripts/toolchain.py run quality.doc-lint` | 文档纪律扫描（Doc 车道同源） |
| `quality.scan-missing-guids` | `python scripts/toolchain.py run quality.scan-missing-guids` | 缺失 GUID 扫描 |

## 约定

- 工程根由脚本自身位置推导（`$PSScriptRoot/../..` 或 `Path(__file__).parents[2]`），显式参数覆盖——移动脚本位置时必须同步核对该推导与脚本互引路径。
- 含中文的 `.ps1` 无 BOM 会被 PS 5.1 按 GBK 误解析——新脚本一律 ASCII-only；存量含中文脚本由 `pwsh` 启动，触碰时转 UTF-8 BOM 或去中文。
- 本地 CDN 端到端：`content/publish-candidate.ps1` 发布 → `content/serve-cdn.ps1` 起服 → Player 带 `-content.cdnUrl` 启动（目录/地址见 `content/local-cdn.json`）。
- 施工记录里引用的历史脚本路径是该次执行时的现状证据，不随本目录结构调整改写；活性入口以本 README、清单与生成索引为准。
