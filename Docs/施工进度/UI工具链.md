# UI 工具链

《UI编辑器工具专项设计》落地的施工记录。设计文档只描述最新设计，本文件只保留最新进度。

## 执行顺序（依专项设计 §7 排期 + §5 优先级）

| 步 | 工具 | 状态 | 设计依据 |
|---|---|---|---|
| 1 | **校验核心** | **已交付（2026-10-11）** | §4 |
| 2 | **绑定标记工具（底层两项：词表单源 + 绑定命名规则）+ 全仓命名整改** | **已交付（2026-10-11）** | §2 |
| 3 | **绑定标记工具（窗口/前缀预设 UI/批量重命名/引用点提示/生成物 hash）** | **已交付（2026-10-12）** | §2 |
| 4 | 样式工具补齐（`UiTextStyleRef` 文字档 + 对账扩 `Screens/`） | 待做 | §3 |
| 5 | 校验进 CI（生成物 hash 对账 + 门禁接入 + **资源族规则「表内 prefab 路径必须存在」**——步 2 改名漏跟表引用即该族缺席所致） | 待做 | §4 + 修改指南 §2.3 |
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

- **表/代码的按路径引用未跟改名**——**已修（2026-10-12 表路径前置批，见下节）**：文件改名保 guid 不断序列化链，按路径消费面（表 201-206、探针、夹具）已全量同步；且清扫面**超出本登记**——`ListPlayModeTests`、`SafeAreaEditModeTests`（路径＋两处节点名断言）、`UiU0EditModeTests`（节点查找）、`FlyTextPoolEditModeTests`（注释口径）同为漏网者（"审计面≠消费者面"第 N 次实锤）。"表内资源路径存在性"门禁仍留步 5；
- **`UIMain` 根名**未改（§9.6 明载"牵动流程/表引用，随页面批次裁决"）——规则报 Warning 追踪；
- **`Hud_Battle.prefab` 含制作侧未提交的 sprite 改动**（Synty → 自制 `Icons/`），本批对其只做 `Txt_/`→`Txt_Slash` 一处命名修正，**文件整体未随批提交**（随制作侧 sprite 批一并入）；
- 中途一次**误操作**（首版"盘上树规划 + 加载树按序改名"遍历序失配，弄乱 5 个 prefab）——已 `git checkout HEAD` 全部回滚，改**单一树**方案重跑，0 MISSING 佐证无残留；
- 窗口树/前缀预设 UI/批量重命名交互/引用点提示/生成物 hash——已随**步 3** 交付（见下）。

## uiform 表路径悬空修复（2026-10-12，步 3 前置批）

表源 201-206 六行 → `Tmp_*` → `toolbox.py luban` 重生成（bytes+lua 发布）→ `gen-build-hash.py` 重跑（shared+meta 双端）→ 按路径引用全量同步（探针 + 4 夹具 + 清扫出的 4 处漏网，清单见上节勘误后的登记项）。

**另修一桩双 LoadingMask**：`Screens/Tmp_Loading.prefab` 自 W1 起同时带壳根本地 mask+视觉与嵌套模板实例 mask——服务写入面 `GetComponentInChildren` 命中壳根、测试读取面 `FindObjectOfType` 命中嵌套，文案永远对不上（被路径红掩盖，PlayMode 首跑暴露）。按《通用加载页设计》§2 清壳：根只留 Canvas+CanvasGroup+嵌套实例（`Loading_Block` 单一 mask），W1 错置在壳上的本地视觉一并退场（批 A 手工制作时在模板本体重建）。

**验证**：L2 PlayMode 全量 **37/40**——本批涉及面全绿（FeedbackSurface 3/3〔含双 mask 修复〕、ListPlayMode 3/3、Navigation 4/4、UiState 3/3、租约组中非 Test.unity 依赖项全绿）；3 红＝Test.unity 既有债（见步 3 遗留）。EditMode 428 中改名跟随面全绿（SafeArea/FlyTextPool/UiU0/UiValidation）。

## 步 3：绑定标记工具（已交付）

**落点**：`Assets/LiteGame/Editor/UiTools/Binding/`

| 件 | 职责 |
|---|---|
| `UiBindingRename.cs` | 命名引擎：树构建（页面/列表项〔Clone_ 子树〕作用域分列）、前缀组合（§9.5 词表下拉+仅填语义名，构造性过 §9.2 文法）、改名计划（节点名与 BindName **同批同写**——§9.1 同文；空 BindName 模板标记不发明索引名；漂移面按新名收口）、计划校验（文法+同批重名+现存兄弟占用，应用前挡）、应用（**深度序**——父改名不踩子路径） |
| `UiBindingReferenceScan.cs` | 引用点扫描：改名联动的 Lua/C# 字符串字面量清单（只提示不代改——§9.2 联动面是作者责任）；默认扫描根＝Lua/UI/App/Tests，命中带 file:line |
| `UiBindGenerationHash.cs` | 生成物 hash（§2.1.5）：绑定面指纹（BindRoot 配置 + BindSearch 同一遍历语义，嵌套生成根独立）＋清单（Editor 域 JSON，键=prefab GUID 改名不断链）＋对账（NotRecorded/Stale/GeneratedMissing=Error、OrphanEntry=Warning；GUIDToAssetPath 同 tick 删除缓存滞后→再核磁盘真身）；菜单 `LiteGame/UI/绑定/生成物 hash/记录（选中/全部）` |
| `Validation/Rules/UiBindingHashRule.cs` | `ui.binding-hash`（绑定族）：hash 对账进**校验核心三入口**（菜单/CLI/EditMode 共用）——CI 走既有 `UiValidator.RunCli`，不另起旁路；**记录是作者动作**，规则只判漂移不代记录（未记录/漂移显红，不冒充通过） |
| `UiBindingWindow.cs` | 窗口（菜单 `LiteGame/UI/绑定/绑定标记工具`）：树展示（BindName/类型徽标、**同文漂移显形**、Clone_ 作用域标记、生成根标记）、前缀预设（回显解析现名→前缀下拉+语义输入→预览所选）、批量（换前缀保留语义〔自造段可治愈到词表段〕／同文修复 BindName:=节点名）、**预览先行**（计划表+引用点清单+错误挡板）、生成物 hash 面板（状态+记录+校验） |
| `IUiRule.cs`（扩展） | `UiValidationContext.BindManifestPath`——清单注入缝（测试用临时清单隔离全局清单状态） |

**写入通道**（§1"撤销用 Prefab Stage；资产写入经 Pipeline API"）：目标 prefab 开 Prefab Stage → 编辑 Stage 实例**全程 Unity Undo**（保存归 Stage）；未开 Stage → `LoadPrefabContents` 改后 `SaveAsPrefabAsset`（资产文件写无编辑器级 Undo——预览先行兜底；选择/勾选变动即作废旧预览）。

### 验证证据

| 项 | 结果 |
|---|---|
| Unity recompile | 0 错误 |
| `UiBindingToolsEditModeTests` | **12/12**：组合文法／计划同文三形态（同步·空不发明·漂移收口）／批量换前缀（含自造段治愈、语义非法跳过）／重名挡应用前／父子同批深度序落位／引用扫描（cs+lua 合成件、真实 `UIMain.lua` 命中）／hash 同内容稳定+绑定面敏感+嵌套根独立／记录-校验-漂移-重记录-生成物缺失闭环（经校验核心规则面）／孤儿条目／规则端口／窗口绑定建树冒烟 |
| 全量 EditMode | **417/428**——新增 12 全绿；11 红为本批前既有（见遗留），本批零新增 |
| L1 门禁 | `test.ps1 -Lane L1 -Profile PullRequest` **[PASS] 1636**（LiteSim 343 · LiteNet 543 · LiteTesting 7 · LiteFramework 715〔**纪律扫描在内，新文件零违规**〕· MetaServer 139〔PR 过滤口径；直跑全量另得 160/160〕） |
| 真实资产规则重跑 | `PASSED=True errors=0 warnings=1`——唯一 Warning 即 §9.6 的 UIMain 根名债（页面批次追踪中）；hash 规则零问题（现役 BindRoot=0 的诚实空态） |
| PlayMode | 步 3 批面 37/40（3 红为 Test.unity 既有债）；**2026-10-13 收口批后全量 40/40**（见遗留节清账） |

### 遗留（如实）

- **窗口交互面属手测**：IMGUI 壳无自动化覆盖（冒烟用例只盖绑定/建树逻辑）；真实改名的走查随制作侧使用反馈，逐步 6 编排面板批一并打磨；
- **预设面落点＝工具窗口**（非 BindNode 组件 Inspector）——保 LiteCodeGen 零 LiteGame 依赖（词表单源不外泄）；《制作规范》§9.5 工具现状行已随批改口径；
- **同文修复批操作未对现役漂移点执行**：`UIMain` 的 BtnClose/Label 漂移收口归**页面批次**（须连 Lua `OnButton("BtnClose")` 同批改——工具只出引用点清单不代改）；
- **步 2 记录勘误**：步 2 曾登记「UIMain/BtnClose→Button_Close，BindName 同步」——与真身不符，**BindName 未同步**（§9.6 表行已勘误；功能链自洽，属同文漂移非断链）；
- **既有红——已清零（2026-10-13 收口批，EditMode 428/428 · PlayMode 40/40）**：
  - `Test.unity` 经用户改名 `Boot.unity`：**场景入库**（gitignore 例外移除）+ 全仓消费者改指（两条 PlayMode 用例、`BuildProfiles` 双端构建清单〔构建现场断链同批修复〕、三处活文档）——"删共享资产前全仓消费者清扫"的实锤案例；
  - `LTextTypewriter`×7＝**组件实缺非用例错**：截点表缺 0 哨兵（起播偏一显示两字符）、富文本截点未配平（`<color=red>红` 留未闭合开标签）、完成晚一拍（须越过末截点才收）、生命周期非 ExecuteAlways（隐藏取消/待播激活语义 EditMode 不触发）——修组件；另加守卫：无任务不落文本（编辑器装载/卸载 prefab 不碰资产文本）；
  - `BattleLaser`×3＝**用例字面量手推口径错**：命中柱半径 `HitscanRadius=0.45`（注释误写视觉身位 BodyBake 0.32——近弧差 0.13 是 z 漂移主因）+ 全精度烘焙常量斜率；按 SimRaycast 同式复算逐位对账后重校（判定几何权威守卫在 L1 `AimParallaxHitTests`，本组用例职责＝View 层端点映射）；
  - `BattleHud.弹药`＝用例笔误：未装备约定 `SelectedWeapon=-1` 须显式置位（C# int 默认 0＝装备槽 0 非未装备）；
  - 遗留一条**弱断言债**：`租约_取到即被释放的竞态` 只断言异常类型——"已释放"与"未初始化"两分支同过（级联根因已随场景修复消失，断言强度债仍在）；
- **生成物 hash 首个真实消费者待路径 B 启用**（BindRoot 现役 0）：记录/校验链路已就绪，步 5 CI 接线直接可用；「表内 prefab 路径必须存在」资源族规则仍留步 5。
