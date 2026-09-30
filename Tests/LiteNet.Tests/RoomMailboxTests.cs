using RoomServer.Application;
using Xunit;

namespace LiteNet.Tests
{
    public sealed class RoomMailboxTests
    {
        [Fact]
        public void 三条队列各自有界_满时显式RejectedFull并保留已有消息()
        {
            var mailbox = new RoomMailbox<int, string, string>(inputCapacity: 2, controlCapacity: 1, outboundCapacity: 1);

            Assert.Equal(RoomMailboxEnqueueResult.Accepted, mailbox.EnqueueInput(10));
            Assert.Equal(RoomMailboxEnqueueResult.Accepted, mailbox.EnqueueInput(11));
            Assert.Equal(RoomMailboxEnqueueResult.RejectedFull, mailbox.EnqueueInput(12));
            Assert.Equal(RoomMailboxEnqueueResult.Accepted, mailbox.EnqueueControl("stop"));
            Assert.Equal(RoomMailboxEnqueueResult.RejectedFull, mailbox.EnqueueControl("again"));
            Assert.Equal(RoomMailboxEnqueueResult.Accepted, mailbox.EnqueueOutbound("snapshot"));

            Assert.Equal(2, mailbox.InputCount);
            Assert.Equal(1, mailbox.ControlCount);
            Assert.Equal(1, mailbox.OutboundCount);
            Assert.Equal(4, mailbox.Count);
            Assert.Equal(2, mailbox.RejectedFullCount);
            Assert.Equal(4, mailbox.AcceptedCount);
        }

        [Fact]
        public void 出队严格控制优先_同一lane保持FIFO()
        {
            var mailbox = new RoomMailbox<int, string, string>(8, 8, 8);
            mailbox.EnqueueInput(1);
            mailbox.EnqueueInput(2);
            mailbox.EnqueueOutbound("out-1");
            mailbox.EnqueueControl("ctrl-1");
            mailbox.EnqueueControl("ctrl-2");

            RoomMailboxMessage<int, string, string> item;
            Assert.True(mailbox.TryDequeue(out item));
            Assert.Equal(RoomMailboxLane.Control, item.Lane);
            Assert.Equal("ctrl-1", item.Control);
            Assert.True(mailbox.TryDequeue(out item));
            Assert.Equal(RoomMailboxLane.Control, item.Lane);
            Assert.Equal("ctrl-2", item.Control);
            Assert.True(mailbox.TryDequeue(out item));
            Assert.Equal(RoomMailboxLane.Input, item.Lane);
            Assert.Equal(1, item.Input);
            Assert.True(mailbox.TryDequeue(out item));
            Assert.Equal(RoomMailboxLane.Input, item.Lane);
            Assert.Equal(2, item.Input);
            Assert.True(mailbox.TryDequeue(out item));
            Assert.Equal(RoomMailboxLane.Outbound, item.Lane);
            Assert.Equal("out-1", item.Outbound);
            Assert.False(mailbox.TryDequeue(out item));
            Assert.Equal(5, mailbox.DequeuedCount);
            Assert.Equal(0, mailbox.Count);
        }

        [Fact]
        public void 完成后拒绝新入队_但允许排空已有消息()
        {
            var mailbox = new RoomMailbox<int, string, string>(2, 2, 2);
            mailbox.EnqueueInput(42);
            mailbox.EnqueueControl("shutdown");

            mailbox.Complete();

            Assert.True(mailbox.IsClosed);
            Assert.False(mailbox.IsDrained);
            Assert.Equal(RoomMailboxEnqueueResult.Closed, mailbox.EnqueueInput(43));
            Assert.Equal(RoomMailboxEnqueueResult.Closed, mailbox.EnqueueControl("late"));
            Assert.Equal(RoomMailboxEnqueueResult.Closed, mailbox.EnqueueOutbound("late"));

            RoomMailboxMessage<int, string, string> item;
            Assert.True(mailbox.TryDequeue(out item));
            Assert.Equal(RoomMailboxLane.Control, item.Lane);
            Assert.True(mailbox.TryDequeue(out item));
            Assert.Equal(RoomMailboxLane.Input, item.Lane);
            Assert.False(mailbox.TryDequeue(out item));
            Assert.True(mailbox.IsDrained);
        }

        [Fact]
        public void lane专用出队只消费指定队列并维护总计数()
        {
            var mailbox = new RoomMailbox<int, string, string>(2, 2, 2);
            mailbox.EnqueueInput(7);
            mailbox.EnqueueControl("c");
            mailbox.EnqueueOutbound("o");

            int input;
            Assert.True(mailbox.TryDequeueInput(out input));
            Assert.Equal(7, input);
            Assert.Equal(2, mailbox.Count);

            string control;
            Assert.True(mailbox.TryDequeueControl(out control));
            Assert.Equal("c", control);
            Assert.Equal(1, mailbox.Count);

            string outbound;
            Assert.True(mailbox.TryDequeueOutbound(out outbound));
            Assert.Equal("o", outbound);
            Assert.False(mailbox.TryDequeueOutbound(out outbound));
            Assert.Equal(3, mailbox.DequeuedCount);
            Assert.Equal(0, mailbox.Counts.Total);
        }

        [Fact]
        public void 构造容量必须为正()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new RoomMailbox<int, int, int>(0, 1, 1));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new RoomMailbox<int, int, int>(1, 0, 1));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new RoomMailbox<int, int, int>(1, 1, 0));
        }
    }
}
