using LiteNet.Protocol;
using LiteNet.Proto;
using LiteNet.Transport;
using RoomServer;
using RoomServer.Application;
using RoomServer.Runtime;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// 分层限流·宿主接线用例（R2 安全批③）：限流点必须**真实经过** Join / Reconnect / OnConnected / 入包
    /// 四条宿主路径（不是"类单测过了就算接线过了"）。IP 维度用 <see cref="FakeRoomTransport.SetRemoteAddress"/>
    /// 供键；未设地址 = 探测不到 → 跳过 IP 维度（端口契约）。各用例只放激被测 lane，其余 lane 给足余量
    /// 以免混淆失败面。
    /// </summary>
    public sealed class ServerHostRateLimitTests
    {
        private static readonly RateLimitSettings.Lane Big = new RateLimitSettings.Lane(10_000, 0);
        private static readonly RateLimitSettings.Lane One = new RateLimitSettings.Lane(1, 0);

        private static RateLimitSettings Lanes(RateLimitSettings.Lane ipConnect, RateLimitSettings.Lane entry,
            RateLimitSettings.Lane account, RateLimitSettings.Lane session)
        {
            return new RateLimitSettings(ipConnect, entry, account, session, buckets: 8192, idleTtlMs: 120_000);
        }

        private static byte[] JoinPacket(string room, string token = "t")
        {
            return PacketCodec.Encode(PacketType.Join, new JoinRequest
            {
                RoomId = room,
                Token = token,
                BuildHash = ServerHost.ServerBuildHash,
            });
        }

        [Fact]
        public void IP连接限流_同址第二连接被断开_空地址放行()
        {
            var t = new FakeRoomTransport();
            using var host = new ServerHost(t, new RoomConfig { Port = 40211, RoomId = "RateConnect" },
                rateLimiter: new RateLimiter(Lanes(One, Big, Big, Big)));

            t.SetRemoteAddress(1, "9.9.9.9");
            t.SetRemoteAddress(2, "9.9.9.9");
            t.RaiseConnected(1);
            t.RaiseConnected(2);

            Assert.Equal(1, host.Sessions.Count);                 // 同址第二连接未登记
            Assert.Contains(2, t.Disconnects);                    // 被断开（同会话容量上限形态）
            Assert.Equal(1, host.RateLimit.RejectedIpConnect);

            t.RaiseConnected(3);                                  // 未设地址（探测不到）→ 放行
            Assert.Equal(2, host.Sessions.Count);
            Assert.Equal(1, host.RateLimit.BucketCount);          // 空地址不建桶（3 号连接）
        }

        [Fact]
        public void IP入场限流_同址第二Join被拒()
        {
            var t = new FakeRoomTransport();
            using var host = new ServerHost(t, new RoomConfig { Port = 40212, RoomId = "RateEntry" },
                rateLimiter: new RateLimiter(Lanes(Big, One, Big, Big)));

            t.SetRemoteAddress(1, "9.9.9.9");
            t.SetRemoteAddress(2, "9.9.9.9");
            t.RaiseConnected(1);
            t.RaiseConnected(2);

            t.RaiseData(1, JoinPacket("RateEntry"));
            Assert.NotNull(t.LastJoinAck(1));                     // 首个 Join 消耗入场令牌

            t.RaiseData(2, JoinPacket("RateEntry"));
            Assert.Null(t.LastJoinAck(2));                        // 同址第二 Join 被限（不发明文 JoinAck）
            Assert.Equal(1, host.RateLimit.RejectedIpEntry);
            Assert.Equal(1, host.Ops.Rejects);
        }

        [Fact]
        public void 账号入场限流_同账号第二张票被拒()
        {
            var issuer = TestTicketIssuer.Random("k1");
            var t = new FakeRoomTransport();
            using var host = new ServerHost(t,
                new RoomConfig { Port = 40213, RoomId = "AcctRoom", ExpectedPlayers = 2 },
                new HmacJoinTicketValidator(new[] { issuer.AsValidatorKey() }), "aud",
                rateLimiter: new RateLimiter(Lanes(Big, Big, One, Big)));

            t.SetRemoteAddress(1, "1.1.1.1");
            t.SetRemoteAddress(2, "2.2.2.2");                     // 不同来源 IP：排除 IP 维度混淆
            t.RaiseConnected(1);
            t.RaiseConnected(2);

            const long now = 5_000_000;
            t.RaiseData(1, PacketCodec.Encode(PacketType.Join, new JoinRequest
            {
                RoomId = "AcctRoom",
                Token = issuer.Issue("p1", "AcctRoom", ServerHost.ServerBuildHash, now,
                    audience: "aud", accountId: "acct-1"),
                BuildHash = ServerHost.ServerBuildHash,
            }));
            Assert.NotNull(t.LastJoinAck(1));                     // 同账号首张票放行（验签后记账）

            t.RaiseData(2, PacketCodec.Encode(PacketType.Join, new JoinRequest
            {
                RoomId = "AcctRoom",
                Token = issuer.Issue("p2", "AcctRoom", ServerHost.ServerBuildHash, now,
                    audience: "aud", accountId: "acct-1"),
                BuildHash = ServerHost.ServerBuildHash,
            }));
            Assert.Null(t.LastJoinAck(2));                        // 同账号第二张票被限
            Assert.Equal(1, host.RateLimit.RejectedAccountEntry);
            Assert.Equal(1, host.Ops.Rejects);
        }

        [Fact]
        public void 重连入场限流_超限请求静默丢弃()
        {
            var t = new FakeRoomTransport();
            using var host = new ServerHost(t, new RoomConfig { Port = 40214, RoomId = "RateReconnect" },
                rateLimiter: new RateLimiter(Lanes(Big, One, Big, Big)));

            t.SetRemoteAddress(1, "9.9.9.9");
            t.RaiseConnected(1);

            byte[] request = PacketCodec.Encode(PacketType.ReconnectRequest,
                new ReconnectRequest { OneTimeToken = "nope" });

            t.RaiseData(1, request);                              // ① 消耗入场令牌 → 回"票据无效"（可见响应）
            Assert.Equal(1, t.CountOf(1, PacketType.ReconnectResponse));

            t.RaiseData(1, request);                              // ② 令牌耗尽 → 静默丢弃（洪水不放大）
            Assert.Equal(1, t.CountOf(1, PacketType.ReconnectResponse));
            Assert.Equal(1, host.RateLimit.RejectedIpEntry);
        }

        [Fact]
        public void Session包限流_超速包静默丢弃_前段包照常处理()
        {
            var t = new FakeRoomTransport();
            using var host = new ServerHost(t, new RoomConfig { Port = 40215, RoomId = "RatePackets" },
                rateLimiter: new RateLimiter(Lanes(Big, Big, Big, new RateLimitSettings.Lane(4, 0))));

            t.RaiseConnected(1);
            // buildHash 故意不符：前 4 包走正常拒绝路径（可数），第 5 包应被包速率闸门静默丢弃
            for (int i = 0; i < 5; i++)
            {
                t.RaiseData(1, PacketCodec.Encode(PacketType.Join, new JoinRequest
                {
                    RoomId = "RatePackets",
                    Token = "t",
                    BuildHash = "bad",
                }));
            }

            Assert.Equal(4, host.Ops.Rejects);                    // 前 4 包被处理
            Assert.Equal(1, host.RateLimit.RejectedSessionPackets);   // 第 5 包被限流丢弃
        }

        [Fact]
        public void 配置分区接入宿主_Session桶按配置生效()
        {
            // 端到端接缝：RoomServerConfig.rate_limit → ServerHost.RateLimit（不是"两边各自单测过了"）。
            var cfg = RoomServerConfig.Parse(@"{
                ""port"": 40216, ""max_rooms"": 2,
                ""combat"": [ { ""id"":1, ""move_speed"":5, ""gravity"":-20, ""hitscan_range"":100,
                             ""hitscan_radius"":0.5, ""hitscan_height"":2, ""base_damage"":25,
                             ""damage_spread"":1, ""entity_hp"":100 } ],
                ""rooms"": { ""default"": { ""expected_players"": 2 } },
                ""rate_limit"": { ""session_burst"": 1, ""session_per_sec"": 0.1 }
            }");
            var t = new FakeRoomTransport();
            using var host = new ServerHost(t, null, null, null, cfg);

            t.RaiseConnected(1);
            byte[] packet = PacketCodec.Encode(PacketType.Join, new JoinRequest
            {
                RoomId = "X",
                Token = "t",
                BuildHash = "bad",
            });
            t.RaiseData(1, packet);                               // 第 1 包：桶里仅 1 枚，被处理（buildHash 拒）
            t.RaiseData(1, packet);                               // 第 2 包：桶已空（补充率 0.1/s）→ 限流丢弃

            Assert.Equal(1, host.Ops.Rejects);
            Assert.Equal(1, host.RateLimit.RejectedSessionPackets);
        }
    }
}
