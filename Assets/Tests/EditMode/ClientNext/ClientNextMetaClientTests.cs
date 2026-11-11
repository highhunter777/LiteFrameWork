using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using LiteGameNext.Platform.Meta;
using MetaWire;
using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>MetaClient 契约：端点/JSON/Bearer/错误分类/取消；用假传输，不依赖网络。</summary>
    public sealed class ClientNextMetaClientTests
    {
        private const string BaseUrl = "http://127.0.0.1:5000";

        [Test]
        public async Task 游客登录_发送设备号并解析令牌()
        {
            var transport = new FakeTransport();
            transport.Enqueue(200, "{\"accountId\":\"acc-1\",\"accessToken\":\"tok-1\",\"tokenType\":\"Bearer\",\"expiresAtMs\":99}");
            var client = new MetaClient(transport);

            MetaCallResult<GuestLoginInfo> result = await client.GuestLoginAsync("device-1");

            Assert.IsTrue(result.Success);
            Assert.AreEqual("tok-1", result.Value.AccessToken);
            Assert.AreEqual(1, transport.Requests.Count);
            Assert.AreEqual("POST", transport.Requests[0].Method);
            Assert.AreEqual(MetaWireEndpoints.GuestLogin, transport.Requests[0].Path);
            StringAssert.Contains("\"deviceId\":\"device-1\"", transport.Requests[0].JsonBody);
            StringAssert.Contains("\"requestId\":", transport.Requests[0].JsonBody);
        }

        [Test]
        public async Task 建房_携带Bearer与建房字段()
        {
            var transport = new FakeTransport();
            transport.Enqueue(200, "{\"instanceId\":\"i\",\"address\":\"127.0.0.1:7777\",\"roomId\":\"r\",\"ticket\":\"t\",\"expiresAtMs\":1}");
            var client = new MetaClient(transport);

            MetaCallResult<JoinTicketInfo> result = await client.CreateRoomAsync(
                "tok", "hash", mapId: 2, expectedPlayers: 4, template: "four");

            Assert.IsTrue(result.Success);
            MetaRequest request = transport.Requests[0];
            Assert.AreEqual(MetaWireEndpoints.CreateRoom, request.Path);
            Assert.AreEqual("tok", request.Bearer);
            StringAssert.Contains("\"buildHash\":\"hash\"", request.JsonBody);
            StringAssert.Contains("\"mapId\":2", request.JsonBody);
            StringAssert.Contains("\"expectedPlayers\":4", request.JsonBody);
            StringAssert.Contains("\"template\":\"four\"", request.JsonBody);
        }

        [Test]
        public async Task 房间列表_解析信封并把空明细归一为空列表()
        {
            var transport = new FakeTransport();
            transport.Enqueue(200, "{\"rooms\":[{\"instanceId\":\"i\",\"rooms\":null}]}");
            var client = new MetaClient(transport);

            MetaCallResult<IReadOnlyList<LobbyInstanceView>> result = await client.ListRoomsAsync("tok");

            Assert.IsTrue(result.Success);
            Assert.AreEqual(1, result.Value.Count);
            Assert.IsNotNull(result.Value[0].Rooms);
            Assert.AreEqual(0, result.Value[0].Rooms.Count);
            Assert.AreEqual("tok", transport.Requests[0].Bearer);
        }

        [Test]
        public async Task 换票_发送房号与哈希()
        {
            var transport = new FakeTransport();
            transport.Enqueue(200, "{\"instanceId\":\"i\",\"address\":\"a\",\"roomId\":\"r\",\"ticket\":\"t\",\"expiresAtMs\":1}");
            var client = new MetaClient(transport);

            await client.RequestJoinTicketAsync("tok", "room-7", "hash-9");

            StringAssert.Contains("\"roomId\":\"room-7\"", transport.Requests[0].JsonBody);
            StringAssert.Contains("\"buildHash\":\"hash-9\"", transport.Requests[0].JsonBody);
        }

        [Test]
        public async Task 战绩_带limit查询并解析列表()
        {
            var transport = new FakeTransport();
            transport.Enqueue(200, "{\"matches\":[{\"matchId\":\"m1\"}]}");
            var client = new MetaClient(transport);

            MetaCallResult<IReadOnlyList<MatchRecordInfo>> result = await client.ListRecentMatchesAsync("tok", 5);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(1, result.Value.Count);
            Assert.AreEqual(MetaWireEndpoints.Matches + "?limit=5", transport.Requests[0].Path);
        }

        [Test]
        public async Task HTTP错误_保留状态码与错误码()
        {
            var transport = new FakeTransport();
            transport.Enqueue(400, "{\"code\":\"lobby.bad-room\",\"messageKey\":\"lobby.bad-room\"}");
            var client = new MetaClient(transport);

            MetaCallResult<JoinTicketInfo> result = await client.RequestJoinTicketAsync("tok", "bad", "hash");

            Assert.IsFalse(result.Success);
            Assert.AreEqual(MetaFailureKind.Http, result.Error.Kind);
            Assert.AreEqual(400, result.Error.StatusCode);
            Assert.AreEqual("lobby.bad-room", result.Error.Code);
        }

        [Test]
        public async Task 网络不可达_归类为Network()
        {
            var transport = new FakeTransport();
            transport.Enqueue(0, "connection refused");
            var client = new MetaClient(transport);

            MetaCallResult<GuestLoginInfo> result = await client.GuestLoginAsync("device");

            Assert.IsFalse(result.Success);
            Assert.AreEqual(MetaFailureKind.Network, result.Error.Kind);
        }

        [Test]
        public async Task 响应形状不符_归类为Protocol()
        {
            var transport = new FakeTransport();
            transport.Enqueue(200, "not-json");
            var client = new MetaClient(transport);

            MetaCallResult<GuestLoginInfo> result = await client.GuestLoginAsync("device");

            Assert.IsFalse(result.Success);
            Assert.AreEqual(MetaFailureKind.Protocol, result.Error.Kind);
        }

        [Test]
        public async Task 取消_原样传播()
        {
            var transport = new FakeTransport { ThrowOnSend = true };
            var client = new MetaClient(transport);
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                try
                {
                    await client.GuestLoginAsync("device", cts.Token);
                    Assert.Fail("应传播取消");
                }
                catch (OperationCanceledException)
                {
                }
            }
        }

        [Test]
        public void 传输构造_基址与超时校验()
        {
            Assert.Throws<ArgumentException>(() => new UnityMetaTransport(""));
            Assert.Throws<ArgumentOutOfRangeException>(() => new UnityMetaTransport(BaseUrl, 0));
        }

        private sealed class FakeTransport : IMetaTransport
        {
            private readonly Queue<MetaResponse> _responses = new Queue<MetaResponse>();

            public List<MetaRequest> Requests { get; } = new List<MetaRequest>();

            public bool ThrowOnSend { get; set; }

            public void Enqueue(long status, string body) => _responses.Enqueue(new MetaResponse(status, body));

            public UniTask<MetaResponse> SendAsync(MetaRequest request, CancellationToken cancellationToken)
            {
                Requests.Add(request);
                if (ThrowOnSend) throw new OperationCanceledException(cancellationToken);
                return UniTask.FromResult(_responses.Count == 0
                    ? new MetaResponse(0, "no response")
                    : _responses.Dequeue());
            }
        }
    }
}
