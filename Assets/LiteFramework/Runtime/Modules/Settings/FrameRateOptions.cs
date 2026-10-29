using System;
using System.Collections.Generic;

namespace LiteFramework
{
    /// <summary>
    /// 帧率上限档（显示设置）：**平台可选档 + 默认值 + 引擎目标值换算**单源。
    /// 桌面按显示器常见刷新率给档，移动按"射击 60 帧"的既定口径给两档（与
    /// <see cref="AppLifetime.MobileTargetFrameRate"/> 的历史口径一致——那个常量改为委托本类，避免两处默认值）。
    /// 取值语义：<see cref="Unlimited"/> 表示不设上限（引擎目标值 -1）。
    /// </summary>
    public static class FrameRateOptions
    {
        /// <summary>无限制（引擎侧换算为 -1＝不设 targetFrameRate）。</summary>
        public const int Unlimited = 0;

        /// <summary>移动端既定档：60 帧。</summary>
        public const int MobileDefault = 60;

        private static readonly int[] DesktopOptions = { Unlimited, 30, 60, 90, 120, 144 };
        private static readonly int[] MobileOptions = { 30, MobileDefault };

        /// <summary>当前平台可选档（顺序即 UI 下拉顺序；只读，调用方不得改）。</summary>
        public static IReadOnlyList<int> For(bool isMobile) => isMobile ? MobileOptions : DesktopOptions;

        /// <summary>平台默认档（首启与"恢复默认"同源）。</summary>
        public static int DefaultFor(bool isMobile) => isMobile ? MobileDefault : Unlimited;

        /// <summary>值是否在当前平台档位内（门面写入前的第二道闸）。</summary>
        public static bool IsValid(int cap, bool isMobile) => Array.IndexOf(isMobile ? MobileOptions : DesktopOptions, cap) >= 0;

        /// <summary>非法值收敛到"最接近的合法档"，都不接近则回默认（不发明平台不支持的帧率）。</summary>
        public static int Snap(int cap, bool isMobile)
        {
            IReadOnlyList<int> options = For(isMobile);
            if (IsValid(cap, isMobile)) return cap;
            int best = options[0];
            int bestDistance = int.MaxValue;
            for (int i = 0; i < options.Count; i++)
            {
                int distance = Math.Abs(options[i] - cap);
                if (distance < bestDistance) { best = options[i]; bestDistance = distance; }
            }
            return best;
        }

        /// <summary>设置值 → 引擎目标帧率（0/负数 → -1＝不设上限；与 <c>Application.targetFrameRate</c> 语义对齐）。</summary>
        public static int ToAppTarget(int cap) => cap <= Unlimited ? -1 : cap;
    }
}
