using System;
using System.Collections.Generic;
using LiteFramework;
using Xunit;

namespace LiteFramework.Tests
{
    /// <summary>
    /// 五层 Scope 树生命周期用例（《商业级通用客户端框架总设计》§6.2 作用域表 + G1 退出条件
    /// "资源/场景/UI 循环后引用和订阅回基线"）：
    /// Root/Account/Match/Scene/UI 五层建树、父取消级联全部子孙、子 Dispose 独立、
    /// 每层 LIFO、兄弟互不影响、泄漏断言（OwnedCount 归零）。
    /// </summary>
    public sealed class ClientScopeTreeTests
    {
        private sealed class Tracked : IDisposable
        {
            private readonly IList<string> _trace;
            private readonly string _name;
            public Tracked(IList<string> trace, string name) { _trace = trace; _name = name; }
            public void Dispose() => _trace.Add("release:" + _name);
        }

        /// <summary>按 §6.2 作用域表建五层树：Root → Account → Match → Scene → UI。</summary>
        private static ClientScope BuildFiveLayerTree()
        {
            var root = new ClientScope("Root");
            var account = root.CreateChild("Account");
            var match = account.CreateChild("Match");
            var scene = match.CreateChild("Scene");
            var ui = scene.CreateChild("UI");
            return root;
        }

        [Fact]
        public void 五层树_建树与父取消级联_全部子孙()
        {
            var root = new ClientScope("Root");
            var account = root.CreateChild("Account");
            var match = account.CreateChild("Match");
            var scene = match.CreateChild("Scene");
            var ui = scene.CreateChild("UI");

            Assert.False(ui.Token.IsCancellationRequested);
            root.Cancel();                                        // 根取消 → 全部子孙级联

            Assert.True(account.Token.IsCancellationRequested);
            Assert.True(match.Token.IsCancellationRequested);
            Assert.True(scene.Token.IsCancellationRequested);
            Assert.True(ui.Token.IsCancellationRequested);

            // 取消 ≠ 释放：资源仍走各自的 LIFO 释放路径
            Assert.False(root.IsDisposed);
            Assert.False(ui.IsDisposed);
        }

        [Fact]
        public void 五层树_逐层Dispose_每层LIFO_兄弟互不影响()
        {
            var trace = new List<string>();
            var root = new ClientScope("Root");
            var account = root.CreateChild("Account");
            var match = account.CreateChild("Match");
            var scene = match.CreateChild("Scene");
            var ui = scene.CreateChild("UI");

            // 每层登记一个资源（逆序验证 LIFO）
            ui.Register(new Tracked(trace, "ui.res"));
            scene.Register(new Tracked(trace, "scene.res"));
            match.Register(new Tracked(trace, "match.res"));

            ui.Dispose();                                         // 只释放 UI 层（关闭界面 → 清展示）
            Assert.Equal(new[] { "release:ui.res" }, trace.ToArray());
            Assert.False(match.IsDisposed);                       // 兄弟层不受影响

            match.Dispose();                                      // Match 层关闭（销毁战斗态）
            Assert.Equal(new[] { "release:ui.res", "release:match.res" }, trace.ToArray());

            scene.Dispose();                                      // Scene 层关闭
            Assert.Equal(new[] { "release:ui.res", "release:match.res", "release:scene.res" }, trace.ToArray());

            account.Dispose();                                    // Account/Root 无登记资源：Dispose 幂等无输出
            root.Dispose();
            Assert.Equal(3, trace.Count);                         // 每层恰好释放一次（幂等）
        }

        [Fact]
        public void 树_单层多项资源_逆序释放_单项异常隔离()
        {
            var trace = new List<string>();
            var scope = new ClientScope("layer");

            scope.Register(new Tracked(trace, "first"));
            scope.Register(new BoomDisposer());
            scope.Register(new Tracked(trace, "last"));

            scope.Dispose();
            // LIFO：last → boom（异常隔离）→ first
            Assert.Equal(new[] { "release:last", "release:first" }, trace.ToArray());
            Assert.Single(scope.DisposeFailures);
        }

        private sealed class BoomDisposer : IDisposable
        {
            public void Dispose() => throw new InvalidOperationException("boom");
        }

        [Fact]
        public void 子Dispose_不影响父级_父后续取消不影响已释放子()
        {
            var root = new ClientScope("Root");
            var account = root.CreateChild("Account");

            account.Dispose();                                    // 子先释放

            Assert.True(account.IsDisposed);
            Assert.False(root.IsDisposed);                        // 父不受子 Dispose 影响
            root.Cancel();                                        // 父取消不抛（已释放子的 CTS 已 Dispose，Cancel 幂等）
            root.Dispose();
        }

        [Fact]
        public void 泄漏断言_OwnedCount随登记与释放增减()
        {
            var scope = new ClientScope("leak-check");
            var res1 = scope.Register(new Tracked(new List<string>(), "r1"));
            scope.Register(new Tracked(new List<string>(), "r2"));

            Assert.Equal(2, scope.OwnedCount);

            res1.Dispose();                                       // 外部提前释放（绕过 Scope）
            scope.Dispose();                                      // Scope 释放：登记清零（全部已释放，无泄漏）

            Assert.Equal(0, scope.OwnedCount);                    // 泄漏断言：释放后登记数归零
        }
    }
}
