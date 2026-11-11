using System;
using LiteFramework;
using LiteSim;

namespace LiteGame.Modules.Battle.Abstractions.View
{
    /// <summary>
    /// 视图组工厂端口（装配根注入）：把"视图域依赖 + 每局 account 作用域/开发叠加层/传送分发"
    /// 收在战斗域装配点，流程只拿 <see cref="IBattleViewGroup"/>。
    /// </summary>
    public interface IBattleViewGroupFactory
    {
        /// <summary>最近一次创建的视图组（**仅测试/诊断用**：冒烟用例向下转型真装配件核对观测面）。
        /// 未创建或已释放（装饰器 Dispose）= null，读数不持有视图组本体。</summary>
        IBattleViewGroup LastCreated { get; }

        /// <param name="account">本局 Account 作用域（视图在其下建 BattleView 子作用域）。</param>
        /// <param name="teleportDispatch">传送分发（开发形态本地服宿主钩子；可空）。</param>
        /// <remarks>开发叠加层不在签名里：它是实现件依赖，由装配期 <c>Func&lt;IBattleDevOverlay&gt;</c> 注入工厂
        /// （契约包不引用表现层类型，规范 04 §6.8 R-D）。</remarks>
        IBattleViewGroup Create(ClientScope account, Action<SimVector3> teleportDispatch);
    }
}
