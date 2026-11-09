using System;
using System.Collections.Generic;

namespace LiteGame.Modules.UI
{
    /// <summary>房间席位（页面投影：席位号/名字/在线）。</summary>
    public readonly struct RoomSeat
    {
        public readonly long PlayerId;
        public readonly string Name;
        public readonly bool Online;

        public RoomSeat(long playerId, string name, bool online)
        {
            PlayerId = playerId;
            Name = name ?? string.Empty;
            Online = online;
        }
    }

    /// <summary>
    /// 房间会话的页面读口：快照 + 事件（订阅即回放）。未装订 = 无房主/房规模 0/空名单。
    /// 实现在适配层（随会话装订/摘除），页面关闭必须退订。
    /// </summary>
    public interface IRoomView
    {
        bool IsOwner { get; }
        int ExpectedPlayers { get; }
        IReadOnlyList<RoomSeat> Seats { get; }

        event Action<bool> OwnerChanged;
        event Action<IReadOnlyList<RoomSeat>> SeatsChanged;
        event Action<string> StartMatchDenied;

        void RequestStartMatch();
    }
}
