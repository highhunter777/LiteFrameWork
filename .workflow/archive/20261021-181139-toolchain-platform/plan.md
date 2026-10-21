# 方案：开发工具链平台统一

> 运行 toolchain-platform · 创建于 2026-10-21T17:43:42+08:00

## 背景与目标

- 现状：编辑器侧约 30 个 `MenuItem`、5 个窗口、4 个编辑器程序集（`LiteGame.Editor`、`LiteCodeGen.Editor`、`LiteSim.Core.Editor`、`DisciplineScanner.Editor`）。入口分散在 `LiteGame/`、`LiteSim/`、`Tools/` 三个根下；脚本入口分散在 `scripts/{codegen,gate,build,content,local,deploy}`；工具元数据同时存在于 `toolchain.json`、`技术支持.md`、`scripts/README.md`、代码地图与 AGENTS.md。
- 要解决的问题：
  1. 人和 Agent 都缺少一个“从这里开始”的统一入口；新增工具只能靠记忆和文档。
  2. 工具清单不是单源：运行时清单、CLI 命令、文档表格各自维护，容易漂移。
  3. 外部注册面（Windows 计划任务、CI 脚本路径、hook 配置）没有被任何门禁检查。本机实测夜间任务仍指向已不存在的 `scripts/nightly-gate.ps1`（实际在 `scripts/gate/`），最近一次运行结果为非 0，落盘日志止于 2026-10-04。
  4. L2 的 `eval_file` 调用缺少 `file` 参数，新鲜度守卫实际被降级为 INFO；统一 CLI 若继续复用这条路径，会把不可信结果传播出去。
- 目标（可观察）：
  - 一个根菜单 `LiteGame/工具链` 与一个 Hub 窗口；所有顶层游戏工具都能从 Hub 发现并打开/执行。
  - 一个 CLI：`python scripts/toolchain.py`；支持 `list / check / docs / open / run / generate / verify / build / doctor`。
  - 一份机器可读清单：`scripts/gate/toolchain.json`；工具、分类、入口、写入面、文档锚、外部注册面都在这里登记。
  - 一份生成文档：`Docs/工具链/工具索引.md`；`docs --check` 可验新鲜度。
  - `doctor` 能发现计划任务/CI/hook 路径漂移；`install-nightly-task.ps1` 能把夜间任务修回正确入口。
  - 新增工具只需“清单一条 + handler 一个（若为编辑器工具）”，不再新增散落菜单与文档副本。

## 需求澄清与假设

- 需要确认的问题：无。用户已裁决走“全工具链平台”档（编辑器 Hub + 页件契约 + 单一 CLI + 清单单源 + 文档生成 + 目录搬动注册面检查）。
- 已确认答复：
  - 统一目标是整个开发工具链，不只是编辑器菜单。
  - 允许旧顶层菜单退场；上下文菜单和可独立复用的包级入口保留。
  - 本批优先“入口/清单/验证链”统一，不重写各工具的业务实现。
- 采用假设：
  1. Hub 壳使用 IMGUI，不新增 Odin 依赖；Odin 编排窗口作为“打开原窗口”目标。
  2. GM 六页已有 `IGmPage` 页件契约，直接嵌入 Hub；烘焙工具箱本身已是多页工具箱，作为单个“打开窗口”目标；UI 三个窗口（绑定/编排/回写）本批作为 launch 目标，不重写内部。
  3. `toolchain.json` 保持在 `scripts/gate/toolchain.json`（不搬进 Assets，避免目录迁移与资源导入）；编辑器从工程根读取。
  4. 清单保存元数据与 CLI 命令；C# handler 表保存行为。二者用 EditMode 对账测试强制 1:1，不能把委托序列化进 JSON。
  5. 单一 CLI 用 Python 实现（JSON、校验、测试成本低；仓库已有 Python 工具链），pwsh 脚本继续作为执行后端；CI/构建的既有直调入口保留。
  6. 生成文档落在 `Docs/工具链/工具索引.md`；设计裁决落在 `Docs/设计文档/架构/开发工具链平台专项设计.md`。
  7. 本批顺带修两个与入口可信度直接相关的小问题：L2 `eval_file --file` 参数、夜间任务注册；不扩散到 L2 全量重构、绑定生成器覆写、SimTrig 写盘契约等已登记债。
- 假设影响与风险：
  - 旧菜单移除会短期改变肌肉记忆；Hub 必须先在真实编辑器里完成冒烟，回滚只需恢复菜单特性。
  - 清单在 Assets 外，Unity 不会自动导入其变更；Hub 提供“重新加载清单”入口，测试直接读文件。
  - 编辑器程序集需要新增对 `LiteSim.Core.Editor`、`DisciplineScanner.Editor` 的引用；必须确认无环并跑 L2 编译。

## 范围与非目标

- 范围内：
  1. 清单 v2：`schemaVersion`、`categories`、`tools`、`surfaces`；迁移现有 `generators`/`diagnostics` 元数据；Python 校验器与单元测试。
  2. `scripts/toolchain.py`：统一 CLI facade、`check`、`docs`、`open`、`run`、`generate`、`verify`、`build`、`doctor`。
  3. 生成 `Docs/工具链/工具索引.md`（分类/工具/入口/写入面/文档/注册面），支持 `--check`。
  4. 编辑器 Hub：`LiteGame/工具链`、搜索/分类/文档/报告；`EditorToolDescriptor` 目录；`IEditorToolPage` 页件契约；GM 六页嵌入；窗口/动作/CLI 三类适配；清单错误页。
  5. 顶层游戏菜单收口：`LiteGame/UI|Lua|Resource|测试|地图`、`LiteSim/*`、`Tools/纪律扫描` 退场；保留 BindRoot/资源右键上下文菜单与 `LiteCodeGen` 包级菜单；Lua 两个快捷键保留在 `LiteGame/工具链/快捷` 下。
  6. 注册面：计划任务、CI 脚本引用、hook 配置、清单路径检查；`install-nightly-task.ps1`；`restore-check` 读取清单版本并汇报注册面。
  7. 文档与质量：新专项设计、生成索引、AGENTS/scripts README/代码地图/技术支持/修改指南/directory-move-sync/审计表回写；施工进度记录。
  8. 测试：Python 单测、L1、Doc、Unity 编译、定向 EditMode、全量 L2（若夜间窗口允许）、BuildHash `--check-shared` 不变量。
- 非目标：
  - 不重写 UI 绑定/编排/回写窗口的内部；它们本批只作为 Hub 的 launch 目标。
  - 不改烘焙、校验、扫描、GM 诊断的业务算法；只做入口、生命周期与契约适配。
  - 不改 BuildHash/协议/Luban 语义；不触碰 `Assets/LiteSim/Core/{Scripts,Systems}`、`Assets/LiteNet/{Proto,Protocol,Transport/Security}`。
  - 不重构 L2 测试执行链、CI job 矩阵、build-player 步骤；只修 `eval_file --file` 与注册面检查。
  - 不修已登记但与本批入口无关的债：`BindGenerator` 覆盖手写主文件、`SimTrigTableGenerator` 无条件写盘、`refresh_hash` 探测失败返回 0、`GmLocalChainPage` 后台同步等；在方案风险与后续建议中保留。
  - 不引入外部工具/依赖；Python 只用标准库。

## 现状与约束

- 相关代码/模块：
  - 编辑器：`Assets/LiteGame/Editor/`（Baking、Gm、UiTools、Style、Resource、DevReload、LuaDevRefill、GenConfig）、`Assets/LiteCodeGen/Editor/`、`Assets/LiteSim/Core/Editor/`、`Assets/Tools/DisciplineScanner/Scripts/Editor/`。
  - 脚本：`scripts/codegen/toolbox.py` 与 `_codegen.py`；`scripts/gate/{test,l0-dep-scan,l2-unity-gate,nightly-gate,restore-check}.ps1`；`scripts/build/`；`scripts/content/`；`scripts/local/`。
  - 清单与文档：`scripts/gate/toolchain.json`、`Docs/工具链/技术支持.md`、`Docs/开发导航/代码地图.md`、`scripts/README.md`、`AGENTS.md`。
  - 测试：`Assets/Tests/EditMode/`（L2）、`Tests/`（L1/L3）、`Assets/LiteTesting/Editor/`。
- 约束：
  - `Assets/` 下新建/修改 `.cs` 可直接改文本，但新文件必须经 unity-pipeline 导入生成 `.meta`；删除/移动 Assets 下文件必须走 unity-pipeline。
  - `LiteGame.Editor` 当前不引用 `LiteSim.Core.Editor` / `DisciplineScanner.Editor`；要嵌入其动作需显式加 asmdef 引用（无环，顶层编辑器程序集依赖下层编辑器程序集）。
  - `IGmPage` 是 internal；新增 public `IEditorToolPage` 并让 `IGmPage : IEditorToolPage`，避免复制生命周期契约。
  - `UiBindingWindow.Open`、`UiComposeWindow.Open`、`BakingToolbox.Open` 等为 private；需改为 public 或加公开入口。
  - PowerShell 5.1 对无 BOM 中文脚本会按 GBK 误解析；新脚本保持 ASCII-only，夜间任务改由 pwsh 启动。
  - 当前工作区有并行线的未提交改动；本批只碰清单/工具入口相关文件，提交前用 `git status` 核对，不覆盖他人改动。
  - `toolchain.json` 在 Assets 外，Unity 不会自动导入；Hub 以工程根 + `scripts/gate/toolchain.json` 读取，失败时显示可操作错误页。
  - 本批不触发 BuildHash 源集；收尾仍跑 `gen-build-hash.py --check-shared` 作为不变量检查。

## 候选方案与取舍

| 方案 | 核心思路 | 优点 | 代价/风险 | 结论 |
|---|---|---|---|---|
| A. 只做 UI 入口 | 新建一个 Hub，把现有窗口/菜单挂进去；不做清单与 CLI | 改动最小，见效快 | 清单/文档/注册面仍多源；Agent 仍需记脚本路径；不满足“全平台” | 否 |
| B. 清单驱动平台（推荐） | `toolchain.json` 单一清单；Hub + Python CLI + 生成文档 + 注册面检查共用；C# handler 表用测试对账 | 一处登记、多面消费；新增工具成本最低；可测试、可巡检 | 需要一次清单迁移与 C# 目录层；旧菜单需收口 | **采用** |
| C. 代码生成式 Hub | 从清单生成菜单/Hub C# 代码与 handler 骨架 | 理论上更少手写 | 生成器复杂、调试困难；handler 行为仍需手写；过度设计 | 否 |
| D. 只做 CLI | 统一 `toolchain.py`，编辑器菜单不动 | 对 Agent 友好 | 人侧仍分散；不解决用户提出的入口问题 | 否 |

推荐方案与理由：采用 B。它把“发现、执行、校验、文档”四处消费统一到一份清单，同时保留现有工具实现；Hub 与 CLI 各自做薄适配，不引入生成器复杂度。页件采用混合迁移：GM 六页直接嵌入（已有 `IGmPage`），烘焙与 UI 窗口作为 launch 目标，避免为了统一而重写高风险 UI。

## 设计要点

### 1. 清单 v2（`scripts/gate/toolchain.json`）

新增字段：

```jsonc
{
  "schemaVersion": 1,
  "categories": [
    { "id": "baking", "title": "内容烘焙", "order": 10 }
  ],
  "tools": [
    {
      "id": "baking.toolbox",
      "title": "烘焙工具箱",
      "category": "baking",
      "kind": "window",              // page | window | action | cli | gate | build | content | docs
      "handler": "baking.toolbox",   // page/window/action 必填
      "summary": "身位/枪口/持枪/爆头/根位移/地图的统一烘焙入口",
      "docs": "Docs/设计文档/游戏玩法/烘焙工具箱专项设计.md",
      "menu": "LiteGame/烘焙工具箱", // 旧路径，仅用于兼容/生成文档；可空
      "writes": ["generated"],       // none|source|generated|asset|distribution
      "destructive": false,
      "order": 10
    }
  ],
  "surfaces": [
    { "id": "ci", "kind": "ci-workflow", "path": ".github/workflows/ci.yml", "required": true },
    { "id": "nightly-task", "kind": "scheduled-task", "name": "LiteGame-Nightly-Gate", "script": "scripts/gate/nightly-gate.ps1", "runner": "pwsh", "required": false },
    { "id": "zcode-hook", "kind": "hook-config", "path": ".zcode/config.json", "required": true },
    { "id": "claude-hook", "kind": "hook-config", "path": ".claude/settings.json", "required": true }
  ]
}
```

迁移规则：
- 原 `generators` 节拆成 `tools` 中 `kind: cli` 的条目；`diagnostics.roomReplay` 同样迁移为 CLI 条目；不保留第二份生成器列表。
- `tools[].command` 是数组，默认调用；`toolchain.py run <id> -- ...` 在其后追加参数。
- `kind` 只允许枚举值；`page/window/action` 必须有 `handler`，`cli/gate/build/content/docs` 必须有 `command`（docs 可为空命令的说明型条目）。
- `category` 必须存在于 `categories`；`id`、`handler`、`menu` 全局唯一。
- `writes` 与 `destructive` 决定 Hub 的执行确认和文档标注。

### 2. Python 单一 CLI（`scripts/toolchain.py`）

命令面：

```text
python scripts/toolchain.py                    # 无参 = list
python scripts/toolchain.py list [--json]
python scripts/toolchain.py check [--json] [--strict]
python scripts/toolchain.py docs [--check]
python scripts/toolchain.py open <editor-id>
python scripts/toolchain.py run <tool-id> [-- args...]
python scripts/toolchain.py generate <build-hash|proto|luban|sync-code|refresh-hash> [args...]
python scripts/toolchain.py verify --lane <L0|L1|L2|L3|Doc|All> [--profile ...]
python scripts/toolchain.py build [--profile ...]
python scripts/toolchain.py doctor [--strict] [--fix-nightly]
```

行为：
- `check`：清单 schema、唯一性、分类、docs/命令脚本路径、CI 中 `scripts/**.ps1|py` 引用存在性、hook 配置中引用的脚本存在性、生成文档新鲜度；可选 surface（计划任务）默认 warning，`--strict` 升级为 error。
- `docs`：从清单生成 `Docs/工具链/工具索引.md`，固定头 + LF + write-if-changed；`--check` 只比较不写。
- `open`：读 `Library/Pipeline/.unity-pipeline-port`；写 `Temp/toolchain-open.cs`（`LiteGame.Toolchain.LiteGameToolboxWindow.Open("<id>");`）；调用 `unity command eval_file --file Temp/toolchain-open.cs`；编辑器未运行/ID 非编辑器工具时给出可操作错误。
- `run`：仅执行 `command` 类工具；`page/window/action` 引导到 `open`；保留子进程退出码。
- `verify`：L0 → `l0-dep-scan.ps1`；L1/L2/L3/All → `test.ps1`；Doc → `test.ps1 -Lane Doc`。
- `build`：委托 `build-player.ps1`。
- `doctor`：`check` + `restore-check.ps1` + 计划任务/pipeline 状态；`--fix-nightly` 调 `install-nightly-task.ps1`（需显式确认，不静默改机器状态）。
- 失败契约：一行 `[toolchain] FAILED: ...` + exit 1（沿用 `_codegen.py` 的失败形态）；参数错误 exit 2；未找到工具 exit 4；编辑器未运行 exit 5。

### 3. 编辑器 Hub（`Assets/LiteGame/Editor/Toolchain/`）

- `ToolchainManifest.cs`：`JsonUtility` 可反序列化的清单模型 + 工程根定位 + 读取/错误收集。
- `EditorToolDescriptor.cs`：清单条目 + C# 行为（`CreatePage` / `OpenWindow` / `Execute`）。
- `EditorToolRegistry.cs`：handler id → 工厂/动作；显式注册，不反射。
- `IEditorToolPage.cs`：`Title / OnShow / OnHide / Draw / Poll`；`EditorToolPageBase` 提供空实现。
- `ToolPages/`：`ActionToolPage`（按钮 + 统一报告）、`WindowLauncherPage`（摘要 + 打开原窗口）、`GmPageAdapter`（宿主 `IGmPage`）、`ManifestErrorPage`（清单不可读时的可操作错误页）。
- `LiteGameToolboxWindow.cs`：
  - 单根菜单 `LiteGame/工具链`；`Open()` / `Open(string id)` 公开静态入口（CLI 深链）。
  - 左侧：搜索框 + 分类列表（清单 `categories` 排序）；右侧：选中工具标题、摘要、标签/写入面/文档链接、内容区、报告区。
  - `page` 工具走生命周期与 0.5s 轮询（仅活动页）；`window` 工具显示打开按钮；`action` 工具执行前按 `destructive/writes` 弹确认；`cli/gate/build/content` 工具显示命令与“复制命令”，不阻塞编辑器。
  - 清单缺失/非法：不显示空目录，显示 `ManifestErrorPage`（路径 + 错误列表 + 重新加载）。
  - 选中项记忆：`EditorPrefs` 前缀 `LiteGame.Toolchain.*`。

### 4. 页件与 GM/烘焙/UI 边界

- `IGmPage` 改为 `internal interface IGmPage : IEditorToolPage { }`；六页零改动即可被 Hub 宿主。
- GM 六页注册为六个 `page` 工具；原 `GmPanel` 壳退场（删除菜单；若测试/文档无引用则删除文件，走 unity-pipeline）。Hub 接管页签生命周期、轮询与选中记忆。
- 烘焙工具箱保持单窗口（内部已是六页工具箱），注册为 `window` 工具；`BakingToolbox.Open` 改为 public。
- UI 绑定/编排/回写保持窗口，注册为 `window` 工具；`Open` 改为 public。它们不在一批内重写为页件。
- 动作型工具（Lua Reload/Refill、SimTrig、IEEE 基线、纪律扫描、Resource 构建、样式对账等）注册为 `action` 工具；需要上下文的 LiteCodeGen/BindRoot 保留 Inspector/资源右键入口，Hub 只提供说明与文档链接，避免制造“没有选中对象也能点”的假入口。

### 5. 菜单收口

- 唯一顶层游戏根：`LiteGame/工具链`。
- 退场：`LiteGame/烘焙工具箱`、`LiteGame/地图/*`、`LiteGame/Lua/*`、`LiteGame/测试/GM 面板`、`LiteGame/Resource/*`、`LiteGame/UI/*`、`LiteSim/*`、`Tools/纪律扫描`。
- 保留：BindRoot Inspector/资源右键上下文菜单、`LiteCodeGen` 包级菜单（独立可复用包）、以及 `LiteGame/工具链/快捷/Lua Reload %&r` 与 `Refill %&f` 两个快捷键别名。
- 所有退场入口先由 Hub 覆盖并通过冒烟；提交前用 grep 确认没有遗留的顶层菜单（除白名单）。

### 6. 文档生成与注册面

- 生成页：`Docs/工具链/工具索引.md`，头注释声明由 `scripts/toolchain.py docs` 生成；按分类列 ID、工具、类型、入口/命令、写入面、文档；附 surfaces 表。
- `Docs/工具链/技术支持.md` 顶部指向生成索引与统一 CLI；手工排障表保留，不复制完整清单。
- 注册面检查：
  - CI：扫描 `.github/workflows/ci.yml` 中 `scripts/**.ps1|py` 字面量，逐一检查存在。
  - hook：解析 `.zcode/config.json` / `.claude/settings.json`，提取 `scripts/hooks/*` 引用，检查存在。
  - 计划任务：Windows 下 `schtasks /query /tn <name> /xml`，校验 `Command` 含 `pwsh`、`Arguments` 含清单中的脚本路径；不存在/漂移默认 warning，`--strict` 为 error。
  - pipeline 包：作为可选 surface 检查目录存在；不校验 9 处补丁（后续项）。
- `install-nightly-task.ps1`：ASCII-only；从清单读取任务名/脚本/runner；用 pwsh 重新注册，打印 action 与下次运行时间；不自动执行。
- `restore-check.ps1`：版本/修订号改为读 `toolchain.json`；末尾调用 `toolchain.py check`，失败只记 WARN（构建机可能没有本机计划任务/编辑器）。

### 7. 关键流程

```text
用户/Agent
  ├─ LiteGame/工具链 ── Hub ── manifest ── 分类/搜索/页件/窗口/动作
  └─ python scripts/toolchain.py ── list/check/docs/open/run/generate/verify/build/doctor
                                      │
                                      ├─ scripts/codegen/toolbox.py（生成器）
                                      ├─ scripts/gate/*.ps1（门禁）
                                      ├─ scripts/build/*.ps1（构建）
                                      └─ unity command eval_file（编辑器深链）
```

### 8. 失败与边界处理

- 清单不可读/非法：Hub 显示错误页；`check` 非 0；`docs` 拒绝生成；不静默显示空列表。
- handler 缺失：Hub 显示“未注册行为”错误；EditMode 对账测试失败。
- 旧窗口不存在/被删：Hub 显示可操作错误；不抛裸异常。
- 编辑器未运行：`open` 明确报错，不尝试猜测实例。
- 破坏性/写资产动作：执行前确认；文档标注 `destructive/writes`；本批不重写动作内部。
- 计划任务不存在：CI/非作者机 warning；本机 `doctor --strict` 失败并提示安装脚本。
- 清单在 Assets 外：Hub 每次打开/点“重新加载”时读文件；编辑器域重载后自然重读。

## 通用性与扩展性

- 根因/共性问题：入口和元数据没有单一来源，导致菜单、脚本、文档、CI/任务各自维护；不是工具数量不足。
- 复用与扩展点：
  - 新增编辑器工具：清单一条 + `EditorToolRegistry.Register` 一个 handler；页件/窗口/动作三种适配覆盖现有形态。
  - 新增 CLI 工具：清单一条 + `command`；`run` 自动支持，无需改 CLI。
  - 新增分类：`categories` 一条；Hub/生成文档自动跟随。
  - 新增注册面：`surfaces` 一条 + 对应 kind 的检查器；CI/hook/计划任务共用。
- 避免硬编码/特判的约束：
  - 菜单文案、分类、路径、命令、写入面只来自清单；C# handler id 是唯一行为键。
  - Hub 不按工具写 switch；只按 `kind` 走四种通用适配。
  - 生成文档不手写；`docs --check` 防漂移。
  - 对账测试强制清单与 handler 一一对应，防止“清单有、行为无”。

## 测试策略

- 先行红测（Python，可实际先失败）：`scripts/tests/test_toolchain.py`
  - 清单必含 `schemaVersion/categories/tools/surfaces`（实现前断言失败）。
  - `toolchain.py list` 子进程返回 0 且包含已知工具 id（实现前断言失败）。
  - `toolchain.py docs --check` 在未生成索引时返回非 0（实现前断言失败）。
  - `check` 对缺失 CI 脚本/hook 的 fixture 返回非 0；可选计划任务缺失仅 warning。
  - docs 生成幂等（第二次内容不变）。
- C# EditMode（`Assets/Tests/EditMode/EditorToolCatalogEditModeTests.cs`）：
  - 清单可读、分类/ID/handler/menu 唯一。
  - 每个 `page/window/action` 有 handler；每个 handler 有清单条目。
  - 每个 `page` 能创建非 null 页；每个 `window/action` 的委托非 null。
  - `LiteGameToolboxWindow.Open("id")` 能选中目标（不渲染 GUI）。
  - 清单缺失时走错误页而不是空目录。
- 门禁验证：
  - `python -m unittest discover -s scripts/tests -p "test_toolchain*.py" -v`
  - `python scripts/toolchain.py check`
  - `python scripts/toolchain.py docs --check`
  - `pwsh scripts/gate/test.ps1 -Lane L1 -Profile PullRequest`
  - `python scripts/gate/doc-lint.py`
  - Unity 编译 + 定向 EditMode（`run_tests --mode editor --filter EditorToolCatalog`）
  - 全量 L2（若夜间窗口允许；否则记录为待夜间门禁，不宣称已验证）
  - `python scripts/codegen/gen-build-hash.py --check-shared`（不变量）
- 手工冒烟（真实操作路径）：打开 `LiteGame/工具链`；搜索一个工具；打开烘焙窗口；切换到一个 GM 页；执行一个只读动作；复制一条 CLI 命令；关闭重开验证选中记忆；用 `python scripts/toolchain.py open gm.test-mode` 验证 CLI 深链。

## 验收标准

- [ ] AC1：`python scripts/toolchain.py list` 按分类列出清单全部工具；未知 id/未知工具返回非 0 且信息可操作。
- [ ] AC2：`check` 校验 schema、唯一性、分类、文档/命令路径、CI/hook 引用、生成索引新鲜度；仓库当前为 0 错误，可选计划任务缺失为 warning。
- [ ] AC3：`docs --check` 通过；`Docs/工具链/工具索引.md` 由清单生成、幂等、已入索引。
- [ ] AC4：Hub 打开后能看到全部分类与工具；窗口类能打开原窗口；动作类能执行并显示报告；CLI 类显示可复制命令；清单损坏时显示错误页。
- [ ] AC5：GM 六页可从 Hub 进入，`IGmPage : IEditorToolPage`，生命周期/轮询由 Hub 驱动；烘焙/UI 窗口作为 launch 目标可从 Hub 打开。
- [ ] AC6：顶层游戏菜单已收口到 `LiteGame/工具链`（快捷键别名与上下文/包级菜单保留）；grep 无白名单外遗留。
- [ ] AC7：`open <editor-id>` 经 Unity Pipeline 打开 Hub 并选中目标；`run/verify/build/generate` 正确委托并保退出码。
- [ ] AC8：`doctor` 能识别本机夜间任务路径/runner 漂移；`install-nightly-task.ps1` 重注册后检查通过；L2 `eval_file` 使用 `--file`，新鲜度守卫不再降级。
- [ ] AC9：新增 Python 单测全绿；L1 全绿；doc-lint 0；Unity 编译 0 错；定向 EditMode 全绿；全量 L2 已跑或明确记录待夜间；`--check-shared` 不变。
- [ ] AC10：设计文档入索引；AGENTS/scripts README/代码地图/技术支持/修改指南/directory-move-sync/审计表同批回写；施工进度有本章证据。

## 风险与回滚

- 风险：
  1. 菜单收口影响使用习惯；Hub 若有未覆盖工具会“找不到入口”。
  2. 编辑器程序集新增引用导致编译错误/环；需先验证 asmdef 图。
  3. GM 页被 Hub 宿主后生命周期/轮询假设不成立（例如依赖 `GmPanel` 壳的 EditorPrefs 或 `OnEnable` 顺序）。
  4. 清单与 handler 漂移；靠 EditMode 对账测试兜底。
  5. 计划任务注册需要本机权限；脚本写错会影响夜间门禁；先用 `-WhatIf`/打印 action 校验。
  6. 生成文档与手工文档出现第二份清单；`docs --check` + 手工文档只保留排障叙事来约束。
  7. 并行工作区有未提交改动；误碰会导致他线丢失。
- 监控/发现：
  - `toolchain check` 在 L0/CI 与本地运行；`doctor` 在需要时运行。
  - EditMode 对账测试随 L2；Hub 冒烟清单在施工记录中逐项打勾。
  - `git diff --name-only` 对照本批文件清单，发现越界立即回退。
- 回滚：
  - 菜单收口回滚：恢复各 `[MenuItem]` 特性即可（单文件小 diff）。
  - Hub 回滚：删除 `Editor/Toolchain/` 目录与 root 菜单；不影响现有窗口。
  - 清单/CLI 回滚：恢复 `toolchain.json` 旧结构，保留 `scripts/toolchain.py` 不影响既有脚本（无生产调用方时）。
  - 夜间任务回滚：`install-nightly-task.ps1` 可用旧参数重注册；任务本身不在仓库。
