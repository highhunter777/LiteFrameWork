using System.Collections.Generic;
using LiteSim;
using RoomServer;
using RoomServer.Application;
using RoomServer.Runtime;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// 房间输入消费用例（《M10实施指导》附「服务端审查」）：
    /// 关注"输入进到房间之后"的语义——多人同 tick 开火是否都记回溯、超前 ack 是否仍推进。
    ///
    /// 改由 <see cref="RoomRuntime"/> 命令面驱动（Join/Tick/ClientInput），不起网络。
    /// </summary>
    public sealed class RoomInputTests
    {
        private const long SharedSeed = 20260919L;

        [Fact]
        public void 同tick两名玩家开火_两人都记回溯判定()
        {
            (RoomRuntime room, Session s1, Session s2) = BuildStartedRoom();
            int frame = room.AuthSim.Frame + 1;                   // inputDelay=1：客户端发"下一帧"

            Input(room, s1, FirePacket(frame, ackSnapshot: room.AuthSim.Frame));
            Input(room, s2, FirePacket(frame, ackSnapshot: room.AuthSim.Frame));

            Step(room);

            // 修正前：pending 是单槽 → 只有后到的那名玩家被记回溯（先到的静默丢失）
            Assert.Equal(2, room.FireInputsProcessed);
        }

        [Fact]
        public void 超前ack的输入_仍被接受并推进权威()
        {
            (RoomRuntime room, Session s1, _) = BuildStartedRoom();
            int frame = room.AuthSim.Frame + 1;
            EntitySlot before = SlotOf(room, 0);

            // ack 超前（服务器自身曾下发"下一待处理帧"作为 ack，客户端原样回传）
            Input(room, s1, MovePacket(frame, moveX: 1f, ackSnapshot: room.AuthSim.Frame + 50));

            Assert.Equal(1, room.Gate.AcceptedCount);             // 输入没被 ack 连坐丢弃
            Assert.True(room.Gate.DroppedAckSnapshot >= 1);       // 但违规有记账

            Step(room);
            EntitySlot after = SlotOf(room, 0);
            Assert.NotEqual(before.Pos.X, after.Pos.X);           // 真的推进了（不是"空输入沿用"）
        }

        [Fact]
        public void 同一玩家同tick多次开火_只记一次回溯()
        {
            (RoomRuntime room, Session s1, _) = BuildStartedRoom();
            int frame = room.AuthSim.Frame + 1;

            Input(room, s1, FirePacket(frame, room.AuthSim.Frame));
            Input(room, s1, FirePacket(frame, room.AuthSim.Frame));   // 冗余重发（同帧）

            Step(room);
            Assert.Equal(1, room.FireInputsProcessed);            // 同帧去重 + 每玩家一次
        }

        // ---- 命令面装配（App 层映射的测试等价物）----

        private static (RoomRuntime room, Session s1, Session s2) BuildStartedRoom()
        {
            var room = new RoomRuntime(new RoomConfig { RoomId = "InputTest", Seed = SharedSeed });
            var seats = new Session[room.ExpectedPlayers];
            var pipeline = new SnapshotPipeline(seats);   // 空 SendTo：只驱动记账，不关心下行

            var s1 = new Session(1, 0);
            var s2 = new Session(2, 0);
            Join(room, s1, seats);
            Join(room, s2, seats);
            Assert.True(room.Started);
            _ = pipeline;
            return (room, s1, s2);
        }

        private static void Join(RoomRuntime room, Session session, Session[] seats)
        {
            var outputs = new List<RoomOutput>();
            room.Execute(RoomCommand.Join(session.ConnectionId), outputs);
            foreach (RoomOutput o in outputs)
            {
                if (o is SignalOutput { Signal: PlayerAdmitted pa })
                {
                    session.PlayerId = pa.PlayerId;
                    seats[pa.PlayerId] = session;
                }
            }
        }

        private static void Input(RoomRuntime room, Session session, in ClientInputBatch batch)
        {
            var outputs = new List<RoomOutput>();
            room.Execute(RoomCommand.ClientInput(session.PlayerId, batch), outputs);
        }

        private static void Step(RoomRuntime room)
        {
            var outputs = new List<RoomOutput>();
            room.Execute(RoomCommand.Tick(0), outputs);
        }

        private static ClientInputBatch FirePacket(int frame, int ackSnapshot)
        {
            var frames = new SimInputFrame[ClientInputBatch.MaxFrames];
            frames[0] = new SimInputFrame { MoveX = 0f, MoveZ = 0f, AimPointX = 10f, AimPointY = 1f, AimPointZ = 0f, Buttons = SimInputFrame.ButtonFire };
            return new ClientInputBatch { Frame = frame, AckSnapshot = ackSnapshot, ViewFrame = 30, Count = 1, Frames = frames };
        }

        private static ClientInputBatch MovePacket(int frame, float moveX, int ackSnapshot)
        {
            var frames = new SimInputFrame[ClientInputBatch.MaxFrames];
            frames[0] = new SimInputFrame { MoveX = moveX, MoveZ = 0f };
            return new ClientInputBatch { Frame = frame, AckSnapshot = ackSnapshot, ViewFrame = 0, Count = 1, Frames = frames };
        }

        private static EntitySlot SlotOf(RoomRuntime room, int playerId)
        {
            long id = room.EntityIdOf(playerId);
            Assert.True(room.AuthSim.TryResolve(id, out int slot));
            return room.AuthSim.Entities[slot];
        }
    }
}
