using System;

namespace LiteFramework
{
    /// <summary>
    /// 阶段宿主：`IStage` 的钩子只认这个**窄接口**，因此同一份阶段实现可以挂在
    /// **平面机**（<see cref="StageMachine{TId,TReq}"/>）与**层级机**（<see cref="HierarchicalStageMachine{TId,TReq}"/>）上。
    ///
    /// 只暴露阶段真正需要的能力（发起请求 + 读当前态）；**机器专属能力不进本接口**：
    /// 层级机的 `ActivePath` / `Raise` / `Interrupted` 等，由阶段自行
    /// `if (m is HierarchicalStageMachine&lt;TId,TReq&gt; hsm) { ... }` 取用。
    /// </summary>
    public interface IStageHost<TId, TReq> where TId : struct
    {
        /// <summary>是否已 Start（平面机：`_current != null`；层级机：活动路径非空）。</summary>
        bool Started { get; }

        /// <summary>当前阶段（平面机：当前态；层级机：**最深活动态**）。</summary>
        TId Current { get; }

        /// <summary>当前阶段已持续时长（秒；迁移后归零）。</summary>
        float StageTime { get; }

        /// <summary>当前阶段驻留的**整数帧数**（每次 `Tick` +1，迁移后归零）——表驱动状态的帧窗口判据（如前 N 帧可取消）。</summary>
        int StageFrames { get; }

        /// <summary>发起迁移请求（只入队，last-wins；平面机 = 一次迁移，层级机 = 一次迁移事务）。
        /// 返回 **false** = 被优先级/中断规则挡下（不改变挂起）；层级机恒为 true。</summary>
        bool Request(TId nextId, in TReq req);

        /// <summary>无 payload 的迁移请求。</summary>
        bool Request(TId nextId);
    }
}
