using System;
using LiteNet.Proto;

namespace LiteGame
{
    /// <summary>BattleClient → 会话端口适配（四透传 + 事件透传；装配由 ProcedureBattle 完成）。</summary>
    public sealed class RoomSessionFace : IRoomSessionFace
    {
        private readonly BattleClient _battle;

        public RoomSessionFace(BattleClient battle)
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