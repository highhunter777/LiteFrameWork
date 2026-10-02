using System;
using System.Collections.Generic;
using Google.Protobuf;
using LiteFramework;
using LiteNet;
using LiteNet.Protocol;
using LiteNet.Proto;
using LiteNet.Transport;
using LiteSim;
using LiteTesting;
using LiteTesting.Unity;
using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 会话与对局上下文验收（全替身、零网络、零墙钟）：
    /// - BattleClient 装配面：连接即 Join / 断线重连闭环 / 版本不符拒绝；
    /// - BattleContext 编排面：StartGame 建 Sim、快照和解、本地实体对齐、
    ///   Ended 恰好一次、Dispose 拆订阅 + Match Scope 清零（离场无 Match 残留的载体断言）。
    /// 形态参照 .NET 侧 RoomClientReconnectTests（假 IClientTransport + 注入时钟）。
    /// 分类口径：[Category(TestCategory.Contract)] 逐用例标注——Unity 测试框架自带 NUnit 3.5
    /// **不含 TraitAttribute**（.NET 侧 L1 用的 3.13 才有），EditMode 一律用 Category（既有约定）。
    /// </summary>
    public sealed class BattleSessionEditModeTests : UnityTestBase
    {
        private const string Host = "127.0.0.1";
        private const int Port = 17777;
        private const string RoomId = "Room-A";
        private const string Token = "c2-dev-token";

        /// <summary>
        /// 假传输：连接/断线事件手工驱动；Send 解码记账（断言上行包型与内容）。
        /// </summary>
        private sealed class FakeClientTransport : IClientTransport
        {
            public bool Connected { get; private set; }
            public int ConnectCalls;
            public event Action OnConnected;
            public event Action<ArraySegment<byte>, bool> OnData;
            public event Action OnDisconnected;
            public readonly List<(PacketType type, IMessage msg, bool reliable)> Sent = new List<(PacketType, IMessage, bool)>();

            public void Connect(string address, int port)
            {
                ConnectCalls++;
                Connected = true;
                OnConnected?.Invoke();
            }

            public void Disconnect()
            {
                if (!Connected) return;
                Connected = false;
                OnDisconnected?.Invoke();
            }

            /// <summary>模拟**网络掉线**（服务器不可达）——与协议层主动 <see cref="Disconnect"/> 区分：
            /// 掉线是"席位仍在服务器重连窗口内"的触发源（Connected → SuspectedLost）。
            /// 未连接时不动相位（从未进房没有席位可失——与 .NET 侧 RaiseDisconnected 同口径）。</summary>
            public void DropConnection()
            {
                if (!Connected) return;
                Connected = false;
                OnDisconnected?.Invoke();
            }

            public void TickIncoming() { }
            public void TickOutgoing() { }

            public void Send(ArraySegment<byte> data, bool reliable)
            {
                if (PacketCodec.TryDecode(data, out PacketType type, out IMessage msg))
                    Sent.Add((type, msg, reliable));
            }

            public void Dispose() => Disconnect();

            public void Deliver(IMessage message, PacketType type)
                => OnData?.Invoke(new ArraySegment<byte>(PacketCodec.Encode(type, message)), true);

            public int CountOf(PacketType type) => Sent.FindAll(s => s.type == type).Count;
        }

        /// <summary>设备替身：恒返回一份"采到了"的空意图——让输入链路（采样 → 帧边界门 → 上行）完整走通。
        /// （BattleContext 经 <see cref="IInputService"/> 取输入，null = 空输入。）</summary>
        private sealed class StubIntentSource : IIntentSource
        {
            public string Name => "stub";
            public IntentSample Sample(in SimVector3 localPos) => new IntentSample(default);
        }

        private FakeClientTransport _transport;

        private BattleClient NewClient(string buildHash = null)
        {
            _transport = new FakeClientTransport();
            var battle = new BattleClient(Host, Port, RoomId, Token, buildHash ?? LiteNet.BuildHash.Value, _transport);
            Assert.That(battle.Phase, Is.EqualTo(ClientSessionPhase.Idle));
            Assert.That(_transport.CountOf(PacketType.Join), Is.EqualTo(1), "连接建立即发 Join（Idle 相位）");
            JoinRequest join = (JoinRequest)_transport.Sent[0].msg;
            Assert.That(join.RoomId, Is.EqualTo(RoomId));
            Assert.That(join.Token, Is.EqualTo(Token));
            Assert.That(join.BuildHash, Is.EqualTo(buildHash ?? LiteNet.BuildHash.Value));
            return battle;
        }

        // ---- BattleClient 装配面 ----

        [Test]
        [Category(TestCategory.Contract)]
        public void 连接即Join_收JoinAck进入Connected()
        {
            BattleClient battle = NewClient();
            _transport.Deliver(new JoinAck { PlayerId = 1, ReconnectToken = "ticket-1" }, PacketType.JoinAck);

            Assert.That(battle.Phase, Is.EqualTo(ClientSessionPhase.Connected));
            Assert.That(battle.Client.PlayerId, Is.EqualTo(1));
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 断线重连闭环_重拨自动发请求_恢复完成回Connected()
        {
            BattleClient battle = NewClient();
            _transport.Deliver(new JoinAck { PlayerId = 0, ReconnectToken = "ticket-1" }, PacketType.JoinAck);

            _transport.DropConnection();                              // 断线：席位仍在重连窗口
            Assert.That(battle.Phase, Is.EqualTo(ClientSessionPhase.SuspectedLost));

            Assert.That(battle.BeginReconnect(), Is.True);           // 凭票据重拨
            Assert.That(_transport.ConnectCalls, Is.EqualTo(2));
            Assert.That(battle.Phase, Is.EqualTo(ClientSessionPhase.Reconnecting));
            Assert.That(_transport.CountOf(PacketType.ReconnectRequest), Is.EqualTo(1), "重拨连接建立后自动发重连请求");
            ReconnectRequest request = (ReconnectRequest)_transport.Sent.Find(s => s.type == PacketType.ReconnectRequest).msg;
            Assert.That(request.OneTimeToken, Is.EqualTo("ticket-1"));

            _transport.Deliver(new ReconnectResponse { Ok = true, BuildHash = LiteNet.BuildHash.Value }, PacketType.ReconnectResponse);
            Assert.That(battle.Phase, Is.EqualTo(ClientSessionPhase.Restoring));

            Assert.That(battle.Client.CompleteRestore(), Is.True);   // 恢复完成 ACK（Reliable）
            Assert.That(_transport.CountOf(PacketType.RestoreComplete), Is.EqualTo(1));
            Assert.That(battle.Phase, Is.EqualTo(ClientSessionPhase.Connected));
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 重连响应版本不符_转Failed_不发恢复ACK()
        {
            BattleClient battle = NewClient();
            _transport.Deliver(new JoinAck { PlayerId = 0, ReconnectToken = "ticket-1" }, PacketType.JoinAck);
            _transport.DropConnection();
            Assert.That(battle.BeginReconnect(), Is.True);

            _transport.Deliver(new ReconnectResponse { Ok = true, BuildHash = "different-build" }, PacketType.ReconnectResponse);
            Assert.That(battle.Phase, Is.EqualTo(ClientSessionPhase.Failed), "跨版本混房必须失败（§9.3 步骤 2）");
            Assert.That(_transport.CountOf(PacketType.RestoreComplete), Is.EqualTo(0), "版本不符绝不发恢复 ACK");
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 无票据断线_BeginReconnect失败转Failed()
        {
            // 票据耗尽形态（镜像 .NET 侧 无票据重连_直接Failed）：进房但 JoinAck 未下发票据
            BattleClient battle = NewClient();
            _transport.Deliver(new JoinAck { PlayerId = 0, ReconnectToken = "" }, PacketType.JoinAck);
            Assert.That(battle.Phase, Is.EqualTo(ClientSessionPhase.Connected));

            _transport.DropConnection();                                // 掉线：席位在重连窗口内
            Assert.That(battle.Phase, Is.EqualTo(ClientSessionPhase.SuspectedLost));

            Assert.That(battle.BeginReconnect(), Is.False, "无票据：重连窗口已耗尽——唯一出路是重新 Join");
            Assert.That(battle.Phase, Is.EqualTo(ClientSessionPhase.Failed));
            Assert.That(_transport.ConnectCalls, Is.EqualTo(1), "票据耗尽不重拨");
        }

        // ---- BattleContext 编排面 ----

        /// <summary>建一个已进房的 BattleClient（JoinAck 已达，PlayerId=0）。</summary>
        private BattleClient JoinedClient()
        {
            BattleClient battle = NewClient();
            _transport.Deliver(new JoinAck { PlayerId = 0, ReconnectToken = "ticket-1" }, PacketType.JoinAck);
            return battle;
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void StartGame建立预测Sim_快照和解对齐本地实体()
        {
            BattleClient battle = JoinedClient();
            using (var account = new ClientScope("Account"))
            {
                account.Register(battle);                        // 生产形态：Account Scope 拥有会话（离场 Dispose 释放）
                var context = new BattleContext(battle, account);
                try
                {
                    Assert.That(context.Sim, Is.Null);
                    _transport.Deliver(new StartGame { Seed = 12345, ConfigHash = 42, Frame = 0 }, PacketType.StartGame);
                    Assert.That(context.Sim, Is.Not.Null, "StartGame 后按 seed 重建同构世界");
                    Assert.That(context.Sim.State.Frame, Is.EqualTo(0));

                    // 权威快照：实体位于出生点（与客户端同构世界一致——预测成立，不该误报和解）
                    var authoritative = new SimWorldState { RngState = 12345UL };
                    SimMapData map = SimMapData.StandardBattleMap();
                    for (int i = 0; i < 2; i++)
                        authoritative.Spawn(new EntitySlot
                        {
                            Hp = CombatConfig.EntityHp,
                            Pos = map.SpawnPoints[i % map.SpawnPointCount],
                            Yaw = 0f,
                        }, out int _);
                    StateSnapshot snapshot = SnapshotCodec.PackFull(1, authoritative, 0);
                    _transport.Deliver(snapshot, PacketType.StateSnapshot);

                    Assert.That(context.LocalEntityId, Is.Not.EqualTo(0), "首份快照按 Slot==PlayerId 对齐本地实体");
                    Assert.That(context.LastSnapshotFrame, Is.EqualTo(1));
                    Assert.That(context.Sim.State.Frame, Is.EqualTo(1), "快照帧锚定本地预测帧轴");
                }
                finally
                {
                    context.Dispose();
                }
            }
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 快照先于StartGame到达_被安全丢弃()
        {
            BattleClient battle = JoinedClient();
            using (var account = new ClientScope("Account"))
            {
                account.Register(battle);                        // 生产形态：Account Scope 拥有会话（离场 Dispose 释放）
                var context = new BattleContext(battle, account);
                try
                {
                    var sim = new SimWorldState { RngState = 1UL };
                    sim.Spawn(new EntitySlot { Hp = 100 }, out int _);
                    _transport.Deliver(SnapshotCodec.PackFull(3, sim, 0), PacketType.StateSnapshot);

                    Assert.That(context.Sim, Is.Null, "StartGame 未达：快照丢弃（Reliable 信令随后即到）");
                    Assert.That(context.LastSnapshotFrame, Is.EqualTo(3));   // 帧号照常记录（诊断面）
                }
                finally
                {
                    context.Dispose();
                }
            }
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 会话失败触发Ended_SessionFailed_自动重连先行()
        {
            BattleClient battle = JoinedClient();
            using (var account = new ClientScope("Account"))
            {
                account.Register(battle);                        // 生产形态：Account Scope 拥有会话（离场 Dispose 释放）
                var context = new BattleContext(battle, account);
                int endedCount = 0;
                BattleContext.EndReason reason = BattleContext.EndReason.Leave;
                context.Ended += r => { endedCount++; reason = r; };

                _transport.Deliver(new StartGame { Seed = 1, ConfigHash = 1, Frame = 0 }, PacketType.StartGame);
                _transport.DropConnection();                          // SuspectedLost → 自动 BeginReconnect
                Assert.That(_transport.ConnectCalls, Is.EqualTo(2), "断线自动重连（C2 会话子集策略）");

                _transport.Deliver(new ReconnectResponse { Ok = false, Reason = "票据无效" }, PacketType.ReconnectResponse);
                Assert.That(battle.Phase, Is.EqualTo(ClientSessionPhase.Failed));
                Assert.That(endedCount, Is.EqualTo(1));
                Assert.That(reason, Is.EqualTo(BattleContext.EndReason.SessionFailed));

                context.Dispose();
            }
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 离场收尾_Ended恰好一次_Dispose拆订阅且MatchScope清零()
        {
            BattleClient battle = JoinedClient();
            using (var account = new ClientScope("Account"))
            {
                account.Register(battle);                        // 生产形态：Account Scope 拥有会话（离场 Dispose 释放）
                var context = new BattleContext(battle, account);
                try
                {
                    // 兄弟 Scope（非 BattleContext 所有）：验证 BattleContext 的 Dispose 不越权动父级
                    ClientScope sibling = account.CreateChild("Scene");
                    Assert.That(account.OwnedCount, Is.EqualTo(1), "CreateChild 不登记（作用域树≠资源清单）——Account 只登记 BattleClient");

                    int endedCount = 0;
                    context.Ended += _ => endedCount++;
                    context.Leave();
                    context.Leave();                                 // 幂等：重复 Leave 不重复发
                    Assert.That(endedCount, Is.EqualTo(1));

                    int snapshotsAfterDispose = 0;
                    context.Dispose();
                    context.Ended += _ => snapshotsAfterDispose++;   // Dispose 后 Ended 不再触发（订阅已拆）
                    context.Leave();
                    _transport.Deliver(new StartGame { Seed = 9, ConfigHash = 9, Frame = 0 }, PacketType.StateSnapshot);

                    Assert.That(snapshotsAfterDispose, Is.EqualTo(0), "Dispose 后事件面全部短路");
                    Assert.That(account.OwnedCount, Is.EqualTo(1), "Account Scope 只剩 BattleClient（Match 归 BattleContext 自有 Scope）");
                    Assert.That(account.IsDisposed, Is.False, "Account 归流程层收尾，BattleContext 不越权");
                    Assert.That(sibling.IsDisposed, Is.False, "兄弟 Scope 不受 BattleContext.Dispose 影响");
                    Assert.That(_transport.Connected, Is.True, "传输归 Account（BattleClient）——BattleContext.Dispose 不动它");

                    sibling.Dispose();
                }
                finally
                {
                    context.Dispose();                               // 幂等
                }
            }
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void Tick驱动_未开局只泵网络_开局上行输入并推进预测()
        {
            BattleClient battle = JoinedClient();
            using (var account = new ClientScope("Account"))
            {
                account.Register(battle);                        // 生产形态：Account Scope 拥有会话（离场 Dispose 释放）
                var inputService = new InputService();
                inputService.SetSource(new StubIntentSource());      // 设备替身：输入链路完整（null 输入 = 不上行）
                var context = new BattleContext(battle, account, inputService);
                try
                {
                    context.Tick(1f / 60);                           // StartGame 未达：只泵网络，不抛
                    Assert.That(context.Sim, Is.Null);
                    Assert.That(_transport.CountOf(PacketType.Input), Is.EqualTo(0), "未进对局不上行输入");

                    _transport.Deliver(new StartGame { Seed = 12345, ConfigHash = 42, Frame = 0 }, PacketType.StartGame);
                    inputService.SampleOnRenderFrame(context.LocalPosition); // 生产序：渲染帧采样 → 逻辑帧 Tick 取用/上行
                    context.Tick(1f / 60);                           // 首个对局 Tick：发输入（帧 1）+ 预测推进
                    Assert.That(_transport.CountOf(PacketType.Input), Is.EqualTo(1));
                    InputMessage input = (InputMessage)_transport.Sent.Find(s => s.type == PacketType.Input).msg;
                    Assert.That(input.Frame, Is.EqualTo(1), "inputDelay=1：发下一帧");
                    Assert.That(context.Sim.State.Frame, Is.EqualTo(1));
                }
                finally
                {
                    context.Dispose();
                }
            }
        }
    }
}
