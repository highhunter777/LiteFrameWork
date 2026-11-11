using System;
using LiteNet;
using LiteSync.Client;

namespace LiteGame.Modules.Battle.Abstractions.Run
{
    /// <summary>
    /// 对局会话客户端端口（Battle 消费面）：BattleContext/BattleInputPipeline/ 房间会话门面只经本口驱动与观测会话——具体实现（KCP 传输 + RoomClient 相位机）归 Bat。
    /// </summary>
    public interface IBattleClient : IDisposable
    {
        /// <summary>RoomClient 会话面（事件订阅/相位观测的直通道；过渡逃生口，见类型注释）。</summary>
        RoomClient Client { get; }

        /// <summary>会话相位（Idle/Connected/SuspectedLost/Reconnecting/Restoring/Failed）。</summary>
        ClientSessionPhase Phase { get; }

        /// <summary>传输连接是否存活。</summary>
        bool Connected { get; }

        /// <summary>网络质量观测（RTT/丢包——HUD 展示面，不参与判定）。</summary>
        NetworkQuality Quality { get; }

        /// <summary>本机是否房主（开房裁决 + 迁移续批）。</summary>
        bool IsOwner { get; }

        /// <summary>泵驱动：接收（含重连超时巡检）。</summary>
        void TickIncoming();

        /// <summary>泵驱动：发送。</summary>
        void TickOutgoing();

        /// <summary>房主提前开局请求（相位守卫/被拒反馈/成功广播归实现）。</summary>
        void SendStartMatchRequest();

        /// <summary>发起重连（无票据/相位不符返回 false——Failed 终态唯一出路是重建会话）。</summary>
        bool BeginReconnect();

        /// <summary>主动断开（离场路径）。</summary>
        void Disconnect();
    }
}
