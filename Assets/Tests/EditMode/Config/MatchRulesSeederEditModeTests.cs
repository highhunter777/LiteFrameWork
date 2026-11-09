using LiteSim;
using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 每局规则播种（<see cref="MatchRulesSeeder"/>）：显式步骤把本局规则持有件与宿主钩子
    /// 一次写清——测试房继承开关、常规入口复位；播种后可被客户端预测/本地服同实例消费。
    /// </summary>
    public sealed class MatchRulesSeederEditModeTests
    {
        [SetUp]
        public void Reset() => ResetState();

        [TearDown]
        public void Cleanup() => ResetState();

        private static void ResetState()
        {
            TestModeRuntime.Current.Active = false;
            TestModeRuntime.Current.BotFrozen = false;
            TestModeRuntime.Current.BotInfiniteHp = false;
            TestModeRuntime.Current.NoDeath = true;
            TestModeRuntime.Current.InfiniteAmmo = false;

            TestModeRuntime.Current.LocalTest.Active = false;
            TestModeRuntime.Current.LocalTest.BotFrozen = false;
            TestModeRuntime.Current.LocalTest.BotInfiniteHp = false;
            TestModeRuntime.Current.LocalTest.TeleportDispatch = false;
            TestModeRuntime.Current.LocalTest.BotResetRequested = false;
        }

        [Test]
        public void 测试房_播种活动局与宿主钩子_同实例登记()
        {
            TestModeRuntime.Current.NoDeath = true;
            TestModeRuntime.Current.InfiniteAmmo = true;
            TestModeRuntime.Current.BotFrozen = true;
            TestModeRuntime.Current.BotInfiniteHp = true;
            TestModeRuntime.Current.LocalTest.TeleportDispatch = true;          // 进房前的残留请求
            TestModeRuntime.Current.LocalTest.BotResetRequested = true;

            MatchTestRules rules = MatchRulesSeeder.Seed(testRoom: true, dev: new DevSession());

            Assert.IsNotNull(rules);
            Assert.IsTrue(rules.NoDeath);
            Assert.IsTrue(rules.InfiniteAmmo);
            Assert.IsTrue(TestModeRuntime.Current.LocalTest.Active, "宿主钩子开关＝测试房");
            Assert.IsTrue(TestModeRuntime.Current.LocalTest.BotFrozen);
            Assert.IsTrue(TestModeRuntime.Current.LocalTest.BotInfiniteHp);
            Assert.IsFalse(TestModeRuntime.Current.LocalTest.TeleportDispatch, "进房清残留");
            Assert.IsFalse(TestModeRuntime.Current.LocalTest.BotResetRequested, "进房清残留");
        }

        [Test]
        public void 常规入口_不继承测试房开关_宿主钩子复位()
        {
            MatchTestRules rules = MatchRulesSeeder.Seed(testRoom: false, dev: new DevSession());

            Assert.IsNotNull(rules);
            Assert.IsFalse(rules.NoDeath, "常规入口不从 TestModeRuntime 继承免死");
            Assert.IsFalse(rules.InfiniteAmmo);
            Assert.IsFalse(TestModeRuntime.Current.LocalTest.Active);
            Assert.IsFalse(TestModeRuntime.Current.LocalTest.BotFrozen);
        }

        [Test]
        public void 重复播种_替换活动局持有件()
        {
            MatchTestRules first = MatchRulesSeeder.Seed(testRoom: true, dev: new DevSession());
            MatchTestRules second = MatchRulesSeeder.Seed(testRoom: false, dev: new DevSession());

            Assert.AreNotSame(first, second, "每局新建持有件（运行期不跨局复用）");
            Assert.IsFalse(second.NoDeath);
        }
    }
}
