# 战斗HUD层级设计

> 状态：**已实施（2026-10-11 批 A 落地；同日晚血条正式化／解包／模板化三批更新）**——`Hud_Battle.prefab` 制作/接线/收集/场景退役全部完成；批 B 装配拆分已由并行 BattleViewRig 批完成；**血条模板化**（血条改为控件模板 `Tmp_HpBar` 的 PrefabInstance，驱动走 `HpBar` 组件 API——见 §2.1）同日落位
> 范围：对局战斗 HUD 单件——`Hud_Battle.prefab`（准心 + 血条/弹药/技能栏面板）
> 上位：[战斗HUD专项设计](战斗HUD专项设计.md)（资产/通路/装配裁决）；[UI制作规范](UI制作规范.md) §9（命名）；[UI框架总设计](UI框架总设计.md)（HUD 分级驱动目标项）
> 命名依据：§9 角色词表——本批新增 `Hud_` 根前缀（对局 HUD 根，表现域，不走 tbuiform/导航；已登记命名词表 §1.1）；容器/文本/图像沿用 `Cot_`/`Txt_`/`Image_`/`Icon_`
> 绑定依据：《战斗HUD专项设计》§2.4——HUD 不进 BindNode 表驱动，**序列化引用即契约**：根挂 `BattleHudView`（MonoBehaviour）持暴露字段；本文 `◄View` 标暴露节点（= BattleHudView 字段），纯装饰不设

## 0. 总则

- **命名**：根 `Hud_Battle`（= prefab 文件名 `Hud_Battle.prefab`）；层级尽量浅（三层封顶）。
- **事件隔离**：根 Canvas **无 GraphicRaycaster**＋CanvasGroup `blocksRaycasts/interactable=false`——整个 HUD 不进事件系统（准心永不拦输入）；每个 Graphic 另设 `raycastTarget=false` 双保险。
- **画布**：ScreenSpaceOverlay、`sortingOrder=0`（模态 UI 排其上）、CanvasScaler 1920×1080（match 0.5）。
- **显隐**：`Cot_Panel` 的 CanvasGroup 由驱动按"本地未对齐整组隐藏"开关（比 SetActive 省重建——制作规范 §4）；根 CanvasGroup 不做业务显隐。
- **角组骨架**：三个角落组（Vitals/Weapon/Skills）均为**零尺寸锚点组**（anchor=pivot=锚角，`sizeDelta=0`）——子件各自显式锚/枢轴并以组点为原点偏移（缺省锚=居中于组点=半边出屏）。
- **字体**：NotoSansSC SDF（`Assets/ShareResource/Fonts/`）；文本色默认军事冷白 `(0.92,0.96,1,1)`，换弹琥珀 `(1,0.72,0.25,1)`。
- **素材**：Synty InterfaceMilitaryCombatHUD（`Assets/Synty/InterfaceMilitaryCombatHUD/Sprites/`，第三方付费包不入库——打包机本地还原）＋准心图 `Assets/Art/Crosshair/{normal,aim}.png`。
- **本地化**：HP/Ammo 为数值（Invariant 拼接，`SetText` 语义——由驱动直写）；`Txt_Reload` 当前为固定英文标签"RELOADING"（技能/本地化系统落地时再评估 SetTextKey，本批不做）。
- **技能栏**：静态占位（技能系统未做）——图标位留空，仅 Underlay/Frame/Hotkey。

## 1. 资产与场景关系

```
Assets/ShareResource/UI/Hud/Hud_Battle.prefab   ← 视觉单一来源（收集路径 PackSeparately——件级热更）
Assets/Scenes/TrainingGround.unity               ← /Battle HUD 场景对象已退役（场景不承载 HUD 视觉；场景名随并行批去中文化）
Assets/Scenes/HUDDesign.unity                    ← 保留为 HUD 制作/预览工作台（prefab 预览环境）
AgentScripts/BuildBattleHud.cs                   ← 迁移批 A 完成后退役（双写根源消除）
```

## 2. 层级树

```
Hud_Battle                          Canvas(Overlay, order 0) + CanvasScaler(1920×1080, match .5) + CanvasGroup(不拦射线)
├─ Cot_Crosshair                    中心锚点容器（驱动写屏位）                        ◄View
│  ├─ Icon_Hip                      腰射准星 Image(Assets/Art/Crosshair/normal) 64×64 ◄View
│  └─ Icon_Ads                      瞄准准星 Image(…/aim) 64×64（**用户裁决改 64**——原构建器 38）◄View
└─ Cot_Panel                        全屏铺开 + CanvasGroup（整组显隐）                ◄View
   ├─ Cot_Vitals                    锚(0,0)+偏移(32,32)——左下角组点
   │  └─ HpBar_Player               血条＝**控件模板 `Tmp_HpBar` 的实例**（业务变体＝`<类型段>_<语义>`）◄View
   │     │（结构＝模板，见《UI命名词表与控件清单》§1.3 `HpBar`／模板 `Assets/ShareResource/UI/Widgets/Tmp_HpBar.prefab`）
   │     ├─ _Bar                    分段条容器（模板内件，`_` 私有前缀）
   │     │  ├─ _Slider               UGUI Slider（前条驱动面：`value` 0..1，纯显示）   ◄View(经 HpBar 组件)
   │     │  ├─ _Background           底槽
   │     │  └─ _FillArea             前/后条容器（fillRect）
   │     │     ├─ _Fill              前条
   │     │     └─ _BackFill          后条（半透明克隆层，延迟滑落——双条血格）
   │     ├─ Cot_IconBox             血条左端图标盒
   │     │  └─ Icon_Health          ICON_MilitaryCombat_Status_Health_02_Underlay
   │     └─ Txt_Hp                  TMP 血量数值（值变化才写）                        ◄View(经 HpBar 组件)
   ├─ Cot_Weapon                    锚(1,0)+偏移(-32,32)——右下角组点
   │  ├─ Icon_Weapon                ICON_SM_Wep_Rifle_01_Military 96×96
   │  ├─ Txt_Ammo                   TMP 54 右对齐 "30"（图标左侧 16、底缘上抬 8）      ◄View
   │  └─ Txt_Reload                 TMP 20 琥珀 "RELOADING"（弹药上方 96）——默认非激活 ◄View
   └─ Cot_Skills                    锚(0.5,0)+偏移(0,24)——底中组点
      ├─ Cot_Skill1                 96×96，x=-112（槽下半入"地面"）
      ├─ Cot_Skill2                 96×96，x=0
      ├─ Cot_Skill3                 96×96，x=+112
      │   （每槽同构）├─ Image_Underlay  SPR_…_Circle_Medium_01_Underlay
      │               ├─ Image_Frame     SPR_…_Circle_Medium_01_Clean
      │               └─ Txt_Hotkey      TMP 22 左上(4,4) "1"/"2"/"3"
```

尺寸/偏移常量一比一取自构建器（`HipIconSize=64/AdsIconSize=38/Edge=32/WeaponIconSize=96/AmmoTextSize=54/ReloadTextSize=20/SkillSlotSize=96/SkillGap=112/HotkeyTextSize=22`）——prefab 制作后这些常量随构建器退役，**prefab 即唯一事实源**。血条（原构建器常量 `HpBarW=420/HpBarH=56/HpFillInset=14/HpTextSize=26`）演进史：**血条正式化批**（2026-10-11）改 Synty 件承载视觉 → **解包收口**（同日晚）展平为普通对象 → **血条模板化批**（同日晚）**回落为控件模板 `Tmp_HpBar` 的 PrefabInstance**（模板＝血条单一形态来源，见下 §2.1；`HpBar_Player` 只保留位置/尺寸 override）。

## 3. BattleHudView 字段映射（暴露 → 消费）

| 暴露节点 | BattleHudView 字段 | 消费驱动 | 语义 |
| --- | --- | --- | --- |
| `Cot_Crosshair` | `RectTransform CrosshairRoot` | BattleCrosshairDriver | 每帧写屏位（AimPoint 单口径） |
| `Icon_Hip` / `Icon_Ads` | `GameObject HipForm / AdsForm` | BattleCrosshairDriver | 腰射/瞄准形态开关（含迟滞） |
| `Cot_Panel` | `CanvasGroup PanelGroup` | BattleHudDriver | 本地未对齐整组隐藏 |
| `HpBar_Player`（模板实例根） | `HpBar HpBar` | BattleHudDriver | 经控件 API `Set(hp, max)`——前条瞬时 + 后条延迟滑落（双条血格） |
| `Txt_Ammo` | `TMP_Text AmmoText` | BattleHudDriver | 弹匣数（值变化才写） |
| `Txt_Reload` | `GameObject ReloadLabel` | BattleHudDriver | 按 `WeaponSlotState.Reloading` 开关 |

暴露共 7 项=驱动的全部读写面；血条走 `LiteGame.UI.HpBar` 组件 API（单点引用，避免嵌套深层字段 override）；`Cot_Vitals/Cot_Weapon/Cot_Skills` 及装饰件不暴露。技能槽无数据面（占位），数据面落地时随批 C 扩展（届时评估技能 CD 走 UI 框架 U2 控件）。

## 2.1 血条形态来源：控件模板 `Tmp_HpBar`

血条**不再是 HUD 私有结构**，而是[控件模板](UI命名词表与控件清单.md) `Tmp_HpBar`（`Assets/ShareResource/UI/Widgets/HpBar.prefab`）的使用变体——模板为血条**单一形态来源**（Synty 军事风皮肤 + UGUI Slider 前条 + 半透明 `_BackFill` 后条 + IconBox + 数值文本）。

| 层 | 资产 | 命名 | 说明 |
| --- | --- | --- | --- |
| 模板 | `ShareResource/UI/Widgets/Tmp_HpBar.prefab` | 根 `Tmp_HpBar`；内部件 `_Bar`/`_Slider`/`_Background`/`_FillArea`/`_Fill`/`_BackFill`（§9.4 私有）；暴露件 `Cot_IconBox`/`Icon_Health`/`Txt_Hp` | 挂 `LiteGame.UI.HpBar` + `BindNode`；收集组 UIWidgets |
| 业务实例 | `Hud_Battle` 的 `Cot_Panel/Cot_Vitals/HpBar_Player` | `HpBar_Player`（§9.3 业务变体＝`<类型段>_<语义>`） | 模板 PrefabInstance；只 override 位置/尺寸 |

- **驱动**：`LiteGame.UI.HpBar` 持 `Slider`（前条）+ `BackFill`（后条）+ `ValueText`；`Set(current, max)` 写前条 `Slider.value`、后条按 `BackLerpSpeed` 逐帧滑落追赶（计时走 `UiAnimationClock.Delta`——§5／红线 9）。后条仅在数值**下降**时滑落，上升瞬时跟上。
- **后条几何**：与 Slider `fillRect` 同锚/枢轴/尺寸，`HpBar.SetBackAnchor` 只改 `anchorMax.x`（与 UGUI Slider 对 fillRect 的可视更新同款手势）。
- 消费者兼容：公开 API `HpBar.Set(current,max)` 与 `HpBar` 类型不变——`UIDemoPage` / `UIBindIndex.SetHp` 零改动。

### 2.1.1 为什么 HUD 血条不走绑定生成（裁决，非遗漏）

框架的绑定体系（`UIBindBase` 路径 A/B：`BindIndexBuilder` 扫 `BindNode` 建索引，或生成类 `CollectControls` 登记字段）**服务的是页面体系**——经 `UINavigationController` 打开、有 `UIForm` 生命周期、经 `BindName` 受控 API（`SetText`/`OnButton`）取件、供 Lua 按名访问。**HUD 被裁定归对局表现域**（§2.2：不走导航、无 `UIForm` 承载、随对局生命周期建/还），故不入该体系——这是 §2.4「序列化引用即契约」的落地，**是有意裁决，不是漏用框架**。

| 面 | 绑定生成（页面体系） | HUD 血条（表现域） |
| --- | --- | --- |
| 生命周期 | `UIForm`（导航开/关） | 对局 Scope（`BattleViewRig` 租约建/还） |
| 取件 | `BindName` 索引 / 生成类字段 | **序列化引用**（`BattleHudView._hpBar` 直连 `HpBar` 组件） |
| 适合结构 | 可变、动态列表、Lua 按名取 | **固定结构**（节点数写死，无动态列表） |
| 驱动 | `UIBindIndex.Get<T>(name)` | 强类型组件引用（编译期校验，零字符串键） |

- **`BindNode` 保留的用途**：`Tmp_HpBar` 模板根所挂 `BindNode`**不为 HUD 消费**——HUD 侧直持 `HpBar` 组件（上表第 4 行）；该标记为**未来页面消费者**（队伍血条、设置页血量预览等经页面体系使用的场景）预留——届时由页面按 §9 填 `BindName` 入索引。模板不留非空 `BindName`（§9.4），故 HUD 实例无绑定键、与「HUD 不读 BindNode」自洽。
- **边界**：若 HUD 未来出现**数据驱动子件**（技能 CD 环等），再评估该子件入 Bind 体系（U2 目标项归 UI 框架节奏）。

## 4. 制作与迁移路径（批 A 执行序）

1. 构建器 `BuildDesign()` 在 HUDDesign 场景产树（现实现，零改动）→ 整树另存为 prefab；
2. 按 §2 更名（`Battle HUD`→`Hud_Battle`、`Crosshair`→`Cot_Crosshair`、`Panel/Vitals/…`→`Cot_/Txt_/Image_/Icon_`——改名映射即本表）→ 挂 `BattleHudView` 并连 8 个字段；
3. prefab 落 `Assets/ShareResource/UI/Hud/` → collector 新增该收集路径（PackSeparately，ui tag）；
4. 训练场场景删除 `/Battle HUD` 对象；`ProcedureBattle` 接线切 `BattleViewRig`（批 B）；
5. 构建器退役；HUDDesign 场景保留（prefab 预览）。

## 5. 与旧层级名差异（改名映射）

| 旧（构建器/场景） | 新（§9） | 说明 |
| --- | --- | --- |
| `Battle HUD` | `Hud_Battle` | 新增 Hud_ 前缀（命名词表 §1.1 已登记） |
| `Crosshair` | `Cot_Crosshair` | 容器词缀 |
| `Hip` / `Ads` | `Icon_Hip` / `Icon_Ads` | 图标词缀 |
| `Panel` | `Cot_Panel` | 显隐组容器 |
| `Vitals` / `Weapon` / `Skills` | `Cot_Vitals` / `Cot_Weapon` / `Cot_Skills` | 角组容器 |
| `Frame` / `Fill` / `HP`（旧框条） | ~~`Image_HpFrame` / `Image_HpFill` / `Txt_Hp`~~ | **已退役**（血条正式化批：旧 Filled 图形血条删除） |
| `Item_00`（旧粘贴草名／Synty 件） | `HpBar_Player` | **词表段 `HpBar_`**（命名词表 §1.3）——业务层实例＝`<类型段>_<语义>`（§9.3）；**血条模板化批**起＝模板 `Tmp_HpBar` 的 PrefabInstance（见 §2.1） |
| `Bar` / `IconBox` / `ICON` / `Label_Stat`（曾入 HUD 血条内） | 归**模板**：`_Bar` / `Cot_IconBox` / `Icon_Health` / `Txt_Hp` | 血条内部结构随模板化移入 `Tmp_HpBar`（模板内件 `_` 私有，暴露件词缀） |
| `HP` / `Ammo` / `Reload` | `Txt_Hp` / `Txt_Ammo` / `Txt_Reload` | 文本词缀 |
| `Icon`（Weapon 下） | `Icon_Weapon` | 消除与词缀 Icon_ 的歧义 |
| `Slot1..3` / `Underlay` / `Hotkey` | `Cot_Skill1..3` / `Image_Underlay` / `Txt_Hotkey` | 词缀化 |
