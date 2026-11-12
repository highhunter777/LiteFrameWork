using System;
using System.Collections.Generic;

namespace LiteGameNext.Platform.Config
{
    /// <summary>准心表现参数（来自 `tbcrosshair`）：静态满张/连发张开/张开上限/后坐。</summary>
    public readonly struct CrosshairFeel
    {
        public CrosshairFeel(int id, float staticMaxScale, float burstGrowth, float burstCap, float kick)
        {
            Id = id;
            StaticMaxScale = staticMaxScale;
            BurstGrowth = burstGrowth;
            BurstCap = burstCap;
            Kick = kick;
        }

        public int Id { get; }
        public float StaticMaxScale { get; }
        public float BurstGrowth { get; }
        public float BurstCap { get; }
        public float Kick { get; }

        /// <summary>缺表时的中性值（准心不放大、不后坐）。</summary>
        public static CrosshairFeel Neutral => new CrosshairFeel(0, 1f, 0f, 1f, 0f);
    }

    /// <summary>`tbcrosshair` → 准心参数表（按武器 id）；缺表回落中性值。</summary>
    public static class CrosshairTableProjection
    {
        /// <summary>投影成 id→参数字典；表为空返回空字典（调用方回落 Neutral）。</summary>
        public static Dictionary<int, CrosshairFeel> Project(cfg.Tables tables)
        {
            var result = new Dictionary<int, CrosshairFeel>();
            if (tables?.Tbcrosshair == null) return result;
            foreach (cfg.crosshair row in tables.Tbcrosshair.DataList)
            {
                if (row == null) continue;
                result[row.Id] = new CrosshairFeel(row.Id, row.StaticMaxScale, row.BurstGrowth,
                    row.BurstCap, row.Kick);
            }
            return result;
        }

        /// <summary>按武器 id 取参数；缺 id 返回中性值。</summary>
        public static CrosshairFeel For(IReadOnlyDictionary<int, CrosshairFeel> map, int weaponId)
            => map != null && map.TryGetValue(weaponId, out CrosshairFeel feel) ? feel : CrosshairFeel.Neutral;
    }
}
