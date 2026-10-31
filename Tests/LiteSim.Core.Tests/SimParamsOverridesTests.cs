using LiteGame.Modules.Config;      // SimParamsOverrides/SimParamsGate：源链接纳入 L1（链接组注释见 LiteSim.Core.Tests.csproj）
using LiteSim;
using Xunit;

namespace LiteSim.Tests
{
    /// <summary>
    /// 判定参数覆写面用例（`#combatgeom` 13 列）：单列覆写只动目标字段、校验口径与表源同口径、
    /// 清空回到源值、求值门只放行"本地测试模式 + 本地传输"。被测面是实例状态（无静态污染）。
    /// </summary>
    public sealed class SimParamsOverridesTests
    {
        [Fact]
        public void 列名单源_13列且与几何构造序一致()
        {
            Assert.Equal(SimParamsOverrides.ColumnCount, SimParamsOverrides.Columns.Length);
            Assert.Equal(13, SimParamsOverrides.Columns.Length);
        }

        [Theory]
        [InlineData(SimParamsOverrides.BodyRadiusColumn, 0.55f)]
        [InlineData(SimParamsOverrides.HitscanHeightColumn, 2.05f)]
        [InlineData(SimParamsOverrides.HitscanRadiusColumn, 0.62f)]
        [InlineData(SimParamsOverrides.HeadRatioColumn, 0.72f)]
        [InlineData(SimParamsOverrides.HeadRadiusColumn, 0.22f)]
        [InlineData(SimParamsOverrides.MuzzleForwardColumn, -0.35f)]
        [InlineData(SimParamsOverrides.MuzzleRightColumn, 0.21f)]
        [InlineData(SimParamsOverrides.MuzzleHeightColumn, 1.05f)]
        [InlineData(SimParamsOverrides.HeadshotDamageShiftColumn, 3f)]
        [InlineData(SimParamsOverrides.HipSpreadFactorColumn, 2.75f)]
        [InlineData(SimParamsOverrides.CorpseFramesColumn, 42f)]
        [InlineData(SimParamsOverrides.HardMaxSpeedColumn, 18.5f)]
        [InlineData(SimParamsOverrides.FaceTurnRadPerSecColumn, 9.25f)]
        public void 单列覆写_只改目标字段其余12项等于源值(string column, float value)
        {
            CombatValues source = Source();
            var overrides = new SimParamsOverrides();
            overrides.Set(column, value);

            Assert.True(overrides.TryApply(source, out CombatValues result));
            Assert.Null(FirstDiff(source.Geometry, result.Geometry, column));
            Assert.Equal(value, Field(result.Geometry, column));

            // CombatValues 其余字段（含测试规则位）不动
            Assert.Equal(source.MoveSpeed, result.MoveSpeed);
            Assert.Equal(source.Gravity, result.Gravity);
            Assert.Equal(source.HitscanRange, result.HitscanRange);
            Assert.Equal(source.BaseDamage, result.BaseDamage);
            Assert.Equal(source.DamageSpread, result.DamageSpread);
            Assert.Equal(source.EntityHp, result.EntityHp);
            Assert.Equal(source.AimMoveSpeed, result.AimMoveSpeed);
            Assert.Equal(source.FireStanceFrames, result.FireStanceFrames);
            Assert.False(result.NoDeath);
            Assert.False(result.InfiniteAmmo);
        }

        [Fact]
        public void 未知列_抛ArgumentException()
        {
            var overrides = new SimParamsOverrides();
            Assert.Throws<System.ArgumentException>(() => overrides.Set("no_such_column", 1f));
            Assert.Throws<System.ArgumentException>(() => overrides.ClearOne("no_such_column"));
            Assert.Throws<System.ArgumentException>(() => overrides.TryGet("no_such_column", out _));
        }

        [Fact]
        public void 非有限值与越界_抛ArgumentOutOfRangeException()
        {
            var overrides = new SimParamsOverrides();
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => overrides.Set(SimParamsOverrides.BodyRadiusColumn, float.NaN));
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => overrides.Set(SimParamsOverrides.HeadRatioColumn, 1.5f));            // (0,1]
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => overrides.Set(SimParamsOverrides.HardMaxSpeedColumn, 0f));           // >0
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => overrides.Set(SimParamsOverrides.CorpseFramesColumn, -1f));          // >=0
            Assert.Throws<System.ArgumentException>(
                () => overrides.Set(SimParamsOverrides.CorpseFramesColumn, 1.5f));         // int 列不收小数
        }

        [Fact]
        public void Clear复原_合并即源值()
        {
            CombatValues source = Source();
            var overrides = new SimParamsOverrides();
            overrides.Set(SimParamsOverrides.BodyRadiusColumn, 0.6f);
            overrides.Set(SimParamsOverrides.CorpseFramesColumn, 33f);
            Assert.Equal(2, overrides.Count);

            overrides.ClearOne(SimParamsOverrides.CorpseFramesColumn);
            Assert.Equal(1, overrides.Count);
            Assert.False(overrides.TryGet(SimParamsOverrides.CorpseFramesColumn, out _));

            overrides.Clear();
            Assert.Equal(0, overrides.Count);
            Assert.False(overrides.HasAny);
            Assert.False(overrides.TryApply(source, out CombatValues result));
            Assert.Null(FirstDiff(source.Geometry, result.Geometry, null));
        }

        [Theory]
        [InlineData(true, true, true)]
        [InlineData(true, false, false)]
        [InlineData(false, true, false)]
        [InlineData(false, false, false)]
        public void 门真值表_仅本地测试模式与本地传输双真放行(bool testMode, bool localTransport, bool expected)
            => Assert.Equal(expected, SimParamsGate.Evaluate(testMode, localTransport));

        // ---- 进房时点的激活解析（T84：两端同值的唯一求值点）----

        [Fact]
        public void 激活解析_门关时结果逐字段等于表值()
        {
            CombatValues table = Source();
            var overrides = new SimParamsOverrides();
            overrides.Set(SimParamsOverrides.BodyRadiusColumn, 0.9f);

            CombatValues active = SimParamsActivation.Resolve(applyOverrides: false, table, overrides);

            Assert.Null(FirstDiff(table.Geometry, active.Geometry, null));
            Assert.Equal(table.MoveSpeed, active.MoveSpeed);
        }

        [Fact]
        public void 激活解析_门开但无覆写时结果逐字段等于表值()
        {
            CombatValues table = Source();
            var overrides = new SimParamsOverrides();

            CombatValues active = SimParamsActivation.Resolve(applyOverrides: true, table, overrides);

            Assert.Null(FirstDiff(table.Geometry, active.Geometry, null));
        }

        [Fact]
        public void 激活解析_覆写实例缺省时按无覆写处理()
        {
            CombatValues table = Source();

            CombatValues active = SimParamsActivation.Resolve(applyOverrides: true, table, overrides: null);

            Assert.Null(FirstDiff(table.Geometry, active.Geometry, null));
        }

        [Fact]
        public void 激活解析_门开且有覆写时只改目标列_其余12列与规则外字段不动()
        {
            CombatValues table = Source();
            var overrides = new SimParamsOverrides();
            overrides.Set(SimParamsOverrides.HitscanRadiusColumn, 0.61f);

            CombatValues active = SimParamsActivation.Resolve(applyOverrides: true, table, overrides);

            Assert.Equal(0.61f, active.Geometry.HitscanRadius);
            Assert.Null(FirstDiff(table.Geometry, active.Geometry, SimParamsOverrides.HitscanRadiusColumn));
            Assert.Equal(table.MoveSpeed, active.MoveSpeed);
            Assert.Equal(table.Gravity, active.Gravity);
            Assert.Equal(table.NoDeath, active.NoDeath);
            Assert.Equal(table.InfiniteAmmo, active.InfiniteAmmo);
        }

        // ---- 辅助：源值与逐字段比对 ----

        private static CombatValues Source()
        {
            var geometry = new CombatGeometry(
                0.31f, 1.75f, 0.42f, 0.8f, 0.14f,
                0.7f, 0.08f, 1.28f,
                2, 4.5f, 150, 24f, 11f);
            return new CombatValues(5f, -9.8f, 100f, 25, 1, 100, 2.5f, 60, geometry);
        }

        private static float Field(CombatGeometry geometry, string column)
        {
            switch (column)
            {
                case SimParamsOverrides.BodyRadiusColumn: return geometry.BodyRadius;
                case SimParamsOverrides.HitscanHeightColumn: return geometry.HitscanHeight;
                case SimParamsOverrides.HitscanRadiusColumn: return geometry.HitscanRadius;
                case SimParamsOverrides.HeadRatioColumn: return geometry.HeadRatio;
                case SimParamsOverrides.HeadRadiusColumn: return geometry.HeadRadius;
                case SimParamsOverrides.MuzzleForwardColumn: return geometry.MuzzleForward;
                case SimParamsOverrides.MuzzleRightColumn: return geometry.MuzzleRight;
                case SimParamsOverrides.MuzzleHeightColumn: return geometry.MuzzleHeight;
                case SimParamsOverrides.HeadshotDamageShiftColumn: return geometry.HeadshotDamageShift;
                case SimParamsOverrides.HipSpreadFactorColumn: return geometry.HipSpreadFactor;
                case SimParamsOverrides.CorpseFramesColumn: return geometry.CorpseFrames;
                case SimParamsOverrides.HardMaxSpeedColumn: return geometry.HardMaxSpeed;
                case SimParamsOverrides.FaceTurnRadPerSecColumn: return geometry.FaceTurnRadPerSec;
                default: throw new System.ArgumentException("未知列：" + column);
            }
        }

        /// <summary>返回首个与 <paramref name="a"/> 不同的列名（<paramref name="expected"/> = 允许不同的列）。</summary>
        private static string FirstDiff(CombatGeometry a, CombatGeometry b, string expected)
        {
            foreach (string column in SimParamsOverrides.Columns)
            {
                if (column == expected) continue;
                if (Field(a, column) != Field(b, column)) return column;
            }
            return null;
        }
    }
}
