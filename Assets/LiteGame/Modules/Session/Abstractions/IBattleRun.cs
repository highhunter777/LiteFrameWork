﻿using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteNet.Proto;
using LiteSim;
using LiteGame.Modules.Input;

namespace LiteGame
{
    /// <summary>对局结束原因（流程段消费的端口事实；实现映射自战斗上下文）。</summary>
    public enum BattleRunOutcome
    {
        /// <summary>玩家主动离场。</summary>
        Leave = 0,
        /// <summary>会话失败（重连超时/被拒/版本不符）。</summary>
        SessionFailed,
        /// <summary>正常结束（冻结结果在）。</summary>
        Completed,
        /// <summary>未开局即散场（无结果可结算）。</summary>
        Aborted,
    }

    /// <summary>
    /// 对局运行端口（流程段唯一认识的"对局"）：StartGame 等待、结束事件、终局原因/结果/席位、
    /// 每帧驱动与离场。实现归会话域（<c>BattleSession</c>）——流程不再直引
    /// <c>BattleClient</c>/<c>BattleContext</c>/<c>BattleContext.EndReason</c>/<c>RoomSessionFace</c>
    /// 这些战斗域内部件。
    /// </summary>
    public interface IBattleRun : IDisposable
    {
        /// <summary>本局地图行键（JoinAck 权威事实；场景组加载与判定图同行键）。</summary>
        int MapId { get; }

        /// <summary>StartGame 是否已到达（重入/迟到等待用）。</summary>
        bool HasStarted { get; }

        /// <summary>解读世界是否已建立（StartGame 成功且 Sim 就绪）。</summary>
        bool HasSimulation { get; }

        /// <summary>终局原因（未结束 = null）；流程只从这里读终局事实。</summary>
        BattleRunOutcome? Outcome { get; }

        /// <summary>冻结的终局结果（结算页数据源；未结束 = null）。</summary>
        MatchEnded MatchResult { get; }

        /// <summary>本地席位（-1 = 未分配）。</summary>
        int PlayerId { get; }

        /// <summary>最近一次收到的快照帧号（诊断/日志）。</summary>
        long ReconcileCount { get; }

        /// <summary>本局规则实例（null = 常规房）。</summary>
        MatchTestRules Rules { get; }

        /// <summary>表现视图域（Battle 阶段创建后挂入；null = 尚未建立/已拆除）。</summary>
        IBattleViewGroup View { get; }

        /// <summary>房间会话门面（页面经 Bridge.room 读房主事实/发提前开局）。</summary>
        IRoomSessionFace RoomSession { get; }

        /// <summary>对局结束（恰好一次；Dispose 不触发）。</summary>
        event Action<BattleRunOutcome> Ended;

        /// <summary>等 StartGame 或对局终止（先到先得；true = 已开局）。</summary>
        UniTask<bool> WaitStartedOrEndedAsync(CancellationToken ct);

        /// <summary>挂入视图域（生命周期随本端口；拆除经 <see cref="DetachView"/>）。</summary>
        void AttachView(IBattleViewGroup view);

        /// <summary>建视图并接线（须在 StartGame 之后、对局场景组就位之后调用）。</summary>
        void ActivateView(Action openBattleMenu = null);

        /// <summary>拆视图域（幂等；消费者/视图/租约作用域由视图组自身逆序完成）。</summary>
        void DetachView();

        /// <summary>每帧驱动：瞄准世界注入 → 输入采样 → 对局上下文推进（唯一驱动入口）。</summary>
        void Tick(float deltaSeconds, IInputService input);

        /// <summary>主动离场（幂等；经 <see cref="Ended"/> 收尾）。</summary>
        void Leave();
    }
}
