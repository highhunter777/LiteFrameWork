using System;
using LiteNet.Proto;

namespace LiteGame
{
    /// <summary>房间会话端口（产品模块域——Lua 桥与流程只认本口；由 App 侧适配
    /// <see cref="BattleClient"/>，测试注 fake 即可直测门面语义）。</summary>
    public interface IRoomSessionFace
    {
        /// <summary>本机是否房主（协议派生态——RoomClient 单源）。</summary>
        bool IsOwner { get; }

        /// <summary>本房期望人数（协议派生态——JoinAck 定值的房事实；0 = 服务器未标注，
        /// 消费方按默认 2 座回落，与 <c>BattleContext</c> 的定容口径同源）。</summary>
        int ExpectedPlayers { get; }

        /// <summary>席位实时面最新快照（协议缓存单源——RoomClient；未收到前为 null）。</summary>
        RoomSeats Seats { get; }

        /// <summary>提前开局被拒反馈（成功路径以 OnStartGame 广播为准——本事件只在被拒时到达，RoomClient 契约）。</summary>
        event Action<StartMatchResponse> StartMatchDenied;

        /// <summary>房主迁移通知（载荷＝新房主席位；事实已重算——订阅方拉取 IsOwner 取新值）。</summary>
        event Action<OwnerChanged> OwnerChanged;

        /// <summary>席位实时面广播（全量快照——进房/断线/恢复完成时到达）。</summary>
        event Action<RoomSeats> RoomSeatsChanged;

        /// <summary>发送提前开局请求（相位守卫在 RoomClient——未入房 no-op）。</summary>
        void SendStartMatchRequest();
    }
}