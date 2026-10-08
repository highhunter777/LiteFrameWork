using System;
using System.Collections.Generic;
using System.IO;
using LiteSim;
using RoomServer;
using RoomServer.Runtime;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// 玩法数值链路守卫（《玩法数值与Luban配置专项设计》）：
    ///
    /// 1. **表 → 默认值一致**：`CombatValues.Default` 的兜底值必须等于表值——否则表没生成/没装载时
    ///    两端在同一份表下算出不同结果（隐形的行为分叉，最难查）。
    /// 2. **服务端解析**（`CombatNumbers.Parse`，bytes 形态）：正常 / 截断 / 缺行 → 抛（fail-fast，
    ///    不静默兜底）。
    /// 3. **装载产出实例**：`LoadTableBytes` 返回的表行经 `ToValues()` 产出 <see cref="CombatValues"/>
    ///    实例（技术债 #1：不再回填全局——实例由装配方显式传递，`HostAssembly.Inputs` 必填卡"丢弃返回值"）。
    ///
    /// 注：**本类不触碰任何全局面**（技术债 #1 根治后无串行集/还原需求——数值一律经实例传递）。
    /// </summary>
    public sealed class CombatNumbersTests
    {
        private const string TableRelativePath = "Assets/GameData/Config/tbcombatnum.bytes";

        [Fact]
        public void 表文件存在且与代码默认值一致()
        {
            string path = Path.Combine(RepoRoot(), TableRelativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path),
                $"缺数值表产物：{TableRelativePath}（跑 Luban/gen.bat Pass 1——数值缺失等于两端分叉）");

            CombatNumValues v = CombatNumbers.Parse(File.ReadAllBytes(path));

            // 表 = 唯一真相；默认实例只是表不可用时的兜底——两者必须一致（漂移即 L1 红）
            Assert.Equal(CombatValues.Default.MoveSpeed, v.MoveSpeed);
            Assert.Equal(CombatValues.Default.Gravity, v.Gravity);
            Assert.Equal(CombatValues.Default.HitscanRange, v.HitscanRange);
            // 身位半径/高度不属表——烘焙常量（CombatConfig.HitscanRadius/HitscanHeight = BodyBake，
            // 单源 = prefab CharacterController；表列已退役）
            Assert.Equal(CombatValues.Default.BaseDamage, v.BaseDamage);
            Assert.Equal(CombatValues.Default.DamageSpread, v.DamageSpread);
            Assert.Equal(CombatValues.Default.EntityHp, v.EntityHp);
            Assert.Equal(CombatNumbers.SingleRowId, v.Id);
        }

        [Fact]
        public void 客户端bin产物存在()
        {
            string bin = Path.Combine(RepoRoot(), "Assets", "GameData", "Config", "tbcombatnum.bytes");
            Assert.True(File.Exists(bin), "缺客户端数值 bin（gen.bat Pass 1）——ConfigService 预取会直接抛");
        }

        [Fact]
        public void 解析_截断字节_抛()
        {
            // 真实表字节截半——ByteBuf 越界读必须抛（格式对不上就不能装载数值，fail-fast）
            string bin = Path.Combine(RepoRoot(), "Assets", "GameData", "Config", "tbcombatnum.bytes");
            byte[] full = File.ReadAllBytes(bin);
            var truncated = new byte[full.Length / 2];
            Array.Copy(full, truncated, truncated.Length);
            Assert.ThrowsAny<Exception>(() => CombatNumbers.Parse(truncated));
        }

        [Fact]
        public void 解析_空行数抛()
        {
            // 全零字节：表头 id 解不出 1 行 → 抛（ByteBuf.ReadSize 读到 0 → 空表 → 缺行）
            Assert.ThrowsAny<Exception>(() => CombatNumbers.Parse(new byte[16]));
        }

        [Fact]
        public void 缺目录装载_抛()
        {
            string missing = Path.Combine(RepoRoot(), "RoomServer", "Data", "__not_exist__");
            Assert.ThrowsAny<Exception>(() => CombatNumbers.LoadTableBytes(missing));
        }

        /// <summary>
        /// 装载链路钉：装载与传递是同一契约的两半——`LoadTableBytes` 返回的表行经 `ToValues()` 产出的
        /// 实例必须等于表值（服务端装配经 `HostAssembly.Inputs.CombatValues` 必填传入；缺此钉则
        /// "装载产物被丢弃"会表现为跑默认值而握手照常通过——表数据进 buildHash 两端同变）。
        /// 与用例①合围：本用例卡"装载产物 = 表值"，用例①卡"默认值 = 表值"。
        /// </summary>
        [Fact]
        public void 装载产出实例_字段与表值一致()
        {
            string dir = Path.Combine(RepoRoot(), "Assets", "GameData", "Config");
            CombatNumValues v = CombatNumbers.LoadTableBytes(dir).Combat;
            CombatValues values = v.ToValues();

            Assert.Equal(v.MoveSpeed, values.MoveSpeed);
            Assert.Equal(v.Gravity, values.Gravity);
            Assert.Equal(v.HitscanRange, values.HitscanRange);
            Assert.Equal(v.BaseDamage, values.BaseDamage);
            Assert.Equal(v.DamageSpread, values.DamageSpread);
            Assert.Equal(v.EntityHp, values.EntityHp);
        }

        /// <summary>实例直传：自定义数值实例经参数直达房间（技术债 #1——无全局装载、无还原面；
        /// 机制级证明见 `LiteSim.Core.Tests/ValuesParameterizationTests`）。</summary>
        [Fact]
        public void 自定义实例经参数直达房间_无全局面参与()
        {
            var custom = new CombatValues(7.5f, -9.8f, 50f, 40, 0, 130);
            var room = new RoomRuntime(new RoomConfig { RoomId = "CfgInst", ExpectedPlayers = 1, Seed = 9 }, custom);
            var outputs = new List<RoomOutput>();
            room.Execute(RoomCommand.Join(1), outputs);

            Assert.True(room.AuthSim.TryResolve(room.EntityIdOf(0), out int slot));
            Assert.Equal(130, room.AuthSim.Entities[slot].Hp);                          // 出生 HP = 实例值（≠ 默认 100）
            Assert.Equal(CombatConfigDigest.Compute(custom), room.FixedConfig.Digest);  // 摘要按实例计算
        }

        private static string RepoRoot()
        {
            for (var c = new DirectoryInfo(AppContext.BaseDirectory); c != null; c = c.Parent)
                if (Directory.Exists(Path.Combine(c.FullName, "Tests"))
                    && File.Exists(Path.Combine(c.FullName, "Tests", "Tests.slnx")))
                    return c.FullName;
            throw new DirectoryNotFoundException("找不到仓库根（需要 Tests/Tests.slnx）");
        }
    }

    /// <summary>触碰 CombatConfig 全局静态的用例集：禁用并行（同 CoreStatic 先例）。</summary>
    [CollectionDefinition("CombatConfigStatic", DisableParallelization = true)]
    public sealed class CombatConfigStaticCollection { }
}
