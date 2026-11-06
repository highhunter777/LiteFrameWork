using UnityEngine;

namespace LiteGame.Modules.Camera
{
    /// <summary>
    /// 相机服务端口（《商业级通用客户端框架总设计》§5 框图 `Platform.Unity` 适配器块的抽象面）。
    /// </summary>
    public interface ICameraService
    {
        /// <summary>是否已收到过焦点（首帧落位后为 true；诊断/测试用）。</summary>
        bool HasFocus { get; }

        /// <summary>当前焦点（表现空间；未跟随过 = <c>Vector3.zero</c>）。</summary>
        Vector3 Focus { get; }

        /// <summary>
        /// 跟随目标表现位姿（每渲染帧调用）。
        /// </summary>
        void Follow(in Vector3 target, in Quaternion facing, float deltaSeconds);

        /// <summary>
        /// 瞄准态通知：本地玩家处于 ADS（右键瞄准）时传 true——期望**瞄准视角 接管**。
        /// </summary>
        void SetAiming(bool aiming);

        /// <summary>瞄准点通知（世界空间；本地解算的屏心射线交点）。每渲染帧幂等调用，仅瞄准期有意义；
        /// hasPoint=false = 本帧无解算（无射线交点）→ 实现不得猜点。语义态进端口、用法归实现：
        /// 当前实现用它做瞄准机近距后撤（瞄准点越近，相机离角色越近），不改视线方向。
        /// </summary>
        void SetAimPoint(in Vector3 point, bool hasPoint);

        /// <summary>左/右肩切换（本地视角构图语义口）：实现按场景肩偏的镜像表达，不改
        /// 距离/俯仰/FOV；无肩偏配置时如实 no-op。左/右的具体宽度归场景配置单源。</summary>
        void ToggleShoulder();

        /// <summary>绝对视角角（弧度；yaw 环绕、pitch 按实现限位钳制）——自由观察回正/强制退出落位用；
        /// 避免用 AddLookDelta 反复逼近留下残差。</summary>
        void SetLookAngles(float yawRad, float pitchRad);

        /// <summary>
        /// 本地视角增量（第三人称自由视角）：把本帧输入源的视角增量（弧度）累进服务自持的相机 yaw/pitch 状态（yaw 环绕、pitch 按实现限位钳制）。
        /// </summary>
        void AddLookDelta(float yawDelta, float pitchDelta);

        /// <summary>当前视角 yaw（弧度，服务自持状态；诊断/测试读口）。未收到过增量 = 0。</summary>
        float LookYaw { get; }

        /// <summary>当前视角 pitch（弧度，正 = 上仰、服务自持状态、已按实现限位钳制；诊断/测试读口）。未收到过增量 = 0。</summary>
        float LookPitch { get; }

        /// <summary>重置跟随状态（解除控制/重开局/复活传送硬切时调：下一次 <see cref="Follow"/> 直接落位，
        /// 不从上一次位置阻尼飞过去——否则镜头追赶期间角色可能在画面外。</summary>
        void Reset();

        /// <summary>按需解析并确认相机就绪（场景可能刚切换；非 Cinemachine 实现可恒 true）。</summary>
        bool EnsureReady();

        /// <summary>释放本服务持有的对象（幂等；宿主/流程关闭时调）。</summary>
        void Shutdown();
    }
}
