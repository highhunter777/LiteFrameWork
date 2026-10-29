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
        public void ResetStatics()
        {
            SimTestRules.Active = false;
            SimTestRules.BotFrozen = false;
            SimTestRules.BotInfiniteHp = false;
            SimTestRules.TeleportDispatch = false;
            SimTestRules.BotResetRequested = false;
            TestModeRuntime.Current.NoDeath = true;
            TestModeRuntime.Current.InfiniteAmmo = false;
            TestModeRuntime.Current.BotFrozen = false;
            TestModeRuntime.Current.BotInfiniteHp = false;
        }

        [TearDown]
        public void Cleanup()
        {
            SimTestRules.Active = false;
            SimTestRules.BotFrozen = false;
            SimTestRules.BotInfiniteHp = false;
            TestModeRuntime.Current.NoDeath = true;
            TestModeRuntime.Current.InfiniteAmmo = false;
            TestModeRuntime.Current.BotFrozen = false;
            TestModeRuntime.Current.BotInfiniteHp = false;
        }

        [Test]
        public void 测试房_播种活动局与宿主钩子_同实例登记()
        {
            TestModeRuntime.Current.NoDeath = true;
            TestModeRuntime.Current.InfiniteAmmo = true;
            TestModeRuntime.Current.BotFrozen = true;
            TestModeRuntime.Current.BotInfiniteHp = true;
            SimTestRules.TeleportDispatch = true;          // 进房前的残留请求
            SimTestRules.BotResetRequested = true;

            MatchTestRules rules = MatchRulesSeeder.Seed(testRoom: true, dev: new DevSession());

            Assert.IsNotNull(rules);
            Assert.IsTrue(rules.NoDeath);
            Assert.IsTrue(rules.InfiniteAmmo);
            Assert.IsTrue(SimTestRules.Active, "宿主钩子开关＝测试房");
            Assert.IsTrue(SimTestRules.BotFrozen);
            Assert.IsTrue(SimTestRules.BotInfiniteHp);
            Assert.IsFalse(SimTestRules.TeleportDispatch, "进房清残留");
            Assert.IsFalse(SimTestRules.BotResetRequested, "进房清残留");
        }

        [Test]
        public void 常规入口_不继承测试房开关_宿主钩子复位()
        {
            MatchTestRules rules = MatchRulesSeeder.Seed(testRoom: false, dev: new DevSession());

            Assert.IsNotNull(rules);
            Assert.IsFalse(rules.NoDeath, "常规入口不从 TestModeRuntime 继承免死");
            Assert.IsFalse(rules.InfiniteAmmo);
            Assert.IsFalse(SimTestRules.Active);
            Assert.IsFalse(SimTestRules.BotFrozen);
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
