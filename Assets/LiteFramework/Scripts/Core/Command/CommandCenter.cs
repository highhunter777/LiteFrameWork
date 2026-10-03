using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LiteFramework
{
    /// <summary>
    /// 命令中心（《命令中心专项设计》§2/§3/§6）：类型化意图请求的注册-执行-结果链路。
    /// 执行序：权限门（GmOnly 且非三宏环境 → GmBlocked 结果不抛）→ 处理器 Execute。
    /// 无处理器 = 编程错误直接抛（GM 面板从注册清单发现命令，不会踩到）；
    /// 处理器异常 = 隔离为 Failed 结果（记日志 + 审计留痕，不炸调用方）。
    /// 命令类型唯一处理器（重复注册抛）；命令对象生命周期归调用方（中心不持有引用）。
    ///
    /// gmEnabled（构造注入——Core 不含宏判定的可测接缝）：三宏之内传 true、release 由装配方按宏传入 false。
    /// 审计环 32 条无条件记录（release 排障同样需要"刚才发了什么命令"）；读取面 Debug 三宏（§6）。
    /// 主线程 only（框架约定）；零引擎依赖（Core/Command，与 Pool/Event 同层）。
    /// </summary>
    public sealed class CommandCenter : ICommandCenter, IModuleStats, ITickable
    {
        private sealed class Registration
        {
            public object Handler;          // ICommandHandler<TCommand> / IAsyncCommandHandler<TCommand>（同键互斥）
            public CommandOptions Options;
            public long RegisteredAt;       // 注册时序（发现面排序）
            public bool Undoable;           // opt-in 撤销能力（仅同步 Send 成功进历史栈）
        }

        private readonly Dictionary<Type, Registration> _handlers = new Dictionary<Type, Registration>(16);
        private readonly bool _gmEnabled;
        private long _registeredSeq;
        private long _sent;
        private long _gmBlocked;
        private long _failed;

        // ---- 批3 高级执行形态（§4）----

        private readonly Queue<PendingCommand> _queue = new Queue<PendingCommand>(16);   // 排队/延迟（Tick 驱动）
        private readonly Dictionary<Type, Func<object, CommandResult>> _adapters = new Dictionary<Type, Func<object, CommandResult>>(16);   // 每类型一次闭包（恢复强类型）
        private readonly Dictionary<Type, Action<object>> _undoAdapters = new Dictionary<Type, Action<object>>(8);   // 可撤销命令的 Undo 闭包
        public int MaxQueued { get; set; } = 256;           // 排队上限（溢出丢弃与事件中心同口径）
        private long _tickCount;
        private long _queuedDropped;
        private readonly List<ICommandInterceptor> _interceptors = new List<ICommandInterceptor>(2);   // 登记序
        private readonly List<UndoEntry> _undo = new List<UndoEntry>(64);    // 历史栈（队尾=最新；容量 64 淘汰最旧）
        private readonly Stack<UndoEntry> _redo = new Stack<UndoEntry>(16);  // 重做栈

        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;


        // 审计环：32 条预分配（无分配记录——字符串 Detail 为引用赋值，非分配）
        private readonly CommandAuditEntry[] _audit = new CommandAuditEntry[AuditCapacity];
        private int _auditNext;             // 环写指针
        private long _auditSeq;
        private const int AuditCapacity = 32;

        public CommandCenter(bool gmEnabled = true)
        {
            _gmEnabled = gmEnabled;
        }

        public IDisposable Register<TCommand>(ICommandHandler<TCommand> handler, CommandOptions options = null) where TCommand : ICommand
            => RegisterCore<TCommand>(handler, options, undoable: false);

        /// <summary>注册可撤销处理器（§4 opt-in）：仅同步 Send 成功进历史栈；入栈即移交撤销所有权。</summary>
        public IDisposable RegisterUndoable<TCommand>(IUndoableCommandHandler<TCommand> handler, CommandOptions options = null) where TCommand : ICommand
            => RegisterCore<TCommand>(handler, options, undoable: true);

        /// <summary>注册异步处理器（§4）：与同步注册同键互斥（重复注册抛同契约）。</summary>
        public IDisposable RegisterAsync<TCommand>(IAsyncCommandHandler<TCommand> handler, CommandOptions options = null) where TCommand : ICommand
            => RegisterCore<TCommand>(handler, options, undoable: false);

        private IDisposable RegisterCore<TCommand>(object handler, CommandOptions options, bool undoable) where TCommand : ICommand
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            var type = typeof(TCommand);
            if (_handlers.ContainsKey(type))
                throw new InvalidOperationException($"命令 {type.Name} 已有处理器（唯一处理器契约——替换先注销返回的 IDisposable）");
            _handlers[type] = new Registration { Handler = handler, Options = options ?? new CommandOptions(), RegisteredAt = _registeredSeq++, Undoable = undoable };
            _adapters[type] = obj => Send((TCommand)obj);   // 每类型一次闭包（注册期定型——Send/排队/重做共用同一核心）
            if (undoable)
            {
                var undoableHandler = (IUndoableCommandHandler<TCommand>)handler;
                _undoAdapters[type] = obj => undoableHandler.Undo((TCommand)obj);   // 每类型一次闭包（低频注册）
            }
            return new Unsubscription(this, type);
        }

        public bool HasHandler<TCommand>() where TCommand : ICommand
            => _handlers.ContainsKey(typeof(TCommand));

        public CommandResult Send<TCommand>(TCommand command) where TCommand : ICommand
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            var type = typeof(TCommand);
            if (!_handlers.TryGetValue(type, out var reg))
                throw new InvalidOperationException($"命令 {type.Name} 无处理器（编程错误——注册清单见发现面）");
            if (reg.Handler is not ICommandHandler<TCommand>)
                throw new InvalidOperationException($"命令 {type.Name} 未注册同步处理器（同步/异步互斥——SendAsync 走异步路径）");

            _sent++;
            // 权限门（§3）：GM 能力在三宏之外物理隔离——运行态拒绝，不执行不抛
            if (reg.Options.GmOnly && !_gmEnabled)
            {
                _gmBlocked++;
                RecordAudit(type, false, CommandReject.GmBlocked, "GM 命令被权限门拦截（release）");
                return CommandResult.Fail(CommandReject.GmBlocked, "GM 命令在当前构建不可用");
            }

            if (!RunCommandInterceptors(type, command))
            {
                RecordAudit(type, false, CommandReject.InterceptorRejected, "被拦截器拒绝");
                return CommandResult.Fail(CommandReject.InterceptorRejected, "被拦截器拒绝（§4 执行序：门→拦截器→处理器）");
            }

            var handler = (ICommandHandler<TCommand>)reg.Handler;
            CommandResult result;
            try
            {
                result = handler.Execute(command);
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Command.{type.Name}");      // 处理器作者代码缺陷：隔离为 Failed,不炸调用方
                _failed++;
                RecordAudit(type, false, CommandReject.HandlerFault, ex.Message);
                return CommandResult.Fail(CommandReject.HandlerFault, ex.Message);
            }

            if (!result.Ok)
            {
                _failed++;
                // 处理器未给 Reason 时归一为 HandlerRejected（审计归因面完整）
                if (result.Reason == CommandReject.None)
                    result = CommandResult.Fail(CommandReject.HandlerRejected, result.Detail);
                RecordAudit(type, false, result.Reason, result.Detail);
                return result;
            }

            if (reg.Undoable)
            {
                // 撤销历史（§4 opt-in）：仅同步 Send 成功进栈；入栈即移交撤销所有权；重做线失效（标准撤销语义）
                _redo.Clear();
                _undo.Add(new UndoEntry(type, command));
                while (_undo.Count > 64) _undo.RemoveAt(0);
            }
            RecordAudit(type, true, CommandReject.None, result.Detail);
            return result;
        }

        /// <summary>命令拦截器链（§4 登记序）：任一拒绝即终止 → InterceptorRejected 短路；
        /// 拦截器异常 = 记日志跳过继续（与事件拦截 fail-open 同口径——界定登记在注释）。</summary>
        private bool RunCommandInterceptors(Type commandType, object command)
        {
            for (int i = 0; i < _interceptors.Count; i++)
            {
                bool pass;
                try { pass = _interceptors[i].Intercept(commandType, command); }
                catch (Exception ex)
                {
                    Log.Error(ex, $"Command.Interceptor.{_interceptors[i].GetType().Name}");
                    continue;
                }
                if (!pass) return false;
            }
            return true;
        }

        // ---- 批3 高级执行形态方法（§4）----

        /// <summary>排队执行（§4 fire-and-forget）：入队，Tick 驱动到期按入队序执行——无返回结果，
        /// 失败走审计与日志；命令实现 IReference 则执行完由中心回收（入队即移交）。
        /// 执行时同样过权限门与拦截器链（经 Send 同一核心）；无处理器=编程错误，入队时早暴露。</summary>
        public void SendQueued<TCommand>(TCommand command, int delayFrames = 0) where TCommand : ICommand
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (delayFrames < 0) delayFrames = 0;
            if (!_handlers.ContainsKey(typeof(TCommand)))
                throw new InvalidOperationException($"命令 {typeof(TCommand).Name} 无处理器（编程错误——入队时早暴露）");

            if (_queue.Count >= MaxQueued)
            {
                _queuedDropped++;
                Log.Error($"命令队列溢出丢弃（MaxQueued={MaxQueued}）: {typeof(TCommand).Name}", "Command");
                RecycleCommand(command);                    // 丢弃同样回收池化命令
                return;
            }
            _queue.Enqueue(new PendingCommand { CommandType = typeof(TCommand), Command = command, DueTick = _tickCount + delayFrames });
        }

        /// <summary>帧驱动（排队/延迟命令到期派发）。注册即发现 ITickable 自动驱动；用于要排队的场景。</summary>
        public void Tick(float realDelta)
        {
            _tickCount++;
            int n = _queue.Count;                           // 快照量:执行期间新入队留到下帧,防同帧级联
            for (int i = 0; i < n; i++)
            {
                var pc = _queue.Dequeue();
                if (pc.DueTick > _tickCount) { _queue.Enqueue(pc); continue; }   // 未到期：留队相对序保持
                try
                {
                    if (_adapters.TryGetValue(pc.CommandType, out var send)) send(pc.Command);
                }
                catch (Exception ex) { Log.Error(ex, $"Command.Tick.{pc.CommandType.Name}"); }   // 中途被注销等边缘:隔离不炸泵
                RecycleCommand(pc.Command);                 // fire-and-forget:执行完回收（含失败/拦截路径）
            }
        }

        /// <summary>异步执行（§4）：无处理器/同步异步类型错配 = 编程错误直接抛；
        /// 处理器异常隔离同 Send；OperationCanceledException 透传（取消语义归调用方）。</summary>
        public async ValueTask<CommandResult> SendAsync<TCommand>(TCommand command, CancellationToken ct = default) where TCommand : ICommand
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            var type = typeof(TCommand);
            if (!_handlers.TryGetValue(type, out var reg))
                throw new InvalidOperationException($"命令 {type.Name} 无处理器（编程错误）");
            if (reg.Handler is not IAsyncCommandHandler<TCommand>)
                throw new InvalidOperationException($"命令 {type.Name} 未注册异步处理器（同步/异步互斥——发现面查注册形态）");

            _sent++;
            if (reg.Options.GmOnly && !_gmEnabled)
            {
                _gmBlocked++;
                RecordAudit(type, false, CommandReject.GmBlocked, "GM 命令被权限门拦截（release）");
                return CommandResult.Fail(CommandReject.GmBlocked, "GM 命令在当前构建不可用");
            }
            if (!RunCommandInterceptors(type, command))
            {
                RecordAudit(type, false, CommandReject.InterceptorRejected, "被拦截器拒绝");
                return CommandResult.Fail(CommandReject.InterceptorRejected, "被拦截器拒绝");
            }

            var handler = (IAsyncCommandHandler<TCommand>)reg.Handler;
            CommandResult result;
            try
            {
                result = await handler.ExecuteAsync(command, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Error(ex, $"Command.{type.Name}");      // 处理器作者代码缺陷：隔离同 Send；取消透传（OCE 不进此守卫）
                _failed++;
                RecordAudit(type, false, CommandReject.HandlerFault, ex.Message);
                return CommandResult.Fail(CommandReject.HandlerFault, ex.Message);
            }

            if (!result.Ok)
            {
                _failed++;
                if (result.Reason == CommandReject.None)
                    result = CommandResult.Fail(CommandReject.HandlerRejected, result.Detail);
                RecordAudit(type, false, result.Reason, result.Detail);
                return result;
            }
            RecordAudit(type, true, CommandReject.None, result.Detail);
            return result;
        }

        /// <summary>命令拦截器注册（§4 登记序；返回注销委托幂等）。</summary>
        public IDisposable RegisterInterceptor(ICommandInterceptor interceptor)
        {
            if (interceptor == null) throw new ArgumentNullException(nameof(interceptor));
            _interceptors.Add(interceptor);
            return new CommandInterceptorUnsubscription(_interceptors, interceptor);
        }

        /// <summary>撤销（§4）：弹出最新一条成功命令执行 Undo；Undo 异常=隔离（半途即弃不回塞——防死循环）；成功转入重做栈。</summary>
        public bool Undo()
        {
            if (_undo.Count == 0) return false;
            var entry = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            if (!_undoAdapters.TryGetValue(entry.CommandType, out var undo))
            {
                Log.Error($"撤销失败：{entry.CommandType.Name} 处理器已注销", "Command");
                RecordAudit(entry.CommandType, false, CommandReject.HandlerFault, "撤销时处理器已注销");
                return false;                                // 前进语义：该条已弹出不回塞
            }
            try
            {
                undo(entry.Command);
                _redo.Push(entry);
                RecordAudit(entry.CommandType, true, CommandReject.None, "Undo");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Command.Undo.{entry.CommandType.Name}");
                RecordAudit(entry.CommandType, false, CommandReject.HandlerFault, ex.Message);
                return false;                                // 不回塞——防撤销死循环
            }
        }

        /// <summary>重做（§4）：弹出重做线顶经 Send 同一核心重新 Execute——Ok 则入撤销栈（Send 内驱动）、审计照记。</summary>
        public bool Redo()
        {
            if (_redo.Count == 0) return false;
            var entry = _redo.Pop();
            try
            {
                if (!_adapters.TryGetValue(entry.CommandType, out var send))
                {
                    Log.Error($"重做失败：{entry.CommandType.Name} 处理器已注销", "Command");
                    return false;
                }
                return send(entry.Command).Ok;              // Send 驱动审计/统计/撤销入栈全套
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Command.Redo.{entry.CommandType.Name}");
                return false;
            }
        }

        private static void RecycleCommand(object command)
        {
            if (command is IReference pooled) ReferencePool.Release(pooled);   // 排队命令入队即移交：执行完（含失败/拦截/溢出）回收
        }

        private sealed class CommandInterceptorUnsubscription : IDisposable
        {
            private readonly List<ICommandInterceptor> _list;
            private readonly ICommandInterceptor _item;
            public CommandInterceptorUnsubscription(List<ICommandInterceptor> list, ICommandInterceptor item) { _list = list; _item = item; }
            public void Dispose() => _list.Remove(_item);    // 幂等
        }

        private struct PendingCommand
        {
            public Type CommandType;
            public object Command;
            public long DueTick;                            // 到期帧（Tick 计数域）
        }

        private struct UndoEntry
        {
            public Type CommandType;
            public object Command;
            public UndoEntry(Type commandType, object command) { CommandType = commandType; Command = command; }
        }


        private void RecordAudit(Type commandType, bool ok, CommandReject reason, string detail)
        {
            _audit[_auditNext] = new CommandAuditEntry { Seq = ++_auditSeq, CommandType = commandType, Ok = ok, Reason = reason, Detail = detail };
            _auditNext = (_auditNext + 1) % AuditCapacity;
        }

        // ---- 读取面（§6：Debug 三宏——HUD/GM 面板同生命周期）----

#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
        /// <summary>注册清单发现面：GM 面板据此列出可用命令（GmOnly/描述/注册时序），不硬编码命令清单。</summary>
        public IReadOnlyList<CommandInfo> GetRegisteredInfos()
        {
            var list = new List<CommandInfo>(_handlers.Count);
            foreach (var kv in _handlers)
                list.Add(new CommandInfo(kv.Key, kv.Value.Options.GmOnly, kv.Value.Options.Description, kv.Value.RegisteredAt));
            return list;
        }

        /// <summary>审计读取面：最近 32 条，旧→新序；低频轮询分配可接受。</summary>
        public IReadOnlyList<CommandAuditEntry> GetAuditLog()
        {
            int count = (int)Math.Min(_auditSeq, AuditCapacity);
            var list = new List<CommandAuditEntry>(count);
            int start = (_auditNext - count + AuditCapacity) % AuditCapacity;
            for (int i = 0; i < count; i++)
                list.Add(_audit[(start + i) % AuditCapacity]);
            return list;
        }
#endif

        private sealed class Unsubscription : IDisposable
        {
            private readonly CommandCenter _center;
            private readonly Type _type;
            public Unsubscription(CommandCenter center, Type type) { _center = center; _type = type; }
            public void Dispose()
            {
                _center._handlers.Remove(_type);            // 幂等（二次注销 no-op）；注销后方可重新注册
            }
        }

        public string StatsName => "CommandCenter";

        /// <summary>契约：实现负责 into.Clear() 再填入（IModuleStats 同族口径）。</summary>
        public void Snapshot(Dictionary<string, string> into)
        {
            into.Clear();
            into["Registered"] = _handlers.Count.ToString();
            into["Sent"] = _sent.ToString();
            into["GmBlocked"] = _gmBlocked.ToString();
            into["Failed"] = _failed.ToString();
            into["QueuedDropped"] = _queuedDropped.ToString();          // 批3 增量键（溢出可观察）
        }
    }

    /// <summary>注册清单条目（发现面——GM 面板列出可用命令）。</summary>
    public readonly struct CommandInfo
    {
        public readonly Type CommandType;
        public readonly bool GmOnly;
        public readonly string Description;
        public readonly long RegisteredAt;    // 注册时序

        public CommandInfo(Type commandType, bool gmOnly, string description, long registeredAt)
        {
            CommandType = commandType; GmOnly = gmOnly; Description = description; RegisteredAt = registeredAt;
        }
    }

    /// <summary>审计条目（§6：命令类型/时序序号/结果/归因——release 排障的"刚才发了什么"）。</summary>
    public struct CommandAuditEntry
    {
        public long Seq;                      // 时序序号（单调）
        public Type CommandType;
        public bool Ok;
        public CommandReject Reason;
        public string Detail;
    }
}
