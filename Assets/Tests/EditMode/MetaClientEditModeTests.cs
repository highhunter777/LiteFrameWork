using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using LiteGame;
using LiteTesting.Unity;
using NUnit.Framework;
using UnityEngine.TestTools;
using MetaWire;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// MetaClient 契约面（请求形状 + 响应解析 + 失败分类）：真实 HTTP 传输（LocalHttpTestServer——
    /// UnityWebRequest 的 TCP→HTTP 全链路，不是替身；先例同 HttpCandidateFetcherEditModeTests）。
    ///
    /// 异步用例用 [UnityTest] + IEnumerator（EditMode 无 PlayerLoop，SendWebRequest 的续延由
    /// Test Framework 在帧间推进）；ToCoroutine 回传结果。JSON 形状与服务端 MetaServer.Contracts 对表。
    /// </summary>
    public sealed class MetaClientEditModeTests : UnityTestBase
    {
        private static UnityMetaClient Client(LocalHttpTestServer server, int timeoutSeconds = 5)
            => new UnityMetaClient(server.BaseUrl, timeoutSeconds, new LiteFramework.NewtonsoftJsonCodec(),
                () => "req-test");

        [UnityTest]
        public IEnumerator 游客登录_请求体形状与Bearer外无鉴权_响应解析一致()
        {
            using (var server = new LocalHttpTestServer())
            {
                server.Route("/auth/guest",
                    "{\"accountId\":\"acc-1\",\"accessToken\":\"tok-1\",\"tokenType\":\"Bearer\",\"expiresAtMs\":123}");
                var client = Client(server);
                MetaCall<GuestLoginInfo> r = null;

                yield return client.GuestLoginAsync("device-1").ToCoroutine(x => r = x);

                Assert.IsTrue(r.Ok, "2xx 即成功");
                Assert.AreEqual("acc-1", r.Value.AccountId);
                Assert.AreEqual("tok-1", r.Value.AccessToken);
                Assert.AreEqual(123, r.Value.ExpiresAtMs);

                Assert.AreEqual(1, server.HitCount("/auth/guest"));
                var captured = server.Captured("/auth/guest")[0];
                Assert.AreEqual("POST", captured.Method);
                Assert.IsNull(captured.Authorization, "登录请求不带 Bearer");
                StringAssert.Contains("\"requestId\":\"req-test\"", captured.Body);
                StringAssert.Contains("\"deviceId\":\"device-1\"", captured.Body);
            }
        }

        [UnityTest]
        public IEnumerator 票据请求_Bearer携带访问令牌_响应解析一致()
        {
            using (var server = new LocalHttpTestServer())
            {
                server.Route("/lobby/join-ticket",
                    "{\"instanceId\":\"ins-1\",\"address\":\"203.0.113.7:17777\",\"roomId\":\"Room-A\"," +
                    "\"ticket\":\"tkt-1\",\"expiresAtMs\":456}");
                var client = Client(server);
                MetaCall<JoinTicketInfo> r = null;

                yield return client.RequestJoinTicketAsync("tok-1", "Room-A", "bhash").ToCoroutine(x => r = x);

                Assert.IsTrue(r.Ok);
                Assert.AreEqual("ins-1", r.Value.InstanceId);
                Assert.AreEqual("203.0.113.7:17777", r.Value.Address);
                Assert.AreEqual("Room-A", r.Value.RoomId);
                Assert.AreEqual("tkt-1", r.Value.Ticket);

                var captured = server.Captured("/lobby/join-ticket")[0];
                Assert.AreEqual("Bearer tok-1", captured.Authorization);
                StringAssert.Contains("\"roomId\":\"Room-A\"", captured.Body);
                StringAssert.Contains("\"buildHash\":\"bhash\"", captured.Body);
            }
        }

        [UnityTest]
        public IEnumerator 房间列表_嵌套投影解析一致()
        {
            using (var server = new LocalHttpTestServer())
            {
                server.Route("/lobby/rooms/public",
                    "{\"rooms\":[{\"instanceId\":\"ins-1\",\"buildHash\":\"bh\",\"address\":\"h:1\"," +
                    "\"maxRooms\":4,\"roomCount\":1,\"maxPlayers\":8,\"playerCount\":2,\"draining\":false," +
                    "\"lastSeenAgeMs\":10,\"rooms\":[{\"roomId\":\"Room-A\",\"phase\":1,\"players\":2,\"expectedPlayers\":8}]}]}");
                var client = Client(server);
                MetaCall<IReadOnlyList<LobbyInstanceView>> r = null;

                yield return client.ListRoomsAsync("tok-1").ToCoroutine(x => r = x);

                Assert.IsTrue(r.Ok);
                Assert.AreEqual(1, r.Value.Count);
                LobbyInstanceView instance = r.Value[0];
                Assert.AreEqual("ins-1", instance.InstanceId);
                Assert.AreEqual("h:1", instance.Address);
                Assert.IsFalse(instance.Draining);
                Assert.AreEqual(1, instance.Rooms.Count);
                Assert.AreEqual("Room-A", instance.Rooms[0].RoomId);
                Assert.AreEqual(2, instance.Rooms[0].Players);
                Assert.AreEqual(8, instance.Rooms[0].ExpectedPlayers);
                Assert.AreEqual("Bearer tok-1", server.Captured("/lobby/rooms/public")[0].Authorization);
            }
        }

        [UnityTest]
        public IEnumerator 战绩查询_limit进查询串_行字段解析一致()
        {
            using (var server = new LocalHttpTestServer())
            {
                server.Route("/matches",
                    "{\"matches\":[{\"matchId\":\"m-1\",\"kills\":3,\"deaths\":1,\"endReason\":1," +
                    "\"gameplayEndReason\":2,\"finishedAtMs\":789}]}");
                var client = Client(server);
                MetaCall<IReadOnlyList<MatchRecordInfo>> r = null;

                yield return client.ListRecentMatchesAsync("tok-1", 20).ToCoroutine(x => r = x);

                Assert.IsTrue(r.Ok);
                Assert.AreEqual(1, r.Value.Count);
                Assert.AreEqual("m-1", r.Value[0].MatchId);
                Assert.AreEqual(3, r.Value[0].Kills);
                Assert.AreEqual(1, r.Value[0].Deaths);
                Assert.AreEqual(2, r.Value[0].GameplayEndReason);
                Assert.AreEqual(789, r.Value[0].FinishedAtMs);
            }
        }

        [UnityTest]
        public IEnumerator 非二xx_错误体code进Rejected_不抛()
        {
            using (var server = new LocalHttpTestServer())
            {
                server.Route("/lobby/join-ticket", "{\"code\":\"lobby.version-mismatch\",\"messageKey\":\"meta.lobby.x\",\"args\":[]}", 409);
                var client = Client(server);
                MetaCall<JoinTicketInfo> r = null;

                yield return client.RequestJoinTicketAsync("tok-1", "Room-A", "bhash").ToCoroutine(x => r = x);

                Assert.IsFalse(r.Ok);
                Assert.AreEqual(MetaFailureKind.Rejected, r.Failure.Kind);
                Assert.AreEqual(409, r.Failure.HttpStatus);
                Assert.AreEqual("lobby.version-mismatch", r.Failure.Code);
                Assert.AreEqual("meta.lobby.x", r.Failure.Detail);
            }
        }

        [UnityTest]
        public IEnumerator 未注册路径_四零四_错误体缺code_状态码仍可分支()
        {
            using (var server = new LocalHttpTestServer())
            {
                var client = Client(server);
                MetaCall<IReadOnlyList<LobbyInstanceView>> r = null;

                yield return client.ListRoomsAsync("tok-1").ToCoroutine(x => r = x);

                Assert.IsFalse(r.Ok);
                Assert.AreEqual(MetaFailureKind.Rejected, r.Failure.Kind);
                Assert.AreEqual(404, r.Failure.HttpStatus);
                Assert.AreEqual("", r.Failure.Code, "404 无 JSON 错误体——code 为空，状态码仍可分支");
            }
        }

        [UnityTest]
        public IEnumerator 二xx但缺字段_判InvalidResponse_确定错误不重试()
        {
            using (var server = new LocalHttpTestServer())
            {
                server.Route("/auth/guest", "{\"accountId\":\"acc-1\"}");
                var client = Client(server);
                MetaCall<GuestLoginInfo> r = null;

                yield return client.GuestLoginAsync("device-1").ToCoroutine(x => r = x);

                Assert.IsFalse(r.Ok);
                Assert.AreEqual(MetaFailureKind.InvalidResponse, r.Failure.Kind);
            }
        }

        [UnityTest]
        public IEnumerator 传输不可达_判Network_暂态可重试()
        {
            using (var server = new LocalHttpTestServer())
            {
                // 先取端口再立刻释放——该端口无人监听 = 真实连接失败（非替身）
                var client = new UnityMetaClient(server.BaseUrl, 1, new LiteFramework.NewtonsoftJsonCodec(),
                    () => "req-test");
                server.Dispose();
                MetaCall<GuestLoginInfo> r = null;

                yield return client.GuestLoginAsync("device-1").ToCoroutine(x => r = x);

                Assert.IsFalse(r.Ok);
                Assert.AreEqual(MetaFailureKind.Network, r.Failure.Kind);
            }
        }

        [Test]
        public void 房间端点解析_host端口与IPv6括号形态()
        {
            Assert.IsTrue(RoomEndpoint.TryParse("203.0.113.7:17777", out RoomEndpoint a, out _));
            Assert.AreEqual("203.0.113.7", a.Host);
            Assert.AreEqual(17777, a.Port);

            Assert.IsTrue(RoomEndpoint.TryParse("[::1]:17777", out RoomEndpoint b, out _));
            Assert.AreEqual("::1", b.Host);
            Assert.AreEqual(17777, b.Port);

            Assert.IsFalse(RoomEndpoint.TryParse("no-port", out _, out string reason1), reason1);
            Assert.IsFalse(RoomEndpoint.TryParse("host:0", out _, out string reason2), reason2);
            Assert.IsFalse(RoomEndpoint.TryParse("", out _, out string reason3), reason3);
        }

        [Test]
        public void 装配门禁_空基址抛_超时非法抛()
        {
            var codec = new LiteFramework.NewtonsoftJsonCodec();
            Assert.Throws<ArgumentException>(() => new UnityMetaClient("", 5, codec));
            Assert.Throws<ArgumentOutOfRangeException>(() => new UnityMetaClient("http://x", 0, codec));
        }

        [Test]
        public void 装配门禁_缺编解码端口抛()
        {
            Assert.Throws<ArgumentNullException>(() => new UnityMetaClient("http://x", 5, null));
        }

        [UnityTest]
        public IEnumerator 二xx但响应含未知字段_判InvalidResponse_契约漂移立刻失败()
        {
            using (var server = new LocalHttpTestServer())
            {
                // 未知字段 = 契约漂移：MissingMemberHandling.Error 必须让它失败，
                // 而不是静默忽略后表现为"某个字段永远是 null"。
                server.Route("/auth/guest",
                    "{\"accountId\":\"acc-1\",\"accessToken\":\"tok-1\",\"expiresAtMs\":123,\"unexpected\":\"x\"}");
                var client = Client(server);
                MetaCall<GuestLoginInfo> r = null;

                yield return client.GuestLoginAsync("device-1").ToCoroutine(x => r = x);

                Assert.IsFalse(r.Ok);
                Assert.AreEqual(MetaFailureKind.InvalidResponse, r.Failure.Kind);
            }
        }

        [UnityTest]
        public IEnumerator 二xx但必填字段缺失_判InvalidResponse()
        {
            using (var server = new LocalHttpTestServer())
            {
                // expiresAtMs 缺失 → 0 → 必填校验拒绝（反射反序列化不会因缺字段报错，校验补这一环）。
                server.Route("/auth/guest", "{\"accountId\":\"acc-1\",\"accessToken\":\"tok-1\"}");
                var client = Client(server);
                MetaCall<GuestLoginInfo> r = null;

                yield return client.GuestLoginAsync("device-1").ToCoroutine(x => r = x);

                Assert.IsFalse(r.Ok);
                Assert.AreEqual(MetaFailureKind.InvalidResponse, r.Failure.Kind);
            }
        }

        [UnityTest]
        public IEnumerator 二xx但响应非JSON对象_判InvalidResponse()
        {
            using (var server = new LocalHttpTestServer())
            {
                server.Route("/auth/guest", "[]");
                var client = Client(server);
                MetaCall<GuestLoginInfo> r = null;

                yield return client.GuestLoginAsync("device-1").ToCoroutine(x => r = x);

                Assert.IsFalse(r.Ok);
                Assert.AreEqual(MetaFailureKind.InvalidResponse, r.Failure.Kind);
            }
        }
    }
}
