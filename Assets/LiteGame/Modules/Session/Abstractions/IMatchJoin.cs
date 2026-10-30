using System;
using LiteNet.Proto;

namespace LiteGame
{
    /// <summary>
    /// 进房连接端口（流程段唯一认识的"连接"）：JoinAck 等待与传输泵。
    /// 实现归会话域（<c>AccountSession</c>）——流程不再直引 <c>BattleClient</c>。
    /// </summary>
    public interface IMatchJoin
    {
        /// <summary>JoinAck 到达（一次性等待；临时订阅由等待方退订）。</summary>
        event Action<JoinAck> Joined;

        /// <summary>推进接收泵（KCP 握手与收包）。</summary>
        void TickIncoming();

        /// <summary>推进发送泵（KCP 发包）。</summary>
        void TickOutgoing();
    }
}
