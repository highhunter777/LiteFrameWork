# 04 · UI 系统与 Lua 脚本层

> **本章回答**：一个页面从"美术做 prefab"到"运行时可开关、可绑定、可热更"要经过哪些层；UI 运行时如何保证单写者与视觉单一来源；Lua 逻辑如何被注册、填充、换表；UI 工具链现在是什么形态。
> **上游权威**：[UI 系统总设计](../设计文档/客户端/UI系统/UI系统总设计.md)、[UI 编辑器工具链专项设计](../设计文档/客户端/UI系统/UI编辑器工具链专项设计.md)、[UI 制作规范](../设计文档/客户端/UI系统/UI制作规范.md)。本章按工作区代码写。
> **口径**：2026-10-22；`Editor/UiTools` 正在重构（旧 "UI 工作台" 文档与当前窗口名不完全对应）。

## 1. 目标与约束

UI 系统要同时满足四个约束：

1. **视觉单一来源**：用户可见结构必须来自 `Assets/ShareResource/UI/{Screens,Widgets,Hud}` 的 prefab；运行时代码不得 `new GameObject`/`AddComponent` 拼界面。
2. **单写者**：页面开关只经 `UINavigationController`；直接 `UIService.ShowAsync` 只留给工具/测试。
3. **逻辑可热更**：页面逻辑在 Lua，C# 只提供受控 API 面（`self.ui`）与生命周期桥。
4. **制作可分工**：美术只碰 prefab/命名/暴露，工程碰登记/绑定生成/接线，两者用"页面契约"交接。

这四条决定了 UI 不是"一个 UIManager"，而是**运行时 + 绑定生成 + 工具链 + Lua 注册表**四段流水线。

## 2. 运行时分层：一个壳 + 四个协作件

`UIService` 只做**开关管线编排**（校验 → 取/建实例 → 转场 → 落池 → 重算遮盖/模态），其余职责外提：

| 件 | 职责 | 关键约束 |
|---|---|---|
| `UIService` | 开关事务序、逻辑换表、Tick 快照转发 | 不重复协作件的机制 |
| `UIFormStore` | 实例 + 租约 + LRU 记账 + 淘汰候选 | 预算/淘汰由壳触发（本表提供候选，并过滤 `Resident`）；枚举期间不得改表 |
| `UICoverageResolver` | 从"仍打开的最高全屏"推导遮盖，逐界面最小迁移 | 含 Covered/Paused，不能只看 Active |
| `UIModalStack` | 模态登记、顶模态、射线遮蔽、Back 目标 | 从仍打开页面推导，不独立记账 |
| `UITransitionRunner` | 转场状态机 + 排队 1 + 超时 2s | 不注册 `ITickable`，由壳在 Tick 帧末转发 |
| `UIFocusCoordinator` | 焦点保存/恢复、平台返回、首个可交互聚焦 | 遮盖/暂停让出，恢复不抢占 |
| `UIBindIndex` | 名字 → 控件索引 + 所有权互斥 | Bind 与命令式只能二选一 |

**目录层级**固定为 `Bottom / Window / Top / System`（索引即 `tbuiform.layer`），**没有 Modal/Loading 层**：模态是"页面属性 + 栈推导"，Loading/Toast 走 `System` 层统一排序。

## 3. 生命周期：七态 + 六种关闭原因

`UIFormState` 七态：

```text
Loading → Active ⇄ Covered / Paused → Closing → Recycled → Disposed
```

- `Active` 才转发 `OnUpdate`；`Covered` 由更高层全屏批量推导；`Paused` 是手动暂停。
- 非法迁移在 `UIForm` 当场抛（fail-fast）。
- `Recycled` 可复用且**不重跑 OnInit**；`Disposed` 是终态。
- 关闭原因 `User / Back / Replace / ScopeExit / Reload / Shutdown`：系统关闭（ScopeExit/Reload/Shutdown）跳过"未保存拦截"与离场动画。

**缓存**：默认预算 `DefaultCacheBudget=16`；`Lru` 参与淘汰、`Resident` 不淘汰但仍计入预算、`DestroyOnClose` 关即销毁。

> **缓存来源**：`PageDefinition.CacheStrategy` 由 `UiPageCatalog.g.cs` 从 `UiPageManifest` 生成；`Resident`/`DestroyOnClose` 已有配置入口（改清单后重跑 `gen-ui-page.py`）。

## 4. 导航：单写者、队列、超时

`UINavigationController` 是产品导航的唯一入口：

| 参数 | 值 | 语义 |
|---|---|---|
| `DefaultQueueCapacity` | 8 | 排队中操作上限（不含正在执行） |
| `DefaultOpenTimeoutSeconds` | 10 | 排队等待上限；≤0 不超时 |
| 转场超时 | 2s | `UITransitionRunner` 兜底，超时广播 `Completed=false` |
| Back 链 | 最顶模态 → 最高非空层栈顶 | 关闭原因 `Back`，可被出栈拦截 |
| 队列满 | 在创建/入栈/OnShow **之前**拒绝 | `UIOpenFailure.Rejected`，不留半成品 |
| 排队期取消 | 标记放弃，出队跳过 | 不加载、不建实例 |

`GoAsync` 保留旧页（新页覆盖其上），`ReplaceAsync` 显式关闭当前记录（旧页收到 `Replace` 原因）。两者都排队串行，避免"同帧连点两个按钮"在转场中互相打断。

## 5. 绑定与代码生成：一个索引

控件暴露只有一条运行期索引来源：

- **标记**：prefab 上挂 `BindNode`，`BindName` 非空；`BindIndexBuilder` 实例化后扫全树登记。空名跳过（只参与代码生成），重名抛。Lua 页面与 C# 页面（`UIBindBase` 派生）共用这条索引。
- **C# 生成类**：`BindRoot` + `BindNode` → `BindGenerator` 生成 `Xxx.cs` / `Xxx.Designer.cs`，只提供强类型字段引用（`UIBindBase` 的受控 API 仍走 `BindIndexBuilder`）。

`UIBindIndex` 是运行期查询底座：

- `Get<T>` 未命中/类型不符直接抛（不返回 null）。
- **所有权互斥**：每个控件登记 `Bound` 或 `Command`；Bind 后再命令式写、或反之，开发三宏下当场抛——防止"谁最后生效"的竞态。
- 绑按钮是替换式（重绑先移除旧监听），池化复用安全。

`LiteCodeGen` 负责生成绑定类；`UiBindGenerationHash` 记录"生成物 hash"，`ui.binding-hash` 规则在三入口（窗口/CLI/EditMode）对账，防止 prefab 改了但生成物没重生成。

## 6. Lua 集成：注册表、五步预载、换表

### 6.1 三注册表

`UiServiceModule` 建三个注册表并放入上下文：

| 注册表 | 键 | 值 | 来源表 |
|---|---|---|---|
| `UiLuaRegistry` | `tbuiform.lua_path` | Lua 模块表 | `tbuiform` |
| `ContentLuaRegistry` | `tbcontententry.entry` | Lua 模块表 | `tbcontententry` |
| `StrategyLuaRegistry` | `tbstrategy.lua_path` | 策略表 | `tbstrategy` |

`LuaKeys.g.cs` 是生成物，当前 6 个 UI 常量：`Page_Login/Lobby/Room/Result/Settings/Records`。`ValidateKnownKeys` 兜底校验其中 1 个（`UI.Page_Login`；UIMain 样例页已于 2026-10-23 退役）。

### 6.2 五步预载

`ProcedurePreload` 的顺序（任一失败 fail-fast 阻断进 Main）：

```text
① 配置表加载（ConfigService）
② Lua 字节预载（内容服务租约）
③ Init env 执行 main.lua
④ RegistryFiller 填三注册表
⑤ 报告统一判定
```

`RegistryFiller` 逐行 require/校验/适配，失败三分类：**路径非法 / 未注册（配置漏配）/ 注册失败（lua 异常）**；单项失败记入报告继续，整批跑完统一判定（不遇错即停）。

### 6.3 生命周期桥

`LuaBehaviourAdapter` 把 `IUIFormLogic` 七回调翻译成 `self:OnInit/OnShow/OnHide/...`：

- 注册表存**模块表**，适配器构造期执行 `module.new()` 得到实例（无 `new` 的旧式逻辑表退化为"模块即实例"）。
- 七个 `LuaFunction` 构造期一次缓存（`OnUpdate` 每帧路径不反复 Get）。
- 方法缺失静默跳过；异常由 `UIForm` 的 `SafeCall` 隔离（单页降级，不波及其他页）。
- `OnInit` 建绑定索引 + 给实例挂 `self.ui` 门面；`OnHide` 解绑全部按钮（池化复用跨环境的安全垫）。
- **所有权分开**：注册表拥有模块引用，页面拥有实例/回调；`Release` 不 Dispose 共享模块。

`self.ui` 是受控 API 面（32 个 shim 方法：按钮、文本、列表、动效、气泡、弹窗等），经**单委托**派发，不新增生成代码。

### 6.4 换表：两种粒度

| 场景 | 行为 |
|---|---|
| `LuaRegistryRefillService`（运行期增量） | **不重建 env**：打标 stale → 重预载 → 清 Bridge 缓存 → 清 require 缓存 → 三注册表 Clear → 同一条 `RegistryFiller` 重填 → `ApplyStaleLogic` 换表 |
| `DevReload`（编辑器重锤） | 重建 env，适合结构性改动 |

`Generation` 只作读数不作判据：每次 Fill 都递增，界面失效一律以 `UIService` 的显式打标为准。

### 6.5 Bridge 门面

Lua 侧只能经白名单门面访问 C#：`data`、`ui`、`content`、`text`（产品侧另有 `meta`/`settings` 后端）。当前 Lua 消费分布（全仓 `*.lua` 统计）：`Bridge.meta` 22 处、`Bridge.text` 13、`Bridge.settings` 10、`Bridge.ui` 10、`Bridge.data` 2、`Bridge.content` 0——**content 门面尚无 Lua 消费方**。

### 6.6 页面流

`PageFlow` 是"流程不认识 UI、UI 不认识流程"的那几处接缝：

- 首进 Main 按落点开页：正式形态 `Login`、本地服务器/开发形态 `Lobby`、直入训练场/工作台则 `None`；
- 对局开局关房间页；对局结束开结算页；对局中打开设置页（HUD 菜单/空栈 Back 的对局菜单语义）。

页面→页面的跳转由 Lua 经 `Bridge.ui` 驱动（仍走导航单写者），`PageFlow` 只接流程侧。

## 7. UI 工具链：当前形态

工作区的 `Assets/LiteGame/Editor/UiTools` 已按职责重排为 `Compose / Binding / Page / Preview / Validation / Naming`：

| 入口（toolchain id） | 形态 | 做什么 |
|---|---|---|
| `ui.workbench` | 工作台（纯 IMGUI 宿主） | 页面清单/维护字段、控件库+受控属性、绑定编辑、C# 生成类+hash、契约生成/diff、校验窄档/全量；角色 Lane 与阶段门控 |
| `ui.page-scaffold` | window（美术） | 视觉基线 → 自包含 prefab + 根契约/安全区 + 页面清单草稿；不写 Lua/表 |
| `ui.page-register` | window（工程） | 草稿契约门槛 → `#uiform.xlsx` 行 + Lua 骨架 → Luban+BuildHash → 清单 `Registered`/缓存策略 → 契约刷新；失败按备份回滚并复核 |
| `ui.validate` | action | 统一校验核心：菜单 / CLI（`RunCli`，供 CI）/ EditMode 三入口 |
| `ui.binding-hash-all` / `-selected` | action | 记录/对账绑定生成物 hash |
| `ui.style.*`（4 个） | action | 颜色刷新/对账、文字档对账/应用 |
| 预览（上下文条入口） | 编辑器内 | 工作台「预览」按钮打开 `UiPagePreview`：专用预览场景进 Play，保存 prefab 热重载，退出回原场景（无独立 toolchain id；P2-3 升级为透视/设备档） |

**唯一编辑面仍是 Unity Prefab Stage**：工具窗口不造第二套画布，不接管几何/锚点/层级；`UiWorkbenchWindow` 只提供上下文、透视、门控与报告。

`UiValidator` 是**唯一校验核心**，规则按 `IUiRule` 端口注册，六族中已接入：

- 绑定（命名文法/前缀词表/私有件 + 生成物 hash 对账；同文/同页同词/编号连续待接入）；
- 资源（表内资源路径存在性；缺引用其余子项/收集组/图集字体名册/生产包排除待接入）；
- 渲染/适配（视觉单一来源，首条已接入）。

文本/行为/性能三族**未接入**，报告里显式列"待接入"，不显示为自动通过。

## 8. 页面与控件现状

- **页面**：`ShareResource/UI/Screens` 有 6 个产品页 prefab（`Page_Login/Lobby/Room/Result/Settings/Records`）、6 个产品模板（`Tmp_BaselineA/B`、`Tmp_Dialog`、`Tmp_ListScreen`、`Tmp_Loading`、`Tmp_Toast`）与 1 个测试载体 `Tmp_UiTestPage`（UIMain 退役后改名的 UI 栈夹具，不登记 tbuiform/页面清单）。
- **控件模板**：`ShareResource/UI/Widgets` 有 30 个 prefab；生产接线集中在 `ProgressBar`/`HpBar`/`Countdown`/`Toast`/`UIBubble`/`FlyTextPool`/`UIWidget`/`VirtualList`/`UIDialog`/`LoadingMask`。
- **未接线控件**：`AvatarFrame`/`GuideHighlight`/`StarRating`/`Stepper`/`BottomNav`/`TabGroup`/`SafeAreaReceiver`/`SimpleListView` 等多数只有模板 + 测试，不能宣称"已接入业务"。
- **红点链**：`RedDotRegistry` 在 `UiServiceModule` 建实例并 `context.Put`，但 C# 侧**无 `Evaluate` 调用点**；`RedDot`/`RedDotTree` 未接入 `UIBindIndex`。
- **主流程**：`ProcedureMain` 会 `PageFlow.OpenLanding` 开登录/大厅页；`UIMain` 样例页已退役，不再是入口或注册页。

## 9. 边界与坑

1. **缓存策略不可达**：`CacheStrategy` 未投影，`Resident`/`DestroyOnClose` 目前是"未兑现的目标"。
2. **红点未生效**：注册表存在 ≠ 红点会亮；没有 `Evaluate` 调用点。
3. **控件"有模板"≠"有消费者"**：30 个控件里大量仅测试覆盖。
4. **Lua 调试器/信号器未接线**：`LuaPanda.lua` 无 require，`Core/eventer.lua` 无调用方。
5. **工具名已收敛**：P2-2 起起页/登记拆为 `ui.page-scaffold`（仅 prefab+草稿）与 `ui.page-register`（契约门槛/表/Lua/生成链/清单/契约刷新）；工作台是编排入口，两窗口可直开；`ui.compose`/`ui.page-new` 已退役。
6. **UIMain 已退役**：产品入口是登录/大厅；UIMain 的 prefab/Lua 已改为测试载体 `Tmp_UiTestPage`/`Tests.UiTestPage`，不再登记 tbuiform 或产出 LuaKeys 常量。
7. **Bridge.content 零消费**：门面存在但没有 Lua 调用方，改它时要先找消费者。

## 10. 本章要点

- UI 的复杂度不在"开关页面"，而在**所有权**：实例/租约/缓存、导航队列、模态与遮盖、焦点、控件驱动方式，每一对都需要单一写者。
- "视觉单一来源"是硬约束，不是风格建议：运行时拼界面会被扫描器打红。
- Lua 热更的关键不是"能跑脚本"，而是**注册表填充 + 换表 + 失败分类**这条可诊断的链。
- 工具链的价值是让"美术的 prefab"和"工程的绑定/校验"在同一份契约上交接；当前校验只覆盖三族，文本/行为/性能仍是缺口。

---

**上一章**：[03 · 客户端框架与运行时](03-客户端框架与运行时.md) ｜ **下一章**：[05 · 确定性 Sim 与战斗判定](05-确定性Sim与战斗判定.md)
