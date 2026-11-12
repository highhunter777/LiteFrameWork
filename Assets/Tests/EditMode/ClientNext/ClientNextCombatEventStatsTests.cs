using LiteGameNext.Features.Battle;
using LiteSim;
using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>战斗事件统计：命中/击杀/开火/着墙分类与归属——命中判定的唯一证据来源。</summary>
    public sealed class ClientNextCombatEventStatsTests
    {
        private const long Local = 10L;
        private const long Other = 20L;

        [Test]
        public void 命中事件_按归属累计()
        {
            var stats = new CombatEventStats();
            // A=被命中者、B=射手；本地_10 打到 _20 → 本地造成 1 次命中
            stats.Record(FrameEventKind.Hit, Other, Local, 25, Local);
            Assert.AreEqual(1L, stats.HitEvents);
            Assert.AreEqual(1, stats.HitsDealtBy(Local));
            Assert.AreEqual(Other, stats.LastHitTarget);
            Assert.AreEqual(25, stats.LastHitDamage);
        }

        [Test]
        public void 被命中_计入承受数()
        {
            var stats = new CombatEventStats();
            stats.Record(FrameEventKind.Hit, Local, Other, 30, Local);
            Assert.AreEqual(1, stats.HitsTakenBy(Local));
            Assert.AreEqual(0, stats.HitsDealtBy(Local));
            Assert.AreEqual(30, stats.LastHitDamage);
        }

        [Test]
        public void 暴击计入命中_着墙单独计()
        {
            var stats = new CombatEventStats();
            stats.Record(FrameEventKind.Crit, Other, Local, 50, Local);
            stats.Record(FrameEventKind.ObstacleHit, Local, 0, 0, Local);
            stats.Record(FrameEventKind.Fire, Local, 0, 0, Local);
            stats.Record(FrameEventKind.Death, Other, Local, 0, Local);
            Assert.AreEqual(1L, stats.HitEvents, "暴击应计入命中");
            Assert.AreEqual(1L, stats.ObstacleHits);
            Assert.AreEqual(1L, stats.FireEvents);
            Assert.AreEqual(1L, stats.DeathEvents);
            Assert.AreEqual(4L, stats.TotalEvents);
        }

        [Test]
        public void 重置_清空全部计数()
        {
            var stats = new CombatEventStats();
            stats.Record(FrameEventKind.Hit, Other, Local, 10, Local);
            stats.Reset();
            Assert.AreEqual(0L, stats.TotalEvents);
            Assert.AreEqual(0L, stats.HitEvents);
            Assert.AreEqual(0, stats.HitsDealtBy(Local));
            Assert.AreEqual(0L, stats.LastHitTarget);
        }
    }
}
