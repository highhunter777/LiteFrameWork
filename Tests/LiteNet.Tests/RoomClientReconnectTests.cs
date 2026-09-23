using System;
using System.Collections.Generic;
using LiteNet.Protocol;
using LiteNet.Transport;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// RoomClient 重连状态机用例（《商业级通用服务端框架总设计》§9.3——R1 批③）：
    /// Connected → SuspectedLost → Reconnecting → Restoring → Connected；超时/拒绝/版本不符 → Failed。
    /// 全部假传输驱动（无 Socket；时钟注入——超时零真实等待）。
    /// </summary>
    public sealed class RoomClientReconnectTests
    {
        /// <summary>假传输：同步握手（Connect 即 OnConnected）；记录发送/重拨/可靠位。</summary>
        private sealed class FakeTransport : IClientTransport
        {
            public readonly List<(ArraySegment<byte> data, bool reliable)> Sent
                = new List<(ArraySegment<byte>, bool)>();
            public int ConnectCalls;
            public string LastHost;
            public int LastPort;
            public bool Disposed;

            public bool Connected => true;

            public event Action OnConnected;
            public event Action<ArraySegment<byte>, bool> OnData;
            public event Action OnDisconnected;

            public void Connect(string address, int port)
            {
                ConnectCalls++;
                LastHost = address;
                LastPort = port;
                OnConnected?.Invoke();     // 假件同步完成握手（真实 KCP 为 Tick 驱动异步——L3 真链路另测）
            }
            public void Disconnect() => OnDisconnected?.Invoke();
            public void TickIncoming() { }
            public void TickOutgoing() { }

            public void Send(ArraySegment<byte> data, bool reliable)
            {
                var copy = new byte[data.Count];
                Buffer.BlockCopy(data.Array, data.Offset, copy, 0, data.Count);
                Sent.Add((new ArraySegment<byte>(copy), reliable));
            }

            public void RaiseData(byte[] packet) => OnData?.Invoke(new ArraySegment<byte>(packet), true);
            public void RaiseDisconnected() => OnDisconnected?.Invoke();

            public object LastOf(PacketType type)
            {
                for (int i = Sent.Count - 1; i >= 0; i--)
                {
                    if (!PacketCodec.TryDecode(Sent[i].data, out PacketType t, out var msg)) continue;
                    if (t == type) return msg;
                }
                return null;
            }

            /// <summary>指定类型最近一包是否走可靠通道（RestoreComplete 必须 Reliable——丢了 = 永久抑制）。</summary>
            public bool SentReliable(PacketType type)
            {
                for (int i = Sent.Count - 1; i >= 0; i--)
                {
                    if (!PacketCodec.TryDecode(Sent[i].data, out PacketType t, out _)) continue;
                    if (t == type) return Sent[i].reliable;
                }
                return false;
            }

            public void Dispose() => Disposed = true;
        }

        /// <summary>已进房客户端（连接端点 + JoinAck + StartGame 版本基准就绪；token 可配）。</summary>
        private static (RoomClient c, FakeTransport t) JoinedClient(Func<long> clock = null, string token = "tok-1")
        {
            var t = new FakeTransport();
            var c = new RoomClient(t, clock);
            c.Connect("127.0.0.1", 17777);                                       // 端点记录（重拨基准）；假件同步 OnConnected
            c.SendJoin("Room-A", "client-token", "hash-1");                      // 版本基准：buildHash
            t.RaiseData(PacketCodec.Encode(PacketType.JoinAck,
                new Proto.JoinAck { PlayerId = 3, ReconnectToken = token }));
            t.RaiseData(PacketCodec.Encode(PacketType.StartGame,
                new Proto.StartGame { Seed = 77, ConfigHash = 42 }));             // 版本基准：seed/configHash
            Assert.Equal(ClientSessionPhase.Connected, c.Phase);
            return (c, t);
        }

        [Fact]
        public void 断线进SuspectedLost_OnDisconnected照常派发()
        {
            var (c, t) = JoinedClient();
            int disconnected = 0;
            c.OnDisconnected += () => disconnected++;

            t.RaiseDisconnected();

            Assert.Equal(ClientSessionPhase.SuspectedLost, c.Phase);
            Assert.Equal(1, disconnected);
        }

        [Fact]
        public void 完整闭环_重拨自动发请求_验版本_恢复完成ACK回Connected()
        {
            var (c, t) = JoinedClient();
            var phases = new List<ClientSessionPhase>();
            c.OnPhaseChanged += (from, to) => phases.Add(to);
            int responses = 0;
            c.OnReconnectResponse += _ => responses++;

            t.RaiseDisconnected();
            Assert.True(c.BeginReconnect());
            Assert.Equal(ClientSessionPhase.Reconnecting, c.Phase);
            Assert.Equal(2, t.ConnectCalls);                                   // 首连 1 + 重拨 1（原端点）
            Assert.Equal("127.0.0.1", t.LastHost);

            // 传输连接建立 → 自动发 ReconnectRequest（Reliable，带一次性票据）
            var req = (Proto.ReconnectRequest)t.LastOf(PacketType.ReconnectRequest);
            Assert.NotNull(req);
            Assert.Equal("tok-1", req.OneTimeToken);
            Assert.True(t.SentReliable(PacketType.ReconnectRequest));

            // 响应 Ok 且版本一致（seed/configHash/buildHash 与 StartGame/Join 同值）→ Restoring
            t.RaiseData(PacketCodec.Encode(PacketType.ReconnectResponse, new Proto.ReconnectResponse
            {
                Ok = true, Seed = 77, ConfigHash = 42, BuildHash = "hash-1",
                Snapshot = new Proto.StateSnapshot { Frame = 100, IsFull = true },
            }));
            Assert.Equal(ClientSessionPhase.Restoring, c.Phase);
            Assert.Equal(1, responses);

            // 应用层宣告恢复完成 → RestoreComplete（Reliable）→ Connected
            Assert.True(c.CompleteRestore());
            Assert.Equal(ClientSessionPhase.Connected, c.Phase);
            Assert.True(t.SentReliable(PacketType.RestoreComplete), "恢复完成 ACK 丢包即永久抑制——必须走可靠通道");
            Assert.False(c.CompleteRestore(), "重复宣告幂等忽略");

            Assert.Equal(new[]
            {
                ClientSessionPhase.SuspectedLost,
                ClientSessionPhase.Reconnecting,
                ClientSessionPhase.Restoring,
                ClientSessionPhase.Connected,
            }, phases);
        }

        [Fact]
        public void 响应版本不符_进Failed_不发恢复完成ACK()
        {
            var (c, t) = JoinedClient();
            t.RaiseDisconnected();
            Assert.True(c.BeginReconnect());

            t.RaiseData(PacketCodec.Encode(PacketType.ReconnectResponse, new Proto.ReconnectResponse
            {
                Ok = true, Seed = 999, ConfigHash = 42, BuildHash = "hash-1",   // seed 与 StartGame(77) 不符
            }));

            Assert.Equal(ClientSessionPhase.Failed, c.Phase);
            Assert.Null(t.LastOf(PacketType.RestoreComplete));                    // 不是同一局：绝不宣告恢复
            Assert.False(c.CompleteRestore());
        }

        [Fact]
        public void 服务器拒绝_进Failed()
        {
            var (c, t) = JoinedClient();
            t.RaiseDisconnected();
            Assert.True(c.BeginReconnect());

            t.RaiseData(PacketCodec.Encode(PacketType.ReconnectResponse,
                new Proto.ReconnectResponse { Ok = false, Reason = "票据无效或已过期" }));

            Assert.Equal(ClientSessionPhase.Failed, c.Phase);
            Assert.Null(t.LastOf(PacketType.RestoreComplete));
        }

        [Fact]
        public void 无票据重连_直接Failed()
        {
            var t = new FakeTransport();
            var c = new RoomClient(t);
            c.Connect("127.0.0.1", 17777);
            t.RaiseData(PacketCodec.Encode(PacketType.JoinAck,
                new Proto.JoinAck { PlayerId = 0, ReconnectToken = "" }));      // 进房但无票据
            Assert.Equal(ClientSessionPhase.Connected, c.Phase);

            t.RaiseDisconnected();
            Assert.False(c.BeginReconnect());                                    // 票据耗尽：唯一出路是重新 Join
            Assert.Equal(ClientSessionPhase.Failed, c.Phase);
            Assert.Equal(1, t.ConnectCalls);                                     // 只有首连，无重拨
        }

        [Fact]
        public void 重连超时_进Failed_注入时钟零真实等待()
        {
            long now = 0;
            var (c, t) = JoinedClient(() => now);
            t.RaiseDisconnected();
            Assert.True(c.BeginReconnect());                                     // deadline = 0 + ReconnectTimeoutMs

            now = RoomClient.ReconnectTimeoutMs - 1;
            c.TickIncoming();
            Assert.Equal(ClientSessionPhase.Reconnecting, c.Phase);              // 未越界仍等待

            now = RoomClient.ReconnectTimeoutMs;
            c.TickOutgoing();
            Assert.Equal(ClientSessionPhase.Failed, c.Phase);                     // 超时 → Failed（§9.3）
        }

        [Fact]
        public void 首连不自动发重连请求()
        {
            var t = new FakeTransport();
            var c = new RoomClient(t);

            c.Connect("127.0.0.1", 27778);                                       // 首连（相位 Idle）
            Assert.Equal(ClientSessionPhase.Idle, c.Phase);
            Assert.Null(t.LastOf(PacketType.ReconnectRequest));                   // 重连请求只在 Reconnecting 相位自动发
        }
    }
}
