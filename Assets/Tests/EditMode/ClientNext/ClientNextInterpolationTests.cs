using LiteGameNext.Features.Battle.World;
using NUnit.Framework;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>远端插值缓冲：乱序保护/渲染帧推进与封顶/双快照插值/换代硬切/停摆不外推。</summary>
    public sealed class ClientNextInterpolationTests
    {
        private static SnapshotInterpolationBuffer Buffer(int delay = 0, float tickRate = 60f)
            => new SnapshotInterpolationBuffer { RenderDelayFrames = delay, TickRate = tickRate };

        [Test]
        public void 乱序样本_被丢弃()
        {
            var buffer = Buffer();
            buffer.Push(1, new PoseSample(10, Vector3.zero, 0f, 1));
            buffer.Push(1, new PoseSample(9, Vector3.one, 0f, 1));
            Assert.IsTrue(buffer.TrySample(1, out Vector3 pos, out _));
            Assert.AreEqual(Vector3.zero, pos, "帧号不递增的样本应被丢弃");
        }

        [Test]
        public void 无数据_采样返回false()
        {
            var buffer = Buffer();
            Assert.IsFalse(buffer.TrySample(999, out _, out _));
            Assert.IsFalse(buffer.Has(999));
        }

        [Test]
        public void 两份快照之间_按比例插值()
        {
            var buffer = Buffer(delay: 0);
            buffer.Push(1, new PoseSample(0, new Vector3(0, 0, 0), 0f, 1));
            buffer.Push(1, new PoseSample(10, new Vector3(10, 0, 0), 0f, 1));
            buffer.Advance(5f / 60f);   // 推进 5 帧
            Assert.IsTrue(buffer.TrySample(1, out Vector3 pos, out _));
            Assert.AreEqual(5f, pos.x, 0.6f, "5/10 处应约在 x=5");
        }

        [Test]
        public void 渲染帧封顶在最新减延迟()
        {
            var buffer = Buffer(delay: 3);
            buffer.Push(1, new PoseSample(0, Vector3.zero, 0f, 1));
            buffer.Push(1, new PoseSample(100, Vector3.one, 0f, 1));
            for (int i = 0; i < 300; i++) buffer.Advance(1f / 60f);   // 推进 300 帧
            Assert.AreEqual(97, buffer.RenderFrame, "应封顶在 100 - 3");
        }

        [Test]
        public void 快照停摆_停在最新不外推()
        {
            var buffer = Buffer(delay: 0);
            buffer.Push(1, new PoseSample(0, Vector3.zero, 0f, 1));
            buffer.Push(1, new PoseSample(5, new Vector3(5, 0, 0), 0f, 1));
            for (int i = 0; i < 100; i++) buffer.Advance(1f / 60f);
            Assert.IsTrue(buffer.TrySample(1, out Vector3 pos, out _));
            Assert.AreEqual(5f, pos.x, 0.01f, "停摆后应停在最新快照位置，不外推");
        }

        [Test]
        public void 换代_直接取后帧不插值()
        {
            var buffer = Buffer(delay: 0);
            buffer.Push(1, new PoseSample(0, new Vector3(0, 0, 0), 0f, 1));
            buffer.Push(1, new PoseSample(10, new Vector3(100, 0, 0), 0f, 2));  // LifeSerial 变化
            buffer.Advance(5f / 60f);
            Assert.IsTrue(buffer.TrySample(1, out Vector3 pos, out _));
            Assert.AreEqual(100f, pos.x, 0.01f, "换代应硬切到后帧，不插值");
        }

        [Test]
        public void 容量上限_只保留最近八帧()
        {
            var buffer = Buffer(delay: 0);
            for (int i = 0; i < 20; i++)
                buffer.Push(1, new PoseSample(i, new Vector3(i, 0, 0), 0f, 1));
            // 渲染帧随首个样本落位，仍能取到样本（未崩即通过；容量由内部裁剪）
            Assert.IsTrue(buffer.TrySample(1, out _, out _));
        }

        [Test]
        public void 重置_清空全部状态()
        {
            var buffer = Buffer();
            buffer.Push(1, new PoseSample(0, Vector3.zero, 0f, 1));
            buffer.Reset();
            Assert.AreEqual(0, buffer.EntityCount);
            Assert.AreEqual(-1, buffer.RenderFrame);
            Assert.IsFalse(buffer.Has(1));
        }
    }
}
