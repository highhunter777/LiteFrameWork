# HUD 弹药读出接线（当前弹匣 / 剩余备弹）

用户指令（2026-10-16）："把 `Txt_MaxAmmo` 接上（按 `MagazineSize` 写，未装备时随 `--` 一起收）"；同日复核点单**更正语义**——"这个语义不是剩余备弹吗" → 用户裁决：读出右半边＝**剩余备弹**（`WeaponRuntime.ReserveAmmo`），节点/字段名同步改 `Txt_ReserveAmmo`／`_reserveAmmoText`（名实一致）。

## 状态

**已完成（附验证证据）**——2026-10-16/17（已提交 `e2c97baa`）。

## 交付

| 面 | 落点 |
| --- | --- |
| 引用件 | `BattleHudView` 增 `_reserveAmmoText`（→`Txt_ReserveAmmo`，由 `Txt_MaxAmmo` **改名**）／`_slashSeparator`（→`Txt_Slash`）＋访问器；`Hud_Battle.prefab` 经 pipeline `run_script` 接线＋改名（回读复核见下） |
| 驱动 | `BattleHudDriver`：装备态写**剩余备弹**（本人私有面 `WeaponRuntime.ReserveAmmo`——换弹从备弹扣、随消耗递减；**值变化才写**）；**未装备** → `Txt_Ammo` 占位 `--`（常量 `UnarmedAmmoPlaceholder`）且**备弹件＋分隔符整组收起** |
| 装配 | `BattleViewRig.AttachHud` 传两引用；缺件＝驱动内静默降级（只显当前弹匣——兼容旧 prefab） |
| 守卫/用例 | `BattleHudMenuEditModeTests` 结构守卫（`Txt_ReserveAmmo` 在位 ＋ `view.ReserveAmmoText` 同一性 ＋ 缺省在显 ＋ 零新射线）；`BattleHudEditModeTests`：装备态「17 / 90」、未装备随占位整组收起、备弹递减写值（SENTINEL 脏位用例） |
| 文档 | 《战斗HUD层级设计》§0 头部／§2 树（`Txt_ReserveAmmo`、`Cot_Ammo`、`Bg_Weapon`；`Txt_Reload` 字号按实测 20→30 校正）／§3 暴露表／§5 改名表；《修改指南》§3.3 HUD 行；审计表登记＋修复登记回写 |

## 验证证据

| 项 | 结果 |
| --- | --- |
| Unity 重编译 | 0 错误（`errors: []`；`eval` 实证新字段在编译产物内在位） |
| EditMode（**六组 34 例全绿**） | `BattleHudMenuEditModeTests` **9/9**（含新结构守卫）· `BattleHudEditModeTests` **5/5**（含新用例）· `BattleRankboardEditModeTests` 8/8 · `BattleKillFeedEditModeTests` 6/6 · `BattleRespawnHudEditModeTests` 4/4 · `BattleNetworkHudEditModeTests` 2/2 |
| prefab 写后回读 | 19 项标量引用非空、9 组数组 `null=0`（8/8/8/8 · 5/5/5/5 · 2）；`_reserveAmmoText → Cot_Panel/Cot_Weapon/Cot_Ammo/Txt_ReserveAmmo`、`_slashSeparator → …/Txt_Slash`；旧字段 `_maxAmmoText` 与旧名 `Txt_MaxAmmo` **零残留**；结构不变量：节点数 102 · `raycastTarget`=1 · `GraphicRaycaster`=1 |
| diff 对账 | 首次接线（无 Stage）与写前快照 diff **仅 2 行**；本轮改名为 Stage 写入（盘面快照按纪律跳过）——对账基准＝首轮快照，差异逐项＝①两字段接线 ②`Txt_Reload` 激活＋x −41.3→20（**用户 Stage 未存编辑，随保存落盘**）③节点改名 |
| L1 门禁 | `pwsh scripts/gate/test.ps1 -Lane L1 -Profile PullRequest` → **[PASS] 1715** |
| doc-lint | `python scripts/gate/doc-lint.py` → OK |

## 语义更正留痕（如实）

初版按用户首条指令以**弹匣容量** `MagazineSize` 写（恒 30，换弹不变）；用户复核指出语义应为**剩余备弹**。更正依据（动手前核实）：《游戏业务系统总设计》§5 武器表列头即「**弹匣/备弹**」（步枪 **30 / 90**、霰弹 **8 / 24**）；sim `WeaponSystem.CompleteReload` 从 `ReserveAmmo` 扣弹、`EquipDefault` 按表 `ReserveAmmo` 起备弹——故收紧为 `ReserveAmmo`（本人私有面，与 `MagAmmo` 同源直读），节点/字段同步改名，避免"`MaxAmmo` 名 + 备弹语义"名实不符。

## 如实说明（边界）

| 项 | 说明 |
| --- | --- |
| 真机观感未实测 | 写入与验证时编辑器处于**用户的 Play 会话**（不打断）；装备态「30 / 90」显示链路由 EditMode 钉住，未见真机画面 |
| 并发编辑（本轮实况） | 写入时**用户的 Prefab Stage 开着**该 prefab：按既有纪律写 Stage 内容（防 Stage 回写覆盖），Stage 中未存编辑（`Txt_Reload` 激活＋x=20）随保存一并落盘——已逐项对账报告，未做还原 |
| 备弹语义形态 | 剩余备弹**随消耗递减**（当前无补给源，只降不升）；「备弹上限」`MaxReserveAmmo` 不上 HUD（用户裁决只接剩余） |
| 切枪 | 备弹随 `SelectedWeapon` 换槽直读已就位；武器图标随武器换图仍归切枪批（本次未动） |
| 工具面 | 编辑器 Play 会话期间本机 `unity` CLI 实例发现失效（`editor_status` 30s 超时），改 HTTP 直连（`AgentScripts/Artifacts/pipeline.py`，token 每轮现读）；CLI 失效根因未深究，登记备查 |

## 续：回带修复与真机复验（用户报"剩余备弹没生效"）

**根因（实锤，非猜测）**：用户报"没生效"后现场诊断——Play 中 HUD 实例的 `_reserveAmmoText = NULL`、节点名仍是 `Txt_MaxAmmo`（旧形态）⇒ 跑的是旧内容。核对磁盘：prefab 在 **00:40 被回带覆盖**（节点名退回 `Txt_MaxAmmo`、`_reserveAmmoText: {fileID: 0}`），即**用户 Prefab Stage 用其旧内存副本保存**，把本批 00:29 的改名＋接线覆盖掉（`Txt_Slash` 因在更早一次写入中已落 Stage 而幸免）——**与 [HUD子件与容器排布](HUD子件与容器排布.md) 记的并发编辑事故同型**。

| 处置 | 落点 |
| --- | --- |
| 重新落定 | `WireHudReserveAmmo.Run()` 再跑（无 Stage 打开时走盘面路径）：改名 `Txt_MaxAmmo`→`Txt_ReserveAmmo` ＋ `_reserveAmmoText` 重接；**写前快照**（`Artifacts/hud_pre_reserve_wire.prefab`）＋ 写后 `Verify()`（19 标量非空 · 9 数组 null=0 · 旧名/旧字段零残留 · 节点 102 · raycast 不变量未破）；与快照 diff **仅 2 行** |
| 真机复验（真实输入注入） | `AgentScripts/BattleLiveProbe.cs`：Input System 设备态**逐帧重压**注入（单次注入会被 OS 状态下一帧覆盖；按下沿还需按下/松开交替才会被游戏侧采样到）。实测：`30 / 90` →连发打空→ `0 / 90` →自动换弹（sim"打空自动换弹"）→ **`30 / 60`**（备弹递减 ✓）；中段截图 `Artifacts/hud_live_fire.png`（枪口火光在场、右下角读出「23 / 0」） |
| 输入注入口径留痕 | 编辑器内注入：电平型（`IsPressed`，开火）单次注入即可；**沿型（`WasPressedThisFrame`，换弹）单次注入必被采样错帧错过**——用按下/松开交替泵；管道自带 `simulate_key`/`simulate_pointer` 在编辑器同样受"沿只存活一帧"限制（dev Player 的 Runtime 侧不受此限） |
| 登记 | 审计表新登记（并发回带第 2 次 + 工具面 CLI 失效），见《代码质量审计-客户端》"新登记"节 |

**教训（建议进纪律）**：外部工具写 prefab 后，若用户 Stage 仍开着旧副本，**下一次 Stage 保存即回带**；本轮处置＝写后回读＋对账报告，**根治候选**（写前探测 Stage 并强制以 Stage 内容为基准写入／写后把盘面状态推回 Stage）留待裁决。
