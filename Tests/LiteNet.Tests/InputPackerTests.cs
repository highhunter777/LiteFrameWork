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
        public void 映射_全字段面逐项钉住_十字段无一漏()
        {
            // P0-3：真服 ServerHost 与本地服 LocalServerTransport 曾各手抄一份逐字段拷贝
            // （AimPoint 六字段真实漂移过一次：真服断点而本地服照常——本地绿掩盖真服红）。
            // 字段面已归 InputPacker 单源；本用例把**全部 10 个字段**填成可区分值逐项断言，
            // 任何一处漏映射即红（原用例只钉了 6 个面，SelectedWeaponSlot/TargetEntityId/ActionSeq 无护栏）。
            var src = new SimInputFrame
            {
                EntityId = 0x0001_0002_0003_0004L,
                MoveX = -3.1415927f,
                MoveZ = 1e-30f,
                AimPointX = 6.2831853f,
                AimPointY = 2.7182818f,
                AimPointZ = -6.2831853f,
                Buttons = 0xDEADBEEFu,
                SelectedWeaponSlot = 7,
                TargetEntityId = 0x0000_0009_0000_000AL,
                ActionSeq = 123456u,
            };

            SimInputFrame back = InputPacker.FromProto(InputPacker.ToProto(src));

            Assert.Equal(src.EntityId, back.EntityId);
            Assert.Equal(BitConverter.SingleToInt32Bits(src.MoveX), BitConverter.SingleToInt32Bits(back.MoveX));
            Assert.Equal(BitConverter.SingleToInt32Bits(src.MoveZ), BitConverter.SingleToInt32Bits(back.MoveZ));
            Assert.Equal(BitConverter.SingleToInt32Bits(src.AimPointX), BitConverter.SingleToInt32Bits(back.AimPointX));
            Assert.Equal(BitConverter.SingleToInt32Bits(src.AimPointY), BitConverter.SingleToInt32Bits(back.AimPointY));
            Assert.Equal(BitConverter.SingleToInt32Bits(src.AimPointZ), BitConverter.SingleToInt32Bits(back.AimPointZ));
            Assert.Equal(src.Buttons, back.Buttons);
            Assert.Equal(src.SelectedWeaponSlot, back.SelectedWeaponSlot);
            Assert.Equal(src.TargetEntityId, back.TargetEntityId);
            Assert.Equal(src.ActionSeq, back.ActionSeq);
        }

        [Fact]
        public void 解包整批_与单帧映射同源_超窗不越界写()
        {
            // P0-3：两个宿主的解包路径必须与打包侧单帧映射**同源**（各写一份就是把漂移面请回来）。
            var recent = new SimInputFrame[20];
            for (int i = 0; i < recent.Length; i++) recent[i] = Make(200 + i, 0.5f + i);
            Proto.InputMessage msg = InputPacker.Pack(1000, recent, 990, 995);

            var into = new SimInputFrame[InputPacker.MaxRedundancy];
            int raw = InputPacker.UnpackFrames(msg, into);

            Assert.Equal(InputPacker.MaxRedundancy, raw);              // 包内条数（打包侧已封顶）
            for (int i = 0; i < InputPacker.MaxRedundancy; i++)
                Assert.Equal(recent[i].EntityId, into[i].EntityId);    // 逐条与打包源一致（含 AimPoint 面）

            // 防御面：包内条数被人为撑过冗余窗时，只拷窗口内条数（不越界写），原始条数如实返回
            Proto.InputMessage oversized = InputPacker.Pack(1000, new SimInputFrame[InputPacker.MaxRedundancy], 990, 995);
            oversized.Frames.Add(new Proto.InputFrame { EntityId = 999 });
            var small = new SimInputFrame[InputPacker.MaxRedundancy + 1];
            int rawOversized = InputPacker.UnpackFrames(oversized, small);

            Assert.Equal(InputPacker.MaxRedundancy + 1, rawOversized);
            Assert.Equal(0L, small[InputPacker.MaxRedundancy].EntityId);   // 该槽未被写（保持默认）
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
