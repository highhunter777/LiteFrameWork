using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteNet;
using LiteNet.Transport;
using UnityEngine;

namespace LiteGame
{
    /// <summary>
    /// 框架网络挂点的 LiteNet 桥接（《状态同步实施方案》§7.1）：
    /// INetworkService（字节级契约）→ KcpTransportClient（LiteNet 字节管道）——房间语义（帧号/Join/冗余）
    /// 一概不过桥（§7.3 红线：框架不知道帧号）；字节收发经 OnReceive 事件交给上层（RoomClient 等）。
    ///
    /// - ITickable：由 GameEntry 统一驱动收发轮询（MVP 单线程，不开网络线程 §7.2）；
    /// - ConnectAsync：kcp2k 握手是 Tick 驱动的异步过程——UniTask.WaitUntil 等 Connected（PlayerLoop 泵）；
    /// - IModuleStats：RTT/收发计数进 HUD（§7.2 诊断行）。
    /// </summary>
    public sealed class KcpNetworkService : INetworkService, IDisposable
    {
        private readonly KcpTransportClient _transport;
        private readonly Dictionary<NetChannel, long> _sentBytes = new Dictionary<NetChannel, long>();
        private readonly Dictionary<NetChannel, long> _recvBytes = new Dictionary<NetChannel, long>();

        private NetState _state = NetState.Disconnected;

        public NetState State => _state;
        public string StatsName => "Net";

        public event Action<NetState, string> OnStateChanged;
        public event Action<byte[], NetChannel> OnReceive;

        public KcpNetworkService() : this(new KcpTransportClient()) { }

        public KcpNetworkService(KcpTransportClient transport)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _transport.OnConnected += () => SetState(NetState.Connected, null);
            _transport.OnDisconnected += () => SetState(NetState.Disconnected, null);
            _transport.OnError += reason => SetState(NetState.Error, reason);
            _transport.OnData += (data, reliable) =>
            {
                var copy = new byte[data.Count];
                Array.Copy(data.Array, data.Offset, copy, 0, data.Count);
                var channel = reliable ? NetChannel.Reliable : NetChannel.Unreliable;
                _recvBytes[channel] = _recvBytes.TryGetValue(channel, out long n) ? n + copy.Length : copy.Length;
                OnReceive?.Invoke(copy, channel);
            };
        }

        /// <summary>发起连接（kcp2k cookie 握手由 Tick 泵驱动；Connected 即完成）。</summary>
        public UniTask ConnectAsync(string host, int port, CancellationToken ct)
        {
            SetState(NetState.Connecting, null);
            _transport.Connect(host, port);
            return UniTask.WaitUntil(() => _transport.Connected, cancellationToken: ct);
        }

        public void Send(ReadOnlySpan<byte> data, NetChannel channel)
        {
            if (_state != NetState.Connected) return;
            var seg = new ArraySegment<byte>(data.ToArray());   // Span→拷贝（kcp2k API 收 ArraySegment）
            _transport.Send(seg, channel == NetChannel.Reliable);
            _sentBytes[channel] = _sentBytes.TryGetValue(channel, out long n) ? n + seg.Count : seg.Count;
        }

        public void Disconnect()
        {
            _transport.Disconnect();
            SetState(NetState.Disconnected, null);
        }

        public void Dispose()
        {
            if (_transport != null) _transport.Dispose();
        }

        /// <summary>ITickable：GameEntry 统一驱动（收发轮询，MVP 单线程 §7.2）。</summary>
        public void Tick(float realDelta)
        {
            _transport?.TickIncoming();
            _transport?.TickOutgoing();
        }

        private void SetState(NetState state, string reason)
        {
            if (_state == state && state != NetState.Error) return;
            _state = state;
            OnStateChanged?.Invoke(state, reason);
        }

        string IModuleStats.StatsName => "Net";

        void IModuleStats.Snapshot(Dictionary<string, string> into)
        {
            into.Clear();
            into["state"] = _state.ToString();
            into["connected"] = (_transport != null && _transport.Connected).ToString();
            foreach (var channel in new[] { NetChannel.Reliable, NetChannel.Unreliable })
            {
                into[$"sent[{channel}]"] = (_sentBytes.TryGetValue(channel, out long s) ? s : 0).ToString();
                into[$"recv[{channel}]"] = (_recvBytes.TryGetValue(channel, out long r) ? r : 0).ToString();
            }
        }
    }
}
