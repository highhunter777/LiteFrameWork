using System;
using LiteSim;

namespace LiteGame.Modules.Config
{
    /// <summary>
    /// 判定几何参数覆写（`#combatgeom` 13 列的调参面）：只记"被改动过的列"，合并时按列重建
    /// <see cref="CombatGeometry"/>，未覆写的字段原样取源值（<see cref="CombatValues"/> 其余字段一律不动）。
    ///
    /// **实例面**：不读任何静态、不落盘。写入方是调参面板（<c>SimParamsDebugTunables</c>）；
    /// 是否允许合并由进房单点按 <see cref="SimParamsGate"/> 每局求值一次后决定
    /// （<see cref="SimParamsActivation"/>；联机/云端恒不合并）；
    /// 落表是编辑器侧 <c>SimParamsExporter</c> 的职责（表才是权威值，覆写只是试验面）。
    /// **校验口径**与 `scripts/codegen/_combatgeom_row.py` / <c>SimConfigMapper.Validate</c> 一致（宁严勿松）：
    /// 非法列名/非有限值/越界当场抛，不留下"能合并但两端都不认"的数值。
    /// </summary>
    public sealed class SimParamsOverrides
    {
        // ---- 列名单源（顺序 = `#combatgeom` 列序 = CombatGeometry 构造参数序）----

        public const string BodyRadiusColumn = "body_radius";
        public const string HitscanHeightColumn = "hitscan_height";
        public const string HitscanRadiusColumn = "hitscan_radius";
        public const string HeadRatioColumn = "head_ratio";
        public const string HeadRadiusColumn = "head_radius";
        public const string MuzzleForwardColumn = "muzzle_fwd";
        public const string MuzzleRightColumn = "muzzle_right";
        public const string MuzzleHeightColumn = "muzzle_height";
        public const string HeadshotDamageShiftColumn = "headshot_damage_shift";
        public const string HipSpreadFactorColumn = "hip_spread_factor";
        public const string CorpseFramesColumn = "corpse_frames";
        public const string HardMaxSpeedColumn = "hard_max_speed";
        public const string FaceTurnRadPerSecColumn = "face_turn_rad_per_sec";

        /// <summary>13 列名（调参面板/导出器/校验共用）。只读约定：调用方不得改写元素。</summary>
        public static readonly string[] Columns =
        {
            BodyRadiusColumn, HitscanHeightColumn, HitscanRadiusColumn,
            HeadRatioColumn, HeadRadiusColumn,
            MuzzleForwardColumn, MuzzleRightColumn, MuzzleHeightColumn,
            HeadshotDamageShiftColumn, HipSpreadFactorColumn,
            CorpseFramesColumn, HardMaxSpeedColumn, FaceTurnRadPerSecColumn,
        };

        /// <summary>列数（与 <see cref="Columns"/> 等长；L1 用例卡漂移）。</summary>
        public const int ColumnCount = 13;

        private readonly float?[] _values = new float?[ColumnCount];

        /// <summary>已覆写的列数（0 = 无覆写，合并即原值）。</summary>
        public int Count
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _values.Length; i++)
                    if (_values[i].HasValue) count++;
                return count;
            }
        }

        /// <summary>是否有任何列被覆写。</summary>
        public bool HasAny => Count > 0;

        /// <summary>写一列覆写（先校验后落值——校验失败不留半合法状态；未知列名抛）。</summary>
        public void Set(string column, float value)
        {
            int index = RequireIndex(column);
            Validate(column, value);
            _values[index] = value;
        }

        /// <summary>清一列覆写（未知列名抛；未覆写 = no-op）。</summary>
        public void ClearOne(string column) => _values[RequireIndex(column)] = null;

        /// <summary>清全部覆写（回到"无覆写 = 合并即原值"）。</summary>
        public void Clear()
        {
            for (int i = 0; i < _values.Length; i++) _values[i] = null;
        }

        /// <summary>读一列覆写（未覆写返回 false；未知列名抛）。</summary>
        public bool TryGet(string column, out float value)
        {
            float? stored = _values[RequireIndex(column)];
            value = stored ?? 0f;
            return stored.HasValue;
        }

        /// <summary>
        /// 把覆写叠到源值上：只重建 <see cref="CombatValues.Geometry"/>，其余字段（含测试规则位）原样保留。
        /// 返回 true = 至少一列被覆写；false = 无覆写（<paramref name="result"/> 与源值逐字段相同）。
        /// </summary>
        public bool TryApply(in CombatValues source, out CombatValues result)
        {
            CombatGeometry geometry = source.Geometry;
            result = source;
            result.Geometry = new CombatGeometry(
                Value(BodyRadiusColumn, geometry.BodyRadius),
                Value(HitscanHeightColumn, geometry.HitscanHeight),
                Value(HitscanRadiusColumn, geometry.HitscanRadius),
                Value(HeadRatioColumn, geometry.HeadRatio),
                Value(HeadRadiusColumn, geometry.HeadRadius),
                Value(MuzzleForwardColumn, geometry.MuzzleForward),
                Value(MuzzleRightColumn, geometry.MuzzleRight),
                Value(MuzzleHeightColumn, geometry.MuzzleHeight),
                IntegerValue(HeadshotDamageShiftColumn, geometry.HeadshotDamageShift),
                Value(HipSpreadFactorColumn, geometry.HipSpreadFactor),
                IntegerValue(CorpseFramesColumn, geometry.CorpseFrames),
                Value(HardMaxSpeedColumn, geometry.HardMaxSpeed),
                Value(FaceTurnRadPerSecColumn, geometry.FaceTurnRadPerSec));
            return HasAny;
        }

        private float Value(string column, float source)
        {
            float? stored = _values[IndexOf(column)];
            return stored ?? source;
        }

        private int IntegerValue(string column, int source)
        {
            float? stored = _values[IndexOf(column)];
            return stored.HasValue ? (int)stored.Value : source;
        }

        private static int IndexOf(string column)
        {
            for (int i = 0; i < Columns.Length; i++)
                if (string.Equals(Columns[i], column, StringComparison.Ordinal)) return i;
            return -1;
        }

        private static int RequireIndex(string column)
        {
            int index = IndexOf(column);
            if (index < 0)
                throw new ArgumentException(
                    "未知判定参数列：" + (column ?? "(null)") + "（合法列见 SimParamsOverrides.Columns）", nameof(column));
            return index;
        }

        /// <summary>值域校验（调用方已过 <see cref="RequireIndex"/>，此处 column 必为合法列名）。</summary>
        private static void Validate(string column, float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, column + " 必须为有限数值");

            switch (column)
            {
                case HeadshotDamageShiftColumn:
                case CorpseFramesColumn:
                    if (value != MathF.Truncate(value))
                        throw new ArgumentException(column + " 为 int 列，不接受小数：" + value, nameof(value));
                    if (value < 0f)
                        throw new ArgumentOutOfRangeException(nameof(value), value, column + " 必须为非负整数");
                    return;
                case HeadRatioColumn:
                    if (value <= 0f || value > 1f)
                        throw new ArgumentOutOfRangeException(nameof(value), value, column + " 必须落在 (0,1]");
                    return;
                case BodyRadiusColumn:
                case HitscanHeightColumn:
                case HitscanRadiusColumn:
                case HeadRadiusColumn:
                case HipSpreadFactorColumn:
                case HardMaxSpeedColumn:
                case FaceTurnRadPerSecColumn:
                    if (value <= 0f)
                        throw new ArgumentOutOfRangeException(nameof(value), value, column + " 必须为正有限数值");
                    return;
                default:
                    return;   // muzzle_fwd/muzzle_right/muzzle_height：表源只要求有限，允许负值
            }
        }
    }
}
