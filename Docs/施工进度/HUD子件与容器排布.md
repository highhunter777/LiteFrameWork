# HUD 子件与容器排布批（排行格 / 击杀播报）

用户指令（2026-10-16）："抽 2 个子件预制体，上 LayoutGroup；击杀播报统一改成我设置的第一个单元的参数；击杀播报上限暂时设定为 5。"

## 状态

**已完成（附验证证据）**——2026-10-16（已提交 `e2c97baa`）。

## 交付

| 面 | 落点 |
| --- | --- |
| 子件模板 ×2（视觉单源） | `Widgets/Tmp_RankCell.prefab`（排行格：`Txt_Name`/`Txt_Kills`/`Image_Highlight`，140×56）／`Widgets/Tmp_KillFeedRow.prefab`（播报行：`Txt_KillFeedKiller`/`Image_KillFeedWeapon`/`Txt_KillFeedVictim`）——**以用户手调的第一个单元为参数源**（图标 **100×100**、击杀者 x=**-118.3**、死者 x=**+118.3**）；改模板一处，全部实例统一 |
| 实例化 + 容器排布 | `Hud_Battle.prefab`：`Cot_RankCell1..8` → `Tmp_RankCell` 实例 ×8 ＋容器 **`HorizontalLayoutGroup`**（spacing 0 / UpperCenter / childControl* false；容器 1120×56）；`Cot_KillFeedRow1..5` → `Tmp_KillFeedRow` 实例 ×5 ＋容器 **`VerticalLayoutGroup`**（spacing 4 / UpperRight；容器 340×176）；`Txt_Score`（团队比分预留）加 `LayoutElement.ignoreLayout=true` 留在容器外 |
| 播报上限 4 → 5 | `BattleKillFeedDriver.RowCount = 5`（注释挂结构守卫口径）；`BattleHudView` 四组引用数组重接 5 项（含新 Row5） |
| 引用面不破 | 实例根名保持 `Cot_RankCell{i}`/`Cot_KillFeedRow{i}`——`BattleHudView` 序列化引用与 `FindDeep` 按名寻址的结构守卫同名即通 |
| 旧构建器防误跑 | `AgentScripts/BuildHudKillFeed.cs`/`BuildHudRankboard.cs` 加**结构收敛守卫**（检测到容器即 `SKIP`）——防止按旧形态重建内联节点并静默回退行数/结构 |

**位置对账（与改造前逐值一致）**：格枢轴 x ＝ `容器rect.xMin(-560) + anchoredPosition.x` ＝ **-490 + 140n**（Play 实测世界 x = 470…1450，对称于 1920 屏心 960）；行 y ＝ **0 / -36 / -72 / -108 / -144**。
**容器接管口径（重要）**：排布容器会**规范化子件锚点**（格被驱动为 anchorMin/Max=(0,1)、位置由容器算）——改造后**不得再手工摆位**；行/格内要加位移动效须动**内层承载件**（外层锚点归容器）。此口径已写入《UI 制作规范》§3。

## 验证证据

| 项 | 结果 |
| --- | --- |
| Unity 重编译 | 0 错误（`RowCount` 改动后多轮） |
| L1 门禁 | **[PASS] 1715**（`pwsh scripts/gate/test.ps1 -Lane L1 -Profile PullRequest`）。中途一次红：`RowCount` 注释带批次/日期 → `纪律_代码注释不含施工痕迹` 拦下 → 改为只陈述约束，复跑全绿 |
| EditMode（4 组 **26 例全绿**） | `BattleHudMenuEditModeTests` 8/8（结构守卫：8 格在位＋按 `RowCount` 逐行点名 5 行、缺省非激活、零射线越界；含顺带修的陈旧断言）· `BattleHudEditModeTests` 4/4 · `BattleKillFeedEditModeTests` 6/6（堆叠/留队/驻留语义，数组按 `RowCount` 自动 5 项）· `BattleRankboardEditModeTests` 8/8 |
| Play 实测（真机链路） | Boot 直入训练场（用户当前开关）→ HUD 在场 `Cot_Panel.alpha=1`；`Cot_Rankboard` 容器 + 8 格（世界 x 470/610/…/1450、可见）· `Cot_KillFeed` 容器 + **5 行**（y 0/-36/-72/-108/-144、缺省非激活）· 行内图标 **(100×100)**；截图 `AgentScripts/Artifacts/hud-5rows.png`（8 格布局在场：2 名参赛者、尾部空格隐藏） |
| 术语/计数同步 | 《UI 制作规范》§2/§9 模板计数 27→**29**、《UI 命名词表与控件清单》§1.3 增两行、《战斗HUD层级设计》§2 树/§4 暴露表/命名演进、《修改指南》§3.3 HUD 行；`doc-lint` OK |

## 续：单元级显隐口径（用户反馈，同日）

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

## 如实说明（待裁决 / 边界）

| 项 | 说明 |
| --- | --- |
| 行高与图标尺寸不匹配 | 你的参数（图标 **100×100**）落在**行高 32、行距 4** 的行上——图标会压过相邻行。已按指令统一，未擅自改几何；要排开只需两个旋钮：**模板行高**（`Tmp_KillFeedRow`）＋**容器 spacing**（如行高 100 / spacing 8）。 |
| 播报行的**游戏内观感**未实见 | 你 Boot 的 `NoDeath=1`（未提交手调）使假人不死 → 造不出真实击杀；行内容链路由 `BattleKillFeedEditModeTests` 6/6 覆盖，Play 只证到"5 行在位、缺省非激活、图标=100×100、容器位置对"。 |
| 早前两条判断**更正** | 我曾报"Cot_Panel 全屏 Image""Cot_KillFeed 被默认关"——均为我的 YAML 归属解析**误报**（实测组件清单：`Cot_Panel` 无 Image、容器为开；恢复脚本为空操作）。实际发生的是**射线越界 1 处**（第 2 个 `raycastTarget`）→ 已归零，现与 HEAD 一致（唯一菜单钮）。 |
| 并发编辑提醒 | 本批期间该 prefab 被**并发保存**过一次（曾致第 5 行丢失、模板参数被回带旧值）；我已重建并逐值复验。若编辑器里仍开着该 prefab，请以磁盘为准（关闭重开）再编辑。 |
| 保留的未提交手调（未处置） | 字体色统一 `#EBF5FF`（多处 TMP `fontColor32`）· `Txt_Score` 文案 `0 - 0` · `Cot_Settings` 挪到 `(-84.8, -22.1)` · `Boot.unity` 的 `UseLocalServer/DirectToTraining/DevMenu = 1`——一律保留，仅报告。 |
| 顺带修（审计已登记） | `BattleHudMenuEditModeTests.设置页_退场钮…` 断言 `BindName=="Exit"` 属**陈旧断言**（HEAD 即有红：流程批 IV 已按"节点名同文"把 prefab 改为 `Button_Exit`/`Txt_Exit`）→ 已按 IV 口径校正。 |
