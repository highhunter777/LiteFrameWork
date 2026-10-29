using System;
using LiteFramework;
using LiteSim;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LiteClient
{
    /// <summary>
    /// New Input System 设备源（<see cref="IIntentSource"/> 的 Input System 实现，
    /// 《角色状态与动作专项设计》§3 第 1 件"键鼠和触屏输出相同的 Move/Aim/Buttons"）。
    ///
    /// **为什么用 Input System 而不是 legacy <c>UnityEngine.Input</c>**：同一套 Action 资产同时
    /// 覆盖键鼠与触屏（《联机战斗演示专项设计》§1"触屏沿用同一 Move/Aim/Buttons 输入面"），
    /// 且重绑/多设备由框架承担——设备源不硬编码 KeyCode。
    ///
    /// **职责边界**（三件里只管第 1 件）：
    /// - 上下文门与帧边界门归 <see cref="IInputService"/>；本类只回答"按键现在是什么"；
    /// - **离散意图的按键沿在这里产生**：<c>Fire</c>/<c>Aim</c> 是连续意图（按住即持续），
    ///   而 <c>Reload</c> 属 §3 第 3 件的"按键沿所在的一个逻辑帧才置位"（`WasPressedThisFrame`），
    ///   需要逐玩家单调递增的 <c>ActionSeq</c>。其余离散动作（Switch/Skill/Pickup/UseItem）
    ///   Action 资产里尚未定义，出现消费者时按 Reload 同款追加；
    /// - 瞄准由**相机屏心射线 → 与预测世界求交得瞄准目标点**解算（AimPoint 单口径：只产点、
    ///   不产方向——朝向与弹道两端自点派生，单向无环；TPS 指针锁定形态下鼠标只驱动相机视角，
    ///   "鼠标屏幕位"不再参与瞄准）；求交所需的世界与槽位经 <see cref="SetAimWorld"/> 注入
    ///   （未注入则退化为纯地面点）；
    /// - **移动按相机平面 yaw 旋转**：W=屏幕上=相机 forward 投影；
    ///   相机为 null（纯测试装配）时退化为世界轴直映射。
    /// </summary>
    public sealed class NewInputIntentSource : IIntentSource, IAimWorldSink, IDisposable
    {
        /// <summary>地面平面（俯视角：瞄准射线打到 y=0 的地面）——目标点解算的**兜底**面
        /// （实体圆柱未命中时落这里，见 <see cref="TryResolveAimPoint"/>）。</summary>
        private readonly Plane _groundPlane = new Plane(Vector3.up, 0f);

        private readonly PlayerInputActions _actions;
        private readonly bool _ownsActions;      // 自建资产时负责释放；外部传入（装配根共享）由调用方释放
        private Camera _camera;
        private readonly LiteClient.IDevSession _dev;   // 本局规则来源（爆头带覆写；null = 常规房/发布形态）

        // 瞄准点（AimPoint 单口径：**唯一瞄准表示**——相机射线与实体/地面的交点，世界坐标；
        // 朝向与弹道方向两端自本点派生，方向字段已随重规划退役）
        private float _aimPointX;
        private float _aimPointY;
        private float _aimPointZ;
        private bool _disposed;

        // 预测世界与本地槽位（三维瞄准解算用；IAimWorldSink 注入，null/越界 ⇒ 退回二维口径）
        private SimWorldState _aimWorld;
        private int _localSlot = -1;

        private Vector3 _aimPointWorld; // 本帧解算的**目标点**（三维；无解算帧 = false）
        private bool _hasAimPoint;
        /// <summary>
        /// 最近一次采样解算出的**瞄准目标点**（世界坐标）。
        ///
        /// **语义随三维化变更（《固定斜视角射击方案专项设计》§3）**：
        /// 该点是"准心射线在世界里点到的东西"——命中实体时**带高度**（准心在敌人头上则点高 1.8m），
        /// 未命中实体时为地面点（y=0）。**不再是"永远是 y=0 的地面点"**。
        /// 消费者注意：构图偏移（<c>ICameraService</c> 只取水平投影）与武器激光（按三维弹道截停）
        /// 都仍成立；但任何"假定它 y=0"的消费方都必须改，否则会与子弹弹道撕裂。
        /// </summary>
        public bool TryGetAimPoint ( out Vector3 point ) { point = _aimPointWorld; return _hasAimPoint; }
        public string Name => "new-input-system";

        /// <param name="actions">Action 资产包装（装配根创建并持有；null = 自建，由本类释放）。</param>
        /// <param name="camera">瞄准解算相机（null = 不做瞄准换算，沿用上次方向）。</param>
        /// <param name="dev">开发会话端口（读本局规则的爆头带覆写；null = 无覆写——常规房/发布形态）。</param>
        public NewInputIntentSource(PlayerInputActions actions = null, Camera camera = null,
            LiteClient.IDevSession dev = null)
        {
            _ownsActions = actions == null;
            _actions = actions ?? new PlayerInputActions();
            _camera = camera;
            _dev = dev;
            _actions.GamePlay.Enable();          // 采集前置：动作图未启用时读数恒为零（静默失效的经典形态）
        }

        /// <summary>瞄准解算相机（对局相机随局变化——表现域建好后经 <c>IInputService.SetAimCamera</c> 注入）。</summary>
        public Camera Camera
        {
            get => _camera;
            set => _camera = value;
        }

        /// <summary>
        /// 瞄准解算相机（装配根注入：表现域建好主相机后设一次）。
        /// **为什么不进 <see cref="IIntentSource"/> 接口**：那会让核心接口依赖 <c>UnityEngine.Camera</c>，
        /// 从而把 `LiteClient.Runtime` 的输入三件排除出 L1 的源链接覆盖（纯逻辑那部分必须能脱离引擎编译）。
        /// 相机只是**本实现**的输入之一，由装配根对具体类型设置即可——接口不必为实现的设备差异扩面。
        ///
        /// 传进来的相机**可能随场景切换被销毁**（主相机是场景对象）：<see cref="ResolveCamera"/> 在采样时
        /// 检查并回落到当前 `Camera.main`，调用方不需要在每次切场景后记得重设（那种约定迟早会漏）。
        /// </summary>
        public void SetAimCamera(Camera camera) => _camera = camera;

        /// <summary>
        /// 注入本地预测世界与本地玩家槽位（<see cref="IAimWorldSink"/>；流程层经
        /// <c>IInputService.SetAimWorld</c> 每渲染帧调用）。
        ///
        /// **为什么必须注入而不能自己找**：瞄准点解算要判"准心点到的是什么"（实体圆柱还是地面），
        /// 那需要世界——而 <see cref="IIntentSource"/> 的契约是设备源**不缓存权威状态**
        /// （否则是与裁决不同帧的陈旧世界，表现为判定时灵时不灵）。世界的所有权在流程层。
        ///
        /// **世界在本源只用于求交**：准心射线与实体圆柱求交（含跳过自身的槽位）——AimPoint 单口径下
        /// 本源**不读、也不产出任何方向/朝向**（朝向与弹道两端自点派生，"锚点闭环自激"问题整体消亡）。
        ///
        /// 传 null / 槽位越界 ⇒ 本源退化为**纯地面点解算**（只与地面平面求交，是合法退化形态）。
        /// </summary>
        public void SetAimWorld(SimWorldState world, int localSlot)
        {
            _aimWorld = world;
            _localSlot = world != null ? localSlot : -1;
        }

        /// <summary>预测世界与本地槽位是否可用（决定走三维口径还是二维退化）。</summary>
        private bool HasAimWorld()
            => _aimWorld != null && _localSlot >= 0 && _localSlot < SimConfig.MaxEntities;

        /// <summary>
        /// 相机射线 → **瞄准目标点**（§4 的核心换算）：
        /// <list type="number">
        /// <item>与预测世界的**实体圆柱**求交——用**同一个** <see cref="SimRaycast"/>（客户端不另立
        ///   圆柱数学：两份几何必然漂移，表现为爆头时灵时不灵，且难以定位）。命中则取交点，
        ///   <b>带高度</b>：准心在敌人头上 → 点高 ≈1.8m → 弹道抬进头部带。</item>
        /// <item>未命中实体则落**地面 y=0**（打空/打远）。</item>
        /// </list>
        ///
        /// **障碍刻意不入此步**：本步的语义是"准心点到的是什么"，而截断是权威判定阶段的事
        /// （<see cref="ShootingSystem"/> 会用同一几何再判一次）。若这里先被墙截断，墙后敌人的准心
        /// 会被拉低成打墙的弹道——既丢了爆头语义，也让"准心对着敌人却打墙"变成采集侧的裁决。
        /// 保持"准心指哪、解出哪条弹道"，子弹是否被墙挡住交给权威判定，视觉与判定自然一致。
        ///
        /// 射程用 <see cref="CombatConfig.HitscanRange"/>：比它更远的目标本来就打不到，
        /// 射线穿过去也不会被算成目标点。
        /// </summary>
        private bool TryResolveAimPoint(Ray ray, in SimVector3 shooterPos, out Vector3 point)
        {
            point = Vector3.zero;
            // **走 Vector3 入口**（形参序不可能传错）：三维重载是 (dx, dz, dy)，曾把方向按 (x,y,z)
            // 顺序直传 ⇒ y/z 对调、水平方向整体错位、实体求交恒 miss → 落地面（"准心在身上打不中、
            // 激光瞄地"实测事故；探针里同射线手算命中、游戏解算不命中即此因）。
            if (HasAimWorld()
                && SimRaycast.RaycastEntities(_aimWorld, _localSlot,
                    new SimVector3(ray.origin.x, ray.origin.y, ray.origin.z),
                    new SimVector3(ray.direction.x, ray.direction.y, ray.direction.z),
                    CombatConfig.HitscanRange, out _, out float t, _dev?.MatchRules))
            {
                point = ray.GetPoint(t);
                return true;
            }

            // 地面兜底：超程（|交点−射手| > 射程）与无交（瞄天）同走**射程截断**——第三人称自由俯仰下
            // 屏心射线可能无地面交或交在极远处，截断保证**任何合法开火帧都有闸内有效点**
            // （消灭"瞄天=无点非法帧 / 超程=InputGate 整帧丢弃〔含 Move〕"两类丢帧）。
            // 截断距离取 <see cref="CombatConfig.HitscanRange"/>（服务器闸 = HitscanRange×1.1——客户端
            // 钳在闸内 0.9 折余量；不与服务器闸常量跨程序集单源，闸值口径以推导注释钉住）。
            if (_groundPlane.Raycast(ray, out float distance))
            {
                Vector3 ground = ray.GetPoint(distance);
                if (SquaredDistanceToShooter(ground, in shooterPos) <= CombatConfig.HitscanRange * CombatConfig.HitscanRange)
                {
                    point = ground;
                    return true;
                }
            }

            point = RangeClampedPoint(ray, in shooterPos);
            return true;
        }

        private static float SquaredDistanceToShooter(Vector3 p, in SimVector3 shooterPos)
        {
            float dx = p.x - shooterPos.X;
            float dy = p.y - shooterPos.Y;
            float dz = p.z - shooterPos.Z;
            return dx * dx + dy * dy + dz * dz;
        }

        /// <summary>沿相机射线取"距射手恰为射程"的截断点（射线与以射手为心、射程为半径的球面交的远根）。
        /// 射线起点近肩必在球内 ⇒ 恒有正根；数学退化（判别式非正/负根）回射线起点，由调用方口径兜底。</summary>
        private static Vector3 RangeClampedPoint(Ray ray, in SimVector3 shooterPos)
        {
            float range = CombatConfig.HitscanRange;
            Vector3 oc = ray.origin - new Vector3(shooterPos.X, shooterPos.Y, shooterPos.Z);
            float b = 2f * Vector3.Dot(oc, ray.direction);
            float c = Vector3.Dot(oc, oc) - range * range;
            float discriminant = b * b - 4f * c;
            if (discriminant <= 0f) return ray.origin;
            float t = (-b + Mathf.Sqrt(discriminant)) * 0.5f;
            return t > 0f ? ray.origin + ray.direction * t : ray.origin;
        }

        /// <summary>取当前可用的瞄准相机：缓存的失效（随场景销毁）就回落到 <c>Camera.main</c>。</summary>
        private Camera ResolveCamera()
        {
            if (_camera != null && !ReferenceEquals(_camera, null)) return _camera;

            _camera = Camera.main;                   // 场景切换后的新主相机（无则 null，见调用点的判定）
            return _camera;
        }

        /// <summary>
        /// 本帧视角增量（像素；`GamePlay/Look`——鼠标 delta）。第三人称自由视角的**相机驱动源**：
        /// 纯视图侧读数（不进意图三件/不进协议——鼠标增量只喂 <c>ICameraService.AddLookDelta</c>，不上行）；
        /// 像素→弧度换算归喂入侧。App 层经注入位消费（R12 边界内），不直接认识 InputSystem。
        /// </summary>
        public Vector2 LookDelta => _actions.GamePlay.Look.ReadValue<Vector2>();

        public IntentSample Sample(in SimVector3 localPos)
        {
            _hasAimPoint = false;
            if (_disposed) return IntentSample.None;
            
            // 未启用/无设备时 InputAction 读数为零——这是"采样到空意图"，不是"没有采样"。
            // 两者的区分见 IntentSample 的注释；本实现只要资产已启用就始终算采到。
            Camera aimCamera = ResolveCamera();       // 相机同时是瞄准解算源与移动的"屏幕基准"（见下）
            Vector2 move = _actions.GamePlay.Move.ReadValue<Vector2>();
            var frame = default(SimInputFrame);
            float moveX = move.x;
            float moveZ = move.y;                     // 2D 向量的 y 轴先按屏幕"上"理解，再旋进世界（见下）
            if (aimCamera != null)
            {
                // **相机相对移动**：W = 屏幕上 = 相机平面 forward 投影，D = 屏幕右 = 相机 right——
                // 肩后/自由相机一旦有 yaw，世界轴直映射的 WASD 会横着走。
                // 只在**采集侧**旋转：Sim 输入契约仍是世界空间向量，服务器/预测/协议零改动。
                // 边界：正俯视（pitch≈-90°）时平面 yaw 不可良定义——该形态应保持俯视相机的固定 yaw。
                float yawRad = aimCamera.transform.eulerAngles.y * Mathf.Deg2Rad;
                float sin = Mathf.Sin(yawRad);
                float cos = Mathf.Cos(yawRad);
                float wx = moveX * cos + moveZ * sin;  // 屏幕右×(cos,-sin) + 屏幕上×(sin,cos)
                float wz = -moveX * sin + moveZ * cos;
                moveX = wx;
                moveZ = wz;
            }
            frame.MoveX = moveX;
            frame.MoveZ = moveZ;

            // 长度 ≤1 契约：数字键盘/手柄可能有轴向过冲与斜向超长，采集侧负责归一
            float moveMag2 = frame.MoveX * frame.MoveX + frame.MoveZ * frame.MoveZ;
            if (moveMag2 > 1f)
            {
                float inv = 1f / Mathf.Sqrt(moveMag2);
                frame.MoveX *= inv;
                frame.MoveZ *= inv;
            }
            // 归一化的浮点舍入可能把长度平方推过 1（1+1ulp）——服务器闸门按 >1 整帧拒收
            // （机理与实测值见 IntentVectorLimit 类注释）；出界即按余量收缩，界内逐位不动。
            IntentVectorLimit.EnsureWithinLengthLimit(ref frame.MoveX, ref frame.MoveZ);

            // 瞄准（**AimPoint 单口径**）：
            // **相机屏心射线** → **与预测世界求交得瞄准目标点 P**（实体圆柱未中落地面/截断）
            // → 只上报 P（点即唯一瞄准表示；朝向与弹道方向由两端自 P 派生，单向无环）。
            //
            // **为什么是点不是方向**：方向口径下"服务端从枪口沿方向求交"命中圆柱近弧，与准心所指
            // 有高度偏差（爆头边界上的"瞄着下沿打不中"）；且方向向量与"锚点/枪口偏移/Yaw 反推"
            // 纠缠出闭环自激与平行差——点口径把这些问题整体消掉。
            // 自由视角下射线可能无地面交/交在极远——由**射程截断**兜底（TryResolveAimPoint 第三档）；
            // 唯一无点路径仍是相机未就绪，沿用上次点。
            // 场景切换后缓存会失效——ResolveCamera 已回落到当前 Camera.main
            if (aimCamera != null)
            {
                // TPS 屏心射线（viewport 中心——分辨率无关）：指针锁定形态下鼠标只驱动相机视角，
                // 瞄准点恒自相机中心解算——不再依赖鼠标屏幕位（Mouse.current 缺席也可解算）。
                var ray = aimCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
                if (TryResolveAimPoint(ray, in localPos, out Vector3 p))
                {
                    _aimPointWorld = p; _hasAimPoint = true;
                    _aimPointX = p.x;
                    _aimPointY = p.y;
                    _aimPointZ = p.z;
                }
            }

            frame.AimPointX = _aimPointX;
            frame.AimPointY = _aimPointY;
            frame.AimPointZ = _aimPointZ;
            frame.Buttons = _actions.GamePlay.Fire.IsPressed() ? SimInputFrame.ButtonFire : 0u;
            frame.Buttons |= _actions.GamePlay.Aim.IsPressed() ? SimInputFrame.ButtonAim : 0u;
            // 换弹 = **按下沿**（离散意图——"按键沿所在的一个逻辑帧"由 InputService 帧边界门收敛；
            // ActionSeq 由 InputService 在帧边界产生，设备源不产 seq）。
            frame.Buttons |= _actions.GamePlay.Reload.WasPressedThisFrame() ? SimInputFrame.ButtonReload : 0u;
            return new IntentSample(frame);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_actions != null)
            {
                _actions.GamePlay.Disable();     // 采集后置：不 Disable 会触发资产析构断言（生成的 ~PlayerInputActions）
                if (_ownsActions) _actions.Dispose();
            }
        }
    }
}
