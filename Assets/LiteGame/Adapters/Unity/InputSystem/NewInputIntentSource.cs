using System;
using LiteFramework;
using LiteSim;
using UnityEngine;
using UnityEngine.InputSystem;
using LiteFramework;
using LiteGame.Modules.Camera;
using LiteGame.Modules.Input;

namespace LiteGame
{
    /// <summary>
    /// New Input System 设备源（IIntentSource 的 Input System 实现， 《角色状态与动作专项设计》§3 第 1 件"键鼠和触屏输出相同的 Move/Aim/Buttons"）。
    /// </summary>
    public sealed class NewInputIntentSource : IIntentSource, IAimWorldSink, IAimCameraSink, ILookDevice, IDisposable
    {
        /// <summary>地面平面（俯视角：瞄准射线打到 y=0 的地面）——目标点解算的**兜底**面
        /// （实体圆柱未命中时落这里，见 <see cref="TryResolveAimPoint"/>）。</summary>
        private readonly Plane _groundPlane = new Plane(Vector3.up, 0f);

        private readonly PlayerInputActions _actions;
        private readonly bool _ownsActions;      // 自建资产时负责释放；外部传入（装配根共享）由调用方释放
        private Camera _camera;
        private readonly Func<MatchTestRules> _matchRules;   // 本局规则读口（爆头带覆写；null = 常规房/发布形态）

        // 瞄准点（AimPoint 单口径：**唯一瞄准表示**——相机射线与实体/地面的交点，世界坐标；
        // 朝向与弹道方向两端自本点派生，方向字段已随重规划退役）
        private float _aimPointX;
        private float _aimPointY;
        private float _aimPointZ;
        private bool _disposed;

        // 预测世界与本地槽位（三维瞄准解算用；IAimWorldSink 注入，null/越界 ⇒ 退回二维口径）。
        private SimWorldState _aimWorld;
        private int _localSlot = -1;

        private Vector3 _aimPointWorld; // 本帧解算的**目标点**（三维；无解算帧 = false）
        private bool _hasAimPoint;

        // 自由观察冻结（Alt；由 BattleLookController 控制）：冻结期写缓存点/缓存移动基准 yaw。
        private bool _aimFrozen;
        private float _frozenAimX;
        private float _frozenAimY;
        private float _frozenAimZ;
        private float _frozenBasisYawRad;

        /// <summary>最近一次**可用**的移动基准 yaw（弧度）。正俯视（pitch≈−90°）时相机 forward
        /// 水平分量趋零、`eulerAngles.y` 进入万向锁——该形态用它保持方向，不猜（见 Sample 的退化判定）。</summary>
        private float _lastBasisYawRad;

        /// <summary>移动基准 / 瞄准点"平面可用"的最小水平分量平方：cos(5°)² ≈ 0.992 → 垂直分量的
        /// 水平投影 = 1−0.992 = 0.008。低于它即判为正俯视退化（pitch 距 −90° 不足 5°）。</summary>
        private const float PlanarYawMinSqr = 0.008f;
        /// <summary>
        /// 最近一次采样解算出的**瞄准目标点**（世界坐标）。
        /// </summary>
        public bool TryGetAimPoint ( out Vector3 point ) { point = _aimPointWorld; return _hasAimPoint; }
        public string Name => "new-input-system";

        /// <param name="actions">Action 资产包装（装配根创建并持有；null = 自建，由本类释放）。</param>
        /// <param name="camera">瞄准解算相机（null = 不做瞄准换算，沿用上次方向）。</param>
        /// <param name="matchRules">本局规则读口（爆头带覆写；null = 无覆写——常规房/发布形态）。
        /// 由装配根绑定开发会话读口，框架适配器不依赖产品态端口。</param>
        public NewInputIntentSource(PlayerInputActions actions = null, Camera camera = null,
            Func<MatchTestRules> matchRules = null)
        {
            _ownsActions = actions == null;
            _actions = actions ?? new PlayerInputActions();
            _camera = camera;
            _matchRules = matchRules;
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
        /// </summary>
        public void SetAimCamera(Camera camera) => _camera = camera;

        /// <summary>
        /// 注入本地预测世界与本地玩家槽位（IAimWorldSink）。
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
        /// 相机射线 → **瞄准目标点**（§4 的核心换算）： 与预测世界的**实体圆柱**求交——用**同一个** SimRaycast（客户端不另立 圆柱数学：两份几何必然漂移，表现为爆头时灵时不灵，且难以定位）。
        /// </summary>
        private bool TryResolveAimPoint(Ray ray, in SimVector3 shooterPos, out Vector3 point)
        {
            point = Vector3.zero;
            // **走 Vector3 入口**（形参序不可能传错）：三维重载是 (dx, dz, dy)，曾把方向按 (x,y,z) 顺序直传 ⇒ y/z 对调、水平方向整体错位、实体求交恒 miss → 落地面（"准心在身上打不中、 激光瞄地"实测事故）。
            if (HasAimWorld()
                && SimRaycast.RaycastEntities(_aimWorld, _localSlot,
                    new SimVector3(ray.origin.x, ray.origin.y, ray.origin.z),
                    new SimVector3(ray.direction.x, ray.direction.y, ray.direction.z),
                    CombatConfig.HitscanRange, out _, out float t, CombatConfig.Geometry))
            {
                point = ray.GetPoint(t);
                return true;
            }

            // 地面兜底：超程（|交点−射手| > 射程）与无交（瞄天）同走**射程截断**——第三人称自由俯仰下 屏心射线可能无地面交或交在极远处，截断保证**任何合法开火帧都有闸内有效点** （消灭"瞄天=无点非法帧 / 超程=InputGate 整帧丢弃〔含 Move〕"两类丢帧）。
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
        /// 本帧视角增量（像素）。
        /// </summary>
        public Vector2 LookDelta => _actions.GamePlay.Look.ReadValue<Vector2>();

        /// <summary>左右肩切换（Q 初档；纯视图侧，不进意图三件/不上行）。</summary>
        public bool ShoulderSwapPressed => Keyboard.current != null
            && Keyboard.current.qKey.wasPressedThisFrame;

        /// <summary>自由观察按住（新 Input System 的 FreeLook action，Alt）。</summary>
        public bool FreeLookHeld => _actions.GamePlay.FreeLook.IsPressed();

        /// <summary>冻结/解冻瞄准点：冻结时缓存当前 AimPoint 与相机平面 yaw（WASD 基准）。</summary>
        public void SetAimFrozen(bool frozen)
        {
            if (frozen)
            {
                if (!_aimFrozen)
                {
                    _frozenAimX = _aimPointX;
                    _frozenAimY = _aimPointY;
                    _frozenAimZ = _aimPointZ;
                    _frozenBasisYawRad = _camera != null
                        ? _camera.transform.eulerAngles.y * Mathf.Deg2Rad
                        : 0f;
                }
                _aimFrozen = true;
            }
            else
            {
                _aimFrozen = false;
            }
        }

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

            // 自由观察冻结：开火/ADS 当帧自动解除冻结（按相机准心走），其余时间保持冻结点。
            bool fireOrAim = _actions.GamePlay.Fire.IsPressed() || _actions.GamePlay.Aim.IsPressed();
            bool useFrozen = _aimFrozen && !fireOrAim;

            // **俯视退化判定**（移动基准与瞄准点共用同一判据）：pitch 逼近 −90° 时相机 forward 的水平分量趋零——`eulerAngles.y` 同时进入万向锁（值退化/跳变），屏心射线又垂直打在 自己脚下（AimPoint 距本体几厘米。）。
            bool planarUsable = true;
            if (aimCamera != null)
            {
                Vector3 camFwd = aimCamera.transform.forward;
                planarUsable = camFwd.x * camFwd.x + camFwd.z * camFwd.z > PlanarYawMinSqr;
            }

            if (aimCamera != null)
            {
                // **相机相对移动**：W = 屏幕上 = 相机平面 forward 投影，D = 屏幕右 = 相机 right—— 肩后/自由相机一旦有 yaw，世界轴直映射的 WASD 会横着走。
                float yawRad;
                if (useFrozen)
                {
                    yawRad = _frozenBasisYawRad;
                }
                else if (planarUsable)
                {
                    yawRad = aimCamera.transform.eulerAngles.y * Mathf.Deg2Rad;
                    _lastBasisYawRad = yawRad;
                }
                else
                {
                    yawRad = _lastBasisYawRad;            // 正俯视：保持上一次可用基准（不猜）
                }
                float sin = Mathf.Sin(yawRad);
                float cos = Mathf.Cos(yawRad);
                float wx = moveX * cos + moveZ * sin;  // 屏幕右×(cos,-sin) + 屏幕上×(sin,cos)
                float wz = -moveX * sin + moveZ * cos;
                moveX = wx;
                moveZ = wz;
            }
            frame.MoveX = moveX;
            frame.MoveZ = moveZ;

            // 长度 ≤1 契约：数字键盘/手柄可能有轴向过冲与斜向超长，采集侧负责归一。
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

            // 瞄准（**AimPoint 单口径**）： **相机屏心射线** → **与预测世界求交得瞄准目标点 P**（实体圆柱未中落地面/截断） → 只上报 P（点即唯一瞄准表示）。
            if (useFrozen)
            {
                _aimPointX = _frozenAimX;
                _aimPointY = _frozenAimY;
                _aimPointZ = _frozenAimZ;
                _aimPointWorld = new Vector3(_frozenAimX, _frozenAimY, _frozenAimZ);
                _hasAimPoint = _frozenAimX != 0f || _frozenAimY != 0f || _frozenAimZ != 0f;
            }
            else if (aimCamera != null && planarUsable)
            {
                // TPS 屏心射线（viewport 中心——分辨率无关）：指针锁定形态下鼠标只驱动相机视角， 瞄准点恒自相机中心解算——不再依赖鼠标屏幕位（Mouse.current 缺席也可解算）。
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
