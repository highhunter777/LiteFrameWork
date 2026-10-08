using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using LiteTesting;
using MetaServer.Contracts.Lobby;
using MetaServer.Modules.Auth;
using Microsoft.Extensions.DependencyInjection;
using RoomServer;
using RoomServer.Application;
using Xunit;

namespace MetaServer.Tests
{
    /// <summary>
    /// Lobby HTTP 端到端（《Meta 服务专项设计》§7、《上云测试专项设计》§2）：
    /// 实例注册 → 容量分配 → Join Ticket 签发 → **房间端验证器验签**闭环。
    ///
    /// L3：真实 Kestrel + 真实回环 HTTP（技法同 <see cref="MetaHostTests"/>）；验签用房间端真实
    /// <see cref="HmacJoinTicketValidator"/>——HTTP 载荷与票据格式的联合契约在此钉死。
    /// </summary>
    [Trait(TestTrait.Category, TestCategory.Integration)]
    public sealed class LobbyHttpTests
    {
        private const string Kid = "l1";
        private const string Audience = "lobby";
        private const string BuildHash = "hash-x";
        private const string Room = "ffa-1";

        private static readonly byte[] InstanceKey = Filled(0x11);
        private static readonly byte[] TicketKey = Filled(0x22);
        private static readonly byte[] AuthKey = Filled(0x33);

        private static byte[] Filled(byte value)
        {
            var bytes = new byte[32];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = value;
            return bytes;
        }

        /// <summary>Lobby+Auth 全量密钥的宿主覆盖项（各用例按需追加）。</summary>
        private static Dictionary<string, string> LobbyOverrides()
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [MetaHost.ConfigSection + ":AuthSigningKeyBase64"] = Convert.ToBase64String(AuthKey),
                [MetaHost.ConfigSection + ":LobbyInstanceKeyBase64"] = Convert.ToBase64String(InstanceKey),
                [MetaHost.ConfigSection + ":LobbyTicketKeyBase64"] = Convert.ToBase64String(TicketKey),
                [MetaHost.ConfigSection + ":LobbyTicketKeyId"] = Kid,
                [MetaHost.ConfigSection + ":LobbyTicketAudience"] = Audience,
            };
        }

        private static Task<MetaHostFixture> StartLobbyHostAsync(Action<Dictionary<string, string>> extra = null)
        {
            return MetaHostFixture.StartAsync(map =>
            {
                foreach (KeyValuePair<string, string> kv in LobbyOverrides()) map[kv.Key] = kv.Value;
                extra?.Invoke(map);
            });
        }

        private static StringContent Json(string body)
        {
            return new StringContent(body, Encoding.UTF8, "application/json");
        }

        private static HttpRequestMessage Authorized(HttpMethod method, string url, string bearer, string body = null)
        {
            var request = new HttpRequestMessage(method, url);
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + bearer);
            if (body != null) request.Content = Json(body);
            return request;
        }

        private static string RegisterBody(string instanceId = "r1", string buildHash = BuildHash,
            string address = "10.0.0.5:7777", int maxRooms = 8, int roomCount = 0,
            int maxPlayers = 64, int playerCount = 0, bool draining = false)
        {
            return "{\"instanceId\":\"" + instanceId + "\",\"buildHash\":\"" + buildHash
                + "\",\"address\":\"" + address + "\",\"maxRooms\":" + maxRooms
                + ",\"roomCount\":" + roomCount + ",\"maxPlayers\":" + maxPlayers
                + ",\"playerCount\":" + playerCount + ",\"draining\":" + (draining ? "true" : "false") + "}";
        }

        private static string JoinTicketBody(string requestId = "req-1", string roomId = Room, string buildHash = BuildHash)
        {
            return "{\"requestId\":\"" + requestId + "\",\"roomId\":\"" + roomId + "\""
                + (buildHash == null ? "" : ",\"buildHash\":\"" + buildHash + "\"") + "}";
        }

        private static async Task<HttpResponseMessage> RegisterAsync(HttpClient http, string baseAddress,
            string body, string bearer)
        {
            return await http.SendAsync(Authorized(HttpMethod.Post,
                baseAddress + "/lobby/instances/register", bearer, body));
        }

        [Fact]
        public async Task 注册_成功_回告心跳时限_投影可查_指标增长()
        {
            await using MetaHostFixture host = await StartLobbyHostAsync();
            using var http = new HttpClient();
            string key = Convert.ToBase64String(InstanceKey);

            HttpResponseMessage response = await RegisterAsync(http, host.BaseAddress,
                RegisterBody(address: "203.0.113.7:17777"), key);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            string json = await response.Content.ReadAsStringAsync();
            Assert.Contains("\"heartbeatTtlMs\":30000", json);

            // 投影查询（同凭据）：上报事实逐项回读
            HttpResponseMessage rooms = await http.SendAsync(Authorized(HttpMethod.Get,
                host.BaseAddress + "/lobby/rooms", key));
            Assert.Equal(HttpStatusCode.OK, rooms.StatusCode);
            string roomsJson = await rooms.Content.ReadAsStringAsync();
            Assert.Contains("\"instanceId\":\"r1\"", roomsJson);
            Assert.Contains("\"address\":\"203.0.113.7:17777\"", roomsJson);
            Assert.Contains("\"lastSeenAgeMs\":", roomsJson);

            Assert.True(host.Ops.LobbyInstanceRegisters >= 1);
            string metrics = await http.GetStringAsync(host.BaseAddress + "/metrics");
            Assert.Contains("meta_lobby_instance_register_total", metrics);
        }

        [Fact]
        public async Task 注册_凭据缺失或错误_401()
        {
            await using MetaHostFixture host = await StartLobbyHostAsync();
            using var http = new HttpClient();

            HttpResponseMessage missing = await http.PostAsync(host.BaseAddress + "/lobby/instances/register",
                Json(RegisterBody()));
            Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
            Assert.Contains(LobbyErrorCodes.InstanceUnauthorized, await missing.Content.ReadAsStringAsync());

            HttpResponseMessage wrong = await RegisterAsync(http, host.BaseAddress,
                RegisterBody(), Convert.ToBase64String(Filled(0x99)));
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);

            HttpResponseMessage rooms = await http.GetAsync(host.BaseAddress + "/lobby/rooms");
            Assert.Equal(HttpStatusCode.Unauthorized, rooms.StatusCode);
        }

        [Fact]
        public async Task 注册_字段越界_400_带字段名()
        {
            await using MetaHostFixture host = await StartLobbyHostAsync();
            using var http = new HttpClient();
            string key = Convert.ToBase64String(InstanceKey);

            HttpResponseMessage response = await RegisterAsync(http, host.BaseAddress,
                RegisterBody(maxRooms: 0), key);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            string json = await response.Content.ReadAsStringAsync();
            Assert.Contains(LobbyErrorCodes.InvalidRequest, json);
            Assert.Contains("maxRooms", json);                  // 字段名定位（非敏感）——但字段值不回显
        }

        [Fact]
        public async Task 注册_表满_拒新实例_503()
        {
            await using MetaHostFixture host = await StartLobbyHostAsync(map =>
                map[MetaHost.ConfigSection + ":LobbyRegistryCapacity"] = "1");
            using var http = new HttpClient();
            string key = Convert.ToBase64String(InstanceKey);

            Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(http, host.BaseAddress, RegisterBody("a"), key)).StatusCode);

            HttpResponseMessage second = await RegisterAsync(http, host.BaseAddress, RegisterBody("b"), key);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, second.StatusCode);
            Assert.Contains(LobbyErrorCodes.RegistryFull, await second.Content.ReadAsStringAsync());

            // 既有实例心跳不受满拒影响
            Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(http, host.BaseAddress, RegisterBody("a"), key)).StatusCode);
        }

        [Fact]
        public async Task 签票_真令牌鉴权_签发结果经房间端验证器闭环()
        {
            await using MetaHostFixture host = await StartLobbyHostAsync();
            using var http = new HttpClient();
            string key = Convert.ToBase64String(InstanceKey);

            Assert.Equal(HttpStatusCode.OK,
                (await RegisterAsync(http, host.BaseAddress, RegisterBody(address: "10.0.0.5:7777"), key)).StatusCode);

            string token = host.Services.GetRequiredService<AccessTokenService>().Issue("acc-7", out _);

            HttpResponseMessage response = await http.SendAsync(Authorized(HttpMethod.Post,
                host.BaseAddress + "/lobby/join-ticket", token, JoinTicketBody()));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            string json = await response.Content.ReadAsStringAsync();
            Assert.Contains("\"instanceId\":\"r1\"", json);
            Assert.Contains("\"address\":\"10.0.0.5:7777\"", json);
            Assert.Contains("\"roomId\":\"" + Room + "\"", json);

            // 取出票据 → 房间端**真实**验证器验签（跨端单源闭环；不命名重复的格式类型）
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            string ticket = doc.RootElement.GetProperty("ticket").GetString();
            var validator = new HmacJoinTicketValidator(new[] { new JoinTicketKey(Kid, TicketKey) });
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            JoinPrincipal principal = validator.Validate(ticket, new JoinContext(Room, BuildHash, Audience, nowMs));
            Assert.True(principal.IsValid);
            Assert.Equal("acc-7", principal.AccountId);

            // nonce 一次性：重放必拒
            Assert.Equal(JoinTicketRejection.Replayed,
                validator.Validate(ticket, new JoinContext(Room, BuildHash, Audience, nowMs + 1)).Rejection);

            Assert.True(host.Ops.TicketsIssued >= 1);
            string metrics = await http.GetStringAsync(host.BaseAddress + "/metrics");
            Assert.Contains("meta_ticket_issue_total", metrics);
        }

        [Fact]
        public async Task 签票_令牌缺失或不可验_401()
        {
            await using MetaHostFixture host = await StartLobbyHostAsync();
            using var http = new HttpClient();

            HttpResponseMessage missing = await http.PostAsync(host.BaseAddress + "/lobby/join-ticket",
                Json(JoinTicketBody()));
            Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
            string missingJson = await missing.Content.ReadAsStringAsync();
            Assert.Contains("auth.unauthorized", missingJson);
            Assert.Contains("Missing", missingJson);            // 拒绝分类进 args（非敏感）

            HttpResponseMessage garbage = await http.SendAsync(Authorized(HttpMethod.Post,
                host.BaseAddress + "/lobby/join-ticket", "not-a-token", JoinTicketBody()));
            Assert.Equal(HttpStatusCode.Unauthorized, garbage.StatusCode);

            // 篡改游客令牌（尾段翻一位）→ 签名不符
            string valid = host.Services.GetRequiredService<AccessTokenService>().Issue("acc-1", out _);
            char[] chars = valid.ToCharArray();
            chars[chars.Length - 1] = chars[chars.Length - 1] == 'A' ? 'B' : 'A';
            HttpResponseMessage tampered = await http.SendAsync(Authorized(HttpMethod.Post,
                host.BaseAddress + "/lobby/join-ticket", new string(chars), JoinTicketBody()));
            Assert.Equal(HttpStatusCode.Unauthorized, tampered.StatusCode);
        }

        [Fact]
        public async Task 签票_版本不符_409_稳定码()
        {
            await using MetaHostFixture host = await StartLobbyHostAsync();
            using var http = new HttpClient();
            string key = Convert.ToBase64String(InstanceKey);
            await RegisterAsync(http, host.BaseAddress, RegisterBody(), key);
            string token = host.Services.GetRequiredService<AccessTokenService>().Issue("acc-7", out _);

            HttpResponseMessage response = await http.SendAsync(Authorized(HttpMethod.Post,
                host.BaseAddress + "/lobby/join-ticket", token, JoinTicketBody(buildHash: "hash-y")));

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Contains(LobbyErrorCodes.VersionMismatch, await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task 签票_无实例_满员_排空_均为503无容量()
        {
            await using MetaHostFixture host = await StartLobbyHostAsync();
            using var http = new HttpClient();
            string key = Convert.ToBase64String(InstanceKey);
            string token = host.Services.GetRequiredService<AccessTokenService>().Issue("acc-7", out _);

            async Task<string> IssueTicket()
            {
                HttpResponseMessage response = await http.SendAsync(Authorized(HttpMethod.Post,
                    host.BaseAddress + "/lobby/join-ticket", token, JoinTicketBody()));
                Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
                return await response.Content.ReadAsStringAsync();
            }

            Assert.Contains(LobbyErrorCodes.NoCapacity, await IssueTicket());       // 无任何实例

            await RegisterAsync(http, host.BaseAddress,
                RegisterBody("full", maxPlayers: 1, playerCount: 1), key);
            Assert.Contains(LobbyErrorCodes.NoCapacity, await IssueTicket());       // 唯一实例满员

            await RegisterAsync(http, host.BaseAddress, RegisterBody("full", maxPlayers: 64), key);
            await RegisterAsync(http, host.BaseAddress, RegisterBody("full", draining: true), key);
            Assert.Contains(LobbyErrorCodes.NoCapacity, await IssueTicket());       // 排空实例不再分配
        }

        [Fact]
        public async Task 签票_实例心跳超时_移出分配集合()
        {
            await using MetaHostFixture host = await StartLobbyHostAsync(map =>
                map[MetaHost.ConfigSection + ":LobbyHeartbeatTtlSeconds"] = "1");
            using var http = new HttpClient();
            string key = Convert.ToBase64String(InstanceKey);
            string token = host.Services.GetRequiredService<AccessTokenService>().Issue("acc-7", out _);

            await RegisterAsync(http, host.BaseAddress, RegisterBody(), key);
            HttpResponseMessage before = await http.SendAsync(Authorized(HttpMethod.Post,
                host.BaseAddress + "/lobby/join-ticket", token, JoinTicketBody()));
            Assert.Equal(HttpStatusCode.OK, before.StatusCode);

            await Task.Delay(1_300);                            // 真实时钟：超过 1s 存活时限

            HttpResponseMessage after = await http.SendAsync(Authorized(HttpMethod.Post,
                host.BaseAddress + "/lobby/join-ticket", token, JoinTicketBody(requestId: "req-2")));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, after.StatusCode);
            Assert.Contains(LobbyErrorCodes.NoCapacity, await after.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task 未配置Lobby_稳定503关闭()
        {
            await using MetaHostFixture host = await MetaHostFixture.StartAsync();
            using var http = new HttpClient();

            HttpResponseMessage register = await http.SendAsync(Authorized(HttpMethod.Post,
                host.BaseAddress + "/lobby/instances/register", "any-key", RegisterBody()));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, register.StatusCode);
            Assert.Contains(LobbyErrorCodes.Disabled, await register.Content.ReadAsStringAsync());

            HttpResponseMessage issue = await http.PostAsync(host.BaseAddress + "/lobby/join-ticket",
                Json(JoinTicketBody()));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, issue.StatusCode);
            Assert.Contains(LobbyErrorCodes.Disabled, await issue.Content.ReadAsStringAsync());

            HttpResponseMessage rooms = await http.GetAsync(host.BaseAddress + "/lobby/rooms");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, rooms.StatusCode);
        }

        [Fact]
        public async Task 全链闭环_真注册客户端_真Meta_房间端验签()
        {
            await using MetaHostFixture host = await StartLobbyHostAsync();

            // 真 LobbyRegistrationClient（真 HttpClient 打真 Kestrel）——客户端载荷 JSON 与
            // Meta DTO 的命名契约在此合拢（替身 handler 与手写 JSON 的用例各自只覆盖半边）。
            var settings = new LobbyRegistrationClient.Settings
            {
                Url = host.BaseAddress + "/lobby/instances/register",
                InstanceId = "room-live",
                InstanceKey = InstanceKey,
                AdvertiseHost = "10.9.9.9",
                HeartbeatIntervalMs = 10_000,
                RequestTimeoutMs = 2_000,
            };
            using var client = new LobbyRegistrationClient(settings, () => new LobbyRegistrationClient.Snapshot
            {
                BuildHash = BuildHash,
                MaxRooms = 8,
                RoomCount = 0,
                MaxPlayers = 64,
                PlayerCount = 0,
                Draining = false,
                Port = 17777,
            }, _ => { });

            Assert.True(client.TrySendOnce(out string error), error);
            Assert.Equal(30_000, client.LastHeartbeatTtlMs);

            using var http = new HttpClient();
            string token = host.Services.GetRequiredService<AccessTokenService>().Issue("acc-9", out _);
            HttpResponseMessage response = await http.SendAsync(Authorized(HttpMethod.Post,
                host.BaseAddress + "/lobby/join-ticket", token, JoinTicketBody(requestId: "req-full")));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("room-live", doc.RootElement.GetProperty("instanceId").GetString());
            Assert.Equal("10.9.9.9:17777", doc.RootElement.GetProperty("address").GetString());
            string ticket = doc.RootElement.GetProperty("ticket").GetString();

            var validator = new HmacJoinTicketValidator(new[] { new JoinTicketKey(Kid, TicketKey) });
            JoinPrincipal principal = validator.Validate(ticket,
                new JoinContext(Room, BuildHash, Audience, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
            Assert.True(principal.IsValid);
            Assert.Equal("acc-9", principal.AccountId);
        }
    }
}
