using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFrameworkNext;
using LiteGameNext.Features.Battle.Hud;
using LiteGameNext.Features.Battle.Input;
using LiteGameNext.Features.Battle.World;
using LiteGameNext.Features.Matchmaking;
using LiteNet.Transport;
using LiteSim;
using LiteSync.Client;
using UnityEngine;

namespace LiteGameNext.Features.Battle
{
    /// <summary>真实房间会话：KCP/安全信封 → RoomClient → Join/Start/MatchEnded；P2b 先接网络边界，Sim/View 后续。</summary>
    public sealed class RoomSession : IDisposable
    {
        public const int JoinTimeoutMs = 10_000;

        private readonly RoomClient _client;
        private readonly IClientTransport _transport;
        private readonly string _roomId;
        private readonly string _ticket;
        private readonly string _buildHash;
        private readonly BattleHudModel _hud;
        private readonly BattleWorldView _world;
        private IBattleInput _input;
        private readonly bool _autoStart;
        private readonly Dictionary<long, Vector3> _positions = new Dictionary<long, Vector3>();
        private readonly UniTaskCompletionSource<BattleResult> _completion =
            new UniTaskCompletionSource<BattleResult>();
        private bool _disposed;
        private bool _started;
        private int _frame;
        private int _lastInputUnityFrame = -1;
        private long _localEntityId;

        public RoomSession(string host, int port, string roomId, string ticket, string buildHash,
            IClientTransport transport = null, BattleHudModel hud = null, BattleWorldView world = null,
            IBattleInput input = null, bool autoStart = true)
        {
            if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("房间主机必填", nameof(host));
            if (port <= 0 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
            _roomId = roomId ?? string.Empty;
            _ticket = ticket ?? string.Empty;
            _buildHash = buildHash ?? string.Empty;
            _hud = hud;
            _world = world;
            _input = input;
            _autoStart = autoStart;
            _transport = transport ?? new SecureEnvelopeClientTransport(
                new KcpTransportClient(), SecureEnvelopeOptions.FromEnvironment());
            _client = new RoomClient(_transport);

            _transport.OnConnected += OnTransportConnected;
            _client.OnJoinAck += OnJoinAck;
            _client.OnStartGame += OnStartGame;
            _client.OnSnapshot += OnSnapshot;
            _client.OnMatchEnded += OnMatchEnded;
            _client.OnPhaseChanged += OnPhaseChanged;
            _client.OnDisconnected += OnDisconnected;
            _client.Connect(host, port);
        }

        public string Status { get; private set; } = "连接中…";

        public bool Connected => _client.Connected;

        public bool IsJoined { get; private set; }

        public int ExpectedPlayers { get; private set; }

        public long LocalEntityId => _localEntityId;

        public int PlayerId => _client.PlayerId;

        public bool TryGetEntityPosition(long entityId, out Vector3 position)
            => _positions.TryGetValue(entityId, out position);

        /// <summary>替换输入源（房间页/测试在开局前后切换）；null = 停止上行。</summary>
        public void SetInput(IBattleInput input) => _input = input;

        /// <summary>房主开局（测试/房间页按钮）；非房主 no-op。</summary>
        public void StartMatch()
        {
            if (_client.IsOwner) _client.SendStartMatchRequest();
        }

        /// <summary>跑会话直到 MatchEnded/失败/取消；房间页关闭或超时也返回失败结果。</summary>
        public async UniTask<BattleResult> RunAsync(CancellationToken cancellationToken)
        {
            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            PumpAsync(linked.Token).Forget(ex => Log.Error("房间泵失败：" + ex));
            try
            {
                UniTask timeout = UniTask.Delay(JoinTimeoutMs, DelayType.Realtime,
                    cancellationToken: linked.Token);
                (bool hasResult, BattleResult result) = await UniTask.WhenAny(_completion.Task, timeout);
                return hasResult ? result : BattleResult.Failed("加入房间超时");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            finally
            {
                linked.Cancel();
                Dispose();
            }
        }

        private async UniTask PumpAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                _client.TickIncoming();
                _client.TickOutgoing();
                if (_started && _input != null && _localEntityId != 0
                    && Time.frameCount != _lastInputUnityFrame)
                {
                    _lastInputUnityFrame = Time.frameCount;
                    _frame = Math.Max(_frame + 1, _client.LastSnapshotFrame + 1);
                    BattleInputState state = _input.Read();
                    _client.SendInput(_frame, BattleInputFrame.Build(state, _localEntityId),
                        Math.Max(0, _client.LastSnapshotFrame));
                }

                await UniTask.Yield();
            }
        }

        private void OnTransportConnected()
        {
            if (_client.Phase == ClientSessionPhase.Idle)
            {
                Status = "发送进房…";
                _client.SendJoin(_roomId, _ticket, _buildHash);
            }
        }

        private void OnJoinAck(LiteNet.Proto.JoinAck ack)
        {
            IsJoined = true;
            ExpectedPlayers = ack.ExpectedPlayers;
            Status = "已进房，等待开局…";
            if (_autoStart && _client.IsOwner
                && (ack.ExpectedPlayers <= 1 || ack.Members.Count >= ack.ExpectedPlayers))
                _client.SendStartMatchRequest();
        }

        private void OnStartGame(LiteNet.Proto.StartGame start)
        {
            Status = "对局进行中…";
            _frame = start.Frame;
            _started = true;
        }

        /// <summary>私有状态里的 HP/弹匣喂 HUD；只做展示，不参与判定。</summary>
        private void OnSnapshot(LiteNet.Proto.StateSnapshot snapshot)
        {
            _world?.ApplySnapshot(snapshot);
            if (snapshot.PrivateState == null) return;
            _localEntityId = snapshot.PrivateState.EntityId;
            for (int index = 0; index < snapshot.Slots.Count; index++)
            {
                LiteNet.Proto.SlotDelta slot = snapshot.Slots[index];
                _positions[slot.Id] = new Vector3(slot.PosX, slot.PosY, slot.PosZ);
                if (slot.Id == _localEntityId && _hud != null) _hud.SetScore(slot.Kills, slot.Deaths);
            }

            if (_hud == null) return;
            int health = snapshot.PrivateState.Resource;
            int ammo = snapshot.PrivateState.Weapons.Count > 0
                ? snapshot.PrivateState.Weapons[0].MagAmmo
                : 0;
            _hud.SetVitals(health, Math.Max(100, health), ammo);
        }

        private void OnMatchEnded(LiteNet.Proto.MatchEnded ended)
        {
            Status = "对局结束";
            if (_hud != null)
            {
                int playerId = _client.PlayerId;
                for (int index = 0; index < ended.Players.Count; index++)
                {
                    LiteNet.Proto.PlayerMatchResult row = ended.Players[index];
                    if (row.PlayerId != playerId) continue;
                    _hud.SetScore(row.Kills, row.Deaths);
                    break;
                }
            }

            _completion.TrySetResult(BattleResult.Completed(_client.PlayerId.ToString()));
        }

        private void OnPhaseChanged(ClientSessionPhase from, ClientSessionPhase to)
        {
            if (to == ClientSessionPhase.Failed)
                _completion.TrySetResult(BattleResult.Failed("房间会话失败"));
        }

        private void OnDisconnected()
        {
            Status = "连接断开";
            _completion.TrySetResult(BattleResult.Failed("连接断开"));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _transport.OnConnected -= OnTransportConnected;
            _client.OnJoinAck -= OnJoinAck;
            _client.OnStartGame -= OnStartGame;
            _client.OnSnapshot -= OnSnapshot;
            _client.OnMatchEnded -= OnMatchEnded;
            _client.OnPhaseChanged -= OnPhaseChanged;
            _client.OnDisconnected -= OnDisconnected;
            _client.Dispose();
            _transport.Dispose();
        }
    }
}
