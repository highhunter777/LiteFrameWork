# UI 工具链

《UI编辑器工具专项设计》落地的施工记录。设计文档只描述最新设计，本文件只保留最新进度。

## 执行顺序（依专项设计 §7 排期 + §5 优先级）

| 步 | 工具 | 状态 | 设计依据 |
|---|---|---|---|
| 1 | **校验核心** | **已交付（2026-10-11）** | §4 |
| 2 | **绑定标记工具（底层两项：词表单源 + 绑定命名规则）+ 全仓命名整改** | **已交付（2026-10-11）** | §2 |
| 3 | **绑定标记工具（窗口/前缀预设 UI/批量重命名/引用点提示/生成物 hash）** | **已交付（2026-10-12）** | §2 |
| 4 | **样式工具补齐（`UiTextStyleRef` 文字档 + 对账扩 `Screens/`）** | **已交付（2026-10-13）** | §3 |
| 5 | **校验进 CI（生成物 hash 对账 + 门禁接入 + 资源族规则「表内 prefab 路径必须存在」）** | **已交付（2026-10-13）** | §4 + 修改指南 §2.3 |
| 6 | **编排面板（宿主窗口 + 绑定暴露/受控属性/校验·生成/控件库四分区；Odin 呈现层）** | **已交付（2026-10-15）** | §5 |
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
- **生成物 hash 首个真实消费者待路径 B 启用**（BindRoot 现役 0）：记录/校验链路已就绪，步 5 已随门禁测试接入夜门；「表内 prefab 路径必须存在」资源族规则已随步 5 交付（见下）。

## 步 4：样式工具补齐（已交付）

**落点**：`Assets/LiteGame/UI/Style/`（运行时标记）+ `Assets/LiteGame/Editor/Style/`（档位单源与工具）

| 件 | 职责 |
|---|---|
| `UI/Style/UiTextStyleRef.cs` | 运行时**声明标记**（自有组件无 Odin）：`UiTextRole` 五角色（Title/Body/Caption/LabelSingle/Emphasis）；只声明不驱动——工具按声明应用/对账，运行时按状态改实例合法 |
| `Editor/Style/UiTextStyle.cs` | 文字档**单源**（与 UiStyle 颜色 token 同构分工——不复制颜色值）：§3.1 基线表 = 字号 24/20/14/20/20、对齐（完整对齐——垂直统一中行）、换行关/关/开/关/开、初始溢出 Truncate/Overflow/Overflow/Truncate/Overflow、Emphasis 唯一加粗档；TMP/UGUI 双映射 |
| `Editor/Style/UiTextStyleTool.cs` | **应用**（按标记精确写入，逐项比对跳过→幂等；失效标记/未知角色不猜不改）+ **对账**（三类：未标记〔整改清单〕/属性漂移〔逐项明细〕/引用失效〔font 缺失 + 标记无文本组件〕）；菜单「应用文字档/对账文字档」；扫描根可注入（测试用） |
| `Editor/Style/UiStyleTool.cs`（改） | 颜色面**扩 Screens**（§3.2 作用域 Widgets+Screens）；扫描根可注入；菜单文案随作用域改「刷新 UI 颜色/对账 UI 颜色」；对账返回结构化清单 |

### 验证证据

| 项 | 结果 |
|---|---|
| Unity recompile | 0 错误 |
| `UiTextStyleEditModeTests` | **8/8**：基线表值逐项核对（§3.1 数字）/ TMP 全属性写入+幂等（含 Italic 位不受字重档干扰）/ UGUI 映射（int 字号/加粗）/ 加粗位只动 Bold / 未标记报告不冒充 / 属性漂移报明细 / 引用失效两类（font 缺失须**非激活节点**——TMP OnEnable 自愈 null font 实证）/ 扫描根注入隔离 |
| 全量 EditMode | **436/436**（428 + 8 新增，零新增红） |
| 真实资产对账首跑基线 | 文字档：**未标记 77 / 属性漂移 0 / 引用失效 0**（标记面从零起步的诚实基线）；颜色（扩面后）：漂移 76 处（既有手调/非 token 色——报告不擅改） |
| L1 门禁 | `test.ps1 -Lane L1 -Profile PullRequest` **[PASS] 1636**（纪律扫描在内，新文件零违规） |

### 遗留（如实）

- **标记面从零起步**：现役 33 个 prefab 的 77 处文本全部未标记（首跑基线）——整改清单随制作侧逐批标记（"未标记先报告，按整改清单收口"，§3.2）；对账/应用对未标记文本零副作用；
- **"声明覆盖"类报告待 SO 覆盖机制**（§3.2 第四类漂移分类）：基线即唯一声明面，合法偏离基线的覆盖声明随换肤/主题需求接 SO 资产（UiStyleAsset 同款先例）；
- **AutoSizing 范围声明**（§3.1"必须声明范围与验证样例"）待首个真实用例接入；
- **UGUI 无水平 Truncate**：不换行档映射 HorizontalOverflow=Overflow、水平截断由容器/Mask 承担（与 TMP Truncate 的视觉等价物是容器职责——口径已在工具注释登记）；
- 字号阶梯是**校验参考**不自动改值（§3.1"不把 22 等数值不看页面就自动改成 24"）——标记+应用是显式动作。

## 步 5：校验进 CI（已交付）

**落点**：`Assets/LiteGame/Editor/UiTools/Validation/`（新规则）+ `UiValidator`（注册与族登记）+ `LiteGame.Editor.asmdef`（补 `Luban.Runtime` 引用——规则装载表产物用 ByteBuf）

| 件 | 职责 |
|---|---|
| `Rules/AssetTablePathRule.cs`（`ui.asset-table-paths`，资源族首条） | **表内资源路径存在性**——装载全部 Luban 表，反射每行公开字符串字段，`.prefab`/`.unity` 结尾即资产路径（无 `Assets/` 前缀按表内惯例补前），盘上不存在＝Error（行定位键随报告）；**通用反射面不枚举列**——未来任何表加路径列即被同一门禁罩住（uiform 悬空两次实锤的机制化，非点位补丁）；表目录整体缺席＝非工程上下文跳过（合成测试根）、单件产物缺失＝Warning；`CheckPaths` 纯核对面供合成负例直喂 |
| `UiValidator`（改） | 资源族 `implemented=true`（"表内路径存在性已接入；其余子项待接入"）+ 规则注册表加 `AssetTablePathRule` |
| 门禁测试 `门禁_真实资产面_命名hash表路径零Error` | **CI 接线本体**：三条资产面规则（命名/hash/表路径）跑真实数据断言零 Error——随 L2 门禁的 EditMode 段自动进夜门（Pipeline 模式 A / batchmode 模式 B 同入口）；视觉面由 `VisualSingleSourceEditModeTests` 既有用例单守（不重复 53s 全量源码扫描）；既有 `RunCli`（`-executeMethod` 非零退出）保留为 CI 批处理备用入口 |

### 验证证据

| 项 | 结果 |
|---|---|
| Unity recompile | 0 错误 |
| `UiValidationEditModeTests` | **14/14**（原 9 + 新 5：规则端口／路径判据不误伤 LuaPath／核对面悬空×2 报 Error 且在盘零误报／**真实表装载提取零悬空**（uiform 修复的回归钉，≥11 行实提取）／门禁真实资产面三规则零 Error＋唯一 Warning＝UIMain 根名债） |
| 既有契约用例随动 | 「资源族不再列待接入」断言翻转、「合成上下文零噪声」由表目录缺席跳过守卫保证（`报告项_五字段齐全` 复绿） |
| 全量 EditMode | **441/441**（436 + 5 新增，零新增红） |
| L1 门禁 | `test.ps1 -Lane L1 -Profile PullRequest` **[PASS] 1636** |

### 遗留（如实）

- **资源族其余子项待接入**（缺引用其余面/收集组/图集字体名册/生产包排除调试补丁——§4 资源族清单），随真实需求逐条接；
- ~~表清单为**显式登记**（九表 `(file, type)` 数组）——新表入 `Tables.cs` 后须同批补 `AssetTablePathRule.Tables` 一行~~ **已修（2026-10-13 表清单单源化，提交 `5e7fe22`；补记见[上云批C收尾补记](上云批C收尾补记.md)）**：清单改 `TableLayout.Files` 派生（`TableName` 裸表名→`cfg` 类名约定解析、失败 fail-fast）、`TableDataDir` 与装载/核对两处拼接口径同批走 `TableLayout.DataDir`/`TableDataPath` 单源——"新表接入第五件"退役（新表入清单零登记成本）；该债 2026-10-13 曾按红线 5 登记进[代码质量审计-客户端](../代码质量审计-客户端.md) §二（"必须与 X 一致"只靠注释的违规形态），修复登记已随批C-⑤ 回写；
- 门禁测试断言 **Error=0 + Warning=1**——Warning 数随页面批次（UIMain 根名）收口后应改回 0 基线，届时同批更新。

## 步 6：编排面板（已交付）

**落点**：`Assets/LiteGame/Editor/UiTools/Compose/`（宿主与四分区）＋ `Assets/LiteGame/Editor/UiTools/Binding/UiBindTreeView.cs`（共享只读树）＋ 运行时标记 `Assets/LiteGame/UI/UiShellPropertyAttribute.cs`

| 件 | 职责 | Sirenix |
|---|---|---|
| `UiComposeWindow.cs` | 宿主窗口（`OdinEditorWindow`，菜单 `LiteGame/UI/编排/UI 编排面板`）：Odin 绘制目标 prefab 与状态行（置页签组之上）+ **四个 Odin `TabGroup` 页签**（绑定暴露/受控属性/校验·生成/控件库）；Stage 跟随 | 有 |
| `UiComposeTarget.cs` | 目标解析**单源**：`StageRootFor` / `Resolve` / `FindNodeForReport`（报告 node 含根名 vs 树口径不含根的收口） | 无 |
| `UiBindingsSection.cs` | 绑定暴露分区：内嵌**只读**树 + 跳转绑定标记工具编辑（编辑动作不搬进宿主） | 无 |
| `UiShellPropertyAttribute.cs`（运行时） | 受控面声明（`LiteGame.UI`；零依赖、只声明不驱动） | 无 |
| `UiShellPropertyCatalog.cs` | 白名单发现：自有程序集组件 + 标注字段；值型最小集（bool/int/float/string/enum）+ `Unsupported` 显式 | 无 |
| `UiShellPropertySection.cs` | 受控属性分区：逐组件 **Odin 属性树只画白名单成员**（写入/Undo/脏标记归属性树） | 有 |
| `UiValidationSection.cs` | 校验分区：**窄档**（资产面只喂本 prefab）+ 全量档；报告列表化 + 定位节点 | 无 |
| `UiGenerateSection.cs` | 生成分区：`BindGenerator.Generate` → 同一步 `UiBindGenerationHash.Record`（先 `DisplayDialog` 确认——生成触发域重载） | 无 |
| `UiWidgetLibrary.cs` / `UiWidgetInstaller.cs` | 控件库：**目录即清单**（扫 `Widgets/` 的 `Tmp_*`）＋**只在 Prefab Stage 内**插入（Undo/嵌套/Variant 归原生） | 无 |
| `UiWidgetLibrarySection.cs` | 控件库分区：**inline `OdinMenuTree` + Odin 搜索栏**（左树；替掉手写过滤框）+ 右侧选中模板的组件/受控面预览 + 插入按钮（父级＝Stage 内选中节点，缺省 Stage 根） | 有 |

**改动既有件（消重）**：`UiBindingWindow.DisplayRoot` 改调 `UiComposeTarget.StageRootFor`；`UiBindingWindow.DrawTree` 改调 `UiBindTreeView.Draw`（窗口行为零变化：勾选/选中/漂移色/徽标/Ping 全保留）；`LiteCodeGen.Editor/BindMenus.FindRootInPrefab` private→public（生成分区复用，不复制实现）；`Assets/Tests/EditMode/LiteGame.EditModeTests.asmdef` 补 `LiteCodeGen.Editor` 引用（测被复用的生成根查找）。

**受控面标注集（本轮最小）**：`HpBar.ValueFormat/BackLerpSpeed`、`ProgressBar.ValueFormat`、`Stepper.Min/Max/Step`、`CountText.Duration`——只标面向内容作者的展示性/行为性字段；**接线引用字段**（`Slider`/`ValueText`/`BackFill`/`Label`）不标（属内部实现）。

**依赖边界**：Odin 只进 `LiteGame.Editor`（`restore-check.ps1` §4 / `toolchain.json` 已把 Odin 列为**阻塞式环境项**）；呈现层＝宿主窗口 + **Odin `TabGroup` 页签** + 目标字段 + 受控属性的 Odin 属性树 + 控件库 inline `OdinMenuTree`；页签体（绑定树/报告）走既有 IMGUI 绘制器，在 Odin 页签内混排；逻辑类与运行时标记**零 Sirenix**——"摘除 Odin 只换绘制层"。

### 验证证据

| 项 | 结果 |
|---|---|
| Unity recompile | 0 错误（含 Odin 引用面：`LiteGame.Editor.asmdef` 自动引用 `Sirenix.OdinInspector.Editor.dll` 成立） |
| Odin 形态实证 | `AgentScripts/VerifyComposeTabs.cs`（未入库复验脚本）：属性树断言页签组 `#Compose` 含四页签且页签名与组内成员全对；临时冒烟窗口强制绘制受控属性/控件库/校验三分区，连续重绘 38 帧**零 error/exception**（冒烟窗口用后即删）——`[OnInspectorGUI, TabGroup(组名, 页签名)]` 与 inline `OdinMenuTree` 可用性由此钉住 |
| `UiComposePanelEditModeTests` | **5/5**：目标解析两态（Stage/资产、非 prefab→null）／窄档报出本 prefab 命名违规并按报告节点反查定位／**报告 node 含根名 vs 树口径不含根**（两口径收口钉）／未接入族显式列出 |
| `UiShellPropertyEditModeTests` | **5/5**：白名单只出标注成员／接线字段不进面／非自有程序集组件不产行／值型归类（含 `Unsupported`）／**真实模板 `Tmp_HpBar` 受控行** |
| `UiComposeGenerateEditModeTests` | **2/2**：生成根查找三态（根上/子物体/无）／记录-校验闭环（未记录 Error → 记录后「未记录·漂移」清零，记录值＝当前绑定面指纹） |
| `UiWidgetLibraryEditModeTests` | **6/6**：真实资产枚举 ≥27 且全 `Tmp_`／Screens 目录不误收／模板名判据／使用段映射／插入守卫（未绑定·未开 Stage 不可插且给原因） |
| 全量 EditMode | **609/609 全绿**——本批新增 18 全绿、零新增红（并行的房间席位红〔`MetaPagesEditModeTests`×5 · `BattleRemoteFireEditModeTests`×1〕已在对方批内修净，本批不含） |
| L1 门禁 | `test.ps1 -Lane L1 -Profile PullRequest` **[PASS] 1694** |
| L2 Unity 门禁 | **未跑**——本批**零序列化资产改动**（只新增/改 `.cs`、`.asmdef`、文档；无 prefab/scene/material/SO 变更），PlayMode 面不受影响；L2 的 EditMode 段已由上面的全量 EditMode 跑覆盖 |
| BuildHash | **未跑亦无需跑**：本轮不碰共享源集/`.proto`/Luban 表源（`UiShellPropertyAttribute` 落 `LiteGame.UI`，不在 `gen-build-hash.py` 闭包内） |
| 窗口交互面 | 属手测（IMGUI/Odin 绘制不做自动化断言——与既有工具窗口同口径） |

### 遗留（如实）

- **绘制面只做逻辑侧断言**：宿主/分区绘制、Odin 属性树行编辑、模板插入的真实走查随制作侧使用反馈；
- **受控面标注从最小集起步**（4 件控件 7 个字段）——其余控件随使用增量标注；未标注＝不进面（不冒充已覆盖）；
- **对象引用类值型不支持**（`Unsupported` 显式列出）——控件/资产引用属接线面，不进受控面；
- **缩略图未做**（RenderTexture 成本）——列表以模板名 + 使用段替代，登记为增强项；
- **全量档校验较慢**（源码面扫描 ~60s）——窗口默认窄档，全量档仅显式按钮且无进度/取消；
- **Odin 依赖面**：未按 `restore-check.ps1` 安装 Odin 的机器 `LiteGame.Editor` 编译失败——由既有环境政策承担，与归档《编辑器设计规划》"Odin 只进 LiteGame.Editor"硬纪律一致（不额外拆 asmdef）。
