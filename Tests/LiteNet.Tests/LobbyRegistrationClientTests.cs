using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LiteTesting;
using RoomServer;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// Lobby 实例注册客户端（《Meta 服务专项设计》§7 实例注册/心跳；《上云测试专项设计》§5）。
    ///
    /// L1：伪造 <see cref="HttpMessageHandler"/>——断言**发送的载荷与鉴权头**（客户端与 Meta 端点
    /// 的 JSON 契约由 L3 闭环用例再用真 HTTP 复验）与失败语义（不抛、计数、对局服务不受影响）。
    /// </summary>
    [Trait(TestTrait.Category, TestCategory.Unit)]
    public sealed class LobbyRegistrationClientTests
    {
        private static readonly byte[] Key = Filled(0x11);

        private static byte[] Filled(byte value)
        {
            var bytes = new byte[32];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = value;
            return bytes;
        }

        private sealed class FakeHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

            public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
            {
                _respond = respond;
            }

            public int Calls;
            public HttpMethod LastMethod;
            public string LastUrl;
            public string LastAuth;
            public string LastBody;

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                Calls++;
                LastMethod = request.Method;
                LastUrl = request.RequestUri.ToString();
                LastAuth = request.Headers.TryGetValues("Authorization", out IEnumerable<string> values)
                    ? string.Join(",", values)
                    : null;
                LastBody = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
                return _respond(request);
            }
        }

        private static LobbyRegistrationClient.Settings Settings(int intervalMs = 10_000)
        {
            return new LobbyRegistrationClient.Settings
            {
                Url = "http://127.0.0.1:9/lobby/instances/register",
                InstanceId = "room-1",
                InstanceKey = Key,
                AdvertiseHost = "203.0.113.7",
                HeartbeatIntervalMs = intervalMs,
                RequestTimeoutMs = 2_000,
            };
        }

        private static LobbyRegistrationClient.Snapshot Snapshot(bool draining = false,
            int playerCount = 5, int port = 17777)
        {
            return new LobbyRegistrationClient.Snapshot
            {
                BuildHash = "hash-x",
                MaxRooms = 8,
                RoomCount = 2,
                MaxPlayers = 64,
                PlayerCount = playerCount,
                Draining = draining,
                Port = port,
            };
        }

        private static HttpResponseMessage Ok(string json)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        }

        [Fact]
        public void 心跳_载荷字段与鉴权头正确_地址用实际端口()
        {
            var handler = new FakeHandler(_ => Ok("{\"heartbeatTtlMs\":30000}"));
            using var client = new LobbyRegistrationClient(Settings(), () => Snapshot(), _ => { }, handler);

            Assert.True(client.TrySendOnce(out string error), error);

            Assert.Equal(1, handler.Calls);
            Assert.Equal(HttpMethod.Post, handler.LastMethod);
            Assert.Equal("http://127.0.0.1:9/lobby/instances/register", handler.LastUrl);
            Assert.Equal("Bearer " + Convert.ToBase64String(Key), handler.LastAuth);

            using var payload = JsonDocument.Parse(handler.LastBody);
            Assert.Equal("room-1", payload.RootElement.GetProperty("instanceId").GetString());
            Assert.Equal("hash-x", payload.RootElement.GetProperty("buildHash").GetString());
            Assert.Equal("203.0.113.7:17777", payload.RootElement.GetProperty("address").GetString());
            Assert.Equal(8, payload.RootElement.GetProperty("maxRooms").GetInt32());
            Assert.Equal(2, payload.RootElement.GetProperty("roomCount").GetInt32());
            Assert.Equal(64, payload.RootElement.GetProperty("maxPlayers").GetInt32());
            Assert.Equal(5, payload.RootElement.GetProperty("playerCount").GetInt32());
            Assert.False(payload.RootElement.GetProperty("draining").GetBoolean());
        }

        [Fact]
        public void 心跳_成功_回告时限被记录_计数增长()
        {
            var handler = new FakeHandler(_ => Ok("{\"heartbeatTtlMs\":30000}"));
            using var client = new LobbyRegistrationClient(Settings(), () => Snapshot(), _ => { }, handler);

            Assert.True(client.TrySendOnce(out _));
            Assert.True(client.TrySendOnce(out _));

            Assert.Equal(2, client.Sent);
            Assert.Equal(0, client.Failed);
            Assert.Equal(30_000, client.LastHeartbeatTtlMs);
        }

        [Fact]
        public void 心跳_排空位随快照透传()
        {
            var handler = new FakeHandler(_ => Ok("{\"heartbeatTtlMs\":30000}"));
            using var client = new LobbyRegistrationClient(Settings(), () => Snapshot(draining: true), _ => { }, handler);

            Assert.True(client.TrySendOnce(out _));

            using var payload = JsonDocument.Parse(handler.LastBody);
            Assert.True(payload.RootElement.GetProperty("draining").GetBoolean());
        }

        [Fact]
        public void 心跳_HTTP拒绝_失败计数_不抛()
        {
            var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
            using var client = new LobbyRegistrationClient(Settings(), () => Snapshot(), _ => { }, handler);

            Assert.False(client.TrySendOnce(out string error));
            Assert.Equal("HTTP 401", error);
            Assert.Equal(1, client.Failed);
            Assert.Equal(0, client.Sent);
        }

        [Fact]
        public void 心跳_传输异常_失败计数_不抛()
        {
            var handler = new FakeHandler(_ => throw new HttpRequestException("connection refused"));
            using var client = new LobbyRegistrationClient(Settings(), () => Snapshot(), _ => { }, handler);

            Assert.False(client.TrySendOnce(out string error));
            Assert.Contains("HttpRequestException", error);
            Assert.Equal(1, client.Failed);
        }

        [Fact]
        public void 心跳_间隔过密_仅告警一次()
        {
            var logs = new List<string>();
            var handler = new FakeHandler(_ => Ok("{\"heartbeatTtlMs\":1000}"));
            using var client = new LobbyRegistrationClient(Settings(intervalMs: 1_000),
                () => Snapshot(), logs.Add, handler);

            Assert.True(client.TrySendOnce(out _));
            Assert.True(client.TrySendOnce(out _));

            int warnings = 0;
            foreach (string line in logs)
                if (line.Contains("警告")) warnings++;
            Assert.Equal(1, warnings);
        }

        [Fact]
        public void 构造_坏URL或缺密钥_拒绝()
        {
            LobbyRegistrationClient.Settings settings = Settings();
            settings.Url = "not-a-url";
            Assert.ThrowsAny<Exception>(() =>
                new LobbyRegistrationClient(settings, () => Snapshot(), _ => { }, new FakeHandler(_ => Ok("{}"))));

            settings = Settings();
            settings.InstanceKey = null;
            Assert.ThrowsAny<Exception>(() =>
                new LobbyRegistrationClient(settings, () => Snapshot(), _ => { }, new FakeHandler(_ => Ok("{}"))));
        }
    }
}
