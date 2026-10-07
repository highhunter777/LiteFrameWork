using System;
using LiteSim;
using Xunit;
using LiteClient;

namespace LiteSim.Tests
{
    /// <summary>
    /// 输入服务（《角色状态与动作专项设计》§3"输入三件"；《联机战斗演示专项设计》§5"输入和三个门"）
    /// 的 L1 覆盖。
    ///
    /// 覆盖的是**输入服务自己那一段**：上下文门（谁拦的、拦下 = 本帧空意图并照常上行——与服务器空输入兜底逐位同值，否则本地沿旧值推进、权威按空输入执行，弹模态即回拉）、
    /// 帧边界门（同一逻辑帧只消费一次）、采样与上行（每渲染帧一份结论，预测与上行同值）。
    /// 前置契约（<c>RollbackSim.OnRealInput</c> 早到即入史）由 RollbackSimTests 覆盖；
    /// 设备源（相机换算）与流程接线属 Unity 侧，归 L2/Player 验证——本文件不假装覆盖它们。
    /// </summary>
    public class InputServiceTests
    {
        /// <summary>可编程设备源替身（不引 UnityEngine，纯数据）。</summary>
        private sealed class FakeSource : IIntentSource
        {
            public SimInputFrame Next;
            public int SampleCount;
            public SimVector3 LastOrigin;
            public bool HasOrigin;

            public string Name => "fake";

            public IntentSample Sample(in SimVector3 localPos)
            {
                SampleCount++;
                LastOrigin = localPos;
                HasOrigin = true;
                return new IntentSample(Next);
            }
        }

        /// <summary>永远"没采到"的设备源（区分 None 与"采到空意图"——见 IntentSample 注释）。</summary>
        private sealed class NothingSource : IIntentSource
        {
            public string Name => "nothing";
            public IntentSample Sample(in SimVector3 localPos) => IntentSample.None;
        }

        private static SimInputFrame Move(float x, float z) => new SimInputFrame { MoveX = x, MoveZ = z };

        // ---- 上下文门 ----

        [Fact]
        public void 上下文门_无拦截源时放行且采样()
        {
            var service = new InputService();
            var source = new FakeSource { Next = Move(0f, 1f) };
            service.SetSource(source);

            service.SampleOnRenderFrame(default);

            Assert.False(service.IsBlocked);
            Assert.Null(service.BlockedByName);
            Assert.Equal(1, source.SampleCount);
            Assert.Equal(1f, service.Pending.MoveZ);
        }

        [Fact]
        public void 上下文门_拦截源成立时不采样且报出是谁拦的()
        {
            var service = new InputService();
            var source = new FakeSource { Next = Move(0f, 1f) };
            service.SetSource(source);
            bool modalOpen = true;
            service.RegisterBlocker(new IntentGate.BlockerKey("ui.modal", "模态打开"), () => modalOpen);

            service.SampleOnRenderFrame(default);

            Assert.True(service.IsBlocked);
            Assert.Equal("ui.modal", service.BlockedByName);
            Assert.Equal("模态打开", service.BlockedReason);
            Assert.Equal(0, source.SampleCount);              // 拦下即不采样（不是"采了再清零"）
            Assert.Equal(1, service.SampleDiposedByGate);
        }

        [Fact]
        public void 上下文门_拦下时本帧输入为空且照常上行_预测与权威兜底同值()
        {
            var service = new InputService();
            var source = new FakeSource { Next = Move(0f, 1f) };
            service.SetSource(source);
            bool modalOpen = false;
            service.RegisterBlocker(new IntentGate.BlockerKey("ui.modal", "模态打开"), () => modalOpen);

            service.SampleOnRenderFrame(default);             // 渲染帧 1：放行，采到"向前"
            Assert.Equal(1f, service.Pending.MoveZ);
            Assert.True(service.TryTakeForSend(out _));        // 生产序：每渲染帧 Sample → Tick 内上行（消费开闸标记）

            modalOpen = true;
            service.SampleOnRenderFrame(default);             // 渲染帧 2：拦下
            // 拦下 = 本帧没有战斗输入——空意图（不保持旧值）。
            // 服务器对缺席帧按空输入兜底执行，本地必须同读零，否则"本地在动、权威已停"每份快照回拉。
            Assert.Equal(0f, service.Pending.MoveZ);
            Assert.Equal(1, service.SampleDiposedByGate);
            Assert.True(service.TryTakeForPrediction(2, out SimInputFrame predicted), "拦下帧照常有输入结论（空）");
            Assert.Equal(0f, predicted.MoveZ);
            Assert.True(service.TryTakeForSend(out SimInputFrame sent), "拦下帧照常上行——空意图包同样携带 ackSnapshot");
            Assert.Equal(0f, sent.MoveZ);

            modalOpen = false;
            service.SampleOnRenderFrame(default);             // 渲染帧 3：UI 关掉即恢复采样（采样先于预测，无凭空零帧）
            Assert.False(service.IsBlocked);
            Assert.Equal(2, source.SampleCount);
            Assert.Equal(1f, service.Pending.MoveZ);
        }

        [Fact]
        public void 上下文门_先登记者优先报出理由()
        {
            var service = new InputService();
            service.SetSource(new FakeSource());
            service.RegisterBlocker(new IntentGate.BlockerKey("ui.modal", "第一位"), () => true);
            service.RegisterBlocker(new IntentGate.BlockerKey("session.reconnecting", "第二位"), () => true);

            service.SampleOnRenderFrame(default);

            Assert.Equal("ui.modal", service.BlockedByName);
            Assert.Equal("第一位", service.BlockedReason);
        }

        [Fact]
        public void 上下文门_同名登记是覆盖而非重名报错_注销按名生效()
        {
            var service = new InputService();
            service.SetSource(new FakeSource());
            bool first = true;
            Assert.True(service.RegisterBlocker(new IntentGate.BlockerKey("x", "旧"), () => first));
            Assert.False(service.RegisterBlocker(new IntentGate.BlockerKey("x", "新"), () => false));

            Assert.Equal(1, service.BlockerCount);
            service.SampleOnRenderFrame(default);
            Assert.False(service.IsBlocked);                   // 取的是新登记的那条

            Assert.True(service.UnregisterBlocker("x"));
            Assert.False(service.UnregisterBlocker("x"));
            Assert.Equal(0, service.BlockerCount);
        }

        [Fact]
        public void 上下文门_无设备源时记数且意图为空_不抛()
        {
            var service = new InputService();
            service.SampleOnRenderFrame(default);

            Assert.False(service.IsBlocked);
            Assert.Equal(1, service.SampleWithNoSource);
            Assert.Equal(0f, service.Pending.MoveX);
            Assert.Equal(0u, service.Pending.Buttons);
        }

        [Fact]
        public void 采样_设备未采到时本帧输入为空_照常上行()
        {
            var service = new InputService();
            var source = new FakeSource { Next = Move(0f, 1f) };
            service.SetSource(source);
            service.SampleOnRenderFrame(default);
            Assert.Equal(1f, service.Pending.MoveZ);

            // 设备"没采到"（未就绪/无设备）——与"采到空意图"是两回事（见 IntentSample 注释），
            // 但与被拦**同读法**：空意图 + 照常上行，与服务器空输入兜底同值。
            service.TryTakeForSend(out _);               // 消费上一帧的"已采样"标记（渲染帧已翻页）
            service.SetSource(new NothingSource());
            service.SampleOnRenderFrame(default);

            Assert.Equal(0f, service.Pending.MoveZ);
            Assert.Equal(1, service.SampleWithNoSource);
            Assert.True(service.TryTakeForSend(out SimInputFrame sent), "未采到也要上行——空意图包让权威与本地同读");
            Assert.Equal(0f, sent.MoveZ);
        }

        [Fact]
        public void 采样_设备采到空意图时照常覆盖_用户真的什么都没按()
        {
            var service = new InputService();
            var source = new FakeSource { Next = Move(0f, 1f) };
            service.SetSource(source);
            service.SampleOnRenderFrame(default);
            Assert.Equal(1f, service.Pending.MoveZ);

            service.TryTakeForSend(out _);               // 消费上一帧的"采过"标记（渲染帧已翻页）
            source.Next = default(SimInputFrame);        // 采到了，但是空意图（松开所有键）
            service.SampleOnRenderFrame(default);

            Assert.True(Math.Abs(service.Pending.MoveZ) < 0.0001f, "空意图要如实覆盖——否则松开按键角色还在走");
            Assert.Equal(0, service.SampleWithNoSource);
        }

        // ---- 采样节流与上行 ----

        [Fact]
        public void 采样_每渲染帧只采一次_重复调用不重采()
        {
            var service = new InputService();
            var source = new FakeSource { Next = Move(0f, 1f) };
            service.SetSource(source);

            service.SampleOnRenderFrame(default);
            service.SampleOnRenderFrame(default);

            Assert.Equal(1, source.SampleCount);
        }

        [Fact]
        public void 上行_每渲染帧一份结论_同一帧只报一次()
        {
            var service = new InputService();
            var source = new FakeSource { Next = Move(0.5f, 0f) };
            service.SetSource(source);

            Assert.False(service.TryTakeForSend(out _));        // 从未采样过 = 无结论不上行

            service.SampleOnRenderFrame(default);
            Assert.True(service.TryTakeForSend(out SimInputFrame sent));
            Assert.Equal(0.5f, sent.MoveX);
            Assert.False(service.TryTakeForSend(out _));        // 同一帧不重复上行

            service.SampleOnRenderFrame(default);               // 新渲染帧重新开闸
            Assert.True(service.TryTakeForSend(out _));
        }

        [Fact]
        public void 采样_瞄准原点逐帧由调用方给出_不在服务里缓存()
        {
            var service = new InputService();
            var source = new FakeSource();
            service.SetSource(source);

            service.SampleOnRenderFrame(new SimVector3(3f, 0f, 4f));
            Assert.True(source.HasOrigin);
            Assert.Equal(3f, source.LastOrigin.X);
            Assert.Equal(4f, source.LastOrigin.Z);
        }

        [Fact]
        public void 采样_非有限分量被清零_NAN不进入预测()
        {
            var service = new InputService();
            var source = new FakeSource
            {
                Next = new SimInputFrame { MoveX = float.NaN, MoveZ = 1f, AimPointX = float.PositiveInfinity, AimPointZ = 0f },
            };
            service.SetSource(source);

            service.SampleOnRenderFrame(default);

            Assert.Equal(0f, service.Pending.MoveX);
            Assert.Equal(1f, service.Pending.MoveZ);            // 合法分量不受影响
            Assert.Equal(0f, service.Pending.AimPointX);        // 非有限瞄准点清零 = 无点
        }

        // ---- 帧边界门 ----

        [Fact]
        public void 帧边界门_同一逻辑帧只消费一次_追帧不产生额外输入()
        {
            var service = new InputService();
            service.SetSource(new FakeSource { Next = Move(0f, 1f) });
            service.SampleOnRenderFrame(default);

            Assert.True(service.TryTakeForPrediction(7, out SimInputFrame first));
            Assert.Equal(1f, first.MoveZ);
            Assert.False(service.TryTakeForPrediction(7, out _));    // 同一逻辑帧重复取用 = 不取

            Assert.True(service.TryTakeForPrediction(8, out _));     // 下一逻辑帧可再取（沿用待用意图）
        }

        [Fact]
        public void 帧边界门_未采样过则逻辑帧取不到输入()
        {
            var service = new InputService();
            service.SetSource(new FakeSource { Next = Move(0f, 1f) });

            Assert.False(service.TryTakeForPrediction(1, out SimInputFrame input));
            Assert.Equal(0f, input.MoveZ);
        }

        [Fact]
        public void 帧边界门_拦下期间逻辑帧取到的是空输入()
        {
            var service = new InputService();
            var source = new FakeSource { Next = Move(0f, 1f) };
            service.SetSource(source);
            bool blocked = false;
            service.RegisterBlocker(new IntentGate.BlockerKey("ui.modal", "模态打开"), () => blocked);

            service.SampleOnRenderFrame(default);
            Assert.True(service.TryTakeForPrediction(1, out _));
            service.TryTakeForSend(out _);                   // 渲染帧边界：消费开闸标记（生产序 Sample → 上行）

            blocked = true;
            service.SampleOnRenderFrame(default);
            // 拦下帧的输入结论 = 空：第 2 帧消费到零，与服务器空输入兜底同值。
            Assert.True(service.TryTakeForPrediction(2, out SimInputFrame second));
            Assert.Equal(0f, second.MoveZ);
        }

        // ---- 复位 ----

        [Fact]
        public void 复位_Reset清派发状态但保留拦截源与设备源()
        {
            var service = new InputService();
            var source = new FakeSource { Next = Move(0f, 1f) };
            service.SetSource(source);
            bool modalOpen = false;
            service.RegisterBlocker(new IntentGate.BlockerKey("ui.modal", "模态打开"), () => modalOpen);
            service.SampleOnRenderFrame(default);
            Assert.True(service.TryTakeForPrediction(3, out _));    // 第 3 帧已消费

            service.Reset();

            Assert.False(service.TryTakeForPrediction(3, out _));   // 派发状态已清（不像已消费过）
            Assert.Equal(1, service.BlockerCount);                  // 拦截源保留
            Assert.Same(source, service.Source);                    // 设备源保留
        }

        [Fact]
        public void 复位_ResetAll清干净一切()
        {
            var service = new InputService();
            service.SetSource(new FakeSource());
            service.RegisterBlocker(new IntentGate.BlockerKey("ui.modal", "模态打开"), () => true);
            service.SampleOnRenderFrame(default);

            service.ResetAll();

            Assert.Equal(0, service.BlockerCount);
            Assert.Null(service.Source);
            Assert.Equal(0, service.SampleDiposedByGate);
            Assert.Equal(0, service.SampleWithNoSource);
            Assert.False(service.IsBlocked);
            Assert.Null(service.BlockedByName);
        }
    }

    /// <summary>拦截源注册表本身的边界（与 <see cref="InputServiceTests"/> 分开：本类只测门的多路裁决）。</summary>
    public class IntentGateTests
    {
        [Fact]
        public void 门_无源即不拦()
        {
            Assert.False(new IntentGate().Evaluate());
        }

        [Fact]
        public void 门_容量上限显性失败_不静默丢弃()
        {
            var gate = new IntentGate();
            for (int i = 0; i < IntentGate.MaxBlockers; i++)
                gate.Register(new IntentGate.BlockerKey("k" + i, ""), () => false);

            Assert.Equal(IntentGate.MaxBlockers, gate.Count);
            Assert.Throws<InvalidOperationException>(
                () => gate.Register(new IntentGate.BlockerKey("overflow", ""), () => false));
        }

        [Fact]
        public void 门_重名直调即抛_名字是追溯的唯一依据()
        {
            var gate = new IntentGate();
            gate.Register(new IntentGate.BlockerKey("dup", ""), () => false);

            Assert.Throws<InvalidOperationException>(() => gate.Register(new IntentGate.BlockerKey("dup", ""), () => false));
        }

        [Fact]
        public void 门_空名即抛()
        {
            Assert.Throws<ArgumentException>(() => new IntentGate().Register(new IntentGate.BlockerKey("", ""), () => false));
        }

        [Fact]
        public void 门_求值可重复调用_条件按当次求值结果为准()
        {
            var gate = new IntentGate();
            bool flag = false;
            gate.Register(new IntentGate.BlockerKey("f", ""), () => flag);

            Assert.False(gate.Evaluate());
            flag = true;
            Assert.True(gate.Evaluate());
            Assert.Equal("f", gate.BlockedBy.Value.Name);
            flag = false;
            Assert.False(gate.Evaluate());
            Assert.Null(gate.BlockedBy);
        }

        [Fact]
        public void 门_按名注销保持其余登记序()
        {
            var gate = new IntentGate();
            gate.Register(new IntentGate.BlockerKey("a", ""), () => false);
            gate.Register(new IntentGate.BlockerKey("b", ""), () => false);
            gate.Register(new IntentGate.BlockerKey("c", ""), () => true);

            Assert.True(gate.Remove("a"));
            Assert.False(gate.Remove("a"));
            Assert.Equal(2, gate.Count);
            Assert.True(gate.Evaluate());
            Assert.Equal("c", gate.BlockedBy.Value.Name);       // b 在前且不成立 → 报 c
        }
    }
}
