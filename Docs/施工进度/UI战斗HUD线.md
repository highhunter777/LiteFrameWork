# UI 战斗 HUD 线（批A prefab 化 → 批C 榜面与退场 → 子件排布）

> **旧体系冻结（2026-10-22）**：本卷记录旧 UI 体系的施工历史，不再对应现行契约；新体系设计见 [UI 系统总设计](../设计文档/客户端/UI系统/UI系统总设计.md)。保留原文作为历史证据，不回填新状态。


> **定位**：战斗 HUD 从场景内建到 prefab 化的迁移线，以及排行榜、弹药读出、子件与容器的后续批。

> **章法**：一卷一条线；每章＝原一份批次记录（标题、正文、证据原样并入，仅降一级标题）。
> **状态图例**（全目录统一）：`已完成（附验证证据）` / `进行中` / `未开始` / `阻塞（附原因）`。
> 每章开头的引用块是该批的原始状态与证据；本卷只做归并与章节化，未改写任何批次结论。

## 章节索引

| 章 | 标题 | 原文件 |
|---:|---|---|
| 1 | 战斗HUD批A（prefab 化 + 引用件 + 收集 + 场景退役） | `战斗HUD批A.md` |
| 2 | 多图批：训练场布点可调 ＋ 真服务器多图 | `多图与训练场布点.md` |
| 3 | 局内排行榜 + 死亡/复活表现（G2-P1 客户端余部 · HUD 批C「比分」与阵亡面） | `局内排行榜.md` |
| 4 | 结算页玩家排行（Cot_Records 改造） | `结算页玩家排行.md` |
| 5 | 战绩独立页（Page_Records） | `战绩独立页.md` |
| 6 | HUD 子件与容器排布批（排行格 / 击杀播报） | `HUD子件与容器排布.md` |
| 7 | HUD 弹药读出接线（当前弹匣 / 剩余备弹） | `HUD弹药备弹读出接线.md` |

---

## 第1章 战斗HUD批A（prefab 化 + 引用件 + 收集 + 场景退役）

<a id="ch1-战斗HUD批A"></a>

> **原文件**：`Docs/施工进度/战斗HUD批A.md`（归并前独立成文，内容整段并入本章）

《战斗HUD专项设计》/《战斗HUD层级设计》批 A 执行（2026-10-11）。批 B 装配拆分已由并行 BattleViewRig 批先行完成——本批与其合流，**HUD 全链按新规范落位**。

### 状态

**已完成（pipeline + Unity 证据齐全）**。

### 交付

| 面 | 落点 |
|---|---|
| 模板 prefab | `Assets/ShareResource/UI/Hud/Hud_Battle.prefab`——训练场现役树 1:1 转制：§9 词缀化改名 23 处（`Hud_Battle/Cot_Crosshair/{Icon_Hip,Icon_Ads}`、`Cot_Panel/{Cot_Vitals,Cot_Weapon,Cot_Skills}`…）＋ **Icon_Ads 38→64（用户裁决）** ＋ 根挂 `BattleHudView`（SerializedObject 接线 8 字段） |
| 引用件（新） | `App/Presentation/BattleHudView.cs`——8 个序列化字段（CrosshairRoot/Hip/Ads/PanelGroup/HpFill/HpText/AmmoText/ReloadLabel）+ `PrefabLocation` 单源；零 Find 契约 |
| 收集 | collector 新组 **UIHud**（`Assets/ShareResource/UI/Hud`，PackSeparately，ui tag）——HUD 件级热更 |
| 场景退役 | `TrainingGround.unity` 删 `/Battle HUD` 对象（场景不承载 HUD 视觉）；HUDDesign 同日改造为 **UiWorkbench**（UI 工作台场景，见《UI工作台设计》——HUD 预览随工作台 W2 可选） |
| 接线（Rig） | `BattleViewRig`：`AcquireHudAsync`（租约→_viewScope→实例化→取引用件）+ `AttachCrosshair/AttachHud` 改 View 引用 + Dispose 回收 HUD 根件；**六条路径常量与 FindChildGameObject 退役——Find 归零** |
| 退役 | `AgentScripts/BuildBattleHud.cs`（构建器双写根源消除；本地未入库件留待删） |

### 迁移过程（如实，三段收敛）

1. v1 顺序改名 bug：父节点先改名致子路径失配，ABORT 未拦住保存——产出半改名 prefab 且场景对象已退役（场景退役本为目标 ✓，树完整在 prefab 内）；
2. v2 fail-fast 生效拦截（接线不全不保存），但改名表仍是旧路径；
3. v3 改「**先收集后统一改名**」模式对齐盘上状态——×23 全中、结构校验 10/10 OK、ADS=64、保存成功。

### 验证证据

| 项 | 结果 |
|---|---|
| 结构校验 | 10/10 OK（层级树全节点按新名在位）＋ `ADS size=64` ＋ `root=Hud_Battle` |
| collector | UIHud 组（Assets/ShareResource/UI/Hud，PackSeparately，ui） |
| Unity recompile | `completed / failed:false / 0 错误`（含 BattleHudView/BattleViewRig 接线） |
| `BattleCrosshairEditModeTests` | **4/4** |
| `VfxServiceEditModeTests`（回归面） | **17/17** |
| Find 归零 | `BattleViewRig` grep `GameObject.Find/FindChildGameObject` = 0 |

### 遗留（如实）

- 实机观感手测（F9：血条/弹药/换弹标签/准心双形态）待本机——逻辑面测试已绿；
- `BuildHudPrefab/2.cs` 迁移脚本留 AgentScripts（未入库）供复跑；
- 技能栏数据面（批 C）按 UI 框架 U2 节奏另行点单。

### 血条正式化（随批收口，2026-10-11）

批 A 产出的 HUD 里，血条遗留两态并存：旧构建器框条（`Image_HpFrame`/`Image_HpFill`/旧 `Txt_Hp`，Filled 图形血条）与用户手工粘贴的 Synty 共享件 `HUD_MilitaryCombat_StatsList_Item_05`（草名 `Item_00`，内含 UGUI `Slider`）。驱动已接 `Slider`，但旧框条仍挂在 prefab 上、`Item_00` 未合规范。本批收口：

| 面 | 落点 |
|---|---|
| 血条正式化 | `Cot_Panel/Cot_Vitals/Item_00` → **`HpBar_Player`**（§9 词表段 `HpBar_`——命名词表 §1.3 已登记血条类型段，业务层实例＝`<类型段>_<语义>`，对齐现役 `HpBar` 控件模板） |
| 内部子件词缀化 | `Bar`/`IconBox`/`ICON`/`Label_Stat` → `Cot_Bar`/`Cot_IconBox`/`Icon_Health`/`Txt_Hp`；UGUI `Slider` 及其固定子结构（Background/Fill Area/Fill/Handle Slide Area/Handle）**保留原名**（组件契约，改名即断 UGUI 引用） |
| 旧血条退役 | `Image_HpFrame`/`Image_HpFill`/旧 `Txt_Hp` 删除；`Cot_Panel` 全屏空 Image（`m_Sprite={fileID:0}` 纯射线面）同时清除——HUD 整组显隐走 CanvasGroup，不需射线面 |
| Slider 基线 | `interactable=false`（纯显示）、`value=1`（满值制作基线）；子树射线兜底关闭（Vitals 剩余 raycastTarget=0） |
| 接线 | `BattleHudView._hpSlider`→`Slider`、`_hpText`→新 `Txt_Hp`（SerializedObject 重连；8 字段全部有效） |

#### 验证证据

| 项 | 结果 |
|---|---|
| 结构复验 | `HpBar_Player`/`Cot_Bar/Slider`/`Cot_IconBox/Icon_Health`/`Txt_Hp` 全 `OK`；旧件 `frame/fill/item00残留` 全 `false` |
| 组件完整性 | 全树无 `[MISSING!]` 组件/丢引用；`BattleHudView` 8 字段全部解析（`_hpSlider=Slider`、`_hpText=Txt_Hp`） |
| L1 | **1594/1594 通过** |
| `BattleHudEditModeTests` | 3/4（`弹药_装备槽弹匣数与换弹态_未装备显示占位` 失败——**基线复算确认与本次改动无关**：stash prefab 后同测试同失败，属驱动默认武器态既有问题，另立） |
| 执行脚本 | `AgentScripts/AdaptHudHpBar3.cs`（未入库，幂等） |

#### 遗留（如实）

- 上述 EditMode 1 例失败为**既有**（`BattleHudDriver` 在未装备判定前先走武器分支写出 `"0"` 而非 `"--"`），非本批引入——按本文档"如实"原则登记，待单独批次处置；
- 观感调参（血条尺寸/配色定稿）待实机手测后按需微调 prefab。

### 血条解包收口（2026-10-11）

制作侧将 `HpBar_Player` 子树由 Synty 共享件 `HUD_MilitaryCombat_StatsList_Item_05` 的嵌套实例**完全解包**为普通对象（制作侧手工操作）——prefab 自持全部视觉与几何，去嵌套依赖。

| 面 | 结果 |
|---|---|
| 形态 | 全树 `PrefabInstance` 归零（无 `m_CorrespondingSourceObject` 非零引用、无 `stripped` 块、无嵌套实例）；`HpBar_Player`/`Cot_Bar`/`Slider`/`Cot_IconBox`/`Icon_Health`/`Txt_Hp` 均为普通节点 |
| 几何 | 位置/尺寸为制作侧手调实况（`HpBar_Player` 540×70 @ (342,35)）——**prefab 实况即权威**，不以文档旧值回改 |
| 接线 | `BattleHudView` 8 字段仍全部有效（`_hpSlider`→`Slider`、`_hpText`→`Txt_Hp`，经重载复验） |
| 素材依赖 | 展平后为直接 sprite 引用（Synty `Sprites/**` 6 件 + 准心 2 件 + TMP/UGUI 内建），无断链 |

#### 遗留（如实）

- ~~`Cot_Panel`（全屏组）上有一 `sprite=null`、`raycastTarget=true` 的空 Image（制作侧作**面板边界对照**用）——制作侧声明**自行删除**，本批不动；删除前该件会拦全屏输入（违《层级设计》§0 事件隔离），**发布前必须清除**（回归登记，见下条）~~ **已清**：对局退场链构建器（2026-10-14）实测该件已不在（制作侧已删）；同批射线隔离守卫另抓到 **`Bg_Weapon` `raycastTarget=true`**（未登记的 §0 违规面——功能无感、双保险破坏）并归零，§0 不变量由 `BattleHudMenuEditModeTests` 钉住（全 HUD 恰 1 个 raycastTarget=true＝菜单钮）；
- 制作侧称此后仍会手工调整血条，prefab 以制作侧实况为准。

### 血条模板化（2026-10-11）

用户裁决：血条**不再是 HUD 私有结构**，改为**控件模板 `Tmp_HpBar` 的使用变体**——模板＝血条单一形态来源。本批将上一段的解包产物**回落为模板 PrefabInstance**，并把模板本身重建为 HUD 现役形态。

| 面 | 落点 |
|---|---|
| **模板重建** | `Assets/ShareResource/UI/Widgets/Tmp_HpBar.prefab` 重建为 HUD 形态：`Tmp_HpBar` 根 + `_Bar`（`_Slider`/`_Background`/`_FillArea{_Fill,_BackFill}`，§9.4 `_` 私有）+ **`_BackFill`（半透明后条·延迟滑落）** + `Cot_IconBox/Icon_Health` + `Txt_Hp`；根挂 `LiteGame.UI.HpBar` + `BindNode`。几何/Sprite/颜色一比一取自 HUD 现役血条 |
| **驱动改写** | `LiteGame.UI.HpBar` 改由 **UGUI `Slider`** 驱动前条（取代原 `FrontFill` `fillAmount`）；后条 `BackFill` 按 `BackLerpSpeed` 逐帧滑落追赶（**计时改走 `UiAnimationClock.Delta`**——§5／红线 9）；上升瞬时跟上。**公开 API `Set(current,max)` 与 `HpBar` 类型不变**——`UIDemoPage`/`UIBindIndex.SetHp` 零改动 |
| **HUD 改实例** | `Hud_Battle` 的 `Cot_Panel/Cot_Vitals/HpBar_Player` 替换为 `Tmp_HpBar` 的 **PrefabInstance**（只 override 位置/尺寸 540×70 @ (342,35)＋根名） |
| **HUD 接线** | `BattleHudView` 的 `_hpSlider`+`_hpText` 两字段 → 单个 **`HpBar _hpBar`**（指实例根组件）；`BattleHudDriver` 构造收 `HpBar`、`Tick` 调 `hpBar.Set(hp,max)`；`BattleViewRig` 随改。**单点组件引用——避免嵌套深层字段 override 的脆弱性** |

#### 验证证据

| 项 | 结果 |
|---|---|
| 模板/实例树一致 | 两者逐节点比对**全同**（含 `_Fill`/`_BackFill`/`_Handle`/`_Background` rect）；全树无 `[MISSING!]` 组件 |
| `BattleHudView` 字段 | 7 字段全部有效（`_hpBar=HpBar_Player(HpBar)`） |
| 射线隔离 | Vitals 子树 raycastTarget=0（全关） |
| L1 | **1594/1594** |
| `VisualSingleSourceEditModeTests` | **8/8** |
| `BattleHudEditModeTests` | 3/4（`弹药_装备槽弹匣数与换弹态_未装备显示占位` 失败——**既有**，与本批无关，见上节登记；HP 用例已随新 `HpBar` API 改绿） |
| 执行脚本 | `AgentScripts/BuildHpBarTemplate.cs` + `WireHudHpBarInstance.cs`（未入库，幂等） |

#### 遗留（如实）

- 实例带若干**良性 no-op override**（`_Fill`/`_Handle` 的 anchor 记录，实测与模板值全同、不影响渲染）——Unity 生成，无法在不破坏实例的前提下剥离，登记备查；
- `HpBar.cs` 后条走自驱 `Update`（控件自身动画），计时已收敛 `UiAnimationClock`；对《UI框架总设计》U3「HUD 分级驱动/DirtySet」目标项留待批 C；
- 双条后条 `_BackFill` 透明度基线 0.35，待实机目视定档。

#### 绑定体系澄清（随批补，2026-10-11）

改造中浮现一个真实张力：**血条被模板化后，模板根挂了 `BindNode`（无人消费）**——HUD 侧走 `BattleHudView` 序列化引用、不读 `BindNode`。用户质询"为何 UI 控制脚本不用框架绑定生成"，核查后**确认为设计裁决而非漏用**，据此澄清（不改代码）：

- HUD 归**对局表现域**（专项设计 §2.2），不走 `UINavigationController`／无 `UIForm` 承载，故绑定体系（`UIBindBase` A/B 路径——`BindIndexBuilder` 扫 `BindNode` 建索引／生成类 `CollectControls` 登记）**不适用**；
- `Tmp_HpBar` 模板根的 `BindNode` 为**未来页面消费者**（队伍血条、设置页血量预览等）预留——届时由页面按 §9 填 `BindName`；模板不留非空 `BindName`（§9.4）与"HUD 不读 BindNode"自洽；
- 逐条对照表 + 边界写入《战斗HUD层级设计》§2.1.1；《战斗HUD专项设计》§2.4 补交叉引用。

---

## 第2章 多图批：训练场布点可调 ＋ 真服务器多图

<a id="ch2-多图与训练场布点"></a>

> **原文件**：`Docs/施工进度/多图与训练场布点.md`（归并前独立成文，内容整段并入本章）

《UI工作台W2》待做表第 5/6 项的落地记录（2026-10-16 用户指令："做训练场布点可调与真服务器多图"）：
把"训练场布点"从**未入库脚本 + 不可复核的整链重跑**升为版本化、非破坏、带烘焙期校验的可调内容；
把"多图"从"只对本地服/测试房成立"升为**真服务器全链**（Meta 出号盖章 → 票据 → 房间按表建图 → JoinAck 权威回执 → 客户端选表行与场景）。

### 状态

**已完成（附验证证据）**——2026-10-16（已提交 `5857ca4b`）。

### 交付 · A：训练场布点可调

| 面 | 落点 |
| --- | --- |
| 版本化布点工具 | 新件 `Assets/LiteGame/Editor/Baking/TrainingGroundLayout.cs`（原 `AgentScripts/BuildTrainingGroundMap.cs` 迁入产品 Editor 程序集）：**`EnsureSeed()`＝非破坏补齐**（只补缺失件——手工拖过的布点不被重跑抹掉）／**`RebuildSeed()`＝全量重建种子**（清旧 `MapCollision`，原 `All()` 语义）；菜单 `LiteGame/地图/训练场布点：…` 两条 + 烘焙工具箱「地图」页两钮 |
| 布点安全闸（烘焙期） | `SimMapBaker`：出生点 **不落障碍内**（2.5D：站立区间 [y, y+1.8]（`BodyBake.Height` 单源）与障碍 y 区间相交 ＋ XZ 落在圆/AABB 内）／**不越边界**（`map_bounds` 的 half_width/half_depth）——违例 fail-fast 并点名（不产半成品）。两条既有地图实烘均通过 |
| 旧入口 | `AgentScripts/BuildTrainingGroundMap.cs` 收成薄转发（`All() → RebuildSeed()`）——旧调用点语义不变 |
| 布点调整链路（写进文档） | 手工调（场景里拖 `spawn_NN`/掩体）或改种子 → 烘焙工具箱「地图」页（训练场烘焙）→ `Luban/gen.bat` → `python scripts/codegen/gen-build-hash.py`；约束（≤16 出生点、名前缀、正缩放、yaw 仅 90° 倍数、不落障碍内/不越界）见《地图数据专项设计》§8.5 |

### 交付 · B：真服务器多图

| 面 | 落点 |
| --- | --- |
| 地图行键的权威载体 | **Join 票据 v2**：`JoinTicketFormat` 追加 `mapId` 段（14 段，签名覆盖内——与其余绑定项同等防篡改；旧 v1 票据不静默接受）；`JoinPrincipal.MapId`；`HmacJoinTicketValidator` 解出。**协议只加 `JoinAck.map_id=8`**（只增不改；客户端不发明行键） |
| Meta/Lobby | `CreateRoomCommand.MapId` / `JoinTicketCommand.MapId`（0 = 未指定；负值 400）→ `LobbyTicketSigner.Issue(..., mapId, ...)` 盖章；`RoomReport/RoomView.MapId`（房间列表可见性，负值 InvalidFields） |
| RoomServer | 房间模板可选 `map_id`（`Config/roomserver.json` 的 `rooms.<模板>`）；`RoomConfig.MapId` / `RoomRuntime.MapId`；新件 `Server/RoomServer/ServerMapCatalog.cs`（行键→判定图，缓存装载，缺行 fail-closed）→ `HostAssembly.Inputs.MapCatalog` → `ServerHost`：**建房行键优先级＝票据盖章 ＞ 模板 ＞ 宿主默认图**；模板与票据冲突 → 拒绝建房；缺行 → 拒绝建房；**既有房间以房事实为准**（票据不一致 → 拒绝进房）；`JoinAck.map_id` = 房事实；`RoomSummary.MapId` 随注册心跳上报 |
| 客户端选图（单一裁决） | `RoomClient.MapId`（JoinAck 定值，重连同源刷新）→ **`BattleScenes.ResolveMapId`（0 = 服务器未标注 → 首图口径）**；场景路径**查地图表 `scene` 列**（`IMapProvider.ScenePath/ScenePaths`）——`BattleScenes` 不再持逐图路径常量；`ProcedureBattle`（开局入场加载；加载链修订后）与 `MatchSessionFactory`（判定图行）同用该裁决；`ProcedureBattle` **离场卸载按表枚举地图场景**（加图不改卸载代码）；本地服 `LocalServerTransport.ForRoom(..., mapId)` 同一条权威回执路径（测试房仍 = 训练图） |
| 地图表新列 | `Luban/Defines/map.xml` 的 `map` bean 加 `scene:string`（地图场景路径）；烘焙器写出（`BakeTargets.MapTarget.ScenePath` 单源）——**加图＝加文件 + 加场景**；两条既有行重烘（摘要不变：`9c3d504907d8210e` / `c78441a74f6bc5f9`） |
| Lua/门面 | `Bridge.meta.CreateRoom(mapId, cb)`（0 = 未指定）；房间列表行新增 `mapId`/`mapName`（名按地图表 `name` 列解析，表未装载 = 空串），大厅页房号行显示 `房号 · 地图名`（显示面，非判据） |
| 生成物 | `gen_proto.py`（JoinAck）+ `Luban/gen.bat`（scene 列）+ `gen-build-hash.py`：shared `086f484db386aefa` / meta `d5235ece5f8e748d` |

### 验证证据

| 项 | 结果 |
| --- | --- |
| L1 门禁 | **[PASS] 1715 tests**（`pwsh scripts/gate/test.ps1 -Lane L1 -Profile PullRequest`；HEAD 基线 1702） |
| 新 L1 用例 | `RoomMapSelectionTests`（10 例）：票据行键建房/模板固定/冲突拒绝/缺行拒绝/单图宿主拒绝/既有房不一致拒绝/未标注回执 0/模板解析/负值抛；`MapProjectionTests` 加训练场行双路径对账 ＋ **行内 scene 列非空且文件存在、行键唯一**；`TestMaps.TrainingGround()` 快照夹具（8 障碍/16 出生/±50，摘要锚 `c78441a74f6bc5f9`） |
| 票据/协议面用例维护 | `JoinTicketIssueUseCaseTests`（13→**14 段 / v2** ＋盖章值可解出）、`JoinTicketValidatorTests`（篡改矩阵加 mapId 段 + 签名字段号 13）、`TestTicketIssuer`（mapId 透传）——**未改口令/密钥/绑定项语义** |
| L3（真 HTTP + 真 KCP） | 新用例 `多图_创建训练场房间_真票据真进房_JoinAck回执行键2`：Meta 真 Kestrel `create-room{mapId=2}` → 真票据 → 真 KCP/信封进房 → **JoinAck.map_id=2** ＋ 房事实行键 2 ＋ 判定图 == 训练场快照摘要（656 ms，独立于 FFA 主链） |
| L3（Meta 端点） | `LobbyHttpTests` 加 2 例：带地图行键的票据经房间端**真实验签器**验出 `principal.MapId==2`；负值 → 400 |
| Unity EditMode | `SimMapBakerEditModeTests` **15/15**（含新负例"出生点落障碍内/越界"）；`MetaEntryEditModeTests` 14/14；`MetaPagesEditModeTests` 23/23；`MetaClientEditModeTests` 10/10；`WorkbenchUiAssetEditModeTests` 2/2 |
| Play 实测（真服务器多图，人类操作模拟） | 本机起真 RoomServer（`Config/roomserver.json` 模板临时钉 `map_id=2`、`expected_players=1`，**验完已还原**；无 `--ticket-key` = 原型形态，行键走模板）→ 可点性射线自检 5/5 → 点「开发」→「直连对局」→ `[Battle] joined (JoinAck)` ＋ **`[Match] 对局场景组已加载:Rig=…BattleRig.unity Map=Assets/Scenes/TrainingGround.unity（map_id=2 叠加数 2）`**、场景清单 `Boot+BattleRig+TrainingGround`、全表现件接线、截图 `AgentScripts/Artifacts/w4-map2-trainingground.png`（训练场灰盒 + HUD 100/100·30/30） |
| Play 实测（离场卸载） | 同局：HUD 菜单钮 → 设置页 → 「退出对局」→ 确认 → `[Scene] 叠加场景已卸载:Assets/Scenes/TrainingGround.unity（余 0）`＋`BattleRig（余 0）`，场景清单只剩 `Boot`，落大厅页（**卸载清单按表枚举**的新路径实走） |
| 布点工具 | `TrainingGroundLayout.EnsureSeed()` 实跑：`MapCollision 已存在（保留既有布点）` / `掩体代理已在（8 件）` / `出生标记已在（16 件）` / `map_bounds 已在`——**场景内容零差异**（`git diff --numstat` 空） |
| 烘焙幂等 | 两图连烘两次：第二次均 `up to date`（write-if-changed 生效）；摘要与批前一致 |
| doc-lint | **OK**（0 violation / 0 warning） |

### 与设计稿的偏差（如实登记）

| 项 | 偏差 | 处置 |
| --- | --- | --- |
| 表加列与真实烘焙的先后 | 表加 `scene` 列后 `Luban/gen.bat` **要求数据源先有值**（缺字段拒载），而烘焙器属 Editor（需 Unity 编译通过——依赖生成物） | 引导步：按烘焙器**产出格式**给两行 JSON 补 `scene` 行 → gen.bat → Unity 编译 → 真烘焙写回并连跑两次 `up to date` **自证产物与引导值逐字节一致**；随后再跑 gen.bat 收口 |
| 玩家侧选图控件 | 门面已收 `CreateRoom(mapId, cb)`、房间列表行已带 `mapId/mapName`，**未加大厅页控件**（无设计稿；控件属 UI 批） | ~~登记待做（见下）~~ **已交付（[开房参数选图与人数](玩法规则与开房线.md#ch3-开房参数选图与人数)，2026-10-17）**：大厅页 `Cot_CreateSetup` 选图＋选人数下拉（含票据 v3／JoinAck 回执与客户端定容） |
| 开发页「测试模式」不可点 | Play 可点性自检暴露**存量缺陷**：`Txt_DevState`（状态读数）与「测试模式」同 y 且 `raycastTarget=true` → 盖住整钮；「关闭」钮原在面板外（y=-30） | 本批修：`state.raycastTarget=false` ＋ 面板加高/按钮重排（`AgentScripts/BuildWorkbenchPanel.cs` → 重建 `DevMenuUI.prefab`）；复验面板 5/5 命中、点击命中物即目标钮。**审计表已登记** |
| `CloudLoopbackL3` FFA 真击杀用例 | 本机运行**失败**（`真击杀未发生（Kills=0；Gate acc=0 oor≈1820）`） | **非本批引入**：`git stash` 回 HEAD 后**同点同签名复现**（环境/存量面）；多图证据改由独立用例承载（不与 FFA 主链耦合）。如实登记，待环境复查 |
| `RebuildSeed` 破坏路径 | 未在本批执行（避免场景 YAML 无谓抖动——其行为＝原 `All()` 已被 III-b 批实跑过） | 如实登记：本批实测的是 `EnsureSeed` 的非破坏性；`RebuildSeed` 的"清旧重建"待下次真有布点改动时实走 |
| 真服务器"无票据"形态 | 直连对局（dev）不带票据 → 行键取**模板/宿主默认**（原型形态的票面缺失不静默：`ResolveMap` 对"有请求但无目录"显式拒绝） | 票据路径由 L3 真链覆盖；两种来源同一优先级罗列在 `RoomConfig.MapId` 注释与设计 §7 |

### 待做（后续批次）

| 序 | 项 | 现状 | 落点/线索 |
| --- | --- | --- | --- |
| 1 | 大厅页**地图选择控件** | 门面/数据已就绪，缺控件 | `Page_Lobby.prefab` 加选择件（`Widgets/Tmp_Dropdown` 实例，参照设置页两处下拉）→ `Page_Lobby.lua` 传 `Bridge.meta.CreateRoom(mapId, cb)`；选项源 = `Cfg.tbmap` 行（`name`/`map_id`） |
| 2 | 房间页显示本房地图 | 房间行数据已含 `mapId/mapName`（大厅页已显示） | `Page_Room.lua` 加一行读数（文案键走 `#ltext.xlsx`） |
| 3 | 训练场布点内容调整（打仗手感） | 布点工具与校验就位，布局仍是种子最小灰盒 | 场景里调（`TrainingGroundLayout.EnsureSeed` 只补不删）→ 重烘 → gen.bat → gen-build-hash；**改布点须联动** `Tests/LiteSim.Core.Tests/TestMaps.TrainingGround()` 与摘要锚 |
| 4 | `CloudLoopbackL3` 真击杀用例复验 | 本机失败（HEAD 同签名） | 环境侧复查（KCP 时序/资源；`oor` 计数指向输入帧超前被整批丢弃） |
| 5 | `map_id` 进 `JoinRequest`？ | **未加**（当前载体：票据盖章 + `JoinAck` 回执） | 若要"客户端自选图"（无 Meta 直连形态），再裁决是否把请求行键放进 Join 信令（D10 复议已在本批落地为"回执字段"，请求侧仍走票据） |

---

## 第3章 局内排行榜 + 死亡/复活表现（G2-P1 客户端余部 · HUD 批C「比分」与阵亡面）

<a id="ch3-局内排行榜"></a>

> **原文件**：`Docs/施工进度/局内排行榜.md`（归并前独立成文，内容整段并入本章）

> 依据：《联机战斗演示专项设计》§3（HUD 清单含"比分"）；《战斗HUD层级设计》§0/§2/§3/§5（两批同批回写）。
> 范围（**两批**）：① **局内排行榜**（FFA "比分"＝击杀，排行榜批）；② **死亡/复活表现**（阵亡批：中央复活
> 倒计时 + 阵亡归属 + 保护期角标 + **金色** 3D 保护壳）。**不含**霰弹枪/切枪表现（用户裁决后置）、
> Team 比分（面预留待 Team 模式）、ping / 击杀提示 / 护盾 / 技能 CD。
> （**后批落地**：击杀提示 / ping·丢包 已于同日[战斗表现批C](战斗与武器线.md#ch4-战斗表现批C) 交付；护盾 / 技能 CD 仍待。）
> 状态：**已完成（附验证证据）**——2026-10-15。

### 裁决（用户，2026-10-15）

- 死亡竞赛下"比分"＝**击杀**；HUD 上方做**横列排行榜**：**名字 + 击杀**（一格＝上名字 / 下击杀），
  实时更新排位与击杀数，**第一名在最左**。
- 名字本批用**占位 `玩家{k}`**（k = 参赛玩家按 EntityId 升序的序号，局内稳定）；**真名字（昵称）归 G3 Profile**。
- **自己那一格高亮**。
- `MatchStateData.Score`（FFA 恒 0，只承载团队模式）：**预留节点 `Txt_Score` 但缺省隐藏**——Team 模式落地即启用。

### 交付物

| 项 | 落点 |
| --- | --- |
| 驱动 | `BattleRankboardDriver`（`App/Presentation/`，新）：每渲染帧收**镜像公共面**（`EntityFlags.Player` ＋ `EntitySlot.Kills`，阵亡/断线保留槽位故不看存活）→ **全序** `Kills 降序 → EntityId 升序` → **从左往右**填 8 格（左＝第一）；**击杀分布变化才重排/写**（指纹脏位）；**自己格**点亮高亮件；人数 < 8 尾部格隐藏；`Team==0` 隐藏 `Txt_Score`。**零每帧分配**（预分配缓冲 + 插入排序） |
| 引用件 | `BattleHudView` 增 `_rankboardRoot` / `_teamScoreText` / `_rankNames[8]` / `_rankKills[8]` / `_rankHighlights[8]` ＋ `HasRankboard` 装配判据 |
| 装配 | `BattleViewRig.AttachRankboard()`（HUD 之后接线；格件不齐 → **静默降级**只警告）＋ `Tick()` 驱动 ＋ `Dispose()` 格归位 |
| prefab | `Hud_Battle.prefab` 增 `Cot_Rankboard`（顶部中锚点组·零尺寸）→ `Cot_RankCell1..8`（140×56 横排，中心 x=-490+140·n；每格 `Image_Highlight`〔纯色底·**无 sprite 不引第三方件**〕/`Txt_Name` 上/`Txt_Kills` 下）＋ `Txt_Score`（预留隐藏）。构建器 `AgentScripts/BuildHudRankboard.cs`（幂等；克隆 `Txt_Reload` 继承 TMP 字体/材质；新 Graphic 全 `raycastTarget=false`） |
| 设计回写 | 《战斗HUD层级设计》§0（比分口径）/§2（层级树）/§3（字段映射）/§5（改名映射）；《联机战斗演示专项设计》§3 HUD 行标注"比分＝实时排行榜" |

### 验证证据

| 项 | 结果 |
| --- | --- |
| 重编译 | **0 错误** |
| 定向 EditMode | `BattleRankboardEditModeTests` **8/8**：击杀降序·第一名在最左·名字为身份序号／同分按 EntityId 升序·两次 Tick 不抖／**脏位**（击杀分布不变不重写）／自己格高亮**唯一**／人数 < 8 尾部格隐藏／非参赛实体不计入／`Txt_Score` FFA 隐藏·Team 显示／Dispose 格归位（幂等） |
| 结构守卫 | `BattleHudMenuEditModeTests` **5/5**（新例：排行榜 8 格预置·引用件齐备·高亮件全不拦射线）——既有"全树恰 1 个 `raycastTarget=true`＝菜单钮"不变量对新件仍成立 |
| 全量 EditMode | **551/551**（全绿；本批 +9 例：驱动 8 + 守卫 1） |
| L1 | `test.ps1 -Lane L1 -Profile PullRequest` **[PASS] 1677**（客户端改动不在 L1 面——复跑确认零回归） |
| BuildHash | **未动共享源集**（客户端 `App`/Lua/prefab 均不在闭包内）——`gen-build-hash.py --check` 双 OK |
| doc-lint | OK（0 违规 / 0 提醒） |

### 边界与挂账（如实）

- **真名字面缺失**：`JoinAck.members` 只有席位 id（[battle.proto](../../Assets/LiteNet/Proto/battle.proto)）、实体无名字字段、
  游客登录只回 `accountId`（无昵称）→ 本批占位 `玩家{k}`；**真名字归 G3 Profile**（[客户端审计](../审计/代码质量审计-客户端.md) 功能缺口节已登记）。
- **`Txt_Score` 为预置未启用面**（`Team==0` 恒隐藏——不显示恒 0 假数据）；待 Team 模式（《游戏业务系统总设计》§137"首版先做死亡竞赛"）。
- **上限 8 格**（= `eight` 模板上限）——> 8 人裁剪（登记）。
- 不在本批：霰弹枪/切枪表现（用户裁决后置）、散布/腰射片段、ping/击杀提示/护盾/技能 CD。（**散布张开 / 击杀提示 / ping·丢包 已于同日[战斗表现批C](战斗与武器线.md#ch4-战斗表现批C) 交付；腰射片段经用户裁决撤回**）
- 实机观感（格宽/字号/高亮强度/顶部落位）归本机手测。

### 阵亡批：死亡/复活表现（2026-10-15）

#### 裁决（用户）
- 阵亡提示**含击杀者**（占位名 `玩家{k}`，与排行榜共用身份序号单源）；复活 UI＝**中央倒计时文本**；
  保护期**连 3D 特效一起做**（新美术件**金色**）。
- 保护期语义（Sim 侧既有、测试钉住）：保护 = `Frame < InvulnerableUntilFrame`（**端点不含**），
  **仅"成功开火"取消**（武器门被拦不清）——`MatchRulesTests.AcceptedShotCancelsProtectionButBlockedWeaponDoesNot`。

#### 交付物
| 项 | 落点 |
| --- | --- |
| 身份序号单源 | `PlayerIdentity`（`App/Presentation/`，新）：参赛玩家（`EntityFlags.Player`）按 EntityId 升序的 1-based 序号 + `PlaceholderName` —— 排行榜与阵亡归属**共用**（消除重复）；排行榜驱动同步收编（原私有序号逻辑退役） |
| 死亡/复活 HUD | `BattleRespawnHudDriver`（新）：`SimView.IsDead` 锁存判据 → 中央面板显隐；倒计时 = `RespawnFrame − Frame` **整秒脏位**；归属 = `Death` 帧事件 × `role==Received`（`OtherId` 击杀者 → `玩家{k}`）；保护角标 = `Frame < InvulnerableUntilFrame`（开关脏位）；未对齐整体收起。构造**委托注入**（`localSlot`/`isLocalDead`）——EditMode 免构造 SimView（同面板驱动先例） |
| 保护壳（金色 3D） | **新美术件** `Assets/ShareResource/FX/fx_spawn_protection.prefab`（胶囊包裹壳 r0.75/h2.1）+ `.mat`（URP Unlit·透明·**金色** `(1,0.78,0.25,0.35)`）；构建器 `AgentScripts/BuildSpawnProtectionFx.cs`（幂等）；`BattleProtectionFxDriver`（新）：保护态内把挂 `_viewRoot` 的壳件**跟随本地角色视图根**（不挂角色视图——避免回池复用把壳留给别的实体），缺件/灰盒静默降级 |
| HUD 件 | `Hud_Battle.prefab` 增 `Cot_Respawn`（`Txt_RespawnTitle`/`Txt_RespawnCountdown`）＋ `Cot_Protection`（`Txt_Protection`）；**两个根缺省非激活**（驱动按事实开关，避免加载闪一下）；`BattleHudView` 引用件 4 项 + `HasRespawnHud` 判据；构建器 `AgentScripts/BuildHudRespawnPanel.cs`（幂等） |
| 装配 | `BattleViewRig`：`AttachRespawnHud()`（注册进 `HitFeedbackDispatcher`——只吃 Death×我阵亡）· `AttachProtectionFx()` · `Tick` · `Dispose`；取件 `AcquireProtectionShellAsync`（缺件警告降级） |

#### 验证证据
| 项 | 结果 |
| --- | --- |
| 重编译 | **0 错误** |
| 定向 EditMode | `BattleRespawnHudEditModeTests` **4/4**（阵亡显示／整秒脏位／复活收起；归属只吃 Death×我阵亡且带占位名；保护角标**端点不含**＋开火取消即关；未对齐收起＋Dispose 幂等）· `BattleProtectionFxEditModeTests` **3/3**（保护期内亮壳并逐帧跟随；端点／开火取消即关；无视图不猜位姿＋未对齐收起）· 结构守卫 `BattleHudMenuEditModeTests` **6/6**（新例：死亡/复活面预置·引用件齐备·缺省非激活） |
| 全量 EditMode | **559/559**（全绿；阵亡批 +8 例：HUD 4 + 保护壳 3 + 守卫 1） |
| PlayMode | **[PASS] 40/40**（本批跑——用户要求） |
| L1 | `test.ps1 -Lane L1 -Profile PullRequest` **[PASS] 1677**（客户端改动不在 L1 面——复跑确认零回归） |
| BuildHash | **未动共享源集**（客户端 `App`/FX/prefab 均不在闭包内）——`gen-build-hash.py --check` 双 OK |

#### 边界与挂账（如实）
- 保护壳为**静态壳**（不脉动——表现参数后续可调）；**金色**由用户裁决；尺寸/透明度观感归本机手测。
- **击杀提示**（我击杀他人）接缝已通（`HitFeedbackDispatcher` 的 `Caused` 分支）但**本批不点亮**——归后续批。**已点亮（击杀提示批，2026-10-15，见[战斗表现批C](战斗与武器线.md#ch4-战斗表现批C)）**——**全局**击杀提示（公共击杀面 `MatchStateDelta.recent_kills` + HUD `Cot_KillFeed`）。
- 阵亡/尸体的**精致美术**（倒伏/血泊）不在本批；`CorpseFrames`（180 帧窗）与死亡动画为规则闭环批既有面。
- 真名字（G3 Profile）· Team 比分面 · >8 人裁剪 · ping/护盾/技能 CD 仍挂账。（**ping·丢包 已交付（应用层 Heartbeat 探针 + `Cot_NetStats`，2026-10-15，见[战斗表现批C](战斗与武器线.md#ch4-战斗表现批C)）**）

---

## 第4章 结算页玩家排行（Cot_Records 改造）

<a id="ch4-结算页玩家排行"></a>

> **原文件**：`Docs/施工进度/结算页玩家排行.md`（归并前独立成文，内容整段并入本章）

> **来源**：用户指令（2026-10-18）「接手做 `GET /lobby/rooms/public` 缺 HTTP 级用例。做结算页玩家排行」→ 形态裁决**「把 `Cot_Records` 改造成玩家排行，原来的（最近战绩）弃用了」**；后续要求落登记 ＋ 空态文案小项。
> **状态**：**已完成（附验证证据）**——2026-10-18（工作区未提交）。

### 交付

| 面 | 落点 |
| --- | --- |
| prefab（unity-pipeline 构建器） | `Assets/ShareResource/UI/Screens/Page_Result.prefab`：面板 `Cot_Records` → **`Cot_Rankboard`**；列表 `VirtualList_Records` → **`VirtualList_Rankboard`**（`BindNode.BindName` 同改）；条目四列 **`Txt_Rank`/`Txt_Name`/`Txt_Kills`/`Txt_Deaths`**（锚点重排 0–0.12 / 0.12–0.50 / 0.50–0.72 / 0.72–1.00）＋新增 `Txt_Deaths` 子节点。构建器 `AgentScripts/BuildResultRankboard.cs`（`Report` 只读探针 ＋ `Build` 幂等；本机重现件不入库） |
| 排行口径（单源） | 新件 `Assets/LiteGame/App/Ui/ResultRanking.cs`：全序＝**击杀降序 → EntityId 升序**（与局内排行榜 `BattleRankboardDriver` 同全序）；名次＝1 ＋ 击杀**严格大于**本行者数（并列同名次）；名字＝`玩家{k}`。`App/Presentation/PlayerIdentity.cs` 增**冻结名册**口径序号重载（`OrdinalOfRows`）＋ `PlaceholderNameFor`——与局内身份序号**同一条规则**，输入面＝冻结行表 |
| 流程接线 | `Assets/LiteGame/App/Ui/PageFlow.cs` 的 `ResultData` 输出 `ranks` 行表；**本机摘要（kills/deaths/rank）与排行同取该份行表**（名次不再两处各算） |
| 页面 | `Assets/LiteGame/Lua/UI/Page_Result.lua`：`OnShow` 吃排行行表 → `SetList("VirtualList_Rankboard", …)`（行字段＝名次/玩家名为数据 ＋ 击杀/阵亡走文案键带参）；**弃用 `Bridge.meta.ListRecords`**；无本局数据（大厅入口）→ 空态 ＋ 空排行 |
| 空态文案 | 键更名 `UI.Result.RecordsEmpty` → **`UI.Result.NoMatchData`**（zh「暂无本局数据」/ en「No match data」）：表源 `Luban/Data/#ltext.xlsx` 经脚本改值 → `python scripts/codegen/gen_luban.py` → `Assets/GameData/Config/UI/tbltext.bytes` ＋ `Assets/LiteGame/Lua/Cfg/tbltext.lua` 同批；BuildHash `--check` 双 OK（表现面不入闭包） |
| 设计回写 | 《上云测试页面层级设计》§5 结算页树 ＋ 排行口径条 |

### 验证证据

| 项 | 结果 |
| --- | --- |
| Unity 重编译 | **0 错** |
| 定向 EditMode | `MetaPagesEditModeTests` **25/25**——含改造后两例（`结算页_本机名次与玩家排行行投影`、`结算页_大厅入口_无本局数据_空态与空排行`）与 `文案键守卫_页面脚本引用的键全在表_含结束原因值域` |
| 资产命名门禁 | `UiValidationEditModeTests` **14/14**（真实资产面命名/表路径零 Error——新节点名与容器改名过闸） |
| prefab 结构复核 | 构建器 `Report`：`Cot_Rankboard → VirtualList_Rankboard`（BindNode 同文）→ `Clone_Item` 四列（锚点/对齐逐项打印核对） |
| 表源→生成物核证 | 生成物内新键 `UI.Result.NoMatchData`（zh/en 值齐），旧键零残留 |
| L1 门禁 | `pwsh scripts/gate/test.ps1 -Lane L1 -Profile PullRequest` → **[PASS] 1741** |

### 如实说明（边界 / 挂账）

| 项 | 说明 |
| --- | --- |
| **Play 端到端未跑** | 排行行真实观感（行高 48、四列比例、长名截断）未实机核。 |
| 本地玩家**行高亮未做** | `SetList` 行字段只写**文本**（`UIBindIndex.WriteListItem` 仅 `RevealOrSetText`），不支持逐行开关 `Image`——要做得另走机制（如行内文本标记）。 |
| **战绩功能链（本批切断）** | 大厅「最近战绩」钮（`Button_Records`，标签 `UI.Lobby.Records`）→ 结算页列表 → `Bridge.meta.ListRecords` → `GET /matches`：本批把该列表改造为玩家排行并删掉 `ListRecords` 调用 ⇒ 从大厅点「最近战绩」只落「暂无本局数据」空态，整链零生产调用方（链本体与用例仍在）。口径待裁决：只弃用列表控件（战绩另寻落点）／整条弃用（退役钮与整链）／保持现状——已登记进 [客户端审计](../审计/代码质量审计-客户端.md)。 |
| 行模板占位文本 | 模板内 `Txt_Rank` 的 text 仍是旧字面量（`Clone_Item` 非激活，运行时由 Lua 覆写，无观感影响）。 |

---

## 第5章 战绩独立页（Page_Records）

<a id="ch5-战绩独立页"></a>

> **原文件**：`Docs/施工进度/战绩独立页.md`（归并前独立成文，内容整段并入本章）

> **来源**：用户指令（2026-10-18）「`Page_Result` 只管本局结算 + 玩家排行；战绩挪到新页（大厅钮改指新页）。链保持活，但要新增一页 prefab（`Tmp_VirtualList` 变体）+ `tbuiform` 登记 + Lua 骨架 + 文案键，并走一遍绑定/结构守门。」
> **状态**：**已完成（附验证证据）**——2026-10-18（工作区未提交）。

### 交付

| 面 | 落点 |
| --- | --- |
| prefab（unity-pipeline 构建器） | 新建 `Assets/ShareResource/UI/Screens/Page_Records.prefab`：`Canvas`+`CanvasScaler`+`CanvasGroup`+**`GraphicRaycaster`**（页面根必挂——缺则 EventSystem 射线空命中、页内按钮不可点）＋`Cot_SafeArea`；`Cot_Header`（`Txt_Title`＋`Button_Back`/`Txt_Back`）；`Cot_Records`/`Bg_RecordsPanel`＋**`VirtualList_Records`（`Tmp_VirtualList` 变体，BindNode 同名）**＋条目三列 `Txt_MatchTime`/`Txt_Kill`/`Txt_EndReason`；`Txt_Empty` 空态。构建器 `AgentScripts/BuildRecordsPage.cs`（`Report` 只读探针 ＋ `Build` 幂等；本机重现件不入库）；同批列入 `AgentScripts/FixCanvasRaycasters.cs` 目标清单（幂等体检"已有"）|
| tbuiform 登记 | `Luban/Data/#uiform.xlsx` 追加 **id=16 / `UI.Page_Records` / `ShareResource/UI/Screens/Page_Records.prefab` / layer 0 / full_screen true** → `python scripts/codegen/gen_luban.py` → `Assets/LiteGame/Lua/Cfg/tbuiform.lua` ＋ `Assets/GameData/Config/UI/tbuiform.bytes` ＋ `Assets/LiteGame/LuaBridge/Generated/LuaKeys.g.cs`（**8 路径常量**，新增 `LuaKeys.UI.Page_Records`）|
| 页面 | `Assets/LiteGame/Lua/UI/Page_Records.lua`（经 `unity command import_asset` 入库，含 `.meta`）：`OnShow` → `Bridge.meta.ListRecords` 行投影（行字段按**条目子节点名**；key = matchId；时间为数据 ＋ 击杀文案键带参 ＋ 结束原因拼键）；空表/未配置 → 空态（`UI.Records.Empty`）；`Button_Back` → 替换回大厅 |
| 文案键 | 新增 `UI.Records.Empty`（zh「暂无战绩」/ en「No recent matches」）：表源 `Luban/Data/#ltext.xlsx` → `gen_luban.py` → `tbltext.bytes` ＋ `tbltext.lua` 同批；标题复用既有 `UI.Lobby.Records`、返回复用 `Common.BackLobby`（零新增冗余键）|
| 大厅接线 | `Page_Lobby.lua` 的 `Button_Records` 改指 `UI.Page_Records`（原指 `Page_Result`）；文件头注释同步 |
| 设计回写 | 《上云测试页面层级设计》§1 关系图／§4／§5／§8／§9 |
| 审计回写 | [客户端审计](../审计/代码质量审计-客户端.md)「战绩功能链（本批切断）」条 → 改判**已修复（另立独立页）** ＋ 修复登记新行 |

### 验证证据

| 项 | 结果 |
| --- | --- |
| Unity 重编译 | **0 错**（`recompile_status.failed=false` / `compilationFailed=false`）|
| 定向 EditMode | `MetaPagesEditModeTests` **27/27**——新增两例（`战绩页_列表行投影_返回大厅`、`战绩页_空表与未配置_落空态`）＋ 大厅入口断言改指新页 ＋ 文案键守卫自动覆盖新页引用 |
| 文案键计数守卫 | `LTextWiringEditModeTests` **5/5**（`ExpectedKeyCount` 55 → **56**——新增 `UI.Records.Empty` 同批核对）|
| 资产命名/结构门禁 | `UiValidationEditModeTests` **14/14**（真实资产面命名/表路径零 Error——新页 prefab 入扫通过）|
| L1 门禁 | `pwsh scripts/gate/test.ps1 -Lane L1 -Profile PullRequest` → **[PASS] 1741** |
| BuildHash | `python scripts/codegen/gen-build-hash.py --check` → **双 OK**（LuaKeys/表数据/新 prefab 均不在共享闭包）|

### 如实说明（边界 / 挂账）

| 项 | 说明 |
| --- | --- |
| **Play 端到端未跑** | 新页实机观感（列表行高、长名截断、空态）与"大厅钮 → 战绩页 → 返回"真导航链未核。|
| 行点击未做 | 战绩列表现无行点击（与原战绩列表一致，只读展示）。|
| 时间列为数据 | `Txt_MatchTime` 取 `Bridge.meta.ListRecords` 行的 `time`（服务端格式化字符串，`SetText` 数据面，不随语言刷新）。|

---

## 第6章 HUD 子件与容器排布批（排行格 / 击杀播报）

<a id="ch6-HUD子件与容器排布"></a>

> **原文件**：`Docs/施工进度/HUD子件与容器排布.md`（归并前独立成文，内容整段并入本章）

用户指令（2026-10-16）："抽 2 个子件预制体，上 LayoutGroup；击杀播报统一改成我设置的第一个单元的参数；击杀播报上限暂时设定为 5。"

### 状态

**已完成（附验证证据）**——2026-10-16（已提交 `e2c97baa`）。

### 交付

| 面 | 落点 |
| --- | --- |
| 子件模板 ×2（视觉单源） | `Widgets/Tmp_RankCell.prefab`（排行格：`Txt_Name`/`Txt_Kills`/`Image_Highlight`，140×56）／`Widgets/Tmp_KillFeedRow.prefab`（播报行：`Txt_KillFeedKiller`/`Image_KillFeedWeapon`/`Txt_KillFeedVictim`）——**以用户手调的第一个单元为参数源**（图标 **100×100**、击杀者 x=**-118.3**、死者 x=**+118.3**）；改模板一处，全部实例统一 |
| 实例化 + 容器排布 | `Hud_Battle.prefab`：`Cot_RankCell1..8` → `Tmp_RankCell` 实例 ×8 ＋容器 **`HorizontalLayoutGroup`**（spacing 0 / UpperCenter / childControl* false；容器 1120×56）；`Cot_KillFeedRow1..5` → `Tmp_KillFeedRow` 实例 ×5 ＋容器 **`VerticalLayoutGroup`**（spacing 4 / UpperRight；容器 340×176）；`Txt_Score`（团队比分预留）加 `LayoutElement.ignoreLayout=true` 留在容器外 |
| 播报上限 4 → 5 | `BattleKillFeedDriver.RowCount = 5`（注释挂结构守卫口径）；`BattleHudView` 四组引用数组重接 5 项（含新 Row5） |
| 引用面不破 | 实例根名保持 `Cot_RankCell{i}`/`Cot_KillFeedRow{i}`——`BattleHudView` 序列化引用与 `FindDeep` 按名寻址的结构守卫同名即通 |
| 旧构建器防误跑 | `AgentScripts/BuildHudKillFeed.cs`/`BuildHudRankboard.cs` 加**结构收敛守卫**（检测到容器即 `SKIP`）——防止按旧形态重建内联节点并静默回退行数/结构 |

**位置对账（与改造前逐值一致）**：格枢轴 x ＝ `容器rect.xMin(-560) + anchoredPosition.x` ＝ **-490 + 140n**（Play 实测世界 x = 470…1450，对称于 1920 屏心 960）；行 y ＝ **0 / -36 / -72 / -108 / -144**。
**容器接管口径（重要）**：排布容器会**规范化子件锚点**（格被驱动为 anchorMin/Max=(0,1)、位置由容器算）——改造后**不得再手工摆位**；行/格内要加位移动效须动**内层承载件**（外层锚点归容器）。此口径已写入《UI 制作规范》§3。

### 验证证据

| 项 | 结果 |
| --- | --- |
| Unity 重编译 | 0 错误（`RowCount` 改动后多轮） |
| L1 门禁 | **[PASS] 1715**（`pwsh scripts/gate/test.ps1 -Lane L1 -Profile PullRequest`）。中途一次红：`RowCount` 注释带批次/日期 → `纪律_代码注释不含施工痕迹` 拦下 → 改为只陈述约束，复跑全绿 |
| EditMode（4 组 **26 例全绿**） | `BattleHudMenuEditModeTests` 8/8（结构守卫：8 格在位＋按 `RowCount` 逐行点名 5 行、缺省非激活、零射线越界；含顺带修的陈旧断言）· `BattleHudEditModeTests` 4/4 · `BattleKillFeedEditModeTests` 6/6（堆叠/留队/驻留语义，数组按 `RowCount` 自动 5 项）· `BattleRankboardEditModeTests` 8/8 |
| Play 实测（真机链路） | Boot 直入训练场（用户当前开关）→ HUD 在场 `Cot_Panel.alpha=1`；`Cot_Rankboard` 容器 + 8 格（世界 x 470/610/…/1450、可见）· `Cot_KillFeed` 容器 + **5 行**（y 0/-36/-72/-108/-144、缺省非激活）· 行内图标 **(100×100)**；截图 `AgentScripts/Artifacts/hud-5rows.png`（8 格布局在场：2 名参赛者、尾部空格隐藏） |
| 术语/计数同步 | 《UI 制作规范》§2/§9 模板计数 27→**29**、《UI 命名词表与控件清单》§1.3 增两行、《战斗HUD层级设计》§2 树/§4 暴露表/命名演进、《修改指南》§3.3 HUD 行；`doc-lint` OK |

### 续：单元级显隐口径（用户反馈，同日）

用户指出：**隐藏单元应关"单元根"，单元内子件保持激活**——否则每次调子件都得先逐个点亮。原实现（格内 `Txt_Name`/`Txt_Kills`/`Image_Highlight` 各自缺省关）确实如此，已按此口径改并**做成守卫**：

| 面 | 改动 |
| --- | --- |
| 模板 | `Tmp_RankCell`（名字/击杀/高亮三子件）与 `Tmp_KillFeedRow`（击杀者/死者）子件**全部保持激活**（实例随之继承） |
| 单元根 | 8 格根 / 5 行根**缺省非激活**（驱动按人数/队列开关）——隐藏只关根 |
| 驱动 | `BattleRankboardDriver` 显隐改走**格根**（新增 `BattleHudView.RankCells`，`HasRankboard` 纳入）；`HideCell` 只关根＋清高亮（高亮仍按位开关）；播报行原本即关行根（击杀者/图标按行状态开关保留） |
| 容器对齐 | 排行容器 `UpperCenter` → **`UpperLeft`**：容器对隐藏单元做紧凑排布，居中会让"可见的 2 格"跑到中间；`UpperLeft` 保证**格位固定**（占用格仍从最左槽位起排，8 格全显时与原来逐值一致） |
| 冗余关闭位清理 | 单元根已被驱动整体开关的组，其子件恢复激活：`Cot_Respawn{Txt_RespawnTitle,Txt_RespawnCountdown}`、`Cot_Protection/Txt_Protection`、`Cot_NetStats/Txt_NetStats`；保持缺省的例外＝单元即单节点且驱动按状态开关的 `Txt_Score`／`Txt_Reload` |
| 守卫 | `BattleHudMenuEditModeTests`：格根缺省非激活＋`view.RankCells` 齐备＋格内/行内子件**保持激活**逐件断言；`BattleRankboardEditModeTests`：隐显断言改**单元级**（格根），并显式断言"子件不被驱动关掉" |
| 口径入规 | 《UI 制作规范》§3 加"单元级显隐"条（含容器紧凑排布与 `UpperLeft` 的格位固定要求） |

**验证**：Unity 重编译 0 错 · EditMode 四组 **26 例全绿**（含新断言）· Play 实测：2 名参赛者 → **格位 1/2 在显（世界 x=470/610，格位固定）**、格 3..8 隐藏（关根）、格内子件激活、5 行根缺省关且行内子件激活、图标 100×100；截图 `AgentScripts/Artifacts/hud-unithide.png`。

**并发编辑事故（如实，含根因）**：本批期间 `Hud_Battle.prefab` **同时开在用户的 Prefab Stage 里**，而我对该资产做了多轮 `LoadPrefabContents → 改 → SaveAsPrefabAsset`——两个写入者轮流覆盖：用户的 Stage 保存抹掉过本批结构（第 5 行一度丢失、模板参数被回带旧值），我的保存也把用户的手调（字色 `#EBF5FF`／`Txt_Score` 文案／`Cot_Settings` 位置·遮罩·通道／`Cot_Panel` 的 Image）写回过旧值。**处置**：改为**只写 Stage 内容**（`PrefabStageUtility` + `SaveAsPrefabAsset(stage.prefabContentsRoot, …)`）并逐项还原用户手调（`RestoreHudUserEdits{,2}.cs`，写盘前快照 `AgentScripts/Artifacts/hud_pre_restructure_*.prefab`），Stage 与资产现一致；两处刻意偏差已报用户（`Cot_Panel.Image.raycastTarget=false` 守 HUD 射线红线；播报容器保持开）。**后续纪律**：动用户正在编辑的 prefab 一律走 Stage 同一份内容、写前快照、写后回读复核并报差异；不做"全量扫属性"式批量改。

### 如实说明（待裁决 / 边界）

| 项 | 说明 |
| --- | --- |
| 行高与图标尺寸不匹配 | 你的参数（图标 **100×100**）落在**行高 32、行距 4** 的行上——图标会压过相邻行。已按指令统一，未擅自改几何；要排开只需两个旋钮：**模板行高**（`Tmp_KillFeedRow`）＋**容器 spacing**（如行高 100 / spacing 8）。 |
| 播报行的**游戏内观感**未实见 | 你 Boot 的 `NoDeath=1`（未提交手调）使假人不死 → 造不出真实击杀；行内容链路由 `BattleKillFeedEditModeTests` 6/6 覆盖，Play 只证到"5 行在位、缺省非激活、图标=100×100、容器位置对"。 |
| 早前两条判断**更正** | 我曾报"Cot_Panel 全屏 Image""Cot_KillFeed 被默认关"——均为我的 YAML 归属解析**误报**（实测组件清单：`Cot_Panel` 无 Image、容器为开；恢复脚本为空操作）。实际发生的是**射线越界 1 处**（第 2 个 `raycastTarget`）→ 已归零，现与 HEAD 一致（唯一菜单钮）。 |
| 并发编辑提醒 | 本批期间该 prefab 被**并发保存**过一次（曾致第 5 行丢失、模板参数被回带旧值）；我已重建并逐值复验。若编辑器里仍开着该 prefab，请以磁盘为准（关闭重开）再编辑。 |
| 保留的未提交手调（未处置） | 字体色统一 `#EBF5FF`（多处 TMP `fontColor32`）· `Txt_Score` 文案 `0 - 0` · `Cot_Settings` 挪到 `(-84.8, -22.1)` · `Boot.unity` 的 `UseLocalServer/DirectToTraining/DevMenu = 1`——一律保留，仅报告。 |
| 顺带修（审计已登记） | `BattleHudMenuEditModeTests.设置页_退场钮…` 断言 `BindName=="Exit"` 属**陈旧断言**（HEAD 即有红：流程批 IV 已按"节点名同文"把 prefab 改为 `Button_Exit`/`Txt_Exit`）→ 已按 IV 口径校正。 |

---

## 第7章 HUD 弹药读出接线（当前弹匣 / 剩余备弹）

<a id="ch7-HUD弹药备弹读出接线"></a>

> **原文件**：`Docs/施工进度/HUD弹药备弹读出接线.md`（归并前独立成文，内容整段并入本章）

用户指令（2026-10-16）："把 `Txt_MaxAmmo` 接上（按 `MagazineSize` 写，未装备时随 `--` 一起收）"；同日复核点单**更正语义**——"这个语义不是剩余备弹吗" → 用户裁决：读出右半边＝**剩余备弹**（`WeaponRuntime.ReserveAmmo`），节点/字段名同步改 `Txt_ReserveAmmo`／`_reserveAmmoText`（名实一致）。

### 状态

**已完成（附验证证据）**——2026-10-16/17（已提交 `e2c97baa`）。

### 交付

| 面 | 落点 |
| --- | --- |
| 引用件 | `BattleHudView` 增 `_reserveAmmoText`（→`Txt_ReserveAmmo`，由 `Txt_MaxAmmo` **改名**）／`_slashSeparator`（→`Txt_Slash`）＋访问器；`Hud_Battle.prefab` 经 pipeline `run_script` 接线＋改名（回读复核见下） |
| 驱动 | `BattleHudDriver`：装备态写**剩余备弹**（本人私有面 `WeaponRuntime.ReserveAmmo`——换弹从备弹扣、随消耗递减；**值变化才写**）；**未装备** → `Txt_Ammo` 占位 `--`（常量 `UnarmedAmmoPlaceholder`）且**备弹件＋分隔符整组收起** |
| 装配 | `BattleViewRig.AttachHud` 传两引用；缺件＝驱动内静默降级（只显当前弹匣——兼容旧 prefab） |
| 守卫/用例 | `BattleHudMenuEditModeTests` 结构守卫（`Txt_ReserveAmmo` 在位 ＋ `view.ReserveAmmoText` 同一性 ＋ 缺省在显 ＋ 零新射线）；`BattleHudEditModeTests`：装备态「17 / 90」、未装备随占位整组收起、备弹递减写值（SENTINEL 脏位用例） |
| 文档 | 《战斗HUD层级设计》§0 头部／§2 树（`Txt_ReserveAmmo`、`Cot_Ammo`、`Bg_Weapon`；`Txt_Reload` 字号按实测 20→30 校正）／§3 暴露表／§5 改名表；《修改指南》§3.3 HUD 行；审计表登记＋修复登记回写 |

### 验证证据

| 项 | 结果 |
| --- | --- |
| Unity 重编译 | 0 错误（`errors: []`；`eval` 实证新字段在编译产物内在位） |
| EditMode（**六组 34 例全绿**） | `BattleHudMenuEditModeTests` **9/9**（含新结构守卫）· `BattleHudEditModeTests` **5/5**（含新用例）· `BattleRankboardEditModeTests` 8/8 · `BattleKillFeedEditModeTests` 6/6 · `BattleRespawnHudEditModeTests` 4/4 · `BattleNetworkHudEditModeTests` 2/2 |
| prefab 写后回读 | 19 项标量引用非空、9 组数组 `null=0`（8/8/8/8 · 5/5/5/5 · 2）；`_reserveAmmoText → Cot_Panel/Cot_Weapon/Cot_Ammo/Txt_ReserveAmmo`、`_slashSeparator → …/Txt_Slash`；旧字段 `_maxAmmoText` 与旧名 `Txt_MaxAmmo` **零残留**；结构不变量：节点数 102 · `raycastTarget`=1 · `GraphicRaycaster`=1 |
| diff 对账 | 首次接线（无 Stage）与写前快照 diff **仅 2 行**；本轮改名为 Stage 写入（盘面快照按纪律跳过）——对账基准＝首轮快照，差异逐项＝①两字段接线 ②`Txt_Reload` 激活＋x −41.3→20（**用户 Stage 未存编辑，随保存落盘**）③节点改名 |
| L1 门禁 | `pwsh scripts/gate/test.ps1 -Lane L1 -Profile PullRequest` → **[PASS] 1715** |
| doc-lint | `python scripts/gate/doc-lint.py` → OK |

### 语义更正留痕（如实）

初版按用户首条指令以**弹匣容量** `MagazineSize` 写（恒 30，换弹不变）；用户复核指出语义应为**剩余备弹**。更正依据（动手前核实）：《游戏业务系统总设计》§5 武器表列头即「**弹匣/备弹**」（步枪 **30 / 90**、霰弹 **8 / 24**）；sim `WeaponSystem.CompleteReload` 从 `ReserveAmmo` 扣弹、`EquipDefault` 按表 `ReserveAmmo` 起备弹——故收紧为 `ReserveAmmo`（本人私有面，与 `MagAmmo` 同源直读），节点/字段同步改名，避免"`MaxAmmo` 名 + 备弹语义"名实不符。

### 如实说明（边界）

| 项 | 说明 |
| --- | --- |
| 真机观感未实测 | 写入与验证时编辑器处于**用户的 Play 会话**（不打断）；装备态「30 / 90」显示链路由 EditMode 钉住，未见真机画面 |
| 并发编辑（本轮实况） | 写入时**用户的 Prefab Stage 开着**该 prefab：按既有纪律写 Stage 内容（防 Stage 回写覆盖），Stage 中未存编辑（`Txt_Reload` 激活＋x=20）随保存一并落盘——已逐项对账报告，未做还原 |
| 备弹语义形态 | 剩余备弹**随消耗递减**（当前无补给源，只降不升）；「备弹上限」`MaxReserveAmmo` 不上 HUD（用户裁决只接剩余） |
| 切枪 | 备弹随 `SelectedWeapon` 换槽直读已就位；武器图标随武器换图仍归切枪批（本次未动） |
| 工具面 | 编辑器 Play 会话期间本机 `unity` CLI 实例发现失效（`editor_status` 30s 超时），改 HTTP 直连（`AgentScripts/Artifacts/pipeline.py`，token 每轮现读）；CLI 失效根因未深究，登记备查 |

### 续：回带修复与真机复验（用户报"剩余备弹没生效"）

**根因（实锤，非猜测）**：用户报"没生效"后现场诊断——Play 中 HUD 实例的 `_reserveAmmoText = NULL`、节点名仍是 `Txt_MaxAmmo`（旧形态）⇒ 跑的是旧内容。核对磁盘：prefab 在 **00:40 被回带覆盖**（节点名退回 `Txt_MaxAmmo`、`_reserveAmmoText: {fileID: 0}`），即**用户 Prefab Stage 用其旧内存副本保存**，把本批 00:29 的改名＋接线覆盖掉（`Txt_Slash` 因在更早一次写入中已落 Stage 而幸免）——**与 [HUD子件与容器排布](UI战斗HUD线.md#ch6-HUD子件与容器排布) 记的并发编辑事故同型**。

| 处置 | 落点 |
| --- | --- |
| 重新落定 | `WireHudReserveAmmo.Run()` 再跑（无 Stage 打开时走盘面路径）：改名 `Txt_MaxAmmo`→`Txt_ReserveAmmo` ＋ `_reserveAmmoText` 重接；**写前快照**（`Artifacts/hud_pre_reserve_wire.prefab`）＋ 写后 `Verify()`（19 标量非空 · 9 数组 null=0 · 旧名/旧字段零残留 · 节点 102 · raycast 不变量未破）；与快照 diff **仅 2 行** |
| 真机复验（真实输入注入） | `AgentScripts/BattleLiveProbe.cs`：Input System 设备态**逐帧重压**注入（单次注入会被 OS 状态下一帧覆盖；按下沿还需按下/松开交替才会被游戏侧采样到）。实测：`30 / 90` →连发打空→ `0 / 90` →自动换弹（sim"打空自动换弹"）→ **`30 / 60`**（备弹递减 ✓）；中段截图 `Artifacts/hud_live_fire.png`（枪口火光在场、右下角读出「23 / 0」） |
| 输入注入口径留痕 | 编辑器内注入：电平型（`IsPressed`，开火）单次注入即可；**沿型（`WasPressedThisFrame`，换弹）单次注入必被采样错帧错过**——用按下/松开交替泵；管道自带 `simulate_key`/`simulate_pointer` 在编辑器同样受"沿只存活一帧"限制（dev Player 的 Runtime 侧不受此限） |
| 登记 | 审计表新登记（并发回带第 2 次 + 工具面 CLI 失效），见《代码质量审计-客户端》"新登记"节 |

**教训（建议进纪律）**：外部工具写 prefab 后，若用户 Stage 仍开着旧副本，**下一次 Stage 保存即回带**；本轮处置＝写后回读＋对账报告，**根治候选**（写前探测 Stage 并强制以 Stage 内容为基准写入／写后把盘面状态推回 Stage）留待裁决。
