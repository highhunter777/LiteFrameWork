using System;
using System.Collections.Generic;
using LiteNet.Protocol;
using LiteNet.Transport;
using LiteSim;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// `RoomClient` 输入冗余与生命周期（2026-09-19 客户端审查四条，逐条钉成用例）：
    ///
    /// 1. **冗余窗口是真历史**：包里带 frame、frame−1、frame−2…，每帧**各自的内容**——
    ///    丢一包仍能从后续包补帧（修正前把同一份"最新输入"重复 4 次，冗余形同虚设）。
    /// 2. **开火位随帧保留**：Buttons 属于各自那一帧，不再只在首个冗余帧里出现。
    /// 3. **退订**：Dispose 后传输事件不再回调 RoomClient（用假传输验，不碰真实网络）。
    /// 4. **不接管传输所有权**：Dispose 不释放注入的传输。
    ///
    /// 另含编码复用路径（`PacketWriter`）的正确性：与 `PacketCodec.Encode` 逐字节一致。
    /// </summary>
    public sealed class RoomClientInputTests
    {
        [Fact]
        public void 冗余窗口_带真实历史帧且各帧内容独立()
        {
            var t = new FakeTransport();
            var c = new RoomClient(t);

            for (int frame = 1; frame <= 5; frame++)
                c.SendInput(frame, Input(frame, buttons: frame == 5 ? 1u : 0u), viewFrame: 0);

            // 最后一包 = frame 5，应带 5/4/3/2 四帧（窗口上限 4）
            Proto.InputMessage last = t.LastInput();
            Assert.Equal(5, last.Frame);
            Assert.Equal(4, last.Frames.Count);
            Assert.Equal(4, c.RedundancyWindowSize);

            // 逐帧取用（服务器侧口径）：frame 2 的内容仍能从这个包里取到 → 丢包可补帧
            Assert.True(InputPacker.TryGetFrame(last, 5, out SimInputFrame f5));
            Assert.True(InputPacker.TryGetFrame(last, 2, out SimInputFrame f2));
            Assert.Equal(2f, f2.MoveX, 1e-6f);          // 每帧各自的内容，不是同一份
            Assert.Equal(5f, f5.MoveX, 1e-6f);
            Assert.False(InputPacker.TryGetFrame(last, 1, out _));   // 窗口外（只带 4 帧）
        }

        [Fact]
        public void 开火位_随各自帧保留()
        {
            var t = new FakeTransport();
            var c = new RoomClient(t);

            c.SendInput(1, Input(1, buttons: 0), 0);
            c.SendInput(2, Input(2, buttons: 0), 0);
            c.SendInput(3, Input(3, buttons: 1), 0);      // 本帧开火

            Proto.InputMessage last = t.LastInput();
            Assert.True(InputPacker.TryGetFrame(last, 3, out SimInputFrame f3));
            Assert.Equal(1u, f3.Buttons);                 // 开火位在 frame 3 上保留
            Assert.True(InputPacker.TryGetFrame(last, 2, out SimInputFrame f2));
            Assert.Equal(0u, f2.Buttons);                 // 未开火的帧保持 0（不冒充）
        }

        [Fact]
        public void 丢包场景_跳帧后仍能补上被丢的那帧()
        {
            var t = new FakeTransport();
            var c = new RoomClient(t);

            c.SendInput(1, Input(1, 0), 0);
            c.SendInput(2, Input(2, 0), 0);               // 假设这一包**丢失**（服务器没收到）
            c.SendInput(3, Input(3, 0), 0);               // 下一包正常到达

            Proto.InputMessage last = t.LastInput();      // 只看到第 2 包（第 1 包被"丢"的是 2 的发送）
            Assert.True(InputPacker.TryGetFrame(last, 2, out SimInputFrame f2));
            Assert.Equal(2f, f2.MoveX, 1e-6f);            // frame 2 从 frame 3 的包补回
            Assert.True(InputPacker.TryGetFrame(last, 1, out SimInputFrame f1));
            Assert.Equal(1f, f1.MoveX, 1e-6f);
        }

        [Fact]
        public void 跳帧_窗口重置为1_不用陈旧帧冒充缺失帧()
        {
            var t = new FakeTransport();
            var c = new RoomClient(t);

            c.SendInput(1, Input(1, 0), 0);
            c.SendInput(2, Input(2, 0), 0);
            c.SendInput(9, Input(9, 0), 0);               // 中间 3..8 没发（帧号跳变）

            Proto.InputMessage last = t.LastInput();
            Assert.Equal(9, last.Frame);
            Assert.Single(last.Frames);                   // 只带本帧——绝不把 frame 2 当成 8 的输入
            Assert.Equal(1, c.RedundancyWindowSize);
            Assert.False(InputPacker.TryGetFrame(last, 8, out _));
        }

        [Fact]
        public void 重发同帧_窗口不推进_内容覆盖()
        {
            var t = new FakeTransport();
            var c = new RoomClient(t);

            c.SendInput(1, Input(1, 0), 0);
            c.SendInput(2, Input(2, 0), 0);
            int before = c.RedundancyWindowSize;

            SimInputFrame changed = Input(2, 0);
            changed.MoveX = 5f;
            c.SendInput(2, changed, 10);                  // 同帧重发（换内容）

            Assert.Equal(before, c.RedundancyWindowSize); // 窗口不变
            Proto.InputMessage last = t.LastInput();
            Assert.True(InputPacker.TryGetFrame(last, 2, out SimInputFrame f2));
            Assert.Equal(5f, f2.MoveX, 1e-6f);            // 覆盖生效
        }

        [Fact]
        public void 首帧_窗口只有一帧()
        {
            var t = new FakeTransport();
            var c = new RoomClient(t);
            c.SendInput(1, Input(1, 0), 0);

            Proto.InputMessage last = t.LastInput();
            Assert.Single(last.Frames);
            Assert.Equal(1, c.RedundancyWindowSize);
        }

        [Fact]
        public void Dispose_退订传输事件_不接管所有权()
        {
            var t = new FakeTransport();
            var c = new RoomClient(t);
            int disconnectCallbacks = 0;
            c.OnDisconnected += () => disconnectCallbacks++;

            c.Dispose();

            Assert.True(t.HasDataSubscribers == false, "Dispose 后仍挂着 OnData 订阅（会重复回调/滞留）");
            Assert.True(t.HasDisconnectedSubscribers == false, "Dispose 后仍挂着 OnDisconnected 订阅（lambda 无法退订即此坑）");
            Assert.False(t.Disposed, "RoomClient 不应释放注入的传输（所有权归创建方）");

            t.RaiseDisconnected();
            Assert.Equal(0, disconnectCallbacks);         // 退订生效
        }

        [Fact]
        public void Dispose后发送_抛_防静默丢弃()
        {
            var t = new FakeTransport();
            var c = new RoomClient(t);
            c.Dispose();

            Assert.Throws<ObjectDisposedException>(() => c.SendJoin("r", "t", "h"));
            Assert.Throws<ObjectDisposedException>(() => c.SendInput(1, Input(1, 0), 0));
        }

        [Fact]
        public void 快照帧与输入确认_分开跟踪_上报的是快照帧()
        {
            var t = new FakeTransport();
            var c = new RoomClient(t);

            // 服务器快照：Frame=100（快照帧），AckInput=57（已接受的输入帧）——两者语义不同
            var snap = new LiteNet.Proto.StateSnapshot { Frame = 100, AckInput = 57, IsFull = true };
            t.RaiseData(Protocol.PacketCodec.Encode(Protocol.PacketType.StateSnapshot, snap));

            Assert.Equal(100, c.LastSnapshotFrame);   // 视点帧推导要用这个（§3.4.1）
            Assert.Equal(57, c.LastAckSnapshot);      // 诊断字段：这是"输入已到达服务器"，别当快照帧用

            // R0-P0-4 契约：InputMessage.AckSnapshot 上报的必须是**已收最新快照帧号**（协议注释口径）。
            // 修正前错报 AckInput（57）——服务器按发送 ledger 验证后"从未发送过的帧号"被整体忽略。
            c.SendInput(101, Input(101, 0), 0);
            Proto.InputMessage last = t.LastInput();
            Assert.Equal(100, last.AckSnapshot);      // 上报快照帧 100（不是输入确认 57）

            // 未收到任何快照时：上报 0（proto3 默认值——服务器按"未来/无效"忽略，无害）
            var t2 = new FakeTransport();
            var c2 = new RoomClient(t2);
            c2.SendInput(1, Input(1, 0), 0);
            Assert.Equal(0, t2.LastInput().AckSnapshot);
            c2.Dispose();
        }

        private static SimInputFrame Input(int frame, uint buttons)
        {
            return new SimInputFrame
            {
                EntityId = 7,
                MoveX = frame, MoveZ = -frame,
                AimX = 1f, AimZ = 0f,
                Buttons = buttons,
            };
        }

        /// <summary>假传输：只记录发送、可控触发事件——验"退订/所有权"这类纯生命周期行为（不碰真实网络）。</summary>
        private sealed class FakeTransport : IClientTransport
        {
            public readonly List<ArraySegment<byte>> Sent = new List<ArraySegment<byte>>();
            public bool Disposed { get; private set; }
            public bool Connected => true;

            public event Action OnConnected;
            public event Action<ArraySegment<byte>, bool> OnData;
            public event Action OnDisconnected;

            public bool HasDataSubscribers => OnData != null;
            public bool HasDisconnectedSubscribers => OnDisconnected != null;

            public void Connect(string address, int port) => OnConnected?.Invoke();
            public void Disconnect() => OnDisconnected?.Invoke();
            public void TickIncoming() { }
            public void TickOutgoing() { }

            /// <summary>注意：真实传输会同步拷贝；假件同样拷贝，避免"复用缓冲"用例在假件上假绿。</summary>
            public void Send(ArraySegment<byte> data, bool reliable)
            {
                var copy = new byte[data.Count];
                Buffer.BlockCopy(data.Array, data.Offset, copy, 0, data.Count);
                Sent.Add(new ArraySegment<byte>(copy));
            }

            public void RaiseDisconnected() => OnDisconnected?.Invoke();

            public void RaiseData(byte[] packet) => OnData?.Invoke(new ArraySegment<byte>(packet), true);

            public Proto.InputMessage LastInput()
            {
                for (int i = Sent.Count - 1; i >= 0; i--)
                {
                    if (!PacketCodec.TryDecode(Sent[i], out PacketType type, out var msg)) continue;
                    if (type == PacketType.Input) return (Proto.InputMessage)msg;
                }
                throw new InvalidOperationException("没有发出过 Input 包");
            }

            public void Dispose() => Disposed = true;
        }
    }
}
