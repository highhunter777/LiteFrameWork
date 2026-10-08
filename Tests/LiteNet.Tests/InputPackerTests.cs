using System;
using LiteNet.Protocol;
using LiteSim;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>输入冗余打包用例（《M10 实施指导》§3：未确认段冗余窗口（容量 16） / 位级精确往返）。</summary>
    public class InputPackerTests
    {
        private static SimInputFrame Make(long id, float x)
        {
            return new SimInputFrame { EntityId = id, MoveX = x, MoveZ = -x, AimPointX = x, AimPointY = 1f, AimPointZ = x * 3f, Buttons = 1u };
        }

        [Fact]
        public void 冗余_容量封顶且首帧为最新()
        {
            var recent = new SimInputFrame[20]; // 20 帧历史 → 只打包容量上限 16 帧
            for (int i = 0; i < recent.Length; i++) recent[i] = Make(100 + i, i);

            var msg = InputPacker.Pack(frame: 60, recent: recent, ackSnapshot: 55, viewFrame: 58);

            Assert.Equal(60, msg.Frame);
            Assert.Equal(55, msg.AckSnapshot);
            Assert.Equal(58, msg.ViewFrame);
            Assert.Equal(InputPacker.MaxRedundancy, msg.Frames.Count);
            Assert.Equal(100, msg.Frames[0].EntityId);                              // recent[0] = 最新帧
            Assert.Equal(115, msg.Frames[InputPacker.MaxRedundancy - 1].EntityId);  // 滑出窗口的更早帧被裁
        }

        [Fact]
        public void 冗余_按帧号取帧_窗口内外()
        {
            var recent = new[] { Make(1, 0.1f), Make(1, 0.2f), Make(1, 0.3f), Make(1, 0.4f) };
            var msg = InputPacker.Pack(100, recent, 90, 0);

            for (int i = 0; i < recent.Length; i++)
            {
                Assert.True(InputPacker.TryGetFrame(msg, 100 - i, out var input));
                Assert.Equal(0.1f * (i + 1), input.MoveX); // recent[i] 对应帧 100-i
            }

            Assert.False(InputPacker.TryGetFrame(msg, 101, out _)); // 未来帧
            Assert.False(InputPacker.TryGetFrame(msg, 96, out _));  // 窗口外（丢包超冗余深度）
        }

        [Fact]
        public void 映射_字段位级精确往返()
        {
            // 含负值/极值——float 经 proto（IEEE 32 位）必须逐位往返
            var src = new SimInputFrame
            {
                EntityId = 0x0001_0002_0003_0004L,
                MoveX = -3.1415927f,
                MoveZ = 1e-30f,
                // **AimPoint 三分量必须填非默认**——否则"零到零"往返会假绿
                // （AimPoint 单口径：《固定斜视角射击方案专项设计》§4；协议字段号 11/12/13，
                // 漏映射则瞄准点静默丢失、命中/爆头永不发生）
                AimPointX = 6.2831853f,
                AimPointY = 2.7182818f,
                AimPointZ = -6.2831853f,
                Buttons = 0xDEADBEEFu,
            };

            Proto.InputFrame wire = InputPacker.ToProto(src);
            SimInputFrame back = InputPacker.FromProto(wire);

            Assert.Equal(src.EntityId, back.EntityId);
            Assert.Equal(BitConverter.SingleToInt32Bits(src.MoveX), BitConverter.SingleToInt32Bits(back.MoveX));
            Assert.Equal(BitConverter.SingleToInt32Bits(src.MoveZ), BitConverter.SingleToInt32Bits(back.MoveZ));
            Assert.Equal(BitConverter.SingleToInt32Bits(src.AimPointX), BitConverter.SingleToInt32Bits(back.AimPointX));
            Assert.Equal(BitConverter.SingleToInt32Bits(src.AimPointY), BitConverter.SingleToInt32Bits(back.AimPointY));
            Assert.Equal(BitConverter.SingleToInt32Bits(src.AimPointZ), BitConverter.SingleToInt32Bits(back.AimPointZ));
            Assert.Equal(src.Buttons, back.Buttons);
        }
    }
}
