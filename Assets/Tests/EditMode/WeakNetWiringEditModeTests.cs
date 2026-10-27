using System;
using System.Collections.Generic;
using System.Reflection;
using LiteFramework;
using LiteNet.Diagnostics;
using LiteNet.Transport;
using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>弱网注入装配/写口/快照/曲线/静态状态守卫。</summary>
    public class WeakNetWiringEditModeTests
    {
        private sealed class FakeTransport : IClientTransport
        {
            public readonly List<(byte[] Data, bool Reliable)> Sent = new List<(byte[], bool)>();
            public bool Connected { get; private set; }
            public event Action OnConnected;
            public event Action<ArraySegment<byte>, bool> OnData;
            public event Action OnDisconnected;

            public void Connect(string address, int port) { Connected = true; OnConnected?.Invoke(); }
            public void Disconnect() { Connected = false; OnDisconnected?.Invoke(); }
            public void TickIncoming() { }
            public void TickOutgoing() { }
            public void Send(ArraySegment<byte> data, bool reliable)
            {
                byte[] copy = new byte[data.Count];
                Array.Copy(data.Array, data.Offset, copy, 0, data.Count);
                Sent.Add((copy, reliable));
            }
            public void Dispose() => Disconnect();
        }

        [Test]
        public void WrapFactory_LeavesTestRoomUntouched()
        {
            var runtime = new WeakNetRuntime(() => 0);
            var transport = new FakeTransport();
            Func<bool, MatchTransport> factory = _ => new MatchTransport(transport, null);
            Func<bool, MatchTransport> wrapped = WeakNetWiring.WrapFactory(factory, runtime);
            Assert.AreSame(transport, wrapped(true).Transport);
        }

        [Test]
        public void WrapFactory_WrapsRealRoom_AndDisabledIsPassthrough()
        {
            var runtime = new WeakNetRuntime(() => 0);
            var inner = new FakeTransport();
            Func<bool, MatchTransport> factory = _ => new MatchTransport(inner, null);
            IClientTransport wrapped = WeakNetWiring.WrapFactory(factory, runtime)(false).Transport;
            Assert.IsInstanceOf<WeakNetClientTransport>(wrapped);
            wrapped.Send(new ArraySegment<byte>(new byte[] { 1, 2 }), true);
            Assert.AreEqual(1, inner.Sent.Count);
        }

        [Test]
        public void PresetCommand_IsAudited_AndUpdatesSnapshot()
        {
            var runtime = new WeakNetRuntime(() => 0);
            var commands = new CommandCenter(new ReferencePoolService(), true);
            using (commands.Register(new SetWeakNetPresetHandler(runtime), new CommandOptions(true, "弱网预设")))
            {
                var kernel = new DebugKernel(commands, null, new IDebugTunableProvider[] { runtime }, runtime);
                runtime.Wrap(new FakeTransport());
                Assert.IsTrue(kernel.TrySetWeakNetPreset("mobile", out string error), error);
                Assert.IsTrue(kernel.WeakNet.Enabled);
                Assert.AreEqual("mobile", kernel.WeakNet.Preset);
                Assert.Greater(commands.GetAuditLog().Count, 0);
            }
        }

        [Test]
        public void Snapshot_AppendLines_ContainsPresetAndCounters()
        {
            var runtime = new WeakNetRuntime(() => 0);
            runtime.TrySetPreset("lossy", out _);
            runtime.Wrap(new FakeTransport());
            var sb = new System.Text.StringBuilder();
            runtime.TakeSnapshot().AppendLines(sb);
            string text = sb.ToString();
            StringAssert.Contains("注入", text);
            StringAssert.Contains("lossy", text);
            StringAssert.Contains("seed=", text);
        }

        [Test]
        public void Snapshot_IsHiddenWithoutActiveTransport()
        {
            var runtime = new WeakNetRuntime(() => 0);
            runtime.TrySetPreset("lan", out _);
            Assert.IsFalse(runtime.TakeSnapshot().Enabled);
        }

        [Test]
        public void TestRoom_DetachesPreviousActiveTransport()
        {
            var runtime = new WeakNetRuntime(() => 0);
            var fake = new FakeTransport();
            runtime.TrySetPreset("lan", out _);
            runtime.Wrap(fake);
            Assert.IsTrue(runtime.TakeSnapshot().Enabled);
            Func<bool, MatchTransport> factory = _ => new MatchTransport(new FakeTransport(), null);
            WeakNetWiring.WrapFactory(factory, runtime)(true);
            Assert.IsFalse(runtime.TakeSnapshot().Enabled);
        }

        [Test]
        public void NetHistory_Uses4HzWindow_AndReportsRealSeconds()
        {
            var history = new NetHistory();
            for (int i = 0; i < 240; i++)
            {
                Assert.IsTrue(history.Sample(i * NetHistory.SampleIntervalMs, 40 + i % 5, 10));
            }
            Assert.AreEqual(240, history.Count);
            Assert.IsTrue(history.TryFormat(40, out string rttLine, out string lossLine));
            StringAssert.Contains("RTT 60s", rttLine);
            StringAssert.Contains("丢包 60s", lossLine);
        }

        [Test]
        public void NetHistory_ThrottlesTo4Hz()
        {
            var history = new NetHistory();
            Assert.IsTrue(history.Sample(0, 10, 0));
            Assert.IsFalse(history.Sample(100, 10, 0));
            Assert.IsTrue(history.Sample(250, 10, 0));
            Assert.AreEqual(2, history.Count);
        }

        [Test]
        public void WeakNetTypes_HaveNoPublicStaticMutableState()
        {
            Type[] types =
            {
                typeof(WeakNetRuntime), typeof(WeakNetPresets), typeof(WeakNetSnapshot),
                typeof(NetHistory), typeof(WeakNetClientTransport), typeof(WeakNetRandom),
            };
            foreach (Type type in types)
            {
                foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    Assert.IsTrue(field.IsInitOnly || field.IsLiteral,
                        $"{type.Name}.{field.Name} 是 public static 可变状态");
                }
            }
        }
    }
}
