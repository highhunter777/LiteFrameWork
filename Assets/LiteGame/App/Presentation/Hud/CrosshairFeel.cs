using System;
using System.Collections.Generic;
using LiteClient;

namespace LiteGame
{
    /// <summary>
    /// 准心手感参数（tbcrosshair 表行的运行时投影——纯 View 表现域：不进判定面摘要、不进 BuildHash
    /// 闭包，单端热更不换联机身份）：静态满张倍率 + 连发临时张开（每发增量/总上限）+ 每发后坐冲量。
    /// 行键 = 武器定义 Id（每把武器一行，行完整性由 ConfigService.ValidateCandidate 钉）。
    /// <see cref="Default"/> 为**表缺行/无表装配的防御回落**（与表 id=0 行同值——EditMode 表守卫
    /// 钉两者一致，同 <c>WeaponConfig.DefaultRifle</c> 先例：错表炸装载，运行期回落不猜）。
    /// </summary>
    public readonly struct CrosshairFeel
    {
        /// <summary>静态满张开倍率：有效散布角达满张角（<see cref="BattleCrosshairDriver.SpreadFullDegrees"/>）
        /// 时的准心缩放（基线 1 = 不张开；角度→倍率线性钳制）。</summary>
        public readonly float StaticMaxScale;

        /// <summary>连发每发张开增量（倍率/发）——本地 Fire 帧事件逐发累加，停火按回正速率指数衰减。</summary>
        public readonly float BurstGrowth;

        /// <summary>连发张开总上限倍率（静态 + 连发增量之和的钳制——连发只张开不收紧）。</summary>
        public readonly float BurstCap;

        /// <summary>每发后坐冲量（画布像素——准心上跳后按回弹速率指数回正）。</summary>
        public readonly float Kick;

        public CrosshairFeel(float staticMaxScale, float burstGrowth, float burstCap, float kick)
        {
            StaticMaxScale = staticMaxScale;
            BurstGrowth = burstGrowth;
            BurstCap = burstCap;
            Kick = kick;
        }

        /// <summary>防御回落（表缺行/无表装配）：与 tbcrosshair id=0 行同值——表守卫用例卡漂移。</summary>
        public static CrosshairFeel Default { get; } = new CrosshairFeel(1.6f, 0.05f, 2f, 4f);
    }

    /// <summary>
    /// 准心手感投影（装配层投影件——UIFormCatalog/LTextCatalog 同族）：<c>cfg.Tables.Tbcrosshair</c> 行
    /// → 武器定义 Id → <see cref="CrosshairFeel"/>。**惰性投影**（MapProvider 同口径：装配期表未装载，
    /// 首次 <see cref="TryGet"/> 才读 <see cref="IConfigService.Tables"/>——未装载即访问由其抛）。
    /// 缺行 → false（调用方回落 <see cref="CrosshairFeel.Default"/>）；行值域合法性闸在
    /// ConfigService.ValidateCandidate（错表炸 Preload，不在此重验）。
    /// </summary>
    public sealed class CrosshairFeelBook
    {
        private readonly IConfigService _config;                  // 生产形态（与 _direct 二选一）
        private readonly Dictionary<int, CrosshairFeel> _direct;  // 直构形态（EditMode 用例——免表装载）
        private Dictionary<int, CrosshairFeel> _projected;       // 惰性投影缓存（生产形态首用建）

        public CrosshairFeelBook(IConfigService config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public CrosshairFeelBook(Dictionary<int, CrosshairFeel> rows)
        {
            _direct = rows ?? throw new ArgumentNullException(nameof(rows));
        }

        /// <summary>按武器定义 Id 取手感（缺行 → false，调用方回落 <see cref="CrosshairFeel.Default"/>）。</summary>
        public bool TryGet(int weaponDefId, out CrosshairFeel feel)
        {
            Dictionary<int, CrosshairFeel> rows = _direct ?? (_projected ??= Project());
            return rows.TryGetValue(weaponDefId, out feel);
        }

        private Dictionary<int, CrosshairFeel> Project()
        {
            var rows = new Dictionary<int, CrosshairFeel>();
            foreach (cfg.crosshair row in _config.Tables.Tbcrosshair.DataList)
                rows[row.Id] = new CrosshairFeel(row.StaticMaxScale, row.BurstGrowth, row.BurstCap, row.Kick);
            return rows;
        }
    }

    /// <summary>
    /// 准心手感的**实时调参面**（<c>GripTuning</c> 同族——GM 面板「准心调参」滑杆实时覆写，不落盘、
    /// 不进任何判定/快照面）：覆写优先于表值/机制常量，复位（&lt; 0）即回表值。**仅开发三宏内存在**
    /// （release 随宏编译剥离、恒用表值/常量）；实机定档后把拖定值写回 tbcrosshair 表源与
    /// <see cref="BattleCrosshairDriver"/> 机制常量。
    /// </summary>
    public static class CrosshairTuning
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
        /// <summary>静态满张倍率覆写（&lt; 0 = 不覆写，用表值）。</summary>
        public static float StaticMaxDevOverride = -1f;

        /// <summary>连发每发张开增量覆写（&lt; 0 = 不覆写）。</summary>
        public static float BurstGrowthDevOverride = -1f;

        /// <summary>连发张开总上限覆写（&lt; 0 = 不覆写）。</summary>
        public static float BurstCapDevOverride = -1f;

        /// <summary>每发后坐冲量覆写（&lt; 0 = 不覆写）。</summary>
        public static float KickDevOverride = -1f;

        /// <summary>连发张开回正速率覆写（&lt; 0 = 不覆写，用机制常量）。</summary>
        public static float BurstRecoverDevOverride = -1f;

        /// <summary>后坐回弹速率覆写（&lt; 0 = 不覆写）。</summary>
        public static float KickRecoverDevOverride = -1f;

        /// <summary>解析静态满张倍率（覆写优先，否则表值）。</summary>
        public static float StaticMaxOf(float tableValue)
            => StaticMaxDevOverride >= 0f ? StaticMaxDevOverride : tableValue;

        /// <summary>解析连发每发增量（覆写优先，否则表值）。</summary>
        public static float BurstGrowthOf(float tableValue)
            => BurstGrowthDevOverride >= 0f ? BurstGrowthDevOverride : tableValue;

        /// <summary>解析连发总上限（覆写优先，否则表值）。</summary>
        public static float BurstCapOf(float tableValue)
            => BurstCapDevOverride >= 0f ? BurstCapDevOverride : tableValue;

        /// <summary>解析后坐冲量（覆写优先，否则表值）。</summary>
        public static float KickOf(float tableValue)
            => KickDevOverride >= 0f ? KickDevOverride : tableValue;

        /// <summary>解析连发回正速率（覆写优先，否则机制常量）。</summary>
        public static float BurstRecoverOf(float constValue)
            => BurstRecoverDevOverride >= 0f ? BurstRecoverDevOverride : constValue;

        /// <summary>解析后坐回弹速率（覆写优先，否则机制常量）。</summary>
        public static float KickRecoverOf(float constValue)
            => KickRecoverDevOverride >= 0f ? KickRecoverDevOverride : constValue;
#else
        /// <summary>解析静态满张倍率（release：恒表值——覆写面随三宏编译剥离）。</summary>
        public static float StaticMaxOf(float tableValue) => tableValue;

        /// <summary>解析连发每发增量（release：恒表值）。</summary>
        public static float BurstGrowthOf(float tableValue) => tableValue;

        /// <summary>解析连发总上限（release：恒表值）。</summary>
        public static float BurstCapOf(float tableValue) => tableValue;

        /// <summary>解析后坐冲量（release：恒表值）。</summary>
        public static float KickOf(float tableValue) => tableValue;

        /// <summary>解析连发回正速率（release：恒机制常量）。</summary>
        public static float BurstRecoverOf(float constValue) => constValue;

        /// <summary>解析后坐回弹速率（release：恒机制常量）。</summary>
        public static float KickRecoverOf(float constValue) => constValue;
#endif
    }
}
