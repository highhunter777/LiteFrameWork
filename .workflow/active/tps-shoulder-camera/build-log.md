# 施工记录：第三人称越肩射击改造

> 运行 tps-shoulder-camera · 阶段 5（正式施工）· 2026-10-20

## 施工记录

### T1 相机视角端口与适配器改造（2026-10-20 完成）

- **端口**：`ICameraService` 新增 `AddLookDelta(yawΔ, pitchΔ)`（纯视图侧——不进意图三件/不进协议）、
  诊断读口 `LookYaw`/`LookPitch`；接口唯一实现方 `CinemachineCameraService` + 测试替身
  `SimViewEditModeTests.CameraProbe` 同步补齐。
- **状态层**：`AddLookDelta` 真实现（yaw 2π 环绕、pitch 钳 `PitchClampMinRad/MaxRad`（-50°/+60°，
  机制初档常量带推导注释，调参面后续迁 GM）、`Reset()` 清视角态（复活/传送硬切回默认视角）、
  Shutdown 拒写）。
- **喂入链**：`NewInputIntentSource.LookDelta`（`GamePlay/Look`——鼠标 delta 像素读口，与
  `MouseScreenPosition` 同口径 R12 边界内）→ `BattleViewRig.AttachInputWiring` 挂
  `LookDeltaOf(_input)`（同 `ScreenPositionOf` 注入位模式）→ `Tick` 内喂
  `_camera.AddLookDelta(x·LookSensitivityRadPerPixel, −y·…)`（0.0025 rad/px 机制初档常量带推导注释；
  pitch 取负——屏幕系 y 向下）→ `Dispose` 摘源。
- **vcam 应用面（视角派生，单源方法 `ApplyRigDerived`）**：双机（主 + 瞄准预放置）每拍随
  `Follow` 写入——轨道 yaw 直写 `CinemachineOrbitalTransposer.m_XAxis.Value`（场景无 InputProvider，
  服务是唯一写者）；俯仰按"水平距不变、y 自场景基准抬 `tan(pitch)`"派生 `m_FollowOffset`
  （**场景基准即 pitch=0 平视角**，服务不引入构图常量）；bind 时采集 offset+轨道轴场景基准
  （`CaptureMainFollowOffset`/`BindAimCamera`），解绑/关闭 `RestoreFollowOffsets` 全量还原
  （镜像/AimOffsetCurve 的"派生写入+还原"先例同款）。原 `ApplyViewMirror`/`ApplyFollowOffset`
  旧镜像写入链收编进该单源（镜像语义成为派生复合的一支——T7 退役时删除该支）。
- 2026-10-20T19:49:57+08:00 · 完成任务 T1 · 证据: Unity 重编译 0 错；EditMode 定向 TpsCameraLook 5/5 + CameraViewMirror 2/2 + SimView 20/20；L1 LiteFramework.Core.Tests 722/722（含纪律扫描）；红测基线 10 红已锁（test-plan.md）。收臂/AimC…
- 2026-10-20T19:50:39+08:00 · 开始任务 T2「场景 rig 越肩化改造」
- 2026-10-20T20:02:34+08:00 · 完成任务 T2 · 证据: BattleRig 双机越肩化（写后回读探针全中）；遗留 Mouse X 输入轴清名；HardLookAt→Composer 平视；CinemachineCollider×2；适配器球面俯仰+焦点 identity；TpsBattleRigEditModeTests 3/3 场景守卫；EditMode 30/30 +…
- 2026-10-20T20:04:00+08:00 · 开始任务 T4「采集侧屏心射线与射程截断 fallback」
- 2026-10-20T20:10:46+08:00 · 完成任务 T4 · 证据: TpsAimPointEditModeTests 3/3（R6/R7/R8）；准心+激光+视角合并回归全绿（激光零回归——AimPoint 口径不变仅换射线源）；L1 722/722；编译 0 错
- 2026-10-20T20:11:22+08:00 · 开始任务 T3「指针锁定管理件」
- 2026-10-20T20:15:59+08:00 · 完成任务 T3 · 证据: TpsPointerLockEditModeTests 2/2 转绿；准心 9/9 回归（光标钩子收编 no-op 默认）；BattleViewRig Tick 驱动（对局控制中=锁+隐藏；门被拦/未建局/死亡=解锁）+ Dispose 解锁；L1 722/722；编译 0 错
- 2026-10-20T20:16:42+08:00 · 开始任务 T5「准心钉屏心与显隐门复审」
- 2026-10-20T20:28:42+08:00 · 完成任务 T5 · 证据: TpsCrosshairCenterEditModeTests 1/1（R9 转绿）；BattleCrosshairEditModeTests 8/8 回归（位置注入位退役、后坐基准=屏心）；指针锁 2/2 采集 3/3 保持；L1 722/722；编译 0 错
- 2026-10-20T20:29:10+08:00 · 开始任务 T6「ADS 构图档位插值与灵敏度缩放」
- 2026-10-20T20:30:07+08:00 · 完成任务 T6 · 证据: ADS 灵敏度收敛（AdsLookSensitivityScale=0.6 带推导注释，IsLocalAiming 沿应用）；ADS 档位本体=双机优先级+brain FOV/构图混合（T2 场景值 60°/50°+贴肩 2.3m）+AimHoldGate 沿用；AimOffsetCurve 构图链复审=保留（随瞄准…
- 2026-10-20T20:30:29+08:00 · 开始任务 T7「团队相机镜像全链退役」
- 2026-10-20T20:31:20+08:00 · 开始任务 T8「aim pitch 程序化俯仰姿态」
- 2026-10-20T20:36:35+08:00 · 完成任务 T7 · 证据: SetViewMirrored/ViewMirrored 全仓 grep 零命中（rg 排除 meta）；接口成员/适配器方法与字段/Mirror180/复合支/TestModeOptions-Id+Info+switch×2/TestModeRuntime 字段/BVR 预览喂值与退场还原/CameraViewMi…
- 2026-10-20T20:43:15+08:00 · 完成任务 T8 · 证据: AimPitchPosture.UpperBodyPitch 恒等钳制 + AimPitchPostureDriver（人形 Chest 映射、世界系前乘绕角色右轴、Evaluate 后置写/退出即停写）+ CharacterLocomotionDriver 注入位 cameraPitchOf（BattleViewR…

## 关键决定

- **视角语义收口在端口**：`AddLookDelta` 是"语义态进端口、接管方式归实现"的又一例——
  不回装 `CinemachineInputProvider`（`SetOrbitInputEnabled` 死链教训：组件直连会把相机语义散出服务端口）。
- **场景基准 = pitch=0 平视角**：适配器对场景 offset 只做几何派生（tan 抬升）不引入数值常量，
  越肩距离/肩偏/高度全部归场景 Inspector（T2 落值）——维持"构图归场景单源"红线。
- **pitch 限位/灵敏度落机制常量**：常量带推导注释单源（`PitchClampMin/MaxRad`、
  `LookSensitivityRadPerPixel`），T6/GM 调参面以常量为默认基线。

## 偏差与处理

- **T1 验收内"收臂（CinemachineCollider）"移 T2**：收臂是 vcam 管线组件（场景件），与场景
  rig 值同批落才可联调（TaskReview F3 同口径）；T1 落的是代码应用面。
- **T1 验收内"Aim Camera 按名解析修债（审计 L42）"移 T2**：修债需动场景/装配链（marker 或
  配置化），单独在 T1 内做会是半方案；T2 场景批内一并处置。
- **镜像共存**：`ApplyRigDerived` 复合了镜像支（`_viewMirrored ? Mirror180(base) : base`）——
  T7 删镜像时该支连同 `SetViewMirrored` 端口一起退役，派生方法成为纯视角。

## 问题与修复

- **`PlayerInputActions.Dispose()` EditMode 报错**（红测期发现）：生成类 `Object.Destroy` 在
  EditMode 非法——已按格式登记审计（技术债·生成器 bug 节），测试侧以共享资产实例规避（源不持
  所有权 ⇒ Dispose 只 Disable）。生成物不可手改，根治挂账生成器批。
- **红测暴露的两个生产行为缺陷已用例锁定**（R7/R8）：瞄天/超程无截断——AimPoint 飞至 61.5km
  （InputGate 距离闸 100×1.1 必丢整帧）——T4 施工修复。

## T1 验证证据

- Unity 重编译 **0 错**（警告全为存量）；
- EditMode 定向：`TpsCameraLookEditModeTests` **5/5**、`CameraViewMirrorEditModeTests` **2/2**
  （派生写入在 pitch=0 下与旧镜像写入位级一致——零回归）、`SimViewEditModeTests` **20/20**；
- L1 `LiteFramework.Core.Tests` **722/722**（含纪律扫描——LiteGame/LiteView/LiteClient 源改动零违规）；
- 红测基线：`total=12 fail=10`（见 test-plan.md 预期失败证据）→ 状态层落位后相机五例转绿，
  余 7 红分别锁定 T3/T4/T5/T8 契约。

### T2 场景 rig 越肩化改造（2026-10-20 完成）

- **场景（BattleRig.unity，双机）**：Player 机 offset `(0.55, 1.7, -3.4)`（右肩/头高/3.4m 距离——
  场景 Inspector 单源）；Aim 机 `(0.55, 1.7, -2.3)`（ADS 贴肩基座，Composer z=1.5 保持
  AimOffsetCurve bind 基准兼容）；双机 `m_XAxis.m_InputAxisName` 清空（**遗留 `Mouse X` 死件退役**
  ——MaxSpeed=0 冻结的 legacy 轮询，视角唯一写者=相机服务）；Player 机 HardLookAt→**Composer**
  （TrackedObjectOffset `(0,1.6,1.2)` 平视、零死区——屏心=瞄准方向）；双机加 **CinemachineCollider**
  （收臂，默认参数+minDist 0.1）。场景值核证＝写后回读探针（日志留证）。
- **适配器几何两笔**：① `Follow` 焦点**恒 identity**（朝向不写自建焦点——TPS 下轨道是视角事实源，
  Composer 构图点世界稳定、不随角色朝向摆动；`facing` 形参留端口语义）；② 俯仰派生改**球面矢量旋转**
  （模长守恒——镜头不随俯仰飞远；θ=0 位级恒等保镜像用例）。
- **修债（审计 L42 部分修复）**：瞄准机解析失败日志 Info→Warning（装配缺口可见化）——修复登记已回写。
- **场景守卫测试**：`TpsBattleRigEditModeTests` 3 例（结构不变量：无输入源/不回正/Composer 平视/
  收臂件在场/近肩距离阈值——数值自由、形态回潮才红）。

### T2 验证证据

- Unity 重编译 **0 错**；EditMode 定向 **30/30**（视角 5 + 场景守卫 3 + 镜像 2 + SimView 20）；
  L1 **722/722**（纪律扫描零违规）。
- **实机待核项（T9/T11 矩阵）**：鼠标右推=视角右转的符号方向、Composer 平视构图观感、
  收臂贴墙行为（含与渐隐的协作）、pitch 限位（-50° 下相机贴地由收臂兜底）。

### T4 采集侧屏心射线与射程截断 fallback（2026-10-20 完成）

- `NewInputIntentSource.Sample`：瞄准射线改**相机屏心**（`ViewportPointToRay(0.5,0.5)`——分辨率
  无关、不再依赖 `Mouse.current`——指针锁定形态下鼠标只驱动视角）。
- `TryResolveAimPoint` 三档：实体交点（原有，相机原点近肩恒在闸内）＞ **闸内地**面交点 ＞
  **射程截断点**（`RangeClampedPoint`：射线与"以射手为心、射程为半径"球面交的远根——四元二次
  解析解，起点近肩恒在球内 ⇒ 恒有正根；退化形回起点如实兜底）。截断距离取
  `CombatConfig.HitscanRange`（服务器闸 1.1 折余量内——**不与服务器闸常量跨程序集单源**，
  推导注释钉口径）。**消灭红测实锤的两类生产丢帧**：瞄天=无点非法帧（61.5km 兜底点）、
  超程=InputGate 整帧丢弃（含 Move）。
- 红测修正一处**测试几何自伤**：R8 原把相机放 200m 高（射线最近点也 200m——永远进不了射程球，
  违反"相机近肩"前提）；改近肩浅俯 0.5°（屏心地面交点 ≈195m 超程——几何前提成立）。

### T4 验证证据

- `TpsAimPointEditModeTests` **3/3**（R6 屏心/R7 瞄天截断/R8 超程截断）；
  合并回归（准心 9 + 激光 21 + 视角 5）全绿；
- L1 **722/722**（纪律扫描零违规）；Unity 重编译 0 错。
- 教训沉淀（已犯一次）：**改测试文件后必须 refresh 再跑 TestRunnerApi**——runner 用的是已编译
  程序集，陈旧程序集会跑出假红（本次 R8 二轮假红的根因）。

### T3 指针锁定管理件（2026-10-20 完成）

- `PointerLockService`（LiteClient 平台件）真实现：`Lock/Unlock` 幂等写 `Cursor.lockState/visible`
  （状态不变不重写平台）；**Cursor 全仓唯一写者**。
- 驱动面（TaskReview F2 落实）：`BattleViewRig.Tick` 尾段按"对局控制中（视图建好 + 本地在场 +
  未被上下文门拦 + 存活〔既有 `IsLocalDead()` 复用〕）= Lock；否则 Unlock"驱动；`Dispose` 解锁
  （离场不遗留锁定态）。
- 准心光标钩子收编：`BattleCrosshairDriver._setCursorVisible` 默认改 no-op（注入位仅供测试断言
  驱动意向）——光标实写单一化到 PointerLockService。
- 验证：`TpsPointerLockEditModeTests` **2/2** 转绿；准心 9/9 回归；采集 3/3 保持；L1 **722/722**。

### T5 准心钉屏心（2026-10-20 完成）

- `BattleCrosshairDriver.Tick` 位置写**画布中心系原点**（全屏居中 Overlay 画布 ⇒ 屏心）+ 后坐上跳
  残余；`screenPosition` 注入位与 `_screenPosition` 字段、`ZeroScreenPosition`、
  `BattleViewRig.ScreenPositionOf`、`NewInputIntentSource.MouseScreenPosition` **全链退役**（零消费者，
  "屏幕光标位"语义终结）；准心/采集测试同步去注入位。
- 验证：`TpsCrosshairCenterEditModeTests` **1/1**（R9 转绿）；`BattleCrosshairEditModeTests` **8/8**
  回归（旧"位置随鼠标"用例删除——钉旧行为，由 R9 取代；后坐基准改屏心）；L1 **722/722**。

### T6 ADS 档位与灵敏度缩放（2026-10-20 完成）

- **档位本体＝双机优先级切换 + Cinemachine brain 的 FOV/构图混合**（T2 场景值：Player 60°FOV/
  3.4m 基座 ↔ Aim 50°FOV/2.3m 贴肩 + Composer TrackedObjectOffset z=1.5 基准），`AimHoldGate`
  滞回沿用——架构原样，未引入代码档位常量。
- 代码增量＝**ADS 灵敏度收敛**：`AdsLookSensitivityScale = 0.6f`（推导注释：FOV 收窄比 50/60≈0.83
  同比收敛再叠手感衰减，同类 TPS 0.5~0.7 中位）在 `BattleViewRig` 喂入处按 `IsLocalAiming()` 应用。
- `SetAimPoint`/`AimOffsetCurve` 构图链**复审＝保留**（ADS 构图随瞄准距离拉近的现行语义在 TPS
  下仍成立——R4 用例锁定视角态跨 ADS 翻转不重置）。
- 验证：R4 绿（既有）；L1 **722/722**；编译 0 错。实机手感对位归 T11 矩阵。

### T7 团队相机镜像全链退役（2026-10-20 完成）

- 删除面（`rg "SetViewMirrored|ViewMirrored"` 全仓**零命中**核证）：`ICameraService.SetViewMirrored`
  成员；适配器 `SetViewMirrored`/`_viewMirrored`/`Mirror180`/派生复合支；`TestModeOptions`
  Id/Info/Get/Set 四处；`TestModeRuntime.ViewMirrored`；`BattleViewRig` 预览喂值与退场还原；
  `CameraViewMirrorEditModeTests`（AssetDatabase 删除含 meta）；`SimViewEditModeTests.CameraProbe`
  替身成员。视角派生基准字段保留（pitch 派生的还原端点，注释改口径）。
- Stage 3 摘除挂账随批清偿（审计修复登记已回写）；镜像用例语义由 `TpsBattleRigEditModeTests`
  结构守卫承接（θ=0 位级恒等性不再显式钉——派生几何仍保该性质）。
- 验证：EditMode 六夹具 **34/34**；L1 **722/722**；编译 0 错。

### T8 俯仰姿态（2026-10-20 完成）

- `AimPitchPosture.UpperBodyPitch` 真实现（区间内恒等、越界钳制——恒等依据：所见即所俯仰）。
- `AimPitchPostureDriver`（LiteView/Animation/Drivers）：**人形 Chest 骨经 `GetBoneTransform` 映射**
  （零路径常量、任意人形 Rig 通用）；**世界系前乘**（骨世界旋转前乘"绕角色右轴的俯仰旋转"——
  Chest 局部轴随 Rig 而异，角色右轴是唯一稳定参照）；写序＝WeaponGripConstraint 同款
  （Evaluate 后、每帧写、退出即停写、无持久状态）；sign：右轴正角=前俯 ⇒ 相机上仰取负（**实机核对项**）。
- 接线：`CharacterLocomotionDriver` 增注入位 `cameraPitchOf`（`Func<float>`——BattleViewRig 喂
  `ICameraService.LookPitch`）；槽位建立（人形映射缺失显性跳过 + Warning）与帧写（谓词
  `IsAiming ∨ IsFireStance`——与激光同谓词族）。
- 验证：全套 TPS 夹具 **70/70**（红测 12 例全绿 + CharacterLocomotion 36 回归 + SimView 20）；
  L1 **722/722**；**`gen-build-hash.py --check-shared` CHECK OK（shared=459a3145904693b7）——
  Sim/协议零改动硬证据（AC5）**；编译 0 错。

### T9 视野复验（文档面已落，实机面待用户）

- Collider 否决条复审已回写《视野与遮挡专项设计》（采纳收臂：原否决理由以固定视角裁决为前提，
  随其退役失效；收臂优先/渐隐兜底协作口径）。
- **实机复验（用户）**：自由视角下渐隐/影区/自身渐隐/收臂协作——T11 矩阵分项。

### 阶段状态（2026-10-20 施工段收口）

- **T1–T8 全部完成**（红测 12/12 转绿；累计 EditMode 相关面全绿；每任务 L1 722/722 纪律零违规）。
- **待办**：T10 文档收口（新专项设计/旧文归档/8 文档同步/施工进度卷）、T9+T11 实机面
  （用户跑矩阵）、L2 夜间门禁、git 提交（batch-wrapup 收尾）。

### 手感对齐批（2026-10-20，用户实机反馈"手感很差；参考绝地求生"触发）

- **根因取证（探针）**：CM2 默认阻尼全开——双机 OrbitalTransposer **位置阻尼 (1,1,1)**（秒级拖拽/
  相机漂）、**Composer 朝向阻尼 (0.5,0.5)**（视线软追）、Heading 速度滤波 4；**FOV 60 过窄**
  （PUBG 水平 103 ≈ Unity 垂直 71）；**构图贴脸**（视点仅前 1.2m——角色居中挡视野、无越肩开阔感）；
  收臂零平滑（贴墙瞬跳）。
- **PUBG 量级对齐（BattleRig.unity，写后回读探针全中）**：双机阻尼全零（刚性跟手）、速度滤波 0、
  Player `offset(0.75,2.0,-2.1)/FOV 75/视点前推 (0,1.85,4.0)`（角色沉左下）、Aim
  `offset(0.75,2.0,-1.5)/FOV 55/视点 (0,1.85,2.0)`（AimOffsetCurve bind 基准自动取新值）、
  Collider 平滑（SmoothingTime 0.1/Damping 0.5）。
- **GM「视角调参」实时定档面**（CrosshairTuning/GripTuning 同族先例）：`LookTuning`（dev 三宏
  覆写类——灵敏度 rad/px、ADS 收敛比；`XxxOf` 解析器；release 恒常量）+ `BattleViewRig` feed 改读
  覆写 + `GmTestModePage.DrawLookTuneBlock`（灵敏度/ADS 比滑杆写 LookTuning；**FOV 双滑杆经
  SerializedObject 直写运行时 vcam lens**——Editor 程序集不引 Cinemachine 装配，按串取组件，
  Play 退出即回场景值）；复位钮。
- 验证：EditMode 四夹具 **19/19**；L1 **722/722**；编译 0 错。
- **实机定档流程（用户）**：对局内 GM「测试模式 → 视角调参」拖定（灵敏度即拖即试、FOV 直写即视）；
  定档后灵敏度/ADS 比写回 `BattleViewRig` 机制常量、FOV 写回 BattleRig.unity（场景 Inspector 单源）。
- 裁决：视角手感数值链=**常量/场景值单源 + GM 覆写定档面**（准心调参批同口径——不建新表）。
- 编辑器插曲：场景开合把 Boot 场景标伪脏弹模态框——选 Don't Save（本批只动 BattleRig，已显式保存）。

### 旋转模型修正批（2026-10-20，PUBG 对比触发）

- **根因**：`Composer` 的 `TrackedObjectOffset=(0,1.85,4)` 挂在焦点上，而焦点 rotation 恒 identity
  ⇒ 视点固定在世界 +Z，不随 yaw 转。实测几何：pitch=0 时视线水平角只在约 -33°..+24° 变化，
  yaw 到 180° 相机跑到角色正前方、角色在相机背后；pitch 只是抬高/压低相机，画面俯仰被压到
  约 +18°..-15°。这不是 PUBG 的"视线 1:1 跟鼠标"。
- **改造**（位置/视线分离）：
  - 视线：双机 `CinemachineComposer` → `CinemachinePOV`；服务把自持 yaw/pitch 直写 POV 轴
    （`m_HorizontalAxis= yaw`、`m_VerticalAxis = -pitch`），yaw 360°、pitch 钳 ±80°。
  - 位置：`OrbitalTransposer` 只做 yaw 环绕；俯仰不再旋转 FollowOffset，主相机绕场景
    `TpsCameraRigTuning.PivotHeight` 做**恒定臂长**俯仰弧（`PitchFollow` 0..1；0.4 = PUBG 量级的
    角色漂移——相机臂不缩短/拉长），`MinBoomHeight` 地面下限防穿地。
  - 符号统一：`pitch 正 = 上仰`；`BattleViewRig` 喂入改 `+look.y`（Unity 屏幕系 y 向上），
    `AimPitchPostureDriver` 的"上仰 → 胸腔后仰"符号随之自洽。
  - ADS：保留双机优先级 + FOV/贴肩 + 灵敏度 ×0.6；`AimOffsetCurve` 从"Composer 视点 z"
    改为"瞄准机沿主视线前移距离"（主/瞄准基座 z 差为端点，近距收短）——任意 pitch 下瞄准机
    都落在主相机屏心射线上，无俯仰视差。
- **场景值**（BattleRig.unity，写后回读）：双机 POV 水平 ±180/wrap、垂直 ±80；**仅 Player 机**
  挂 `TpsCameraRigTuning`：`PitchFollow=0.4` / `PivotHeight=1.6` / `MinBoomHeight=0.4`；
  瞄准机不挂（位置由主视线派生，避免死配置）；FollowOffset/FOV/优先级/收臂件沿用上批。
- **验证**：
  - 真实服务探针：pitch +30° 主相机 `(y=1.555,z=-2.137) arm=2.138`，瞄准机 `delta=0.6
    along=0.6 across=0`（严格在主视线上）；pitch -30° 主相机 `(y=2.428,z=-1.971) arm=2.138`；
    POV `h=90/v=∓30`。
  - EditMode：TpsCameraLook 6/6（含恒定臂长/视线方向纯数学）、TpsBattleRig 4/4（POV 结构守卫 +
    场景所有权 + 玩家自身碰撞忽略）、TpsAimPoint 3/3、TpsCrosshairCenter 1/1、TpsPointerLock 2/2、
    AimOffsetCurve 3/3、TpsAimPitch 1/1、WeaponGrip 4/4（含枪骨随 Chest 俯仰镜像）、
    CharacterLocomotion 35/35、SimView 20/20、BattleCrosshair 8/8。
  - 编译 0 错；L1 **1785/1785**；`gen-build-hash.py --check-shared` CHECK OK
    （shared=459a3145904693b7，Sim/协议零改动）。
- **问题与修复**：
  - **往地看抖动（用户实机报告）**：`CinemachineCollider.m_IgnoreTag` 为空，而相机 look-at 目标是
    脚底焦点、玩家 `CharacterController` 在 Default 层且 Untagged ⇒ 碰撞射线穿过玩家自身胶囊，
    每帧把相机拉近、下一帧又被位置写回，形成自激抖动（俯仰越低越明显）。修法：玩家 prefab 根加
    `Player` tag，双机 Collider `m_IgnoreTag=Player`；场景守卫加"prefab tag / 双机 IgnoreTag"断言。
  - **瞄准俯仰时枪不动/错位（用户实机报告）**：枪骨 `root/add_weapon_r` 是 root 级、不在 Chest
    层级下；`AimPitchPostureDriver` 只转 Chest ⇒ 手随胸走、枪留在原世界位。修法：driver 把 Chest
    同一世界俯仰旋转绕 Chest 枢轴镜像到枪骨（保留动画自身的枪位/后坐相对姿态，不覆盖成固定握枪
    偏移）；同时把俯仰姿态限定**本地槽位**（远端不跟本机镜头）。新增真 prefab EditMode 用例
    `俯仰姿态_枪骨随Chest同刚体旋转_不脱节`。
  - `TpsBattleRigEditModeTests` 原每次 `OpenScene/CloseScene`，与已加载场景重复句柄，曾把编辑器
    卡进场景保存死锁；改为"已加载则复用、只关自己开的"，并给 `ApplyRigDerived` 加**首次 Follow 前
    no-op**（纯状态测试不写场景 vcam）。重启编辑器后定向用例全绿，未再复现。
- **待办**：T10 专题设计按 POV/恒定臂长弧/ADS 新链重写；实机矩阵（T9/T11）验旋转跟手、角色构图、
  上看/下看边界、ADS 无甩动；`PitchFollow=0.4` / `PivotHeight=1.6` 为场景初档，实机可继续调
  （Inspector 单源；调高 follow 更锁角色、调低更漂）。

---

## 相机高度试调（2026-10-30）

- 用户实机反馈步枪/准心视差，先试低成本方向：只降相机 `FollowOffset.y`，不动 pivot/x/z。
- 场景 `BattleRig.unity`：`Player Camera` y 2 → **1.75**；`Aim Camera` y 2 → **1.65**（x=0.75、z=-2.1/-1.5 不变，`PivotHeight=1.6` 不变）。
- 验证：`TpsBattleRigEditModeTests` **4/4**；Unity 无代码改动。待用户进对局看手感（角色遮挡/掩体视野/ADS 进出/俯仰）。
- 若观感不足，下一档候选 1.85/1.70（更保守）或 1.70/1.60（更低）；仍不齐则归“枪管朝向/Aim IK”，不是高度问题。

---

## 开发直入接线修复（2026-10-30）

- 现象：Boot 场景 `DevStartupConfig.DirectToTraining=1`，Play 后仍停在入口页，无 `[Main] 直入训练场`。
- 根因：`ContainerModule` 构造 `ProcedureMain` 时未传 `IDevSession`，`ProcedureMain` 回落 `NullDevSession`（容器里实际是 `DevSession`）；`DirectToTrainingEnabled` 恒 false。
- 修复：`new ProcedureMain(..., dev: context.Require<IDevSession>())`。
- 验证（真实 Play）：`clear_console → editor_play` 后日志 `[Main] 直入训练场` → `[Match] 本地测试模式 Room-Test` → `[Battle] joined (JoinAck)` → `BattleRig/TrainingGround` 叠加加载 → 表现件 attach；探针 `stage=Battle devActive=True testModeActive=True`。随后 `editor_stop`。
- 备注：直入后仍出现一条 `checksum-mismatch` 诊断包日志（未阻塞进局），另案观察，不并入本修复。

### 追加（2026-10-30）：相机高度回退

- 用户实机判定降高“没啥用”；`BattleRig` 已回退 `Player Camera y=2.0`、`Aim Camera y=2.0`（x/z 不变），场景已保存。
- 结论：视差主因不是相机高度，下一步归“武器朝向/瞄准姿态”，不再在高度上试档。

### 追加（2026-10-30）：ADS 枪口对齐

- 用户确认“枪口瞄准线总在准心下方”；相机高度回退 2.0/2.0 后，新增 `WeaponAimConstraint`。
- 行为：仅本地、仅瞄准态；把根级枪骨绕自身枢轴补转，使 `Muzzle` 世界前向指向准心点；单帧 ≤20°；不改手/臂；退出瞄准停写。
- 接线：`CharacterLocomotionDriver(aimPointOf)` → `BattleViewSceneAssembler.AttachCore` → `BattleViewRig` 传 `BattleLookController.AimPoint`。
- 验证：Unity 编译 0 错；真 prefab 脚本 `valid=True idleDrift=0.000 residual=0.276 frames=1`。本机 Unity TestRunner 跑批再次卡死（重启后复现），定向 EditMode 未执行；用例已补，待 TestRunner 恢复后跑。

### 追加（2026-10-30）：腰射也对齐

- 用户追加“腰射也要对齐”；`WeaponAimConstraint` 门控由仅 `IsAiming` 扩到 `IsAiming || IsFireStance`。
- 修正上限分档：ADS 20°，腰射 45°（FireIdle 比 AimIdle 低，允许更大抬枪量）；非射击语境停写。
- 真 prefab 脚本：`valid=True idleDrift=0.000 hipResidual=0.276 adsResidual=0.034 frames=2`；Unity 编译 0 错。

### 修正（2026-10-30）：手/枪分离

- 用户实机反馈 ADS/腰射手和枪分离；根因是上一版只转 `add_weapon_r`，手（Chest 子骨）没跟着转。
- 改为 `WeaponAimConstraint` 把**胸腔 + 枪骨作为同一刚体**绕 Chest 枢轴补转：手随 Chest 转，枪骨镜像同一旋转；握持关系不再变化。
- 真 prefab 脚本：`handPosDrift=0.000000 handRotDrift=0.000000`，`hipResidual=0.450`、`adsResidual=0.097`；编译 0 错。
- 代价：修正角来自胸腔扭转（ADS≤20°/腰射≤45°），不是手臂 IK；视觉上上半身会朝准心带一点，但手不脱枪。

### 追加（2026-10-30）：左右肩切换

- 用户要求加左右肩切换；`ICameraService.ToggleShoulder()` + `CinemachineCameraService` 镜像场景 `FollowOffset.x`；ADS 随主偏移同步；`Reset` 回右肩。
- 输入 Q（`NewInputIntentSource.ShoulderSwapPressed`）→ `BattleLookController` 仅 pointerLocked 消费。
- Play 端口脚本：`before=0.750 left=-0.750 right=0.750`；Unity 编译 0 错。
