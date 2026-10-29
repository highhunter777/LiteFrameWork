#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using System.Collections.Generic;
using LiteClient;
using LiteSim;
using LiteView.Animation;

namespace LiteGame
{
    /// <summary>
    /// 现有手写调参面的统一描述（开发三宏内）：爆头区 / 持枪幅度 / 准心 / 视角。
    /// 默认值来自烘焙/表/机制常量，覆写复位统一回源值；编辑器「调试参数」页按描述渲染，
    /// 不再为每组写专用滑杆代码。准心/视角默认值仍写在代码常量里（定档后迁表/配置），
    /// 标 <see cref="DebugTunableScope.Product"/>；爆头区/持枪默认值已由烘焙产物提供，覆写按调试维度归类。
    /// 当前对局规则经 <see cref="IDevSession.MatchRules"/> 读（实例状态，无 Sim 侧静态"当前局"）。
    /// </summary>
    public sealed class FeatureDebugTunables : IDebugTunableProvider
    {
        private readonly IDevSession _dev;

        public FeatureDebugTunables(IDevSession dev = null) => _dev = dev;

        public void CollectDebugTunables(ICollection<DebugTunable> into)
        {
            AddHead(into);
            AddGrip(into);
            AddCrosshair(into);
            AddLook(into);
        }

        // ---- 爆头区（判定面沙箱件；活动局 + 跨局待用值双落）----

        public float CurrentHeadLine()
        {
            var match = _dev?.MatchRules;
            if (match != null) return LiteSim.MatchTestRules.ResolveHeadHitLine(match);
            return TestModeRuntime.Current.HeadHitLineOverride >= 0f
                ? TestModeRuntime.Current.HeadHitLineOverride
                : LiteSim.CombatConfig.HeadHitLine;
        }

        public float CurrentHeadRadius()
        {
            var match = _dev?.MatchRules;
            if (match != null) return LiteSim.MatchTestRules.ResolveHeadshotRadius(match);
            return TestModeRuntime.Current.HeadshotRadiusOverride >= 0f
                ? TestModeRuntime.Current.HeadshotRadiusOverride
                : LiteSim.CombatConfig.HeadshotRadius;
        }

        public void SetHead(float line, float radius)
        {
            TestModeRuntime.Current.HeadHitLineOverride = line;
            TestModeRuntime.Current.HeadshotRadiusOverride = radius;
            var match = _dev?.MatchRules;
            if (match != null)
            {
                match.HeadHitLineOverride = line;
                match.HeadshotRadiusOverride = radius;
            }
        }

        private void AddHead(ICollection<DebugTunable> into)
        {
            into.Add(new DebugTunable
            {
                Id = "head.line",
                Group = "爆头区",
                Label = "爆头线下沿（m）",
                Kind = DebugTunableKind.Float,
                Scope = DebugTunableScope.Sandbox,
                Default = LiteSim.CombatConfig.HeadHitLine,
                Min = 0.9f,
                Max = LiteSim.CombatConfig.HitscanHeight,
                Decimals = 3,
                RuntimeVisible = true,
                Get = CurrentHeadLine,
                Set = value => SetHead(value, CurrentHeadRadius()),
                Reset = () => SetHead(-1f, -1f),
            });
            into.Add(new DebugTunable
            {
                Id = "head.radius",
                Group = "爆头区",
                Label = "爆头柱半径（m）",
                Kind = DebugTunableKind.Float,
                Scope = DebugTunableScope.Sandbox,
                Default = LiteSim.CombatConfig.HeadshotRadius,
                Min = 0.15f,
                Max = 0.45f,
                Get = CurrentHeadRadius,
                Set = value => SetHead(CurrentHeadLine(), value),
                Reset = () => SetHead(-1f, -1f),
            });
        }

        // ---- 持枪幅度（纯表现）----

        private static void AddGrip(ICollection<DebugTunable> into)
        {
            into.Add(new DebugTunable
            {
                Id = "grip.torso",
                Group = "持枪幅度",
                Label = "换弹期跟随强度",
                Kind = DebugTunableKind.Float,
                Scope = DebugTunableScope.Presentation,
                Default = GripBake.TorsoWeight,
                Min = 0f,
                Max = 1f,
                Decimals = 2,
                RuntimeVisible = true,
                Get = () => GripTuning.TorsoWeightLive,
                Set = value => GripTuning.TorsoWeightDevOverride = value,
                Reset = () => GripTuning.TorsoWeightDevOverride = -1f,
            });
        }

        // ---- 准心（纯表现；六个参数共享同一运行时覆写面）----

        private static void AddCrosshair(ICollection<DebugTunable> into)
        {
            CrosshairFeel d = CrosshairFeel.Default;
            AddCrosshair(into, "crosshair.static", "静态满张倍率",
                d.StaticMaxScale, 1f, 3f,
                () => CrosshairTuning.StaticMaxOf(d.StaticMaxScale),
                value => CrosshairTuning.StaticMaxDevOverride = value,
                () => CrosshairTuning.StaticMaxDevOverride = -1f);
            AddCrosshair(into, "crosshair.growth", "连发每发张开增量",
                d.BurstGrowth, 0f, 0.5f,
                () => CrosshairTuning.BurstGrowthOf(d.BurstGrowth),
                value => CrosshairTuning.BurstGrowthDevOverride = value,
                () => CrosshairTuning.BurstGrowthDevOverride = -1f);
            AddCrosshair(into, "crosshair.cap", "连发张开总上限",
                d.BurstCap, 1f, 3.5f,
                () => CrosshairTuning.BurstCapOf(d.BurstCap),
                value => CrosshairTuning.BurstCapDevOverride = value,
                () => CrosshairTuning.BurstCapDevOverride = -1f);
            AddCrosshair(into, "crosshair.kick", "每发后坐冲量（px）",
                d.Kick, 0f, 30f,
                () => CrosshairTuning.KickOf(d.Kick),
                value => CrosshairTuning.KickDevOverride = value,
                () => CrosshairTuning.KickDevOverride = -1f);
            AddCrosshair(into, "crosshair.burst-recover", "连发回正速率（1/s）",
                BattleCrosshairDriver.BurstRecoverPerSecond, 0.5f, 12f,
                () => CrosshairTuning.BurstRecoverOf(BattleCrosshairDriver.BurstRecoverPerSecond),
                value => CrosshairTuning.BurstRecoverDevOverride = value,
                () => CrosshairTuning.BurstRecoverDevOverride = -1f);
            AddCrosshair(into, "crosshair.kick-recover", "后坐回弹速率（1/s）",
                BattleCrosshairDriver.KickRecoverPerSecond, 2f, 30f,
                () => CrosshairTuning.KickRecoverOf(BattleCrosshairDriver.KickRecoverPerSecond),
                value => CrosshairTuning.KickRecoverDevOverride = value,
                () => CrosshairTuning.KickRecoverDevOverride = -1f);
        }

        private static void AddCrosshair(ICollection<DebugTunable> into, string id, string label,
            float defaultValue, float min, float max,
            System.Func<float> get, System.Action<float> set, System.Action reset)
        {
            into.Add(new DebugTunable
            {
                Id = id,
                Group = "准心参数",
                Label = label,
                Kind = DebugTunableKind.Float,
                Scope = DebugTunableScope.Product,
                Default = defaultValue,
                Min = min,
                Max = max,
                Decimals = 2,
                Get = get,
                Set = set,
                Reset = reset,
            });
        }

        // ---- 视角（灵敏度/ADS 收敛；FOV 是场景 rig 值，保留在 GM 页直改）----

        private static void AddLook(ICollection<DebugTunable> into)
        {
            into.Add(new DebugTunable
            {
                Id = "look.sensitivity",
                Group = "视角参数",
                Label = "视角灵敏度（rad/px）",
                Kind = DebugTunableKind.Float,
                Scope = DebugTunableScope.Product,
                Default = BattleLookController.LookSensitivityRadPerPixel,
                Min = 0.0005f,
                Max = 0.008f,
                Decimals = 4,
                Get = () => LookTuning.SensitivityOf(BattleLookController.LookSensitivityRadPerPixel),
                Set = value => LookTuning.SensitivityDevOverride = value,
                Reset = () => LookTuning.SensitivityDevOverride = -1f,
            });
            into.Add(new DebugTunable
            {
                Id = "look.ads-scale",
                Group = "视角参数",
                Label = "ADS 灵敏度收敛比",
                Kind = DebugTunableKind.Float,
                Scope = DebugTunableScope.Product,
                Default = BattleLookController.AdsLookSensitivityScale,
                Min = 0.1f,
                Max = 1f,
                Get = () => LookTuning.AdsScaleOf(BattleLookController.AdsLookSensitivityScale),
                Set = value => LookTuning.AdsScaleDevOverride = value,
                Reset = () => LookTuning.AdsScaleDevOverride = -1f,
            });
        }
    }
}
#endif
