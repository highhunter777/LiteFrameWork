using LiteSim;
using RoomServer;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// 可信 ACK 用例（《商业级通用服务端框架总设计》§5 P0-4）：
    /// ACK 只能确认"真实发送过、单调前进、仍在 ledger 窗内"的快照帧；
    /// 释放 = 该帧发送后的累计字节（水位永不因伪造/回退/超前 ack 倒退或清空）。
    /// </summary>
    public sealed class SnapshotAckTests
    {
        private const int SeedBytes = 1024;

        private static (Room room, Session s) BuildRoom()
        {
            var room = new Room(new RoomConfig { RoomId = "AckTest", ExpectedPlayers = 1 });
            room.SendTo = (session, type, msg, reliable) => { };
            room.Broadcaster.SendTo = room.SendTo;
            var s = new Session(1, 0);
            room.AssignPlayerId(s);
            room.Start(12345L);
            return (room, s);
        }

        [Fact]
        public void 合法ACK_释放到该帧的精确累计_水位单调不减()
        {
            var (room, s) = BuildRoom();
            s.RecordSnapshotSend(2, 100);
            s.RecordSnapshotSend(4, 150);
            s.RecordSnapshotSend(6, 120);              // 累计 370

            room.OnClientAck(s, 4);                    // 确认到帧 4 → 释放到累计 250
            Assert.Equal(250, s.AckedBytes);
            Assert.Equal(4, s.LastAcceptedAckFrame);
            Assert.Equal(4, s.LastAckSnapshot);        // 验证过才更新（NeedsFull 判据吃可信值）
            Assert.Equal(370 - 250, s.SendQueueBytes - s.AckedBytes);   // 水位 = 帧 4 之后发出的量

            room.OnClientAck(s, 6);                    // 前进到帧 6 → 释放到 370
            Assert.Equal(370, s.AckedBytes);
            Assert.Equal(0, s.SendQueueBytes - s.AckedBytes);
            Assert.Equal(0, room.AckRejected);
        }

        [Fact]
        public void 伪造超前ACK_从未发送过的帧_被忽略且不清水位()
        {
            var (room, s) = BuildRoom();
            s.RecordSnapshotSend(2, 100);

            room.OnClientAck(s, 99);                   // 从未发送过帧 99（伪造/时钟错乱）
            Assert.Equal(1, room.AckRejectedFuture);
            Assert.Equal(0, s.AckedBytes);            // 水位分文未动
            Assert.Equal(-1, s.LastAcceptedAckFrame);
            Assert.Equal(-1, s.LastAckSnapshot);       // 未验证的 ack 不污染判据

            room.OnClientAck(s, -1);                   // 负值同样拒绝
            Assert.Equal(1, room.AckRejectedStale);
            Assert.Equal(0, s.AckedBytes);
        }

        [Fact]
        public void 回退与重复ACK_被忽略()
        {
            var (room, s) = BuildRoom();
            s.RecordSnapshotSend(2, 100);              // 累计 100
            s.RecordSnapshotSend(4, 150);              // 累计 250

            room.OnClientAck(s, 4);
            Assert.Equal(250, s.AckedBytes);

            room.OnClientAck(s, 4);                    // 重复确认同一帧
            room.OnClientAck(s, 2);                    // 回退确认旧帧
            Assert.Equal(2, room.AckRejectedStale);
            Assert.Equal(250, s.AckedBytes);           // 水位不倒退
            Assert.Equal(4, s.LastAcceptedAckFrame);
        }

        [Fact]
        public void 伪造中间帧_只能释放到该帧_不能清空全部()
        {
            var (room, s) = BuildRoom();
            s.RecordSnapshotSend(2, 100);
            s.RecordSnapshotSend(4, 150);
            s.RecordSnapshotSend(6, 120);              // 累计 370

            // 攻击面：客户端只收到帧 2，却谎报帧 6 想"清空水位"——
            // 但帧 6 **确实发送过**（ledger 在），所以释放合法且精确：只释放到帧 6 的累计。
            // 真正的防线在"超前帧拒绝"（上一用例）与累计口径（谎报旧帧只能少释放，不能多释放）。
            room.OnClientAck(s, 6);
            Assert.Equal(370, s.AckedBytes);

            // 反向：谎报**更旧**的帧只会被回退拒绝——水位不倒退
            room.OnClientAck(s, 2);
            Assert.Equal(1, room.AckRejectedStale);
            Assert.Equal(370, s.AckedBytes);
        }

        [Fact]
        public void 超窗陈旧ACK_ledger条目被覆盖后忽略_水位保持保守()
        {
            var (room, s) = BuildRoom();

            // 灌满并覆盖 ledger（容量 128）：帧 1..130 → 帧 1/2 被覆盖
            for (int f = 1; f <= SnapshotLedger.Capacity + 2; f++)
                s.RecordSnapshotSend(f, 10);

            room.OnClientAck(s, 1);                    // 帧号范围合法（≤ LastSent）但条目已覆盖
            Assert.Equal(1, room.AckRejectedEvicted);
            Assert.Equal(0, s.AckedBytes);             // 水位保持保守（该客户端早已被 NeedsFull/背压照顾）

            // 窗内最新帧照常释放
            int latest = SnapshotLedger.Capacity + 2;
            room.OnClientAck(s, latest);
            Assert.Equal((SnapshotLedger.Capacity + 2) * 10L, s.AckedBytes);
            Assert.Equal(1, room.AckRejectedEvicted);  // 不再增长
        }

        [Fact]
        public void 广播记账_经RecordSnapshotSend入账_帧号与累计可追溯()
        {
            var (room, s) = BuildRoom();
            room.StepFrame();
            room.StepFrame();                          // 第 2 帧触发 30Hz 抽帧（stride=2）→ 已发出一份快照

            Assert.True(s.LastSentSnapshotFrame > 0, "广播后 LastSentSnapshotFrame 必须前移");
            Assert.True(s.SendQueueBytes > 0);
            long cumulative = s.SendQueueBytes;

            // 客户端 ack 刚收到的快照帧 → 全额释放（水位归零）
            room.OnClientAck(s, s.LastSentSnapshotFrame);
            Assert.Equal(cumulative, s.AckedBytes);
            Assert.Equal(0, s.SendQueueBytes - s.AckedBytes);
        }
    }
}
