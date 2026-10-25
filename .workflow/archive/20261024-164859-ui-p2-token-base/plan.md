# 方案：UI 系统 P2-3a 令牌源与主题资产（JSON→UiThemeAsset，退役硬编码）

> 运行 `ui-p2-token-base` · 创建于 2026-10-24 · 批次 P2-3a（P2-3 四批中的第一批）

## 背景与目标

- **现状**：UI 令牌值全部是编辑期 C# 常量——`Assets/LiteGame/Editor/Style/UiStyle.cs` 持有 12 个颜色（`Bg/BgDeep/ItemBg/Primary/Accent/Warn/Success/Text/TextDim/TextBright/Raycast/Mark`），`Assets/LiteGame/Editor/Style/UiTextStyle.cs` 持有 5 个文字档（`Title/Body/Caption/LabelSingle/Emphasis`，含字号/对齐/换行/截断/字重）。样式工具 `UiStyleTool`（最近邻匹配 + 阈值 1e-4）与 `UiTextStyleTool` 各自从这两份常量取值做批量应用与对账；校验 `ui.style.color-drift` / `ui.style.text-drift` 复用同一对账单源。
- **要解决的问题**：设计已裁决令牌源为 JSON、生成 `UiThemeAsset`（SO）、工具只读生成物（[工具链专项设计 §8.1/§8.3](Docs/设计文档/客户端/UI系统/UI编辑器工具链专项设计.md)、[总设计 §6.1](Docs/设计文档/客户端/UI系统/UI系统总设计.md)），但**令牌源 JSON、主题资产 SO、生成器、`ui.style.tokens` 工具四件全缺**；值仍散在 C# 里，改一次色板要改代码，且没有"JSON 改了但 SO 没重生成"的守卫。P2-3 的后续三批（引用与应用、预览矩阵、存量收敛）都依赖这份底座。
- **目标（可观察）**：令牌值只有一处可改（JSON）；`UiTheme.asset` 由工具确定性生成并可复算；`UiStyle`/`UiTextStyle` 的硬编码值表退役为读生成物；样式工具与真实面基线（Warning 302＝颜色 161＋未标记文本 141）逐项不变；**不产生任何 prefab 改动**。

## 需求澄清与假设

- **已确认答复**（本轮）：P2-3 按 4 批推进（B1 令牌底座 → B2 引用与应用 → B3 预览矩阵/设备档 → B4 存量收敛与收口），先做 B1；"不要一次性做完"。
- **需要确认的问题**：无阻塞问题。以下 6 条为方案假设，随本方案一并提审，用户可在验收标准确认阶段否决任意一条。
- **采用假设**：
  - **A1 组覆盖（修订）**：B1 只作者**有真实消费者的两组**——`color`（12）与 `typography`（5）；`spacing/radius/elevation/motion/icon` 五组写空 `tokens: {}`（schema 合法）。口径是"**必填字段最小集**"：typography 的 `fontFamily`/`lineHeight` 是 schema 必填、B1 无消费者（`lineHeight` 归 B2 应用器、`fontFamily` 供交接/校验），属**预留字段**，不为它们造工具；其余五组不写任何值。
  - **A6 取值路径与成员形态（修订 3）**：`UiStyle`/`UiTextStyle` 改为**catalog 派生读口**——`UiStyle` 的 12 个旧名与 `UiStyle.Tokens` 由 `static readonly 字段` 改为**只读属性**（getter 每次 `UiTokenCatalog.Load()` 取值、不缓存；工具内部不逐节点调用，只装载一次后下传）；`UiTextStyle` 的 5 个档位静态字段**删除**，`All` 改为只读属性（每次由 catalog 物化新数组），`TryGet(role, out)` 签名不变；`UiTextStyleDef` 的**构造器签名与可见性都不变**（保持 public，零构造器 API 变更）——已 grep `new UiTextStyleDef(` 全仓仅 `UiTextStyle.cs:46/50/54/58/62` 五处且全在本文件内，唯一生产读口仍是 `All`/`TryGet`。措辞口径：**仓库内调用点源码零改动、表达式源兼容**（`UiStyle.Primary`/`UiStyle.Tokens`/`UiTextStyle.All`/`TryGet` 照常编译运行），公开成员形态由字段→只读属性如实登记（非二进制兼容，属源兼容）。样式工具既有公开重载不变，每次操作只 `Load()` 一次后下传；另加"传入 catalog"重载（方法名带 `With`，避免 `null` 字面量歧义），仅测试用；`StyleColorDriftRule`/`StyleTextDriftRule`/`EditorToolHandlers` 的既有调用点**生产代码零改动**（仅重新编译）。
  - **A2 资产面**：B1 **零 prefab 改动**；8bit 量化带来的颜色收敛（见 D1）推迟到 B4 统一执行并留 diff。
  - **A3 ID 冻结**：本批首次冻结令牌 ID（v1.0），此后改名走"迁移映射 + 旧 ID 别名"（总设计 §6.1）。
  - **A4 工具面**：B1 不新增工作台透视（样式透视归 B2、预览透视归 B3）；只登记 CLI/Hub action `ui.style.tokens`。
  - **A5 归属**：`UiThemeAsset` 放运行时程序集 `LiteClient.UI`（纯数据类，不注册容器、无运行期消费者，与 `WidgetSpec` 同型）；生成器与读面放编辑器程序集。
- **假设影响与风险**：A1 使 B2 需要补 spacing/radius 等组的首个消费者时再补值（不返工，只追加数据）；A2 使"值与旧常量等价"对颜色只成立到 8bit 量化（≤0.5/255，不可见）；A5 若将来要求运行期热更主题，SO 需进收集组（B2/B4 处理）。

## 范围与非目标

- **范围内**：
  1. 令牌源 JSON：`Assets/LiteGame/Editor/UiTools/UiStyleTokens.json`（按已冻结 schema 2.0；放编辑器目录——它是设计态源，**不进包**；`Assets/GameData/Config` 是 YooAsset 收集路径 `Assets\BundleCollectorSetting.asset:48`，放那里会把设计源打进 Player）。
  2. 模型 + 校验：`UiThemeTokens` 及令牌类型、`UiThemeTokenValidator`（ID 文法/组归属/唯一性/取值范围/未知字段）。
  3. 规范文本与指纹：`UiThemeTokenJson`（确定序、LF）+ `sourceHash`。
  4. 主题资产：`UiThemeAsset`（SO，`LiteClient.UI`）+ 生成器 `UiThemeAssetGenerator`（确定性、幂等、负例不写盘、路径守卫）。
  5. 生成物落点：`Assets/GameData/Config/UI/UiTheme.asset`（入库）。
  6. 读面与退役：`UiTokenCatalog`（每次操作装载一次、按参数下传；旧名→只读属性，调用点源兼容）+ `UiStyle`/`UiTextStyle` 值表退役（只留非令牌表现档与旧名映射）。
  7. 工具：`ui.style.tokens`（`EditorToolHandlers` + `scripts/gate/toolchain.json` + 工具索引重生成）。
  8. 校验：`ui.style.token-drift`（JSON 与 SO 语义不一致/缺文件/非法）。
  9. 行尾纪律：新增 JSON 落在既有 `.gitattributes` 规则 `Assets/LiteGame/Editor/UiTools/*.json text eol=lf` 覆盖面内（**不新增规则**，只验证 `git check-attr` 生效）。
  10. 文档回写 + 夹具 + 全量门禁。
- **非目标**：
  - 不改任何 prefab、场景、图集、字体资产；**不跑** `ui.style.colors-refresh`（另有 B4）。
  - 不做 `UiStyleRef` 引用组件、`UiStyleResolver`、应用器、样式透视（B2）。
  - 不做设备档/截图矩阵/预览透视（B3）。
  - 不收敛 302 条 Warning（B4）；不登记新例外条目。
  - 不改 `UiStyleTokens.schema.json`（若实施中发现必须扩字段，回方案阶段重审）。
  - 不搬 `Assets/LiteGame/Editor/Style/` 到 `UiTools/`（目录归并另批，按 `directory-move-sync`）。
  - 不碰 BuildHash 源集/Luban 表/协议（红线 1 不触发）。

## 现状与约束

- **相关代码/模块**：
  - `Assets/LiteGame/Editor/Style/{UiStyle,UiStyleTool,UiTextStyle,UiTextStyleTool}.cs` —— 值表与两个工具（**改写面**）。
  - **既有消费者（`grep UiStyle\\.|UiTextStyle\\.` 实测；生产代码调用点零改动，表达式源兼容）**：`Assets/LiteGame/Editor/UiTools/Validation/Rules/StyleColorDriftRule.cs:33`（`UiStyleTool.ReportDrift`）、`StyleTextDriftRule.cs:36-37`（`ReportTextDrift`/`ReportBrokenRefs`）、`Assets/LiteGame/Editor/Toolchain/EditorToolHandlers.cs:181/188/197/203`（`ApplyColors`/`ReportDrift`/`ApplyTextStyles`/`ReportTextDrift`）；夹具 `Assets/Tests/EditMode/UiStyleRuleEditModeTests.cs:78/142`（`UiStyle.Primary`）、`UiTextStyleEditModeTests.cs:42-69/88-210`（`UiTextStyle.All`/`TryGet`）——**两套夹具可能因数值断言的期望值来源改为 catalog 而改动**（只改期望值来源，不降断言强度），计入改写面。
  - **改写面清单（精确）**：`Editor/Style/` 4 个文件 + `EditorToolHandlers.cs`（`RegisterUi` 加 1 个 action）+ `UiValidator.cs`（Rules 数组 +1、样式族 Note 改文）+ `UiRuleIds.cs`（+1 常量）+ `UiTools/Validation/IUiRule.cs`（`UiValidationContext` 增 `ThemeJsonPath`/`ThemeAssetPath` 两个可选参数与属性）+ `scripts/gate/toolchain.json`；测试面 2 套既有样式夹具（可能随动，仅期望值来源）+ 3 套新增夹具。**其余生产文件不动源码。**
  - `Assets/LiteGame/Editor/UiTools/Validation/{UiValidator,UiRuleIds,IUiRule}.cs` —— 规则注册与上下文注入缝。
  - `Assets/LiteGame/Editor/Toolchain/EditorToolHandlers.cs`（`RegisterUi`）、`scripts/gate/toolchain.json` —— 工具登记。
  - `Assets/LiteClient/UI/Style/UiTextStyleRef.cs` —— `UiTextRole` 枚举与文字档标记（角色→令牌 ID 映射的落点）。
  - `Assets/LiteGame/Editor/UiTools/UiDeterministicJson.cs` —— 既有确定性 JSON 口径（Newtonsoft：固定缩进、LF、末尾换行）。
  - 设计：`Docs/设计文档/客户端/UI系统/UiStyleTokens.schema.json`（2.0 冻结）、工具链 §8.1/§8.3、总设计 §6.1/§6.2。
  - 测试：`Assets/Tests/EditMode/{UiStyleRuleEditModeTests,UiTextStyleEditModeTests}.cs`（须保持全绿）。
- **约束**：
  - **红线 5（禁硬编码）**：令牌值单源＝JSON；C# 不得再持有颜色/字号字面量（测试断言的期望值除外）。
  - **红线 6/13**：不新增 public static 可变状态；`UiTokenCatalog` 不做静态缓存（域重载后缓存会读到旧值），每次工具操作只装载一次、按参数下传。
  - **红线 4（Unity 序列化）**：SO 必须在 Editor 内由工具生成，禁手写 `.asset`；生成走 `unity-pipeline` eval。
  - **生成物纪律**：`UiTheme.asset` 禁手改，漂移由 `ui.style.token-drift` 检出；生成确定性（重跑字节一致）。
  - **schema 只读**：JSON 必须满足 `additionalProperties: false`——未知字段一律拒绝，不静默忽略。
  - **资源入库**：令牌源 JSON（编辑器设计源）与 `UiTheme.asset`（生成物）均入库；`Assets/GameData/Config/UI/` 当前只有三张表 `.bytes`，本批新增首个 SO。
  - **工作流前置**：`.workflow/config.json` 原为 8 阶段变体（缺方案评审/验收确认），本批已补 `review-plan`/`confirm-acceptance` 两阶段（本批前置动作，不改脚本）。
  - **质量登记表（audit-debt-check 已查）**：`Assets/LiteGame/Editor/Style/*` 无在案未修的存量债；客户端审计 L109 提到"`UiStyleTool.cs:28` 引用 `UIDemoPage` 路径常量"已不成立于当前代码（`UIDemoPage.cs` 孤儿文件本身不属本批）。
  - **环境**：本机可能同时开着多个 Unity Editor，`unity-tests.py` 需显式 `--project-path`；编辑期命令走 `pwsh`。

## 候选方案与取舍

| 方案 | 核心思路 | 优点 | 代价/风险 | 结论 |
|---|---|---|---|---|
| **A（推荐）** | JSON 为唯一源；生成 SO；工具经 `UiTokenCatalog` **实例**读 SO；`UiStyle`/`UiTextStyle` 值表删除，只留"旧名→ID"映射与非令牌表现档（对齐/换行/截断） | 单一真相；可确定性复算；规则可判"SO 过期/手改"；测试可注入合成目录 | 触及 2 个既有工具与 2 个既有夹具；旧静态读口改为只读属性（调用点源兼容，工具内部走实例） | **采用** |
| B | 保留 C# 常量，JSON/SO 作镜像，两边靠测试比对 | 改动最小 | 双源必然漂移；违反红线 5；"改一次色板"目标落空 | 拒绝 |
| C | 不生成 SO，直接把 JSON 当 `TextAsset` 由工具/运行期解析 | 少一次生成 | 与设计 §8.3「生成 `UiThemeAsset`（SO）」冲突；运行期解析开销与类型安全都更差 | 拒绝 |

- **子决策 D1（颜色精度，修订 2）**：schema 的 `colorValue` 只允许 `#RRGGBB`/`#RRGGBBAA`，无法表达 `0.12f` 这类浮点。选 **8bit 量化**（`Color32` 存 SO），逐通道误差 ≤0.5/255（不可见）。**"对账判定不变"不靠算术断言，而靠既有真实面基线钉直接判定**：`UiValidationEditModeTests` 今天就把 302/161/141 钉住（P1 收口时实测并入库），B1 改完必须仍绿——若量化导致翻转，这条既有钉会直接红，本批停工提审（不在本批改钉）。替代理性：扩 schema 允许浮点需改已冻结设计，拒绝。
- **子决策 D2（文字档切分）**：typography 令牌只承载 schema 有的字段（字体族/字号/行高/字重/字距）；对齐/换行/溢出策略是**表现档**而非设计令牌，继续留在编辑器常量（`UiTextStyle`），其字号/字重从令牌取。避免把非令牌字段塞进令牌格式。
- **推荐理由**：A 是本批唯一同时满足"单一真相 + 可机检 + 不改 prefab"的形态；B/C 都把问题往后推。

## 设计要点

- **接口/数据**：
  - `UiThemeTokens`：`schemaVersion`、`meta{name,version,theme,description}`、七组 `groups`（每组 `tokens` 字典：ID→令牌对象）。令牌对象按 schema：color `{value,description?}`；typography `{fontFamily,fontSize,lineHeight,weight,letterSpacing?,description?}`；number `{value,description?}`；elevation `{color,offsetX,offsetY,blur,spread,description?}`；motion `{durationMs,easing?,description?}`。
  - `UiThemeAsset`（`Assets/LiteClient/UI/Style/UiThemeAsset.cs`）：`schemaVersion`、`theme`、`version`、`sourceHash` + 七组数组（`UiThemeColorEntry{id,Color32 value,description}` 等）；数组按 ID `Ordinal` 排序；无时间戳。
  - 路径单源 `UiThemePaths`：`DefaultJsonPath = "Assets/LiteGame/Editor/UiTools/UiStyleTokens.json"`（设计源、不进包）、`DefaultAssetPath = "Assets/GameData/Config/UI/UiTheme.asset"`（生成物、设计 §8.3 指定、随 `Assets/GameData/Config` 收集组进包）。
  - `UiTokenCatalog`：`Load(assetPath?)` → `TryColor(id) / TryTypography(id) / Colors / Typography`；读到缺失或非法资产时显式失败（不返回空集冒充成功）。**取值路径**：工具既有公开重载保留，每次操作 `Load()` 一次后把 catalog 逐层传参；另加 `ApplyColorsWith(catalog, dirs)`/`ReportDriftWith(catalog, dirs)`/`ApplyTextStylesWith(catalog, dirs)`/`ReportTextDriftWith(catalog, dirs)` 等**测试用重载**（独立方法名，避免与 `null` 字面量调用歧义），既有调用点不改。
  - **颜色读口（`UiStyle` 门面，修订）**：12 个旧名（`Bg`…`Mark`）与 `Tokens` 改为只读属性，getter 按上表冻结 ID 走 `UiTokenCatalog.Load()`；`Distance` 保持静态方法。**C# 里不再保留任何颜色字面量**（含默认值/注释示例）；工具内部不走门面（用注入的 catalog 一次装载），门面只服务既有调用点与测试。
  - **文字档物化（修订 3）**：删除 `UiTextStyle` 的 5 个 `static readonly` 档位字段与全部字号字面量；`All` 改为只读属性（每次 `BuildFrom(catalog)` 物化新数组，不缓存），`TryGet(role, out)` 签名与返回类型不变；`UiTextStyleDef` 的 `FontSize`/`Bold` 保留为**物化快照字段**，构造器保持 public（可见性不改，仓库内仅本文件构造）；对齐/换行/溢出仍为表现常量（非令牌）。
  - **令牌 ID ↔ 旧名映射**（单源，落 `UiStyle` 内；`color.bg.screen` 等为冻结 ID）：
    | 旧 C# 名 | 令牌 ID | 旧值（RGBA） | 量化 hex |
    |---|---|---|---|
    | Bg | `color.bg.screen` | 0.12/0.12/0.16/0.95 | `#1F1F29F2` |
    | BgDeep | `color.bg.deep` | 0.08/0.08/0.10/0.90 | `#14141AE6` |
    | ItemBg | `color.bg.item` | 0.18/0.18/0.22/1.00 | `#2E2E38FF` |
    | Primary | `color.brand.primary` | 0.90/0.75/0.25/1.00 | `#E6BF40FF` |
    | Accent | `color.brand.accent` | 0.30/0.70/0.95/1.00 | `#4CB2F2FF` |
    | Warn | `color.state.warn` | 0.90/0.25/0.20/1.00 | `#E64033FF` |
    | Success | `color.state.success` | 0.40/0.85/0.40/1.00 | `#66D966FF` |
    | Text | `color.text.primary` | 0.92/0.92/0.94/1.00 | `#EBEBF0FF` |
    | TextDim | `color.text.dim` | 0.60/0.60/0.65/1.00 | `#9999A6FF` |
    | TextBright | `color.text.bright` | 1.00/1.00/1.00/1.00 | `#FFFFFFFF` |
    | Raycast | `color.effect.raycast` | 1.00/1.00/1.00/0.01 | `#FFFFFF03` |
    | Mark | `color.effect.mark` | 1.00/0.85/0.30/0.25 | `#FFD94C40` |

    （取整规则单源：`Mathf.RoundToInt(channel * 255f)`（Unity 银行家舍入），上表已按此复算；夹具反向校验"JSON hex → Color32 → 与旧常量的逐通道差 ≤ 0.5/255 + 1e-6"与"平方距离 ≤ 1e-4"。）
  - 文字角色 → 令牌：`font.title/body/caption/label/emphasis`，`fontSize` = 24/20/14/20/20，`weight` = Regular/Regular/Regular/Regular/Bold，`lineHeight` = 1.0（schema 必填，暂无消费者），`fontFamily` = `NotoSansSC SDF`（交接/校验用，暂无运行期消费者）。
- **关键流程**（`ui.style.tokens`）：读 JSON → 反序列化（`MissingMemberHandling.Error`）→ 校验（全量收集问题，不首错即停）→ 规范序列化算 `sourceHash` → 读盘上 SO（缺失则新建；存在则逐字段比较）→ 有差异才写（`EditorUtility.SetDirty` + `AssetDatabase.SaveAssets`）→ 返回报告（七组计数、`sourceHash`、`changed`、耗时）。重复执行 `changed=false` 且资产字节不变。
- **漂移规则与注入缝**：`ui.style.token-drift`（Family 样式）从 `UiValidationContext` 取 `ThemeJsonPath`/`ThemeAssetPath`（缺省走 `UiThemePaths`），按 JSON 重算期望值并与盘上 SO 逐组比对（ID/值/`sourceHash`），不一致报 Error、缺文件/非法报 Error。测试用**临时副本**注入路径（复制到 `Assets/TempUiThemeTests/` 后改副本），用例 `finally` 删除临时资产目录（含 `.meta`）并断言真实 JSON/SO 字节未变、`Assets/TempUiThemeTests` 不存在。
- **失败与边界**：
  - JSON 缺失/JSON 非法/校验不过 → 工具返回 Failed 并列出问题（含 JSON 路径/令牌 ID），**零写盘**；SO 不被创建或改写。
  - 资产路径不在 `Assets/` 下或后缀非 `.asset` → 拒绝（与契约/清单同口径）。
  - SO 存在但被手改 → 生成器按 JSON 重算后覆盖并报告 `changed=true`（附差异摘要）；`ui.style.token-drift` 在覆盖前报 Error，提示跑 `ui.style.tokens`。
  - 组内空集 → 合法（B1 五组即空），不报错；未知组/未知字段 → Error。
  - `UiStyle`/`UiTextStyle` 读不到资产（全新检出且未生成）→ 显式异常带路径提示，不静默回退到旧常量（回退＝复活双源）。
- **文档与登记回写**：工具链专项设计 §8.1（令牌源路径、生成口径与"组按消费者落地"口径 v1.0）、§8.3（`UiThemeAsset` 行补路径/生成物说明）；`UiValidator.Families` 样式族 Note 去掉"令牌 SO 管线待建"并写入 `ui.style.token-drift`；`UiRuleIds` 注释；`python scripts/toolchain.py docs` 重生成 `Docs/工具链/工具索引.md`；施工进度 `UI系统线` 新增第 10 章（P2-3a）；审计表若有涉本批文件的存量债则登记/回写（实施前查 `audit-debt-check`）。

## 通用性与扩展性

- **根因**：令牌值没有"设计态源"，工具各自持值。B1 把值从代码搬到数据，并把"读值"收敛到单一目录对象，后续任何令牌消费者（应用器、解析器、校验、预览）都只经 `UiTokenCatalog`/SO 取值。
- **复用与扩展点**：新增令牌组/令牌 = 改 JSON + 扩 SO 数组 + 扩 catalog（不动工具与规则）；新增消费者 = 注入 catalog，不复制值；新增规则 = 用既有 `IUiRule` 端口；新增工具 = `EditorToolHandlers` + `toolchain.json` 既有登记面。
- **避免硬编码/特判**：ID 前缀与组归属由一张表驱动校验；路径常量单源；无 `if (id == "color.bg.screen")` 式分支；颜色值只出现在 JSON，C# 仅测试断言（且由 catalog 派生或显式 hex 断言）。
- **演进**：schema 升版（2.x）时校验器只放行已知版本并在 SO 里带 `schemaVersion`，消费方按版本拒绝过期资产；令牌改名走别名（A3）。

## 测试策略

- **测试层级与关键用例**（EditMode 三套 + 回归）：
  1. `UiThemeTokensEditModeTests`（约 10 例）：合法 JSON 解析并计数（12 色 + 5 文字）；七组必须存在；空组合法；ID 前缀与组不符/ID 重复/颜色格式非法/字号越界/行高越界/未知 weight/未知组/未知字段/meta 缺失 → 逐条错误（断言错误码与位置，不只断言"失败"）；规范文本两次序列化一致、`sourceHash` 稳定。
  2. `UiThemeAssetGeneratorEditModeTests`（约 8 例）：临时 JSON→临时 SO 生成并读回（颜色 `Color32` 精确、文字档字段一致）；二次生成 `changed=false` 且资产字节不变；校验不过零写盘（临时目录无新文件）；路径守卫（`Assets/` 外、非 `.asset` 后缀拒绝）；JSON 缺失报错；手改 SO 后再生成被覆盖并报告。
  3. `UiStyleRetirementEditModeTests`（约 9 例）：12 个旧名→ID 映射齐全且值 = 旧常量量化（≤0.5/255，且 `Distance` ≤1e-4）；`UiStyle.Primary`/`UiTextStyle.All` 等**只读属性**与 catalog 取值一致（同源断言，证明不再有第二份字面量）；5 档字号/字重与旧一致；`UiTokenCatalog` 缺资产时的显式失败；`UiStyleTool.ReportDrift` 对合成 prefab（命中/不命中各一）判定不变；`ui.style.token-drift` 红绿各一（**注入临时 JSON 路径**：改副本不重生成→Error；对临时 SO 重生成后→无 Error，真实文件零触碰）。
  4. **回归**：`UiStyleRuleEditModeTests`（样式两规则）、`UiTextStyleEditModeTests`（五角色断言）、`UiValidationEditModeTests`（真实面 302/161/141 钉——**AC4 的机器判据**）、`EditorToolCatalogEditModeTests`（工具在册/文档存在/产物登记）、`UiToolchainWiringEditModeTests`。
- **施工顺序（先探底再改值）**：动 `UiStyle`/`UiTextStyle` 之前，先单独跑一次 `UiValidationEditModeTests` 记录当前 302/161/141 与颜色距离分布（作为"改前"证据），改完再跑同一条对比——不让量化位移在无基线的情况下静默改变判定。
- **如何先失败后通过**：先落类型/方法签名骨架（显式 `NotImplementedException` 或空返回，保证可编译），再写三套夹具并运行——失败必须是**断言失败**（不是编译错误/环境错误）；`wf verify --red` 记录 `evidence/red.json`；实现后同一命令转绿。
- **运行方式**：`python scripts/gate/unity-tests.py --mode editor --filter <Suite> --project-path <工程根>`；`red_commands` 换成本批三套的 filter 命令（历史 20 条红命令已在 P2-2 之后转为回归，移出 red 面）；全量门禁走 `wf verify`（L1、doc、l0、toolchain-check、docs-check、l2-meta + 三套新夹具 + 既有样式/校验回归）。

## 验收标准

- [ ] **AC1 令牌源唯一（严格）**：`Assets/LiteGame/Editor/UiTools/UiStyleTokens.json` 存在且可被 `ui.style.tokens` 校验通过；含 12 个 `color.*` 与 5 个 `font.*` 令牌，五组空集合法；**连续两次生成，第二次报 `changed=false` 且 `UiTheme.asset` 逐字节不变**（唯一判据，不接受"字段级相等"替代）。
- [ ] **AC2 生成物可读回**：`UiTheme.asset` 能从资产读回主题名/版本/`sourceHash` 与七组令牌计数（12/5/0/0/0/0/0），颜色读回与 JSON 的 8bit 值逐位一致。
- [ ] **AC3 漂移可检出**：只改 JSON（加一个颜色令牌或改一个色值）不重生成，全量校验报 `ui.style.token-drift` **Error** 且文案指出"SO 过期"；跑 `ui.style.tokens` 后该 Error 消失（一条可复现命令链）。
- [ ] **AC4 基线不漂（用今日已绿的既有钉判定）**：改完后 `UiValidationEditModeTests` 仍绿——**Error 0 / Warning 302（颜色 161 + 未标记文本 141）**；`UiStyleRuleEditModeTests` 与 `UiTextStyleEditModeTests` 全绿。**若量化导致翻转使这条既有钉变红：本批停工提审，不在本批改钉/加例外。**
- [ ] **AC5 零资产改动（白名单 + 临时清理）**：`git status --porcelain Assets/` 只出现白名单路径（`UiStyleTokens.json`、`Assets/GameData/Config/UI/UiTheme.asset(.meta)`、`Editor/Style/`、`Editor/UiTools/`、`Editor/Toolchain/`、`Tests/EditMode/`、`LiteClient/UI/Style/`）；临时测试目录（`Assets/TempUiThemeTests/`）已删除（含 `.meta`，`Test-Path` 为假）；**`Assets/ShareResource/` 零变更**。
- [ ] **AC6 工具与门禁在册**：`python scripts/toolchain.py list` 含 `ui.style.tokens`，工具索引已重生成且 `toolchain.py docs --check` 通过；`wf verify` 全绿（含 L1 与三套新夹具），`doc-lint` 0/0。

## 风险与回滚

- **风险**：
  1. **SO 二次保存字节漂移**（Unity YAML 可能写非确定内容）：AC1 要求逐字节不变；若实测首次就漂移，**停工回方案阶段重审**（不在施工中把它改成"字段级相等"蒙混过关），并在 build-log 留实测证据供裁决。
  2. **8bit 量化后误跑 refresh**：会把 161 处颜色改到量化值（≤0.5/255）。控制：B1 不跑该工具；AC4 由既有 302/161/141 钉直接判定；B4 统一执行并留 diff/截图。
  3. **静态读口/缓存复活**：域重载后读到旧值。控制：`UiTokenCatalog` 每次工具操作装载一次、按参数下传，禁止 static 缓存（设计要点已定）。
  4. **手改生成物或忘了重生成**：`ui.style.token-drift` + 工具幂等复算双保险。
  5. **SO 进收集组**：`Assets/GameData/Config` 是 YooAsset 收集路径，`UiTheme.asset`（≤20KB）会进包且 B1 无运行期消费者（B2 起消费）；JSON 放编辑器目录不进包。若用户要求"无消费者资产不进包"，则在 B2 前临时移出收集路径（Unity 移动保 GUID）。
  6. **既有夹具断言随量化值变动**：`UiStyleRuleEditModeTests` 用 `UiStyle.Primary` 造 prefab（值随令牌变，仍应命中）；若断言写死旧浮点需改成"从 catalog 取期望值"——**保持数值断言强度**（仍钉 24/20/14 与 12 色映射），不放宽为"非空即过"。
- **监控/发现**：`ui.style.tokens` 报告（changed/计数/hash）、`ui.style.token-drift` 规则、AC4 的 302/161/141 既有钉、`wf verify` 全量。
- **回滚**：本批改写面＝`Editor/Style/` 4 文件 + `EditorToolHandlers.cs`（+1 action）+ `UiValidator.cs`（Rules +1、样式族 Note）+ `UiRuleIds.cs`（+1 常量）+ `UiTools/Validation/IUiRule.cs`（上下文 +2 字段）+ `scripts/gate/toolchain.json` + 2 套既有夹具（可能随动：仅期望值来源）；新增＝JSON、SO、约 6 个新类、新规则、3 套夹具、文档。`git revert <批次提交>` 可完整回滚；无表/协议/BuildHash/场景影响，无需重跑 `gen-build-hash.py`/`Luban`。若只想暂时停用，删 `UiTheme.asset` 会让工具显式失败（不静默回退），恢复生成即可。
