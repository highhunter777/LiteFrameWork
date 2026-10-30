# 08 UI 与 Lua 脚本层

> **定位**：解释客户端 UI 从 prefab 到"可开关、可绑定、可热更"的现行走线，给出工具入口、页面目录与排障锚点。
> **权威边界**：不裁决目标架构（归 [设计文档](../设计文档/README.md)）、不记批次（归 [施工进度](../施工进度/README.md)）。
> **来源**：合并自 `技术总结/04-UI系统与Lua脚本层` 与 `项目说明/08-UI工作台`（旧路径已删，映射见 [说明索引](README.md)）。
> **关联**：[03-客户端框架与运行时](03-客户端框架与运行时.md) · [13-调试面与排障](13-调试面与排障.md) · [15-工具链手册](15-工具链手册.md) · [规范 05-UI 制作](../规范/05-UI制作.md) · [规范 06-UI 美术协作与交接](../规范/06-UI美术协作与交接.md) · [设计文档·UI 系统总设计](../设计文档/客户端/UI系统/UI系统总设计.md) · [施工进度·UI 系统线](../施工进度/UI系统线.md) · [审计·客户端质量登记](../审计/代码质量审计-客户端.md)

## 1. 四条硬约束

UI 不是"一个 UIManager"，而是**运行时 + 绑定生成 + 工具链 + Lua 注册表**四段流水线。四段共同服务的约束（违反由扫描器、校验规则或运行时抛错兜住）：

1. **视觉单一来源**：可见结构只能来自 `Assets/ShareResource/UI/{Screens,Widgets,Hud}` 的 prefab，运行时不得 `new GameObject`/`AddComponent` 拼界面。`RuntimeVisualRule` 扫 `UiValidator.SourceScanDirs`（`Assets/LiteFramework/Runtime`、`Assets/LiteGame/App`、`Assets/LiteView` 等）。
2. **单写者**：产品页面开关只经 `UINavigationController`；`UIService.ShowAsync` 只留给工具/测试。
3. **逻辑可热更**：页面逻辑在 Lua，C# 只提供受控 API 面（`self.ui`）与生命周期桥，换表不重建 `LuaEnv`。
4. **制作可分工**：美术管 prefab/命名/暴露，工程管表行/绑定/接线，以**页面契约**交接。

## 2. 运行时：一个壳 + 协作件

`UIService`（`Assets/LiteFramework/Runtime/Modules/UI/UIService.cs`）只做**开关管线编排**（校验 → 取/建实例 → 转场 → 落池 → 重算遮盖/模态），其余由协作件各管一件：`UIFormStore`/`UIFormCacheEvictor`/`UIFormStats` 管实例、租约、LRU 记账与淘汰选谁（表只给候选集，只淘汰池中非 `Resident`）；`UIFormLogicBinder` 管逻辑解析/注入/换表标记；`UILayerGroupFactory`/`UIStack` 管运行时根与层级组（索引即 `PageDefinition.Layer`）；`UICoverageResolver` 管遮盖推导（以"仍逻辑打开的最高全屏"为界，更低组全遮，逐界面最小迁移并联动焦点）；`UIModalStack` 管模态、顶模态、射线遮蔽与 Back 目标（从仍打开页面推导，只依赖 `IUIFormQuery`）；`UITransitionRunner` 管转场（不注册 `ITickable`，壳在 Tick 帧末转发）；`UIFocusCoordinator`/`UIBindIndex` 管焦点与名字到控件索引（`Get` 未命中直接抛）。

层级固定为 `UIService.GroupNames = Bottom / Window / Top / System`（索引 0/1/2/3）。**没有独立的 Modal/Loading 层**：模态是"页面属性 + 栈推导"，Toast/Loading/错误重试统一落 `System` 层并由统一排序器分配 `order`——反馈面不得自建 Canvas 或私有 `sortingOrder`。

## 3. 生命周期、缓存与关闭原因

```text
Loading → Active ⇄ Covered / Paused → Closing → Recycled → Disposed
```

- 只有 `Active` 转发 `OnUpdate`；`Covered` 由更高层全屏批量推导，`Paused` 是手动暂停；非法迁移在 `UIForm` 当场抛。`Recycled` 可复用且**不重跑 `OnInit`**，`Disposed` 是终态；Tick 走快照迭代（`_tickSnapshot`），回调里开关界面安全。
- 关闭原因 `CloseReason` 六值：`User / Back / Replace / ScopeExit / Reload / Shutdown`；系统关闭（后三个）跳过"未保存拦截"与离场动画，`User`/`Back` 可被出栈拦截。
- 缓存预算 `UIService.DefaultCacheBudget = 16`（`Resident` 也计入，只是不参与淘汰）；`UICacheStrategy` 三值：`Lru`（默认）/ `Resident`（关闭入池不淘汰）/ `DestroyOnClose`（关即销毁、不进池）。
- 策略链路：`UiPageManifest.json` 的 `cacheStrategy` → `gen-ui-page.py` → `UiPageCatalog.g.cs` → 运行时（`UIService` 关闭路径按 `DestroyOnClose` 销毁，`UIFormStore` 淘汰候选过滤 `Resident`）。当前 11 个登记页全部 `Lru`。

## 4. 导航单写者

`UINavigationController`（与 `UIService` 同目录）串行排队执行 Go/Back：

| 参数 | 值 | 语义 |
|---|---|---|
| `DefaultQueueCapacity` | 8 | 排队中操作上限（不含正在执行） |
| `DefaultOpenTimeoutSeconds` | 10 | 排队等待上限；`<=0` 不超时 |
| `UITransitionRunner.DefaultMaxDuration` | 2s | 转场兜底，超时广播 `Completed=false` |
| 转场待办队列 | 1 | 同帧 last-wins；目标即当前 Incoming 则忽略，再来的丢弃计数 |

- **队列满在创建/入栈/OnShow 之前拒绝**（`UIOpenException(Rejected)`，不留半成品）；**排队期取消**＝标记放弃、出队跳过；**执行期取消**贯穿 `ShowAsync`，半成品由其回滚。
- `UIOpenFailure`：`Canceled / LoadFailed / InitFailed / Busy / Rejected / Timeout`（同界面在途且数据不同＝`Busy`，不静默覆盖）。读数：`Executed`/`RejectedByCapacity`/`TimedOut`/`CancelledWhileQueued`。
- `GoAsync` 保留旧页（新页覆盖其上），`ReplaceAsync` 显式关闭当前记录（旧页收 `Replace`）。Back 链：**最顶模态优先**，其次最高非空层栈顶（原因 `Back`，可被出栈拦截）；`BackInterceptor` 返回 true＝本次 Back 已消费（如 Loading 阻断）。平台返回统一口径：`Focus.PlatformBack` 挂 `nav.BackAsync`（`UiServiceModule`）。

## 5. 绑定：一条运行期索引

- prefab 挂 `BindNode`、`BindName` 非空；`BindIndexBuilder` 实例化后扫全树登记，空名跳过（只参与代码生成），重名抛。当前 35 个 prefab 共 86 个 `BindName`。
- `UIBindIndex.Get<T>` 未命中抛 `KeyNotFoundException`、类型不符抛 `InvalidOperationException`，不返回 null。
- **所有权互斥**：每控件登记 `Bound`（数据绑定）或 `Command`（命令式），一方驱动后再以另一方驱动即抛，消除"谁最后生效"竞态。按钮绑定是替换式（重绑先移除旧监听），池化复用安全；Lua 适配器在 `OnHide` 解绑全部按钮。
- **生成类路径当前无生产页面**：`BindRoot`+`BindNode` → `BindGenerator` → `Xxx.cs`/`Xxx.Designer.cs`，但无页面挂 `BindRoot`，`UiBindManifest.json`（绑定面指纹）未生成；无 `BindRoot` 时不伪造 hash。生成文件禁手改，主类可写 partial。

## 6. Lua 层

### 6.1 三注册表

`UiServiceModule` 构造并 `context.Put` 三个注册表（`Assets/LiteGame/LuaBridge/LuaRegistries.cs`），键为全路径，重复注册抛：

| 注册表（kind） | 键 | 来源表 |
|---|---|---|
| `UiLuaRegistry`（UI） | `PageDefinition.LuaPath`（`UI.Page_Login`） | `UI/tbuiform.lua_path` |
| `ContentLuaRegistry`（Content） | `TbContentEntry.Entry` | `Content/tbcontententry.entry` |
| `StrategyLuaRegistry`（Strategies） | `TbStrategy.LuaPath`（空＝C# 默认实现） | `Strategy/tbstrategy.lua_path` |

`LuaKeys.g.cs`（`Luban/gen_lua_keys.py` 生成）当前 6 个 UI 常量：`Page_Login / Page_Lobby / Page_Room / Page_Result / Page_Settings / Page_Records`。`RegistryFiller.KnownUiKey = LuaKeys.UI.Page_Login`：启动期校验"编译期已知的约定路径必须可解析"。

### 6.2 预载五步序

`ProcedurePreload`（`Assets/LiteGame/App/Procedure/Stages/ProcedurePreload.cs`）在配置装载后执行五步，任一失败即阻断进 Main：① Lua 全量预载（字节经 `IContentService` 租约，清单由装配点注入）→ ② `_lua.Init(...)`（env + 事件桥）+`DoMain()` 执行 `main.lua` → ③ `RegistryFiller.FillAll()` 逐行 require/校验/填充并出报告 → ④ 报告整批判定（有失败→记 Error、进 Error 流程）→ ⑤ 放行：登记设置页为模态（表驱动反查 `formId`）后 `Request(Main)`。失败/取消都先 `LaunchLoading.End()`；`LuaPreloader` 缓存键用斜杠、模块名用点分（与 `ReleaseLayout` 同口径）。

### 6.3 填充与失败分类

`RegistryFiller` 逐行：路径须"两段式且根与所在表一致" → 预载缓存命中 → `require` 返回 table → `registry.Fill`。单项失败记入报告并继续，整批跑完统一判定；三类：**路径非法**（非两段式或根不符）、**未注册（配置漏配）**（预载缓存未命中，表行引用的 Lua 文件不存在）、**注册失败（lua 异常）**（未返回逻辑表或执行抛错）；另有报告项"LuaKeys 常量未注册"。

### 6.4 生命周期桥与 `self.ui`

`LuaBehaviourAdapter`（`Assets/LiteGame/LuaBridge/LuaBehaviourAdapter.cs`）把 `IUIFormLogic` 七回调翻译成 `self:OnInit/OnShow/OnUpdate/OnPause/OnCover/OnReveal/OnHide`：注册表存**模块表**，适配器构造期 `module.new()` 得实例（无 `new` 的旧表退化为"模块即实例"），七个 `LuaFunction` 构造期一次缓存；方法缺失静默跳过，异常由 `UIForm.SafeCall` 隔离（单页降级）；`OnInit` 建绑定索引 + 挂 `self.ui`，`OnHide` 解绑全部按钮；**所有权分开**：注册表拥有模块引用，页面拥有实例/回调，`Release` 不 Dispose 共享模块。

`self.ui` 是生成的白名单门面（`UiApiShim.g.cs` 的 `LuaTemplate`，**33 个方法**：按钮/文本/key 文本/进度/倒计时/提示/确认/表单控件/列表/动效），经单委托 `Action<string, LuaTable>` 派发，缺省动效时长由适配器用 `UiFxDefaults` 替换。源头 `Assets/LiteGame/Editor/UiTools/UiApi/UiApiSpec.json`，重生成 `python scripts/codegen/gen-ui-api.py`。

### 6.5 换表两条路径

| 路径 | 行为 | 适用 |
|---|---|---|
| `LuaRegistryRefillService.RefillAsync` | **不重建 env**：`MarkLogicStale` 打标 → 重预载 → 清 Bridge 缓存位 → 清 require 缓存 → 三注册表 Clear → `FillAll` 重填 → 已打开界面跑完旧表后换表（池中实例由 `ApplyStaleLogic` 立刻换） | 运行期热更 |
| DevReload | 重建 env（先全关界面） | 编辑器期结构性改动 |

三注册表 `Generation` 之和只作 HUD 读数，**不作失效判据**；判据是 `UIService` 的显式打标。

### 6.6 Bridge 门面与页面流

Lua 只能经白名单门面访问 C#：`Bridge` 只导出 `data / ui / content / text` 四件（`Assets/LiteGame/LuaBridge/Bridge.Data.cs`），产品侧 `meta`/`settings` 经 `ProductBridges.BindGlobals` 在同一 env 装订；`ILuaRegistry` 与业务系统不导出。当前 Lua 消费（`Assets/LiteGame/Lua/**/*.lua`）：`Bridge.meta` 22 处、`Bridge.text` 13、`Bridge.settings` 10、`Bridge.ui` 10、`Bridge.data` 2、`Bridge.content` **0**。

`PageFlow`（`Assets/LiteGame/App/Ui/PageFlow.cs`）只接流程与 UI 之间的接缝，页面跳转由 Lua 经 `Bridge.ui` 驱动（仍走导航单写者）。`Landing` 三值 `Login`（正式）/`Lobby`（本地服务器、开发）/`None`（直入训练场、工作台）；`OpenLanding` 只在首进 Main 开一次页，账号重入走 `ShowLogin`。开局关房间页、结束开结算页、对局中开设置页（导航数据 `battleExit` 门控"退出对局"）。

## 7. 工具链

### 7.1 目录与入口

`Assets/LiteGame/Editor/UiTools/`（98 个 `.cs`）按职责分十二个目录（`Workbench`、`Compose`、`Binding`、`Contract`、`Page`、`Preview`、`Validation`、`Widgets`、`Text`、`Naming`、`Reference`、`UiApi`）；清单与生成物为 `UiPageManifest.json`、`UiWidgetManifest.json`、`Contracts/<Page>.contract.json`（11 份）、`UiRuleExceptions.json`、`UiStyleTokens.json`、`UiDeviceProfiles.json`。

16 个 UI 类工具 id（`python scripts/toolchain.py open <id>`）：`ui.workbench`（唯一制作入口）、`ui.page-scaffold`、`ui.page-register`、`ui.contract`、`ui.contract-diff`、`ui.page-manifest`、`ui.widget-manifest`、`ui.validate`、`ui.binding-hash-all`、`ui.binding-hash-selected`、`ui.style.colors-refresh`/`colors-report`/`text-apply`/`text-report`/`tokens`、`ui.preview-matrix`。

### 7.2 五透视与 Lane

`UiWorkbenchPerspectives` 是单源（窗口与夹具共用）：页面 10（`page.list`/`page.metadata`/`page.scaffold`/`page.register`）、控件库 20（`widgets.insert`/`widgets.properties`）、绑定 30（`binding.edit`/`binding.generate`/`binding.hash`）、契约 40（`contract.generate`/`contract.diff`）、校验 50（`validate.narrow`/`validate.full`）。

`UiLanePolicy` 是"Lane × 动作"门控单源（表驱动，窗口里不得散落 `if (lane == …)`）Lane 只是 UX 分层与默认动作集，不是权限系统。词表 `Art`/`Technical`/`Engineering`，一个 prefab 同一时间一个 Owner。全部允许 `OpenPage`/`EditBinding`/`GenerateContract`/`Validate`/`Preview`；`EditMetadata` 限美术/工程（TA 只读）；`InsertWidget` 限美术/TA；`DiffContract` 限 TA/工程；`Scaffold` 限美术；`Register`/`GenerateBinding` 限工程。

### 7.3 起页与登记

**起页（美术）**：输入页面名 `Page_<Semantic>`（根名与文件名同文）+ 视觉基线（`Tmp_BaselineA`/`Tmp_BaselineB`/`Tmp_ListScreen`/空白 `Tmp_Page`）。产物：`Assets/ShareResource/UI/Screens/Page_<Semantic>.prefab`（从基线**复制**为自包含 prefab，不是 Variant）、`UiPageManifest` 草稿条目（`owner` 默认 `art.ui`、`status=Draft`）；**不写** Lua、不写 `tbuiform`、不生成绑定类。根契约幂等补齐 `Canvas`+`CanvasScaler`（1920×1080，Match 0.5）+`GraphicRaycaster`+`CanvasGroup`；`SafeAreaReceiver` 挂在现有拉伸 `Cot_*` 容器（无则由工具新建）。交接前须生成草稿契约（查 `sourceHash`/`controls[]`/`text[]`/`templates[]`），草稿布局改动后必须重生成（`ContractDriftRule` 判 Error），无草稿契约时登记被拒。

**登记（工程）**：输入页面名、`id`（窗口给"最小空闲 ≥16"建议值，不复用 201–206）、`layer`（0–3）、`full_screen`、缓存策略；契约门槛＝`Contracts/<Page>.contract.json` 存在、`formId=0`、`prefabGuid` 与当前 prefab 一致。步骤与失败面 `UiPageRegisterFailure`（`Validate / Contract / Lua / WriteTable / Codegen / Manifest / ContractRefresh`）：① 预检（查重、非法名/保留 id/layer 越界）→ 失败零产物；② 写表前备份 → `Luban/Data/#uiform.xlsx` 追加 `id/lua_path/prefab/layer/full_screen` 行；③ `codegen.luban` → `codegen.build-hash`；④ Lua 骨架 `Assets/LiteGame/Lua/UI/<Page>.lua`（已存在则保留）；⑤ 清单 `Draft → Registered` + 写 `formId`/缓存策略；⑥ 契约按真实 `formId` 重生成。回滚：表写失败按备份回滚并复核行不存在；Luban/BuildHash 失败回滚表行并重跑 Luban（清单仍 Draft）；清单保存失败不回滚表；契约刷新失败提示重跑 `ui.contract`。

### 7.4 契约、校验与预览

- 契约＝prefab 对外 API 快照；契约 diff 视为 UI API 变更，破坏性变更需双方评审，**不手改契约**。`UiPageManifest` 五态 `Draft / InReview / Registered / Wired / Frozen`，`UiPageStatusRules` 是迁移表唯一实现源。
- `UiValidator` 是**唯一校验核心**，三入口共用（菜单、CLI `RunCli`、EditMode）；规则按 `IUiRule` 端口注册，当前 **24 条规则、10 个规则族**（结构、命名/绑定、契约、样式、文本、布局/适配、资源、性能、行为、渲染/适配）。各族缺口写在 `UiValidator.Families`，未落地子项显式标注、不显示为通过；例外走 `UiRuleExceptions.json`。
- 预览：工作台「预览」→ `UiPagePreview` 打开专用预览场景进 Play，页面由 `UiPreviewDriver` 直接实例化，**不跑 Boot/Lua/数据**；保存该 prefab 资产即热重载，退出 Play 回到原场景，预览中隐藏 DevHUD。参数经 `EditorPrefs`/`SessionState` 键 `LiteGame.UiPreview.*` 传递（设备档、宽高、四边 inset、locale、字体缩放）；安全区由 `PreviewSafeAreaStrategy` 注入页面内 `SafeAreaReceiver`；设备档清单 `UiDeviceProfiles.json`，语言档含伪本地化（`PseudoLocale`）。**是布局档**：按钮回调、列表数据、语言服务行为不在这一档验证。

## 8. 现状清点

`Assets/ShareResource/UI/Screens` 13 个 prefab = 6 个产品页 + 6 个模板 + 1 个测试载体。生成目录 `UiPageCatalog.g.cs`（源头 `#uiform` + `UiPageManifest` + `Contracts`，重生成 `python scripts/codegen/gen-ui-page.py`）：产品页 11–16（`Page_Login/Lobby/Room/Result/Settings/Records`），除设置页 15 为 Layer 1 外全为 Layer 0 全屏，设置页在预载放行时登记为模态；模板 201–206（`Tmp_Dialog` 占 201 Layer 3 与 206 Layer 1 两行，其余为 Loading/Toast/两个基线）。6 个产品页全部 `status=Registered`、`cacheStrategy=Lru`、有契约；模板页也登记在表与清单里——读目录时不要假设"一页一行"。

- `Tmp_UiTestPage`（原样例页改名）与 `Tests/UiTestPage.lua` 是 UI 栈夹具：**不登记** `tbuiform`、不产出 `LuaKeys` 常量。
- 控件模板 `Assets/ShareResource/UI/Widgets` 30 个 prefab；HUD 为 `Assets/ShareResource/UI/Hud/Hud_Battle.prefab`（`UIHud` 收集组单件打包）。生产接线集中在 `ProgressBar`/`HpBar`/`Countdown`/`Toast`/`UIBubble`/`FlyTextPool`/`VirtualList`/`UIDialog`/`LoadingMask`；`AvatarFrame`/`GuideHighlight`/`StarRating`/`Stepper`/`BottomNav`/`TabGroup`/`SafeAreaReceiver`/`SimpleListView` 等只有模板与测试，不能宣称接入业务。
- **红点未生效**：`RedDotRegistry` 在 `UiServiceModule` 建实例并 `Put` 进上下文，但 C# 侧没有 `Register` 规则也没有 `Evaluate` 调用点，Lua 侧零引用；`RedDot`/`RedDotTree` 未接入 `UIBindIndex`。
- **Lua 调试器未接线**：`Assets/LiteGame/Lua/LuaPanda.lua` 存在但无 `require` 消费者；`Core/` 只有 `class.lua`。

**边界**：预览场景/预览驱动/脚手架模板/深检面/调试面板不得进收集组或构建，`UiTools` 与工作台只在编辑器程序集；契约、清单、`UiPageCatalog.g.cs`、`UiApiShim.g.cs`、`LuaKeys.g.cs` 由工具生成，禁手改；"能点/能操作"的结论必须走真实操作路径（可点性自检 → 模拟指针/按键 → 副作用断言），不以直调 API 冒充。

## 9. 排障与核对

Console 标签：`[UI]`＝UI 服务/导航；`[Lua]`＝Lua 宿主与注册表填充；`[LuaRefill]`＝运行期重填；`[Patch]`/`[Content]`＝内容侧（见 [09](09-内容热更与资源管线.md)）；工作台日志前缀 `[ui/workbench]`。

| 现象 | 原因/处置 |
|---|---|
| 工作台显示"未绑定目标" / 页面下拉为空 | 从页面选择器选页或在 Prefab Stage 选中 prefab；刷新；检查 `#uiform.xlsx` 可读与 `python -c "import openpyxl"` |
| 登记被拒（契约缺失 / guid 不符） | 先跑 `ui.contract` 生成或刷新（`formId=0`、prefabGuid 与当前 prefab 一致） |
| "表已写入但清单失败" / "契约未刷新" | 前者重跑 `ui.page-manifest` 且不要重复登记；后者重跑 `ui.contract`（`ContractDriftRule` 会一直报红） |
| 绑定未命中 / 点了没反应 | 先核对 `BindName` 与节点类型（`KeyNotFoundException` 即索引无该名）；再看可点性（遮挡、`interactable`、`blocksRaycasts`）与回调是否被 `OnHide` 解绑 |
| 页面复用后状态残留 / 真机安全区不对 | 前者查 `OnShow` 是否重推数据、异步是否有代次判据；后者用设备档预览 + 真机包，检查 `SafeAreaReceiver` 是否在内容容器 |

Unity 侧操作（打开 prefab、改场景、跑测试）一律走 unity-pipeline CLI，见 [15-工具链手册](15-工具链手册.md)。