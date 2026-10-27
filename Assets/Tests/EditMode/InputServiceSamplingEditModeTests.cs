using LiteClient;
using LiteTesting;
using LiteSim;
using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 输入采样帧边界（A6）：同一渲染帧内重复调用 <see cref="InputService.SampleOnRenderFrame"/>
    /// 是追帧/多 procedure 步进的正常形态，**不得吞掉本帧已产生、尚未上行的结论**；
    /// 清标记的唯一时机是 <see cref="InputService.TryTakeForSend"/>（上行消费）。
    /// </summary>
    [Category(TestCategory.Contract)]
    public sealed class InputServiceSamplingEditModeTests
    {
        private sealed class FixedSource : IIntentSource
        {
            public SimInputFrame Next = new SimInputFrame { MoveX = 1f };
            public bool Handled = true;

            public string Name => "fixed";
            public IntentSample Sample(in SimVector3 localPos)
                => Handled ? new IntentSample(Next) : IntentSample.None;
        }

        [Test]
        public void 同渲染帧重复采样_不吞本帧上行结论()
        {
            var input = new InputService();
            input.SetSource(new FixedSource());

            input.SampleOnRenderFrame(default);
            input.SampleOnRenderFrame(default);             // 同帧重入（追帧步进）

            Assert.IsTrue(input.TryTakeForSend(out SimInputFrame sent), "同帧重采样后仍须可上行（不得吞本帧）");
            Assert.AreEqual(1f, sent.MoveX, 1e-6f);
            Assert.IsFalse(input.TryTakeForSend(out _), "同一渲染帧只上行一次");
        }

        [Test]
        public void 下一渲染帧_重新开闸_且只上行一次()
        {
            var input = new InputService();
            input.SetSource(new FixedSource());

            input.SampleOnRenderFrame(default);
            Assert.IsTrue(input.TryTakeForSend(out _));
            Assert.IsFalse(input.TryTakeForSend(out _));

            input.SampleOnRenderFrame(default);             // 上行已清标记 = 下一渲染帧
            Assert.IsTrue(input.TryTakeForSend(out _), "新渲染帧应重新开闸");
            Assert.IsFalse(input.TryTakeForSend(out _));
        }

        [Test]
        public void 未采样帧不上行_被拦帧照常上行零意图()
        {
            var input = new InputService();
            input.SetSource(new FixedSource());

            Assert.IsFalse(input.TryTakeForSend(out _), "从未采样：不上行");

            input.RegisterBlocker(new IntentGate.BlockerKey("test", "被拦"), () => true);
            input.SampleOnRenderFrame(default);
            Assert.IsTrue(input.TryTakeForSend(out SimInputFrame blocked), "被拦帧也要照常上行零意图");
            Assert.AreEqual(0f, blocked.MoveX, 1e-6f, "拦截 = 空意图");
        }
    }
}
