using LiteGameNext.Features.Battle.Hud;
using LiteSim;
using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>伤害数字：文本简写/聚合合并窗口/归属判定/超窗淘汰。</summary>
    public sealed class ClientNextDamageNumberTests
    {
        private const long Local = 10L;
        private const long Remote = 20L;

        [Test]
        public void 文本_万以下原样()
        {
            Assert.AreEqual("25", DamageNumberText.Format(25));
            Assert.AreEqual("9999", DamageNumberText.Format(9999));
        }

        [Test]
        public void 文本_万以上简写K()
        {
            Assert.AreEqual("10K", DamageNumberText.Format(10000));
            Assert.AreEqual("12.3K", DamageNumberText.Format(12300));
            Assert.AreEqual("20K", DamageNumberText.Format(20000));
        }

        [Test]
        public void 归属_打到他人记Dealt_被打记Taken()
        {
            var agg = new DamageNumberAggregator();
            DamageNumber dealt = agg.Record(FrameEventKind.Hit, Remote, Local, 25, 0f);
            Assert.AreEqual(DamageRole.Dealt, dealt.Role);
            DamageNumber taken = agg.Record(FrameEventKind.Hit, Local, Local, 30, 0f);
            Assert.AreEqual(DamageRole.Taken, taken.Role);
        }

        [Test]
        public void 窗口内同键命中_累加成一条()
        {
            var agg = new DamageNumberAggregator();
            agg.Record(FrameEventKind.Hit, Remote, Local, 25, 0f);
            DamageNumber merged = agg.Record(FrameEventKind.Hit, Remote, Local, 15, 0.1f);
            Assert.AreEqual(40, merged.Value, "窗口内应累加");
            Assert.AreEqual(1, agg.Count, "应只有一条");
        }

        [Test]
        public void 超窗同键命中_另起一条()
        {
            var agg = new DamageNumberAggregator();
            agg.Record(FrameEventKind.Hit, Remote, Local, 25, 0f);
            DamageNumber fresh = agg.Record(FrameEventKind.Hit, Remote, Local, 15,
                DamageNumberAggregator.MergeWindowSeconds + 0.1f);
            Assert.AreEqual(15, fresh.Value, "超窗应新起条目");
            Assert.AreEqual(2, agg.Count);
        }

        [Test]
        public void 暴击与普通_不互相合并()
        {
            var agg = new DamageNumberAggregator();
            agg.Record(FrameEventKind.Hit, Remote, Local, 25, 0f);
            DamageNumber crit = agg.Record(FrameEventKind.Crit, Remote, Local, 50, 0.1f);
            Assert.AreEqual(50, crit.Value, "暴击档独立，不与普通合并");
            Assert.IsTrue(crit.Crit);
        }

        [Test]
        public void 不同目标_不合并()
        {
            var agg = new DamageNumberAggregator();
            agg.Record(FrameEventKind.Hit, Remote, Local, 25, 0f);
            DamageNumber other = agg.Record(FrameEventKind.Hit, 30L, Local, 15, 0.1f);
            Assert.AreEqual(15, other.Value);
            Assert.AreEqual(2, agg.Count);
        }

        [Test]
        public void 淘汰_超窗条目被清()
        {
            var agg = new DamageNumberAggregator();
            agg.Record(FrameEventKind.Hit, Remote, Local, 25, 0f);
            int removed = agg.Prune(DamageNumberAggregator.MergeWindowSeconds * 3f + 1f);
            Assert.AreEqual(1, removed);
            Assert.AreEqual(0, agg.Count);
        }

        [Test]
        public void 重置_清空全部()
        {
            var agg = new DamageNumberAggregator();
            agg.Record(FrameEventKind.Hit, Remote, Local, 25, 0f);
            agg.Reset();
            Assert.AreEqual(0, agg.Count);
        }
    }
}
