# 实施任务清单：第三人称越肩射击改造

> 本文件由 workflow.py 生成，请使用 `task` 子命令更新，勿手改。

## 任务清单

阶段：正式施工（implement） ｜ 完成 8/11（取消 0，阻塞 0）

| ID | 任务 | 依赖 | 状态 | 验收标准 | 验证方式 | 证据 |
|---|---|---|---|---|---|---|
| T1 | 相机视角端口与适配器改造 | - | 完成 | ICameraService 新增 AddLookDelta/pitch 钳制/构图档位成员；CinemachineCameraService 自持 yaw/pitch 状态并应用到 vcam（越肩偏移+收臂）；Look 增量经端口喂入（消费 Player.inputactions 既有 Look action）；顺手修债：Aim Camera 按名解析改配置化引用（审计 L42） | EditMode 定向（相机端口用例）+ Unity 重编译 0 错 | Unity 重编译 0 错；EditMode 定向 TpsCameraLook 5/5 + CameraViewMirror 2/2 + SimView 20/20；L1 LiteFramework.Core.Tests 722/722（… |
| T2 | 场景 rig 越肩化改造 | T1 | 完成 | 训练场/对局场景 Main/Aim 双机改为越肩形态参数（肩偏移/相机距离/pitch 限位/FOV 两档），场景序列化只走 unity-pipeline；场景值核证记录 | unity-pipeline 场景读写 + EditMode 相机探针断言 | BattleRig 双机越肩化（写后回读探针全中）；遗留 Mouse X 输入轴清名；HardLookAt→Composer 平视；CinemachineCollider×2；适配器球面俯仰+焦点 identity；TpsBattleRi… |
| T3 | 指针锁定管理件 | - | 完成 | LiteClient 平台适配层新增指针锁定管理（对局锁+隐藏、上下文门被拦/死亡/退场解锁的状态机）；收编 BattleCrosshairDriver._setCursorVisible 为唯一光标写者 | EditMode 锁定状态机用例 | TpsPointerLockEditModeTests 2/2 转绿；准心 9/9 回归（光标钩子收编 no-op 默认）；BattleViewRig Tick 驱动（对局控制中=锁+隐藏；门被拦/未建局/死亡=解锁）+ Dispose … |
| T4 | 采集侧屏心射线与射程截断 fallback | T1, T2 | 完成 | NewInputIntentSource 改屏心射线解算 AimPoint；TryResolveAimPoint 增第三档射程截断点（截断距离按 InputGate 闸值 HitscanRange*1.1 反解、带推导注释）——瞄天/超程开火帧恒有有效点；MouseScreenPosition 消费面处置 | EditMode 采集解算用例 + L1 采集面回归 | TpsAimPointEditModeTests 3/3（R6/R7/R8）；准心+激光+视角合并回归全绿（激光零回归——AimPoint 口径不变仅换射线源）；L1 722/722；编译 0 错 |
| T5 | 准心钉屏心与显隐门复审 | T3, T4 | 完成 | BattleCrosshairDriver 位置源改屏心（不再跟鼠标点）；显隐门复审（腰射常显口径）；散布/后坐 tbcrosshair 表现不动 | EditMode 准心位置/显隐用例 | TpsCrosshairCenterEditModeTests 1/1（R9 转绿）；BattleCrosshairEditModeTests 8/8 回归（位置注入位退役、后坐基准=屏心）；指针锁 2/2 采集 3/3 保持；L1 72… |
| T6 | ADS 构图档位插值与灵敏度缩放 | T1, T2 | 完成 | SetAiming 升级为构图档位插值驱动（肩收紧/FOV/灵敏度缩放），AimHoldGate 滞回保留；SetAimPoint/AimOffsetCurve 构图链复审结论落档 | EditMode 档位插值用例 + 实机 ADS 无甩动分项 | ADS 灵敏度收敛（AdsLookSensitivityScale=0.6 带推导注释，IsLocalAiming 沿应用）；ADS 档位本体=双机优先级+brain FOV/构图混合（T2 场景值 60°/50°+贴肩 2.3m）+Ai… |
| T7 | 团队相机镜像全链退役 | T1 | 完成 | 删 SetViewMirrored 端口/适配器镜像逻辑/TestModeOptions.Id.ViewMirrored 预览开关与 CameraViewMirrorEditModeTests；过期注释清理；grep 零残留 | grep SetViewMirrored/ViewMirrored 零命中 + L1 全量 | SetViewMirrored/ViewMirrored 全仓 grep 零命中（rg 排除 meta）；接口成员/适配器方法与字段/Mirror180/复合支/TestModeOptions-Id+Info+switch×2/TestM… |
| T8 | aim pitch 程序化俯仰姿态 | T1, T2 | 完成 | 上半身随相机 pitch 区间插值（后置写先例 WeaponGripConstraint 同款写序：动画 Evaluate 后、单一写者、退出即停写）；推导注释；素材批挂账登记审计 | EditMode 姿态插值用例 + 实机俯仰射击分项 | AimPitchPosture.UpperBodyPitch 恒等钳制 + AimPitchPostureDriver（人形 Chest 映射、世界系前乘绕角色右轴、Evaluate 后置写/退出即停写）+ CharacterLocomo… |
| T9 | 视野 A1 自由视角复验与 Collider 否决条复审 | T2 | 待办 | 自由视角下渐隐/影区/自身渐隐/收臂协作实机复验全过；CinemachineCollider 否决条复审裁决回写视野专项（新版本号） | 实机分项 + doc-lint | - |
| T10 | 文档收口：新专项设计与视角引用同步 | T1, T2, T3, T4, T5, T6, T7, T8, T9 | 待办 | 新《第三人称越肩射击专项设计》落位并取代《固定斜视角射击方案专项设计》（旧文归档+索引更新）；8 份文档视角引用同步（设计 README/HUD/视野/角色状态/地图/烘焙/联机演示/业务总设计）；施工进度卷登记 | python scripts/gate/doc-lint.py 全过 | - |
| T11 | 实机矩阵重写与全量门禁 | T1, T2, T3, T4, T5, T6, T7, T8, T9, T10 | 待办 | 实机矩阵新写（旋转/收臂/ADS/准心=弹道同源/渐隐/指针门/移动手感/镜像回归/边界）并全跑留证；L1 全量+纪律扫描+buildHash --check-shared 不变+L2 | 实机矩阵全过 + pwsh scripts/gate/l2-unity-gate.ps1 | - |

## 任务详情

### T1 · 相机视角端口与适配器改造

- 文件: Assets/LiteView/Camera/ICameraService.cs, Assets/LiteClient/Adapters/Platform.Unity/CinemachineCameraService.cs

### T2 · 场景 rig 越肩化改造

- 文件: Assets/Scenes

### T3 · 指针锁定管理件

- 文件: Assets/LiteClient/Adapters/Platform.Unity

### T4 · 采集侧屏心射线与射程截断 fallback

- 文件: Assets/LiteClient/Adapters/Platform.Unity/NewInputIntentSource.cs

### T5 · 准心钉屏心与显隐门复审

- 文件: Assets/LiteGame/App/Presentation/BattleCrosshairDriver.cs

### T6 · ADS 构图档位插值与灵敏度缩放

- 文件: Assets/LiteClient/Adapters/Platform.Unity/CinemachineCameraService.cs

### T7 · 团队相机镜像全链退役

- 文件: Assets/LiteView/Camera/ICameraService.cs, Assets/LiteClient/Adapters/Platform.Unity/CinemachineCameraService.cs

### T8 · aim pitch 程序化俯仰姿态

- 文件: Assets/LiteView/Animation, Assets/LiteGame/App/Presentation

### T9 · 视野 A1 自由视角复验与 Collider 否决条复审

- 文件: Docs/设计文档/游戏玩法/视野与遮挡专项设计.md

### T10 · 文档收口：新专项设计与视角引用同步

- 文件: Docs

### T11 · 实机矩阵重写与全量门禁

- 文件: Docs/施工进度/战斗与武器线.md

