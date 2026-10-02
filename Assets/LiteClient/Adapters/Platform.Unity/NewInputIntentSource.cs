using System;
using LiteFramework;
using LiteSim;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LiteGame
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
    /// - **离散意图的按键沿在这里产生**：<c>Fire</c> 是连续意图（按住即持续），
    ///   而 Reload/Switch/Skill/Pickup/UseItem 属 §3 第 3 件的"按键沿所在的一个逻辑帧才置位"，
    ///   需要逐玩家单调递增的 <c>ActionSeq</c>。Action 资产里这些动作**尚未定义**，
    ///   故本版只产出 Fire/Move/Aim，不伪造其它位；
    /// - 瞄准方向由**鼠标屏幕点 → 地面平面**解算，参照原点由调用方逐帧给出
    ///   （<see cref="Sample"/> 的 <c>localPos</c>——生产调用方传 Sim 预测态位置，不读视图 Transform）；
    /// - **移动按相机平面 yaw 旋转**：W=屏幕上=相机 forward 投影；
    ///   相机为 null（纯测试装配）时退化为世界轴直映射。
    /// </summary>
    public sealed class NewInputIntentSource : IIntentSource, IDisposable
    {
        /// <summary>地面平面（俯视角：瞄准射线打到 y=0 的地面）。</summary>
        private readonly Plane _groundPlane = new Plane(Vector3.up, 0f);

        private readonly PlayerInputActions _actions;
        private readonly bool _ownsActions;      // 自建资产时负责释放；外部传入（装配根共享）由调用方释放
        private Camera _camera;
        private float _aimX = 1f;                // 瞄准方向（长度 ≤1；默认朝 +X）
        private float _aimZ;
        private bool _disposed;

        public string Name => "new-input-system";

        /// <param name="actions">Action 资产包装（装配根创建并持有；null = 自建，由本类释放）。</param>
        /// <param name="camera">瞄准解算相机（null = 不做瞄准换算，沿用上次方向）。</param>
        public NewInputIntentSource(PlayerInputActions actions = null, Camera camera = null)
        {
            _ownsActions = actions == null;
            _actions = actions ?? new PlayerInputActions();
            _camera = camera;
            _actions.GamePlay.Enable();          // 采集前置：动作图未启用时所有读数恒为零（静默失效的经典形态）
        }

        /// <summary>瞄准解算相机（对局相机随局变化——表现壳建好后经 <c>IInputService.SetAimCamera</c> 注入）。</summary>
        public Camera Camera
        {
            get => _camera;
            set => _camera = value;
        }

        /// <summary>
        /// 瞄准解算相机（装配根注入：表现壳建好主相机后设一次）。
        /// **为什么不进 <see cref="IIntentSource"/> 接口**：那会让核心接口依赖 <c>UnityEngine.Camera</c>，
        /// 从而把 `LiteClient.Runtime` 的输入三件排除出 L1 的源链接覆盖（纯逻辑那部分必须能脱离引擎编译）。
        /// 相机只是**本实现**的输入之一，由装配根对具体类型设置即可——接口不必为实现的设备差异扩面。
        ///
        /// 传进来的相机**可能随场景切换被销毁**（主相机是场景对象）：<see cref="ResolveCamera"/> 在采样时
        /// 检查并回落到当前 `Camera.main`，调用方不需要在每次切场景后记得重设（那种约定迟早会漏）。
        /// </summary>
        public void SetAimCamera(Camera camera) => _camera = camera;

        /// <summary>取当前可用的瞄准相机：缓存的失效（随场景销毁）就回落到 <c>Camera.main</c>。</summary>
        private Camera ResolveCamera()
        {
            if (_camera != null && !ReferenceEquals(_camera, null)) return _camera;

            _camera = Camera.main;                   // 场景切换后的新主相机（无则 null，见调用点的判定）
            return _camera;
        }

        /// <summary>
        /// 鼠标屏幕位（准心/HUD 消费面）。InputSystem 的 import 只许在本
        /// 适配器边界内——App 层的准心驱动经注入位消费本属性，不直接认识 InputSystem。
        /// 无鼠标设备（纯触屏形态）回退零点。
        /// </summary>
        public Vector2 MouseScreenPosition
        {
            get
            {
                Mouse mouse = Mouse.current;
                return mouse != null ? mouse.position.ReadValue() : Vector2.zero;
            }
        }

        public IntentSample Sample(in SimVector3 localPos)
        {
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

            // 瞄准：鼠标位置 → 地面平面交点 → 相对瞄准原点（aimpoint 口径，见 Sample 的调用方）的方向
            // 场景切换后缓存会失效——ResolveCamera 已回落到当前 Camera.main
            if (aimCamera != null && Mouse.current != null)
            {
                var ray = aimCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
                if (_groundPlane.Raycast(ray, out float distance))
                {
                    Vector3 p = ray.GetPoint(distance);
                    float ax = p.x - localPos.X;
                    float az = p.z - localPos.Z;
                    float aimMag2 = ax * ax + az * az;
                    if (aimMag2 > 0.000001f)
                    {
                        float inv = 1f / Mathf.Sqrt(aimMag2);
                        _aimX = ax * inv;
                        _aimZ = az * inv;
                    }
                }
            }

            frame.AimX = _aimX;
            frame.AimZ = _aimZ;
            frame.Buttons = _actions.GamePlay.Fire.IsPressed() ? SimInputFrame.ButtonFire : 0u;
            frame.Buttons |= _actions.GamePlay.Aim.IsPressed() ? SimInputFrame.ButtonAim : 0u;
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
