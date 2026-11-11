using System;
using System.Collections.Generic;
using LiteNet.Transport;
using LiteSync.Client;
using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>RoomClient.SendInput 的传输出口：调用后必须交给 IClientTransport（不验证服务器）。</summary>
    public sealed class ClientNextRoomInputSendTests
    {
        [Test]
        public void SendInput_交给传输不可靠通道()
        {
            var transport = new FakeTransport();
            using var client = new RoomClient(transport);
            client.Connect("127.0.0.1", 17777);
            client.SendInput(1, default, 0);
            client.TickOutgoing();

            Assert.GreaterOrEqual(transport.UnreliableSends, 1, "SendInput 未到达 transport");
        }

        private sealed class FakeTransport : IClientTransport
        {
            public bool Connected { get; private set; }

            public int ReliableSends { get; private set; }

            public int UnreliableSends { get; private set; }

            public void Connect(string address, int port) => Connected = true;

            public void Disconnect() => Connected = false;

            public void TickIncoming()
            {
            }

            public void TickOutgoing()
            {
            }

            public void Send(ArraySegment<byte> data, bool reliable)
            {
                if (reliable) ReliableSends++;
                else UnreliableSends++;
            }

            public event Action OnConnected;
            public event Action<ArraySegment<byte>, bool> OnData;
            public event Action OnDisconnected;

            public void Dispose()
            {
                OnConnected = null;
                OnData = null;
                OnDisconnected = null;
            }
        }
    }
}
