using LiteNet.Protocol;
using LiteNet.Proto;
using LiteSim;
using RoomServer;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// 输入闸门单元用例（《M10实施指导》附「服务端审查」2026-09-19）：
    /// 取帧口径、ack 处理、已消费帧拒绝、按键白名单——四条都是"服务器边界"行为，
    /// 用假消息直接测 `InputGate`，不依赖网络与房间。
    /// </summary>
    public sealed class InputGateTests
    {
        private const int PlayerId = 0;
        private const long EntityId = 42L;

        [Fact]
        public void 取帧_优先服务器当前帧加一()
        {
            var gate = new InputGate(2);
            // 客户端包：msg.Frame = 11，窗口 [11,10,9,8]；服务器当前帧 10 → 需要 11
            InputMessage msg = Packet(11, 1, 2, 3, 4);
            Assert.True(gate.Store(msg, PlayerId, EntityId, serverFrame: 10, out int frame, out _));
            Assert.Equal(11, frame);
        }

        [Fact]
        public void 取帧_窗口含所需帧时取该帧的独立内容()
        {
            var gate = new InputGate(2);
            // 窗口 [11,10,9,8]；服务器当前帧 9 → 需要 10（不是包内最新的 11）
            InputMessage msg = Packet(11, 1f, 2f, 3f, 4f);
            Assert.True(gate.Store(msg, PlayerId, EntityId, serverFrame: 9, out int frame, out SimInputFrame input));
            Assert.Equal(10, frame);
            Assert.Equal(2f, input.MoveX, 1e-6f);          // 取的是 10 那一帧的内容（不是最新帧）
        }

        [Fact]
        public void 已消费帧_拒绝且不入预存()
        {
            var gate = new InputGate(2);
            // 服务器当前帧 10；包内窗口 [9,8,7,6] 全部已消费 → 拒绝，且不得滞留
            InputMessage msg = Packet(9, 1f, 2f, 3f, 4f);
            Assert.False(gate.Store(msg, PlayerId, EntityId, serverFrame: 10, out _, out _));
            Assert.Equal(1, gate.DroppedStaleFrame);
            Assert.Equal(0, gate.AcceptedCount);
            Assert.False(gate.TryConsume(9, PlayerId, out _));   // 未预存（不会有永不消费的滞留项）
            Assert.False(gate.TryConsume(8, PlayerId, out _));
        }

        [Fact]
        public void 超前ack_不丢输入_只计数并钳位()
        {
            var gate = new InputGate(2);
            // ack 超前（客户端回传的 ack 曾是"下一待处理帧"——见 RoomBroadcaster 的钳位说明）
            InputMessage msg = Packet(11, 1f, 1f, 1f, 1f, ackSnapshot: 99);
            Assert.True(gate.Store(msg, PlayerId, EntityId, serverFrame: 10, out int frame, out _));
            Assert.Equal(11, frame);                          // 输入照常被接受
            Assert.Equal(1, gate.AcceptedCount);
            Assert.Equal(1, gate.DroppedAckSnapshot);         // 违规有记账
            Assert.Equal(10, gate.LastClampedAckSnapshot);    // 越界值被钳到 serverFrame
        }

        [Fact]
        public void 负ack_同样只计数不丢输入()
        {
            var gate = new InputGate(2);
            InputMessage msg = Packet(11, 1f, 1f, 1f, 1f, ackSnapshot: -5);
            Assert.True(gate.Store(msg, PlayerId, EntityId, serverFrame: 10, out _, out _));
            Assert.Equal(1, gate.DroppedAckSnapshot);
            Assert.Equal(0, gate.LastClampedAckSnapshot);
        }

        [Fact]
        public void 按键白名单_伪造服务器内部位被丢()
        {
            var gate = new InputGate(2);
            InputMessage msg = Packet(11, 1f, 1f, 1f, 1f, buttons: SimInputFrame.ButtonFireFlag);
            Assert.False(gate.Store(msg, PlayerId, EntityId, serverFrame: 10, out _, out _));
            Assert.Equal(1, gate.DroppedIllegalButtons);
        }

        [Fact]
        public void 同帧去重_首条生效()
        {
            var gate = new InputGate(2);
            Assert.True(gate.Store(Packet(11, 1f, 0f, 0f, 0f), PlayerId, EntityId, 10, out _, out _));
            // 同帧重发（内容不同）→ 丢弃（首条生效）
            Assert.False(gate.Store(Packet(11, 9f, 0f, 0f, 0f), PlayerId, EntityId, 10, out _, out _));
            Assert.Equal(1, gate.DroppedDuplicateFrame);
            Assert.True(gate.TryConsume(11, PlayerId, out SimInputFrame kept));
            Assert.Equal(1f, kept.MoveX, 1e-6f);
        }

        [Fact]
        public void 未来帧超容忍窗_拒绝()
        {
            var gate = new InputGate(2);
            int tooFar = 10 + InputGate.FutureFrameTolerance + 1;
            Assert.False(gate.Store(Packet(tooFar, 1f, 0f, 0f, 0f), PlayerId, EntityId, 10, out _, out _));
            Assert.Equal(1, gate.DroppedOutOfRange);
        }

        [Fact]
        public void 实体Id防伪_覆写为会话所属()
        {
            var gate = new InputGate(2);
            InputMessage msg = Packet(11, 1f, 0f, 0f, 0f);
            msg.Frames[0].EntityId = 99999;                   // 客户端上报伪 Id
            Assert.True(gate.Store(msg, PlayerId, EntityId, 10, out _, out SimInputFrame input));
            Assert.Equal(EntityId, input.EntityId);           // 一律覆写
        }

        // ---- P0 输入面扩展（《游戏业务系统总设计》§3.2/§3.3/§5.4）----

        [Fact]
        public void 按键白名单_P0玩家位全集放行()
        {
            var gate = new InputGate(2);
            uint allDefined = SimInputFrame.ButtonFire | SimInputFrame.ButtonReload | SimInputFrame.ButtonSwitchWeapon
                | SimInputFrame.ButtonSkill1 | SimInputFrame.ButtonSkill2 | SimInputFrame.ButtonSkill3
                | SimInputFrame.ButtonPickup | SimInputFrame.ButtonUseItem | SimInputFrame.ButtonDodge;
            // Fire/Dodge 连续/保留位不带 seq 也可；离散位带 seq=1（首个，>0 基线）→ 合法
            InputMessage msg = Packet(11, 1f, 0f, 0f, 0f, buttons: allDefined, actionSeq: 1, target: 77L);
            Assert.True(gate.Store(msg, PlayerId, EntityId, 10, out _, out SimInputFrame input));
            Assert.Equal(allDefined, input.Buttons);
            Assert.Equal(1u, input.ActionSeq);                // 字段透传
            Assert.Equal(1, input.SelectedWeaponSlot);        // 字段透传（默认 1 = 合法槽）
            Assert.Equal(77L, input.TargetEntityId);          // 字段透传
        }

        [Fact]
        public void 离散意图_缺seq被拒()
        {
            var gate = new InputGate(2);
            // Reload 是离散意图：必须携带非零 seq（合法 SDK 不会构造缺 seq 的离散请求）
            InputMessage msg = Packet(11, 1f, 0f, 0f, 0f, buttons: SimInputFrame.ButtonReload, actionSeq: 0);
            Assert.False(gate.Store(msg, PlayerId, EntityId, 10, out _, out _));
            Assert.Equal(1, gate.DroppedIllegalActionSeq);

            // 连续意图（Fire）不带 seq：照常放行——seq 纪律只约束离散动作
            InputMessage fire = Packet(11, 1f, 0f, 0f, 0f, buttons: SimInputFrame.ButtonFire);
            Assert.True(gate.Store(fire, PlayerId, EntityId, 10, out _, out _));
        }

        [Fact]
        public void 离散意图_seq重放被拒_严格递增才放行()
        {
            var gate = new InputGate(2);

            // seq=7 的 Skill1 请求被接受（>0 基线）
            Assert.True(gate.Store(Packet(11, 0f, 0f, 0f, 0f, buttons: SimInputFrame.ButtonSkill1, actionSeq: 7),
                PlayerId, EntityId, 10, out _, out _));

            // 同 seq 跨帧重放（伪造/迟到）→ 拒
            Assert.False(gate.Store(Packet(12, 0f, 0f, 0f, 0f, buttons: SimInputFrame.ButtonSkill1, actionSeq: 7),
                PlayerId, EntityId, 11, out _, out _));
            // 更小的 seq（乱序回放）→ 拒
            Assert.False(gate.Store(Packet(13, 0f, 0f, 0f, 0f, buttons: SimInputFrame.ButtonUseItem, actionSeq: 3),
                PlayerId, EntityId, 12, out _, out _));
            Assert.Equal(2, gate.DroppedIllegalActionSeq);

            // 递增 seq 的后续请求 → 放行
            Assert.True(gate.Store(Packet(14, 0f, 0f, 0f, 0f, buttons: SimInputFrame.ButtonUseItem, actionSeq: 8),
                PlayerId, EntityId, 13, out _, out _));
            Assert.Equal(2, gate.DroppedIllegalActionSeq);    // 计数不因合法请求增加
        }

        [Fact]
        public void seq窗口_冗余同帧重复由同帧去重处理_不误伤后帧()
        {
            var gate = new InputGate(2);
            // 冗余包语义：同帧同 seq 重发 → 同帧去重（首条生效）；下一帧的新请求用新 seq 正常过
            Assert.True(gate.Store(Packet(11, 1f, 0f, 0f, 0f, buttons: SimInputFrame.ButtonReload, actionSeq: 5),
                PlayerId, EntityId, 10, out _, out _));
            Assert.False(gate.Store(Packet(11, 1f, 0f, 0f, 0f, buttons: SimInputFrame.ButtonReload, actionSeq: 5),
                PlayerId, EntityId, 10, out _, out _));
            Assert.Equal(1, gate.DroppedDuplicateFrame);

            Assert.True(gate.Store(Packet(12, 0f, 0f, 0f, 0f, buttons: SimInputFrame.ButtonPickup, actionSeq: 6,
                target: 88L), PlayerId, EntityId, 11, out int frame, out SimInputFrame input));
            Assert.Equal(12, frame);
            Assert.Equal(88L, input.TargetEntityId);          // 拾取目标透传（语义解析归 Sim）
        }

        [Fact]
        public void 切枪槽位_越界被拒_范围内放行()
        {
            var gate = new InputGate(2);

            // 伪造武器槽（§5.4：范围是传输层可见事实）→ 整条丢弃
            InputMessage bad = Packet(11, 1f, 0f, 0f, 0f, buttons: SimInputFrame.ButtonSwitchWeapon, actionSeq: 1);
            bad.Frames[0].SelectedWeaponSlot = SimConfig.WeaponSlotsPerEntity;   // 越界
            Assert.False(gate.Store(bad, PlayerId, EntityId, 10, out _, out _));
            Assert.Equal(1, gate.DroppedIllegalWeaponSlot);

            InputMessage negative = Packet(12, 1f, 0f, 0f, 0f, buttons: SimInputFrame.ButtonSwitchWeapon, actionSeq: 2);
            negative.Frames[0].SelectedWeaponSlot = -1;                          // 负值
            Assert.False(gate.Store(negative, PlayerId, EntityId, 11, out _, out _));
            Assert.Equal(2, gate.DroppedIllegalWeaponSlot);

            // 合法切枪 → 放行且目标槽透传
            InputMessage ok = Packet(13, 1f, 0f, 0f, 0f, buttons: SimInputFrame.ButtonSwitchWeapon, actionSeq: 3);
            ok.Frames[0].SelectedWeaponSlot = 1;
            Assert.True(gate.Store(ok, PlayerId, EntityId, 12, out _, out SimInputFrame input));
            Assert.Equal(1, input.SelectedWeaponSlot);
        }

        [Fact]
        public void 多玩家同帧输入_互不顶替_各取各的()
        {
            var gate = new InputGate(2);
            // 2026-09-22 P0 修复回归：帧号单键会让玩家 0 的同帧输入被玩家 1 顶掉（TryConsume 只有一人拿到）
            InputMessage m0 = Packet(11, 0.5f, 0f, 0f, 0f);
            InputMessage m1 = Packet(11, -0.5f, 0f, 0f, 0f);
            Assert.True(gate.Store(m0, playerId: 0, entityId: 10, serverFrame: 10, out _, out _));
            Assert.True(gate.Store(m1, playerId: 1, entityId: 20, serverFrame: 10, out _, out _));

            Assert.True(gate.TryConsume(11, 0, out SimInputFrame c0));
            Assert.True(gate.TryConsume(11, 1, out SimInputFrame c1));
            Assert.Equal(0.5f, c0.MoveX, 1e-6f);
            Assert.Equal(-0.5f, c1.MoveX, 1e-6f);
            Assert.Equal(10L, c0.EntityId);
            Assert.Equal(20L, c1.EntityId);

            // 消费即出队：再取为空（各玩家自己的帧已各自消费）
            Assert.False(gate.TryConsume(11, 0, out _));
            Assert.False(gate.TryConsume(11, 1, out _));
        }

        /// <summary>构造一条窗口 [frame, frame-1, frame-2, frame-3] 的输入包（每帧内容各不相同）。</summary>
        private static InputMessage Packet(int frame, float m0, float m1, float m2, float m3,
            int ackSnapshot = 0, uint buttons = 0, uint actionSeq = 0, long target = 0)
        {
            var msg = new InputMessage { Frame = frame, AckSnapshot = ackSnapshot, ViewFrame = 0 };
            float[] moves = { m0, m1, m2, m3 };
            for (int i = 0; i < InputPacker.MaxRedundancy; i++)
            {
                msg.Frames.Add(new InputFrame
                {
                    EntityId = 1,
                    MoveX = moves[i], MoveZ = 0f, AimX = 1f, AimZ = 0f,
                    Buttons = i == 0 ? buttons : 0u,
                    SelectedWeaponSlot = i == 0 ? 1 : 0,
                    TargetEntityId = i == 0 ? target : 0L,
                    ActionSeq = i == 0 ? actionSeq : 0u,
                });
            }
            return msg;
        }
    }
}
