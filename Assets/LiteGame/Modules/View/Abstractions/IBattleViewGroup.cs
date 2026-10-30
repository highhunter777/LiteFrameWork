using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteSim;

namespace LiteGame
{
    /// <summary>
    /// 对局视图组端口（Battle 阶段唯一视图入口）：资源取件 → 建视图/接线 → 每帧驱动 → 逆序拆除。
    /// 实现归产品表现层（Presentation 的视图装配件）；流程只持本口，不持有具体装配件类型。
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

    /// <summary>
    /// 视图组工厂端口（装配根注入）：把"视图域依赖 + 每局 account 作用域/开发叠加层/传送分发"
    /// 收在表现层装配点，流程只拿 <see cref="IBattleViewGroup"/>。
    /// </summary>
    public interface IBattleViewGroupFactory
    {
        /// <param name="account">本局 Account 作用域（视图在其下建 BattleView 子作用域）。</param>
        /// <param name="devOverlay">本局开发叠加层（发布形态 null = 零开销）。</param>
        /// <param name="teleportDispatch">传送分发（开发形态本地服宿主钩子；可空）。</param>
        IBattleViewGroup Create(ClientScope account, IBattleDevOverlay devOverlay,
            Action<SimVector3> teleportDispatch);
    }
}