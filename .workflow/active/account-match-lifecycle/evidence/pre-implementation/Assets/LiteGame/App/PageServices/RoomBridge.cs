using System;
using System.Collections.Generic;
using LiteFramework;
using LiteGame.Modules.UI;
using LiteNet.Proto;

using LiteSync.Client;
namespace LiteGame
{
    /// <summary>
    /// 房间会话门面（<see cref="IRoomSessionBridge"/> ＋ 页面端口 <see cref="IRoomView"/>）：
    /// 装订/摘除会话、回放房主与席位事实、转发提前开局。
    /// </summary>
    public sealed class RoomBridge : IRoomSessionBridge, IRoomView
    {
        private IRoomSessionFace _session;
        private IReadOnlyList<RoomSeat> _seats = Array.Empty<RoomSeat>();

        /// <summary>房主事实变化（页面订阅面）。</summary>
        public event Action<bool> OwnerChanged;
        /// <summary>席位快照变化（页面订阅面；装订即回放当前快照）。</summary>
        public event Action<IReadOnlyList<RoomSeat>> SeatsChanged;
        /// <summary>提前开局被拒（成功不回调——OnStartGame 流程接管）。</summary>
        public event Action<string> StartMatchDenied;

        /// <summary>席位快照（未装订 = 空）。</summary>
        public IReadOnlyList<RoomSeat> Seats => _seats;

        /// <summary>当前房主事实（未绑会话 = false——页面默认隐藏开钮）。</summary>
        public bool IsOwner => _session != null && _session.IsOwner;

        /// <summary>本房期望人数（未绑会话 = 0——消费方按"未标注"处理：不显假数）。</summary>
        public int ExpectedPlayers => _session != null ? _session.ExpectedPlayers : 0;

        /// <summary>会话装订（Battle 阶段进——ProcedureBattle 经端口调）。幂等：重复装订先摘旧。</summary>
        public void BindSession(IRoomSessionFace session)
        {
            UnbindSession();
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _session.StartMatchDenied += OnStartMatchDenied;
            _session.OwnerChanged += OnOwnerChangedInternal;
            _session.RoomSeatsChanged += OnRoomSeatsInternal;
            Raise(OwnerChanged, _session.IsOwner);      // 装订即回放（JoinAck 先到的序）
            PublishSeats(_session.Seats);
        }

        /// <summary>会话摘除（Battle 阶段收尾必调——离场/失败后页面订阅不得命中死会话）。</summary>
        public void UnbindSession()
        {
            if (_session == null) return;
            _session.StartMatchDenied -= OnStartMatchDenied;
            _session.OwnerChanged -= OnOwnerChangedInternal;
            _session.RoomSeatsChanged -= OnRoomSeatsInternal;
            _session = null;
            _seats = Array.Empty<RoomSeat>();
        }

        /// <summary>提前开局请求（房主按钮点击）；被拒走 <see cref="StartMatchDenied"/>，成功不回调。</summary>
        public void RequestStartMatch()
        {
            if (_session == null)
            {
                Raise(StartMatchDenied, "not-in-room");   // 迟到点击（离场后页面残余回调）——显性降级不悬挂
                return;
            }
            _session.SendStartMatchRequest();
        }

        private void OnOwnerChangedInternal(OwnerChanged change) => Raise(OwnerChanged, IsOwner);

        private void OnRoomSeatsInternal(RoomSeats seats) => PublishSeats(seats);

        private void OnStartMatchDenied(StartMatchResponse response)
            => Raise(StartMatchDenied, response?.Reason ?? "denied");

        /// <summary>席位快照投影（页面读口唯一来源）。</summary>
        private void PublishSeats(RoomSeats seats)
        {
            var list = new List<RoomSeat>(seats?.Seats != null ? seats.Seats.Count : 0);
            if (seats?.Seats != null)
            {
                for (int i = 0; i < seats.Seats.Count; i++)
                {
                    SeatInfo seat = seats.Seats[i];
                    list.Add(new RoomSeat(seat.PlayerId, seat.DisplayName, seat.Connected));
                }
            }
            _seats = list;
            Raise(SeatsChanged, _seats);
        }

        /// <summary>订阅者异常隔离（页面回调抛错不得打断会话事件链）。</summary>
        private static void Raise<T>(Action<T> handler, T value)
        {
            if (handler == null) return;
            try { handler(value); }
            catch (Exception ex) { Log.Error("RoomBridge 订阅者异常:" + ex.Message, LogTag.UI); }
        }
    }
}
