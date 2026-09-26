using System;
using LiteSim;
using UnityEngine;

namespace LiteGame
{
    /// <summary>
    /// 键鼠设备源（<see cref="IIntentSource"/> 的 PC 实现）：键位 + 鼠标地面射线 → <see cref="SimInputFrame"/>。
    /// **只产意图数据，不产生位移**——位移由 Sim 的 <c>MovementSystem</c> 依输入推进
    /// （《状态同步专项设计》§1 原则 3"View → Sim 只有输入和读取"）。
    ///
    /// 与旧实现（<c>LiteSim.View.PlayerController</c>）的职责收窄（2026-09-26 输入服务批）：
    /// 上下文门与帧边界门**上移到 <see cref="InputService"/>**——设备源不再自己裁决"该不该动"，
    /// 只回答"按键现在是什么"。收益有三：拦截源可登记可追溯、逻辑帧消费纪律不再靠调用点自觉、
    /// 设备源可整体替换（触屏/手柄）而不必重写裁决逻辑。
    ///
    /// 两个契约的落地位置（设备侧）：
    /// - **采集侧归一**：移动/瞄准长度 ≤ 1（<see cref="SimInputFrame"/> 契约）；非法值由传输层与 Sim 再挡一道；
    /// - **瞄准参照原点**：调用方传 <c>localPos</c>（本地玩家位置，取自 Sim 预测态——不读视图 Transform：
    ///   视图位置是平滑过的表现量，拿它算瞄准会把平滑误差回灌进输入）。
    ///
    /// 相机换算（WASD → 世界 MoveX/MoveZ；鼠标屏幕射线 → 地面 Aim 方向）在设备侧用
    /// <c>UnityEngine.Mathf</c> 无妨——**输入是数据**（进输入历史与传输），换算确定性不影响 Sim。
    /// </summary>
    public sealed class KeyboardMouseIntentSource : IIntentSource
    {
        /// <summary>地面平面（俯视角：瞄准射线打到 y=0 的地面）。</summary>
        private readonly Plane _groundPlane = new Plane(Vector3.up, 0f);

        private Camera _camera;
        private float _aimX = 1f;      // 瞄准方向（长度 ≤1；默认朝 +X）
        private float _aimZ;

        public string Name => "keyboard-mouse";

        /// <summary>移动键位（默认 WASD；Action Maps + 重绑落 <c>GameSettings</c> 归 U2 余部——本批保持键位直读）。</summary>
        public KeyCode Forward = KeyCode.W;
        public KeyCode Back = KeyCode.S;
        public KeyCode Left = KeyCode.A;
        public KeyCode Right = KeyCode.D;

        /// <summary>开火键（默认鼠标左键）。</summary>
        public bool FireOnMouse = true;

        /// <summary>瞄准相机（俯视角；null = 无瞄准换算，沿用上次方向）。</summary>
        public Camera Camera
        {
            get => _camera;
            set => _camera = value;
        }

        public KeyboardMouseIntentSource(Camera camera = null) => _camera = camera;

        /// <summary>
        /// 采集本渲染帧。**不写 Sim**——返回值由 <see cref="InputService"/> 交给
        /// <c>BattleContext</c>（预测消费）与传输（上行）。
        /// </summary>
        /// <param name="localPos">本地玩家当前**预测**世界位置（瞄准方向的参照原点）。</param>
        public SimInputFrame Sample(in SimVector3 localPos)
        {
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

            var frame = default(SimInputFrame);
            frame.MoveX = mx;
            frame.MoveZ = mz;
            frame.AimX = _aimX;
            frame.AimZ = _aimZ;
            frame.Buttons = (FireOnMouse && Input.GetMouseButton(0)) ? SimInputFrame.ButtonFire : 0u;

            // ---- 离散意图（未实现，见 IInputService 数值面登记）----
            // Reload/Switch/Skill/Pickup/UseItem 需要按键沿检测 + 逐玩家单调递增的 action_seq，
            // 本批不产生这些位（ActionSeq 保持 0）。武器槽/目标实体随对应消费者接入（尚无设备映射）。
            return frame;
        }
    }
}
