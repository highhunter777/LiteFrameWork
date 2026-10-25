# 方案：构建 preflight：变更驱动最小重建集

> 运行 build-preflight · 创建于 2026-10-24 · 修订 6（吸收五轮独立评审）

## 背景与目标

- 现状：出包只有单体链 `scripts/build/build-player.py`（restore → L0 → BuildHash → profile 同步 → 内置 bundle → dry-run → IL2CPP → 产物新鲜度，`build-player.py:22-28,87-94,168-211,213-281`），没有"改了什么→要不要出包→重建什么"的判定面；发版矩阵是人读表（`Docs/项目说明/11-联机测试流程.md:34-41`）。
- 要解决的问题：`change-to-verified` / `rebuild-to-change` 的浪费来自不知道最小重建集；路线图 T1 要求按 diff 拆开内容、纯客户端 C#、共享源三条路径，不需要出包时明确说"不需要"（`调试与开发工具链演进路线图.md:170-174,222`）。
- 目标（可观察）：给定 git 基线，输出**内环验证**与**外环交付**两套动作与理由；文档/工具/测试改动明确"不需要出包"；未知路径保守升级；运行前后工作树零变更。

## 需求澄清与假设

- 需要确认的问题：
  1. preflight 只做 advisory（只报告、不阻断 build-player）还是门禁？本方案取 advisory。
  2. 基线默认 `origin/main`，`--base <ref>` 覆盖；纳入未提交与未跟踪文件。
- 已确认答复：无（用户 2026-10-24 点单两线并行）。
- 采用假设：
  - A1：独立只读 CLI `build.plan`，不改 `build-player.py`；接门禁另批。
  - A2：内容当前走 Player 内置包（`BuiltinBundleBuildInvoker.cs:14-19`），服务端也读 `GameData/Config/*.bytes`；内容改动的**内环**只需 Editor 重载，**外环交付**必须重打包 + 表值换表重启。
  - A3：共享/Meta 判定复用 `gen-build-hash.py` 的 `collect()` 实际文件集合与 `META_TARGETS/META_EXTRA_FILES`（含其 `SKIP_DIRS`/`Editor` 排除），不按裸目录前缀；同时扫描 `RoomServer.csproj` 单文件出树链接做漂移对账。
  - A4：矩阵 6 行改为 6 条固定测试用例逐条断言（不解析 Markdown）；文档侧把矩阵改为指向 `build.plan`。
  - A5：清单条目 `build.plan`：`kind: cli`、`plane: external`、`purpose: delivery`、`writes: ["none"]`、`docs: Docs/项目说明/05-验证与门禁.md`、输出前缀 `[build/plan]`；无 outputs（`_toolchain_manifest.py:259-263`）。
  - A6：与 UI P2-3a 并行时 `toolchain.json` 是共享写入面；登记任务最后做、最小 hunk、编辑后立即 `toolchain.py check`。
  - A7：`MapTableProjection.cs` 是已知漏登记共享件（`RoomServer.csproj:35` 源链接、`SHARED_LINK_FILES` 未登记）；本批按 shared 处理（两端重建 + 警告），但**不改** `gen-build-hash.py` 常量/BuildHash；补登记另批并登记审计债。
- 假设影响与风险：A2 避免"内容不用重打包"的误导；A3 用生成器 collect 结果避免 Editor 子目录误判；A7 保证动作正确而不引入 BuildHash 变化。

## 范围与非目标

- 范围内：
  1. `scripts/build/build-plan.py`：只读 CLI；`--base <ref>`（默认 `origin/main`）、`--json`、`--changed-file <path>`（可重复）、`--project-path`。
  2. 变更采集：`git diff --name-status <base>...HEAD` + 工作区/暂存 + 未跟踪；忽略 `.workflow/`、`LocalNotes/`、`Builds/`、`TestResults/`、`Temp/`、`Library/`、`obj/`、`bin/`、`__pycache__/`；输出忽略计数。
  3. 分类内核：`classify(paths) -> Finding[]`，**按优先级唯一 area**（不重叠），每条含 `path/area/ring/innerActions/deliveryActions/reason`。
  4. 双计划汇总：`innerLoop`、`delivery`、`deliveryNeedsPlayerBuild`、`unknownPaths`、`manifestDrift[{path,known}]`、`notes`。
  5. 输出：人读摘要 + `--json`（`schemaVersion: 1`）。
  6. 清单登记 + 文档回写（11 流程、05 门禁、工具索引、施工进度新章）。
  7. 漂移守卫：解析 `RoomServer.csproj`，只对**单文件** `<Compile Include>` 出树链接对账（通配项如 Luban Runtime/Generated 视为已登记类别）；已知例外集合钉为 `{MapTableProjection.cs}`，新增漂移即红。
- 非目标：
  - 不改 `build-player.py`；不自动构建/缓存；不补登记 `MapTableProjection.cs`（另批）；不改 BuildHash 算法/源集；不改 CI。
  - 不联网/不调 Unity/不写资产；不解析 `.meta` GUID。
- 动作枚举（输出顺序＝执行语义序）：`editor_reload` → `regen_proto` → `regen_luban` → `regen_buildhash` → `run_authoritative_tests` → `rebuild_content_bundle` → `rebuild_client_player` → `rebuild_server_room` → `publish_server`（`deploy-room` 全链：构建上传/表/起服）→ `publish_meta`（Meta 产物构建/上传/重启任务全链）→ `publish_room_config` → `restart_room` → `none`。汇总时按该顺序去重。

## 现状与约束

- 相关代码/模块：
  - `build-player.py:22-28,87-94,168-211,213-281`。
  - `gen-build-hash.py:46-71,103-137`（`SHARED_TARGETS`、`SHARED_LINK_FILES`、`collect()` 与 `SKIP_DIRS`/Editor 排除）、`:78-88`（META）。
  - `Server/RoomServer/RoomServer.csproj:27-37`：Luban Runtime 通配、`GameData/Generated` 通配、`SimConfigMapper.cs`、`TableLayout.cs`、`MapTableProjection.cs`。
  - `Docs/项目说明/11-联机测试流程.md:34-41`（6 行矩阵）。
  - `scripts/gate/toolchain.json:825-875`；`scripts/_toolchain_manifest.py:259-263`；`_toolchain_domains.py`（build 域 docs 要求）；`_toolchain_docs.py`。
  - `scripts/tests/test_build_player.py`。
- 约束：
  - 红线 5：规则集中；矩阵固定用例。
  - 红线 1：共享/Meta 复用生成器常量与 `collect()`，不建第二份；漂移显式化。
  - 只读：`sys.dont_write_bytecode=True`；AC 以 `git status --porcelain` 前后一致判定。
  - 工具契约：`writes:["none"]`、`[build/plan]`、docs 指 05；与 UI 线并行按 A6。
  - 审计：新登记 `MapTableProjection` 漏登记（红线 1 周边）。

## 候选方案与取舍

| 方案 | 核心思路 | 优点 | 代价/风险 | 结论 |
|---|---|---|---|---|
| A | 独立只读 CLI + 双口径 + 生成器 collect 复用 + 漂移对账 | 可单测、零 Unity、动作正确、后续可被 build-player/CI 消费 | 规则表维护；已知漏项显式例外 | **采用** |
| B | `build-player.py --plan` 内嵌 | 入口集中 | 语义混入 writes=distribution；仍需单独测内核 | 拒绝 |
| C | 纯文档 checklist | 零代码 | 不可判定/回归/消费 | 拒绝 |

推荐 A。

## 设计要点

- 分类优先级（每个文件只归一个 area；area 决定动作集；同 area 多文件合并）：
  1. `shared`：路径 ∈ `collect(SHARED_TARGETS, SHARED_LINK_FILES)` 的实际文件集合（含生成器 Editor 排除）；或 ∈ 已知漏登记共享件 `{MapTableProjection.cs}` → inner `regen_buildhash + run_authoritative_tests`；delivery `+ rebuild_client_player + rebuild_server_room + publish_server`（矩阵第 3 行：服务端重新发布上传）；ring=outer。
  2. `proto`：`**/*.proto` → inner `regen_proto + regen_buildhash + run_authoritative_tests`；delivery `+ 两端重建 + publish_server`；outer。
  3. `luban`：`Luban/Data/**`、`Luban/Defines/**` → inner `regen_luban + regen_buildhash + run_authoritative_tests`；delivery `rebuild_content_bundle + 两端重建 + publish_server`（表数据进服务端）；outer。
  4. `meta`：inner `regen_buildhash + run_authoritative_tests`；delivery `publish_meta`（Meta 产物构建/上传/重启任务全链）；其中 `META_EXTRA_FILES` 里位于 `Assets/RoomServer/**` 的三件（`JoinTicketFormat.cs`/`Base64Url.cs`/`MatchStateMachine.cs`）**同时附加** delivery `rebuild_server_room + publish_server`；outer。
  5. `server`：inner `run_authoritative_tests`；delivery：`Server/RoomServer/**` → `rebuild_server_room + publish_server`；`Server/MetaServer/**` → `publish_meta`；`Server/ReplayTool/**` → `none`；outer。
  6. `room_kernel`：`Assets/RoomServer/**` → inner `editor_reload`；delivery `rebuild_server_room + publish_server`（**不加客户端重打包**——矩阵第 4 行"客户端不动"；本地服档位编辑器重编译即可）；outer。
  7. `editor_code`：`Assets/**/Editor/**` 的 `.cs` → inner `editor_reload + run_authoritative_tests`；delivery `none`；inner（Editor 下的非 `.cs` 资源归 content）。
  8. `client_code`：`Assets/LiteClient/**`、`Assets/LiteGame/**`（`Editor/`、`Lua/` 除外）、`Assets/LiteFramework/**`、`Assets/LiteView/**`、`Assets/LiteSim/**`（shared 命中优先）、`Assets/LiteTesting/**` 的 `.cs`，以及 `Assets/**/*.asmdef` → `rebuild_client_player`；这些目录下的非 `.cs` 资源归 content；outer。
  9. `package`：inner `run_authoritative_tests`；delivery：`Packages/com.code-philosophy.luban/**` → 两端重建 + `regen_buildhash + publish_server`；其他 `Packages/**` → `rebuild_client_player` + note；outer。
  10. `project_settings`：inner `editor_reload`；delivery `rebuild_client_player`；outer。
  11. `deploy_config`：inner `run_authoritative_tests`；delivery：`deploy/roomserver.windows.json` → `publish_room_config + restart_room`；其他 `deploy/**`、`scripts/**`、`.github/**` → `none`（note 跑门禁）；inner。
  12. `content`：`Assets/GameData/Config/**`（表值/SO）、`Assets/LiteGame/Lua/**`、`Assets/ShareResource/**`、`Assets/Scenes/**`、**以及 `Assets/**` 下其余非 `.cs` 资源**（Art/Animation/FX/Model/Sound/Resources/Map/Prefab 等）→ inner `editor_reload`；delivery `rebuild_content_bundle + rebuild_client_player`（内置包）；其中 `Assets/GameData/Config/**` 表值附加 `publish_server`（服务端读同一份 bytes，走 deploy-room 全链含表/起服）；middle。
  13. `test`：`Assets/Tests/**`、`Tests/**` → `run_authoritative_tests`；inner。
  14. `docs`：`Docs/**`、根 `*.md` → `none`；inner。
  15. `unknown`：其余根文件/未知 → inner `run_authoritative_tests`；delivery `regen_buildhash + 两端重建 + publish_server`；outer；入 `unknownPaths`。
- 漂移对账：分别解析两个 csproj 的出树 `<Compile Include>`，按各自已登记集合比对：
  - `Server/RoomServer/RoomServer.csproj` 单文件项 vs `SHARED_LINK_FILES`：`SimConfigMapper.cs`/`TableLayout.cs` 已登记；`MapTableProjection.cs` 为 known=true 例外；其他单文件 known=false。
  - `Server/MetaServer/MetaServer.csproj` 单文件项 vs `META_EXTRA_FILES`：三件已登记；其他 known=false。
  - 通配项只允许已登记白名单（RoomServer：`Packages/com.code-philosophy.luban/Runtime/*.cs`、`Assets/GameData/Generated/**/*.cs`；MetaServer 按其 csproj 实际通配另行登记）；**新增未登记通配**入 `manifestDrift`（known=false），不静默放过。
- `shared` 的路径集合来自 `collect()`；`--changed-file` 模式同样按集合比对（需加载生成器模块，`dont_write_bytecode`）。`collect()` 的 ROOT 固定为当前仓库根：`--project-path` 只用于 git/csproj 读取，若指向别的仓库直接 fail-fast（本工具有意只服务当前仓库）。
- 失败与边界：非 git/未知 base → exit 1；空 diff → "无改动" exit 0；路径归一；`--changed-file` 跳过 git。
- 通用性：规则数据表 + 动作枚举；后续 build-player/CI 只消费 `BuildPlan`。

## 通用性与扩展性

- 根因：构建决策与文档矩阵各写一份、不可执行。
- 复用：`gen-build-hash.collect()`、`_common` 输出约定、unittest；漂移对账用 csproj 文本。
- 避免硬编码：文件集合来自生成器；规则表集中；仅已知例外显式。

## 测试策略

- `scripts/tests/test_build_plan.py`：
  1. shared 目录/源链接件/`MapTableProjection` → buildhash + 两端（`MapTableProjection` 带 warning）。
  2. proto/Luban → 生成 + 两端 + 内容。
  3. client_code（含 asmdef）→ 客户端；room_kernel → 内环 editor + 交付服务端（无客户端重打包）。
  4. 内容（Config/Lua/ShareResource/Art/Animation/FX/Model/Sound/Resources/Prefab/Scene）→ 内环 editor；交付内容包+重打包。
  5. Editor 代码 → inner only；test/docs/scripts/deploy 脚本 → none。
  6. `deploy/roomserver.windows.json` → 配置发布 + 重启。
  7. Packages Luban → 两端 + 哈希；其他包 → 客户端；ProjectSettings/BuildProfiles → 客户端。
  8. 未知路径 → 保守两端 + unknownPaths。
  9. 优先级：`Assets/LiteSim/Core/Scripts/Editor/**` 判 editor_code（不判 shared）；`Assets/LiteGame/Lua/**` 判 content（不判 client_code）；`Assets/LiteGame/Editor/**` 判 editor_code。
  10. 矩阵 6 行固定用例。
  11. 路径归一/重复/忽略计数；base 解析失败；未跟踪文件。
  12. JSON schema（`schemaVersion:1`，键集合）。
  13. 只读：在当前仓库运行 `--base HEAD` 与 `--changed-file` 两种模式，`git status --porcelain` 前后一致（不指向临时仓库——`collect()` ROOT 固定，跨仓库 fail-fast 有单独用例）。
  14. 漂移守卫：`parse_csproj(text)` 为纯函数；真实两个 csproj 的单文件 known 例外 == `{MapTableProjection.cs}`；用构造文本注入一条新增单文件链接与一条未登记通配，断言分别 known=false 并输出警告。
- 先失败后通过：先落骨架 CLI（空实现 exit 1），测试断言失败；再实现。

## 验收标准

- [ ] AC1：干净树 `--base HEAD` 输出"无改动，不需要出包"，exit 0。
- [ ] AC2：共享源/源链接件 → 内环 `regen_buildhash`；交付两端重建 + `publish_server`；理由指向权威源集。
- [ ] AC3：proto/Luban/客户端 C#/房间内核/内容/文档脚本测试/配置发布 混合改动，**每个 Finding 都有 innerActions 与 deliveryActions**，动作集符合规则表且按语义序去重；内容交付含"内置包随 Player，需重打包"（表值另含 `publish_server`）；房间内核交付**不含**客户端重打包（矩阵第 4 行）。
- [ ] AC4：未知路径保守两端并列入 `unknownPaths`；`manifestDrift` 含 `MapTableProjection.cs`（`known=true`）；临时注入的新增单文件链接/未登记通配均 `known=false` 且有警告。
- [ ] AC5：`--json` 含 `schemaVersion=1` 与 `base/head/innerLoop/delivery/deliveryNeedsPlayerBuild/findings/unknownPaths/manifestDrift/ignoredCount/notes`。
- [ ] AC6：运行前后 `git status --porcelain` 一致；`build.plan` 登记后 `toolchain.py check` 0 error、`toolchain.py docs --check` 通过。
- [ ] AC7：矩阵 6 行固定用例逐条通过且拆到子域：内容（含美术/动画/音效）→ 内容包+重打包；服务端宿主 RoomServer → 服务端重建+`publish_server`，MetaServer → `publish_meta`；共享源（含 proto）→ 两端重建+`publish_server`；房间内核 → 服务端重建+`publish_server`、客户端不动；表源 → 生成+两端重建+内容包+`publish_server`；仅配置 → `publish_room_config + restart_room`（不重打包）。
- [ ] AC8：Python 测试、L1、doc-lint、toolchain-check 全绿；`gen-build-hash.py --check-shared` 值不变。
- [ ] AC9：路径归一、忽略计数、base 解析失败、未跟踪采集、优先级冲突用例通过。

## 风险与回滚

- 风险：规则漏项（未知保守 + 固定用例）；矩阵漂移（6 用例）；import 副作用（`dont_write_bytecode` + `__main__` 保护 + 工作树断言）；与 UI 线并发（A6）；`MapTableProjection` 长期不补（可见警告 + 审计登记）。
- 监控：Python/L1/doc/toolchain-check；`manifestDrift` 输出。
- 回滚：删除脚本/测试/清单条目/文档新增段，重新生成工具索引（`toolchain.py` 索引产物）；不动资产/生成物，零迁移。
