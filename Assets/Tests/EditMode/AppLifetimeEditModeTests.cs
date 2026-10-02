using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteTesting;
using LiteTesting.Unity;
using NUnit.Framework;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// AppLifetime 桥用例（《商业级通用客户端框架总设计》§6.1 ClientHost 责任
    /// "处理 OnApplicationPause/Focus/Quit、低内存……关闭前刷新设置、存档、遥测"）：
    /// Unity 平台消息经 AppLifetime 转发 ClientHost；OnApplicationQuit 触发
    /// "QuitIntent 收集 → 刷新钩子 → 逆序 ShutdownAsync"的优雅退出序列。
    /// 假模块同步完成 → 桥内有界等待立即返回；挂死/异步善后的超时放行归平台宿主语义。
    /// </summary>
    public sealed class AppLifetimeEditModeTests : UnityTestBase
    {
        private sealed class FakeModule : IClientModule
        {
            private readonly List<string> _trace;
            public string Name { get; }
            public FakeModule(string name, List<string> trace) { Name = name; _trace = trace; }
            public UniTask InitializeAsync(ClientContext context, CancellationToken ct)
            {
                _trace.Add("init:" + Name);
                return UniTask.CompletedTask;
            }
            public UniTask ShutdownAsync(CancellationToken ct)
            {
                _trace.Add("shutdown:" + Name);
                return UniTask.CompletedTask;
            }
        }

        private GameObject _go;
        private AppLifetime _lifetime;
        private ClientHost _host;
        private List<string> _trace;

        [SetUp]
        protected void SetUp()
        {
            _trace = new List<string>();
            _host = new ClientHost();
            _host.AddModule(new FakeModule("A", _trace));
            _host.AddModule(new FakeModule("B", _trace));
            _go = new GameObject("AppLifetimeTest");
            _lifetime = _go.AddComponent<AppLifetime>();
            _lifetime.Bind(_host);
        }

        [TearDown]
        protected void TearDown()
        {
            if (_go != null) UnityEngine.Object.DestroyImmediate(_go);
        }

        private void Send(string message, object arg = null)
        {
            // 编辑态 SendMessage 不派发引擎消息（ShouldRunBehaviour 断言）——生命周期转发改直调 public 方法
            switch (message)
            {
                case "OnApplicationPause": _lifetime.OnApplicationPause((bool)arg); break;
                case "OnApplicationFocus": _lifetime.OnApplicationFocus((bool)arg); break;
                case "OnLowMemory": _lifetime.OnLowMemory(); break;
                case "OnApplicationQuit": _lifetime.OnApplicationQuit(); break;
                default: throw new ArgumentException("未知消息 " + message);
            }
        }

        [Test]
        public void 平台消息_Pause_Focus_LowMemory_转发ClientHost订阅者()
        {
            int pauseCalls = 0;
            bool lastPause = true;
            int focusCalls = 0;
            int lowMemoryCalls = 0;

            _host.SubscribePause(v => { pauseCalls++; lastPause = v; });
            _host.SubscribeFocus(_ => focusCalls++);
            _host.SubscribeLowMemory(() => lowMemoryCalls++);

            Send("OnApplicationPause", true);
            Send("OnApplicationPause", false);
            Send("OnApplicationFocus", false);
            Send("OnLowMemory");

            Assert.AreEqual(2, pauseCalls);
            Assert.IsFalse(lastPause);
            Assert.AreEqual(1, focusCalls);
            Assert.AreEqual(1, lowMemoryCalls);
        }

        [Test]
        public void 退出_触发优雅关闭序列_刷新钩子先于模块逆序关闭()
        {
            _host.AddPreShutdownFlush("settings", ct => { _trace.Add("flush:settings"); return UniTask.CompletedTask; });

            Pump(_host.InitializeAsync());
            _trace.Clear();
            Send("OnApplicationQuit");

            // 优雅序列：QuitIntent（无订阅者）→ 刷新钩子 → 逆序模块关闭（桥内有界等待同步完成）
            CollectionAssert.AreEqual(new[] { "flush:settings", "shutdown:B", "shutdown:A" }, _trace.ToArray());
        }

        [Test]
        public void 未绑定Host_平台消息静默不抛()
        {
            var bare = new GameObject("BareLifetime").AddComponent<AppLifetime>();   // 未 Bind
            try
            {
                bare.OnApplicationPause(true);
                bare.OnApplicationQuit();
                Assert.Pass("未绑定时不抛出");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(bare.gameObject);
            }
        }

        private static void Pump(UniTask task)
        {
            var awaiter = task.GetAwaiter();
            while (!awaiter.IsCompleted) { }
            awaiter.GetResult();
        }
    }
}
