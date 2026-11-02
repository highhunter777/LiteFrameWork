using System;

namespace LiteGame
{
    /// <summary>
    /// 视图组挂载接缝（**实现件内部面**，不进契约包）：把 <see cref="BattleContext"/> 挡在
    /// <see cref="IBattleViewGroup"/> 之外——流程只经端口取件/驱动/释放，挂载由战斗域内的会话调用。
    /// 契约包不得引用实现类型（规范 04 §6.8 R-D：依赖方向优先于归属）。
    /// </summary>
    internal interface IBattleViewAttachment
    {
        /// <summary>建视图并接线（须在 StartGame 之后、对局场景组就位之后调用）。</summary>
        void Attach(BattleContext context, Action openBattleMenu = null);
    }
}
