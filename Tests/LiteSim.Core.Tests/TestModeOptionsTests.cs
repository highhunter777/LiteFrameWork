using System;
using System.Linq;
using LiteGame;        // TestModeOptions/TestModeRuntime：源链接纳入 L1（链接组注释见 LiteSim.Core.Tests.csproj）
using LiteSim;
using Xunit;

namespace LiteSim.Tests
{
    /// <summary>
    /// 测试模式开关写口用例（《GM 与调试工具分层设计》§1：TestModeOptions=开关唯一写点+副作用收口，
    /// TestModeRuntime=运行时快照+请求桥+爆头跨局待用值）。两件零引擎依赖按源链接纳入 L1；
    /// 被测面是全局静态——开测前复位（程序集串行纪律见 <see cref="TestParallelization"/>，历史动因正是
    /// TestMode/TestModeRuntime 一族静态）。副作用口径：InfiniteAmmo/BotInfiniteHp 仅测试模式（Active 门）武装，
    /// 非测试模式一律不写运行态。
    /// </summary>
    public sealed class TestModeOptionsTests
    {
        /// <summary>开测前复位被测静态与用例写到的宿主钩子（请求桥残留经 Take* 读即清吸收——清零语义只归消费方）。</summary>
        private static void Reset()
        {
            TestModeRuntime.Current.Active = false;
            TestModeRuntime.Current.TakeEnterRequest();                    // 复位：吸收残留请求（读即清）
            TestModeRuntime.Current.TakeExitRequest();
            TestModeRuntime.Current.TakeTeleportRequest();
            TestModeRuntime.Current.HeadHitLineOverride = -1f;
            TestModeRuntime.Current.HeadshotRadiusOverride = -1f;
            TestModeRuntime.Current.BotInfiniteHp = false;
            TestModeRuntime.Current.LocalTest.BotInfiniteHp = false;
            TestModeOptions.ApplyDefaults();              // 开关全默认 + BotCount 回 1（数量项不在开关表）
        }

        [Fact]
        public void 开关表_覆盖全部Id且无重复_标签非空_两组齐备()
        {
            Reset();

            // 表漏 Id ⇒ Get/Set 直读字段的 switch 不同步（新增开关只改表的面约定失去保证）
            Assert.Equal(Enum.GetValues(typeof(TestModeOptions.Id)).Length, TestModeOptions.All.Length);
            Assert.Equal(TestModeOptions.All.Length, TestModeOptions.All.Select(o => o.Id).Distinct().Count());
            Assert.All(TestModeOptions.All, o => Assert.False(string.IsNullOrEmpty(o.Label)));

            // 两分组齐备：GM 面板与 DevStartupConfig 按分组分流渲染（组消失 = 页面静默漏区）
            Assert.Contains(TestModeOptions.All, o => o.Group == TestModeOptions.Group.Startup);
            Assert.Contains(TestModeOptions.All, o => o.Group == TestModeOptions.Group.Live);
        }

        [Fact]
        public void 单一写点_全开关正反向经Get同读()
        {
            Reset();

            foreach (TestModeOptions.Info option in TestModeOptions.All)
            {
                TestModeOptions.Set(option.Id, !option.Default);
                Assert.Equal(!option.Default, TestModeOptions.Get(option.Id));
                TestModeOptions.Set(option.Id, option.Default);
                Assert.Equal(option.Default, TestModeOptions.Get(option.Id));
            }
        }

        [Fact]
        public void 默认口径_ApplyDefaults逐开关套表值_数量回1()
        {
            Reset();
            foreach (TestModeOptions.Info option in TestModeOptions.All)
                TestModeOptions.Set(option.Id, !option.Default);          // 先反向污染
            TestModeRuntime.Current.BotCount = 99;

            TestModeRuntime.Current.ApplyDefaults();

            foreach (TestModeOptions.Info option in TestModeOptions.All)
                Assert.Equal(option.Default, TestModeOptions.Get(option.Id));
            Assert.Equal(1, TestModeRuntime.Current.BotCount);                     // 表外数量项随默认口径单独落
        }

        [Fact]
        public void 无限子弹副作用_仅测试模式且有活动局才写规则持有件()
        {
            // 非测试模式：快照照记，规则持有件不武装（判定路径读注入实例——测试模式外不受 GM 开关影响）
            Reset();
            var match = new MatchTestRules();
            TestModeOptions.Set(TestModeOptions.Id.InfiniteAmmo, true, match);
            Assert.True(TestModeRuntime.Current.InfiniteAmmo);
            Assert.False(match.InfiniteAmmo);

            // 测试模式 + 无对局（未给持有件）：只记快照（下次进房入口种子），不炸
            Reset();
            TestModeRuntime.Current.Active = true;
            TestModeOptions.Set(TestModeOptions.Id.InfiniteAmmo, true);
            Assert.True(TestModeRuntime.Current.InfiniteAmmo);

            // 测试模式 + 有对局：快照与持有件同步（本地服同进程——滑杆一动两端同值）
            Reset();
            TestModeRuntime.Current.Active = true;
            match = new MatchTestRules();
            TestModeOptions.Set(TestModeOptions.Id.InfiniteAmmo, true, match);
            Assert.True(match.InfiniteAmmo);
        }

        [Fact]
        public void bot无限生命副作用_宿主钩子等于测试模式与开关的与门()
        {
            Reset();
            TestModeRuntime.Current.Active = false;
            TestModeOptions.Set(TestModeOptions.Id.BotInfiniteHp, true);
            Assert.False(TestModeRuntime.Current.LocalTest.BotInfiniteHp);                      // 非测试模式一律不武装

            TestModeRuntime.Current.Active = true;
            TestModeOptions.Set(TestModeOptions.Id.BotInfiniteHp, true);
            Assert.True(TestModeRuntime.Current.LocalTest.BotInfiniteHp);                        // 与门开

            TestModeOptions.Set(TestModeOptions.Id.BotInfiniteHp, false);
            Assert.False(TestModeRuntime.Current.LocalTest.BotInfiniteHp);                       // 现场关经同一写口收口（不残留 true）
        }

        [Fact]
        public void 请求桥与爆头待用值_套默认不代清_消费即清二次为假()
        {
            Reset();
            TestModeRuntime.Current.RequestEnter();
            TestModeRuntime.Current.RequestExit();
            TestModeRuntime.Current.RequestTeleport();
            TestModeRuntime.Current.HeadHitLineOverride = 1.2f;
            TestModeRuntime.Current.HeadshotRadiusOverride = 0.3f;

            TestModeRuntime.Current.ApplyDefaults();                               // 套默认只管开关与数量

            // 请求不被写口代清；消费 = Take* 读即清（一次性语义由 API 编译期强制——本批根治项的行为面）
            Assert.True(TestModeRuntime.Current.TakeEnterRequest());
            Assert.True(TestModeRuntime.Current.TakeExitRequest());
            Assert.True(TestModeRuntime.Current.TakeTeleportRequest());
            Assert.Equal(1.2f, TestModeRuntime.Current.HeadHitLineOverride);      // 跨局待用值同样不代清
            Assert.Equal(0.3f, TestModeRuntime.Current.HeadshotRadiusOverride);

            // 二次消费恒假：忘清零/双消费在类型化读即清口径下不可能
            Assert.False(TestModeRuntime.Current.TakeEnterRequest());
            Assert.False(TestModeRuntime.Current.TakeExitRequest());
            Assert.False(TestModeRuntime.Current.TakeTeleportRequest());
        }
    }
}
