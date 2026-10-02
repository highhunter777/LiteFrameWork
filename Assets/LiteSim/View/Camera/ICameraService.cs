using UnityEngine;

namespace LiteSim.View
{
    /// <summary>
    /// 相机服务端口（《商业级通用客户端框架总设计》§5 框图 `Platform.Unity` 适配器块的抽象面；
    /// 《联机战斗演示专项设计》§3"相机跟本地预测位置；和解期间跟衰减后的表现位置"）。
    ///
    /// **为什么是端口而不是直接用 Cinemachine**：表现层（<see cref="SimView"/>）只该知道
    /// "有个相机在跟这个表现位置"，不该认识 Cinemachine 这个包——同 <see cref="IVFXService"/> 的处置
    /// （表现层定端口、适配器实现、消费者只认端口）。换掉相机后端只换适配器，SimView 与流程零改动。
    ///
    /// **为什么住 `LiteSim.View` 而不是 `LiteFramework.Core`**：本接口带 <c>UnityEngine.Vector3</c>，
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
        /// <paramref name="facing"/> = 本地玩家**朝向**（视觉前沿为 +Z 的那份旋转——SimView 摆位约定）：
        /// 实现把它写到跟随焦点上，**构图偏移**（如 Cinemachine FramingTransposer 的
        /// TrackedObjectOffset——其语义是"随 Follow 目标旋转"）随之变**角色系**（X=右肩、Z=前方）。
        /// 背景（2026-10-02 用户裁决"焦点带旋转"）：此前焦点只写位置、旋转恒 identity，
        /// offset 实际是世界系固定——玩家转向后构图点绕角色乱转。实现可只对**自建焦点**应用旋转
        /// （场景配置的 Follow 目标自带姿态，不越权覆写）。
        /// </summary>
        void Follow(in Vector3 target, in Quaternion facing, float deltaSeconds);

        /// <summary>
        /// 瞄准态通知（2026-10-02 瞄准相机批）：本地玩家处于 ADS（右键瞄准）时传 true——期望**瞄准视角
        /// 接管**；松开回 false。**语义态进端口、接管方式归实现**（Cinemachine = 抬瞄准 vcam 优先级），
        /// 与构图同理不在接口表达任何档位常量。
        /// 每渲染帧幂等调用（实现内部按变化沿生效）；实现自备目标（如瞄准 vcam）的解析与还原——
        /// 瞄准视角缺配置（场景没有瞄准相机）时如实 no-op，不得替场景自建。
        /// </summary>
        void SetAiming(bool aiming);

        /// <summary>重置跟随状态（解除控制/重开局时调：下一帧重新落位，不从上一次位置飞过去）。</summary>
        void Reset();

        /// <summary>释放本服务持有的对象（幂等；宿主/流程关闭时调）。</summary>
        void Shutdown();
    }
}
