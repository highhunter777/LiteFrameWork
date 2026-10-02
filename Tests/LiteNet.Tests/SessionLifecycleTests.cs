using RoomServer;
using RoomServer.Application;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// 会话生命周期用例（《商业级通用服务端框架总设计》§9.2）：
    /// SessionManager 容量上限与周期清理、ReconnectService 时钟注入/容量、重绑后新会话水位归零
    /// （旧连接 ACK 状态不复活）。纯 L1，无 Socket/墙钟。
    /// </summary>
    public sealed class SessionLifecycleTests
    {
        /// <summary>虚拟单调时钟（与 ServerLoopTests 同款形态：Timestamp/Frequency 语义对齐 Stopwatch）。</summary>
        private sealed class VirtualClock : IMonotonicClock
        {
            public long _ts;
            public long Timestamp => _ts;
            public long Frequency => 1000;   // 1 刻 = 1ms（测试可读）
        }

        // ---- SessionManager 容量与清理 ----

        [Fact]
        public void 会话容量上限_超限拒绝登记()
        {
            var table = new SessionManager(2);
            Assert.True(table.TryAdd(new Session(1, 0)));
            Assert.True(table.TryAdd(new Session(2, 0)));
            Assert.False(table.TryAdd(new Session(3, 0)));            // 超限
            Assert.Equal(2, table.Count);
            Assert.Equal(2, table.Capacity);

            Assert.Null(table.GetOrAddOnFirstPacket(4, 0));           // 首包兜底同样受限
            Assert.NotNull(table.GetOrAddOnFirstPacket(1, 0));        // 已登记者照常返回
        }

        [Fact]
        public void 断线会话收到迟到首包_不在没有新连接事件时复活()
        {
            var table = new SessionManager(2);
            var disconnected = new Session(7, 0, 11) { Disconnected = true };
            Assert.True(table.TryAdd(disconnected));
            long lastSeenBefore = disconnected.LastSeenMs;

            Session late = table.GetOrAddOnFirstPacket(7, 1);

            Assert.Same(disconnected, late);
            Assert.True(late.Disconnected);
            Assert.Equal(lastSeenBefore, late.LastSeenMs);
            Assert.Equal(1, table.Count);

            // 真正的连接 Id 复用必须经过 OnConnected 对应的 TryAddNew，
            // 才会创建新代次并允许新包进入。
            Assert.True(table.TryAddNew(7, 2, out Session fresh));
            Assert.NotSame(disconnected, fresh);
            Assert.False(fresh.Disconnected);
            Assert.NotEqual(disconnected.Epoch, fresh.Epoch);
            Assert.Equal(1, fresh.Epoch);
        }

        [Fact]
        public void 周期清理_断线超窗与未进房静默被移除()
        {
            var table = new SessionManager(8);
            var seated = new Session(1, 0) { PlayerId = 0 };            // 已进房（活跃）
            var disconnected = new Session(2, 0) { PlayerId = 1 };
            disconnected.Disconnected = true;
            var unseatedIdle = new Session(3, 0);                       // 未进房且静默
            var unseatedFresh = new Session(4, 0) { LastSeenMs = 300_000 };   // 未进房但刚来过
            table.TryAdd(seated);
            table.TryAdd(disconnected);
            table.TryAdd(unseatedIdle);
            table.TryAdd(unseatedFresh);

            // 断线但仍在重连窗口内、未进房静默尚未到点：全部保留
            Assert.Equal(0, table.Cleanup(SessionManager.UnseatedIdleTtlMs));
            Assert.True(table.TryGet(2, out _));

            // 超出重连窗口：断线会话移除；未进房静默（idle > 60s）移除；未进房但刚来过保留
            long now = SessionManager.DisconnectedTtlMs + 1000;
            int cleaned = table.Cleanup(now);
            Assert.Equal(2, cleaned);
            Assert.True(table.TryGet(1, out _));
            Assert.False(table.TryGet(2, out _));
            Assert.False(table.TryGet(3, out _));
            Assert.True(table.TryGet(4, out _));                        // idle = 301s-300s = 1s < 60s
        }

        // ---- ReconnectService 时钟注入/容量 ----

        [Fact]
        public void 重连票据_过期由注入时钟判定()
        {
            var clock = new VirtualClock();
            var service = new ReconnectService(clock: clock);
            string ticket = service.Issue(playerId: 3, roomId: "R");

            Assert.True(service.TryConsume(ticket, out int playerId, out _));   // 时效内一次成功
            Assert.Equal(3, playerId);

            string neverConsumed = service.Issue(playerId: 4, roomId: "R");
            clock._ts += ReconnectService.TicketTtlMs + 1;              // 越过 TTL（虚拟时间——无真实等待）
            Assert.False(service.TryConsume(neverConsumed, out _, out _));   // 过期拒绝
            Assert.Equal(0, service.Count);                            // 一次性：消费尝试即作废（无论成败）

            string stale = service.Issue(playerId: 5, roomId: "R");
            clock._ts += ReconnectService.TicketTtlMs + 1;              // stale 也过期且从未被消费
            Assert.Equal(1, service.PurgeExpired());                   // 周期清理移除过期条目
            Assert.Equal(0, service.PurgeExpired());                   // 幂等：无事可清
            _ = stale;
        }

        [Fact]
        public void 重连票据_容量上限淘汰最老()
        {
            var clock = new VirtualClock();
            var service = new ReconnectService(capacity: 2, clock: clock);
            string first = service.Issue(1, "R");
            service.Issue(2, "R");
            service.Issue(3, "R");                                      // 超限 → 淘汰最老

            Assert.False(service.TryConsume(first, out _, out _));       // 最老条目已淘汰
            Assert.Equal(2, service.Count);
        }

        // ---- 票据熵源（§P0-6"CSPRNG 生成、至少 128 bit 熵"）----

        [Fact]
        public void 重连票据_CSPRNG_不可复现且熵宽达标()
        {
            // 可预测串（如 "rc{serial:x}-{playerId:x}"）会让两个新实例按同一顺序签发产出**同一串**。
            // CSPRNG 下签发序列不可复现——这条用例钉住"可预测串"不回归。
            string a = new ReconnectService().Issue(playerId: 7, roomId: "R");
            string b = new ReconnectService().Issue(playerId: 7, roomId: "R");
            Assert.NotEqual(a, b);

            string token = new ReconnectService().Issue(playerId: 7, roomId: "R");
            Assert.Equal(22, token.Length);                               // 16 B → base64url 去 padding
            Assert.Matches("^[A-Za-z0-9_-]{22}$", token);                 // base64url 字母表，无 '=' padding

            // 还原字节数 = 128 bit（熵宽是设计口径，不是"看起来随机"）
            string padded = token.Replace('-', '+').Replace('_', '/');
            switch (padded.Length % 4) { case 2: padded += "=="; break; case 3: padded += "="; break; }
            Assert.Equal(ReconnectService.TokenEntropyBytes, System.Convert.FromBase64String(padded).Length);
        }

        [Fact]
        public void 重连票据_批量签发无重复()
        {
            var service = new ReconnectService();
            var seen = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
            for (int i = 0; i < 64; i++)
            {
                string token = service.Issue(playerId: 3, roomId: "R");
                Assert.True(seen.Add(token), "票据重复：" + token);
            }
        }

        // ---- 重绑：新会话水位归零，旧 ACK 不复活 ----

        [Fact]
        public void 重绑后新会话_水位与ACK状态从零开始()
        {
            // 旧连接：有发送记账与已验证 ACK
            var old = new Session(1, 0);
            old.RecordSnapshotSend(10, 500);
            Assert.Equal(AckResult.Accepted, old.TryAcceptAck(10));
            Assert.Equal(500, old.AckedBytes);

            // 重绑（§9.2：旧 Connection 整体作废，新 Connection 从零记账）
            var fresh = new Session(2, 0);

            Assert.Equal(0, fresh.SendQueueBytes);
            Assert.Equal(0, fresh.AckedBytes);
            Assert.Equal(-1, fresh.LastAcceptedAckFrame);
            Assert.Equal(-1, fresh.LastSentSnapshotFrame);

            // 旧连接曾确认过的帧号，在新连接上不代表"已发送"——被拒绝（不复活旧 ACK 状态）
            Assert.Equal(AckResult.RejectedFuture, fresh.TryAcceptAck(10));
            Assert.Equal(0, fresh.AckedBytes);
        }
    }
}
