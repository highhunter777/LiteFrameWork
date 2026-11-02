using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;

namespace LiteGame
{
    /// <summary>
    /// 对局视图组端口（Battle 阶段唯一视图入口）：资源取件 → 建视图/接线 → 每帧驱动 → 逆序拆除。
    /// 实现归战斗域（`Setup/BattleViewRig` 及装配件）；流程只持本口，不持有具体装配件类型。
    /// **挂载不进本口**：`Attach(BattleContext)` 住实现件内部接缝 <see cref="IBattleViewAttachment"/>——
    /// 契约包不得引用域内实现类型（规范 04 §6.8 R-D）。
    /// </summary>
    public interface IBattleViewGroup : IDisposable
    {
        /// <summary>取全部对局表现资源（逐项降级——缺美术不当对局失败）。</summary>
        UniTask LoadAsync(CancellationToken ct);

        /// <summary>每帧驱动视图消费者（<see cref="BattleContext.Tick"/> 之后调用）。</summary>
        void Tick(float elapseSeconds);

        /// <summary>渲染帧采样之前的诊断件驱动（编辑器专用；发布空实现）。</summary>
        void TickPreSample();
    }
}