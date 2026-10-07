using System;
using LiteSim;
using Xunit;

namespace LiteSim.Tests
{
    /// <summary>输入历史机制用例（《M9 实施指导》§2.4：记录/覆盖语义/窗口判定/位级判等）。</summary>
    public class InputHistoryTests
    {
        private static SimInputFrame Input(long id, float mx, float mz, float aimX, uint buttons)
        {
            // AimPointZ 固定非零（AimPoint 单口径下点即瞄准表示；本用例只验证身份/差异语义）
            return new SimInputFrame { EntityId = id, MoveX = mx, MoveZ = mz, AimPointX = aimX, AimPointY = 1f, AimPointZ = 0.5f, Buttons = buttons };
        }

        [Fact]
        public void 记录与读取_往返一致()
        {
            var h = new InputHistory(32, 2);
            var inputs = new[] { Input(1, 0.5f, -0.25f, 1.0f, 0u), Input(2, -1f, 1f, 0f, SimInputFrame.ButtonFire) };
            h.Record(4, inputs, new[] { true, false });

            Assert.True(h.TryGet(4, out var got, out var pred));
            Assert.Equal(inputs[0], got[0]);
            Assert.Equal(inputs[1], got[1]);
            Assert.True(pred[0]);
            Assert.False(pred[1]);
            Assert.True(h.IsAnyPredicted(4));
        }

        [Fact]
        public void 环形覆写_最老帧被逐出()
        {
            var h = new InputHistory(32, 2);
            for (int f = 0; f < 33; f++)
                h.Record(f, new[] { Input(f, f, f, f, 0u), Input(f + 100, 0f, 0f, 0f, 0u) }, new[] { false, false });

            Assert.False(h.TryGet(0, out _, out _));
            Assert.True(h.TryGet(1, out _, out _));
            Assert.True(h.TryGet(32, out _, out _));
        }

        [Fact]
        public void Overwrite_覆盖真实值并清预测位()
        {
            var h = new InputHistory(32, 2);
            h.Record(6, new[] { Input(1, 0f, 0f, 0f, 0u), Input(2, 0f, 0f, 0f, 0u) }, new[] { true, true });

            var real = new[] { Input(1, 0.7f, 0.3f, 2.0f, SimInputFrame.ButtonFire), Input(2, -0.2f, 0.9f, 0.5f, 0u) };
            h.Overwrite(6, real);

            Assert.True(h.TryGet(6, out var got, out var pred));
            Assert.Equal(real[0], got[0]);
            Assert.Equal(real[1], got[1]);
            Assert.False(pred[0]);
            Assert.False(pred[1]);
            Assert.False(h.IsAnyPredicted(6));           // 预测位已清（回滚判定据此短路）
        }

        [Fact]
        public void Overwrite_早到真实输入_未记录帧亦可入史()
        {
            var h = new InputHistory(32, 2);
            var real = new[] { Input(9, 1f, 1f, 0f, 0u), Input(10, 0f, 0f, 0f, 0u) };
            h.Overwrite(50, real);                      // 帧尚未模拟——真实输入先行入史

            Assert.True(h.TryGet(50, out var got, out var pred));
            Assert.Equal(real[0], got[0]);
            Assert.False(pred[0]);
            Assert.False(pred[1]);
            Assert.False(h.IsAnyPredicted(50));
        }

        [Fact]
        public void 越界与未记录帧_TryGet返回false()
        {
            var h = new InputHistory(32, 2);
            h.Record(3, new[] { Input(1, 0f, 0f, 0f, 0u), Input(2, 0f, 0f, 0f, 0u) }, new[] { false, false });

            Assert.False(h.TryGet(2, out _, out _));      // 未记录
            Assert.False(h.TryGet(35, out _, out _));    // 越界（容量 32：槽 35%32=3 持帧 3 ≠ 35）
            Assert.False(h.TryGet(-1, out _, out _));
        }

        [Fact]
        public void Differs_逐位判等()
        {
            var h = new InputHistory(32, 2);
            var stored = new[] { Input(1, 0.5f, 0f, 1f, 0u), Input(2, 0f, 0f, 0f, 0u) };
            h.Record(2, stored, new[] { true, false });

            var same = new[] { Input(1, 0.5f, 0f, 1f, 0u), Input(2, 0f, 0f, 0f, 0u) };
            Assert.False(h.Differs(2, same));            // 逐位相同 → 无需回滚

            var fireDiff = new[] { Input(1, 0.5f, 0f, 1f, SimInputFrame.ButtonFire), Input(2, 0f, 0f, 0f, 0u) };
            Assert.True(h.Differs(2, fireDiff));         // 开火位不同 → 回滚

            // 1 ULP 级浮点差异也判不同（位级判等，非近似比较）
            float oneUlp = BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(0.5f) + 1);
            var ulpDiff = new[] { Input(1, oneUlp, 0f, 1f, 0u), Input(2, 0f, 0f, 0f, 0u) };
            Assert.True(h.Differs(2, ulpDiff));

            var yawDiff = new[] { Input(1, 0.5f, 0f, 2f, 0u), Input(2, 0f, 0f, 0f, 0u) };
            Assert.True(h.Differs(2, yawDiff));
        }

        [Fact]
        public void IsAnyPredicted_未记录帧返回false()
        {
            var h = new InputHistory(32, 2);
            Assert.False(h.IsAnyPredicted(7));           // 未记录 → false（不触发回滚）
        }
    }
}
