using UnityEngine;

namespace LiteSim.View
{
    /// <summary>
    /// 本地玩家输入采集（《联机战斗演示专项设计》§5 联调顺序"输入和三个门"；《M11实施指导》§2.3 C2）：
    /// 键鼠 → <see cref="SimInputFrame"/>。**只产出意图数据，不产生位移**——位移由 Sim 的
    /// MovementSystem 依输入推进（《状态同步专项设计》§1 原则 3"View → Sim 只有输入和读取"）。
    ///
    /// 三个门（按设计逐条落地）：
    /// - **上下文门**（<see cref="SetGate"/>）：UI/菜单/重连提示打开 → <see cref="Intent"/> 置零
    ///   （含 Buttons——不止移动，开火/换弹等离散意图一并清零，否则"关掉 UI 后突然开了一枪"）；
    /// - **逻辑帧消费门**：本类每渲染帧刷新"待用意图"，**消费一次**由 <see cref="RollbackSim"/> 承担
    ///   （PrepareNext 在逻辑帧边界取值；追帧时同值复用，不产生额外输入）——本类不重复实现；
    /// - **采集侧归一**：移动/瞄准向量长度 ≤ 1（Sim 侧契约，见 <see cref="SimInputFrame"/> 注释）；
    ///   非法/越界由传输层与 Sim 再挡一道。
    ///
    /// 相机相关换算（WASD → 世界 MoveX/MoveZ；鼠标屏幕射线 → 地面 AimX/AimZ）在 View 侧用
    /// UnityEngine.Mathf 无妨——**输入是数据**（进 InputHistory 与传输），换算确定性不影响 Sim
    /// （《M11实施指导》§2.2 明示）。
    /// </summary>
    public sealed class PlayerController
    {
        /// <summary>地面平面（俯视角：瞄准射线打到 y=GroundY 的平面）。</summary>
        private readonly Plane _groundPlane = new Plane(Vector3.up, 0f);

        private Camera _camera;
        private float _aimX = 1f;      // 瞄准方向（长度 ≤1；默认朝 +X）
        private float _aimZ;

        /// <summary>上下文门：false = 意图全零（UI/菜单/重连提示打开）。默认放行。</summary>
        private System.Func<bool> _gate;

        /// <summary>移动键位（默认 WASD；重绑归 U2 的 Action Maps——本批不做控制器抽象）。</summary>
        public KeyCode Forward = KeyCode.W;
        public KeyCode Back = KeyCode.S;
        public KeyCode Left = KeyCode.A;
        public KeyCode Right = KeyCode.D;

        /// <summary>开火键（默认鼠标左键）。</summary>
        public bool FireOnMouse = true;

        /// <summary>最近一次采集被上下文门拦下（诊断/HUD 用）。</summary>
        public bool LastBlockedByGate { get; private set; }

        /// <summary>瞄准相机（俯视角；null = 无瞄准换算，沿用上次方向）。</summary>
        public Camera Camera
        {
            get => _camera;
            set => _camera = value;
        }

        public PlayerController(Camera camera = null, System.Func<bool> gate = null)
        {
            _camera = camera;
            _gate = gate;
        }

        /// <summary>设置上下文门（null = 放行）。UI 协调器据此裁决意图是否送达 Sim。</summary>
        public void SetGate(System.Func<bool> gate) => _gate = gate;

        /// <summary>
        /// 采集本渲染帧的意图。**不写 Sim**——返回值由调用方（BattleContext 的 inputProvider）
        /// 交给 <c>RollbackSim</c> 与传输。
        /// </summary>
        /// <param name="localPos">本地玩家当前世界位置（瞄准方向的参照原点——取自 Sim 状态，不读视图 Transform：
        /// 视图位置是经过平滑的表现量，拿它算瞄准会把平滑误差回灌进输入）。</param>
        public SimInputFrame Collect(in SimVector3 localPos)
        {
            var frame = default(SimInputFrame);

            if (_gate != null && !_gate())
            {
                LastBlockedByGate = true;
                return frame;                      // 全零：移动与 Buttons 一并清零（上下文门）
            }
            LastBlockedByGate = false;

            // ---- 移动：长度 ≤1 契约（斜向归一）----
            float mx = (Input.GetKey(Right) ? 1f : 0f) - (Input.GetKey(Left) ? 1f : 0f);
            float mz = (Input.GetKey(Forward) ? 1f : 0f) - (Input.GetKey(Back) ? 1f : 0f);
            float moveMag2 = mx * mx + mz * mz;
            if (moveMag2 > 1f)
            {
                float inv = 1f / Mathf.Sqrt(moveMag2);
                mx *= inv;
                mz *= inv;
            }

            // ---- 瞄准：鼠标地面射线 → 方向（长度 ≤1）----
            if (_camera != null)
            {
                var ray = _camera.ScreenPointToRay(Input.mousePosition);
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

            frame.MoveX = mx;
            frame.MoveZ = mz;
            frame.AimX = _aimX;
            frame.AimZ = _aimZ;
            frame.Buttons = (FireOnMouse && Input.GetMouseButton(0)) ? SimInputFrame.ButtonFire : 0u;
            return frame;
        }
    }
}
