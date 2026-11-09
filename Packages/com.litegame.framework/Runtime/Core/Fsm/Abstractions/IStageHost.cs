using System;

namespace LiteFramework
{
    /// <summary>
    /// 阶段宿主：阶段钩子经此接口查询活动态并请求迁移，可用于平面机与层级机。
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

        /// <summary>发起迁移请求（只入队，last-wins）；通过守卫后返回 true。</summary>
        bool Request(TId nextId, in TReq req);

        /// <summary>无 payload 的迁移请求。</summary>
        bool Request(TId nextId);
    }
}
