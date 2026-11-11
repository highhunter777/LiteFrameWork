using System;
using LiteGame.Modules.Battle.Shared;

namespace LiteGame.Modules.Battle
{
    /// <summary>
    /// 视图组挂载接缝（**实现件内部面**，不进契约包）：把 BattleContext 挡在 IBattleViewGroup 之外——流程只经端口取件/驱动/释放，挂载由战斗域内的会话调用。
    /// </summary>
    internal interface IBattleViewAttachment
    {
        /// <summary>建视图并接线（须在 StartGame 之后、对局场景组就位之后调用）。</summary>
        void Attach(BattleContext context, Action openBattleMenu = null);
    }
}
