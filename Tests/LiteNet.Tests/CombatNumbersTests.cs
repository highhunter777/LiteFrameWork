using System;
using System.IO;
using System.Text.Json;
using LiteSim;
using RoomServer;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// 玩法数值链路守卫（《玩法数值解耦审查与Luban表设计》§4 批②④，2026-09-19）：
    ///
    /// 1. **表 ↔ 代码默认值一致**：`CombatConfig` 的兜底值必须等于表值——否则"表没生成/没装载"时会
    ///    静默跑默认值，两端在同一份表下算出不同结果（隐形的行为分叉，最难查）。
    /// 2. **服务端解析**（`CombatNumbers.Parse`）：正常 / 缺字段 / 坏格式 → 抛（fail-fast，不静默兜底）。
    /// 3. **回填生效**：`LoadFrom` 后消费点读到新值（改表即生效，无需改代码）。
    ///
    /// 注：修改 `CombatConfig` 静态面会波及其它用例 → 本类禁止并行 + 用 finally 还原。
    /// </summary>
    [Collection("CombatConfigStatic")]
    public sealed class CombatNumbersTests
    {
        private const string TableRelativePath = "RoomServer/Data/tbcombatnum.json";

        [Fact]
        public void 表文件存在且与代码默认值一致()
        {
            string path = Path.Combine(RepoRoot(), TableRelativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path),
                $"缺数值表产物：{TableRelativePath}（跑 Luban/gen.bat Pass 1b——数值缺失等于两端分叉）");

            CombatNumValues v = CombatNumbers.Parse(File.ReadAllText(path));

            // 表 = 唯一真相；代码默认值只是"表不可用时的兜底"——两者必须一致（漂移即 L1 红）
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
        public void 解析_缺字段_抛()
        {
            string json = "[{\"id\":1,\"move_speed\":5}]";     // 缺 gravity 等
            var ex = Assert.Throws<InvalidDataException>(() => CombatNumbers.Parse(json));
            Assert.Contains("gravity", ex.Message);
        }

        [Fact]
        public void 解析_空数组或非数组_抛()
        {
            Assert.Throws<InvalidDataException>(() => CombatNumbers.Parse("[]"));
            Assert.Throws<InvalidDataException>(() => CombatNumbers.Parse("{}"));
        }

        [Fact]
        public void 缺文件装载_抛()
        {
            string missing = Path.Combine(RepoRoot(), "RoomServer", "Data", "__not_exist__.json");
            Assert.Throws<FileNotFoundException>(() => CombatNumbers.Load(missing));
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
