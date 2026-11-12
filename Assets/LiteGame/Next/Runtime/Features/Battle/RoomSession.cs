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
        private readonly BattleAudioDriver _audioDriver;
        private readonly DamageNumberAggregator _damageNumbers;
        private readonly uint? _expectedConfigHash;
        private IBattleInput _input;
        private readonly bool _autoStart;
        private readonly Dictionary<long, Vector3> _positions = new Dictionary<long, Vector3>();
        private readonly UniTaskCompletionSource<BattleResult> _completion =
            new UniTaskCompletionSource<BattleResult>();
        private readonly UniTaskCompletionSource<bool> _joined = new UniTaskCompletionSource<bool>();
        private bool _disposed;
        private bool _started;
        private int _frame;
        private int _lastClockFrame = -1;
        private BattleFrameClock _clock;
        private long _localEntityId;

        public RoomSession(string host, int port, string roomId, string ticket, string buildHash,
            IClientTransport transport = null, BattleHudModel hud = null, BattleWorldView world = null,
            IBattleInput input = null, bool autoStart = true,
            BattleAudioDriver audioDriver = null, DamageNumberAggregator damageNumbers = null,
            uint? expectedConfigHash = null)
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
            _audioDriver = audioDriver;
            _damageNumbers = damageNumbers;
            _expectedConfigHash = expectedConfigHash;
            _transport = transport ?? new SecureEnvelopeClientTransport(
                new KcpTransportClient(), SecureEnvelopeOptions.FromEnvironment());
            _client = new RoomClient(_transport);

            _transport.OnConnected += OnTransportConnected;
            _client.OnJoinAck += OnJoinAck;
            _client.OnStartGame += OnStartGame;
            _client.OnSnapshot += OnSnapshot;
            _client.OnCombatEvents += OnCombatEvents;
            _client.OnMatchEnded += OnMatchEnded;
            _client.OnPhaseChanged += OnPhaseChanged;
            _client.OnDisconnected += OnDisconnected;
            _client.Connect(host, port);
        }

        public string Status { get; private set; } = "连接中…";

        public bool Connected => _client.Connected;

        public bool IsJoined { get; private set; }

        public int SentInputs { get; private set; }

        public int LastInputFrame { get; private set; }

        public int LastAckInput => _client.LastAckSnapshot;

        public int ExpectedPlayers { get; private set; }

        /// <summary>本局地图行键（来自 JoinAck；0 = 服务端未标注，由调用方取默认图）。</summary>
        public int MapId { get; private set; }

        /// <summary>JoinAck 到达时回调地图行键（调用方据此加载地图场景）。</summary>
        public event Action<int> JoinAcked;

        /// <summary>权威战斗事件统计（命中/击杀/开火/着墙）——命中判定的唯一可信来源。</summary>
        public CombatEventStats Combat { get; } = new CombatEventStats();

        public long LocalEntityId => _localEntityId;

        public int PlayerId => _client.PlayerId;

        public bool TryGetEntityPosition(long entityId, out Vector3 position)
            => _positions.TryGetValue(entityId, out position);

        /// <summary>取一个非本地实体（本地服 bot/对手）的位置；无则 false。</summary>
        public bool TryGetOtherEntityPosition(out long entityId, out Vector3 position)
        {
            foreach (KeyValuePair<long, Vector3> pair in _positions)
            {
                if (pair.Key == 0 || pair.Key == _localEntityId) continue;
                entityId = pair.Key;
                position = pair.Value;
                return true;
            }

            entityId = 0;
            position = default;
            return false;
        }

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
            UniTask pump = PumpAsync(linked.Token);
            try
            {
                using CancellationTokenSource joinCts =
                    CancellationTokenSource.CreateLinkedTokenSource(linked.Token);
                joinCts.CancelAfter(JoinTimeoutMs);
                try
                {
                    (int winner, bool joined, BattleResult result) = await UniTask
                        .WhenAny(_joined.Task, _completion.Task)
                        .AttachExternalCancellation(joinCts.Token);
                    if (winner == 1) return result;
                }
                catch (OperationCanceledException) when (!linked.IsCancellationRequested)
                {
                    return BattleResult.Failed("加入房间超时");
                }

                return await _completion.Task.AttachExternalCancellation(linked.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            finally
            {
                linked.Cancel();
                await pump.SuppressCancellationThrow();
                Dispose();
            }
        }

        private async UniTask PumpAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                _client.TickIncoming();
                _client.TickOutgoing();
                if (_started && _input != null && _localEntityId != 0 && _clock != null)
                {
                    _clock.Tick(Time.unscaledDeltaTime);
                    // 网络到达晚于服务器开局：用快照+3 把本地帧轴拉到服务器未来窗内。
                    int target = _client.LastSnapshotFrame + 3;
                    for (int index = 0; index < 8 && _clock.Frame < target; index++)
                        _clock.Tick(1f / 60f);
                    int frame = _clock.Frame;
                    if (frame != _lastClockFrame)
                    {
                        _lastClockFrame = frame;
                        _frame = frame;
                        LastInputFrame = frame;
                        SentInputs++;
                        BattleInputState state = _input.Read();
                        _client.SendInput(frame, BattleInputFrame.Build(state, _localEntityId),
                            Math.Max(0, _client.LastSnapshotFrame)); // 视点帧=已渲染快照，不是本地超前帧
                    }
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
            _joined.TrySetResult(true);
            ExpectedPlayers = ack.ExpectedPlayers;
            MapId = ack.MapId;
            Status = "已进房，等待开局…";
            JoinAcked?.Invoke(MapId);
            if (_autoStart && _client.IsOwner
                && (ack.ExpectedPlayers <= 1 || ack.Members.Count >= ack.ExpectedPlayers))
                _client.SendStartMatchRequest();
        }

        /// <summary>收到 StartGame：校验配置摘要（不等即判据不同源，拒绝本局）；然后起帧钟。</summary>
        private void OnStartGame(LiteNet.Proto.StartGame start)
        {
            // 红线 1：客户端按 StartGame.ConfigHash 复算比对，不等即拒局。
            if (_expectedConfigHash.HasValue && start.ConfigHash != _expectedConfigHash.Value)
            {
                string message = "配置摘要不一致：服务端=" + start.ConfigHash.ToString("x8")
                    + " 客户端=" + _expectedConfigHash.Value.ToString("x8");
                Log.Error("[ConfigDigest] " + message);
                Status = message;
                ConfigMismatch = message;
                _client.Disconnect();
                return;
            }

            Status = "对局进行中…";
            _frame = start.Frame;
            _clock = new BattleFrameClock((ulong)Math.Max(0L, start.Seed), Math.Max(1, ExpectedPlayers));
            _started = true;
        }

        /// <summary>配置摘要不一致时的说明（null = 一致或未校验）。</summary>
        public string ConfigMismatch { get; private set; }

        /// <summary>私有状态里的 HP/弹匣喂 HUD；只做展示，不参与判定。</summary>
        private void OnSnapshot(LiteNet.Proto.StateSnapshot snapshot)
        {
            // Unity fake-null：world 可能已被 BattleFeature 销毁，`?.` 挡不住。
            if (_world != null) _world.ApplySnapshot(snapshot);
            if (snapshot.PrivateState == null) return;
            // PrivateState 的 EntityId 在 delta 快照可能缺省为 0；只在非 0 时更新，避免把本地实体清空。
            if (snapshot.PrivateState.EntityId != 0) _localEntityId = snapshot.PrivateState.EntityId;
            for (int index = 0; index < snapshot.Slots.Count; index++)
            {
                LiteNet.Proto.SlotDelta slot = snapshot.Slots[index];
                _positions[slot.Id] = new Vector3(slot.PosX, slot.PosY, slot.PosZ);
                if (slot.Id != _localEntityId || _hud == null) continue;
                _hud.SetScore(slot.Kills, slot.Deaths);
                // delta 快照的 Hp=0 无法区分“未变化”与“已死亡”；只在全量快照写生命。
                if (snapshot.IsFull) _hud.SetVitals(slot.Hp, Math.Max(100, slot.Hp), _hud.Ammo);
            }

            if (_hud != null && snapshot.PrivateState.Weapons.Count > 0)
                _hud.SetAmmo(snapshot.PrivateState.Weapons[0].MagAmmo);
        }

        /// <summary>权威战斗事件：累计到 <see cref="Combat"/>，并把每条写成单行 JSON 日志（命中验证的唯一证据面）。</summary>
        private void OnCombatEvents(LiteNet.Proto.CombatEventBatch batch)
        {
            if (batch == null) return;
            for (int i = 0; i < batch.Records.Count; i++)
            {
                LiteNet.Proto.CombatEventEntry entry = batch.Records[i];
                var kind = (LiteSim.FrameEventKind)entry.Kind;
                Combat.Record(kind, entry.A, entry.B, entry.Value, _localEntityId);
                RouteFeedback(kind, entry);
                if (VerboseEventLog)
                    Log.Info("[CombatEvent] {\"kind\":\"" + kind + "\",\"a\":" + entry.A + ",\"b\":" + entry.B
                        + ",\"frame\":" + entry.Frame + ",\"value\":" + entry.Value
                        + ",\"local\":" + _localEntityId + "}");
            }
        }

        /// <summary>事件 → 音效与伤害数字（表现面；不参与判定）。</summary>
        private void RouteFeedback(LiteSim.FrameEventKind kind, LiteNet.Proto.CombatEventEntry entry)
        {
            var position = new Vector3(entry.X, entry.Y, entry.Z);
            _audioDriver?.OnCombatEvent(kind, entry.A, _localEntityId, position);
            if (_damageNumbers == null) return;
            if (kind != LiteSim.FrameEventKind.Hit && kind != LiteSim.FrameEventKind.Crit) return;
            DamageNumber number = _damageNumbers.Record(kind, entry.A, _localEntityId, entry.Value,
                Time.realtimeSinceStartup);
            Log.Info("[DamageNumber] " + number.Text + " (" + number.Role + ") target=" + number.TargetId);
        }

        /// <summary>是否逐条打印战斗事件（默认开：命中验证需要可读证据；压测时关）。</summary>
        public bool VerboseEventLog { get; set; } = true;

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

            var rows = new List<BattleSettlementRow>(ended.Players.Count);
            for (int index = 0; index < ended.Players.Count; index++)
            {
                LiteNet.Proto.PlayerMatchResult row = ended.Players[index];
                rows.Add(new BattleSettlementRow(row.PlayerId.ToString(), row.Kills, row.Deaths, 0L));
            }

            _completion.TrySetResult(BattleResult.Completed(_client.PlayerId.ToString(), rows));
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
            _client.OnCombatEvents -= OnCombatEvents;
            _client.OnMatchEnded -= OnMatchEnded;
            _client.OnPhaseChanged -= OnPhaseChanged;
            _client.OnDisconnected -= OnDisconnected;
            _client.Dispose();
            _transport.Dispose();
        }
    }
}
