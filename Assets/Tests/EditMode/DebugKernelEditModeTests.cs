using System;
using System.Collections.Generic;
using System.Reflection;
using LiteFramework;
using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 《GM 与调试工具分层专项设计》§7 内核契约：发现/执行/选项/请求桥/读模型。
    /// 投影（编辑器页、HUD、控制台）只消费这些口，不在用例里重复投影逻辑。
    /// </summary>
    public sealed class DebugKernelEditModeTests
    {
        private CommandCenter _commands;
        private DebugKernel _kernel;
        private readonly List<IDisposable> _registrations = new List<IDisposable>();
        private readonly Dictionary<TestModeOptions.Id, bool> _optionSnapshot = new Dictionary<TestModeOptions.Id, bool>();
        private bool _activeSnapshot;

        [SetUp]
        public void SetUp()
        {
            _commands = new CommandCenter(true);
            _kernel = new DebugKernel(_commands, null);
            _registrations.Clear();
            _optionSnapshot.Clear();
            foreach (TestModeOptions.Info option in TestModeOptions.All)
                _optionSnapshot[option.Id] = TestModeOptions.Get(option.Id);
            _activeSnapshot = TestModeRuntime.Active;
            TestModeRuntime.Active = false;               // 选项副作用不武装到活动局
            TestModeRuntime.TakeEnterRequest();
            TestModeRuntime.TakeExitRequest();
            TestModeRuntime.TakeTeleportRequest();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (IDisposable registration in _registrations)
                registration.Dispose();
            _registrations.Clear();

            foreach (KeyValuePair<TestModeOptions.Id, bool> pair in _optionSnapshot)
                TestModeOptions.Set(pair.Key, pair.Value);
            TestModeRuntime.Active = _activeSnapshot;
            TestModeRuntime.TakeEnterRequest();
            TestModeRuntime.TakeExitRequest();
            TestModeRuntime.TakeTeleportRequest();
        }

        [Test]
        public void 命令发现_按注册序返回且带GmOnly()
        {
            RegisterProbe();

            IReadOnlyList<CommandInfo> commands = _kernel.Commands;

            Assert.AreEqual(1, commands.Count);
            Assert.AreEqual(typeof(DebugKernelProbeCommand), commands[0].CommandType);
            Assert.IsTrue(commands[0].GmOnly);
        }

        [Test]
        public void 执行无参命令_类型名命中并进审计()
        {
            RegisterProbe();

            bool executed = _kernel.TryExecute(typeof(DebugKernelProbeCommand), out CommandResult result, out string error);

            Assert.IsTrue(executed, error);
            Assert.IsTrue(result.Ok);
            Assert.AreEqual("probe-ok", result.Detail);
            Assert.AreEqual(1, _kernel.Audit.Count);
            Assert.AreEqual(typeof(DebugKernelProbeCommand), _kernel.Audit[0].CommandType);
            Assert.IsTrue(_kernel.Audit[0].Ok);
        }

        [Test]
        public void 执行无参命令_字符串精确名与唯一后缀都命中()
        {
            RegisterProbe();

            Assert.IsTrue(_kernel.TryExecute(nameof(DebugKernelProbeCommand), out CommandResult exact, out string exactError), exactError);
            Assert.IsTrue(exact.Ok);
            Assert.IsTrue(_kernel.TryExecute("ProbeCommand", out CommandResult suffix, out string suffixError), suffixError);
            Assert.IsTrue(suffix.Ok);
        }

        [Test]
        public void 执行带参命令_拒绝并说明需专面板()
        {
            _registrations.Add(_commands.Register<DebugKernelParameterizedCommand>(
                new DebugKernelParameterizedHandler(), new CommandOptions(true, "带参探针")));

            bool executed = _kernel.TryExecute(typeof(DebugKernelParameterizedCommand), out _, out string error);

            Assert.IsFalse(executed);
            StringAssert.Contains("无参", error);
        }

        [Test]
        public void 执行未知命令_返回错误()
        {
            Assert.IsFalse(_kernel.TryExecute("NoSuchCommand", out _, out string error));
            StringAssert.Contains("未找到", error);
        }

        [Test]
        public void 设置选项_已知id写入单表()
        {
            bool applied = _kernel.TrySetOption("InfiniteAmmo", true, out string error);

            Assert.IsTrue(applied, error);
            Assert.IsTrue(_kernel.GetOption(TestModeOptions.Id.InfiniteAmmo));
            Assert.IsTrue(TestModeOptions.Get(TestModeOptions.Id.InfiniteAmmo));
        }

        [Test]
        public void 设置选项_未知id返回错误()
        {
            Assert.IsFalse(_kernel.TrySetOption("NoSuchOption", true, out string error));
            StringAssert.Contains("未找到", error);
        }

        [Test]
        public void 请求桥_写入后可被消费且读即清()
        {
            _kernel.RequestEnterTestMode();
            Assert.IsTrue(TestModeRuntime.TakeEnterRequest());
            Assert.IsFalse(TestModeRuntime.TakeEnterRequest());

            _kernel.RequestTeleport();
            Assert.IsTrue(TestModeRuntime.TakeTeleportRequest());
            Assert.IsFalse(TestModeRuntime.TakeTeleportRequest());
        }

        [Test]
        public void 无对局时_对局诊断为无样本()
        {
            if (BattleDiagnostics.Current != null) BattleDiagnostics.Detach(BattleDiagnostics.Current);

            Assert.IsFalse(_kernel.HasBattle);
            Assert.IsEmpty(string.Join("\n", _kernel.DescribeBattle()));
        }

        [Test]
        public void 直连测试身份_已退役()
        {
            Assert.IsNull(typeof(TestModeRuntime).GetMethod("RequestDirect",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static), "RequestDirect 已移除");
            Assert.IsNull(typeof(TestModeRuntime).GetMethod("TakeDirectRequest",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static), "TakeDirectRequest 已移除");
            Assert.IsNull(typeof(ProcedureMatch).GetField("TestToken",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static), "TestToken 已移除");
            Assert.IsNull(typeof(ProcedureMatch).GetField("DefaultRoomId",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static), "Room-A 常量已移除");
            Assert.IsNull(typeof(DevStartupConfig).GetField("UseLocalServer",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance), "UseLocalServer 已移除");
        }

        [Test]
        public void 读模型发现_包含命令选项与三类读数()
        {
            IReadOnlyList<string> sections = DebugKernel.ReadSections;

            CollectionAssert.Contains(sections, "commands");
            CollectionAssert.Contains(sections, "options");
            CollectionAssert.Contains(sections, "stats");
            CollectionAssert.Contains(sections, "log");
            CollectionAssert.Contains(sections, "diag");
            CollectionAssert.Contains(sections, "battle");
            CollectionAssert.Contains(sections, "audit");
        }

        [TestCase("", false)]
        [TestCase("   ", false)]
        [TestCase("help", true)]
        [TestCase("  HeLP  a  b ", true)]
        [TestCase("option InfiniteAmmo on", true)]
        public void 控制台解析_空白与大小写与参数(string line, bool expected)
        {
            bool parsed = DebugConsoleCommand.TryParse(line, out _, out _);

            Assert.AreEqual(expected, parsed);
        }

        [Test]
        public void 控制台解析_切出verb与参数()
        {
            Assert.IsTrue(DebugConsoleCommand.TryParse("  HeLP  a  b ", out string verb, out string[] args));

            Assert.AreEqual("help", verb);
            CollectionAssert.AreEqual(new[] { "a", "b" }, args);
        }

        [Test]
        public void 调参发现_按描述读写与复位()
        {
            var provider = new DebugTunableProbeProvider();
            var kernel = new DebugKernel(_commands, null, new IDebugTunableProvider[] { provider });
            kernel.RebuildTunables();

            Assert.AreEqual(1, System.Linq.Enumerable.Count(kernel.Tunables, t => t.Id == "probe.value"));
            Assert.IsTrue(kernel.TrySetTunable("probe.value", 7f, out string error), error);
            Assert.AreEqual(7f, provider.Value);
            Assert.IsTrue(kernel.ResetTunable("probe.value", out error), error);
            Assert.AreEqual(2f, provider.Value);
            Assert.IsFalse(kernel.TrySetTunable("probe.value", 99f, out error));
            StringAssert.Contains("超出范围", error);
        }

        [Test]
        public void 运行期调参_只保留白名单且排除Product()
        {
            var provider = new DebugTunableRuntimeProbeProvider();
            var kernel = new DebugKernel(_commands, null, new IDebugTunableProvider[] { provider });
            kernel.RebuildTunables();

            Assert.IsTrue(System.Linq.Enumerable.Any(kernel.Tunables, t => t.Id == "probe.runtime"));
            Assert.IsTrue(System.Linq.Enumerable.Any(kernel.Tunables, t => t.Id == "probe.product"));
            Assert.IsFalse(System.Linq.Enumerable.Any(kernel.RuntimeTunables, t => t.Id == "probe.product"));
            Assert.IsFalse(System.Linq.Enumerable.Any(kernel.RuntimeTunables, t => t.Id == "probe.editor-only"));
            Assert.IsTrue(System.Linq.Enumerable.Any(kernel.RuntimeTunables, t => t.Id == "probe.runtime"));
        }

        [Test]
        public void 产品参数_准心与视角已定档且不进运行期白名单()
        {
            var kernel = new DebugKernel(_commands, null, new IDebugTunableProvider[] { new FeatureDebugTunables() });
            kernel.RebuildTunables();

            var productIds = new List<string>();
            for (int i = 0; i < kernel.Tunables.Count; i++)
            {
                DebugTunable tunable = kernel.Tunables[i];
                if (tunable.Scope != DebugTunableScope.Product) continue;
                productIds.Add(tunable.Id);
                Assert.IsFalse(tunable.RuntimeVisible, tunable.Id + " 已定档，不应再声明运行期可见");
            }

            CollectionAssert.AreEquivalent(new[]
            {
                "crosshair.static", "crosshair.growth", "crosshair.cap", "crosshair.kick",
                "crosshair.burst-recover", "crosshair.kick-recover",
                "look.sensitivity", "look.ads-scale",
            }, productIds);
            Assert.AreEqual(11, kernel.Tunables.Count);
            Assert.AreEqual(2, kernel.RuntimeTunables.Count);
            Assert.IsFalse(System.Linq.Enumerable.Any(kernel.RuntimeTunables, t => t.Scope == DebugTunableScope.Product));
        }

        [Test]
        public void 武器试验参数_发现写口与待用值双落()
        {
            int damageSnapshot = TestModeRuntime.WeaponDamageOverride;
            int rpmSnapshot = TestModeRuntime.WeaponRpmOverride;
            try
            {
                var kernel = new DebugKernel(_commands, null, new IDebugTunableProvider[] { new WeaponDebugTunables() });
                kernel.RebuildTunables();

                Assert.AreEqual(2, kernel.Tunables.Count);
                Assert.AreEqual(2, kernel.RuntimeTunables.Count);
                Assert.IsTrue(kernel.TrySetTunable("weapon.damage", 50f, out string error), error);
                Assert.AreEqual(50, TestModeRuntime.WeaponDamageOverride);
                Assert.AreEqual(50f, new WeaponDebugTunables().CurrentDamage());
                Assert.IsTrue(kernel.TrySetTunable("weapon.rpm", 1200f, out error), error);
                Assert.AreEqual(1200, TestModeRuntime.WeaponRpmOverride);
                Assert.IsTrue(kernel.ResetTunable("weapon.damage", out error), error);
                Assert.AreEqual(-1, TestModeRuntime.WeaponDamageOverride);
            }
            finally
            {
                TestModeRuntime.WeaponDamageOverride = damageSnapshot;
                TestModeRuntime.WeaponRpmOverride = rpmSnapshot;
            }
        }

        [Test]
        public void 调参文本_Bool与Float解析()
        {
            DebugTunable boolTunable = DebugTunableRuntimeProbeProvider.BoolTunable();
            Assert.IsTrue(DebugTunableText.TryParse(boolTunable, "on", out float on, out _));
            Assert.AreEqual(1f, on);
            Assert.IsTrue(DebugTunableText.TryParse(boolTunable, "false", out float off, out _));
            Assert.AreEqual(0f, off);
            Assert.IsFalse(DebugTunableText.TryParse(boolTunable, "maybe", out _, out _));

            DebugTunable floatTunable = DebugTunableRuntimeProbeProvider.FloatTunable();
            Assert.IsTrue(DebugTunableText.TryParse(floatTunable, "0.25", out float value, out _));
            Assert.AreEqual(0.25f, value);
            Assert.IsFalse(DebugTunableText.TryParse(floatTunable, "9", out _, out string error));
            StringAssert.Contains("超出范围", error);
        }

        [Test]
        public void 测试模式与日志上下文读口_经内核()
        {
            Assert.AreEqual(TestModeRuntime.Active, _kernel.IsTestModeActive);
            Assert.AreEqual(Log.CurrentMatchId, _kernel.CurrentMatchId);
            Assert.AreEqual(Log.CurrentSessionId, _kernel.CurrentSessionId);
        }

        [Test]
        public void 重置假人_宿主钩子经内核置位()
        {
            try
            {
                _kernel.RequestBotReset();
                Assert.IsTrue(LiteSim.SimTestRules.BotResetRequested);
            }
            finally
            {
                LiteSim.SimTestRules.BotResetRequested = false;
            }
        }

        [Test]
        public void 性能读模型_帧样本与HUD耗时回填()
        {
            var kernel = new DebugKernel(_commands, null);
            for (int i = 0; i < 40; i++) kernel.SampleFrame(0.016f);   // 30 帧预热 + 10 帧样本

            Assert.IsTrue(kernel.TryTakePerf(out PerfReading perf));
            Assert.AreEqual(10, perf.Frames.SampleCount);
            Assert.GreaterOrEqual(perf.Frames.Fps, 50);
            Assert.LessOrEqual(perf.Frames.Fps, 70);
            Assert.AreEqual(0, perf.Frames.LongFrames);
            Assert.Greater(perf.Frames.P50Ms, 15f);
            Assert.Less(perf.Frames.P99Ms, 18f);

            kernel.RecordHudBuildMs(5f);
            kernel.RecordHudBuildMs(5f);
            Assert.IsTrue(kernel.TryTakePerf(out PerfReading after));
            Assert.AreEqual(5f, after.HudBuildMs, 0.01f);
        }

        [Test]
        public void 性能读模型_无帧样本为空()
        {
            var kernel = new DebugKernel(_commands, null);

            Assert.IsFalse(kernel.TryTakePerf(out PerfReading perf));
            Assert.IsFalse(perf.HasFrames);
        }

        private void RegisterProbe()
        {
            _registrations.Add(_commands.Register<DebugKernelProbeCommand>(
                new DebugKernelProbeHandler(), new CommandOptions(true, "无参探针")));
        }
    }

    public sealed class DebugTunableRuntimeProbeProvider : IDebugTunableProvider
    {
        public void CollectDebugTunables(ICollection<DebugTunable> into)
        {
            into.Add(FloatTunable(runtimeVisible: true));
            into.Add(new DebugTunable
            {
                Id = "probe.product",
                Group = "探针",
                Label = "产品探针",
                Kind = DebugTunableKind.Float,
                Scope = DebugTunableScope.Product,
                Default = 1f,
                Min = 0f,
                Max = 2f,
                RuntimeVisible = true,
                Get = () => 1f,
                Set = _ => { },
            });
            into.Add(FloatTunable(id: "probe.editor-only", runtimeVisible: false));
        }

        public static DebugTunable FloatTunable(string id = "probe.runtime", bool runtimeVisible = true)
            => new DebugTunable
            {
                Id = id,
                Group = "探针",
                Label = "运行期探针",
                Kind = DebugTunableKind.Float,
                Scope = DebugTunableScope.Sandbox,
                Default = 1f,
                Min = 0f,
                Max = 2f,
                RuntimeVisible = runtimeVisible,
                Get = () => 1f,
                Set = _ => { },
            };

        public static DebugTunable BoolTunable()
            => new DebugTunable
            {
                Id = "probe.bool",
                Group = "探针",
                Label = "Bool 探针",
                Kind = DebugTunableKind.Bool,
                Scope = DebugTunableScope.Sandbox,
                Default = 0f,
                Min = 0f,
                Max = 1f,
                Get = () => 0f,
                Set = _ => { },
            };
    }

    public sealed class DebugTunableProbeProvider : IDebugTunableProvider
    {
        public float Value = 2f;

        public void CollectDebugTunables(ICollection<DebugTunable> into)
        {
            into.Add(new DebugTunable
            {
                Id = "probe.value",
                Group = "探针",
                Label = "探针值",
                Kind = DebugTunableKind.Float,
                Scope = DebugTunableScope.Sandbox,
                Default = 2f,
                Min = 0f,
                Max = 10f,
                Get = () => Value,
                Set = value => Value = value,
                Reset = () => Value = 2f,
            });
        }
    }

    public sealed class DebugKernelProbeCommand : ICommand
    {
    }

    public sealed class DebugKernelParameterizedCommand : ICommand
    {
        public DebugKernelParameterizedCommand(int value) { }
    }

    public sealed class DebugKernelProbeHandler : ICommandHandler<DebugKernelProbeCommand>
    {
        public CommandResult Execute(DebugKernelProbeCommand command) => CommandResult.Success("probe-ok");
    }

    public sealed class DebugKernelParameterizedHandler : ICommandHandler<DebugKernelParameterizedCommand>
    {
        public CommandResult Execute(DebugKernelParameterizedCommand command) => CommandResult.Success("parameterized-ok");
    }
}
