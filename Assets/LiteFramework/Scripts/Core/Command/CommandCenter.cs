using System;
using System.Collections.Generic;

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
    public sealed class CommandCenter : ICommandCenter, IModuleStats
    {
        private sealed class Registration
        {
            public object Handler;          // ICommandHandler<TCommand>（批3 扩异步/可撤销处理器）
            public CommandOptions Options;
            public long RegisteredAt;       // 注册时序（发现面排序）
        }

        private readonly Dictionary<Type, Registration> _handlers = new Dictionary<Type, Registration>(16);
        private readonly bool _gmEnabled;
        private long _registeredSeq;
        private long _sent;
        private long _gmBlocked;
        private long _failed;

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
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            var type = typeof(TCommand);
            if (_handlers.ContainsKey(type))
                throw new InvalidOperationException($"命令 {type.Name} 已有处理器（唯一处理器契约——替换先注销返回的 IDisposable）");
            _handlers[type] = new Registration { Handler = handler, Options = options ?? new CommandOptions(), RegisteredAt = _registeredSeq++ };
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

            _sent++;
            // 权限门（§3）：GM 能力在三宏之外物理隔离——运行态拒绝，不执行不抛
            if (reg.Options.GmOnly && !_gmEnabled)
            {
                _gmBlocked++;
                RecordAudit(type, false, CommandReject.GmBlocked, "GM 命令被权限门拦截（release）");
                return CommandResult.Fail(CommandReject.GmBlocked, "GM 命令在当前构建不可用");
            }

            var handler = (ICommandHandler<TCommand>)reg.Handler;
            CommandResult result;
            try
            {
                result = handler.Execute(command);
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Command.{type.Name}");      // 处理器作者代码缺陷：隔离为 Failed，不炸调用方
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

            RecordAudit(type, true, CommandReject.None, result.Detail);
            return result;
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
