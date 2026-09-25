using System;
using System.Collections.Generic;
using LiteTesting;
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
    /// **可替换性验证**（《状态同步实施方案》§4.6 第二刀；2026-09-19 收口）：
    /// `ServerHost` 现在只依赖窄端口 <see cref="IRoomTransport"/>——本用例用**假的传输实现**
    /// 跑通"装配 → 连接 → Join → 满员开局"全链路，证明换传输框架确实只需新写适配器 + 装配一行，
    /// 而不是嘴上说可替换（假件也能跑，才是真的解耦）。
    /// </summary>
    [Trait(TestTrait.Category, TestCategory.Contract)]
    public sealed class ServerHostTransportTests
    {
        [Fact]
        public void 窄端口换实现_ServerHost零改动跑通装配到开局()
        {
            var t = new FakeRoomTransport();
            using var host = new ServerHost(t, new RoomConfig { Port = 33333, RoomId = "Fake" });

            Assert.Equal(33333, t.StartedPort);                        // 构造即 Start（宿主接管传输）

            t.RaiseConnected(1);
            t.RaiseConnected(2);
            t.RaiseData(1, JoinPacket());
            JoinAck ack1 = t.LastJoinAck(1);
            Assert.NotNull(ack1);
            Assert.Equal(0, ack1.PlayerId);
            Assert.False(string.IsNullOrEmpty(ack1.ReconnectToken));   // E3 一次性票据照常发

            t.RaiseData(2, JoinPacket());
            JoinAck ack2 = t.LastJoinAck(2);
            Assert.NotNull(ack2);
            Assert.Equal(1, ack2.PlayerId);

            Assert.True(host.Room.Started, "满员应自动 StartGame（无 MatchMaker）");
            Assert.NotNull(t.LastStartGame(1));                        // 两名成员都收到 StartGame
            Assert.NotNull(t.LastStartGame(2));
            Assert.Equal(2, host.Sessions.Count);
        }

        [Fact]
        public void 窄端口换实现_版本红线与输入路由不受影响()
        {
            var t = new FakeRoomTransport();
            using var host = new ServerHost(t, new RoomConfig { Port = 33334, RoomId = "Fake" });
            t.RaiseConnected(1);

            // buildHash 不符 → 拒绝（不发明文 JoinAck），走 Ops rejects 记账
            t.RaiseData(1, PacketCodec.Encode(PacketType.Join,
                new JoinRequest { RoomId = "Fake", Token = "t", BuildHash = "deadbeef" }));
            Assert.Null(t.LastJoinAck(1));
            Assert.Equal(1, host.Ops.Rejects);

            // 正确 hash → 放行；随后驱动 Pump 不应抛（假传输的 Tick 是 no-op，房间尚未 Start）
            t.RaiseData(1, JoinPacket());
            Assert.NotNull(t.LastJoinAck(1));
            host.Pump();
        }

        [Fact]
        public void 房间号不符或缺失_拒绝进房()
        {
            var t = new FakeRoomTransport();
            using var host = new ServerHost(t, new RoomConfig { Port = 33336, RoomId = "Room-A" });
            t.RaiseConnected(1);

            // ① 请求了别的房间（--room 可配后，这不能再靠"恰好只有一个房间"蒙过去）
            t.RaiseData(1, PacketCodec.Encode(PacketType.Join,
                new JoinRequest { RoomId = "Room-B", Token = "t", BuildHash = ServerHost.ServerBuildHash }));
            Assert.Null(t.LastJoinAck(1));
            Assert.Equal(1, host.Ops.Rejects);
            Assert.Empty(host.Room.MemberIds());           // 连接存在但未占席位（拒绝的是进房，不是连接）

            // ② 房间号缺失
            t.RaiseData(1, PacketCodec.Encode(PacketType.Join,
                new JoinRequest { RoomId = "", Token = "t", BuildHash = ServerHost.ServerBuildHash }));
            Assert.Equal(2, host.Ops.Rejects);
            Assert.Null(t.LastJoinAck(1));

            // ③ 房间号正确 → 放行（红线只挡错房间，不挡正常进房）
            t.RaiseData(1, PacketCodec.Encode(PacketType.Join,
                new JoinRequest { RoomId = "Room-A", Token = "t", BuildHash = ServerHost.ServerBuildHash }));
            Assert.NotNull(t.LastJoinAck(1));
            Assert.Equal(2, host.Ops.Rejects);             // 计数不再增长
        }

        [Fact]
        public void 宿主持有所有权_Dispose释放传输()
        {
            var t = new FakeRoomTransport();
            var host = new ServerHost(t, new RoomConfig { Port = 33335, RoomId = "Fake" });
            host.Dispose();
            Assert.True(t.Disposed, "ServerHost 接管传输所有权：Dispose 应释放它");
        }

        // ---- R0-A 边界（《商业级通用服务端框架总设计》§5 P0-3）----

        [Fact]
        public void 超长入包_解析前硬边界拒绝并计数_不崩溃()
        {
            var t = new FakeRoomTransport();
            using var host = new ServerHost(t, new RoomConfig { Port = 33337, RoomId = "Fake" });
            t.RaiseConnected(1);

            // 超过 MaxInboundPacketBytes 的"Join"包（内容合法但体量越界）——解析前直接丢弃
            var oversized = new byte[ServerHost.MaxInboundPacketBytes + 1];
            oversized[0] = (byte)PacketType.Join;
            t.RaiseData(1, oversized);

            Assert.Equal(1, host.Ops.PacketOversized);
            Assert.Equal(0, host.Ops.Rejects);
            Assert.Null(t.LastJoinAck(1));                       // 未进房（且 proto 解析器从未见到这个包）

            // 边界内正常包照常处理
            t.RaiseData(1, JoinPacket());
            Assert.NotNull(t.LastJoinAck(1));
        }

        [Fact]
        public void Join字段超长_按UTF8字节上限拒绝()
        {
            var t = new FakeRoomTransport();
            using var host = new ServerHost(t, new RoomConfig { Port = 33338, RoomId = "Room-A" });
            t.RaiseConnected(1);

            // token 超 256 UTF-8 字节（多字节字符——字节数 ≠ 字符数）
            string longToken = new string('年', ServerHost.MaxTokenBytes / 3 + 1);
            t.RaiseData(1, PacketCodec.Encode(PacketType.Join,
                new JoinRequest { RoomId = "Room-A", Token = longToken, BuildHash = ServerHost.ServerBuildHash }));
            Assert.Null(t.LastJoinAck(1));
            Assert.Equal(1, host.Ops.Rejects);

            // roomId 超 64 字节
            string longRoom = new string('r', ServerHost.MaxRoomIdBytes + 1);
            t.RaiseData(1, PacketCodec.Encode(PacketType.Join,
                new JoinRequest { RoomId = longRoom, Token = "t", BuildHash = ServerHost.ServerBuildHash }));
            Assert.Equal(2, host.Ops.Rejects);

            // 合法边界值（恰好 ≤ 上限）放行
            string maxToken = new string('t', ServerHost.MaxTokenBytes);
            t.RaiseData(1, PacketCodec.Encode(PacketType.Join,
                new JoinRequest { RoomId = "Room-A", Token = maxToken, BuildHash = ServerHost.ServerBuildHash }));
            Assert.NotNull(t.LastJoinAck(1));
            Assert.Equal(2, host.Ops.Rejects);                   // 不再增长
        }

        [Fact]
        public void 坏包计数_未知类型与随机字节不污染会话()
        {
            var t = new FakeRoomTransport();
            using var host = new ServerHost(t, new RoomConfig { Port = 33339, RoomId = "Fake" });
            t.RaiseConnected(1);

            t.RaiseData(1, new byte[] { 200, 1, 2, 3 });          // 未定义 packet type
            t.RaiseData(1, new byte[] { (byte)PacketType.Join, 0xFF, 0xFF, 0xFF }); // proto 解析失败
            t.RaiseData(1, System.Array.Empty<byte>());           // 空包

            Assert.Equal(3, host.Ops.PacketRejects);
            Assert.Null(t.LastJoinAck(1));
            Assert.Equal(0, host.Ops.Rejects);                    // 坏包是丢弃不是进房拒绝

            // 之后正常包照常处理（会话未被坏包污染）
            t.RaiseData(1, JoinPacket());
            Assert.NotNull(t.LastJoinAck(1));
        }

        // ---- R1 批③：重连闭环（§9.2/§9.3；App+Runtime 全链，假传输零 Socket）----

        [Fact]
        public void 重连闭环_重绑Restoring抑制增量_恢复ACK后整帧全量重锚()
        {
            var t = new FakeRoomTransport();
            using var host = new ServerHost(t, new RoomConfig { Port = 33341, RoomId = "Fake", ExpectedPlayers = 2 });
            t.RaiseConnected(1);
            t.RaiseConnected(2);
            t.RaiseData(1, JoinPacket());
            t.RaiseData(2, JoinPacket());
            Assert.True(host.Room.Started, "满员自动开局");
            string token = t.LastJoinAck(1).ReconnectToken;

            // 跑帧建立广播基线（stride=2：第 2/4 次 Pump 各广播一次）
            for (int i = 0; i < 4; i++) host.Pump();
            Assert.Equal(2, t.CountOf(1, PacketType.StateSnapshot));

            // conn1 断线：席位保留、对局继续；conn2 广播不受影响
            t.RaiseDisconnected(1);
            int snaps2 = t.CountOf(2, PacketType.StateSnapshot);
            for (int i = 0; i < 4; i++) host.Pump();
            Assert.Equal(SeatPhase.Disconnected, host.Room.SeatOf(0).Phase);
            Assert.Equal(snaps2 + 2, t.CountOf(2, PacketType.StateSnapshot));

            // conn3 凭一次性票据重连：席位 Restoring；响应带版本确认（§9.3 步骤 2）+ 权威全量快照 + 输入历史
            t.RaiseConnected(3);
            t.RaiseData(3, PacketCodec.Encode(PacketType.ReconnectRequest,
                new ReconnectRequest { OneTimeToken = token }));
            var resp = (ReconnectResponse)t.Last(3, PacketType.ReconnectResponse);
            Assert.NotNull(resp);
            Assert.True(resp.Ok);
            Assert.True(resp.Snapshot.IsFull, "重连响应必须是权威全量快照");
            Assert.True(resp.History.Count > 0, "应带回输入历史（§9.3 步骤 5）");
            Assert.Equal(host.Room.Seed, resp.Seed);
            Assert.Equal(host.Room.FixedConfig.Digest, resp.ConfigHash);
            Assert.Equal(ServerHost.ServerBuildHash, resp.BuildHash);
            Assert.Equal(SeatPhase.Restoring, host.Room.SeatOf(0).Phase);
            Assert.Equal(3, host.Room.SeatOf(0).ConnectionId);

            // Restoring 抑制：conn3 无增量；conn2 照常（且未被 fresh 水位拖成全帧）
            for (int i = 0; i < 4; i++) host.Pump();
            Assert.True(t.CountOf(3, PacketType.StateSnapshot) == 0, "恢复完成前不得投增量广播");
            Assert.Equal(snaps2 + 4, t.CountOf(2, PacketType.StateSnapshot));

            // 票据一次性：同票重放被拒（§5.6/E3）
            t.RaiseData(3, PacketCodec.Encode(PacketType.ReconnectRequest,
                new ReconnectRequest { OneTimeToken = token }));
            Assert.False(((ReconnectResponse)t.Last(3, PacketType.ReconnectResponse)).Ok);

            // 恢复完成 ACK → 席位 Active + 整帧全量重锚（抑制期间基线已越过重连快照帧）
            t.RaiseData(3, PacketCodec.Encode(PacketType.RestoreComplete, new RestoreComplete()));
            for (int i = 0; i < 2; i++) host.Pump();
            Assert.Equal(SeatPhase.Active, host.Room.SeatOf(0).Phase);
            Assert.Equal(1, host.Ops.RestoresCompleted);
            var resumed = (StateSnapshot)t.Last(3, PacketType.StateSnapshot);
            Assert.NotNull(resumed);
            Assert.True(resumed.IsFull, "恢复后首包整帧全量（广播链重锚——增量自此无缺口）");
            Assert.Equal(host.Room.AuthSim.Frame, resumed.Frame);
        }

        private static byte[] JoinPacket()
        {
            return PacketCodec.Encode(PacketType.Join,
                new JoinRequest { RoomId = "Fake", Token = "tok", BuildHash = ServerHost.ServerBuildHash });
        }

        /// <summary>
        /// 假传输：只实现窄端口（不碰 kcp2k）——同时验证"端口够不够用"（若 ServerHost 用到端口外的东西，
        /// 本类就编译不过，解耦度自证）。
        /// </summary>
    }

    /// <summary>
    /// Join 票据接缝**接入真实准入路径**的端到端用例（《框架先行》§5-4"运行时票据验证接口与
    /// 非法票据测试"；§6"真实网络链应经过**与运行时一致的验证器**"）。
    ///
    /// <see cref="JoinTicketValidatorTests"/> 验的是验证器本身的判断；本组验的是
    /// **ServerHost 真的在用它**——若只在旁边放个验证器、Join 路径仍只看非空 token，
    /// 前一组依然全绿而接缝形同虚设。
    ///
    /// 走假传输 <see cref="FakeRoomTransport"/>：不碰 kcp2k，纯命令面闭环。
    /// </summary>
    [Trait(TestTrait.Category, TestCategory.Integration)]
    public sealed class JoinTicketAdmissionTests
    {
        private const string Room = "TicketRoom";
        private const long Now = 5_000_000;

        /// <summary>装配了验证器的宿主的 Join 通道（与生产同一条 <see cref="ServerHost.HandleJoin"/> 路径）。</summary>
        private static (ServerHost host, TestTicketIssuer issuer, FakeRoomTransport t) NewHost(string audience = "aud")
        {
            var issuer = TestTicketIssuer.Random("k1");
            var t = new FakeRoomTransport();
            var host = new ServerHost(t, new RoomConfig { Port = 40001, RoomId = Room, ExpectedPlayers = 2 },
                new HmacJoinTicketValidator(new[] { issuer.AsValidatorKey() }), audience);
            return (host, issuer, t);
        }

        private static byte[] JoinWith(string token, string room = Room, string hash = null)
        {
            return PacketCodec.Encode(PacketType.Join, new JoinRequest
            {
                RoomId = room,
                Token = token,
                BuildHash = hash ?? ServerHost.ServerBuildHash,
            });
        }

        [Fact]
        public void 合法票据_经真实Join路径放行()
        {
            var h = NewHost();
            using var host = h.host;
            h.t.RaiseConnected(1);

            h.t.RaiseData(1, JoinWith(h.issuer.Issue("p1", Room, ServerHost.ServerBuildHash, Now,
                audience: "aud")));

            Assert.NotNull(h.t.LastJoinAck(1));
            Assert.Equal(0, h.t.LastJoinAck(1).PlayerId);
            Assert.Equal(0, host.Ops.Rejects);
        }

        [Fact]
        public void 非空但未签名的token_被拒绝_证明非空不再是准入理由()
        {
            // 这是接缝真正接上的判据：装配验证器后，任意非空串**不再**能进房。
            var h = NewHost();
            using var host = h.host;
            h.t.RaiseConnected(1);

            h.t.RaiseData(1, JoinWith("i-am-not-a-ticket"));

            Assert.Null(h.t.LastJoinAck(1));
            Assert.Equal(1, host.Ops.Rejects);
            Assert.Equal(1, host.TicketRejections(JoinTicketRejection.Malformed));
        }

        [Fact]
        public void 过期票据_经真实Join路径拒绝并计入分类()
        {
            var h = NewHost();
            using var host = h.host;
            h.t.RaiseConnected(1);
            // 宿主时钟起于 Stopwatch 归零（构造时的 _nowMs ≈ 0..数十 ms），**不是**用例里的 Now 常量。
            // 故过期票据必须签成"明确早于 0"：nbf=-20000 / exp=-9000 —— 宿主任何时刻都已越过它。
            string expired = h.issuer.Issue("p1", Room, ServerHost.ServerBuildHash, nowMs: -10_000,
                ttlMs: 1_000, notBeforeMs: -20_000, audience: "aud");

            h.t.RaiseData(1, JoinWith(expired));

            Assert.Null(h.t.LastJoinAck(1));
            Assert.Equal(1, host.TicketRejections(JoinTicketRejection.Expired));
        }

        [Fact]
        public void 篡改票据_经真实Join路径拒绝()
        {
            var h = NewHost();
            using var host = h.host;
            h.t.RaiseConnected(1);
            string tampered = h.issuer.IssueThenTamper("p1", Room, ServerHost.ServerBuildHash, Now,
                partIndex: 7, newPartValue: "AAAA");   // buildHash 段

            h.t.RaiseData(1, JoinWith(tampered));

            Assert.Null(h.t.LastJoinAck(1));
            Assert.Equal(1, host.TicketRejections(JoinTicketRejection.BadSignature));
        }

        [Fact]
        public void 重放同一票据_第二次被拒绝()
        {
            var h = NewHost();
            using var host = h.host;
            string ticket = h.issuer.Issue("p1", Room, ServerHost.ServerBuildHash, Now,
                audience: "aud", nonce: "n-replay");

            h.t.RaiseConnected(1);
            h.t.RaiseData(1, JoinWith(ticket));
            Assert.NotNull(h.t.LastJoinAck(1));

            h.t.RaiseConnected(2);
            h.t.RaiseData(2, JoinWith(ticket));

            Assert.Null(h.t.LastJoinAck(2));
            Assert.Equal(1, host.TicketRejections(JoinTicketRejection.Replayed));
        }

        [Fact]
        public void 票据绑定别的房间_与Join房间号不一致即拒绝()
        {
            var h = NewHost();
            using var host = h.host;
            h.t.RaiseConnected(1);
            // 票据签名有效、房间号红线也过（JoinRequest 里房间号正确），但**票据绑定的是别处**
            string ticket = h.issuer.Issue("p1", "OtherRoom", ServerHost.ServerBuildHash, Now, audience: "aud");

            h.t.RaiseData(1, JoinWith(ticket));

            Assert.Null(h.t.LastJoinAck(1));
            Assert.Equal(1, host.TicketRejections(JoinTicketRejection.RoomMismatch));
        }

        [Fact]
        public void 未装配验证器_退回原型非空校验_且身份留空()
        {
            // 兼容形态：历史用例与本地联调不受影响；但 Principal 必须为 null——
            // 未验证不等于已验证（不得留下可被误当身份的残留）。
            var t = new FakeRoomTransport();
            using var host = new ServerHost(t, new RoomConfig { Port = 40002, RoomId = Room, ExpectedPlayers = 2 });
            t.RaiseConnected(1);

            t.RaiseData(1, JoinWith("plain-token"));

            Assert.NotNull(t.LastJoinAck(1));
            Assert.True(host.Sessions.TryGet(1, out Session session));
            Assert.Null(session.Principal);
        }

        [Fact]
        public void 票据连续拒绝_不影响已放行连接与对局()
        {
            var h = NewHost();
            using var host = h.host;
            h.t.RaiseConnected(1);
            h.t.RaiseData(1, JoinWith(h.issuer.Issue("p1", Room, ServerHost.ServerBuildHash, Now, audience: "aud")));
            Assert.NotNull(h.t.LastJoinAck(1));

            h.t.RaiseConnected(2);
            for (int i = 0; i < 5; i++) h.t.RaiseData(2, JoinWith("garbage-" + i));
            Assert.Equal(5, host.TicketRejections(JoinTicketRejection.Malformed));

            // 被拒连接断开不牵连已进房者
            h.t.RaiseDisconnected(2);
            Assert.NotNull(h.t.LastJoinAck(1));
            Assert.True(host.Sessions.TryGet(1, out Session s1));
            Assert.False(s1.Disconnected);
        }

    }
}
