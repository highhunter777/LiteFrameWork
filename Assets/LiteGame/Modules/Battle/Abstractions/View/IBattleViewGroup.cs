using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;

namespace LiteGame.Modules.Battle.Abstractions.View
{
    /// <summary>
    /// 对局视图组端口（Battle 阶段唯一视图入口）：资源取件 → 建视图/接线 → 每帧驱动 → 逆序拆除。
    /// </summary>
    public interface IBattleViewGroup : IDisposable
    {
        /// <summary>取全部对局表现资源（逐项降级——缺美术不当对局失败）。</summary>
        UniTask LoadAsync(CancellationToken ct);

        /// <summary>每帧驱动视图消费者（主循环 Tick 之后调用）。</summary>
        void Tick(float elapseSeconds);

        /// <summary>渲染帧采样之前的诊断件驱动（编辑器专用；发布空实现）。</summary>
        void TickPreSample();
    }
}