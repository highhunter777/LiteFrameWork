using System;
using System.Collections.Generic;
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
            TestModeRuntime.TakeDirectRequest();
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
            TestModeRuntime.TakeDirectRequest();
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

        private void RegisterProbe()
        {
            _registrations.Add(_commands.Register<DebugKernelProbeCommand>(
                new DebugKernelProbeHandler(), new CommandOptions(true, "无参探针")));
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
