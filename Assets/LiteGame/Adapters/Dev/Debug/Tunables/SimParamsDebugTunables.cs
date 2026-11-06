#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using System.Collections.Generic;
using LiteGame.Modules.Config;
using LiteSim;

namespace LiteGame
{
    /// <summary>
    /// 判定参数调参面（`#combatgeom` 13 列）：把 SimParamsOverrides 实例暴露成滑杆 （Group = 判定参数）。
    /// </summary>
    public sealed class SimParamsDebugTunables : IDebugTunableProvider
    {
        private const string GroupName = "判定参数";

        private readonly SimParamsOverrides _overrides;

        public SimParamsDebugTunables(SimParamsOverrides overrides) => _overrides = overrides;

        public void CollectDebugTunables(ICollection<DebugTunable> into)
        {
            if (_overrides == null || into == null) return;   // 无覆写实例：本组不注册

            CombatGeometry geometry = CombatConfig.Geometry;   // 只读展示：已发布几何（未发布 = Default 投影）

            Add(into, SimParamsOverrides.BodyRadiusColumn, "身位半径（m）",
                geometry.BodyRadius, 0.05f, 2f, 3);
            Add(into, SimParamsOverrides.HitscanHeightColumn, "命中柱高（m）",
                geometry.HitscanHeight, 0.5f, 3f, 3);
            Add(into, SimParamsOverrides.HitscanRadiusColumn, "命中柱半径（m）",
                geometry.HitscanRadius, 0.05f, 2f, 3);
            Add(into, SimParamsOverrides.HeadRatioColumn, "爆头带下沿比例",
                geometry.HeadRatio, 0.01f, 1f, 3);
            Add(into, SimParamsOverrides.HeadRadiusColumn, "爆头柱半径（m）",
                geometry.HeadRadius, 0.02f, 1f, 3);
            Add(into, SimParamsOverrides.MuzzleForwardColumn, "枪口前向偏移（m）",
                geometry.MuzzleForward, -2f, 2f, 3);
            Add(into, SimParamsOverrides.MuzzleRightColumn, "枪口右向偏移（m）",
                geometry.MuzzleRight, -2f, 2f, 3);
            Add(into, SimParamsOverrides.MuzzleHeightColumn, "枪口高度（m）",
                geometry.MuzzleHeight, -1f, 3f, 3);
            Add(into, SimParamsOverrides.HeadshotDamageShiftColumn, "爆头伤害偏移（档）",
                geometry.HeadshotDamageShift, 0f, 10f, 0);
            Add(into, SimParamsOverrides.HipSpreadFactorColumn, "腰射散布倍率",
                geometry.HipSpreadFactor, 0.1f, 10f, 3);
            Add(into, SimParamsOverrides.CorpseFramesColumn, "尸体保留帧数",
                geometry.CorpseFrames, 0f, 600f, 0);
            Add(into, SimParamsOverrides.HardMaxSpeedColumn, "硬上限速度（m/s）",
                geometry.HardMaxSpeed, 1f, 100f, 2);
            Add(into, SimParamsOverrides.FaceTurnRadPerSecColumn, "转身角速度（rad/s）",
                geometry.FaceTurnRadPerSec, 0.1f, 60f, 2);
        }

        private void Add(ICollection<DebugTunable> into, string column, string label, float defaultValue,
            float min, float max, int decimals)
        {
            into.Add(new DebugTunable
            {
                Id = "simparam." + column,
                Group = GroupName,
                Label = label,
                Kind = DebugTunableKind.Float,
                // 沙箱参数：调试期有效、不落盘、不进摘要/重放（覆写只服务本地测试模式的调参试验）。
                Scope = DebugTunableScope.Sandbox,
                Default = defaultValue,
                Min = min,
                Max = max,
                Decimals = decimals,
                RuntimeVisible = true,
                Get = () => Current(column, defaultValue),
                Set = value => _overrides.Set(column, value),
                Reset = () => _overrides.ClearOne(column),
            });
        }

        private float Current(string column, float fallback)
            => _overrides.TryGet(column, out float value) ? value : fallback;
    }
}
#endif
