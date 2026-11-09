using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiteFramework;
using Xunit;

namespace LiteFramework.Tests
{
    /// <summary>
    /// CommandBus 高级执行形态直测（《命令总线专项设计》§4 批3）： 排队（fire-and-forget/到期序/有界溢出/执行时过门与拦截）。
    /// </summary>
    [Collection("CoreStatic")]
    public sealed class CommandAdvancedTests
    {
        private sealed class Ping : ICommand { }
        private sealed class Plain : ICommand { }
        private sealed class GMPing : ICommand { }

        private sealed class EchoHandler : ICommandHandler<Ping>
        {
            public int Calls;
            public bool Reject;
            public bool Throw;
            public CommandResult Execute(Ping command)
            {
                Calls++;
                if (Throw) throw new InvalidOperationException("处理器故障注入");
                return Reject ? CommandResult.Fail(CommandReject.HandlerRejected, "业务拒") : CommandResult.Success("ok");
            }
        }

        private sealed class GMEcho : ICommandHandler<GMPing>
        {
            public int Calls;
            public CommandResult Execute(GMPing command) { Calls++; return CommandResult.Success("gm"); }
        }

        private sealed class AsyncPingHandler : IAsyncCommandHandler<Ping>
        {
            public bool Throw;
            public int Calls;
            public ValueTask<CommandResult> ExecuteAsync(Ping command, CancellationToken ct)
            {
                Calls++;
                if (Throw) throw new InvalidOperationException("异步处理器故障注入");
                return ValueTask.FromResult(CommandResult.Success("async"));
            }
        }

        // ---- 排队执行（§4） ----

        [Fact]
        public void SendQueued_到期按入队序执行()
        {
            var commands = new CommandBus();
            var echo = new EchoHandler();
            commands.Register(echo);

            commands.SendQueued(new Ping(), delayFrames: 1);
            commands.SendQueued(new Ping(), delayFrames: 1);
            commands.SendQueued(new Ping(), delayFrames: 1);

            commands.Tick(0.016f);                          // 到期（delay 0 域）：本帧快照 3 件都到期
            Assert.Equal(3, echo.Calls);
        }

        [Fact]
        public void SendQueued_溢出丢弃计数()
        {
            var commands = new CommandBus(maxQueued: 1);
            var echo = new EchoHandler();
            commands.Register(echo);

            commands.SendQueued(new Ping());                // 入队
            commands.SendQueued(new Ping());                // 溢出：丢弃

            var into = new Dictionary<string, string>();
            ((IModuleStats)commands).Snapshot(into);
            Assert.Equal("1", into["QueuedDropped"]);       // 丢弃可观察
        }

        [Fact]
        public void SendQueued_执行时过权限门_入队时不拦()
        {
            var commands = new CommandBus(gmEnabled: false);   // release 语义
            var gm = new GMEcho();
            commands.Register(gm, new CommandOptions(gmOnly: true));

            commands.SendQueued(new GMPing());              // 入队成功（无处理器才抛；门在执行时）
            commands.Tick(0.016f);                          // 执行 → 权限门拦截

            Assert.Equal(0, gm.Calls);                      // 物理不执行
            var into = new Dictionary<string, string>();
            ((IModuleStats)commands).Snapshot(into);
            Assert.Equal("1", into["GmBlocked"]);           // 执行时拦（"SendQueued 同样过权限门"§4）
        }

        // ---- 异步处理器（§4） ----

        [Fact]
        public void 同步异步注册互斥()
        {
            var a = new CommandBus();
            a.Register(new EchoHandler());
            Assert.Throws<InvalidOperationException>(() => a.RegisterAsync(new AsyncPingHandler()));

            var b = new CommandBus();
            b.RegisterAsync(new AsyncPingHandler());
            Assert.Throws<InvalidOperationException>(() => b.Register(new EchoHandler()));
        }

        [Fact]
        public void SendAsync_成功路径()
        {
            var commands = new CommandBus();
            var async_ = new AsyncPingHandler();
            commands.RegisterAsync(async_);

            var result = commands.SendAsync(new Ping()).GetAwaiter().GetResult();

            Assert.True(result.Ok);
            Assert.Equal(1, async_.Calls);
        }

        [Fact]
        public void Send打在异步注册上_类型错配fail_fast()
        {
            var commands = new CommandBus();
            commands.RegisterAsync(new AsyncPingHandler());
            Assert.Throws<InvalidOperationException>(() => commands.Send(new Ping()));
        }

        [Fact]
        public void SendAsync_处理器异常隔离为Failed()
        {
            var commands = new CommandBus();
            commands.RegisterAsync(new AsyncPingHandler { Throw = true });

            var result = commands.SendAsync(new Ping()).GetAwaiter().GetResult();   // 不炸调用方

            Assert.False(result.Ok);
            Assert.Equal(CommandReject.HandlerFault, result.Reason);
        }

        // ---- 撤销/重做（§4） ----

        private sealed class UndoableEcho : IUndoableCommandHandler<Ping>
        {
            public int Calls;
            public int Undos;
            public bool Reject;
            public CommandResult Execute(Ping command) { Calls++; return Reject ? CommandResult.Fail(CommandReject.HandlerRejected, "业务拒") : CommandResult.Success(); }
            public void Undo(Ping command) { Undos++; }
        }

        [Fact]
        public void 撤销重做_闭环_Undo逆序_Redo重新Execute()
        {
            var commands = new CommandBus();
            var handler = new UndoableEcho();
            commands.RegisterUndoable(handler);

            commands.Send(new Ping());
            Assert.True(commands.CanUndo);
            Assert.False(commands.CanRedo);
            Assert.Equal(1, handler.Calls);

            Assert.True(commands.Undo());
            Assert.Equal(1, handler.Undos);
            Assert.False(commands.CanUndo);
            Assert.True(commands.CanRedo);

            Assert.True(commands.Redo());                   // 重做 = 重新 Execute
            Assert.Equal(2, handler.Calls);                 // 二次执行
            Assert.Equal(1, handler.Undos);                 // 不再调 Undo
            Assert.False(commands.CanRedo);
            Assert.True(commands.CanUndo);                  // 重做后回到可撤销
    }

        [Fact]
        public void 撤销_失败命令与不可撤销命令不入栈()
        {
            var commands = new CommandBus();
            var undoable = new UndoableEcho { Reject = true };
            commands.RegisterUndoable(undoable);
            commands.Send(new Ping());                      // 失败：不入栈
            Assert.False(commands.CanUndo);

            commands.Register(new PlainEcho());             // 另一类型普通（不可撤销）
            commands.Send(new Plain());
            Assert.False(commands.CanUndo);                 // 不可撤销命令不进栈
        }

        private sealed class PlainEcho : ICommandHandler<Plain>
        {
            public CommandResult Execute(Plain command) => CommandResult.Success();
        }

        [Fact]
        public void 撤销栈_容量64_淘汰最旧()
        {
            var commands = new CommandBus();
            var handler = new UndoableEcho();
            commands.RegisterUndoable(handler);
            for (int i = 0; i < 70; i++) commands.Send(new Ping());   // 超容量：最旧 6 条被淘汰

            int undone = 0;
            while (commands.Undo()) undone++;
            Assert.Equal(64, undone);                       // 只剩 64 条可撤
            Assert.False(commands.CanUndo);
        }
    }
}
