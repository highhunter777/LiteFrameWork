# UI 工具链

《UI编辑器工具专项设计》落地的施工记录。设计文档只描述最新设计，本文件只保留最新进度。

## 执行顺序（依专项设计 §7 排期 + §5 优先级）

| 步 | 工具 | 状态 | 设计依据 |
|---|---|---|---|
| 1 | **校验核心** | **已交付（2026-10-11）** | §4 |
| 2 | **绑定标记工具（底层两项：词表单源 + 绑定命名规则）+ 全仓命名整改** | **已交付（2026-10-11）** | §2 |
| 3 | 绑定标记工具（窗口/前缀预设 UI/批量重命名/引用点提示/生成物 hash） | 待做 | §2 |
| 4 | 样式工具补齐（`UiTextStyleRef` 文字档 + 对账扩 `Screens/`） | 待做 | §3 |
| 5 | 校验进 CI（生成物 hash 对账 + 门禁接入） | 待做 | §4 + 修改指南 §2.3 |
| 6 | 编排面板（控件库/绑定树/受控属性/校验生成） | 待做 | §5 |
| 7 | UIPatch 调试补丁 | 待做（随 U4 消费者） | §6 |

## 步 1：校验核心（已交付）

**落点**：`Assets/LiteGame/Editor/UiTools/Validation/`

| 件 | 职责 |
|---|---|
| `UiRuleReport.cs` | 报告模型：`UiIssue{ruleId,severity,asset,node,message,fixHint}` + `UiSeverity`（Info/Warning/Error）+ `UiRuleFamily`（含"是否已接入"）+ `UiReport`（收集/汇总/文本渲染，含"待接入族"栏） |
| `IUiRule.cs` | 规则端口：`RuleId`/`Family`/`Check(context)`；`UiValidationContext` **双侧**输入面（`SourceDirs` 源码面 + `AssetPaths` 资产面 + `ProjectRoot`） |
| `UiValidator.cs` | 校验核心：规则注册表 + `Run(context)→UiReport`；六族表 `Families`（未接入族显式登记）；菜单 `LiteGame/UI/校验/校验 UI 资产`；CLI `RunCli`（Error 非零退出）；`BuildDefaultContext`（源码扫描面 + Screens/Widgets 资产） |
| `Rules/RuntimeVisualRule.cs` | **首条规则**（渲染/适配族，`ui.runtime-visual`）：包既有 `VisualConstructionScanner`（源码面），产出统一报告项 |

**关键约束（§4 落地）**：
- 同一核心三入口共用（菜单/CLI/EditMode 测试）——不各写一套；
- 未接入的五个规则族（绑定/资源/文本/行为/性能）在报告里**显式列为"待接入"，不显示为自动通过**；
- 首条规则**只包装** `VisualConstructionScanner`，不改其判定（该扫描器仍由 `VisualSingleSourceEditModeTests` 直接消费）。

**为何首条选它**：视觉单一来源是既有唯一规则（零风险包装即可验证端口形状）；校验核心是其余工具（绑定标记/样式/编排面板/CI）的公共底座——故第一步做核心 + 首条规则。

## 验证证据

| 项 | 结果 |
|---|---|
| Unity recompile | 0 错误 |
| `UiValidationEditModeTests` | **6/6**（正例×2 / 负例×1 / 报告契约×2 / 端口×1） |
| 三入口共用 | 菜单 `LiteGame/UI/校验/校验 UI 资产` 跑通；`RunCli` 静态入口就位（Error→退出码 1）；测试直调核心 |
| L1 | **1594/1594** |
| 报告形状 | 窄上下文实跑：`PASSED=True errors=0`；"待接入规则族"列出 5 族（绑定/资源/文本/行为/性能），"渲染/适配"已接入 |

## 遗留（如实）

- **全量扫描慢**（~60s，扫 4 个源码目录——与 `VisualSingleSource` 同因）；经 unity-pipeline `run_script` 调用会撞其 30s 管道上限，但**菜单/CLI/EditMode 三入口无此限**，属工具调用方式而非校验器问题。后续若需 CI 高频跑，评估增量扫描或缓存；
- 边界：本步**只落地 1 条规则**，其余五族为骨架占位（`Families` 登记）；绑定/资源等资产面规则留后续步。

## 步 2：绑定标记工具·底层两项 + 全仓命名整改（已交付）

### 交付物

| 件 | 落点 |
|---|---|
| 词表单源 | `Assets/LiteGame/Editor/UiTools/Naming/UiNamingWords.cs`——《命名词表与控件清单》**机器可读副本（权威）**：角色词 8 / 控件段 7 / 模板段 20 / `Tmp_` / 语义词 20；§9.2 文法正则**单源**（`GrammarPattern`）；查询口 `TryParse`（按首下划线切分）/ `IsKnownPrefix` / `HasExactlyOneUnderscore` |
| 绑定命名规则 | `Validation/Rules/BindingNamingRule.cs`（`ui.binding-naming`，**资产面**）：节点名过 §9.2 文法（Error）；前缀段属词表（未登记=Warning，§1 允许登记扩展）；`_` 私有件（模板内合法／**页面节点禁**）；**根名**走 `Page_`/`Hud_`/`Tmp_` 判据（存量页面根=Warning，§9.6 待页面批次）；**UGUI 契约件**（Slider fill/handle、ScrollRect viewport/content、Toggle graphic/target）与**模板实例内部件**按身份豁免 |
| 核心接入 | `UiValidator`：绑定族 `implemented=true`；规则注册表加 `BindingNamingRule`；资产面扩 `Hud/` |

### 全仓命名整改（§9.6"随模板批次整形"，本批即该批）

| 面 | 处置 |
|---|---|
| 模板/夹具**根** | 改**文件名** `Tmp_<控件类型>`（Unity 规则"prefab 根名=文件名"）——27 件 Widgets + 6 件 Screens 夹具，走 `AssetDatabase.MoveAsset`（**保 guid**，实例/引用不断链；collector `AddressByFileName` 地址随名自动跟随） |
| 模板**内部件** | 词缀化：Button→`Button_<语义>`（剥 `Btn`）、文本→`Txt_<语义>`、纯实现件→`_<语义>`（`Blocker→_Blocker`、`Frame→_Frame`、`Star0..4→_Star0..4`）；**UGUI 契约件保留原名** |
| **克隆条目模板** | `_Template/_ItemTemplate` → `Clone_Template`/`Clone_Item`（§9.6） |
| 业务节点 | `UIMain/BtnClose→Button_Close`（§9.6 登记债，同批清；`BindName` 同步）；弹药分隔符 `Txt_/`→`Txt_Slash` |
| 夹具实例根 | `Baseline→Cot_Baseline`、`Loading→Loading_Block`、`Toast→Toast_Info`、`VirtualList→VirtualList_Rooms`、`Content→Dialog_Confirm` |
| 代码同步 | `UIDemoPage.Templates[]` 改 `Tmp_*`；`HpBar.cs`/`BattleHudView.cs`/`BattleHudDriver.cs` 文档注释同步 |
| 合计 | **35 个 prefab / 93 处节点改名 + 33 个文件改名**；全树 **0 MISSING 组件** |

### 验证证据

| 项 | 结果 |
|---|---|
| Unity recompile | 0 错误 |
| 规则重跑 | **Error 0 / Warning 1**（仅 `UIMain` 根——§9.6 登记的页面根债，随页面批次；**不冒充通过**） |
| `UiValidationEditModeTests` | **9/9**（增绑定用例：合法名通过／多下划线 Error／自造段 Warning／页面 `_` Error／模板 `_` 合法／端口标识） |
| `VisualSingleSourceEditModeTests` | **8/8**（回归） |
| L1 | **1595/1595** |
| 实例不断链 | `Hud_Battle` 血条实例源正确解析为 `Tmp_HpBar.prefab`；`HpBar` 组件接线有效（`Slider=_Slider`） |
| 执行脚本 | `AgentScripts/ApplyUiRenames.cs`（单树规划+改名，幂等）+ `RenameTemplateFiles.cs`（保 guid 文件改名），未入库 |

### 遗留（如实）

- **`UIMain` 根名**未改（§9.6 明载"牵动流程/表引用，随页面批次裁决"）——规则报 Warning 追踪；
- **`Hud_Battle.prefab` 含制作侧未提交的 sprite 改动**（Synty → 自制 `Icons/`），本批对其只做 `Txt_/`→`Txt_Slash` 一处命名修正，**文件整体未随批提交**（随制作侧 sprite 批一并入）；
- 中途一次**误操作**（首版"盘上树规划 + 加载树按序改名"遍历序失配，弄乱 5 个 prefab）——已 `git checkout HEAD` 全部回滚，改**单一树**方案重跑，0 MISSING 佐证无残留；
- 窗口树/前缀预设 UI/批量重命名交互/引用点提示/生成物 hash 留**步 3**。
