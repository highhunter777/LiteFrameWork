# 命中反馈批D（ObstacleHit 着墙 / 公共开火信号 / 远端开火火光）

《命中反馈与伤害数字专项设计》批D（2026-10-15，用户点单"执行命中反馈批 D ObstacleHit；远端开火火光"）：着墙反馈与远端开火火光是同一"命中反馈扩展"面的两件——后者要求先落**公共开火信号**（原登记归属 CombatEvent 批；本轮按 `recent_kills` 同款"快照公共窗口"先例落 Fire 面，完整可靠 CombatEvent 层仍留 P1）。

> 本记录只保留最新进度与证据；设计裁决在各自设计文档，本文件不裁决设计。

## 状态

**已完成（附验证证据）**——2026-10-15。

## 交付

| 子批 | 面 | 落点 |
| --- | --- | --- |
| 1 · ObstacleHit 产出 | Sim（共享源集） | `FrameEventKind.ObstacleHit=6`（只增枚举）；`ShootingSystem` 弹丸环"障碍截停"分支写事件（主体=开火者、`Pos`=弹道与障碍交点——`obstacleT` 已折三维参数直乘）；**纯脱靶不产**（区别于"被墙截停"） |
| 1 · 着墙消费 | 客户端（帧事件消费者） | `HitFeedbackDispatcher` 角色分派补 `ObstacleHit`（主体=开火者，同 Fire——不补会按"我被击中"口径分派）；`BattleImpactFxDriver` 增着墙档（本地 `Caused` → 障碍命中点 `PlayAt fx_impact_wall`，计数器 `WallImpactCount`）；`fx_impact_wall.prefab`＝KriptoFX `ConcreteImpact` 复制件（`BuildCombatFxAssets` 构建器扩一行，幂等；依赖留原包、新 GUID、FX 组自动入包） |
| 2 · 公共开火信号（CombatEvent·Fire 面） | 协议＋房间内核＋App | `battle.proto`：`message FireRecord{shooter,frame}`＋`MatchStateDelta.recent_fires=12`（只增字段号）→ `gen_proto` 重生成；房间内核 `FireRecordWindow`（有界环 16、纯数据）+`RoomRuntime.Fires`+`CaptureFires`（**回溯判定之前**捕获——`ConsumePendingFire` 会清事件缓冲）；App 层 `FireSignalPacker` 灌 wire；`SnapshotPipeline`（含本地服注入）与 `ReconnectResponder` 两路同灌（随每份快照全量＋重连快照携带） |
| 3 · 远端开火火光 | 客户端（快照公共面） | `PublicFireFeed`（去重键 `(shooter,frame)`、首份/重连不重播；本地开火不消费——防双闪）；`BattleContext.FireFeed` 喂入＋重连 `Reset`；`BattleRemoteMuzzleFlashDriver`（待播队列 → 远端槽位 → `WeaponMounts` 枪口挂点 `Play fx_muzzle_flash`，缩放取本地同源常量 `BattleMuzzleFlashDriver.EffectScale`；视图缺位/灰盒无挂点/实体离场静默退化）；`BattleViewRig` 接线（attach/tick/dispose） |
| 顺带修正 | 房间内核 | **同帧待回溯射手清事件缓冲致击杀窗口漏记**：`LagCompensator.CompensateFire` 的 `ClearFireBuffers` 会抹掉前向步进事件——原"环末单点 `CaptureKills`"漏记前向死亡（同帧多射手还会互相清）。捕获点改"回溯前一次＋每位射手回溯结算后各一次" |

## 验证证据

| 项 | 结果 |
| --- | --- |
| L1 门禁 | `scripts/gate/test.ps1 -Lane L1 -Profile PullRequest` → **[PASS] 1690**（0 失败；含新用例：Sim 着墙/纯脱靶、`FireSignalTests` 窗口/映射/**双射手**集成、`KillFeedTests` 同帧回溯边缘） |
| 反向验证（捕获点修正） | 暂退旧形态跑 `KillFeedTests.DeathIsRecordedEvenWhenBackfillRunsSameFrame` → **1 失败（Expected 1/Actual 0）** ⇒ 用例咬住缺陷；恢复修正后全绿 |
| Unity 重编译 | **0 错误**（双判据：`up_to_date` 状态稳定 ＋ `errors` 空） |
| BuildHash | ★ 共享源集＋协议改动 → `gen-build-hash.py`：共享 **`d7c76f39ce32e91c`（102 文件）**；`--check` 双 OK；Meta `6376b2cf72b87d09` 未动 |
| RoomServer 宿主 | `dotnet build Server/RoomServer/RoomServer.csproj` → 0 错 0 警 |
| 资源归置 | `ShareResource/FX/fx_impact_wall.prefab`（ConcreteImpact 复制件）已生成（pipeline `run_script`；其余两件幂等跳过） |
| EditMode | 新增 `BattleRemoteFireEditModeTests`（开火面去重/不重播/Reset＋远端火光驱动过滤/挂点/退化/Dispose）与 `BattleCombatFxEditModeTests` 着墙两例——**留夜间流水线（白天不跑 L2）**，如实登记 |

## 裁决与边界（如实）

- **公共开火信号形态**＝`recent_kills` 同款"快照公共窗口"（有界 16、随每份快照全量＋重连携带、客户端按 `(shooter,frame)` 去重）——非完整 `CombatEvent` 层（帧＋eventSeq 可靠流仍留 P1；本轮只落 Fire 面，Hit/Cast/Pickup 未动）。
- **窗口容量 16**：30Hz 快照下覆盖最密火力约 0.2s，对"掉几拍快照仍补闪"够用；火光为表现增益（可容忍丢），与击杀"必达"语义不同，同容量足够。
- **本地不消费公共开火面**：本地开火走本地帧事件即时播（预测即时性）；公共面晚约一拍快照到达，若不滤本地会双闪。
- **着墙单一件**：`ConcreteImpact` 作墙件默认（灰盒/建筑通吃）；**表面分流**（Brick/Metal/…）需地图表面标记（KriptoFX `MaterialType` 等价物）；**贴花/尘埃朝向贴合表面**未接（事件无法线——KriptoFX 件按根朝向约定，其 `FPSFireManager` 以 `LookAt(point+normal)` 对齐；当前按 identity 落位）——两项挂账。
- **着墙不产伤害/命中**：判定语义不变（子弹不穿墙）；同一扣扳机：`Fire` 一条＋被截停弹丸各一条 `ObstacleHit`（霰弹 8 弹丸着墙 = 8 条；帧事件缓冲容量 256 足够）。
- **音效**：着墙音效无素材（`Sound/Impact` 仅测试铃）——挂账。
- **顺带修正属击杀面**：触发条件＝同帧存在待回溯开火（实战多射手常态）；修正对"单射手/无回溯"路径零行为变化（L1 全量回归实证）。

## 遗留 / 挂账

- **表面分流**（地图表面标记）· **贴花朝向对齐** · **着墙音效素材**——[客户端审计](../代码质量审计-客户端.md) 新登记节。
- **开火窗口容量不可配**（同击杀窗口同款；随房型配置批合并处理）——[客户端审计](../代码质量审计-客户端.md) / [服务端审计](../代码质量审计-服务端审计.md) 双登记。
- **实机手测**：着墙观感（贴花/尘埃朝向）、远端火光（双人/云测可见）、四人+ 混战火光密度——归手测/PlayMode。
- **完整 CombatEvent 层**（Hit/Cast/Pickup＋eventSeq 可靠流）仍留 P1 口径不变。

## 混批实况（文件归属）

- 本批与「战斗表现批C」余量**同文件载重**（`battle.proto`/`SnapshotDiffer`/`SnapshotPipeline`/`RoomRuntime`/`LocalServerTransport`/`ServerHost`/`ReconnectResponder` 等文件含批C 未提交改动）——提交须整体或按行剥分，由用户裁决；批C 记录与本记录互引。
