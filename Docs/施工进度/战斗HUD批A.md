# 战斗HUD批A（prefab 化 + 引用件 + 收集 + 场景退役）

《战斗HUD专项设计》/《战斗HUD层级设计》批 A 执行（2026-10-11）。批 B 装配拆分已由并行 BattleViewRig 批先行完成——本批与其合流，**HUD 全链按新规范落位**。

## 状态

**已完成（pipeline + Unity 证据齐全）**。

## 交付

| 面 | 落点 |
|---|---|
| 模板 prefab | `Assets/ShareResource/UI/Hud/Hud_Battle.prefab`——训练场现役树 1:1 转制：§9 词缀化改名 23 处（`Hud_Battle/Cot_Crosshair/{Icon_Hip,Icon_Ads}`、`Cot_Panel/{Cot_Vitals,Cot_Weapon,Cot_Skills}`…）＋ **Icon_Ads 38→64（用户裁决）** ＋ 根挂 `BattleHudView`（SerializedObject 接线 8 字段） |
| 引用件（新） | `App/Presentation/BattleHudView.cs`——8 个序列化字段（CrosshairRoot/Hip/Ads/PanelGroup/HpFill/HpText/AmmoText/ReloadLabel）+ `PrefabLocation` 单源；零 Find 契约 |
| 收集 | collector 新组 **UIHud**（`Assets/ShareResource/UI/Hud`，PackSeparately，ui tag）——HUD 件级热更 |
| 场景退役 | `TrainingGround.unity` 删 `/Battle HUD` 对象（场景不承载 HUD 视觉）；HUDDesign 同日改造为 **UiWorkbench**（UI 工作台场景，见《UI工作台设计》——HUD 预览随工作台 W2 可选） |
| 接线（Rig） | `BattleViewRig`：`AcquireHudAsync`（租约→_viewScope→实例化→取引用件）+ `AttachCrosshair/AttachHud` 改 View 引用 + Dispose 回收 HUD 根件；**六条路径常量与 FindChildGameObject 退役——Find 归零** |
| 退役 | `AgentScripts/BuildBattleHud.cs`（构建器双写根源消除；本地未入库件留待删） |

## 迁移过程（如实，三段收敛）

1. v1 顺序改名 bug：父节点先改名致子路径失配，ABORT 未拦住保存——产出半改名 prefab 且场景对象已退役（场景退役本为目标 ✓，树完整在 prefab 内）；
2. v2 fail-fast 生效拦截（接线不全不保存），但改名表仍是旧路径；
3. v3 改「**先收集后统一改名**」模式对齐盘上状态——×23 全中、结构校验 10/10 OK、ADS=64、保存成功。

## 验证证据

| 项 | 结果 |
|---|---|
| 结构校验 | 10/10 OK（层级树全节点按新名在位）＋ `ADS size=64` ＋ `root=Hud_Battle` |
| collector | UIHud 组（Assets/ShareResource/UI/Hud，PackSeparately，ui） |
| Unity recompile | `completed / failed:false / 0 错误`（含 BattleHudView/BattleViewRig 接线） |
| `BattleCrosshairEditModeTests` | **4/4** |
| `VfxServiceEditModeTests`（回归面） | **17/17** |
| Find 归零 | `BattleViewRig` grep `GameObject.Find/FindChildGameObject` = 0 |

## 遗留（如实）

- 实机观感手测（F9：血条/弹药/换弹标签/准心双形态）待本机——逻辑面测试已绿；
- `BuildHudPrefab/2.cs` 迁移脚本留 AgentScripts（未入库）供复跑；
- 技能栏数据面（批 C）按 UI 框架 U2 节奏另行点单。

## 血条正式化（随批收口，2026-10-11）

批 A 产出的 HUD 里，血条遗留两态并存：旧构建器框条（`Image_HpFrame`/`Image_HpFill`/旧 `Txt_Hp`，Filled 图形血条）与用户手工粘贴的 Synty 共享件 `HUD_MilitaryCombat_StatsList_Item_05`（草名 `Item_00`，内含 UGUI `Slider`）。驱动已接 `Slider`，但旧框条仍挂在 prefab 上、`Item_00` 未合规范。本批收口：

| 面 | 落点 |
|---|---|
| 血条正式化 | `Cot_Panel/Cot_Vitals/Item_00` → **`HpBar_Player`**（§9 词表段 `HpBar_`——命名词表 §1.3 已登记血条类型段，业务层实例＝`<类型段>_<语义>`，对齐现役 `HpBar` 控件模板） |
| 内部子件词缀化 | `Bar`/`IconBox`/`ICON`/`Label_Stat` → `Cot_Bar`/`Cot_IconBox`/`Icon_Health`/`Txt_Hp`；UGUI `Slider` 及其固定子结构（Background/Fill Area/Fill/Handle Slide Area/Handle）**保留原名**（组件契约，改名即断 UGUI 引用） |
| 旧血条退役 | `Image_HpFrame`/`Image_HpFill`/旧 `Txt_Hp` 删除；`Cot_Panel` 全屏空 Image（`m_Sprite={fileID:0}` 纯射线面）同时清除——HUD 整组显隐走 CanvasGroup，不需射线面 |
| Slider 基线 | `interactable=false`（纯显示）、`value=1`（满值制作基线）；子树射线兜底关闭（Vitals 剩余 raycastTarget=0） |
| 接线 | `BattleHudView._hpSlider`→`Slider`、`_hpText`→新 `Txt_Hp`（SerializedObject 重连；8 字段全部有效） |

### 验证证据

| 项 | 结果 |
|---|---|
| 结构复验 | `HpBar_Player`/`Cot_Bar/Slider`/`Cot_IconBox/Icon_Health`/`Txt_Hp` 全 `OK`；旧件 `frame/fill/item00残留` 全 `false` |
| 组件完整性 | 全树无 `[MISSING!]` 组件/丢引用；`BattleHudView` 8 字段全部解析（`_hpSlider=Slider`、`_hpText=Txt_Hp`） |
| L1 | **1594/1594 通过** |
| `BattleHudEditModeTests` | 3/4（`弹药_装备槽弹匣数与换弹态_未装备显示占位` 失败——**基线复算确认与本次改动无关**：stash prefab 后同测试同失败，属驱动默认武器态既有问题，另立） |
| 执行脚本 | `AgentScripts/AdaptHudHpBar3.cs`（未入库，幂等） |

### 遗留（如实）

- 上述 EditMode 1 例失败为**既有**（`BattleHudDriver` 在未装备判定前先走武器分支写出 `"0"` 而非 `"--"`），非本批引入——按本文档"如实"原则登记，待单独批次处置；
- 观感调参（血条尺寸/配色定稿）待实机手测后按需微调 prefab。

## 血条解包收口（2026-10-11）

制作侧将 `HpBar_Player` 子树由 Synty 共享件 `HUD_MilitaryCombat_StatsList_Item_05` 的嵌套实例**完全解包**为普通对象（制作侧手工操作）——prefab 自持全部视觉与几何，去嵌套依赖。

| 面 | 结果 |
|---|---|
| 形态 | 全树 `PrefabInstance` 归零（无 `m_CorrespondingSourceObject` 非零引用、无 `stripped` 块、无嵌套实例）；`HpBar_Player`/`Cot_Bar`/`Slider`/`Cot_IconBox`/`Icon_Health`/`Txt_Hp` 均为普通节点 |
| 几何 | 位置/尺寸为制作侧手调实况（`HpBar_Player` 540×70 @ (342,35)）——**prefab 实况即权威**，不以文档旧值回改 |
| 接线 | `BattleHudView` 8 字段仍全部有效（`_hpSlider`→`Slider`、`_hpText`→`Txt_Hp`，经重载复验） |
| 素材依赖 | 展平后为直接 sprite 引用（Synty `Sprites/**` 6 件 + 准心 2 件 + TMP/UGUI 内建），无断链 |

### 遗留（如实）

- `Cot_Panel`（全屏组）上有一 `sprite=null`、`raycastTarget=true` 的空 Image（制作侧作**面板边界对照**用）——制作侧声明**自行删除**，本批不动；删除前该件会拦全屏输入（违《层级设计》§0 事件隔离），**发布前必须清除**（回归登记，见下条）；
- 制作侧称此后仍会手工调整血条，prefab 以制作侧实况为准。

## 血条模板化（2026-10-11）

用户裁决：血条**不再是 HUD 私有结构**，改为**控件模板 `Tmp_HpBar` 的使用变体**——模板＝血条单一形态来源。本批将上一段的解包产物**回落为模板 PrefabInstance**，并把模板本身重建为 HUD 现役形态。

| 面 | 落点 |
|---|---|
| **模板重建** | `Assets/ShareResource/UI/Widgets/HpBar.prefab`（`Tmp_HpBar`）重建为 HUD 形态：`HpBar` 根 + `_Bar/Slider`（`_Background`/`_FillArea/_Fill`/`_HandleArea/_Handle`，§9.4 `_` 私有）+ **`_FillArea/_BackFill`（半透明后条·延迟滑落）** + `Cot_IconBox/Icon_Health` + `Txt_Hp`；根挂 `LiteGame.UI.HpBar` + `BindNode`。几何/Sprite/颜色一比一取自 HUD 现役血条 |
| **驱动改写** | `LiteGame.UI.HpBar` 改由 **UGUI `Slider`** 驱动前条（取代原 `FrontFill` `fillAmount`）；后条 `BackFill` 按 `BackLerpSpeed` 逐帧滑落追赶（**计时改走 `UiAnimationClock.Delta`**——§5／红线 9）；上升瞬时跟上。**公开 API `Set(current,max)` 与 `HpBar` 类型不变**——`UIDemoPage`/`UIBindIndex.SetHp` 零改动 |
| **HUD 改实例** | `Hud_Battle` 的 `Cot_Panel/Cot_Vitals/HpBar_Player` 替换为 `Tmp_HpBar` 的 **PrefabInstance**（只 override 位置/尺寸 540×70 @ (342,35)＋根名） |
| **HUD 接线** | `BattleHudView` 的 `_hpSlider`+`_hpText` 两字段 → 单个 **`HpBar _hpBar`**（指实例根组件）；`BattleHudDriver` 构造收 `HpBar`、`Tick` 调 `hpBar.Set(hp,max)`；`BattleViewRig` 随改。**单点组件引用——避免嵌套深层字段 override 的脆弱性** |

### 验证证据

| 项 | 结果 |
|---|---|
| 模板/实例树一致 | 两者逐节点比对**全同**（含 `_Fill`/`_BackFill`/`_Handle`/`_Background` rect）；全树无 `[MISSING!]` 组件 |
| `BattleHudView` 字段 | 7 字段全部有效（`_hpBar=HpBar_Player(HpBar)`） |
| 射线隔离 | Vitals 子树 raycastTarget=0（全关） |
| L1 | **1594/1594** |
| `VisualSingleSourceEditModeTests` | **8/8** |
| `BattleHudEditModeTests` | 3/4（`弹药_装备槽弹匣数与换弹态_未装备显示占位` 失败——**既有**，与本批无关，见上节登记；HP 用例已随新 `HpBar` API 改绿） |
| 执行脚本 | `AgentScripts/BuildHpBarTemplate.cs` + `WireHudHpBarInstance.cs`（未入库，幂等） |

### 遗留（如实）

- 实例带若干**良性 no-op override**（`_Fill`/`_Handle` 的 anchor 记录，实测与模板值全同、不影响渲染）——Unity 生成，无法在不破坏实例的前提下剥离，登记备查；
- `HpBar.cs` 后条走自驱 `Update`（控件自身动画），计时已收敛 `UiAnimationClock`；对《UI框架总设计》U3「HUD 分级驱动/DirtySet」目标项留待批 C；
- 双条后条 `_BackFill` 透明度基线 0.35，待实机目视定档。
