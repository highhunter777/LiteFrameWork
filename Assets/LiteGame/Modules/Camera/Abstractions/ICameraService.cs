using UnityEngine;

namespace LiteGame
{
    /// <summary>
    /// 相机服务端口（《商业级通用客户端框架总设计》§5 框图 `Platform.Unity` 适配器块的抽象面；
    /// 《联机战斗演示专项设计》§3"相机跟本地预测位置；和解期间跟衰减后的表现位置"）。
    ///
    /// **为什么是端口而不是直接用 Cinemachine**：表现层（<see cref="SimView"/>）只该知道
    /// "有个相机在跟这个表现位置"，不该认识 Cinemachine 这个包——同 <see cref="IVFXService"/> 的处置
    /// （表现层定端口、适配器实现、消费者只认端口）。换掉相机后端只换适配器，SimView 与流程零改动。
    ///
    /// **为什么住 `LiteView` 而不是 `LiteFramework.Core`**：本接口带 <c>UnityEngine.Vector3</c>，
    /// 而 Core 声明了 `noEngineReferences: true`（核心机制不依赖引擎，是硬约束）。表现层端口归表现层。
    ///
    /// 契约：
    /// - <see cref="Follow"/> 在**渲染帧**调用（与 SimView 的视图更新同拍），传本地玩家**表现位置**
    ///   （预测位置；和解期间是衰减后的位置——消费方决定，本端口不解释）；
    /// - 实现**只写相机**，不写 Sim、不读 Sim 状态（《状态同步专项设计》§1 原则 3）；
    /// - **相机构图（俯角/距离/FOV/阻尼/优先级）不在本接口表达**：那是产品表现决策，由
    ///   虚拟相机的场景/预制配置单源表达（《客户端总设计》§12.2 视觉单一来源）。实现不得用
    ///   代码再写一份档位常量，否则编辑器里调完发现"改了没用"；
    /// - <see cref="Shutdown"/> 释放本服务**自己创建**的对象（自建目标等），幂等；
    ///   场景/预制里已有的对象不归它处置（§6.2 所有权）。
    /// </summary>
    public interface ICameraService
    {
        /// <summary>是否已收到过焦点（首帧落位后为 true；诊断/测试用）。</summary>
        bool HasFocus { get; }

        /// <summary>当前焦点（表现空间；未跟随过 = <c>Vector3.zero</c>）。</summary>
        Vector3 Focus { get; }

        /// <summary>
        /// 跟随目标表现位姿（每渲染帧调用；<paramref name="deltaSeconds"/> = 真实帧间隔）。
        /// **只跟随位置**：第三人称自由视角的视线由 <see cref="AddLookDelta"/> 驱动，实现不把
        /// <paramref name="facing"/> 写进焦点旋转——否则角色转身会带动构图，和自由视角的轨道
        /// 事实源打架。保留 <paramref name="facing"/> 形参供未来视图消费方（如动画朝向），
        /// 当前相机实现如实不读它。
        /// </summary>
        void Follow(in Vector3 target, in Quaternion facing, float deltaSeconds);

        /// <summary>
        /// 瞄准态通知：本地玩家处于 ADS（右键瞄准）时传 true——期望**瞄准视角
        /// 接管**；松开回 false。**语义态进端口、接管方式归实现**（Cinemachine = 抬瞄准 vcam 优先级），
        /// 与构图同理不在接口表达任何档位常量。
        /// 每渲染帧幂等调用（实现内部按变化沿生效）；实现自备目标（如瞄准 vcam）的解析与还原——
        /// 瞄准视角缺配置（场景没有瞄准相机）时如实 no-op，不得替场景自建。
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

        /// <summary>
        /// 本地视角增量（第三人称自由视角）：把本帧输入源的视角增量（弧度）累进服务自持的相机
        /// yaw/pitch 状态（yaw 环绕、pitch 按实现限位钳制）。**pitch 正 = 上仰**（与 POV 垂直轴
        /// 的"正 = 向下"相反，符号换算归实现）。**纯视图侧**——视角不进意图三件、不进协议、不上行：
        /// 相机姿态是本地表现，Sim/判定零涉及。每渲染帧幂等调用（零增量 = 无操作）。
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
