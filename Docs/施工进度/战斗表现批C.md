# 战斗表现批C（腰射片段 + 准心散布张开 / 全局击杀提示 / ping·丢包）

《游戏业务系统总设计》§5（散布）、[联机战斗演示专项设计](../设计文档/gameplay/联机战斗演示专项设计.md) §3（HUD 清单）、[战斗HUD层级设计](../设计文档/client/ui/战斗HUD层级设计.md) §0/§2/§3/§5（HUD 面）、[角色状态与动作专项设计](../设计文档/gameplay/角色状态与动作专项设计.md) §7（瞄准/散布行）跟进批（2026-10-15，用户裁决"开批"，按 1→2→3 序执行）。

> 本记录只保留最新进度与证据；设计裁决在各自设计文档，本文件不裁决设计。

## 状态

**已完成（附验证证据）**——2026-10-15。

## 交付

原按用户裁决顺序 1→2→3 三件；**子批 1A（腰射站姿族）已按用户裁决撤回（2026-10-15）**，现留 1（准心静态散布张开）/ 2 / 3：

| 子批 | 面 | 落点 |
| --- | --- | --- |
| 1 · 准心静态散布张开 | 准心视觉（表现面，不改判定） | `BattleCrosshairDriver` 增 `MinSpreadScale=1`/`MaxSpreadScale=1.6`/`SpreadFullDegrees=5` + `SpreadScaleOf`——按**有效散布角**（瞄准 = 表值、腰射 × `CombatConfig.HipSpreadFactor`）线性钳制，脏位写 `_reticle.localScale`；`SimView.TryGetSpreadDegrees(slot)`（经 `WeaponSystem.IsEquipped` 取当前装备武器行） |
| ~~1A · 腰射站姿族~~（**已撤回，2026-10-15 用户裁决**） | 动画 Profile 语义 + 开火族分族 | ~~`CharacterAnimationIds` 增 `Combat.HipIdle`/`Combat.HipMoveBlend`；表源两行；`CombatLeafStages` 按 `IsAiming` 分族；`LocomotionRootStage.HipWalkForm`；驱动构造强校验 + `TryGetMotion` 3 权重分支~~ —— **全链回退**（代码/表源/生成物/测试/文档），开火族恢复为统一持 `AimIdle` 循环；撤回缘由见下"裁决与边界" |
| 2 · 全局击杀提示 | **新增公共击杀面** + HUD 面 | proto 加 `message KillRecord{killer,victim,frame,weapon_slot}` + `MatchStateDelta.recent_kills = 11`（只增字段号）→ `gen_proto.py`；**房间内核** `RoomRuntime.Kills`（`KillRecordWindow` 有界环 16，`StepFrame` 在 `ConsumePendingFire` 后捕 `Death` 帧事件＝击杀者/死者/帧/武器槽——**零 Sim 改动、Runtime 不见 proto（R11）**）；**App 层** `KillFeedPacker` 灌 wire，`SnapshotPipeline` 构造注入窗口（`BroadcastIfDue` 在 `BeginFrame` 后填 `_differ.MatchDelta.RecentKills`）、`ReconnectResponder` 同灌（重连快照同携带）；**本地服** `LocalServerTransport` 同注；**客户端** `PublicKillFeed`（`(killer,victim,frame)` 去重环 + 首份/重连标已见不重播）经 `BattleContext.OnSnapshot` 喂入（重连 `Reset`）；**HUD** `Cot_KillFeed`（4 行**全预置**、**多条堆叠**、行满留队、驻留 4s）+ `BattleKillFeedDriver`（行格式 `击杀者 [武器图标] 死者`＝`玩家1 ⚔ 玩家2`；本地 = "你"；`killer==0` → `玩家{v} 阵亡` 无图标）+ `BattleHudView` 行四组数组与 `WeaponIcons`（下标 = 装备槽）；`BattleViewRig.AttachKillFeed` 接线 |
| 2 · 资源 | 第三方复制件 | `Assets/ShareResource/UI/Icons/Weapons/` 补入霰弹图标复制件 `ICON_SM_Wep_Shotgun_01_Military.png`（复制进收集目录、不带 `.meta`、新 GUID、gitignore 覆盖）；**复核确认 `Icon_Weapon` 早已是步枪复制件**（`ICON_SM_Wep_Preset_B_Rifle_03_Military_Clean`，同目录、`0` Synty 直引残留）——本批无需再改指 |
| 3 · ping/丢包 | 应用层 Heartbeat 探针（用户裁决三选一取最重） | `PacketType.Heartbeat = 16`（**双向同类型；不复用退役号 7**）+ `PacketCodec` 解析 case；`RoomClient` 1Hz Unreliable 探针（`client_time` = 发送时刻单调毫秒兼探针 id；仅 `Connected`/`Restoring` 发；滚动窗口 8 条未回声占比算丢包；回声配对算 RTT；相位离开即重置）+ `NetworkQuality{RttMs,LossPermille,Connected}`；`ServerHost.HandleHeartbeat` / `LocalServerTransport` **原样回显**；`BattleClient`/`BattleContext` 透传；HUD `Cot_NetStats`/`Txt_NetStats`（`48ms / 0.3%`）+ `BattleNetworkHudDriver`（离线/无样本整组隐藏、值变才写）+ `BattleViewRig.AttachNetworkHud` |
| 设计回写 | 文档 | 《战斗HUD层级设计》§0/§2/§3/§5（击杀提示面 + 网络质量面 + `Icon_Weapon` 复制件名）；《联机战斗演示专项设计》§3（HUD 行）；《角色状态与动作专项设计》§7（准心行 + 散布行——**动画行随 1A 撤回保持原口径**）；《游戏业务系统总设计》§5（散布与击发口径 + §10 HUD 行）；`battle.proto` 注释（Heartbeat 口径）；**1A 撤回面的文档同步**（动画行、本记录、索引行、审计登记） |
| 施工器 | AgentScripts（不入库） | `BuildHudKillFeed.cs`、`BuildHudNetStats.cs`（幂等构建器：新 Graphic 全 `raycastTarget=false`，行/件缺省非激活） |

## 验证证据

| 项 | 结果 |
| --- | --- |
| 重编译 | **0 错误** |
| 定向 xunit（LiteNet.Tests） | `KillFeedTests` **3/3**（窗口环覆盖序 / wire 映射幂等 / **房间集成**：死亡 → 窗口记 killer·victim·frame·武器槽 → 快照携带）；`RoomClientProbeTests` **4/4**（1Hz 节流 + 回声配对算 RTT / 滚动窗口算丢包 333‰ / 相位离开重置 / 离线不发） |
| 定向 EditMode | `BattleKillFeedEditModeTests` **6/6**（首份不重播·去重·Reset / 三种行格式 + 按槽取图 / 驻留整组回收 / 行满留队不覆盖 / 不在册击杀者按无归属 / Dispose 归位）；`BattleNetworkHudEditModeTests` **2/2**（离线与无样本隐藏·千分比→百分比 / 无源空转） |
| 结构守卫 | `BattleHudMenuEditModeTests` **8/8**（新例：击杀提示面 4 行预置·引用件齐备·缺省非激活·零新射线；网络质量件在位·引用已接·缺省非激活）——"全树恰 1 个 `raycastTarget=true`＝菜单钮"不变量对新件仍成立 |
| 全量 EditMode | **570/570**（全绿；本批新增 `BattleKillFeedEditModeTests` 6 例 · `BattleNetworkHudEditModeTests` 2 例 · 结构守卫 `BattleHudMenuEditModeTests` 2 例；1A 撤回后 `CharacterLocomotionEditModeTests`/`ClipManifestDriverEditModeTests` 回退并全绿） |
| PlayMode | **[PASS] 40/40**（本批跑） |
| L1 门禁 | `scripts/gate/test.ps1 -Lane L1 -Profile PullRequest` → **[PASS] 1684**（LiteFramework 715 · LiteNet 454 · LiteSim 363 · LiteTesting 7 · MetaServer 145；0 失败） |
| BuildHash | ★ 协议源集改动（`battle.proto`/`Generated/Battle.cs`/`PacketType.cs`/`PacketCodec.cs`/`SnapshotDiffer.cs`）→ 重跑 `gen-build-hash.py`：共享 **`1e261db1cdab8b12`**（100 文件）；`--check-shared` **CHECK OK**；Meta 常量 `6376b2cf72b87d09` 未动（Meta 源集未触） |
| 生成物 | `python scripts/codegen/gen_proto.py --protoc …`（KillRecord/`recent_kills`）；`RoomServer` Release 生成成功（15 警告，均为既有）；`Luban/gen_luban.py` 在 1A 撤回后复跑——产物（`tbanimationprofile.bytes`/`.lua`、`animationprofile.cs`）与 HEAD **零 diff**（链路幂等实证） |
| 资源复核 | `Hud_Battle.prefab` GameObject **69 → 88**（+1 击杀提示容器 +16 行件 +1 网络质量容器 +1 文本 = **恰 19，零节点丢失**；文件全文重排为 Unity 重序列化所致）；`0` Synty 直引残留在 prefab |

## 裁决与边界（如实）

- **击杀提示口径 = 全局**（用户裁决）：走**公共击杀面**（房间级权威记录，快照携带），**不是**本地即时路——含他人击杀；本地造成的击杀同样经此路（权威记录含本人）。
- **行格式** <c>击杀者 [武器图标] 死者</c>（用户点名"显示玩家1用了什么武器击杀玩家2"）：动词省略，武器图标代其位；**多条堆叠保持不变**（不互相覆盖）。
- **武器图标按"装备槽"取图**（下标 = 装备槽 `0..WeaponSlotsPerEntity-1`；数据面单源 `tbweapon.slot/icon`）——wire 只带槽位，故不按 def id 索引（施工摘要原设想"索引=def id"改为按槽，与 wire 形态一致）。
- **RTT/丢包为表现·诊断面**：不参与任何判定/准入（哨兵、握手、重连裁决一律不读它）；探针**不承担保活**（保活仍由 kcp2k 内建 Ping/Timeout）。
- **只做静态散布张开**（用户裁决"不做连发临时张开"）：`Sim` 散布口径零改动；`SpreadFullDegrees=5` 为手测定档常量。
- **腰射站姿族已撤回**（用户裁决，2026-10-15）：子批 1A（`Combat.HipIdle`/`Combat.HipMoveBlend` + 开火族按瞄准分族 + 驱动构造强校验）**全链回退**——`git checkout HEAD` 净退 9 文件（`CharacterAnimationIds`/`CombatLeafStages`/`LocomotionRootStage`/`CharacterLocomotionDriver`/`CharacterLocomotionEditModeTests`/`ClipManifestDriverEditModeTests` + 表源 `#animationprofile.xlsx` 与生成物 `tbanimationprofile.bytes`/`.lua`），并跑 `gen_luban.py` 复证**生成物与 HEAD 零 diff**；改后**开火族恢复为统一持 `AimIdle` 循环**（腰射/瞄准在动画面不再分族）。设计文档（《角色状态与动作专项设计》§7 动画行）同步回原口径——**1（准心散布）/ 2（击杀提示）/ 3（ping·丢包）保留**。
- **AgentScripts 构建器不入库**（本仓惯例：一次性施工脚本）；`Assets/ShareResource/UI/Icons/Weapons/` 复制件不入库（第三方付费复制件，随打包机本地还原；`.gitignore` 已覆盖）。

## 遗留 / 挂账

- **Team 模式击杀提示过滤**：`KillRecord` 只带实体 Id，无队伍面——Team 落地时须按队伍过滤（[客户端审计](../代码质量审计-客户端.md) 新登记节）。
- **击杀窗口容量不可配**（默认 16，`RoomConfig` 无字段）；有界、随每份快照全量重发（带宽恒定小代价）——[客户端审计](../代码质量审计-客户端.md) / [服务端审计](../代码质量审计-服务端审计.md) 双登记。
- **准心散布张开幅度未实机手测**（归手测/PlayMode 观感复核；若需按武器区分则表化——随数值调参批）。
- **连发临时张开 / 后坐 / 准心手感表化**已由准心调参批（10-15，`ba49628e`）落地（`tbcrosshair` 表现域表 + GM「准心调参」滑杆定档面）；**护盾 / 技能 CD** 仍待后续批（技能 CD 待技能系统）。
- **后批修正（顺带，命中反馈批D，2026-10-15）**：同帧存在待回溯射手时，其回溯会清事件缓冲——击杀窗口捕获点已改"回溯前＋每位射手结算后"（防前向死亡漏记），反向验证咬住；见[命中反馈批D](命中反馈批D.md)。
- **真名字（G3 Profile）· Team 比分面 · >8 人裁剪**（排行榜批既有挂账，本批未动）。
- **`Icon_Weapon` 已为复制件**：本批复核确认（`0` Synty 直引残留），用户"顺手改指复制件"一项**无需动作**——按事实记录。
