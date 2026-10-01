# 快照全量 AOI 缺席判死修复

> 状态：**已完成（附验证证据）**
> 范围：《状态同步专项设计》§5.2/§6.2/§8（AOI 只影响广播不影响判定——红线）；缺陷由 2026-10-01 用户问询触发（"现在 bot 是不是会消失；按 F9 瞬间移动玩家会有什么问题"）
> 证据时点：2026-10-01

## 缺陷（真缺陷，非设计意图）

**机制链**（每一环均有代码事实）：

1. `SnapshotDiffer.BuildFor` 对**全量帧**也做 AOI 过滤（半径 30m、格 10m）——视野外实体不进全量包；
2. `SnapshotReassembler.Apply` 对全量的语义是"**缺席槽位一律判死**"（清整个 AliveBitmap 只回填包内槽位）——该语义以**全图**为前提，与 ① 矛盾；
3. `RollbackSim.OnAuthoritativeSnapshot` 任何一次失配/超前覆盖都把镜像整体 `CopyTo` 进本地预测 Sim；
4. `SimView.SyncViews` 按 `_sim.IsAlive` 回收视图 → **视野外实体被无声杀死**（死亡事件在和解静默门内，无任何死亡表现）。

**触发条件**：实体出视野后 ≤1s 必有一帧全量（`ProtocolConstants.FullEveryFrames=60`）把它在镜像里判死；随后任一次和解采纳即落地。离线 F9 形态的触发源＝开局首包超前覆盖 / 开火换枪（不预测的公共面变化）/ >83ms 卡顿帧（客户端追帧上限 5 < 服务端 8 → 快照超前直接采纳）；真联机下丢包/远端输入即常发 → **视野外实体基本必然消失**。消失后服务端(活)↔客户端(死)每快照失配 → 30Hz 和解风暴 + `SendMismatch` 每帧上报；走回视野内还要等下一帧全量（≤1s）才无声冒回。

## 修复内容

| 文件 | 改动 |
|---|---|
| `Assets/LiteNet/Protocol/SnapshotDiffer.cs` | `BuildFor` 全量分支绕过 AOI——全图活体直发（槽位升序）；AOI 只保留在增量分支；类注释钉死"全量缺席=死只对全图成立" |
| `Assets/RoomServer/Application/SnapshotPipeline.cs` | 背压档 3 `TrimFarthest` 豁免全量帧（缺席判死语义同一条红线）；裁剪只作用于增量帧 |
| `Assets/LiteSim/View/SimView.cs` | `TryInterpolate` 补**远端硬切**：前后快照位置差超 `SnapDistance` 直接对齐新快照（§6.2"远端必要时 snap"落地——消除传送/复活被插值播成 33ms 横穿地图的"飞人"） |

带宽代价：全量 ≤1Hz 且 ≤ 活体数，整帧可付；AOI 的收益全在 30Hz 增量上，不受影响。重连路径（`ServerHost.HandleReconnect` → `SnapshotCodec.PackFull`）本就全图整帧，未受影响、无需改。

## 用例

- `Tests/LiteNet.Tests/SnapshotSourceTests.cs`：`全量帧不裁AOI_首帧与周期全量都带视野外实体_增量仍按可见裁剪`（旧实现即红：全量被裁→缺席判死）、`和解采纳镜像时_AOI外实体不被误杀`（differ→reassembler→RollbackSim 全链——旧实现和解后实体被判死）
- `Tests/LiteNet.Tests/RoomBroadcastTests.cs`：`背压档3_只裁增量帧_全量帧整帧保留`（真管线：档 3 全量整帧 / 增量仍裁半的回归守卫）
- `Assets/Tests/EditMode/SimViewEditModeTests.cs`：`远端插值_前后快照跳变超SnapDistance硬切_不播成飞人`（Unity 侧，随夜间 L2 跑）

## 验证证据

- L1：`dotnet test Tests/LiteNet.Tests/LiteNet.Tests.csproj` → **350/351 通过**（2026-10-01）。唯一失败＝`BuildHash_与当前源码复算一致` 的**既有红**：并行"全链诊断样例"线在改 LiteNet 源集（RoomClient.cs/LiteNet.csproj/ServerHost.cs/Diagnostics/*，工作区未提交），本批 SnapshotDiffer/SimView 亦在源集内——重跑 `python scripts/gen-build-hash.py` 需两线合流后由后收口的批统一执行（同 R2 批"唯一失败＝快照既有 BuildHash 红"处置口径）。另：`JoinDiagChainTests`（并行线 WIP 新用例）一次运行假红、复跑即绿，属其线内问题，本批不动。
- Unity 编译：pipeline `recompile` → `recompile_status` = `{"status":"completed","failed":false,"errors":[]}`（2026-10-01，0 错误）。

## F9（瞬间移动玩家）处置结论

用户问询的第二问按下列口径落档：

1. **已随本批消除**：AOI 连锁消失（瞬移 >~30m 不再把 bot/远端"裁死"——它只是冻结，增量不更新、全量 ≤1s 兜底刷新）；远端"飞人"（远端 snap 落地）。
2. **属设计允许、无需处理**：瞬移自造一次和解+硬切（`SnapDistance=3m`，§6.2 复活/传送允许硬切）；本地预测在快照到达前于原地多跑 ≤1 个快照间隔。
3. **真要做传送类功能（调试热键/复活/传送技能）时的前置件**，本批不做：输入帧表达不了位置跳变——需新增服务端 `RoomCommand`（权威帧边界写位置，客户端不预测）；落点必须过与出生点同款的"清障"校验（客户端 `CharacterController.Move` 收敛会被墙挡住 → 表现/判定持续分叉）；调试热键按 F9/F10 同款三宏门禁（`UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG`）。

## 遗留登记

- **checksum 全图口径 × AOI 增量裁剪的失配波**：线上 checksum（`SimChecksum.ComputePublicChecksum`，随每份快照）按**全图**算，视野外实体的变化不进增量 → 实体在视野外移动期间客户端每快照失配和解（每帧全量 ≤1s 自愈收敛；当前 1v1 站桩 bot 无感）。根治需把 checksum 比对口径按视点可见集裁剪（两端同源可见性判定，避免视点位置竞态）——**协议/设计级决策，归状态同步专项后续批**，不以表现修补替代。
