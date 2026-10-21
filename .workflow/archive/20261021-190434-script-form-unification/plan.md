# 方案：脚本形态统一

> 运行 script-form-unification · 设计权威：[脚本形态统一专项设计](../../../Docs/设计文档/架构/脚本形态统一专项设计.md)

## 背景与目标

- **现状**：`scripts/` 下同时存在 18 个 `.ps1`（约 2672 行）与 15 个 `.py`（约 2536 行）；入口有“脚本直调 / `toolbox.py` / `toolchain.py`”三套路径；`deploy/gm.ps1`、`deploy/deploy-room.ps1`、`deploy/wg-peer.ps1`、`local/local-chain.ps1` 等用户面脚本未进清单；内部模块（`toolchain_*.py`）与一次性脚本（`pipeline/patch_p1_plugin_metas.py`）无统一标识；EOL/编码/帮助/退出码/输出前缀各不相同。
- **要解决的问题**：新脚本没有默认形态；移动或改名脚本时注册面静默漏检；Agent 与维护者每次都要读源码猜调用方式；编码与行尾差异制造 diff 噪声和 PS 5.1 解析风险。
- **目标（可观察）**：
  1. 所有用户面脚本在 `scripts/gate/toolchain.json` 有登记，`toolchain.py check` 能发现孤儿脚本、编码与 EOL 违规；
  2. 内部库、测试、一次性脚本有机械可识别的形态（`_` 前缀 / `tests/` / `@oneshot`），不再与入口混放；
  3. 本次新增的入口具备 canonical 命令、原生帮助、用法错误退出 2、统一输出前缀；
  4. 被触碰的脚本统一 LF 与编码规则，业务逻辑零变化；
  5. 上述规则由测试与 `toolchain.py check` 守卫，而不是靠文档约定。

## 需求澄清与假设

- **已确认**：
  - 上一轮用户裁决“先出专项设计”，设计已落档 `Docs/设计文档/架构/脚本形态统一专项设计.md`（v0.1，已入设计索引，Doc lane 0/0）。
  - 不发起全量 PS→Python 迁移；`codegen/` 保留 `gen_<对象>.py` 与工具箱入口例外；大型 Windows 绑定脚本（`build-player` / `l2-unity-gate` / `local-chain`）不因“统一”重写。
- **本次采用假设**：
  1. 现有工作区中未提交的工具链 L1/L2-A/B 改动是本次基线；执行前需要用户确认提交边界（避免把设计批与施工批混在一个提交）。
  2. 本次运行只覆盖 **S-A 登记与检查地基 + S-C 的“新增/触碰文件”部分 + 新登记入口的 S-B 基线**；存量入口的全面帮助/退出码归一与 L2-D1 的 `args` schema 另起运行。
  3. 云侧部署脚本不在本机真实执行；验证到 `Get-Help`/参数校验/静态检查为止，不发起 SSH、上传、起服或改云上配置。
  4. 共享根推导收口（`_common.py` / `_common.ps1`）归后续 S-C2；本次只消除新增/触碰文件内的重复推导，不搬动所有存量脚本的根逻辑。
- **假设影响与风险**：若用户认为本次应一次性覆盖全部 S-A～S-D，范围与验证成本会显著上升，且 S-B 会被 L2-D1 阻塞；若基线提交边界未确认，施工 diff 会与既有未提交工具链改动混在一起。

## 范围与非目标

### 范围内（本次运行）

- **S-A 角色与登记**：
  - 清单新增 `deploy` 分类与 `deploy.room` / `deploy.gm` / `deploy.wg-peer`、`env.local-chain`、`quality.doc-lint`、`quality.scan-missing-guids` 登记；
  - 内部库与一次性脚本归类：`toolchain_manifest.py` / `toolchain_docs.py` / `toolchain_surfaces.py` → `_` 前缀；`content/local-cdn.config.ps1` → `_local-cdn.ps1`；`pipeline/patch_p1_plugin_metas.py` 标 `@oneshot`；
  - `toolchain.py check` 增加孤儿可执行脚本、编码、EOL 检查；
  - 更新 `scripts/README.md`、生成工具索引、必要的操作文档指针。
- **S-C 文件与配置（仅新增/触碰文件）**：
  - 新增根 `.gitattributes`，声明 `scripts/**` 文本 LF；
  - 本次触碰的脚本统一 LF；PS 编码按设计规则（新脚本 ASCII-only，存量含中文触碰时转 UTF-8 BOM 或去中文）；
  - 新登记入口补四段式头注释与帮助；
  - 配置加载器改名并更新 dot-source 引用。
- **S-B 基线（仅新登记入口）**：
  - canonical 命令写清单；
  - `Get-Help` / `--help` 可用；
  - 用法错误返回 2（至少覆盖未知子命令/缺必填参数的自定义校验路径）；
  - 顶层摘要/错误行使用 `[<域>/<工具>]` 前缀；
  - 输出与退出码以测试钉住。

### 非目标（本次明确不做）

- 不做全量 PS→Python 语言迁移；不改写 `build-player.ps1` / `l2-unity-gate.ps1` / `local-chain.ps1` 的业务实现。
- 不实现 L2-D1 的清单 `args` schema、`run --dry-run`、`refresh_hash` 严格失败（归工具链 L2-D 批次）。
- 不统一所有存量入口的帮助/输出/退出码；本次只建立检查与新增入口基线，存量按“触碰即归一”滚动。
- 不改 CI 车道、测试分层、Unity Pipeline、BuildHash 闭包、共享源集或协议/表。
- 不执行云侧部署、WireGuard peer 变更、GM 动作或任何需要真实密钥的操作。

## 现状与约束

### 相关代码与数据

| 面 | 事实 |
|---|---|
| 清单 | `scripts/gate/toolchain.json` schemaVersion 3，7 分类 / 53 工具 / 5 注册面；categories：baking/ui/gm/codegen/quality/build/env |
| 未登记用户面 | `scripts/deploy/gm.ps1`、`deploy-room.ps1`、`wg-peer.ps1`、`scripts/local/local-chain.ps1` |
| 未登记但可直接运行 | `scripts/gate/doc-lint.py`（被 `test.ps1 -Lane Doc` 引用）、`scripts/content/scan_missing_guids.py` |
| 内部库 | `scripts/toolchain.py` 导入 `toolchain_manifest.py` / `toolchain_docs.py` / `toolchain_surfaces.py`；`scripts/codegen/_codegen.py`、`scripts/deploy/_ssh-host.ps1` 已合规 |
| 一次性脚本 | `scripts/pipeline/patch_p1_plugin_metas.py`（`--pristine` 补丁回放，非日常流程） |
| 配置加载器 | `scripts/content/local-cdn.config.ps1` 被 `publish-candidate.ps1` / `serve-cdn.ps1` dot-source；数据在 `local-cdn.json` |
| 编码/EOL | PS 中 `test.ps1` / `deploy-room.ps1` / `gm.ps1` / `wg-peer.ps1` 为 CRLF；Python 中 `gen-build-hash.py` / `scan_missing_guids.py` 为 CRLF；`l2-unity-gate.ps1` 带 BOM；多个 PS 含中文但无 BOM |
| 退出码 | 本地多为 0/1（`throw`→1）；`local-chain` 在 Meta 构建失败时透传 `$LASTEXITCODE`；`serve-cdn` 透传 dotnet 退出码；`nightly-gate` 透传子进程码。`wg-peer.ps1` 的 2/3/4 在远端 here-string 内，是远端哨兵，不是本地退出码 |
| 帮助 | deploy 三件已有 `<# .SYNOPSIS ... #>`；gate/build/content/local 多数只有头注释或 `param()`，无 comment-based help；`local-chain` 无 help |
| 消费面 | `deploy-room` / `gm` / `wg-peer` 被 `Docs/开发导航/云侧速查.md`、10 手册、11 流程引用；`local-chain` 被 AGENTS.md、07 手册、scripts/README 引用；`doc-lint` 被 Doc lane 与文档规范引用 |

### 约束

- **PowerShell 编码**：PS 5.1 会把无 BOM UTF-8 中文按 GBK 误读；新 PS 脚本 ASCII-only，存量含中文在被触碰时转 UTF-8 BOM 或去中文。所有入口由 `pwsh` 启动（计划任务 runner 已是 `pwsh`）。
- **行尾**：仓库 `core.autocrlf=true` 且无 `.gitattributes`；本次新增规则后只归一“本次触碰的文件”，不做全仓一次性格式化（设计 §5.1）。
- **清单是单源**：分类/工具/命令/文档路径只改 `toolchain.json`；生成索引由 `toolchain.py docs` 重生成，不手改。
- **工具链检查不能执行脚本**：孤儿/编码/EOL 检查只做静态扫描，不能在 `check` 中跑 Unity、SSH 或云侧命令。
- **云侧安全**：`deploy/*` 与 `local-chain` 的验证不得触发真实网络/密钥读取；只做 `Get-Help`、参数校验与静态引用检查。
- **不改业务行为**：除“未知命令/缺参退出码”“输出前缀”“帮助块”外，不改变脚本的执行链、产物、默认值或密钥处理。
- **提交边界**：当前工作区已有未提交的工具链 L1/L2-A/B 与设计文档；施工前需用户确认基线提交或明确允许混合 diff。

## 候选方案与取舍

| 方案 | 核心思路 | 优点 | 代价/风险 | 结论 |
|---|---|---|---|---|
| A 分波次（推荐） | 本次先做 S-A + S-C（触碰文件）+ 新入口 S-B 基线；存量契约与语言边界后续波次 | 风险低、可独立验收；先建立检查地基，后续每加一个工具都受益；不阻塞于 L2-D1 | 全量形态统一不是一次完成；存量脚本仍有帮助/输出差异 | **采用** |
| B 一次做完 S-A～S-D | 登记、全部契约、文件形态、语言边界一次落地 | 形态一次到位 | S-B 依赖未施工的 L2-D1；全量 PS 契约改写与 EOL 归一 diff 大；云侧脚本验证成本高 | 不采用 |
| C 只补登记 | 只把未登记脚本写进清单，其他不动 | 成本最低、立刻消除“找不到” | 孤儿检查、编码/EOL、新入口契约仍无守卫；下一批继续产生同型问题 | 不采用（只作为 A 的子集） |

**推荐 A 的理由**：设计已把 S-A 定为“登记与检查地基”，它不依赖 L2-D1，且能立即阻止新增孤儿/编码债；把完整契约与语言迁移拆到后续波次，符合“迁移按风险分批”和“最小改动”。

## 设计要点

### 1. 清单登记（S-A）

新增 `deploy` 分类（`order` 放在 codegen 与 quality 之间），并登记：

| id | category | kind | canonical command | docs |
|---|---|---|---|---|
| `deploy.room` | deploy | cli | `pwsh scripts/deploy/deploy-room.ps1` | `Docs/项目说明/10-Windows云主机部署.md` |
| `deploy.gm` | deploy | cli | `pwsh scripts/deploy/gm.ps1` | `Docs/开发导航/云侧速查.md` |
| `deploy.wg-peer` | deploy | cli | `pwsh scripts/deploy/wg-peer.ps1` | `Docs/开发导航/云侧速查.md` |
| `env.local-chain` | env | cli | `pwsh scripts/local/local-chain.ps1` | `Docs/项目说明/07-本地联测.md` |
| `quality.doc-lint` | quality | gate | `python scripts/gate/doc-lint.py` | `Docs/修改指南.md` |
| `quality.scan-missing-guids` | quality | cli | `python scripts/content/scan_missing_guids.py` | `Docs/工具链/技术支持.md` |

- 所有 `docs` 路径必须存在（`toolchain.py check` 会校验）。
- `writes` 按实际写面声明：deploy 三件写云上/本机 `.conf`/构建产物（`writes: ["distribution"]` 或 `["none"]` 按现有语义）；`env.local-chain` 写 `deploy/.env` 与 `Assets/StreamingAssets/deploy.args`（`writes: ["source","distribution"]`，不标 destructive）；`quality.*` 只读（`writes: ["none"]`）。
- `safety` 对只读/短命令工具保持缺省（按保守 `dryRun=unsupported/undo=none/confirmation=required`）；不要在本次给 deploy 脚本伪造 dry-run。
- 生成索引：`python scripts/toolchain.py docs`，并在 Doc lane 校验 freshness。

### 2. 内部库与一次性脚本归类（S-A）

| 现路径 | 目标 | 处理 |
|---|---|---|
| `scripts/toolchain_manifest.py` | `scripts/_toolchain_manifest.py` | 改 `toolchain.py` 导入；更新任何文档引用 |
| `scripts/toolchain_docs.py` | `scripts/_toolchain_docs.py` | 同上 |
| `scripts/toolchain_surfaces.py` | `scripts/_toolchain_surfaces.py` | 同上 |
| `scripts/content/local-cdn.config.ps1` | `scripts/content/_local-cdn.ps1` | 改 `publish-candidate.ps1` / `serve-cdn.ps1` 的 dot-source；不改 JSON 数据 |
| `scripts/pipeline/patch_p1_plugin_metas.py` | 原路径 | 文件头加 `@oneshot` + 用途/日期；不登记 |
| `scripts/gate/doc-lint.py` | 原路径 | 登记为 `quality.doc-lint`（不再只靠 `test.ps1` 引用） |
| `scripts/content/scan_missing_guids.py` | 原路径 | 登记为 `quality.scan-missing-guids`，补 `argparse --help` 与退出码 |

**不可登记项**：`scripts/toolchain.py`（全局入口，孤儿检查显式放行）、`scripts/hooks/shared-source-reminder.ps1`（由 hook surface 引用，孤儿检查把 surface 引用计为覆盖）、`scripts/codegen/gen-*.py`（由 `toolbox.py` 的 `TOOLS` 注册表引用）、`scripts/tests/test_toolchain.py`（测试）、`scripts/codegen/_codegen.py` 与 `scripts/deploy/_ssh-host.ps1`（内部库）。

### 3. 孤儿/编码/EOL 检查（S-A）

新增内部模块 `scripts/_toolchain_scripts.py`（`_` 前缀，符合新规则），由 `toolchain.py collect_checks` 调用；只对 canonical manifest 启用，避免 `--manifest` 临时夹具产生噪声。

**孤儿判定**（按优先级）：

1. 出现在清单任一 `command` 的路径 token 中；
2. 是全局入口 `scripts/toolchain.py`；
3. 基名以 `_` 开头（内部库）；
4. 位于 `scripts/tests/`；
5. 文件头前 20 行含 `@oneshot`；
6. 被某个已登记入口的脚本文本引用（如 `toolbox.py` 的 `TOOLS`、`test.ps1` 的 `doc-lint.py`），或被 hook surface 配置引用（如 `.zcode/config.json` / `.claude/settings.json` 引用 `scripts/hooks/*.ps1`）。

未命中任一条件 → 告警 `[toolchain] WARN: unregistered executable script: <path>`；`--strict` 升级为错误。

**编码/EOL 判定**：

- Python：BOM → 告警/严格错误；CRLF → 告警/严格错误；
- PowerShell：BOM-less 且含非 ASCII → 告警/严格错误；CRLF → 告警/严格错误；BOM + 非 ASCII 允许；
- 排除 `__pycache__`、`.venv`、`node_modules`、二进制；只扫 `scripts/**/*.{py,ps1}`。

**实现约束**：检查器不硬编码具体脚本清单；只读清单、文件头标记、目录与引用关系。新增脚本的接入方式是“补登记/加 `_`/加 `@oneshot`”，而不是改检查器。

### 4. 文件与配置形态（S-C，限触碰文件）

- 新增根 `.gitattributes`：`scripts/** text eol=lf`（必要时补 `scripts/*.py` / `scripts/*.ps1` 规则）；用 `git check-attr` 验证。
- 本次触碰的脚本把 CRLF 归一为 LF；用 Python 字节模式转换（保留除 `\r\n` 外的字节），转换前后做“去掉 EOL 后内容 hash 相同”校验。
- PS 编码：本次触碰的 `gm.ps1` / `deploy-room.ps1` / `wg-peer.ps1` / `test.ps1` 等按规则处理；含中文且继续用 PS 的加 UTF-8 BOM，新脚本保持 ASCII-only。
- `_local-cdn.ps1` 只做改名与引用更新；`local-cdn.json` 不动。
- 新登记入口补四段式头注释（用途/入口/契约/消费点）；已有 comment-based help 的 deploy 三件补 `[CmdletBinding()]`（不改业务）。

### 5. 新入口契约基线（S-B0）

只覆盖本次新登记的入口：

- **帮助**：`deploy.*` 用 `Get-Help` 可见 `.SYNOPSIS/.DESCRIPTION/.PARAMETER`；`env.local-chain` 增加 comment-based help；`quality.*` 用 `--help`。
- **用法错误**：`local-chain.ps1` 未知命令改为 usage + `exit 2`（当前 `exit 1`）；`gm.ps1` / `wg-peer.ps1` 的缺必填参数改为显式 usage + `exit 2`，避免非交互式 PowerShell 弹出 Mandatory 提示；`doc-lint.py` / `scan_missing_guids.py` 由 argparse 返回 2。
- **输出前缀**：新入口的顶层摘要/错误行加 `[deploy/room]`、`[deploy/gm]`、`[deploy/wg]`、`[local/chain]`、`[quality/doc]`、`[quality/guids]`；详细日志不强制重写。
- **退出码**：本地入口只返回 0/1/2；`local-chain`/`serve-cdn`/`nightly-gate` 的子进程透传面统一映射为 1（原始码写 stderr），本次只改 `local-chain` 的 Meta 构建失败分支和未知命令分支；`serve-cdn`/`nightly-gate` 的透传收口留给 S-B 全量批次。
- **非交互**：不在 CI/Agent 路径等待输入；破坏性动作仍由既有 `-Force` / 显式参数控制。

### 6. 不改的部分

- 不改任何脚本的业务算法、产物路径、默认值、密钥读取顺序。
- 不把 `deploy` 脚本改成 Python；不给它们加伪造的 `--dry-run`。
- 不改 `build-player.ps1` / `l2-unity-gate.ps1` 的构建链；本次最多给它们加帮助块（若触碰），不改逻辑。
- 不改 Unity 序列化资产、共享源集、协议、表或 BuildHash。

## 通用性与扩展性

- **根因**：形态差异来自“没有登记判据 + 没有默认语言规则 + 没有文件形态检查”，而不是某个脚本写错。方案用清单 + 文件标记 + 静态检查解决共性，而不是逐脚本特判。
- **复用点**：
  - 清单 `command` 是 canonical 入口的唯一来源；
  - `_` 前缀 / `tests/` / `@oneshot` 是内部面的机械标记；
  - `_toolchain_scripts.py` 的检查器只消费清单与目录约定，新增脚本无需改检查器；
  - 后续 `_common.py` / `_common.ps1` 复用根推导与输出/退出契约，避免每脚本复制。
- **避免硬编码**：检查器不维护第二份脚本白名单；新增工具只改 `toolchain.json` 与脚本自身；分类只改 `categories`，Hub/索引自动跟随。
- **扩展路径**：新脚本 → 选角色（全局/域/叶子/内部/测试/一次性）→ 按规则命名与头部 → 补登记或加标记 → `toolchain.py check` 自动覆盖。S-B 全量批次再把 `args`/dry-run 接到 L2-D1。

## 测试策略

### 先写测试（red-first）

新增 `scripts/tests/test_toolchain_scripts.py`，覆盖：

1. 孤儿检查：临时根目录中放置未登记的可执行脚本 → 返回 warning；`_` 前缀 / `tests/` / `@oneshot` / 被登记入口引用 / hook surface 引用 → 不告警；`strict` 把 warning 变 error。
2. 编码/EOL：临时文件构造 BOM Python、BOM-less 非 ASCII PS、CRLF 文件 → 返回对应告警；干净文件不告警。
3. 清单：新增 `deploy.*` / `env.local-chain` / `quality.*` 条目存在，command 路径与 docs 路径存在；`toolchain.py docs --check` current。
4. 新入口契约：`local-chain.ps1` 未知命令退出 2；`scan_missing_guids.py --help` 退出 0；`doc-lint.py --help` 退出 0；deploy 三件 `Get-Help` 能返回 SYNOPSIS（通过 `pwsh -NoProfile -Command "Get-Help ..."`，不执行脚本主体）。
5. 现有 `test_toolchain.py` 的 `check`/`list`/`docs` 用例更新新增条目断言。

预期失败证据：在实现检查器与清单前运行新增测试，必须因“缺少条目/缺少告警/退出码不是 2”而失败，而不是导入错误或环境故障。

### 验证命令

```powershell
python -m unittest discover -s scripts/tests -p "test_toolchain*.py" -v
python scripts/toolchain.py check
python scripts/toolchain.py docs --check
python scripts/gate/doc-lint.py
pwsh -NoProfile -File scripts/gate/test.ps1 -Lane L1 -Profile PullRequest
```

- 云侧脚本只做 `Get-Help`/静态检查，不跑 SSH。
- EOL/编码转换后额外跑“去 EOL 内容 hash 前后一致”的字节校验。
- 不跑 L2；本次不触碰 Unity/共享源集。

## 验收标准

- [ ] **AC1 登记完整**：清单新增 6 个工具与 `deploy` 分类；所有用户面脚本均有 canonical command；`toolchain.py check` 与 `docs --check` 通过。
- [ ] **AC2 孤儿检查有效**：新增测试覆盖“未登记脚本告警”“`_`/tests/`@oneshot`/引用/surface 豁免”“strict 升级为错误”，且 repo 当前无未解释孤儿。
- [ ] **AC3 内部面归类**：`toolchain_*.py` → `_toolchain_*.py`；`local-cdn.config.ps1` → `_local-cdn.ps1`；`patch_p1_plugin_metas.py` 带 `@oneshot`；全仓旧路径引用为零。
- [ ] **AC4 文件形态**：`.gitattributes` 生效；本次触碰文件全部 LF；BOM/非 ASCII 规则通过检查；转换前后去 EOL 内容 hash 一致。
- [ ] **AC5 新入口契约**：6 个新登记入口有帮助；`local-chain` 未知命令/`gm`/`wg-peer` 缺参返回 2；顶层摘要前缀统一；详细日志不回归。
- [ ] **AC6 业务零变化**：除帮助/输出前缀/退出码/编码/EOL/改名外，无脚本逻辑改动；`git diff` 不涉及 Unity 资产、共享源集、协议、表、BuildHash。
- [ ] **AC7 文档回写**：`scripts/README.md`、生成工具索引、必要的操作文档指针更新；Doc lane 0/0。
- [ ] **AC8 门禁**：`test_toolchain*.py` 全绿；`toolchain.py check`/`docs --check` 绿；Doc lane 绿；L1 绿。

## 风险与回滚

| 风险 | 监控/发现 | 回滚 |
|---|---|---|
| 改名（`toolchain_*`、`local-cdn` 加载器）造成导入/dot-source/文档引用断裂 | `python scripts/toolchain.py check`、`grep` 旧名、`test_toolchain*.py`、Doc lane | 按 git rename 回退；恢复旧文件与引用的单文件 diff |
| 清单新增分类/工具导致 Hub 或索引变化 | `toolchain.py docs` 重新生成并 diff；`EditorToolCatalogEditModeTests` 只在 Unity 侧，本次不跑 | 回退 `toolchain.json` 与生成索引 |
| EOL/编码转换掩盖逻辑改动 | 转换前后去 EOL 内容 hash；`git diff --stat` 限定文件数 | 回滚该文件，改用触碰时逐文件归一 |
| 帮助/退出码改动影响既有调用方 | `grep` 调用面；Doc lane；L1 | 保留旧输出/退出码一个批次，或回退该入口改动 |
| 云侧脚本验证误触发网络 | 只跑 `Get-Help`/参数校验；禁止执行 deploy 主体 | 立即停止；检查是否有 SSH/云上副作用（本方案不应发生） |
| 与并行会话的未提交改动混 diff | 施工前 `git status` + 内容 diff；按文件/hunk 分离 | 先提交基线或使用独立分支/worktree；不覆盖他人改动 |
| 共享根推导未收口导致移动脚本仍断裂 | 本次只触碰文件内去重复；后续 S-C2 统一 `_common` | 记录为后续批次，不在本次宣称全仓根推导单源 |

**监控方式**：每个任务完成后跑对应测试；阶段验证跑完整命令集；施工日志记录命令、输出摘要、文件清单。**回滚路径**：按任务粒度回退；清单/索引/改名/`.gitattributes` 均可单文件或单提交回退；不涉及数据迁移或云上状态。
