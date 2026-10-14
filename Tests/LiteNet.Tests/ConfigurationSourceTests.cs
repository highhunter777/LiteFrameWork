using System;
using System.Collections.Generic;
using System.IO;
using LiteClient;
using LiteSim;
using LiteSim.Tests;
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
        public void 真实表映射_数值与默认实例一致()
        {
            CombatValues values = Parse(Read("tbmovementconfig"), Read("tbentityconfig"));
            var expected = new CombatValues(5f, -20f, 100f, 25, 1, 100, 2.5f, 60);
            Assert.Equal(expected.MoveSpeed, values.MoveSpeed);
            Assert.Equal(expected.Gravity, values.Gravity);
            Assert.Equal(expected.HitscanRange, values.HitscanRange);
            Assert.Equal(expected.BaseDamage, values.BaseDamage);
            Assert.Equal(expected.DamageSpread, values.DamageSpread);
            Assert.Equal(expected.EntityHp, values.EntityHp);
            Assert.Equal(expected.AimMoveSpeed, values.AimMoveSpeed);
            Assert.Equal(expected.FireStanceFrames, values.FireStanceFrames);
        }

        [Fact]
        public void 移动表改重力_直达权威积分与摘要()
        {
            CombatValues values = Parse(MovementBytes(-9.8f), Read("tbentityconfig"));
            Assert.Equal(-9.8f, values.Gravity);
            // 批 E：身份按**表字节**算——改重力字节即摘要变（不再是值投影进摘要）
            uint original = CombatConfigDigest.ComputeBase(
                new[] { new CombatConfigDigest.Entry("Movement/tbmovementconfig", Read("tbmovementconfig")) });
            uint changed = CombatConfigDigest.ComputeBase(
                new[] { new CombatConfigDigest.Entry("Movement/tbmovementconfig", MovementBytes(-9.8f)) });
            Assert.NotEqual(original, changed);

            var world = new SimWorldState();
            long id = world.Spawn(new EntitySlot { Hp = values.EntityHp, Pos = new SimVector3(0f, 10f, 0f) }, out _);
            Assert.True(world.TryResolve(id, out int slot));
            MovementSystem.Run(world.Entities, world.AliveBitmap, TestMaps.Greybox(), values);
            Assert.Equal(-9.8f * SimConfig.Dt, world.Entities[slot].Vel.Y);
        }

        [Fact]
        public void 实体表改生命_直达房间出生与固定快照()
        {
            CombatValues values = Parse(Read("tbmovementconfig"), EntityBytes(1, 240));
            uint baseDigest = CombatConfigDigest.ComputeBase(
                new[] { new CombatConfigDigest.Entry("Entity/tbentityconfig", EntityBytes(1, 240)) });
            var room = new RoomRuntime(new RoomConfig { RoomId = "EntitySource", ExpectedPlayers = 1, Seed = 9 }, values,
                gameplayDigestBase: baseDigest);
            room.Execute(RoomCommand.Join(1), new List<RoomOutput>());
            Assert.True(room.AuthSim.TryResolve(room.EntityIdOf(0), out int slot));
            Assert.Equal(240, room.AuthSim.Entities[slot].Hp);
            Assert.Equal(240, room.FixedConfig.Values.EntityHp);
            // 房间身份 = 装载基摘要 + 本局规则初值（规则位由 values 盖章——此处常规房 = false/false）
            Assert.Equal(CombatConfigDigest.WithTestRules(baseDigest, false, false), room.FixedConfig.Digest);
        }

        /// <summary>
        /// **批 E 核心不变量**：判定面表字节基摘要两端取数同源——
        /// 服务端盘上直读（`ServerTableLoader`）与客户端装载缓存形（`ConfigService` 同形缓存）算出同一摘要；
        /// 判定表改一字节 → 变；表现/内容表（`tbanimationprofile`——用户裁决"也要可热更"）改一字节 → 不变。
        /// </summary>
        [Fact]
        public void 判定面表字节基摘要_两端同源_判定表敏感_表现表不敏感()
        {
            string dir = Path.Combine(RepoRoot(), ServerTableLoader.RelativeDir);
            ServerTableLoad load = ServerTableLoader.LoadTableBytes(dir);      // 服务端取数（盘上直读）
            Dictionary<string, byte[]> cache = ReadCache(dir);                 // 客户端取数（裸表名 → 字节）

            Assert.Equal(TableLayout.ComputeGameplayDigestBase(cache), load.Digest);

            // 判定表（Combat/tbweapon）改一字节 → 身份变
            Dictionary<string, byte[]> weaponTouched = ReadCache(dir);
            weaponTouched["tbweapon"][0] ^= 0xFF;
            Assert.NotEqual(load.Digest, TableLayout.ComputeGameplayDigestBase(weaponTouched));

            // 表现面表（Animation/tbanimationprofile 与 UI/tbltext）改字节 → 身份不动（单端热更不换联机身份）
            Dictionary<string, byte[]> presentationTouched = ReadCache(dir);
            presentationTouched["tbanimationprofile"][0] ^= 0xFF;
            presentationTouched["tbltext"][0] ^= 0xFF;
            Assert.Equal(load.Digest, TableLayout.ComputeGameplayDigestBase(presentationTouched));
        }

        /// <summary>判定面清单 = 表清单按域过滤（无漏无多）——"新增表落域即入面"的守卫，
        /// 禁第二份人工清单漂移（修改指南 §1.2 单源纪律）。</summary>
        [Fact]
        public void 判定面清单_按域过滤派生_域内无表即红()
        {
            var expected = new List<string>();
            foreach (string file in TableLayout.Files)
            {
                string domain = file.Substring(0, file.IndexOf('/'));
                foreach (string judgment in TableLayout.JudgmentDomains)
                    if (domain == judgment) { expected.Add(file); break; }
            }
            Assert.Equal(expected, TableLayout.JudgmentFiles);

            foreach (string domain in TableLayout.JudgmentDomains)
            {
                bool any = false;
                foreach (string file in TableLayout.Files)
                    if (file.StartsWith(domain + "/", StringComparison.Ordinal)) { any = true; break; }
                Assert.True(any, $"判定面域 '{domain}' 在 Files 里没有任何表——域名单源漂移");
            }
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
            var ex = Assert.Throws<InvalidDataException>(() => ServerTableLoader.Parse(combat, movement, entity));
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
            => ServerTableLoader.Parse(Read("tbcombatnum"), movement, entity).ToValues();

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
            // 领域相对路径单源 = TableLayout.Files（表名 → 领域子目录），与 ServerTableLoader/ConfigService 同源。
            foreach (string entry in TableLayout.Files)
            {
                if (TableLayout.TableName(entry) != table) continue;
                return File.ReadAllBytes(Path.Combine(RepoRoot(), ServerTableLoader.RelativeDir,
                    entry.Replace('/', Path.DirectorySeparatorChar) + ".bytes"));
            }
            throw new FileNotFoundException($"TableLayout 无此表：{table}");
        }

        /// <summary>仓库根定位（与 ServerTableLoader/测试侧同款标记：Assets + Tests/Tests.slnx）。</summary>
        private static string RepoRoot()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "Tests", "Tests.slnx")))
                    return dir.FullName;
            throw new DirectoryNotFoundException("找不到仓库根");
        }

        /// <summary>装载缓存形（裸表名 → 字节）——与客户端 `ConfigService.LoadAsync` 预取缓存同形（可改字节后复算）。</summary>
        private static Dictionary<string, byte[]> ReadCache(string configDir)
        {
            var cache = new Dictionary<string, byte[]>();
            foreach (string entry in TableLayout.Files)
                cache[TableLayout.TableName(entry)] =
                    File.ReadAllBytes(Path.Combine(configDir, entry.Replace('/', Path.DirectorySeparatorChar) + ".bytes"));
            return cache;
        }
    }
}
