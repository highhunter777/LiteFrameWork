using System;
using System.Collections.Generic;
using LiteNet.Protocol;
using LiteSim;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>信封编解码用例（《M10 实施指导》§3 协议往返组：逐字节一致/坏包丢弃不抛）。</summary>
    public class PacketCodecTests
    {
        [Fact]
        public void 信封_全消息类型往返一致()
        {
            var cases = new (PacketType Type, Google.Protobuf.IMessage Message)[]
            {
                (PacketType.Join, new Proto.JoinRequest { RoomId = "r1", Token = "t1", BuildHash = "abc123" }),
                (PacketType.JoinAck, new Proto.JoinAck { PlayerId = 3, Members = { 1, 2, 3 } }),
                (PacketType.StartGame, new Proto.StartGame { Seed = 0x5EEDBEEF12345678L, ConfigHash = 42u }),
                (PacketType.Input, new Proto.InputMessage { Frame = 99, AckSnapshot = 88, ViewFrame = 77,
                    Frames = { new Proto.InputFrame { EntityId = 65537L, MoveX = 0.5f, MoveZ = -0.25f, AimPointX = 1.25f, AimPointY = 1f, AimPointZ = -0.5f, Buttons = 1u,
                        SelectedWeaponSlot = 1, TargetEntityId = 131074L, ActionSeq = 9u } } }),
                (PacketType.StateSnapshot, new Proto.StateSnapshot { Frame = 12, IsFull = true, Checksum = 123456u, AckInput = 11,
                    Slots = { new Proto.SlotDelta { Slot = 2, Id = 65538L, PosX = 1.5f, Hp = 75, Flags = 7u,
                        Shield = 60, Kills = 3, Deaths = 1, SelectedWeapon = 1, ActionId = 301, ActionPhase = 2, ActionStartFrame = 120 } },
                    Match = new Proto.MatchStateDelta { Phase = 1, Team = 0, Score = 0, Timer = 10800, Round = 1, Winner = 0L },
                    PrivateState = new Proto.PrivateStateDelta { EntityId = 65538L, Resource = 42,
                        Weapons = { new Proto.WeaponDelta { WeaponDefId = 9001, MagAmmo = 30, ReserveAmmo = 90, State = 1, NextFireFrame = 500, ReloadEndFrame = 0, EquipEndFrame = 12, ShotSeq = 3 } },
                        Skills = { new Proto.SkillDelta { ActionId = 301, CooldownEnd = 600, Charges = 1 } },
                        Status = { new Proto.StatusDelta { EffectId = 51, EndFrame = 900, Param = 60 } },
                        Bag = { new Proto.BagSlotDelta { ItemDefId = 701, Count = 3, QuickSlot = 4 } } } }),
                (PacketType.MismatchReport, new Proto.MismatchReport { Frame = 34 }),
                (PacketType.Heartbeat, new Proto.Heartbeat { ClientTime = 123456789L }),
                (PacketType.Leave, new Proto.Leave()),
                (PacketType.ReconnectRequest, new Proto.ReconnectRequest { OneTimeToken = "one-time" }),
                (PacketType.ReconnectResponse, new Proto.ReconnectResponse { Ok = true,
                    Snapshot = new Proto.StateSnapshot { Frame = 55, IsFull = true, Checksum = 9u, AckInput = 54 } }),
            };

            foreach (var (type, message) in cases)
            {
                byte[] packet = PacketCodec.Encode(type, message);

                Assert.True(PacketCodec.TryDecode(new ArraySegment<byte>(packet), out var decodedType, out var decoded));
                Assert.Equal(type, decodedType);
                Assert.Equal(message, decoded); // proto 消息值相等（字段级）
            }
        }

        [Fact]
        public void 信封_序列化确定性_两次编码逐字节一致()
        {
            var msg = new Proto.InputMessage { Frame = 7, AckSnapshot = 6, ViewFrame = 0 };
            msg.Frames.Add(new Proto.InputFrame { EntityId = 1L, MoveX = 0.123f, MoveZ = -0.456f, AimPointX = 2.718f, AimPointY = 1f, AimPointZ = 0.5f, Buttons = 0u });

            byte[] a = PacketCodec.Encode(PacketType.Input, msg);
            byte[] b = PacketCodec.Encode(PacketType.Input, msg);
            Assert.Equal(a, b);
        }

        [Fact]
        public void 信封_空包与未知类型丢弃()
        {
            Assert.False(PacketCodec.TryDecode(new ArraySegment<byte>(new byte[0]), out _, out _));
            Assert.False(PacketCodec.TryDecode(new ArraySegment<byte>(new byte[] { 0 }), out _, out _));      // None——噪声过滤
            Assert.False(PacketCodec.TryDecode(new ArraySegment<byte>(new byte[] { 99, 1, 2 }), out _, out _)); // 未定义类型
        }

        [Fact]
        public void 信封_字段中截断丢弃_整字段截断按proto3兼容解析()
        {
            byte[] packet = PacketCodec.Encode(PacketType.Join, new Proto.JoinRequest { RoomId = "room-x", Token = "tok", BuildHash = "h" });

            // 字段中间截断（EOF mid-field）→ proto 解析失败 → 丢弃不抛
            var midCut = new byte[packet.Length - 1];
            Array.Copy(packet, midCut, midCut.Length);
            Assert.False(PacketCodec.TryDecode(new ArraySegment<byte>(midCut), out _, out _));

            // 整字段截断（末字段 3 字节恰好整体吞掉）→ proto3 前向兼容：缺失尾部字段按默认值解析成功——
            // 这是协议演进容错特性，不是坏包（修订原"截断必失败"的错误先验）
            var fieldCut = new byte[packet.Length - 3];
            Array.Copy(packet, fieldCut, fieldCut.Length);
            Assert.True(PacketCodec.TryDecode(new ArraySegment<byte>(fieldCut), out var type, out var msg));
            Assert.Equal(PacketType.Join, type);
            var join = Assert.IsType<Proto.JoinRequest>(msg);
            Assert.Equal("room-x", join.RoomId);
            Assert.Equal("tok", join.Token);
            Assert.Equal("", join.BuildHash); // 缺失字段 = 默认值
        }

        [Fact]
        public void 信封_带偏移的分段解码()
        {
            byte[] packet = PacketCodec.Encode(PacketType.Leave, new Proto.Leave());
            var padded = new byte[5 + packet.Length + 3];
            Array.Copy(packet, 0, padded, 5, packet.Length);
            var segment = new ArraySegment<byte>(padded, 5, packet.Length); // ArraySegment 偏移语义——kcp2k 收包形态

            Assert.True(PacketCodec.TryDecode(segment, out var type, out _));
            Assert.Equal(PacketType.Leave, type);
        }
    }
}
