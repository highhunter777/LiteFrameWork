using System;
using System.IO;
using LiteSim;
using RoomServer;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// 玩法数值链路守卫（《玩法数值解耦审查与Luban表设计》②）：
    ///
    /// 1. **表 → 代码默认值一致**：`CombatConfig` 的兜底值必须等于表值——否则表没生成/没装载时会
    ///    静默跑默认值，两端在同一份表下算出不同结果（隐形的行为分叉，最难查）。
    /// 2. **服务端解析**（`CombatNumbers.Parse`，bytes 形态）：正常 / 截断 / 缺行 → 抛（fail-fast，
    ///    不静默兜底）。
    /// 3. **回填生效**：`LoadFrom` 后消费点读到新值（改表即生效，无需改代码）。
    ///
    /// 注：修改 `CombatConfig` 静态面会影响其它用例 → 本类禁并行 + 用 finally 还原。
    /// </summary>
    [Collection("CombatConfigStatic")]
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

            // 表 = 唯一真相；代码默认值只是表不可用时的兜底——两者必须一致（漂移即 L1 红）
            Assert.Equal(CombatConfig.MoveSpeed, v.MoveSpeed);
            Assert.Equal(CombatConfig.Gravity, v.Gravity);
            Assert.Equal(CombatConfig.HitscanRange, v.HitscanRange);
            Assert.Equal(CombatConfig.HitscanRadius, v.HitscanRadius);
            Assert.Equal(CombatConfig.HitscanHeight, v.HitscanHeight);
            Assert.Equal(CombatConfig.BaseDamage, v.BaseDamage);
            Assert.Equal(CombatConfig.DamageSpread, v.DamageSpread);
            Assert.Equal(CombatConfig.EntityHp, v.EntityHp);
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
        /// 装载链路钉：装载与回填是同一契约的两半——`LoadTableBytes` 返回后运行面必须等于表值。
        /// 缺此钉则"装载返回值被丢弃、不回填"的服务端会跑硬编码默认值而握手照常通过
        /// （表数据进 buildHash 两端同变），一旦改表即客户端用表值/服务端用默认值的静默分叉。
        /// 与用例①合围：本用例卡"运行面 = 表值"，用例①卡"默认值 = 表值"。
        /// </summary>
        [Fact]
        public void 装载即回填_运行面读到表值()
        {
            string dir = Path.Combine(RepoRoot(), "Assets", "GameData", "Config");
            CombatNumValues v = CombatNumbers.LoadTableBytes(dir);

            Assert.Equal(v.MoveSpeed, CombatConfig.MoveSpeed);
            Assert.Equal(v.Gravity, CombatConfig.Gravity);
            Assert.Equal(v.HitscanRange, CombatConfig.HitscanRange);
            Assert.Equal(v.HitscanRadius, CombatConfig.HitscanRadius);
            Assert.Equal(v.HitscanHeight, CombatConfig.HitscanHeight);
            Assert.Equal(v.BaseDamage, CombatConfig.BaseDamage);
            Assert.Equal(v.DamageSpread, CombatConfig.DamageSpread);
            Assert.Equal(v.EntityHp, CombatConfig.EntityHp);
        }

        /// <summary>回填生效：改表值 → 消费点（Sim 系统读的静态面）立即变；finally 还原避免污染其它用例。</summary>
        [Fact]
        public void 回填生效_消费点读到表值()
        {
            float oldMove = CombatConfig.MoveSpeed;
            int oldHp = CombatConfig.EntityHp;
            try
            {
                new CombatNumValues
                {
                    MoveSpeed = 7.5f,
                    Gravity = -9.8f,
                    HitscanRange = 50f,
                    HitscanRadius = 0.25f,
                    HitscanHeight = 1.5f,
                    BaseDamage = 40,
                    DamageSpread = 0,
                    EntityHp = 130,
                }.Apply();

                Assert.Equal(7.5f, CombatConfig.MoveSpeed);
                Assert.Equal(-9.8f, CombatConfig.Gravity);
                Assert.Equal(40, CombatConfig.BaseDamage);
                Assert.Equal(0, CombatConfig.DamageSpread);
                Assert.Equal(130, CombatConfig.EntityHp);
            }
            finally
            {
                CombatConfig.LoadFrom(5f, -20f, 100f, 0.5f, 2f, 25, 1, 100);   // 与表值同源的兜底
                Assert.Equal(oldMove, CombatConfig.MoveSpeed);
                Assert.Equal(oldHp, CombatConfig.EntityHp);
            }
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
