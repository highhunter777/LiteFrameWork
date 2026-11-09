using System;
using LiteFramework;
using LiteSim;

namespace LiteGame
{
    /// <summary>
    /// Match 运行时端口（Battle 对会话材料的**唯一消费口**）：对局运行只从这里取 Match 资源域、 Match 服务子域、会话客户端、本局规则与地图委托——不认 Match 域实现件（MatchSession）。
    /// </summary>
    public interface IMatchRuntime
    {
        /// <summary>本局 Match 资源域（BattleContext 在其下建 Battle 子作用域；Dispose 归会话持有方）。</summary>
        ClientScope Scope { get; }

        /// <summary>建 Match 服务子域（**未 Seal**——调用方注册后 Seal）。这是 Battle 唯一的容器入口。</summary>
        ServiceContainer CreateMatchServices();

        /// <summary>本局会话客户端端口（BattleContext 的事件/泵/相位来源）。</summary>
        IBattleClient Client { get; }

        /// <summary>本局规则持有件（与权威侧同实例；常规房 = null）。</summary>
        MatchTestRules Rules { get; }

        /// <summary>地图提供委托（行键 → 判定图；装配根从 Map 域投影，LiteSim 类型过端口）。</summary>
        Func<int, SimMapData> Maps { get; }

        /// <summary>地图行键裁决委托（JoinAck 的 MapId → 表行键；装配根绑单源裁决）。</summary>
        Func<int, int> ResolveMapId { get; }
    }
}
