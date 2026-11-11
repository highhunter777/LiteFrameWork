using System;
using LiteNet.Proto;

using LiteSync.Client;
using LiteGame.Modules.Battle.Abstractions.Run;
using LiteGame.Modules.Battle.Abstractions.Session;
namespace LiteGame
{
    /// <summary>会话客户端端口 → 房间会话端口适配（四透传 + 事件透传；由 BattleSession 装配）。
    /// 只经 <see cref="IBattleClient"/> 端口读写——不认具体客户端实现（A1 解耦）。</summary>
    public sealed class RoomSessionFace : IRoomSessionFace
    {
        private readonly IBattleClient _battle;

        public RoomSessionFace(IBattleClient battle)
            => _battle = battle ?? throw new ArgumentNullException(nameof(battle));

        public bool IsOwner => _battle.IsOwner;

        public int ExpectedPlayers => _battle.Client.ExpectedPlayers;

        public RoomSeats Seats => _battle.Client.Seats;

        public event Action<StartMatchResponse> StartMatchDenied
        {
            add => _battle.Client.OnStartMatchResponse += value;
            remove => _battle.Client.OnStartMatchResponse -= value;
        }

        public event Action<OwnerChanged> OwnerChanged
        {
            add => _battle.Client.OnOwnerChanged += value;
            remove => _battle.Client.OnOwnerChanged -= value;
        }

        public event Action<RoomSeats> RoomSeatsChanged
        {
            add => _battle.Client.OnRoomSeats += value;
            remove => _battle.Client.OnRoomSeats -= value;
        }

        public void SendStartMatchRequest() => _battle.SendStartMatchRequest();
    }
}