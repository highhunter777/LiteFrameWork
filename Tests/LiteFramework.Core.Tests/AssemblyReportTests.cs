using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using Xunit;

namespace LiteFramework.Tests
{
    /// <summary>
    /// 装配报告与拓扑排序用例：初始化序＝依赖深度分层（同层按注册序），报告记录提供段/消费段。
    /// </summary>
    public sealed class AssemblyReportTests
    {
        private interface IProduct { }
        private interface IMissing { }
        private sealed class Product : IProduct { }
        private sealed class Marker { }

        private sealed class Segment : IClientSegment, IClientSegmentGraph
        {
            private readonly List<string> _trace;
            private readonly Action<SegmentContext> _initialize;

            public Segment(string name, List<string> trace, Action<SegmentContext> initialize, params string[] dependsOn)
            {
                Name = name;
                _trace = trace;
                _initialize = initialize;
                DependsOn = dependsOn;
            }

            public string Name { get; }
            public IReadOnlyList<string> DependsOn { get; }

            public UniTask InitializeAsync(SegmentContext context, CancellationToken ct)
            {
                _trace.Add("init:" + Name);
                _initialize?.Invoke(context);
                return UniTask.CompletedTask;
            }

            public UniTask ShutdownAsync(CancellationToken ct)
            {
                _trace.Add("shutdown:" + Name);
                return UniTask.CompletedTask;
            }
        }

        private static void Pump(UniTask task)
        {
            var awaiter = task.GetAwaiter();
            while (!awaiter.IsCompleted) { }
            awaiter.GetResult();
        }

        [Fact]
        public void 初始化序按依赖深度分层_同层保持注册序()
        {
            var trace = new List<string>();
            var host = new ClientHost();
            host.AddSegment(new Segment("C", trace, null, "A"));
            host.AddSegment(new Segment("A", trace, null));
            host.AddSegment(new Segment("B", trace, null));

            Pump(host.InitializeAsync());

            Assert.Equal(new[] { "init:A", "init:B", "init:C" }, trace.ToArray());
            Assert.Equal(new[] { "A", "B", "C" }, host.AssemblyReport.SegmentOrder.ToArray());
        }

        [Fact]
        public void 关闭按初始化序逆序执行()
        {
            var trace = new List<string>();
            var host = new ClientHost();
            host.AddSegment(new Segment("A", trace, null));
            host.AddSegment(new Segment("B", trace, null, "A"));

            Pump(host.InitializeAsync());
            Pump(host.ShutdownAsync());

            Assert.Equal(new[] { "init:A", "init:B", "shutdown:B", "shutdown:A" }, trace.ToArray());
        }

        [Fact]
        public void 报告记录产物提供段与消费段()
        {
            var trace = new List<string>();
            var product = new Product();
            var host = new ClientHost();
            host.AddSegment(new Segment("Producer", trace, context => context.Put<IProduct>(product)));
            host.AddSegment(new Segment("Consumer", trace, context => context.Require<IProduct>(), "Producer"));

            Pump(host.InitializeAsync());

            AssemblyProduct entry = host.AssemblyReport.Products.Single(p => p.Product == typeof(IProduct));
            Assert.Equal("Producer", entry.Provider);
            Assert.Contains("Consumer", entry.Consumers);
            Assert.Empty(host.AssemblyReport.Unresolved);
        }

        [Fact]
        public void 容器直注册形态同样归属当前段()
        {
            var trace = new List<string>();
            var host = new ClientHost();
            host.AddSegment(new Segment("Registered", trace, context => context.RegisterInstance(new Marker())));

            Pump(host.InitializeAsync());

            AssemblyProduct entry = host.AssemblyReport.Products.Single(p => p.Product == typeof(Marker));
            Assert.Equal("Registered", entry.Provider);
        }

        [Fact]
        public void Require缺失消息包含模块名与产物名()
        {
            var trace = new List<string>();
            var host = new ClientHost();
            host.AddSegment(new Segment("Needs", trace, context => context.Require<IMissing>()));

            var ex = Assert.Throws<InvalidOperationException>(() => Pump(host.InitializeAsync()));

            Assert.Contains("Needs", ex.Message);
            Assert.Contains("IMissing", ex.Message);
            Assert.Null(host.AssemblyReport);
        }
    }
}
