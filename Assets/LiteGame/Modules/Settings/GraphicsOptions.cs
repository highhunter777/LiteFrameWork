using System;
using System.Collections.Generic;
using LiteFramework;

namespace LiteGame
{
    /// <summary>
    /// 图形覆盖档（图形设置）：渲染缩放 / 抗锯齿 / 阴影三组允许值 + 阴影档参数，单源。
    ///
    /// **为什么是"覆盖"而不是独立档位**：画质预设（低/中/高）已由三套 URP 资产承载整体观感；
    /// 覆盖项只处理"某一项特别吃性能"的个例，<see cref="FollowPreset"/> 表示不覆盖（零开销）。
    ///
    /// **阴影只走两个公开标量**（URP 14 的 `shadowDistance` / `shadowCascadeCount`）：阴影开关与
    /// 阴影贴图分辨率是 `internal set`，用反射改会把表现档绑在包实现细节上。
    /// </summary>
    public static class GraphicsOptions
    {
        /// <summary>跟随画质预设（不做任何覆盖）。</summary>
        public const int FollowPreset = -1;

        private static readonly int[] RenderScaleValues = { FollowPreset, 70, 85, 100 };
        private static readonly int[] AntiAliasingValues = { FollowPreset, 0, 2, 4 };
        private static readonly int[] ShadowValues = { FollowPreset, 0, 1, 2 };

        /// <summary>阴影档参数（索引 0/1/2＝低/中/高）：距离与级联数。</summary>
        private static readonly float[] ShadowDistances = { 25f, 50f, 80f };
        private static readonly int[] ShadowCascadeCounts = { 1, 2, 4 };

        public static IReadOnlyList<int> RenderScaleOptions => RenderScaleValues;
        public static IReadOnlyList<int> AntiAliasingOptions => AntiAliasingValues;
        public static IReadOnlyList<int> ShadowOptions => ShadowValues;

        public static bool IsRenderScale(int value) => Array.IndexOf(RenderScaleValues, value) >= 0;
        public static bool IsAntiAliasing(int value) => Array.IndexOf(AntiAliasingValues, value) >= 0;
        public static bool IsShadow(int value) => Array.IndexOf(ShadowValues, value) >= 0;

        /// <summary>非法值一律回"跟随预设"（覆盖项不做猜档：猜错会改画面观感）。</summary>
        public static int SnapRenderScale(int value) => IsRenderScale(value) ? value : FollowPreset;
        public static int SnapAntiAliasing(int value) => IsAntiAliasing(value) ? value : FollowPreset;
        public static int SnapShadow(int value) => IsShadow(value) ? value : FollowPreset;

        /// <summary>阴影档 → 阴影距离（米）；非阴影档返回 0（调用方只在生效时取）。</summary>
        public static float ShadowDistanceOf(int level)
            => level >= 0 && level < ShadowDistances.Length ? ShadowDistances[level] : 0f;

        /// <summary>阴影档 → 级联数；非阴影档返回 1。</summary>
        public static int ShadowCascadeCountOf(int level)
            => level >= 0 && level < ShadowCascadeCounts.Length ? ShadowCascadeCounts[level] : 1;
    }
}
