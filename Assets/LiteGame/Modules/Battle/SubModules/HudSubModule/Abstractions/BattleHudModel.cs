using System;
using LiteNet;
using LiteSim;
using LiteGame.Modules.Battle.Abstractions.Read;
using LiteGame.Modules.Battle.Shared;

namespace LiteGame.Modules.Battle.SubModules.HudSubModule.Abstractions
{
    /// <summary>
    /// 战斗 HUD 族只读数据模型：把装配点构造的读模型、公共事件面、质量探针与本地 Id 绑定口收成一处。
    /// **不是 ViewModel**——没有可观察属性/绑定/命令；驱动每帧拉取事实并显式写视图。
    /// </summary>
    public sealed class BattleHudModel
    {
        /// <summary>本地预测态读源（HUD 面板/击杀提示）。</summary>
        public IBattleReadModel Predicted { get; }

        /// <summary>公共事实读源（权威镜像优先；排行榜）。</summary>
        public IBattleReadModel Public { get; }

        /// <summary>阵亡/复活读源（公共事实 + 本地阵亡锁存）。</summary>
        public IBattleReadModel Respawn { get; }

        /// <summary>本地实体 Id（视图未建时为 0）。</summary>
        public Func<long> LocalEntityId { get; }

        /// <summary>网络质量探针（会话未接时 None）。</summary>
        public Func<NetworkQuality> Quality { get; }

        /// <summary>公共战斗事件面（击杀队列；可空 = 无源）。</summary>
        public PublicCombatFeed Feed { get; }

        public BattleHudModel(IBattleReadModel predicted, IBattleReadModel publicFacts, IBattleReadModel respawn,
            Func<long> localEntityId, Func<NetworkQuality> quality, PublicCombatFeed feed)
        {
            Predicted = predicted ?? throw new ArgumentNullException(nameof(predicted));
            Public = publicFacts ?? throw new ArgumentNullException(nameof(publicFacts));
            Respawn = respawn ?? throw new ArgumentNullException(nameof(respawn));
            LocalEntityId = localEntityId ?? throw new ArgumentNullException(nameof(localEntityId));
            Quality = quality ?? (() => NetworkQuality.None);
            Feed = feed;
        }
    }
}
