using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;

namespace LiteGame
{
    /// <summary>
    /// 对局视图组端口（Battle 阶段唯一视图入口）：资源取件 → 建视图/接线 → 每帧驱动 → 逆序拆除。
    /// 实现归战斗域（`Setup/BattleViewRig` 及装配件）；流程只持本口，不持有具体装配件类型。
    /// </summary>
    public interface IBattleViewGroup : IDisposable
    {
        /// <summary>取全部对局表现资源（逐项降级——缺美术不当对局失败）。</summary>
        UniTask LoadAsync(CancellationToken ct);

        /// <summary>建视图并接线（须在 StartGame 之后、对局场景组就位之后调用）。</summary>
        void Attach(BattleContext context, Action openBattleMenu = null);

        /// <summary>每帧驱动视图消费者（<see cref="BattleContext.Tick"/> 之后调用）。</summary>
        void Tick(float elapseSeconds);

        /// <summary>渲染帧采样之前的诊断件驱动（编辑器专用；发布空实现）。</summary>
        void TickPreSample();
    }
}