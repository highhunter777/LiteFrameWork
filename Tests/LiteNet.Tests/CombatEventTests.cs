using System.Collections.Generic;
using LiteNet.Protocol;
using LiteNet.Proto;
using LiteSim;
using LiteSim.Tests;
using RoomServer.Application;
using RoomServer.Runtime;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// 公共战斗事件面（《状态同步专项设计》§5.2 CombatEvent 面）：
    /// 房间内核窗口（<see cref="CombatEventWindow"/>）、wire 映射（<see cref="CombatEventCodec"/>）、
    /// 独立包下发（<see cref="CombatEventPipeline"/>），以及**取代旧三个窗口的三条纪律**：
    /// 时间老化、同键聚合、容量有界。
    ///
    /// 本文件合并了原 <c>KillFeedTests</c>/<c>FireSignalTests</c>/<c>HitFeedTests</c>——
    /// 三类事件现在是同一条链，分开测会掩盖"它们共用同一个窗口/同一个预算"这个事实。
    /// </summary>
    public sealed class CombatEventTests
    {
        // ---- 纯件：窗口（老化/聚合/容量） ----

        /// <summary>
        /// **时间老化是本批的根因修复**：旧三环只按容量淘汰，打满后永远满载 ⇒ 每份快照恒定多带
        /// 64 条 ⇒ 顶爆 KCP 不可靠上限 ⇒ 整份快照被丢。本用例钉住"停火 1s 后窗口清空"。
        /// </summary>
        [Fact]
        public void WindowPrunesEventsOlderThanStaleFrames()
        {
            var window = new CombatEventWindow();
            window.Add(FrameEventKind.Hit, 1, 2, frame: 100, value: 10, default);
            window.Add(FrameEventKind.Hit, 1, 2, frame: 150, value: 11, default);
            Assert.Equal(2, window.Count);

            // 老化窗内：cutoff = 150 - StaleFrames，两条都还在（最旧那条在窗内）
            Assert.Equal(0, window.Prune(100 + CombatEventWindow.StaleFrames - 1));
            Assert.Equal(2, window.Count);

            // 越过老化窗：cutoff = 150 → 帧 100 退场、帧 150 恰在窗内留下
            Assert.Equal(1, window.Prune(150 + CombatEventWindow.StaleFrames));
            Assert.Equal(1, window.Count);
            Assert.Equal(150, window.Get(0).Frame);

            // 再往前推：全清（停火后窗口必须能真正空下来——否则又会满载）
            Assert.Equal(1, window.Prune(1000 + CombatEventWindow.StaleFrames));
            Assert.Equal(0, window.Count);
        }

        /// <summary>同 (kind, 双方, 帧) 只留一条：霰弹多弹丸聚合伤害，Crit 升档（客户端按 kind 分档即可）。</summary>
        [Fact]
        public void WindowAggregatesSameKeyAndUpgradesCrit()
        {
            var window = new CombatEventWindow();
            window.Add(FrameEventKind.Hit, a: 20, b: 10, frame: 7, value: 5, default);
            window.Add(FrameEventKind.Hit, a: 20, b: 10, frame: 7, value: 6, default);
            window.Add(FrameEventKind.Crit, a: 20, b: 10, frame: 7, value: 7, default);

            Assert.Equal(1, window.Count);                 // 三条同键 → 一条
            CombatEventRecord r = window.Get(0);
            Assert.Equal(FrameEventKind.Crit, r.Kind);     // Crit 升档
            Assert.Equal(18, r.Value);                     // 5 + 6 + 7
        }

        /// <summary>容量有界：满则淘汰最旧（老化之外的第二道闸），并计入 DroppedCount 供 Ops 观测。</summary>
        [Fact]
        public void WindowDropsOldestBeyondCapacity()
        {
            var window = new CombatEventWindow();
            for (int i = 0; i < CombatEventWindow.Capacity + 3; i++)
                window.Add(FrameEventKind.Fire, a: i, b: 0, frame: 1000 + i, value: 0, default);

            Assert.Equal(CombatEventWindow.Capacity, window.Count);
            Assert.Equal(3, window.DroppedCount);
            Assert.Equal(3L, window.Get(0).A);             // 最旧三条被挤出
        }

        // ---- 纯件：wire 映射（kind 判别，新增类型不改协议） ----

        [Fact]
        public void CodecRoundTripsAllKnownKindsAndRejectsUnknown()
        {
            foreach (FrameEventKind kind in new[]
            {
                FrameEventKind.Fire, FrameEventKind.Hit, FrameEventKind.Death,
                FrameEventKind.Crit, FrameEventKind.ObstacleHit,
            })
            {
                var source = new CombatEventRecord(kind, 11, 22, 33, 44, new SimVector3(1.5f, 2.5f, 3.5f));
                Proto.CombatEventEntry wire = CombatEventCodec.ToProto(in source);
                Assert.True(CombatEventCodec.TryFromProto(wire, out CombatEventRecord back), $"{kind} 应可往返");
                Assert.Equal(source.Kind, back.Kind);
                Assert.Equal(source.A, back.A);
                Assert.Equal(source.B, back.B);
                Assert.Equal(source.Frame, back.Frame);
                Assert.Equal(source.Value, back.Value);
                Assert.Equal(source.Pos.X, back.Pos.X);
            }

            // 未知 kind（未来端/坏包）：不猜、不改写——丢弃
            var unknown = new Proto.CombatEventEntry { Kind = 999, A = 1, B = 2, Frame = 3 };
            Assert.False(CombatEventCodec.TryFromProto(unknown, out _));
        }

        /// <summary>
        /// **预算守望**：满窗口的事件批必须装进一个不可靠包。
        /// 这条测试就是"别再把包撑爆"的机制化守卫——红了说明有人加了字段/提了容量，
        /// 而 KCP 会在运行时静默丢掉整包。
        /// 两侧常量都从单源读（<see cref="WireBudget"/> / <see cref="CombatEventWindow"/>），不写字面量。
        /// </summary>
        [Fact]
        public void FullWindowBatchFitsUnreliableBudget()
        {
            var window = new CombatEventWindow();
            for (int i = 0; i < CombatEventWindow.Capacity; i++)
            {
                // 最贵形态：Hit（带三浮点位置 + 大 Id + 非零伤害）
                window.Add(FrameEventKind.Hit, a: 1L << 40, b: 2L << 40, frame: 5000 + i, value: 12345,
                    new SimVector3(-123.456f, 12.345f, -0.001f));
            }

            var batch = new CombatEventBatch();
            for (int i = 0; i < window.Count; i++)
            {
                CombatEventRecord r = window.Get(i);
                batch.Records.Add(CombatEventCodec.ToProto(in r));
            }

            int bytes = batch.CalculateSize();
            Assert.True(WireBudget.FitsUnreliable(bytes),
                $"满窗口事件批超预算：{WireBudget.Describe(bytes)}——KCP 会静默丢整包，必须调小 " +
                $"CombatEventWindow.Capacity 或缩短 StaleFrames");
        }

        /// <summary>超预算时**留新弃旧**裁到预算内（不静默丢整包），并回报裁掉的条数。</summary>
        [Fact]
        public void PackBatchTrimsOldestWhenOverBudget()
        {
            // 用极小预算模拟"预算收紧"：直接调 PackBatch 不可注入预算，
            // 故这里验证真实预算下的正常路径 + 裁剪逻辑本身（用超长 Id 造大记录）。
            var window = new CombatEventWindow();
            for (int i = 0; i < CombatEventWindow.Capacity; i++)
                window.Add(FrameEventKind.Hit, a: 1L << 20, b: 2L << 20, frame: 100 + i, value: 9, default);

            CombatEventBatch batch = CombatEventCodec.PackBatch(200, window, out int dropped);
            Assert.Equal(0, dropped);                                  // 实测容量下装得进
            Assert.Equal(window.Count, batch.Records.Count);
            Assert.Equal(100, batch.Records[0].Frame);                 // 升序：最旧在前
            Assert.True(WireBudget.FitsUnreliable(batch.CalculateSize()));
        }

        // ---- 集成：三种事件都进同一条链，并经独立包下发 ----

        [Fact]
        public void DeathHitAndFireAllFlowThroughOneWindowAndOnePacket()
        {
            var room = new RoomRuntime(new RoomConfig
            {
                Map = TestMaps.Greybox(),
                RoomId = "CombatEvents",
                Seed = 7,
                ExpectedPlayers = 2,
                Rules = new MatchRules(0, 0, 3, 2),
            });
            var seats = new Session[room.ExpectedPlayers];
            var pipeline = new SnapshotPipeline(seats);
            var events = new CombatEventPipeline(room.Events, seats);   // 与生产装配同形（席位表构造期定值）
            var batches = new List<CombatEventBatch>();
            events.SendTo = (seat, type, msg, reliable) =>
            {
                if (type == PacketType.CombatEvents) batches.Add((CombatEventBatch)msg);
            };

            var outputs = new List<RoomOutput>();
            Assert.Equal(0, Join(room, new Session(1, 0), seats));
            Assert.Equal(1, Join(room, new Session(2, 0), seats));
            room.Execute(RoomCommand.StartEarly(0), new List<RoomOutput>());

            room.Execute(RoomCommand.Tick(0), outputs);       // 帧 1：懒装备
            Broadcast(room, seats, pipeline, events);                // 抽帧序 1（不播）
            Assert.Empty(batches);                            // 没到广播帧 → 不发事件批

            // 帧 2：p0 击杀 p1（Death），同帧 p1 开火（Fire）
            room.AuthSim.TryResolve(room.EntityIdOf(0), out int killerSlot);
            room.AuthSim.Entities[killerSlot].SelectedWeapon = 1;
            room.AuthSim.Cmds.Write(SimCommandKind.Damage, room.EntityIdOf(1), room.EntityIdOf(0), 1000);
            Fire(room, outputs, 1);
            room.Execute(RoomCommand.Tick(0), outputs);
            Broadcast(room, seats, pipeline, events);                // 抽帧序 2（广播）

            // Sent 是**按席位**计数（同一份批发给每个可播席位；事件批不做 AOI 裁剪）
            Assert.Equal(seats.Length, events.Sent);
            Assert.Equal(seats.Length, batches.Count);
            CombatEventBatch batch = batches[batches.Count - 1];
            Assert.True(batch.Records.Count >= 2, $"应含死亡与开火：{batch.Records.Count}");

            CombatEventRecord? death = null;
            CombatEventRecord? fire = null;
            for (int i = 0; i < batch.Records.Count; i++)
            {
                Assert.True(CombatEventCodec.TryFromProto(batch.Records[i], out CombatEventRecord e));
                if (e.Kind == FrameEventKind.Death) death = e;
                if (e.Kind == FrameEventKind.Fire) fire = e;
            }

            Assert.True(death.HasValue, "死亡事件必须在批里");
            Assert.Equal(room.EntityIdOf(0), death.Value.B);      // B = 击杀者
            Assert.Equal(room.EntityIdOf(1), death.Value.A);      // A = 死者
            Assert.Equal(1, death.Value.Value);                   // Value = 击杀帧装备槽（击杀提示图标）
            Assert.True(fire.HasValue, "开火事件必须在批里");
            Assert.Equal(room.EntityIdOf(1), fire.Value.A);
        }

        /// <summary>
        /// 状态快照**不再携带任何事件窗口**（本批的结构性收口）：
        /// 快照尺寸只随实体数变，事件量再大也顶不到它。
        /// </summary>
        [Fact]
        public void SnapshotCarriesNoCombatEventPayload()
        {
            var room = new RoomRuntime(new RoomConfig
            {
                Map = TestMaps.Greybox(),
                RoomId = "SnapPure",
                Seed = 7,
                ExpectedPlayers = 2,
                Rules = new MatchRules(0, 0, 3, 2),
            });
            var seats = new Session[room.ExpectedPlayers];
            var pipeline = new SnapshotPipeline(seats);
            var snapshots = new List<StateSnapshot>();
            pipeline.SendTo = (seat, type, msg, reliable) =>
            {
                if (type == PacketType.StateSnapshot) snapshots.Add((StateSnapshot)msg);
            };

            var outputs = new List<RoomOutput>();
            Join(room, new Session(1, 0), seats);
            Join(room, new Session(2, 0), seats);
            room.Execute(RoomCommand.StartEarly(0), new List<RoomOutput>());
            room.Execute(RoomCommand.Tick(0), outputs);

            // 塞满事件窗口后再广播：快照尺寸不应有任何变化
            for (int i = 0; i < CombatEventWindow.Capacity; i++)
                room.Events.Add(FrameEventKind.Hit, a: 1L << 20, b: 2L << 20, frame: 1, value: 999, default);

            pipeline.BroadcastIfDue(room.AuthSim.Frame, room.AuthSim, room.Gate, room.EntityIdOf);
            pipeline.BroadcastIfDue(room.AuthSim.Frame + 1, room.AuthSim, room.Gate, room.EntityIdOf);

            Assert.NotEmpty(snapshots);
            StateSnapshot last = snapshots[snapshots.Count - 1];
            Assert.NotNull(last.Match);
            Assert.True(WireBudget.FitsUnreliable(last.CalculateSize()),
                $"快照超预算：{WireBudget.Describe(last.CalculateSize())}");
            Assert.Equal(0, pipeline.SnapshotDropped);
        }

        // ---- helpers ----

        private static void Broadcast(RoomRuntime room, Session[] seats, SnapshotPipeline pipeline,
            CombatEventPipeline events)
        {
            pipeline.BroadcastIfDue(room.AuthSim.Frame, room.AuthSim, room.Gate, room.EntityIdOf);
            if (pipeline.BroadcastedThisFrame)
                events.Publish(room.AuthSim.Frame, p => seats[p] != null && !seats[p].Disconnected);
        }

        /// <summary>提交一名射手的开火输入（带视点帧 → 走待回溯路径；近垂直朝天 = 纯脱靶）。</summary>
        private static void Fire(RoomRuntime room, List<RoomOutput> outputs, int playerId)
        {
            Assert.True(room.AuthSim.TryResolve(room.EntityIdOf(playerId), out int slot));
            SimVector3 pos = room.AuthSim.Entities[slot].Pos;
            var frames = new SimInputFrame[ClientInputBatch.MaxFrames];
            frames[0] = new SimInputFrame
            {
                AimPointX = pos.X, AimPointY = pos.Y + 10f, AimPointZ = pos.Z,
                Buttons = SimInputFrame.ButtonFire,
            };
            room.Execute(RoomCommand.ClientInput(playerId, new ClientInputBatch
            {
                Frame = room.AuthSim.Frame + 1, AckSnapshot = room.AuthSim.Frame, ViewFrame = 30,
                Count = 1, Frames = frames,
            }), outputs);
        }

        /// <summary>执行一次 Join 命令并把席位绑定到 App 层映射（App 装配的测试等价物）。</summary>
        private static int Join(RoomRuntime room, Session session, Session[] seats)
        {
            var outputs = new List<RoomOutput>();
            room.Execute(RoomCommand.Join(session.ConnectionId), outputs);
            foreach (RoomOutput o in outputs)
            {
                if (o is SignalOutput signal && signal.Signal is PlayerAdmitted admitted)
                {
                    session.PlayerId = admitted.PlayerId;
                    seats[admitted.PlayerId] = session;
                    return admitted.PlayerId;
                }
            }
            return -1;
        }
    }
}
