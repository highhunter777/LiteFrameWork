# 方案：第三人称越肩射击改造

> 运行 tps-shoulder-camera · 创建于 2026-10-20

## 背景与目标

- 现状：游戏为"固定斜视角射击"——CM2 vcam pitch/yaw 钉死 45–50°、只跟随本地玩家位置；鼠标不驱动相机、准心跟随鼠标屏幕点；瞄准链为 **AimPoint 单口径**（`屏幕射线∩世界 → P → Yaw → 枪口 → 弹道 → 双柱判定`，相机无关、单向无环）。视角裁决见《固定斜视角射击方案专项设计》V1–V4（2026-10-06）与团队相机镜像（2026-10-20，Stage 0+1 已入库）。
- 要解决的问题：用户指令将游戏转为**第三人称越肩射击（TPS）**——鼠标增量驱动相机 yaw/pitch、越肩构图、指针锁定屏心准心；固定斜视角形态整体退役。
- 目标（可观察）：
  1. 对局内鼠标增量实时旋转相机（yaw 自由、pitch 钳制），构图越肩（肩偏移），贴墙/障碍收臂不穿模；
  2. 准心恒钉屏幕中心，AimPoint 由屏心射线解算；**任何合法开火帧都有有效点**（含瞄天/超程——射程截断 fallback，消灭 InputGate 超程/无点丢帧）；
  3. ADS（右键）= 肩位收紧 + FOV 收窄 + 灵敏度缩放，`AimHoldGate` 滞回保留；
  4. 固定斜视角与团队相机镜像全链退役；
  5. **Sim/协议/快照/服务端/BuildHash 零改动**（`gen-build-hash.py --check-shared` 不变为门禁）。

## 需求澄清与假设

- 需要确认的问题：无（关键项均已裁决）。
- 已确认答复（2026-10-20 用户裁决）：
  1. 瞄准交互 = **指针锁定 + 屏心准心**（TPS 标准形态）；
  2. 固定斜视角**完全退役**（不保留双模式）；
  3. 俯仰瞄准姿态（aim pitch）= **程序化先行**（动画后置写先例 `WeaponGripConstraint`），素材批后置挂账；
  4. 立项形态 = **staged-delivery 全门禁**。
- 采用假设：
  - 场景 rig 双机（Main/Aim）euler/offset 序列化值以磁盘真身为准（VFS 索引时滞未读到场景细节，施工首步核证）；
  - 团队镜像实现面按施工记录 ch12 落点（`SetViewMirrored` 端口 + 适配器 FollowOffset 取反 + `TestModeOptions.Id.ViewMirrored` 预览开关），施工以代码真身核对退役面；
  - CM2（2.10.7）足以表达越肩 rig（Transposer/Composer/Collider），不升级 CM3。
- 假设影响与风险：场景序列化值或镜像落点若与记载不一致，退役面按 grep 真身扩散，不影响方案主干。

## 范围与非目标

- 范围内：
  - `ICameraService` 端口扩展（视角增量/钳制/构图档位）+ `CinemachineCameraService` 适配（yaw/pitch 自持状态、越肩偏移、收臂）；
  - 场景 rig 改造（训练场/对局场景，**走 unity-pipeline**）；
  - 指针锁定管理（对局锁定+隐藏、上下文门被拦/死亡/退场解锁）——收编 `BattleCrosshairDriver` 既有 `Cursor.visible` 钩子为唯一写者；
  - 采集侧：屏心射线 + 射程截断 fallback（`NewInputIntentSource`，LiteClient 程序集、零 BuildHash）；
  - 准心钉屏心（`BattleCrosshairDriver` 位置源切换与显隐门复审）；
  - ADS 两档构图插值 + 灵敏度缩放；
  - 移动相机平面旋转公式启用（既有公式，非恒等路径）；
  - aim pitch 程序化姿态（上半身后置写）；
  - 团队镜像退役（端口/适配/预览开关/过期注释）；
  - 视野 A1 复验（渐隐/影区/自身渐隐）+ `CinemachineCollider` 否决条复审；
  - 文档收口：新专项设计取代旧专项（旧文归档）+ 8 份文档视角引用同步（设计 README 索引/战斗HUD/视野遮挡/角色状态/地图数据/烘焙工具箱/联机战斗演示/业务总设计）+ 实机矩阵重写。
- 非目标：
  - Sim/协议/快照/checksum/服务端任何改动、BuildHash 重录；
  - 第一人称切换、掩体吸附、蹲伏（B 档按视野专项既有排期）；
  - 相机后坐 kick（挂账，先沿用准心 kick）；
  - aim up/down 动画素材采购/生成（后置批）；
  - 团队模式基线（原 Stage 2，本就未开始）。

## 现状与约束

- 相关代码/模块（2026-10-20 清点证据）：
  - 相机端口：`Assets/LiteView/Camera/ICameraService.cs`（`HasFocus/Focus/Follow(target,facing,dt)/SetAiming/SetAimPoint/Reset/Shutdown`；构图归场景 vcam 的文档口径）；
  - 相机适配：`Assets/LiteClient/Adapters/Platform.Unity/CinemachineCameraService.cs`（`CinemachineVirtualCamera` 主/Aim 双机 + `CinemachineComposer`（`TrackedObjectOffset.z`，AimOffsetCurve）+ 优先级抬升 `AimPriorityBoost`；主相机=最高优先级 vcam、按名排除 `"Aim Camera"`）；
  - 采集：`NewInputIntentSource`（鼠标点射线 → `SimRaycast.RaycastEntities`(≤HitscanRange) → 地面 y=0 兜底 → 无点=沿用上次；**移动按相机 yaw 平面旋转公式在 `Sample`**；`MouseScreenPosition` 供准心）；
  - 输入资产：`Player.inputactions` GamePlay map **已有 `Look` action（`<Mouse>/delta`，当前零消费）**、Fire/Aim/Move/Dash/Reload；InputSystem 1.11.2；Cinemachine 2.10.7；
  - 准心：`BattleCrosshairDriver`（位置=注入鼠标点、`Cursor.visible` 管理钩子、Hip/Ads 双形态、tbcrosshair 散布/后坐表不动）；
  - 激光：`BattleLaserDriver`（不读相机 Transform，只吃 AimPoint——相机无关，预期零改）；
  - 视野：`BattleVisionBlockerDriver`/`VisionSegment`（按本地渲染相机实时几何——相机无关设计，逻辑零动只复验）；
  - 动画：`AnimationPlayer` 三通道能力位构造、`PlayBlend`/`UpdateBlendWeights` 多向权重（`BuildAimWeights(MoveRelAngleDeg)` 四向通路确证——战斗态 strafe 素材已在用）；`SimView.UpdateCamera` 每帧只喂位置+朝向；
  - 服务端准入：`RoomServer/Runtime/InputGate.cs`（AimPoint 距离闸 `HitscanRange×1.1` 超程**整帧丢弃**含 Move；开火帧无点=非法）。
- 约束：
  - Sim/协议零改动硬门禁（共享源集 `Assets/LiteSim/Core/{Scripts,Systems}`、`Assets/LiteNet/...` 禁触）；
  - Unity 序列化资产（场景 rig）只走 unity-pipeline；
  - 相机语义收口在 `ICameraService` 端口（`SetOrbitInputEnabled` 死链教训：**不回装 `CinemachineInputProvider`**）；
  - 机制常量须有推导注释；灵敏度/肩位/pitch 限位数值走场景 Inspector + GM 调参面（沿既有裁决与先例）；
  - 本机 shell 通道损坏：命令门禁经 exec_editor_script + Process 直起；
  - 对局实机验证须在 180s 僵尸局窗口内完成（存量缺陷登记在案）。

## 候选方案与取舍

| 方案 | 核心思路 | 优点 | 代价/风险 | 结论 |
|---|---|---|---|---|
| A 扩展 Cinemachine 服务 | CM2 rig 改 3rd-person 形态（Transposer 肩偏移 + `CinemachineCollider` 收臂）；yaw/pitch 状态自持在适配器、由新端口喂增量；不回装 InputProvider | 复用既有适配/场景通道/Inspector 调参裁决；阻尼/混合等手感件现成 | CM2 组件语义半黑盒 | **采纳** |
| B 自研弹簧臂 | 纯逻辑组件（跟随+肩偏+收臂射线+ADS 插值），战斗场景退 Cinemachine | 机制全显式、L1 可测、零黑盒 | 手感从零调；弃既有适配投入 | 否决 |
| C 最小解锁 | 只开旋转、不动构图/收臂 | 一天级 | 不满足"越肩"本质（无肩位/穿模） | 否决（对照） |

推荐 A：与既有"端口收口语义 + 场景配构图"架构同构，改造面集中在适配器与场景 rig；若 CM2 手感不达预期，可退 B 而端口面不变。

## 设计要点

- 接口/数据：
  - `ICameraService` 新增视角面：`AddLookDelta(yawΔ, pitchΔ)`（弧度；**纯视图侧，不进意图三件/不进协议**）+ pitch 钳制 + 构图档位插值（Hip↔Ads：肩偏移、FOV、灵敏度缩放）——成员形态施工时按单一职责定稿；`Follow(facing)` 的语义随 TPS 复审（构图不再按角色朝向）；
  - 指针锁定：LiteClient 平台适配层新增管理件（锁定状态机；UI 门被拦/死亡/退场解锁），收编 `BattleCrosshairDriver._setCursorVisible` 为唯一光标写者；
  - 采集 fallback：`TryResolveAimPoint` 增第三档**射程截断点**——实体交点 > 地面交点（超程截断）> 射线方向射程截断点；截断距离按 `|P−射手| ≤ HitscanRange×1.1`（InputGate 闸值）反解并带推导注释；
  - ADS：`SetAiming` 升级为构图档位插值驱动（保留 `AimHoldGate`）；`SetAimPoint`/AimOffsetCurve 构图链复审；
  - 团队镜像退役：删 `SetViewMirrored` 端口、适配器镜像、`TestModeOptions.Id.ViewMirrored` 预览开关（审计 Stage 3 摘除挂账一并清偿）。
- 关键流程（改造后）：鼠标增量（指针锁定）→ `AddLookDelta` → 相机 yaw/pitch（钳制）→ vcam 越肩位姿（+收臂）→ **屏心射线** → 实体/地面/射程截断 → AimPoint 上行（口径不变）→ Yaw 派生/枪口/弹道/判定原样；准心=屏心。
- 失败与边界：
  - 相机未就绪/上下文门被拦：零视角增量、指针解锁（现状语义保持）；
  - 射线无交（瞄天）：射程截断点——消灭"无点开火=非法帧"；
  - 收臂贴墙：CM2 Collider 收臂优先、视野渐隐兜底（复验项）；
  - 指针锁定与 ESC 退场链：退场链走 UI 导航（ESC→设置页），锁定态 ESC 语义施工定稿（优先不劫持系统解锁行为）。

## 通用性与扩展性

- 根因/共性问题：视角形态是**相机服务语义**而非游戏特判——固定斜视角把"鼠标不转相机"做成场景现状保证（组件移除），自由视角把"鼠标转相机"做成端口语义（机制化），同源收敛于 `ICameraService`；
- 复用与扩展点：`AddLookDelta` 对任何"自由视角跟随相机"消费者通用（观战/回放/载具）；构图档位插值是通用"构图状态机"，第三档（蹲姿相机等）只加档不加机制；射程截断 fallback 是与视角形态无关的采集侧通用几何规则；
- 避免硬编码/特判：灵敏度/肩位/pitch 限位/FOV 走场景 Inspector + GM 调参页（准心调参先例）；机制常量带推导注释；禁按团队/地图写相机分支。

## 测试策略

- 层级与关键用例：
  - EditMode：相机端口行为（增量累积/pitch 钳制/档位插值/Shutdown 还原/场景重解析）、采集解算（屏心射线、射程截断、无交不再产无点开火帧）、准心屏心与显隐门、指针锁定状态机、镜像退役后零残留；
  - L1（dotnet）：采集面/相机探针既有用例改造回归；**纪律扫描必跑**（改 LiteView/LiteGame 源后）；
  - 实机矩阵（新写，取代旧矩阵 #9/#10）：旋转手感、收臂、ADS 无甩动、准心=弹道同源（激光端点重合）、渐隐/影区复验、指针门（UI 开解锁）、移动手感（非恒等公式）、镜像退役回归、边界（贴墙/瞄天/贴脸）。
- 先失败后通过：EditMode 新用例先行（端口未实现 ⇒ 编译红/断言红），经 TestRunnerApi 定向跑记录红测证据；L1 侧以采集面改造点用例为红测面。

## 验收标准

- [ ] AC1：对局内鼠标增量驱动相机 yaw（360° 自由）/pitch（钳制），构图越肩、贴墙收臂不穿模（实机矩阵）；
- [ ] AC2：准心恒屏心；瞄天/超程开火不再产生被 InputGate 丢弃的无点/超程帧（EditMode 用例 + InputGate `DroppedOutOfRange` 观测归零）；
- [ ] AC3：ADS 两档插值 + 灵敏度缩放 + `AimHoldGate` 保留，进出无甩动（实机）；
- [ ] AC4：固定斜视角与团队镜像全链退役：`SetViewMirrored`/预览开关/过期注释零残留（grep 证据），旧专项归档、新专项落位、8 份文档同步（doc-lint 过）；
- [ ] AC5：Sim/协议零改动：`gen-build-hash.py --check-shared` 不变、L1 全量绿、纪律扫描 0 违规；
- [ ] AC6：视野 A1 自由视角复验通过（渐隐/影区/自身渐隐），`CinemachineCollider` 否决条复审有裁决记录；
- [ ] AC7：aim pitch 程序化姿态落地（俯仰射击上半身随相机 pitch 区间插值），素材批挂账登记。

## 风险与回滚

- 风险：CM2 rig 组件手感不达预期（收臂/阻尼）→ 实机矩阵暴露后可退方案 B 而端口面不变；指针锁定与模态 UI 交互冲突 → 矩阵覆盖；自由视角下渐隐 popping（0.5s 最短保持已有）；采集截断距离与 InputGate 闸值边界（推导复核）；**俯仰越肩下"准心-弹道视差"**（AimPoint 定义保持"所见即所打"，近距肩位视差如实接受并矩阵记录）；
- 监控/发现：EditMode 定向 + L1 全量 + 实机矩阵 + 相机状态诊断直读；联机观测 `InputGate.DroppedOutOfRange` 应归零；
- 回滚：改造集中于适配器/场景 rig/采集三件，单批 revert 即回固定斜视角（裁决历史在 git 与归档文档）；Sim 面零改动 ⇒ 回滚无协议/两端一致性问题。
