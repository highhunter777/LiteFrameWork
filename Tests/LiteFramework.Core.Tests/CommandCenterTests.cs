using System;
using System.Collections.Generic;
using System.Linq;
using LiteFramework;
using Xunit;

namespace LiteFramework.Tests
{
    /// <summary>
    /// CommandCenter 直测（《命令中心专项设计》§2/§3/§6 验收——机制件零直测不允许）：
    /// 批1 核心（注册/唯一性/执行序/异常隔离/fail-fast/权限门两态/审计环/统计）
    /// + 批2 读取面（发现面/审计读取）。
    /// [Collection("CoreStatic")]：处理器异常路径写全局 Log（ErrorCount/Recent），与断言日志面的用例串行。
    /// </summary>
    [Collection("CoreStatic")]
    public sealed class CommandCenterTests
    {
        private sealed class Ping : ICommand { }
        private sealed class GmPing : ICommand { }
        private sealed class Nobody : ICommand { }

        private sealed class ProbeHandler : ICommandHandler<Ping>
        {
            public int Calls;
            public bool Reject;
            public bool Throw;
            public CommandResult Execute(Ping command)
            {
                Calls++;
                if (Throw) throw new InvalidOperationException("处理器故障注入");
                return Reject
                    ? CommandResult.Fail(CommandReject.HandlerRejected, "业务拒绝")
                    : CommandResult.Success("执行成功");
            }
        }

        // ---- 批1 核心 ----

        [Fact]
        public void 注册Send_成功路径_处理器收到且结果Ok()
        {
            var commands = new CommandCenter();
            var handler = new ProbeHandler();
            commands.Register(handler);

            var result = commands.Send(new Ping());

            Assert.True(result.Ok);
            Assert.Equal(CommandReject.None, result.Reason);
            Assert.Equal(1, handler.Calls);
            Assert.True(commands.HasHandler<Ping>());
        }

        [Fact]
        public void 处理器主动拒绝_失败结果透传且计数()
        {
            var commands = new CommandCenter();
            commands.Register(new ProbeHandler { Reject = true });

            var result = commands.Send(new Ping());

            Assert.False(result.Ok);
            Assert.Equal(CommandReject.HandlerRejected, result.Reason);
            Assert.Equal("业务拒绝", result.Detail);
            var into = new Dictionary<string, string>();
            ((IModuleStats)commands).Snapshot(into);
            Assert.Equal("1", into["Failed"]);
        }

        [Fact]
        public void 处理器异常_隔离为Failed不炸调用方_日志与审计留痕()
        {
            var commands = new CommandCenter();
            commands.Register(new ProbeHandler { Throw = true });

            var result = commands.Send(new Ping());        // 不抛：隔离为失败结果

            Assert.False(result.Ok);
            Assert.Equal(CommandReject.HandlerFault, result.Reason);
            var last = Log.Recent[Log.Recent.Count - 1];
            Assert.Equal(LogLevel.Error, last.Level);
            var into = new Dictionary<string, string>();
            ((IModuleStats)commands).Snapshot(into);
            Assert.Equal("1", into["Failed"]);              // 返回失败与异常合计
        }

        [Fact]
        public void 无处理器_编程错误直接抛()
        {
            var commands = new CommandCenter();
            Assert.Throws<InvalidOperationException>(() => commands.Send(new Nobody()));
        }

        [Fact]
        public void 重复注册_唯一处理器契约抛()
        {
            var commands = new CommandCenter();
            commands.Register(new ProbeHandler());
            Assert.Throws<InvalidOperationException>(() => commands.Register(new ProbeHandler()));
        }

        [Fact]
        public void 注销幂等_注销后Send转无处理器抛()
        {
            var commands = new CommandCenter();
            var un = commands.Register(new ProbeHandler());
            un.Dispose();
            un.Dispose();                                   // 幂等
            Assert.False(commands.HasHandler<Ping>());
            Assert.Throws<InvalidOperationException>(() => commands.Send(new Ping()));

            var un2 = commands.Register(new ProbeHandler());    // 注销后方可重新注册
            Assert.True(commands.HasHandler<Ping>());
        }

        [Fact]
        public void 权限门_GmOnly三宏内放行()
        {
            var commands = new CommandCenter(gmEnabled: true);   // 三宏内语义（L1 构建带 LITEFRAMEWORK_DEBUG）
            var handler = new GmProbeHandler();
            commands.Register(handler, new CommandOptions(gmOnly: true, description: "GM 示例"));

            var result = commands.Send(new GmPing());

            Assert.True(result.Ok);                          // 三宏内：GM 能力放行
            Assert.Equal(1, handler.Calls);
        }

        [Fact]
        public void 权限门_GmOnly三宏外拒绝_不执行不抛且计数()
        {
            var commands = new CommandCenter(gmEnabled: false);  // release 语义（构造注入可测接缝）
            var handler = new GmProbeHandler();
            commands.Register(handler, new CommandOptions(gmOnly: true));

            var result = commands.Send(new GmPing());       // 运行态拒绝：失败结果不抛

            Assert.False(result.Ok);
            Assert.Equal(CommandReject.GmBlocked, result.Reason);
            Assert.Equal(0, handler.Calls);                  // 物理不执行
            var into = new Dictionary<string, string>();
            ((IModuleStats)commands).Snapshot(into);
            Assert.Equal("1", into["GmBlocked"]);
            Assert.Equal("0", into["Failed"]);              // 拦截不是失败：两计数不混
        }

        [Fact]
        public void 权限门_非Gm命令不受门影响()
        {
            var commands = new CommandCenter(gmEnabled: false);
            commands.Register(new ProbeHandler());

            Assert.True(commands.Send(new Ping()).Ok);      // 业务命令 release 正常执行
        }

        [Fact]
        public void 审计环_容量32_环形覆盖_旧到新序()
        {
            var commands = new CommandCenter();
            commands.Register(new ProbeHandler());
            for (int i = 0; i < 40; i++) commands.Send(new Ping());   // 超容量：前 8 条被覆盖

            var audit = commands.GetAuditLog();
            Assert.Equal(32, audit.Count);
            Assert.Equal(9, audit[0].Seq);                  // 最旧留痕为第 9 次
            Assert.Equal(40, audit[31].Seq);                // 最新为第 40 次
            Assert.True(audit[31].Ok);
            Assert.Equal(CommandReject.None, audit[31].Reason);
        }

        [Fact]
        public void 统计_Snapshot四键()
        {
            var commands = new CommandCenter();
            var into = new Dictionary<string, string>();
            ((IModuleStats)commands).Snapshot(into);

            Assert.Equal("0", into["Registered"]);
            Assert.Equal("0", into["Sent"]);
            Assert.Equal("0", into["GmBlocked"]);
            Assert.Equal("0", into["Failed"]);
        }

        // ---- 批2 读取面 ----

        [Fact]
        public void 发现面_注册清单含类型GmOnly描述与时序()
        {
            var commands = new CommandCenter();
            commands.Register(new ProbeHandler(), new CommandOptions(gmOnly: false, description: "常规命令"));
            commands.Register(new ThrowlessGmHandler(), new CommandOptions(gmOnly: true, description: "GM 命令"));

            var infos = commands.GetRegisteredInfos();
            Assert.Equal(2, infos.Count);
            var ping = infos.First(i => i.CommandType == typeof(Ping));
            var gm = infos.First(i => i.CommandType == typeof(GmPing));
            Assert.False(ping.GmOnly);
            Assert.Equal("常规命令", ping.Description);
            Assert.True(gm.GmOnly);
            Assert.Equal("GM 命令", gm.Description);
            Assert.True(gm.RegisteredAt > ping.RegisteredAt);    // 注册时序可排
        }

        [Fact]
        public void 发现面_注销后清单同步消失()
        {
            var commands = new CommandCenter();
            var un = commands.Register(new ProbeHandler());
            Assert.Single(commands.GetRegisteredInfos());
            un.Dispose();
            Assert.Empty(commands.GetRegisteredInfos());
        }

        private sealed class GmProbeHandler : ICommandHandler<GmPing>
        {
            public int Calls;
            public CommandResult Execute(GmPing command)
            {
                Calls++;
                return CommandResult.Success("GM 执行成功");
            }
        }

        private sealed class ThrowlessGmHandler : ICommandHandler<GmPing>
        {
            public CommandResult Execute(GmPing command) => CommandResult.Success();
        }
    }
}
