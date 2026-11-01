using System;

namespace LiteFramework
{
    /// <summary>
    /// 驻留看门狗声明（可选接口，《状态机专项设计》§3.2）：当前阶段驻留帧数超限且无挂起时，
    /// 机器在 Tick 统一裁决并 <see cref="StageMachineCore{TId,TReq}.ForceState"/> 强制迁往 <see cref="TimeoutTarget"/>。
    /// 定位是**驻留异常升级**（流程卡死、AI 卡态）——编排级超时兜底（如 UI 转场收尾）归各自编排层，两者正交。
    /// </summary>
    public interface IStageTimeout<TId> where TId : struct
    {
        /// <summary>驻留上限（帧）；&lt;= 0 = 不启用看门狗。</summary>
        int TimeoutFrames { get; }

        /// <summary>超时目标（须已注册；超时触发时经 ForceState 通道，绕准入不绕重入/未注册检查）。</summary>
        TId TimeoutTarget { get; }
    }
}
