using System;

namespace LiteFramework
{
    /// <summary>框架基础设施的构造选项（装配段只决定"开不开/多吵"，不手拼构造参数）。</summary>
    public readonly struct FrameworkInfrastructureOptions
    {
        /// <summary>开发形态：引用池检测重复归还、日志下限更低。</summary>
        public readonly bool IsDevelopment;

        /// <summary>日志最低级别；null = 按形态默认（开发 Info / 发布 Warning）。</summary>
        public readonly LogLevel? MinimumLevel;

        /// <summary>是否安装结构化日志（`persistentDataPath/logs/*.jsonl`）。</summary>
        public readonly bool InstallStructuredLog;

        public FrameworkInfrastructureOptions(bool isDevelopment, LogLevel? minimumLevel, bool installStructuredLog)
        {
            IsDevelopment = isDevelopment;
            MinimumLevel = minimumLevel;
            InstallStructuredLog = installStructuredLog;
        }

        /// <summary>开发三宏形态的默认选项（开发：Info + 结构化日志；发布：Warning + 不落盘）。</summary>
        public static FrameworkInfrastructureOptions FromDevBuild()
            => new FrameworkInfrastructureOptions(DevBuildFacts.IsDevelopment, null, DevBuildFacts.IsDevelopment);
    }

    /// <summary>
    /// 平台基础设施装配入口：把"池 → 文件 → JSON 编解码 → 日志安装"的**构造顺序**收回框架，
    /// 装配段只调一次 <see cref="Create"/> 并登记产物（不再 `new ReferencePoolService(...)` 手拼）。
    ///
    /// 为什么需要：顺序本身是契约（`Log.Install` → `SetHelper` → 条件结构化日志；`FileSystem` 需要
    /// 路径提供者 + JSON 序列化器），散在装配段里等于要求每个装配点背下同一串构造细节。
    ///
    /// 所有权：本对象持有池/日志的关闭责任（`ILog`/`IReferencePool` 为进程级常驻），
    /// `Files`/`Json` 由装配段按需 `Scope.Register`。
    /// </summary>
    public sealed class FrameworkInfrastructure : IDisposable
    {
        public IReferencePool References { get; }

        public IFileStore Files { get; }

        public IJsonCodec Json { get; }

        public ILog Logger { get; }

        public IMainThreadDispatcher Dispatcher { get; }

        public CommandCenter Commands { get; }

        private bool _disposed;

        private FrameworkInfrastructure(IReferencePool references, IFileStore files, IJsonCodec json,
            ILog logger, IMainThreadDispatcher dispatcher, CommandCenter commands)
        {
            References = references;
            Files = files;
            Json = json;
            Logger = logger;
            Dispatcher = dispatcher;
            Commands = commands;
        }

        /// <summary>按固定顺序构造并安装（顺序即契约；幂等之外的失败直接抛给引导层）。</summary>
        public static FrameworkInfrastructure Create(FrameworkInfrastructureOptions options)
        {
            var references = new ReferencePoolService(trackUsage: options.IsDevelopment);

            // 文件端口：路径提供者 + JSON 序列化器是它的构造参数（序列化器刻意不进容器——
            // 唯一消费者是本构造函数，注册进容器却没有容器内消费者会违反"注册必须有生产消费者"）。
            var files = new FileSystem(new UnityPathProvider(), new NewtonsoftJsonSerializer());

            // 网络契约 JSON 端口（严格类别读取 + 字面量拼装）：业务只认 IJsonCodec。
            var json = new NewtonsoftJsonCodec();

            var level = options.MinimumLevel ?? (options.IsDevelopment ? LogLevel.Info : LogLevel.Warning);
            var logger = new LogService(level);
            Log.Install(logger);
            Log.SetHelper(new UnityLogHelper());
            if (options.InstallStructuredLog) StructuredLogInstaller.Install();

            var dispatcher = new MainThreadDispatcher();
            var commands = new CommandCenter(references, gmEnabled: options.IsDevelopment);

            return new FrameworkInfrastructure(references, files, json, logger, dispatcher, commands);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            (References as IDisposable)?.Dispose();   // 池的实现可释放（接口只约束取还，不绑生命周期）
        }
    }
}
