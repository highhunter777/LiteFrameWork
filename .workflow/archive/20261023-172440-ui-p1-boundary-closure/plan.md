# UI 系统 P1 边界收口：五族校验接入 + 契约补全 + 遍历统一

## 背景与目标

P1（P1-0～P1-6，提交 `3bdfe8c9`）收口时登记了待接边界。本批一次性收口：

1. **五族校验待接入**：样式/文本/布局适配/性能/行为（P1-4 只落了结构/契约族，报告显式列「待接入」）。
2. **契约 `owner`/`designRef` 未投影**：页面清单已承载，契约生成不取值。
3. **契约 `events[]` 恒空**：等引用对账产生消费者再填（P1-1 登记）。
4. **UIMain 常驻壳不产契约**：需裁决「维持跳过」或「补壳级契约」。
5. **绑定面遍历两份语义**：`BindSearch`（生成器域）与 `UiBindSurfaceWalker`（hash/契约/校验域）各走一遍（审计 L247）。

另含 P1-4 边界登记的 **结构族只覆盖 `Page_*`**（HUD/UIMain 根契约未冻结，审计 L252）与同域发现：制作规范 §3.1/§3.3 的 MUST 列表与已裁决实况冲突（见「需求澄清」）。

可观察目标：

- `UiValidator.Families` 十族全部 `implemented=true`，各族待接入子项在描述里显式列**不含原因**。
- 真实资产面全量校验**零 Error**；Warning 基线可计量（颜色漂移 109、未标记文本 101——债可见，不在本批整改）。
- 11 份页面契约重生成入库：`owner`/`designRef` 投影、`events[]` 从 Lua 接线点填充、`sourceHash` 更新。
- 生成器域只剩一套遍历语义；绑定生成物与 hash 逐字节/逐位不变。
- `wf verify` 全绿（含新增 EditMode 夹具与首条 PlayMode 行为夹具门禁）。

## 需求澄清与假设

用户裁决（2026-10-23，本批开工前确认）：

1. **结构族覆盖面**：选「改规范对齐实况 + 扩覆盖」——制作规范 §3.1 根契约四件套限定 `Page_*`；`Tmp_*`（Screens）系统面记「Canvas + CanvasGroup + 按需 GraphicRaycaster + **禁 CanvasScaler**（继承缩放，UI-LAYOUT-01）」；§3.3 HUD 根去 GraphicRaycaster、记「子 Canvas 开射线」模式（战斗 HUD 已裁决的输入形态）。结构族规则照新规范扩到 `Hud_*`/`Tmp_*`。
2. **UIMain 壳契约**：选「冻结**无壳契约**」——维持批量生成跳过 + 告警（工具链设计 §5.2.1 已有口径），本批在施工记录记载冻结决策，不改代码。

采用假设（调查实证，写入实现边界）：

- **样式族单源**：项目现行样式 token 单源是编辑器 `UiStyle`（12 色 token + 命中阈值 1e-4）与 `UiTextStyle`（五角色文字档基线），对账逻辑已在 `UiStyleTool.ReportDrift`/`UiTextStyleTool.ReportTextDrift`。样式族规则**复用这两个单源与对账函数**，不建 §8.1 的 JSON→SO 令牌管线（P2/T3 范围），也不复制第二份阈值。
- **文本族表源**：`tbltext`（Key/Zh/En 三列）+ `UiLTextTable` 编辑器读面已存在；规则读真实表，表读失败显式 Error（不伪造空表，沿用清单规则先例）。
- **行为族执行面**：PlayMode 测试基建已存在（`Tests/UI/PlayMode` 8 夹具）；列表重绑/取消/转场输入锁已有真覆盖，本批只补缺口用例。
- **遍历统一的资产面风险**：Screens/Widgets/Hud 全域 **零 `BindRoot`**、零「Clone_ 子树 BindNode / 模板内部件 BindNode」分歧（探针实证）——生成器域改造不影响任何现存生成物；`UiBindManifest.json` 不存在（无记录面）。
- **events[] 语义**：契约只做对账提示（§5.2「事件绑定仍以代码为准」）；Lua 接线点形态＝`self.ui:OnButton("名", function() ... end)` 等受控 API，字面量第一参提取可行；匿名闭包 handler 取首个 `self:方法(` 调用，解析不到记 `"anonymous"`。

## 范围与非目标

### 范围（按任务组）

| 组 | 内容 |
| --- | --- |
| A 规则族接入 | 文本族、样式族、布局/适配族、性能静态族、行为族（PlayMode）；契约族补全（漂移/未消费/越界/文本双写） |
| B 契约生成器补全 | `owner`/`designRef` 从页面清单投影；`events[]` 从 Lua 接线点收集；11 契约重生成入库 |
| C 遍历统一 | 遍历核心下沉 `LiteCodeGen.Editor`；`UiBindSurfaceWalker` 委托核心；`BindSearch` 变「核心遍历 + 生成器域适配」（标识符校验/重名收集/OrderIndex 留在生成器域）；审计 L268 `partial` 关键字顺手修 |
| D 结构族扩覆盖 | 制作规范 §3.1/§3.3 改文（用户裁决 A）；`PageStructureRule` 扩 `Hud_*`/`Tmp_*`（Screens） |
| E 收尾 | `Families` 表翻绿、门禁配置（含 playmode 命令）、审计回写（L247/L248/L250/L252/L268）、施工进度新章、UIMain 冻结记载 |

### 非目标（显式排除，各族描述同口径登记）

- 样式令牌 JSON→SO 管线（§8.1 `UiThemeAsset`）——P2/T3；状态色硬编码规则（Selectable ColorBlock 的 token 映射未裁决）；「令牌不存在」（无令牌引用组件机制）。
- 设备档/布局基线——锚点漂移、超宽屏溢出（P4）；图集基建（无 `*.spriteatlas`，图集依赖规则无判据）；Player 性能预算（§11.6「Player 性能另测」）。
- 行为族「焦点」用例——焦点契约未冻结。
- 命名/绑定族 pending 子项（BindName 同文/同页同词/编号连续性）——与五族边界无依赖，另批。
- 文本族「缺字/fallback 链」——取决于 TMP 字体资产 population mode，施工中实证；Dynamic 模式下静态缺字检查无判据则登记不含。
- WidgetSpec 状态/尺寸语义一致性（审计 L250 维持挂账——词表取值是人工裁决，无机器判据）。
- 210 条 Warning 基线的 prefab 整改——本批只让债可见（SHOULD 级），不改 40+ prefab。
- P2 工作台、起页/登记拆分等全部 P2 范围。

## 现状与约束

- 校验核心：`UiValidator.Run(UiValidationContext)` → 注册规则表 `IUiRule[]`（现行 8 条）；`Families` 十族（九族 + 项目自定义「渲染/适配」）；例外机制 `UiRuleExceptions`（日期到期/通配/降级）已就绪。
- 规则端口：`IUiRule { RuleId, Family, Check(context) }`，上下文可注入路径（表/hash/清单），测试先例齐全（`UiValidationRulesEditModeTests` 8 例、`UiRuleExceptionsEditModeTests` 7 例）。
- 依赖方向约束：`LiteGame.Editor` → `LiteCodeGen.Editor`（asmdef 已引用）——遍历核心**只能下沉** `LiteCodeGen.Editor`，反向不可行。
- 契约生成器：`UiContractPage.Owner/DesignRef` 字段与序列化**已支持**，缺口仅在 `GenerateRegistered`/`UiContractBatch` 构造 `UiPageMetadata` 时不传值；`sourceHash` 已含 `event|` 行（P1-0 冻结口径），填充 events 只改内容不改口径。
- 真实面基线（探针实证）：`Page_*` 根全合规（ScaleWithScreenSize/1920×1080/m0.5、零子缩放器、零 Animator）；`Tmp_*`（Screens）六件＝Canvas+CanvasGroup+三件有射线+全无缩放器；`Hud_Battle`＝Canvas+CanvasScaler+CanvasGroup+无射线（+子 Canvas 例外）+SafeArea 0；颜色漂移 109、未标记文本 101、字号分布 {14/20/22/24/28/32/36/60}。
- 纪律约束：注释零施工痕迹/日期戳（L1 守卫）；`write_file` 前清点文件内类型声明；同文件 replace 串行；门禁命令经 `exec_editor_script + Process.Start`（本机 shell 通道损坏）。

## 候选方案与取舍

| 决策点 | 候选 | 取舍 |
| --- | --- | --- |
| 样式族数据源 | ① 新建独立样式扫描器 ② 复用 `UiStyleTool`/`UiTextStyleTool` 对账函数 | **选 ②**：单源纪律——阈值（1e-4）、token 集、文字档基线已单源在对账函数处，规则只做「对账结果 → UiIssue」投影；避免第二份阈值/遍历 |
| 遍历统一落点 | ① `BindSearch` 引用 LiteGame 侧 walker ② 核心下沉 `LiteCodeGen.Editor`，两侧委托 ③ 复制一份语义 | **选 ②**：① 不可行（程序集循环依赖）；③ 违背「只留一套语义」债务收敛目标；② 零新依赖边、`UiBindSurfaceWalker` 公共 API 不动（零消费者改动） |
| 未消费判定 | ① 仅 `self.ui:*("名"` 调用点 ② bindName 字面量全文匹配 | **选 ②**：实证存在变量中转（`_Begin("Button_QuickEnter",…)` → `SetInteractable(btn,…)`），调用点匹配会误报；全文匹配覆盖中转，动态构造名不识别（如实登记） |
| events[] handler | ① 匿名闭包内首个 `self:方法(` ② 一律记 anonymous | **选 ①**：对账提示语义下尽力解析，解析不到回落 anonymous；不为 Lua 完整语法分析过度建设 |
| 行为族形态 | ① 扩既有夹具 ② 新建 `UiBehaviorPlayModeTests` | **选 ②**：行为族是「族」级新执行面（六例清单），独立夹具便于门禁 filter 与后续按例追加；既有夹具不动 |
| 契约漂移判定 | ① 只比 sourceHash ② 重生成后逐字节 diff | **选 ②**：`UiContractDiff` 已有分类（Breaking/Changed），重生成对账同时覆盖手改检出（§9.3「手改生成物被检出」）与内容漂移；非破坏性 Changed 也报（契约必须新鲜） |

## 设计要点

### 新规则定名与级别（`UiRuleIds` 追加）

| 规则 ID | 族 | 检查 | 级别 |
| --- | --- | --- | --- |
| `ui.text.static-key` | 文本 | `LTextKeyRef.Key` 不在 tbltext；Lua `SetTextKey*` 的 key 不在 tbltext | Error |
| `ui.text.placeholder` | 文本 | `LTextKeyRef` 用于带占位符条目；`SetTextKey` 用带参条目；`SetTextKeyArgs` 参数数 ≠ 条目占位符数 | Error/Warning |
| `ui.text.locale-column` | 文本 | 条目 Zh 空（源语言缺失）；En 空 | Error/Warning |
| `ui.text.rich-text` | 文本 | tbltext 值含白名单外富文本标签（白名单单源：`b/i/color/size`） | Warning |
| `ui.style.color-drift` | 样式 | Graphic 颜色不命中任何 token（复用 `UiStyleTool.ReportDrift`） | Warning |
| `ui.style.text-drift` | 样式 | 未标记文本（Unmarked）/标记漂移（Mismatch）/标记失效（BrokenRef）（复用 `UiTextStyleTool.ReportTextDrift`） | Warning/Warning/Error |
| `ui.layout.scaler` | 布局/适配 | `Page_*` 根 CanvasScaler 参数偏离基线；页面子树第二套 CanvasScaler | Error |
| `ui.performance.raycast-target` | 性能 | 单 prefab raycastTarget=true 计数超阈值 | Warning |
| `ui.performance.layout-depth` | 性能 | LayoutGroup/ContentSizeFitter 嵌套链深度超阈值 | Warning |
| `ui.performance.animator` | 性能 | 页面根挂 Animator（UI-PREFAB-03）；UI 子树任意 Animator | Error/Warning |
| `ui.contract.drift` | 契约 | 登记页契约与「prefab 重生成」不一致（含手改）；缺契约 | Error/Warning |
| `ui.contract.reference` | 契约 | required 控件未被 Lua 消费（未消费）；Lua 调用点名不在契约面（越界） | Warning/Error |
| `ui.contract.text-double-write` | 契约 | `LTextKeyRef` 节点名被 Lua `SetText*/SetTextKey*` 再写 | Error |

- 阈值集中配置：新增 `UiRuleThresholds` 单源（raycastTarget 预算、布局深度上限），不写死在扫描器（§6.3）。
- 结构族扩展（用户裁决 A）：`Page_*` 维持四件套；`Tmp_*`（Screens）＝Canvas+CanvasGroup 必备、GraphicRaycaster 按需、**CanvasScaler 禁止**；`Hud_*`＝Canvas+CanvasScaler+CanvasGroup 必备、GraphicRaycaster 按需（子 Canvas 模式合法）；SafeAreaReceiver 仍仅 `Page_*` 要求。
- 契约生成器：`GenerateRegistered`/`UiContractBatch` 读页面清单，按 prefabPath 匹配投影 `owner`/`designRef`（清单缺条目→null + Warning）；`Collect` 增 `CollectEvents`（Lua 扫描器供数，按 `bindName|event` 稳定排序）；`events[].event` 定名（`onClick`/`onListClick`/`onToggleChanged`/`onSliderChanged`/`onDropdownChanged` 与受控 API 同文）。
- Lua 引用扫描器（新，`UiTools/Reference/`）：API 调用点提取（受控 API 名单单源）+ bindName 字面量全文匹配；供契约生成（events）与契约规则（未消费/越界/双写）共用一个数据面。
- 遍历核心（新，`LiteCodeGen/Editor/EditorTools/BindSurfaceWalk.cs`）：纯遍历与标记（clone 作用域/嵌套 BindRoot/模板边界），`UiBindSurfaceWalker` 与 `BindSearch` 均委托；生成器域适配保留标识符校验（关键字表补 `partial`，审计 L268）/重名收集/OrderIndex。
- 行为族夹具（新，`Tests/UI/PlayMode/UiBehaviorPlayModeTests.cs`）：①复用复位（脏化 CanvasGroup alpha/interactable/blocksRaycasts/anchoredPosition → 关 → 复开全复位，`PrepareForShow` 契约）②语言切换（`ApplyLocale` 后静态文案换语言值且 OnShow 不重跑）③模态射线穿透（模态层页面打开后，底层页面 blocksRaycasts=false 且射线打不到底层按钮，`UIModalStack.RecomputeBlocking` 契约）。

## 通用性与扩展性

- **根因**：五族缺位的根因是「校验核心已立、规则按族分期」——本批按同一 `IUiRule` 端口逐族接入，核心零改动（只追加规则注册与 Families 翻绿）；后续规则（令牌/图集/焦点）同端口追加，无需再动核心。
- **复用点**：样式族直接复用两工具对账函数（规则=投影层）；契约规则与契约生成共用 Lua 扫描器（一个数据面两个消费者）；遍历核心一处实现三个消费域（hash/契约/校验/生成器）。
- **防硬编码**：阈值进 `UiRuleThresholds` 单源；富文本白名单、受控 API 名单、根契约组件清单各一处常量；规则 ID 集中 `UiRuleIds`。
- **不引入假想需求**：不做 Lua 完整语法分析（字面量提取够用且如实登记边界）；不做令牌 SO 管线；行为族只做有冻结契约的三例。

## 测试策略

红测先行（合成输入断言新规则/新能力，负例先红后绿）：

| 夹具 | 用例面 |
| --- | --- |
| `UiTextRuleEditModeTests` | 真实 key 正例/编造 key 负例、占位符形态、语言列缺失、富文本白名单、表读失败显式 Error |
| `UiStyleRuleEditModeTests` | 注入临时目录合成 prefab：非 token 色/未标记文本/漂移/标记失效，级别映射 |
| `UiLayoutPerformanceRuleEditModeTests` | 合成 prefab：scaler 参数偏离、子树第二 scaler、根/子树 Animator、Layout 深度链、raycastTarget 计数 |
| `UiContractReconciliationEditModeTests` | 合成 Lua 源+合成契约：未消费/越界/双写/漂移（含手改检出）、owner/designRef 投影、events 收集与 handler 提取 |
| `BindSearchUnificationEditModeTests` | 合成层级：Clone_ 子树 BindNode 不进 marks、模板内部件不进 marks、嵌套 BindRoot 不深入、根节点不入、重名/关键字（含 `partial`）错误收集、生成文本逐字节不变回归 |
| `UiBehaviorPlayModeTests` | 复用复位/语言切换/模态射线穿透三用例（PlayMode） |
| 真实面校准 | 全量 `UiValidator.Run()` 真实面零 Error；Warning 计数 ≥ 基线（109 颜色 + 101 未标记 + 未消费若干——如实记录实测值） |

门禁：`.workflow/config.json` 追加上述 EditMode filters（commands + red_commands 双列）与 `ui-behavior-playmode`（`unity-tests.py --mode playmode`）；既有 20 命令不动。

## 验收标准

1. `Families` 十族全 `implemented=true`；报告「待接入」栏仅剩已登记原因的子项（令牌 SO/设备档/图集/Player/焦点/状态色）。
2. 真实资产面全量校验零 Error；Warning 基线实测数记录进施工记录。
3. 11 契约重生成入库：`owner=art.ui` 全投影、`designRef` 透传、`Page_Login` events[] 含 `Button_GuestLogin`/`Button_Settings` 接线、`text[]`/`controls[]` 不变、重复生成逐字节一致；`ui.contract-diff` 对基线零破坏性。
4. 遍历统一后 `UiBindingToolsEditModeTests`（hash 回归）全绿、生成文本零变化、`BindSearch` 新语义用例绿。
5. 结构族扩覆盖后真实面零 Error（`Hud_Battle`/`Tmp_*` 按新规范口径合规）；制作规范 §3.1/§3.3 改文与规则同批一致。
6. `wf verify` 全绿（含新增命令）；先红证据 `evidence/red.json` 齐备。
7. 审计登记表：L247/L268 记修复，L248/L250/L252 记口径收敛结果；`UI系统线.md` 新章；UIMain 冻结决策入施工记录。

## 风险与回滚

| 风险 | 缓解 | 回滚 |
| --- | --- | --- |
| 未消费/越界规则误报（动态构造名/模板内部件寻址） | 字面量全文匹配已覆盖变量中转；施工中先在真实 6 Lua 页跑出基线清单，真阳面收绿、误报面降级或例外登记 | 规则级独立文件，可单独摘除注册表行 |
| 遍历统一动 LiteCodeGen 生成器 | 真实面零 BindRoot（无生成物可坏）；合成回归逐字节比对 | `BindSearch` 恢复独立实现（git revert 单文件） |
| 契约 sourceHash 变化引发 diff 误报 | 重生成与规则同批落地，`ui.contract-diff` 自证；`schemaVersion` 不动（events/owner 字段本在 schema 2.0 内） | 契约生成器改动独立可 revert，重生成旧口径 |
| PlayMode 门禁时长/环境抖动 | 单 filter 命令 + timeout 900s；失败按既有「单跑复验」纪律判定 | config.json 单命令摘除不影响其余门禁 |
| 样式 Warning 洪水淹没报告 | SHOULD 级设计口径（可见即可、进质量度量）；报告按族分组渲染已有 | 级别不升 Error；节点级例外机制可减噪 |
| Lua 扫描器误提（字符串巧合） | bindName 全文匹配仅对「该页契约 controls/text 名单」精确匹配，不做泛匹配 | 同规则级摘除 |
