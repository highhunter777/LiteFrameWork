# UI工作台W2（语言行 · 状态注入 · HUD 预览 · 回写工具 · 发布剥离）

《UI工作台设计》W2 执行（2026-10-16）。W2 原文列为"可选，随真实需求"六项——本批逐项落地或裁决，含**判据实测校准**（探针）与**人类操作模拟验证**（《修改指南》§2.3 口径）。

## 状态

**已完成（附验证证据）**——2026-10-16。

## 交付

| 面 | 落点 |
| --- | --- |
| 工具条（新画布） | 场景 `UiWorkbench.unity` 新增 `WorkbenchOverlay`（Canvas sortingOrder 5000——页面层 100..499 之上）+ `Cot_Tools` 紧凑两行（宽 384：中/EN/当前语言/空列表/长文本/复位注入/HUD 预览/回写工具 + 窄幅读数）。**为何分两画布**：面板排序低于页面语义层（页面上浮、不遮目检），而语言/注入/HUD 预览要在**页面打开时也能点到**——页面近乎不透明会盖住下面的面板 |
| 语言切换行 | 驱动 `SetLocale` 经 Lua 桥 `Bridge.Text.SetLocale`（设置页同一条链）→ `LocalizationService.SetLocale` → 页面索引 `ILocaleBound` 自动重刷；工具条 `Txt_Locale` 回读当前语言 |
| 状态注入 | **空列表**＝对目标页全部 `VirtualList`/`SimpleListView` 注入零行源；**长文本**＝对目标页全部 TMP 文本追加样本（记录原文，可显式还原）；**复位**＝还原文本 + 列表走"关+开"真链路复位。作用域＝**本工作台目录打开过的最近页**（页面实例取用口未对产品开：`UIService` 公开面只有 `IsOpen`）；无目标页时状态行显式提示 |
| HUD 预览 | 内容服务租约取 `BattleHudView.PrefabLocation` → 裸实例化（零接线静态画面：无对局数据源，数值/准心为设计期默认）；再点关闭＝销毁实例 + 归还租约；随面板销毁归还 |
| 回写工具 | 新件 `Assets/LiteGame/Editor/UiTools/WriteBack/`：`PrefabWriteBackScan`（差异枚举："实例 vs 源 prefab 资产"逐属性，标来源与不可回写原因）、`PrefabWriteBackApply`（`SerializedObject` 逐属性直写资产 + Undo + `SaveAssets`）、`PrefabWriteBackWindow`（菜单 `LiteGame/UI/回写/Prefab 改动回写`；**Play 内可用**）。工作台工具条 `Button_WriteBack` 经菜单路径跳转 |
| 发布剥离 | ①`UIWorkbenchDriver` 整件套三宏（`UNITY_EDITOR \|\| DEVELOPMENT_BUILD \|\| LITEFRAMEWORK_DEBUG`，与 `DevHUD.cs` 同款——release 剥净）②`LiteGameIgnoreRule.DevAssetExclusions`（`Assets/Scenes/UiWorkbench.unity` 精确排除——Scenes 收集组整目录 `CollectAll`，排除只能落在忽略规则）③守卫用例钉"路径存在 + 规则真的忽略 + 产品场景不误伤" |
| 面板抽 dev 预制体（追加，2026-10-16 用户反馈"Boot 勾选没工作台"） | 面板/工具条/驱动收成 `Assets/LiteGame/DevHUD/WorkbenchUI.prefab`（**面板资产单源**，20 字段接线随预制体走）；`UiWorkbench.unity` 瘦身为"引导件+相机+DebugTuner（工作台模式开）"；`DebugTuner.EnsureWorkbenchPanel` 在开关下自建（编辑器形态；dev 预制体不在收集组）。构建器 `AgentScripts/BuildWorkbenchPanel.cs` 改为"建预制体 + 场景瘦身"（引 `DebugTuner.WorkbenchPrefabPath` 单源常量）。**两个入口同一结果**：Boot 勾选即进工作台 / 打开工作台场景 Play。 |
| 工作台模式开关（追加，2026-10-16 用户提议） | `DebugTuner.WorkbenchMode`（字段排在"本地服务器"下方 = 新小节「UI 工作台」）+ 静态镜像 `WorkbenchModeEnabled`（沿用 `UseLocalServerEnabled` 同款 dev-only 范式，整件随三宏剥离）+ `ProcedureMain.EnterBattleSceneAsync` 的 `#if` 门禁读：勾选后 **Main 不加载对局场景组**。工作台场景的 DebugTuner **默认开**、Boot 默认关（各场景序列化值决定形态，Boot 行为不变）。**形态裁决**：不做"按活动场景名门控"（红线 5 违规形态），走既有 dev 开关面（设计 §5 已回写）。 |
| 顺手修（同批发现） | ①`_rowTemplate` 断线（W0 起）：根因＝构建脚本把 `RectTransform` 赋给 `GameObject` 字段，Unity **静默置空**（目录行一键开/关自 W0 起从未生效）；②按钮 Label 矩形 200×50 溢出邻钮抢射线——首轮可点性自检 34/38 暴露；修：Label 矩形=按钮矩形 + `raycastTarget=false` |

## 验证证据

| 项 | 结果 |
| --- | --- |
| Unity recompile | **0 错误**（多轮；`recompile_status.failed=false` + `console_status.compilationFailed=false` 双判据） |
| 新 EditMode 用例 | `PrefabWriteBackEditModeTests` **7/7**——显式记录默认勾选/数值差异默认不勾、未勾选不落盘+勾选后固化+复扫归零、实例新增组件（`IsAddedComponentOverride`）拒绝、场景引用（指向场景对象）拒绝、非实例选择显式失败、回写菜单路径单源守卫、dev 资产排除守卫 |
| Play 实机（可点性自检） | 面板+工具条 38 钮（含 2 个未激活行模板本体）：**射线命中 36/36**（页面打开状态下亦然——页面不挡面板/工具条） |
| 新守卫用例 | `WorkbenchUiAssetEditModeTests` **2/2**：①预制体存在 + 驱动 20 字段引用**齐备**（把 W0 那类"静默置空"缺陷机检化）②工作台场景**不含面板件**（面板资产单源） |
| Play 实机（Boot 勾选入口，用户场景原样复现） | Boot 的 `DebugTuner.WorkbenchMode` 勾上 → Play：面板+工具条自建（可点性 **36/36**）、场景清单只有 Boot（跳过对局场景组）、目录 `开` 点 12 真实换页（`UIForm[12] 打开`）；验完**已还原开关为 0**（Boot 默认关，YAML 复核 `WorkbenchMode: 0`） |
| Play 实机（关闭态回归） | Boot 开关关 → Play：无面板（Buttons=0）、`Boot+BattleRig+Arena01` 照旧加载（正常游戏流程行为不变） |
| Play 实机（工作台模式开关） | 工作台场景（开关开）：场景清单只有 `UiWorkbench`——**无 BattleRig/Arena01**，Console `[Main] 工作台模式：跳过对局场景组加载（DebugTuner.WorkbenchMode）`，工作台照常就绪；Boot 场景（开关默认关，回归）：`Boot + BattleRig + Arena01` 三场景照旧加载（行为不变） |
| Play 实机（操作模拟） | 射线定目标 → `pointerDown/Up/Click` 三派发（不直调回调）：开 12/14 ✅ 页面栈真实换页（`UIRoot/Bottom` 页名切换 + 状态行"开 12 完成"）；关 14 ✅；**空列表注入**（先灌 3 行合成数据 → 点击 → 列表 0 行 + 状态行"空列表注入:1 个列表（14）"）；**长文本注入** 17 处；**语言 en**：页面文本 "暂无战绩"→"No recent matches"、"返回大厅"→"Back to Lobby"（真 LText 链路）；**HUD 预览** present（截图可见准心/弹药 30/30）；**复位注入**（页面重开 14 复位列表）；**回写入口** → 编辑器窗口列表出现 `Prefab 回写` |
| 截图存证 | `AgentScripts/Artifacts/w2-page-hud-en.png`（页面英文 + 工具条 + 面板）、`w2-hud-preview2.png`（HUD 预览：准心/弹药/血条 + 紧凑工具条不压 HUD 底栏）——工作区本地（`AgentScripts/` 已忽略） |
| 判据探针 | `AgentScripts/ProbePrefabMods{,2..5}.cs`（一次性，保留供复核）：override 记录语义实测（见"偏差"节） |
| 验证工具（可重复用） | `AgentScripts/WorkbenchW2Probe.cs`——`Env`（可点性射线自检）/ `ClickNamed` / `ClickRow` / `State`（面板·工具条·页面栈·列表·长文本·编辑器窗口读数）/ `Scenes`（场景加载证据）；`AgentScripts/BuildWorkbenchPanel.cs`（面板+工具条幂等构建器，改布局后重跑即重建场景） |
| L1 门禁 | **[PASS] L1 completed 1702 tests**（`pwsh scripts/gate/test.ps1 -Lane L1 -Profile PullRequest`）。首轮**全 lane 零执行**——根因是**并行线提交 c4976015** 给 `ISceneService` 加 `Progress` 后 `Tests/LiteFramework.Core.Tests/GmSceneCommandTests.cs:21` 的 `FakeSceneService` 未同步（CS0535，非本批面）；本批顺带补该成员（按接口"无加载为 0"口径）后转绿——已在审计表登记 |
| doc-lint | **OK**（`python scripts/gate/doc-lint.py`） |
| BuildHash | **不涉及**——改动面（`LiteGame.DevHUD`/`LiteGame.Editor`/`LiteGame.UI` 无改动/`Tests`）均不在共享闭包；无协议/表源改动 |

## 与设计稿的偏差（如实登记）

| 项 | 偏差 | 处置 |
| --- | --- | --- |
| 来源判据 | 设计原文"驱动每帧写面天然屏蔽"预设 Unity 能区分"Inspector 编辑"与"脚本直写"。**实测（探针 1/5）**：`SerializedObject` 直写**不产生** override 记录；`RecordPrefabInstancePropertyModifications`（Inspector 口径）才记录，且记录项 `target` 是**源资产侧对象**；RectTransform 的位置/尺寸/锚点路径在实例创建时即登记为**默认项**、此后恒标默认（`IsDefaultOverride` 恒真——Inspector 改动同样认不出） | 工具改为两档标注：「**显式记录**」（非默认记录项，默认勾选）/「**数值差异**」（默认不勾，逐条核对「实例值 → 资产值」）；设计文档 §2 同批改写为实测口径 |
| 状态注入作用域 | 设计只写"空列表/长文本"注入 | 注入目标＝"本工作台目录打开过的页"——**不新增产品 API**（页面实例取用口未对产品开）；未开页 → 显式提示"状态注入需先经目录打开一个页面"。实测：上云入口自动开的登录页不在作用域内（目录开一次页即可）。设计 §2 登记 |
| HUD 预览层级 | — | HUD 根画布 sortingOrder 0（低于页面层 100+）——**与对局内层级一致**：开页时 HUD 在页面之下，目检 HUD 需先关页。设计 §2 登记 |
| 面板落点（W0 决策修订） | W0 记录"面板本体＝场景内 UI+驱动件"；用户反馈"Boot 勾选没工作台"暴露它无法从正常启动进入 | **改为 dev 预制体单源 + 开关自建**（设计 §1/§5 同批改写）：场景不再内嵌面板，Boot 与工作台场景两入口共用一份预制体；dev 预制体不在收集组 → 自建仅编辑器形态（开发版 Player 只享受跳过场景组），已登记 |
| 工具条布局（两轮） | 首版单行 ~1000px 宽 | 截图目检发现压住 HUD 底栏血条左段 → 改紧凑两行（≤400px，只占左下空地，镀不到血条/弹药） |
| 场景名门控跳过玩法场景 | 设计列为"如成负担随 W2 评估" | **已实现为 DebugTuner 开发开关（2026-10-16 追加；取代本行先前的"不实现"结论）**：`WorkbenchMode` 勾选后 Main 不加载对局场景组——不做按活动场景名的门控（红线 5），改走既有 dev 开关面（字段 + 静态镜像 + `#if` 门禁读，与 `UseLocalServerEnabled` 同款；红线 6 面沿用同族 dev-only 三宏豁免形态，见审计登记）。工作台场景默认开、Boot 默认关；**退役条件**＝"战斗场景按对局加载/离场卸载"落地后语义成为默认 |
| 战斗场景加载时机（用户同批提问） | 用户问"是否应开始对局才加载、对局结束卸载" | **未动代码，属改裁决面**：现状是《地图数据专项设计》D16/批1 登记的"Boot 常驻 ＋ 玩法场景加性加载（幂等常驻）"，且产品流程**零卸载**（唯一卸载是 GM 场景命令）。评估：运行期只有相机 vcam 与视觉背景依赖场景，判定面走 `tbmap` 两端解耦；搬动需改 5 处（Main 摘加载、Match/Battle 注入 `ISceneService`、Battle 收尾卸载、窗口⑤接进度、四份文档回写），并有一个视觉后果（Main/结算失去地图背景）与一个编辑器坑（"领养"场景卸载是 no-op）。**建议形态**：Match（JoinAck 后）加载并报进度 → Battle 收尾统一卸载。**待裁决后落地** |
| 发布剥离口径核对 | 设计 §5 登记"职责在发布管道，随发布批核对" | **核对结果＝两个真实缺口并本批修复**：①`UiWorkbench.unity` 原会被 Scenes 收集组整目录收进生产包；②`UIWorkbenchDriver` 原无三宏守卫（仅靠"场景不入构建列表"保持不可达，IL2CPP Minimal 剥离不保证移除）。另登记：L0 的"开发内容排除"静态检查（《热更与内容发布专项设计》§304）仍未实施——进审计表 |

## 追加批：流程整理（2026-10-16 用户裁决：开发页 dev 覆盖件 / 训练场独立地图数据 / 工作台改场景加载 / 房间列表文案接受）

**已落地（本批：工作台载体 + 入口整理）**

| 面 | 落点 |
| --- | --- |
| 工作台载体＝场景 | `UiWorkbench.unity`＝"相机（AudioListener 禁用态）+ 携带引导件 GameEntry（禁用态）+ `WorkbenchSceneEntry` + DebugTuner（工作台模式开）+ `WorkbenchUI.prefab` 实例"；`DebugTuner.EnsureWorkbenchScene` 在开关下**加性加载**（编辑器形态）。两形态共用同一场景：**直接 Play**（场景入口启用携带引导件走真装配）/ **Boot 加性加载**（不二次装配） |
| 入口矩阵（单点决策） | `ProcedureMain.ResolveLanding`：直入训练场/直入工作台 → **不开页**；本地服务器 → **大厅页**（跳过登录）；Meta → 登录页；否则不开页。`PageFlow.OpenEntryOnce` → `OpenLanding(Landing)` |
| 直入训练场（新开关） | `DebugTuner.DirectToTraining`（排在 `WorkbenchMode` **之上**，优先级更高）：Main 就绪即发测试模式请求（`ApplySnapshotOrDefaults` + `TestModeRuntime.RequestEnter`，请求桥读即清） |
| 开发页（dev 覆盖件） | 新件 `Assets/LiteGame/DevHUD/DevMenuPanel.cs` + `DevMenuUI.prefab`（画布 6000，**任何页面在场都可点**）：左上「开发」钮 → 面板 = 测试模式（训练场）/ UI 工作台 / 关闭 + 形态读数；由 `DebugTuner.EnsureDevMenu` 在编辑器形态开出（`DevMenu` 字段默认开） |
| F9/F10 删除 | 代码 3 处（`ProcedureMain` F9/F10 块、`ProcedureBattle.LeaveKey`）；离场入口＝UI（ESC→设置页→退出对局）；dev 入口＝开发页 + 直入开关 |
| dev 入口进局收页 | `PageFlow.CloseOpenPagesForBattle`（dev 入口直入对局时在开页面会盖住 HUD——产品路径由"房间页→开局"自然收口） |

**验证证据（Play 实测）**

| 项 | 结果 |
| --- | --- |
| 直接 Play 工作台场景 | 场景入口启用携带引导件 → 真装配 → 就绪 + 可点性 **36/36**，只加载 `UiWorkbench` |
| Boot 工作台模式加性加载 | `Boot + UiWorkbench` 两场景、面板 **36/36**、无对局场景组；修复"2 个 AudioListener"告警（加性形态用宿主的监听器） |
| 开发页 | 出场且「开发」钮射线命中；**测试模式**点击 → 进局（`阶段:Battle`）+ 在开页面被收（`Page_Login(inactive)`）；**UI 工作台**点击 → 工作台场景加载（`UiWorkbench loaded`，就绪:True） |
| 本地服务器 → 大厅 | Boot 临时开 `UseLocalServer` → 启动落 `Page_Lobby`（无登录页；房间列表显"加载失败"文案＝无 Meta，用户已接受）；验完**已还原 `UseLocalServer: 0`** |
| 编译/门禁 | Unity 0 错；doc-lint OK；L1（见下） |

**追加批 III-a：对局场景组按对局加载 / 离场卸载（2026-10-16）**

| 面 | 落点 |
| --- | --- |
| 单源 | 新件 `Assets/LiteGame/App/Procedure/BattleScenes.cs`：`Rig`/`Arena`/`Training` 路径 + `MapFor(testRoom)`（**视觉与判定必须同图**——训练场地图数据未落地前恒回首图，行内挂账） |
| 加载（Match） | `ProcedureMatch` 注入 `ISceneService`；JoinAck 后、移交 Battle 前 `LoadBattleScenesAsync`：Rig → 地图，进度进**"进房等待场"**窗口（`LaunchLoading.Report` 离散步进）；失败上抛 → Match 既有失败路径收口 |
| 卸载（Battle） | `ProcedureBattle` 注入 `ISceneService`；`finally` 收尾（`DetachView` 之后）`UnloadBattleScenes()`——Rig ＋ 两张地图（幂等；失败只记日志，不阻塞离场）；编辑器"领养"形态卸载是 no-op（手测预期） |
| Main 减负 | `ProcedureMain` 摘掉场景组加载、窗口④进度上报、工作台跳过读——Main 阶段只有启动场景 + UI；`WorkbenchMode` 的"跳过"语义随之退役（开关保留为工作台入口） |
| 装配 | `ContainerModule`：Main 去 `_scenes`；Match/Battle 加 `_scenes` |

**验证证据**：Boot Play → `Scenes` 探针只有 `Boot`（Main 不再加载）；开发页「测试模式」→ 进局后 `BattleRig + Arena01` 已加载（`阶段:Battle`、动画槽建立）。**待补（根因已查明＝存量缺陷，非本批）**：离场卸载的端到端 Play 确认被**设置页打不开**挡住——复验时发现 `Page_Settings`（HEAD 即有）两处存量缺陷：①`Txt_Exit`/`Button_Exit` 的 `BindName` 都是 `Exit` ⇒ `OnInit` 判重 fail-fast（**已修**：`AgentScripts/FixSettingsExitBindNames.cs`，节点名同文）；②`OnShow` 类型不符 `绑定索引[Dropdown_Quality] 期望 Dropdown 实得 Image`（**未修**，待定位 `BindIndexBuilder` 解析面与嵌套下拉件）。两条均已登记审计表；该页打通后即可用"ESC→设置页→退出对局"复验卸载。

**追加批 III-b：训练场＝第二张地图（2026-10-16，用户裁决"训练场作为第二张地图就行"）**

| 面 | 落点 |
| --- | --- |
| 场景标记件（原缺） | 新件 `AgentScripts/BuildTrainingGroundMap.cs`（幂等重建）：`MapCollision` 根 ＋ **7 件掩体代理**（BoxCollider/正缩放/无旋转）＋ 原顶层 `Cube` 归入采集根（`cover_04`）＋ **`spawn_00..15`**（半径 22 环）＋ `map_bounds`（`MapBoundsMarker` 0 / 50×50）——**布点即常量、改这里重跑即改布局**（最小灰盒，可后续调） |
| 数据布局 | `Luban/Data/map.json` → **`Luban/Data/Map/map_arena01.json`**（每图一文件）；`map.xml` 表注册 `input="Map"`（读整目录）——**加图＝加文件**，不改表声明 |
| 烘焙器 | `BakeTargets` 加 `MapTarget` 结构 ＋ `ArenaMap`/`TrainingMap`（行 id 引 `MapTableProjection.TrainingMapId` 单源；`BakeTargets` 改 public——run_script 以文件为编译单元直调）；`SimMapBaker.Bake(target)` 参数化（`All()`/`AllTraining()` 对外入口） |
| 表链 | 训练场烘焙：**8 障碍 ＋ 16 出生点**，摘要 `c78441a74f6bc5f9`（首图 `9c3d504907d8210e` 不变 ✓ 迁移内容保真）；`Luban/gen.bat` → `tbmap` **两行**（lua/bytes 双证）；`gen-build-hash.py`：shared `8ee08553b5e5f20f` / meta `83ce53cadebd7fd1`，`--check` 复算 **OK**（`Generated/*.cs` 无 schema 变化 ⇒ 常量不变） |
| 运行期选行 | `MapTableProjection.TrainingMapId = 2`（单源）；`BattleScenes.MapIdFor(testRoom)` 与 `MapFor(testRoom)` **同一个切换**（视觉＝判定同图）；`ProcedureMatch` 本地服建传输按模式选行；`AccountSession.TestRoom` 带上标记 → `MatchSessionFactory` 取同一行（客户端预测与本地权威同图） |

**门禁**：Unity 编译 0 错；**L1 [PASS] 1702**（含一处用例维护：`MapProjectionTests.篡改表字节_行内digest不符_拒载` 原用"尾部翻一位"——多图后尾部字节属最后一张图、改不到首图，改为**篡改首图行内 `content_digest` 串**，与行数无关）；doc-lint OK；`gen-build-hash.py --check` 双常量复算 OK。

**验证证据（Play 实测）**：开发页「测试模式」→ `[Match] 对局场景组已加载:Rig=BattleRig Map=Assets/Scenes/TrainingGround.unity`，场景清单 `UiWorkbench + BattleRig + TrainingGround`（**无 Arena01**），`阶段:Battle`；截图 `AgentScripts/Artifacts/w3-training-ground.png`（进房等待场＋HUD 100/100·30/30）。

**遗留**：训练场布点为最小灰盒（7 掩体/16 环型出生点/50×50 边界）——要打仗手感按需调 `BuildTrainingGroundMap.cs` 后重跑再烘；真服务器多图（协议无 `map_id`，D10）仍未设计。

**（先前查清的前置，供对照）**

**硬发现**：`TrainingGround.unity` **缺全部烘焙标记件**（无 `MapCollision` 根、无 `spawn_*`、无 `map_bounds`；只有顶层 `Cube` 带一个 BoxCollider）——`SimMapBaker` 会在"找不到 MapCollision 根"处直接 ABORT。故顺序为：

1. **补场景标记件**（unity-pipeline，禁直改 .unity）：建 `MapCollision` 根 → 障碍代理（仅 Box/Sphere/Capsule、正缩放、yaw∈{0,90,180,270}、禁 MeshCollider）→ `spawn_00..15`（不挂碰撞体）→ `map_bounds` + `MapBoundsMarker`（HalfWidth/HalfDepth>0 覆盖障碍）。
2. **烘焙目标扩展**（单源）：`BakeTargets` 加训练场目标（路径/map_id=2/name/输出文件）；`SimMapBaker` 入口参数化（`All()` 保持 Arena01 不变）。
3. **烘焙** → 产物进 Luban 数据源（现 `Luban/Data/map.json` 单行；多行形态需与 `Luban/Defines/map.xml` 的 `input` 口径一起定：目录输入或多行 JSON——生成链细节待小跑验证）。
4. `Luban/gen.bat` → `tbmap.bytes` 两行 + `Generated` 重生成 → `python scripts/codegen/gen-build-hash.py`（`Generated` 在共享闭包内）。
5. 运行期选行：`MapTableProjection` 加训练图 id 常量；`ProcedureMatch` 本地/测试路径按 `testRoom` 选行（真服务器侧协议无 `map_id`，多图需另设计——本轮范围外）。
6. `BattleScenes.MapFor` 翻到 `testRoom ? Training : Arena` + 直入训练场复验；文档/审计销账。

**遗留（本追加批未做，方向已裁决）**

- **对局场景组按对局加载/离场卸载**：加载搬到 `ProcedureMatch`（JoinAck 后、移交 Battle 前——必须早于 Battle 相机硬门；进度接"进房等待场"窗口），卸载在 `ProcedureBattle` 收尾；地名按模式参数化（正式 Arena01 / 测试 TrainingGround）。
- **训练场独立地图数据**：`tbmap` 现只有 `map_id=1`（arena01）——训练场需烘焙（`BakeTargets` 已列 TrainingGround）+ 新表行 + 生成物重生成（表源改动→跑 build-hash）+ 判定面摘要核对。
- **退出链在 dev 落点复用**：ESC→设置页→退出 的动作按落点分派（退出测试对局/关工作台 → 回大厅页），退出钮文案键；dev 落点收尾建议走"读即清请求桥"（`TestModeRuntime` 先例）。
- **从对局/战斗中开工作台**不会卸载已加载的战斗场景（正常入口路径在按需加载落地后天然无此场景）；如需强制收，走 `ISceneService` 卸载 + 阶段门（本次未做）。

## 待做（后续批次，按建议顺序）

| 序 | 项 | 现状 | 落点/线索 |
| --- | --- | --- | --- |
| 1 | **设置页 `Dropdown_Quality` 绑定类型不符**（`OnShow` 抛 `期望 Dropdown 实得 Image`）——设置页仍打不开，"退出对局"链整条不通 | 未修（存量，HEAD 即有；审计表已登记） | 定位 `BindIndexBuilder` 类型解析面 vs 页面内嵌套下拉件（`Widgets/Tmp_Dropdown` 实例 + BindName 经 PrefabInstance 覆写为 `Dropdown_Quality`）；先跑 `AgentScripts/FixSettingsExitBindNames.cs` 那次的复现路径（Play→开发页→测试模式→HUD 菜单钮） |
| 2 | **离场卸载的端到端 Play 确认** | 代码已接（III-a），验证被第 1 项挡住 | 第 1 项修好后：ESC/菜单 → 设置页 → 退出对局 → 确认弹窗 → 断言 `Scenes` 只剩启动场景、`[Nav] Replace id=12` |
| 3 | **IV：dev 落点退出链复用** | 未做 | ESC→设置页→退出 在"直入训练场/直入工作台"下可用：退出动作按落点分派（离场＋卸载 / 关工作台场景）、退出钮文案按落点（需 `tbltext` 键）、dev 落点收尾走读即清请求桥（`TestModeRuntime` 先例） |
| 4 | **V：F9/F10 文档扫尾** | 项目说明 01/README/02 已改；其余未扫 | 07 联测手册（F 键流程多处）、代码地图、修改指南、上云页面层级设计；历史施工记录按惯例保留 |
| 5 | 训练场布点为最小灰盒（7 掩体/16 环形出生点/50×50 边界） | 可调内容，非缺陷 | 改 `AgentScripts/BuildTrainingGroundMap.cs` → 重跑 → `SimMapBaker.AllTraining()` → `Luban/gen.bat` → `gen-build-hash.py` |
| 6 | 真服务器多图 | 未设计（协议无 `map_id`，D10） | 多图跨服需另行设计；当前多图只对本地服/测试房成立 |

## 遗留（如实）

- **发布剥离的门禁面**：现在是"忽略规则 + 单源守卫用例"（钉路径存在性与规则行为），**没有**在出包流程里断言"bundle 清单不含 dev 件"——L0 侧缺口已登记审计表。
- **状态注入不覆盖"非工作台打开"的页面**（如上云入口自动开的登录页/流程开的页）——需先在目录开一次页；若后续要覆盖，须给 `UIService` 开只读"取已开 form"口（产品 API，另批裁决）。
- **回写工具边界**：结构面（新增组件/新增子对象）与场景对象引用显式列出并拒绝；不回滚实例 override（要实例回归干净态用 Unity 原生 Overrides ▸ Revert）。

## 操作说明

日常操作（打开/就绪、开页面、反馈面、语言与边界态注入、HUD 预览、回写闭环、排障）已抽为手册：[Docs/项目说明/08-UI工作台.md](../../项目说明/08-UI工作台.md)（单源，避免本文件与手册两处同文）。
