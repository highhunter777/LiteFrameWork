# 战斗HUD层级设计

> 状态：**现行设计**——`Hud_Battle.prefab` 的层级/命名/暴露即当前实现（含血条走控件模板 `Tmp_HpBar` 的 PrefabInstance——见 §2.1；退场菜单角组、排行榜、阵亡面、击杀提示、网络质量、HUD 子件模板与弹药「当前/备弹」三件均已在内）。批次与证据见 [施工进度/UI战斗HUD线](../../../施工进度/UI战斗HUD线.md)。
> 范围：对局战斗 HUD 单件——`Hud_Battle.prefab`（准心 + 血条/弹药/技能栏面板）
> 上位：[战斗HUD专项设计](战斗HUD专项设计.md)（资产/通路/装配裁决）；[UI制作规范](UI制作规范.md) §9（命名）；[UI框架总设计](UI框架总设计.md)（HUD 分级驱动目标项）
> 命名依据：§9 角色词表——本批新增 `Hud_` 根前缀（对局 HUD 根，表现域，不走 tbuiform/导航；已登记命名词表 §1.1）；容器/文本/图像沿用 `Cot_`/`Txt_`/`Image_`/`Icon_`
> 绑定依据：《战斗HUD专项设计》§2.4——HUD 不进 BindNode 表驱动，**序列化引用即契约**：根挂 `BattleHudView`（MonoBehaviour）持暴露字段；本文 `◄View` 标暴露节点（= BattleHudView 字段），纯装饰不设

## 0. 总则

- **命名**：根 `Hud_Battle`（= prefab 文件名 `Hud_Battle.prefab`）；层级尽量浅（三层封顶）。
- **事件隔离**：根 Canvas **无 GraphicRaycaster**＋CanvasGroup `blocksRaycasts/interactable=false`——整个 HUD 不进事件系统（准心永不拦输入）；每个 Graphic 另设 `raycastTarget=false` 双保险。**唯一例外＝菜单角组**（对局退场链，2026-10-14）：`Cot_Settings` 持子 Canvas＋`GraphicRaycaster`＋CanvasGroup（`ignoreParentGroups=true`）——"**单独开射线**"：菜单钮可点、其余 HUD 仍零射线（结构守卫钉住：全树恰 1 个 `raycastTarget=true`＝菜单钮、`GraphicRaycaster` 仅角组一处）。
- **画布**：ScreenSpaceOverlay、`sortingOrder=0`（模态 UI 排其上）、CanvasScaler 1920×1080（match 0.5）。
- **显隐**：`Cot_Panel` 的 CanvasGroup 由驱动按"本地未对齐整组隐藏"开关（比 SetActive 省重建——制作规范 §4）；根 CanvasGroup 不做业务显隐。
- **角组骨架**：三个角落组（Vitals/Weapon/Skills）均为**零尺寸锚点组**（anchor=pivot=锚角，`sizeDelta=0`）——子件各自显式锚/枢轴并以组点为原点偏移（缺省锚=居中于组点=半边出屏）。
- **字体**：NotoSansSC SDF（`Assets/ShareResource/Fonts/`）；文本色默认军事冷白 `(0.92,0.96,1,1)`，换弹琥珀 `(1,0.72,0.25,1)`。
- **素材**：Synty InterfaceMilitaryCombatHUD（`Assets/Synty/InterfaceMilitaryCombatHUD/Sprites/`，第三方付费包不入库——打包机本地还原）＋准心图 `Assets/Art/Crosshair/{normal,aim}.png`。
- **本地化**：HP/Ammo 为数值（Invariant 拼接，`SetText` 语义——由驱动直写）；`Txt_Reload` 当前为固定英文标签"RELOADING"（技能/本地化系统落地时再评估 SetTextKey，本批不做）。
- **技能栏**：静态占位（技能系统未做）——图标位留空，仅 Underlay/Frame/Hotkey。
- **比分（FFA）＝实时排行榜**（排行榜批，2026-10-15 用户裁决）：死亡竞赛下"比分"即**击杀数**——`Cot_Rankboard` 顶部横列 **8 格**（= `eight` 模板上限，**prefab 预置不运行时建**），每格 **上 `Txt_Name` / 下 `Txt_Kills`**，**第一名在最左**（格位固定、内容按 `Kills 降序 → EntityId 升序` 填入，全序防同分抖动）；**自己那格**点亮 `Image_Highlight`。名字本批为**占位 `玩家{k}`**（k = 参赛玩家按 EntityId 升序序号，局内稳定；真名字/昵称归 G3 Profile）。`MatchStateData.Score`（FFA 恒 0，只承载团队模式）仅作 `Txt_Score` **预留节点、缺省隐藏**（`Team=1` 落地即启用）。
- **死亡/复活表现**（阵亡批，2026-10-15 用户裁决）：① **中央复活倒计时**——`Cot_Respawn`（屏幕中央）：`Txt_RespawnTitle`（"已阵亡"，带归属时"被 玩家{k} 击杀"）/ `Txt_RespawnCountdown`（秒 = `RespawnFrame − Frame` 的整秒，**秒粒度才写**；`!IsDead` 即隐藏）；② **阵亡归属**——`Death` 帧事件且 `role==Received`（我阵亡）→ 标题带击杀者身份序号（**占位名单源与排行榜共用**）；③ **保护期角标** `Cot_Protection`（底中）：本地 `Frame < InvulnerableUntilFrame` 显示（**开火即取消**／超时自然结束）。
- **全局击杀提示**（击杀提示批，2026-10-15 用户裁决）：`Cot_KillFeed`（右上角·设置钮下方）——**多条堆叠**（4 行预置；各行独立驻留 4s，行满则新条留队等待、不覆盖他人）；行格式 <c>击杀者 [武器图标] 死者</c>（击杀者=本地 → "你"，否则占位"玩家{k}"；击杀者无归属（自杀/环境）→ <c>玩家{v} 阵亡</c> 且无图标）；数据源＝**快照携带的最近击杀窗口**（`MatchStateDelta.recent_kills`——房间级公共事实，**全局**含他人击杀）；武器图标按**击杀帧装备槽**取图（下标 = 装备槽，收集目录复制件）。
- **网络质量显示**（ping/丢包批，2026-10-15）：`Cot_NetStats`（左上角）——`Txt_NetStats` 显示应用层探针（1Hz `Heartbeat` 回声配对）推断的 **RTT / 丢包千分比**（<c>48ms / 0.3%</c>）；**离线/无样本整组隐藏**（不显示陈旧值）；**表现/诊断面，不参与任何判定**（哨兵/准入一律不读它）。
- **3D 保护壳（FX 域，非本件/本表）**：`fx_spawn_protection`（**金色**半透明包裹壳）挂本地角色视图根跟随——取件租约＋实例（激光束同款），缺件/灰盒静默降级；资产落 FX 收集组（`ShareResource/FX` 件级热更）。与阵亡批同批落地仅是施工批次事实，**归属以 FX 域为准**（设计边界见《[战斗HUD专项设计](战斗HUD专项设计.md)》§4）。

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
   ├─ Cot_Rankboard                 锚(0.5,1)+偏移(0,-16)——顶部中组点（FFA 比分=击杀排行榜）
   │  ├─ Cot_RankCell1..8           8 格横排 = **`Widgets/Tmp_RankCell` 实例**（容器 `HorizontalLayoutGroup` spacing 0 / **UpperLeft**，容器 1120×56）
   │  │                             ——格位固定（枢轴 x = -490 + 140·n）、内容按排行序填；尾空格**关格根**（格内子件保持激活）；改格视觉去模板
   │  │  ├─ Image_Highlight         格底高亮件（**仅自己那格**点亮；缺省非激活）
   │  │  ├─ Txt_Name                TMP 20 居中（上）——占位"玩家{k}"（真名字归 G3 Profile）
   │  │  └─ Txt_Kills               TMP 24 居中（下）——击杀数
   │  └─ Txt_Score                  团队比分（**预留**：Team=1 才显；FFA 缺省隐藏）   ◄View
   ├─ Cot_Respawn                   锚(0.5,0.5)——屏幕中央（死亡等待期显示）
   │  ├─ Txt_RespawnTitle           TMP 40 居中"已阵亡"（有归属 → "被 玩家{k} 击杀"）  ◄View
   │  └─ Txt_RespawnCountdown       TMP 72 居中 剩余秒数（秒粒度才写）                  ◄View
   ├─ Cot_Protection                锚(0.5,0)+偏移(0,120)——底中（复活保护期角标，缺省隐藏）
   │  └─ Txt_Protection             TMP 24 居中"保护中"                                ◄View
   ├─ Cot_KillFeed                  锚(1,1)+偏移(-32,-84)——右上角·设置钮下方（全局击杀提示，行堆叠向下）◄View
   │  └─ Cot_KillFeedRow1..5       行 = **`Widgets/Tmp_KillFeedRow` 实例**（5 行＝上限；容器 `VerticalLayoutGroup` spacing 4 / UpperRight，容器 340×176）
   │                              ——缺省非激活（驱动按待展示队列开关）；行走高/图标/文字偏移去模板改（改一处全行统一）
   │     ├─ Txt_KillFeedKiller     TMP 22 右对齐（"你"/"玩家{k}"）                    ◄View(数组)
   │     ├─ Image_KillFeedWeapon   28×28（基线 = 步枪图标；驱动按击杀帧装备槽换图；无归属隐藏）◄View(数组)
   │     └─ Txt_KillFeedVictim     TMP 22 左对齐（"玩家{v}"）                          ◄View(数组)
   ├─ Cot_NetStats                   锚(0,1)+偏移(32,-24)——左上角（网络质量，缺省隐藏） ◄View
   │  └─ Txt_NetStats               TMP 20 左对齐"48ms / 0.3%"                          ◄View
   ├─ Cot_Weapon                    锚(1,0)+偏移(-32,32)——右下角组点
   │  ├─ Bg_Weapon                  底板（Synty 复制件；纯装饰，不接驱动）
   │  ├─ Icon_Weapon                ICON_SM_Wep_Preset_B_Rifle_03_Military_Clean 96×96（`ShareResource/UI/Icons/Weapons/` 复制件；换图随武器归切枪批）
   │  ├─ Cot_Ammo                   弹匣读出容器（无排布件——三件绝对定位）
   │  │  ├─ Txt_Ammo                TMP 54 右对齐 "30"（当前弹匣；未装备 "--"）       ◄View
   │  │  ├─ Txt_Slash               TMP 54 右对齐 "/"（分隔符——与备弹件**同显隐**）   ◄View
   │  │  └─ Txt_ReserveAmmo         TMP 54 右对齐 "90"（剩余备弹＝本人私有面 `ReserveAmmo`）◄View
   │  └─ Txt_Reload                 TMP 30 琥珀 "RELOADING"（弹药上方）——默认非激活   ◄View
   ├─ Cot_Skills                    锚(0.5,0)+偏移(0,24)——底中组点
      ├─ Cot_Skill1                 96×96，x=-112（槽下半入"地面"）
      ├─ Cot_Skill2                 96×96，x=0
      ├─ Cot_Skill3                 96×96，x=+112
      │   （每槽同构）├─ Image_Underlay  SPR_…_Circle_Medium_01_Underlay
      │               ├─ Image_Frame     SPR_…_Circle_Medium_01_Clean
      │               └─ Txt_Hotkey      TMP 22 左上(4,4) "1"/"2"/"3"
   └─ Cot_Settings                  锚(1,1)+偏移(-32,-32)——右上角组点（**单独开射线**：子 Canvas+GraphicRaycaster+CanvasGroup ignoreParentGroups=true——§0 唯一事件例外）
      └─ Button_Settings            170×44 设置入口（Box_White_01 半透底+Button，全 HUD 唯一 raycastTarget=true）◄View
         └─ Txt_Settings            TMP 20 居中 "SETTINGS"（固定英文——RELOADING 同款口径）
```

尺寸/偏移常量一比一取自构建器（`HipIconSize=64/AdsIconSize=38/Edge=32/WeaponIconSize=96/AmmoTextSize=54/ReloadTextSize=20/SkillSlotSize=96/SkillGap=112/HotkeyTextSize=22`）——prefab 制作后这些常量随构建器退役，**prefab 即唯一事实源**。血条（原构建器常量 `HpBarW=420/HpBarH=56/HpFillInset=14/HpTextSize=26`）演进史：**血条正式化批**（2026-10-11）改 Synty 件承载视觉 → **解包收口**（同日晚）展平为普通对象 → **血条模板化批**（同日晚）**回落为控件模板 `Tmp_HpBar` 的 PrefabInstance**（模板＝血条单一形态来源，见下 §2.1；`HpBar_Player` 只保留位置/尺寸 override）。

## 2.1 血条形态来源：控件模板 `Tmp_HpBar`

血条**不再是 HUD 私有结构**，而是[控件模板](UI命名词表与控件清单参考.md) `Tmp_HpBar`（`Assets/ShareResource/UI/Widgets/Tmp_HpBar.prefab`）的使用变体——模板为血条**单一形态来源**（Synty 军事风皮肤 + UGUI Slider 前条 + 半透明 `_BackFill` 后条 + IconBox + 数值文本）。

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

## 3. BattleHudView 字段映射（暴露 → 消费）

| 暴露节点 | BattleHudView 字段 | 消费驱动 | 语义 |
| --- | --- | --- | --- |
| `Cot_Crosshair` | `RectTransform CrosshairRoot` | BattleCrosshairDriver | 每帧写屏位（AimPoint 单口径）+ 散布/连发缩放与后坐偏移（手感按 `tbcrosshair` 武器行，GM「准心调参」滑杆定档） |
| `Icon_Hip` / `Icon_Ads` | `GameObject HipForm / AdsForm` | BattleCrosshairDriver | 腰射/瞄准形态开关（含迟滞） |
| `Cot_Panel` | `CanvasGroup PanelGroup` | BattleHudDriver | 本地未对齐整组隐藏 |
| `HpBar_Player`（模板实例根） | `HpBar HpBar` | BattleHudDriver | 经控件 API `Set(hp, max)`——前条瞬时 + 后条延迟滑落（双条血格） |
| `Txt_Ammo` | `TMP_Text AmmoText` | BattleHudDriver | 弹匣数（值变化才写；未装备显示 `--`） |
| `Txt_ReserveAmmo` | `TMP_Text ReserveAmmoText` | BattleHudDriver | 剩余备弹（本人私有面 `WeaponRuntime.ReserveAmmo`——换弹从备弹扣、随消耗递减；未装备**随占位收起**，不留陈旧值） |
| `Txt_Slash` | `GameObject SlashSeparator` | BattleHudDriver | 「当前 / 备弹」分隔符（与备弹件**同显隐**——收起时读出只剩 `--`） |
| `Txt_Reload` | `GameObject ReloadLabel` | BattleHudDriver | 按 `WeaponSlotState.Reloading` 开关 |
| `Button_Settings` | `Button SettingsButton` | BattleViewRig（装配期接 onClick → `PageFlow.OpenBattleSettings`，拆装期摘） | 对局设置入口（退场链）——点击开设置页（对局中导航数据带 `battleExit`，ESC 空栈同源） |
| `Cot_Rankboard` | `RectTransform RankboardRoot` | BattleRankboardDriver | 排行榜容器（日志口；显隐随对局） |
| `Cot_RankCell1..8`（`Tmp_RankCell` 实例 ×8） | `GameObject[] RankCells`（格根） / `TMP_Text[] RankNames` / `TMP_Text[] RankKills` / `GameObject[] RankHighlights` | BattleRankboardDriver | 格位固定（容器 `HorizontalLayoutGroup` spacing 0 / UpperLeft 排布）；按 `Kills 降序 → EntityId 升序` 从左往右填（左=第一）；高亮仅自己格；人数 < 8 **尾部空格隐藏＝关格根**（格内子件保持激活，便于编辑） |
| `Txt_Score` | `TMP_Text TeamScoreText` | BattleRankboardDriver | 团队比分**预留**（`Team==0` 隐藏——FFA 恒 0 不显示假数据） |
| `Cot_Respawn` | `RectTransform RespawnRoot` | BattleRespawnHudDriver | 死亡等待面板显隐（`SimView.IsDead` 开关） |
| `Txt_RespawnTitle` | `TMP_Text RespawnTitle` | BattleRespawnHudDriver | "已阵亡"／带归属"被 玩家{k} 击杀"（`Death` 帧事件 `role==Received` 驱动，事件到达才写） |
| `Txt_RespawnCountdown` | `TMP_Text RespawnCountdown` | BattleRespawnHudDriver | 剩余秒（`RespawnFrame − Frame` 取整秒；**秒粒度才写**） |
| `Cot_Protection` | `RectTransform ProtectionRoot` | BattleRespawnHudDriver | 保护期角标显隐（`Frame < InvulnerableUntilFrame`；开火取消/超时自然结束） |
| `Cot_KillFeed` | `RectTransform KillFeedRoot` | BattleKillFeedDriver | 击杀提示容器（显隐随对局；日志口） |
| `Cot_KillFeedRow1..5`（`Tmp_KillFeedRow` 实例 ×5） | `GameObject[] KillFeedRows` / `TMP_Text[] KillFeedKillers` / `Image[] KillFeedWeapons` / `TMP_Text[] KillFeedVictims` | BattleKillFeedDriver | **5 行全预置＝上限**（容器 `VerticalLayoutGroup` 排布）；消费**公共击杀面**（快照 `recent_kills`）逐条播报，**多条堆叠**、行满留队、驻留 4s 回收 |
| `Image_KillFeedWeapon`（各槽基线图） | `Sprite[] WeaponIcons` | BattleKillFeedDriver | 武器图标（下标 = 装备槽 `0..WeaponSlotsPerEntity-1`，收集目录复制件）——按击杀帧槽取图 |
| `Cot_NetStats` / `Txt_NetStats` | `RectTransform NetStatsRoot` / `TMP_Text NetStatsText` | BattleNetworkHudDriver | 网络质量显示（探针 RTT/丢包千分比）；离线/无样本整组隐藏 |

暴露共 8 项=面板/准心/菜单驱动的读写面；**排行榜批（2026-10-15）追加 4 项**（容器 + 名字/击杀/高亮三数组 + `Txt_Score` 预留），由 `BattleRankboardDriver` 独担；**阵亡批（2026-10-15）追加 5 项**（`Cot_Respawn` 面板 3 件 + `Cot_Protection` 角标 2 件），由 `BattleRespawnHudDriver` 独担；**击杀提示批（2026-10-15）追加 6 项**（`Cot_KillFeed` 容器 + 行四组数组 + 武器图标数组），由 `BattleKillFeedDriver` 独担；**ping/丢包批（2026-10-15）追加 2 项**（`Cot_NetStats`/`Txt_NetStats`），由 `BattleNetworkHudDriver` 独担——四批各自单一职责、互不混装；**弹药读出（2026-10-16）追加 2 项**（`Txt_ReserveAmmo` ＋ `Txt_Slash`，与在册的 `Txt_Ammo` 同组「当前 / 备弹」——`Txt_MaxAmmo` 由此改名），由 `BattleHudDriver` 兼担——`Cot_Ammo` 容器不暴露（显隐只开两件后缀）。血条走 `LiteGame.UI.HpBar` 组件 API（单点引用，避免嵌套深层字段 override）；`Cot_Vitals/Cot_Weapon/Cot_Skills/Cot_Rankboard/Cot_Respawn/Cot_Protection/Cot_KillFeed/Cot_NetStats/Cot_Ammo` 容器及装饰件不暴露（`Cot_Settings` 组件不暴露——按钮即交互面）。技能槽无数据面（占位），数据面落地时随批 C 扩展（届时评估技能 CD 走 UI 框架 U2 控件；技能数据面已按裁决移出批 C——待技能系统）。

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
| —（新增） | `Cot_Rankboard` / `Cot_RankCell1..8` / `Txt_Name` / `Txt_Kills` / `Image_Highlight` / `Txt_Score` | 排行榜批（2026-10-15）：FFA 比分=击杀（无旧名，全新件） |
| —（新增） | `Cot_Respawn` / `Txt_RespawnTitle` / `Txt_RespawnCountdown` / `Cot_Protection` / `Txt_Protection` | 阵亡批（2026-10-15）：死亡等待倒计时 + 保护期角标（无旧名，全新件）；3D 保护壳见 `Assets/ShareResource/FX/fx_spawn_protection.prefab`（金色，归 FX 域——不走本表，见 §0） |
| —（新增） | `Cot_KillFeed` / `Cot_KillFeedRow1..4` / `Txt_KillFeedKiller` / `Image_KillFeedWeapon` / `Txt_KillFeedVictim` | 击杀提示批（2026-10-15）：全局击杀提示（无旧名，全新件；4 行全预置、行堆叠向下） |
| —（新增） | `Tmp_RankCell` / `Tmp_KillFeedRow`（子件模板，`Widgets/`） | HUD 子件与容器批（2026-10-16）：排行格/播报行抽模板＋容器排布；行数 4→5 |
| —（新增） | `Cot_NetStats` / `Txt_NetStats` | ping/丢包批（2026-10-15）：网络质量显示（无旧名，全新件） |
| —（新增／制作侧手改） | `Bg_Weapon` / `Cot_Ammo`（容器）/ `Txt_Slash`（原 `Txt_/`，UI 工具链批改名）/ `Txt_ReserveAmmo`（原 `Txt_MaxAmmo`，随语义更正改名） | 弹药读出（2026-10-16）：右下角「当前弹匣 / 剩余备弹」读出——`Txt_Ammo` 移入 `Cot_Ammo` 容器；备弹＝`WeaponRuntime.ReserveAmmo`（未装备随 `--` 收起），分隔符与备弹件同显隐 |
