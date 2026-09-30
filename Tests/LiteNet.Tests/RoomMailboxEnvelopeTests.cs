using System;
using LiteSim;
using RoomServer.Application;
using RoomServer.Runtime;
using Xunit;

namespace LiteNet.Tests
{
    public sealed class RoomMailboxEnvelopeTests
    {
        [Fact]
        public void InputEnvelope_复制输入数组并保留原始Count()
        {
            var frames = new SimInputFrame[ClientInputBatch.MaxFrames];
            frames[0] = new SimInputFrame { MoveX = 0.25f, Buttons = SimInputFrame.ButtonFire };
            var batch = new ClientInputBatch
            {
                Frame = 17,
                AckSnapshot = 9,
                ViewFrame = 12,
                Count = ClientInputBatch.MaxFrames + 1,
                Frames = frames,
            };
            var command = RoomCommand.ClientInput(3, batch);
            var envelope = RoomInputEnvelope.FromCommand("r", 11, 3, 5, 7, command);

            frames[0] = default;

            Assert.Equal(3, envelope.PlayerId);
            Assert.Equal(11, envelope.ConnectionId);
            Assert.Equal(5, envelope.SessionEpoch);
            Assert.Equal(ClientInputBatch.MaxFrames + 1, envelope.Command.Input.Count);
            Assert.Equal(0.25f, envelope.Command.Input.Frames[0].MoveX);
            Assert.Equal(SimInputFrame.ButtonFire, envelope.Command.Input.Frames[0].Buttons);
        }

        [Fact]
        public void Envelope_元数据与命令不一致时拒绝()
        {
            Assert.Throws<ArgumentException>(() => RoomControlEnvelope.FromCommand(
                "r", 99, -1, 1, 0, RoomCommand.Join(7)));
            Assert.Throws<ArgumentException>(() => RoomControlEnvelope.FromCommand(
                "r", -1, 8, 1, 0, RoomCommand.RestoreAck(7)));
            Assert.Throws<ArgumentException>(() => RoomInputEnvelope.FromCommand(
                "r", 1, 8, 1, 0, RoomCommand.ClientInput(7, default)));
        }

        [Fact]
        public void Envelope_错误lane命令拒绝()
        {
            Assert.Throws<ArgumentException>(() => RoomControlEnvelope.FromCommand(
                "r", -1, -1, 0, 0, RoomCommand.ClientInput(1, default)));
            Assert.Throws<ArgumentException>(() => RoomInputEnvelope.FromCommand(
                "r", 1, 1, 0, 0, RoomCommand.Join(1)));
        }
    }
}
