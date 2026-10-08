using System;
using System.Collections.Generic;
using System.IO;
using LiteClient;
using LiteSim;
using Luban;
using RoomServer;
using RoomServer.Runtime;
using Xunit;

namespace LiteNet.Tests
{
    public sealed class ConfigurationSourceTests
    {
        [Fact]
        public void 源表职责_战斗表不再存重力与出生生命()
        {
            Assert.Null(typeof(cfg.combatnum).GetField("Gravity"));
            Assert.Null(typeof(cfg.combatnum).GetField("EntityHp"));
            var movement = new cfg.Tbmovementconfig(new ByteBuf(Read("tbmovementconfig")));
            var entity = new cfg.Tbentityconfig(new ByteBuf(Read("tbentityconfig")));
            Assert.Equal(MovementValues.Default.Gravity, movement.Get(1).Gravity);
            Assert.Equal(EntityValues.Default.InitialHp, entity.Get(1).InitialHp);
        }

        [Fact]
        public void 真实表映射_数值与默认摘要保持一致()
        {
            CombatValues values = Parse(Read("tbmovementconfig"), Read("tbentityconfig"));
            var previous = new CombatValues(5f, -20f, 100f, 25, 1, 100);
            Assert.Equal(CombatConfigDigest.CanonicalText(previous), CombatConfigDigest.CanonicalText(values));
            Assert.Equal(CombatConfigDigest.Compute(previous), CombatConfigDigest.Compute(values));
        }

        [Fact]
        public void 移动表改重力_直达权威积分与摘要()
        {
            CombatValues values = Parse(MovementBytes(-9.8f), Read("tbentityconfig"));
            Assert.Equal(-9.8f, values.Gravity);
            Assert.NotEqual(CombatConfigDigest.Compute(CombatValues.Default), CombatConfigDigest.Compute(values));

            var world = new SimWorldState();
            long id = world.Spawn(new EntitySlot { Hp = values.EntityHp, Pos = new SimVector3(0f, 10f, 0f) }, out _);
            Assert.True(world.TryResolve(id, out int slot));
            MovementSystem.Run(world.Entities, world.AliveBitmap, SimMapData.StandardBattleMap(), values);
            Assert.Equal(-9.8f * SimConfig.Dt, world.Entities[slot].Vel.Y);
        }

        [Fact]
        public void 实体表改生命_直达房间出生与固定快照()
        {
            CombatValues values = Parse(Read("tbmovementconfig"), EntityBytes(1, 240));
            var room = new RoomRuntime(new RoomConfig { RoomId = "EntitySource", ExpectedPlayers = 1, Seed = 9 }, values);
            room.Execute(RoomCommand.Join(1), new List<RoomOutput>());
            Assert.True(room.AuthSim.TryResolve(room.EntityIdOf(0), out int slot));
            Assert.Equal(240, room.AuthSim.Entities[slot].Hp);
            Assert.Equal(240, room.FixedConfig.Values.EntityHp);
            Assert.Equal(CombatConfigDigest.Compute(values), room.FixedConfig.Digest);
            Assert.NotEqual(CombatConfigDigest.Compute(CombatValues.Default), room.FixedConfig.Digest);
        }

        [Theory]
        [InlineData("tbcombatnum")]
        [InlineData("tbmovementconfig")]
        [InlineData("tbentityconfig")]
        public void 必要来源缺行_拒绝默认兜底(string emptyTable)
        {
            byte[] combat = emptyTable == "tbcombatnum" ? new byte[] { 0 } : Read("tbcombatnum");
            byte[] movement = emptyTable == "tbmovementconfig" ? new byte[] { 0 } : Read("tbmovementconfig");
            byte[] entity = emptyTable == "tbentityconfig" ? new byte[] { 0 } : Read("tbentityconfig");
            var ex = Assert.Throws<InvalidDataException>(() => CombatNumbers.Parse(combat, movement, entity));
            Assert.Contains(emptyTable, ex.Message);
        }

        [Fact]
        public void 实体表缺默认定义_共用校验与服务端同样拒绝()
        {
            var combat = new cfg.Tbcombatnum(new ByteBuf(Read("tbcombatnum")));
            var movement = new cfg.Tbmovementconfig(new ByteBuf(Read("tbmovementconfig")));
            var entity = new cfg.Tbentityconfig(new ByteBuf(EntityBytes(2, 100)));
            Assert.Contains("tbentityconfig", SimConfigMapper.Validate(combat, movement, entity));
            Assert.Throws<InvalidDataException>(() => SimConfigMapper.BuildCombatValues(combat, movement, entity));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void 出生生命非法_拒绝装载(int hp)
        {
            var ex = Assert.Throws<InvalidDataException>(() => Parse(Read("tbmovementconfig"), EntityBytes(1, hp)));
            Assert.Contains("initial_hp", ex.Message);
        }

        [Theory]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        [InlineData(float.NegativeInfinity)]
        public void 重力非有限值_拒绝装载(float gravity)
        {
            var ex = Assert.Throws<InvalidDataException>(() => Parse(MovementBytes(gravity), Read("tbentityconfig")));
            Assert.Contains("gravity", ex.Message);
        }

        [Fact]
        public void 实体表截断_拒绝装载()
        {
            byte[] bytes = Read("tbentityconfig");
            Array.Resize(ref bytes, bytes.Length - 1);
            Assert.ThrowsAny<Exception>(() => Parse(Read("tbmovementconfig"), bytes));
        }

        private static CombatValues Parse(byte[] movement, byte[] entity)
            => CombatNumbers.Parse(Read("tbcombatnum"), movement, entity).ToValues();

        private static byte[] EntityBytes(int id, int hp)
        {
            var buf = new ByteBuf();
            buf.WriteSize(1);
            buf.WriteInt(id);
            buf.WriteInt(hp);
            return buf.CopyData();
        }

        private static byte[] MovementBytes(float gravity)
        {
            byte[] bytes = Read("tbmovementconfig");
            var reader = new ByteBuf(bytes);
            Assert.Equal(1, reader.ReadSize());
            Assert.Equal(1, reader.ReadInt());
            // 定位真实表行中的第十个 float（gravity），其他字段保持原始字节。
            for (int i = 0; i < 9; i++) reader.ReadFloat();
            var writer = new ByteBuf(bytes, 0, reader.ReaderIndex);
            writer.WriteFloat(gravity);
            return bytes;
        }

        private static byte[] Read(string table)
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "Tests", "Tests.slnx")))
                    return File.ReadAllBytes(Path.Combine(dir.FullName, CombatNumbers.RelativeDir, table + ".bytes"));
            throw new DirectoryNotFoundException("找不到仓库根");
        }
    }
}
