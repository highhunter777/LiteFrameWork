using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using LiteTesting;
using LiteNet.Protocol;
using LiteNet.Proto;
using LiteNet.Transport;
using RoomServer;
using RoomServer.Runtime;
using Xunit;

namespace LiteNet.Tests
{
    public sealed class SecureEnvelopeTransportTests
    {
        [Fact]
        public void Handshake_EncryptsBusinessPayload_InBothDirections()
        {
            var key = CreateKey(0x21);
            var clientWire = new FakeClientTransport();
            var serverWire = new FakeRoomTransport();
            using var client = new SecureEnvelopeClientTransport(clientWire, new SecureEnvelopeOptions(key));
            using var server = new SecureEnvelopeRoomTransport(serverWire, new SecureEnvelopeOptions(key));
            int clientConnected = 0;
            int serverConnected = 0;
            var clientPackets = new List<byte[]>();
            var serverPackets = new List<byte[]>();
            client.OnConnected += () => clientConnected++;
            server.OnConnected += _ => serverConnected++;
            client.OnData += (data, _) => clientPackets.Add(Copy(data));
            server.OnData += (_, data, _) => serverPackets.Add(Copy(data));

            serverWire.RaiseConnected(7);
            clientWire.RaiseConnected();
            serverWire.RaiseData(7, clientWire.Sent[0].Data, true);
            clientWire.RaiseData(serverWire.Sent[0].Data, true);
            serverWire.RaiseData(7, clientWire.Sent[1].Data, true);

            Assert.Equal(1, clientConnected);
            Assert.Equal(1, serverConnected);

            byte[] clientPayload = { 1, 2, 3, 4 };
            client.Send(new ArraySegment<byte>(clientPayload), false);
            serverWire.RaiseData(7, clientWire.Sent[2].Data, false);
            Assert.Equal(clientPayload, serverPackets[0]);

            byte[] serverPayload = { 9, 8, 7 };
            server.SendTo(7, new ArraySegment<byte>(serverPayload), true);
            clientWire.RaiseData(serverWire.Sent[1].Data, true);
            Assert.Equal(serverPayload, clientPackets[0]);
        }

        [Fact]
        public void TamperedPayload_IsRejectedAndDisconnects()
        {
            (SecureEnvelopeClientTransport client, SecureEnvelopeRoomTransport server,
                FakeClientTransport clientWire, FakeRoomTransport serverWire) = Establish();
            using (client)
            using (server)
            {
                int received = 0;
                int disconnected = 0;
                server.OnData += (_, _, _) => received++;
                server.OnDisconnected += _ => disconnected++;

                client.Send(new ArraySegment<byte>(new byte[] { 4, 5, 6 }), true);
                byte[] tampered = Copy(clientWire.Sent[2].Data);
                tampered[tampered.Length - 1] ^= 0x40;
                serverWire.RaiseData(7, new ArraySegment<byte>(tampered), true);

                Assert.Equal(0, received);
                Assert.Equal(1, disconnected);
            }
        }

        [Fact]
        public void ReplayedEnvelope_IsRejectedAfterFirstDelivery()
        {
            (SecureEnvelopeClientTransport client, SecureEnvelopeRoomTransport server,
                FakeClientTransport clientWire, FakeRoomTransport serverWire) = Establish();
            using (client)
            using (server)
            {
                int received = 0;
                int disconnected = 0;
                server.OnData += (_, _, _) => received++;
                server.OnDisconnected += _ => disconnected++;

                client.Send(new ArraySegment<byte>(new byte[] { 7, 8, 9 }), false);
                byte[] packet = Copy(clientWire.Sent[2].Data);
                serverWire.RaiseData(7, new ArraySegment<byte>(packet), false);
                serverWire.RaiseData(7, new ArraySegment<byte>(packet), false);

                Assert.Equal(1, received);
                Assert.Equal(1, disconnected);
            }
        }

        [Fact]
        public void WrongKey_DoesNotExposeConnectedEvent()
        {
            var clientWire = new FakeClientTransport();
            var serverWire = new FakeRoomTransport();
            using var client = new SecureEnvelopeClientTransport(clientWire, new SecureEnvelopeOptions(CreateKey(0x31)));
            using var server = new SecureEnvelopeRoomTransport(serverWire, new SecureEnvelopeOptions(CreateKey(0x32)));
            int clientConnected = 0;
            int serverConnected = 0;
            client.OnConnected += () => clientConnected++;
            server.OnConnected += _ => serverConnected++;

            serverWire.RaiseConnected(7);
            clientWire.RaiseConnected();
            serverWire.RaiseData(7, clientWire.Sent[0].Data, true);
            clientWire.RaiseData(serverWire.Sent[0].Data, true);

            Assert.Equal(0, clientConnected);
            Assert.Equal(0, serverConnected);
        }

        // ---- 握手攻击面：首包/信道/证明/再握手须全部 fail-closed ----

        [Fact]
        public void 首包非握手_拒绝并断开_不产生会话()
        {
            var serverWire = new FakeRoomTransport();
            using var server = new SecureEnvelopeRoomTransport(serverWire, new SecureEnvelopeOptions(CreateKey(0x51)));
            int connected = 0;
            server.OnConnected += _ => connected++;

            serverWire.RaiseConnected(7);
            serverWire.RaiseData(7, new ArraySegment<byte>(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }), true);

            Assert.Equal(0, connected);
            Assert.Contains(7, serverWire.Disconnects);   // 垃圾首包 = 直接断开（不回包不等待）
        }

        [Fact]
        public void ClientHello经不可靠信道_拒绝()
        {
            var clientWire = new FakeClientTransport();
            var serverWire = new FakeRoomTransport();
            using var client = new SecureEnvelopeClientTransport(clientWire, new SecureEnvelopeOptions(CreateKey(0x52)));
            using var server = new SecureEnvelopeRoomTransport(serverWire, new SecureEnvelopeOptions(CreateKey(0x52)));
            int connected = 0;
            server.OnConnected += _ => connected++;

            serverWire.RaiseConnected(7);
            clientWire.RaiseConnected();
            // ClientHello 以 unreliable 信道到达——握手消息必须走可靠信道（防丢包驱动的状态机错位）
            serverWire.RaiseData(7, clientWire.Sent[0].Data, false);

            Assert.Equal(0, connected);
            Assert.Contains(7, serverWire.Disconnects);
        }

        [Fact]
        public void ClientFinish携带错误证明_拒绝且不建立会话()
        {
            var clientWire = new FakeClientTransport();
            var serverWire = new FakeRoomTransport();
            using var client = new SecureEnvelopeClientTransport(clientWire, new SecureEnvelopeOptions(CreateKey(0x53)));
            using var server = new SecureEnvelopeRoomTransport(serverWire, new SecureEnvelopeOptions(CreateKey(0x54)));
            int connected = 0;
            server.OnConnected += _ => connected++;

            serverWire.RaiseConnected(7);
            clientWire.RaiseConnected();
            serverWire.RaiseData(7, clientWire.Sent[0].Data, true);   // ClientHello → ServerHello（错钥证明）
            clientWire.RaiseData(serverWire.Sent[0].Data, true);      // 客户端侧验服务器证明失败 → 客户端自断
            // 攻击者继续伪造 ClientFinish（64 字节：头 6 + 证明 32 + 杂质）
            var bogus = new byte[6 + 32];
            bogus[0] = (byte)'L'; bogus[1] = (byte)'S'; bogus[2] = (byte)'E'; bogus[3] = (byte)'1';
            bogus[4] = 1; bogus[5] = 3;
            for (int i = 6; i < bogus.Length; i++) bogus[i] = 0xAA;
            serverWire.RaiseData(7, new ArraySegment<byte>(bogus), true);

            Assert.Equal(0, connected);
            Assert.Contains(7, serverWire.Disconnects);
        }

        [Fact]
        public void 建立会话后再收握手包_按解密失败处置_断开()
        {
            (SecureEnvelopeClientTransport client, SecureEnvelopeRoomTransport server,
                FakeClientTransport clientWire, FakeRoomTransport serverWire) = Establish();
            using (client)
            using (server)
            {
                int disconnected = 0;
                server.OnDisconnected += _ => disconnected++;

                // 再握手尝试（重放 ClientHello）——已建立会话只认密文，解不开即断
                serverWire.RaiseData(7, clientWire.Sent[0].Data, true);

                Assert.Equal(1, disconnected);
            }
        }

        [Fact]
        public void 伪服务器ServerHello_客户端验证明失败自断()
        {
            var clientWire = new FakeClientTransport();
            var serverWire = new FakeRoomTransport();
            using var client = new SecureEnvelopeClientTransport(clientWire, new SecureEnvelopeOptions(CreateKey(0x55)));
            using var server = new SecureEnvelopeRoomTransport(serverWire, new SecureEnvelopeOptions(CreateKey(0x56)));
            int clientConnected = 0;
            int clientDisconnected = 0;
            client.OnConnected += () => clientConnected++;
            client.OnDisconnected += () => clientDisconnected++;

            serverWire.RaiseConnected(7);
            clientWire.RaiseConnected();
            serverWire.RaiseData(7, clientWire.Sent[0].Data, true);   // ClientHello 到服务器 → 服务器回 ServerHello（错钥证明）
            clientWire.RaiseData(serverWire.Sent[0].Data, true);      // 伪 ServerHello 到客户端 → 证明验不过自断

            Assert.Equal(0, clientConnected);
            Assert.Equal(1, clientDisconnected);                       // 客户端不信任无证明的握手
        }

        [Fact]
        public void 载荷上限_边界内通过_越界在加密口拒绝()
        {
            (SecureEnvelopeClientTransport client, SecureEnvelopeRoomTransport server,
                FakeClientTransport clientWire, FakeRoomTransport serverWire) = Establish();
            using (client)
            using (server)
            {
                int received = 0;
                server.OnData += (_, _, _) => received++;

                // 边界内：默认上限 64KB 逐字节通过
                var atLimit = new byte[64 * 1024];
                client.Send(new ArraySegment<byte>(atLimit), true);
                serverWire.RaiseData(7, clientWire.Sent[2].Data, true);
                Assert.Equal(1, received);

                // 越界：加密口直接抛（不产生半包上线）
                var tooBig = new byte[64 * 1024 + 1];
                Assert.Throws<ArgumentOutOfRangeException>(() =>
                    client.Send(new ArraySegment<byte>(tooBig), true));
            }
        }

        private static (SecureEnvelopeClientTransport client, SecureEnvelopeRoomTransport server,
            FakeClientTransport clientWire, FakeRoomTransport serverWire) Establish()
        {
            byte[] key = CreateKey(0x41);
            var clientWire = new FakeClientTransport();
            var serverWire = new FakeRoomTransport();
            var client = new SecureEnvelopeClientTransport(clientWire, new SecureEnvelopeOptions(key));
            var server = new SecureEnvelopeRoomTransport(serverWire, new SecureEnvelopeOptions(key));
            serverWire.RaiseConnected(7);
            clientWire.RaiseConnected();
            serverWire.RaiseData(7, clientWire.Sent[0].Data, true);
            clientWire.RaiseData(serverWire.Sent[0].Data, true);
            serverWire.RaiseData(7, clientWire.Sent[1].Data, true);
            return (client, server, clientWire, serverWire);
        }

        private static byte[] CreateKey(byte seed)
        {
            var key = new byte[32];
            for (int i = 0; i < key.Length; i++) key[i] = (byte)(seed + i);
            return key;
        }

        private static byte[] Copy(ArraySegment<byte> data)
        {
            var copy = new byte[data.Count];
            Buffer.BlockCopy(data.Array, data.Offset, copy, 0, data.Count);
            return copy;
        }

        private sealed class SentPacket
        {
            internal readonly byte[] Data;
            internal readonly bool Reliable;

            internal SentPacket(ArraySegment<byte> data, bool reliable)
            {
                Data = Copy(data);
                Reliable = reliable;
            }
        }

        private sealed class FakeClientTransport : IClientTransport
        {
            internal readonly List<SentPacket> Sent = new List<SentPacket>();
            internal bool DisconnectRequested;

            public bool Connected { get; private set; }
            public event Action OnConnected;
            public event Action<ArraySegment<byte>, bool> OnData;
            public event Action OnDisconnected;

            public void Connect(string address, int port) { }
            public void TickIncoming() { }
            public void TickOutgoing() { }
            public void Send(ArraySegment<byte> data, bool reliable) => Sent.Add(new SentPacket(data, reliable));
            public void Disconnect()
            {
                DisconnectRequested = true;
                if (!Connected) return;
                Connected = false;
                OnDisconnected?.Invoke();
            }

            public void Dispose() { }

            internal void RaiseConnected()
            {
                Connected = true;
                OnConnected?.Invoke();
            }

            internal void RaiseData(ArraySegment<byte> data, bool reliable) => OnData?.Invoke(data, reliable);
        }

        private sealed class FakeRoomTransport : IRoomTransport
        {
            internal readonly List<SentPacket> Sent = new List<SentPacket>();
            internal readonly List<int> Disconnects = new List<int>();
            public event Action<int, ArraySegment<byte>, bool> OnData;
            public event Action<int> OnConnected;
            public event Action<int> OnDisconnected;

            public void Start(int port) { }

            /// <summary>本类假件不涉及端口回读（装饰器转发语义由 SecureEnvelopeRoomTransport 转发断言覆盖）。</summary>
            public int BoundPort => -1;

            public void TickIncoming() { }
            public void TickOutgoing() { }
            public void SendTo(int connectionId, ArraySegment<byte> data, bool reliable) => Sent.Add(new SentPacket(data, reliable));
            public void Broadcast(ArraySegment<byte> data, bool reliable) { }
            public void Disconnect(int connectionId)
            {
                Disconnects.Add(connectionId);
                OnDisconnected?.Invoke(connectionId);
            }

            public string GetRemoteAddress(int connectionId) => "127.0.0.1";
            public void Dispose() { }

            internal void RaiseConnected(int connectionId) => OnConnected?.Invoke(connectionId);
            internal void RaiseData(int connectionId, ArraySegment<byte> data, bool reliable)
                => OnData?.Invoke(connectionId, data, reliable);
        }
    }

    /// <summary>
    /// 安全信封的**真实 KCP 回环**段：信封装饰器套在真 KCP 两端——
    /// 握手与密文在真实 UDP/MTU/可靠信道语义下走通，Join→JoinAck 全链可用。
    /// 与上层 <see cref="SecureEnvelopeTransportTests"/>（假传输，钉协议与攻击面）互补：
    /// 假件抹不掉的这层（Socket 时序、MTU、KCP 会话）由本组钉住。
    /// </summary>
    [Trait(LiteTesting.TestTrait.Category, LiteTesting.TestCategory.Integration)]
    public sealed class SecureEnvelopeRealTransportTests : IDisposable
    {
        private int Port => _host.BoundPort;   // 动态端口：宿主绑定后回读（Start(0) 由系统分配）
        private const string Config = @"{
            ""port"": 0, ""max_rooms"": 2, ""audience"": """",
            ""default_template"": ""two"",
            ""rooms"": { ""two"": { ""expected_players"": 2 } }
        }";

        private readonly ServerHost _host;
        private readonly SecureEnvelopeClientTransport _client;

        public SecureEnvelopeRealTransportTests()
        {
            byte[] key = CreateKey(0x77);
            var cfg = RoomServerConfig.Parse(Config);
            _host = new ServerHost(
                new SecureEnvelopeRoomTransport(new KcpTransportServer(), new SecureEnvelopeOptions(key)),
                null, null, null, cfg);
            _client = new SecureEnvelopeClientTransport(
                new KcpTransportClient(), new SecureEnvelopeOptions(key));
        }

        public void Dispose()
        {
            _client.Dispose();
            _host.Dispose();
        }

        private static byte[] CreateKey(byte seed)
        {
            var key = new byte[32];
            for (int i = 0; i < key.Length; i++) key[i] = (byte)(seed + i);
            return key;
        }

        [Fact]
        public void 真KCP回环_信封握手后_Join全链可用()
        {
            var connected = new ManualResetEventSlim(false);
            var joined = new ManualResetEventSlim(false);
            int? playerSeat = null;
            _client.OnConnected += () => connected.Set();
            _client.OnData += (data, _) =>
            {
                if (joined.IsSet) return;
                if (PacketCodec.TryDecode(data, out PacketType type, out Google.Protobuf.IMessage msg)
                    && type == PacketType.JoinAck)
                {
                    playerSeat = ((JoinAck)msg).PlayerId;
                    joined.Set();
                }
            };

            _client.Connect("127.0.0.1", Port);
            var watch = Stopwatch.StartNew();
            while (!connected.Wait(10) && watch.ElapsedMilliseconds < 5000) PumpOne();
            Assert.True(_client.Connected, "信封握手 5s 内未完成（真 KCP）");

            byte[] join = PacketCodec.Encode(PacketType.Join, new JoinRequest
            {
                RoomId = "two", Token = "tok", BuildHash = ServerHost.ServerBuildHash,
            });
            _client.Send(new ArraySegment<byte>(join), true);

            watch.Restart();
            while (!joined.Wait(10) && watch.ElapsedMilliseconds < 5000) PumpOne();
            Assert.True(joined.IsSet, "信封密文上行的 Join 未在 5s 内等到 JoinAck");
            Assert.True(playerSeat >= 0, "JoinAck 应带有效号位");
        }

        private void PumpOne()
        {
            _host.Pump();
            _client.TickIncoming();
            _client.TickOutgoing();
            Thread.Sleep(10);
        }
    }
}
