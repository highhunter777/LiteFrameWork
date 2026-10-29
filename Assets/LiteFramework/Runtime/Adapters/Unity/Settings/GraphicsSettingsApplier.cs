using System;
using UnityEngine;

namespace LiteFramework
{
    /// <summary>
    /// 图形设置生效器：**画质预设**（低/中/高 ↔ 平台档位按名匹配、缺档按序号钳位）。
    ///
    /// 覆盖项（渲染缩放/抗锯齿/阴影）走"影子管线资产"，其克隆与还原属阶段 B（设计 §4.2）；
    /// 本类当前只负责预设档，保证与既有行为逐位一致。
    /// </summary>
    public sealed class GraphicsSettingsApplier
    {
        /// <summary>UI 画质档数（低/中/高）；<c>GameSettings.Quality</c> 值域 0..3，UI 只暴露 0..2。</summary>
        public const int UiQualityTiers = 3;

        /// <summary>UI 档 → 平台档位名（缺同名档位时按序号钳位，不发明画质等级）。</summary>
        private static readonly string[] TierNames = { "Low", "Medium", "High" };

        /// <summary>UI 档（0..2，越界钳制）。</summary>
        public int Quality(GameSettings settings) => Mathf.Clamp(settings.Quality.Get(), 0, UiQualityTiers - 1);

        public void SetQuality(GameSettings settings, int tier)
        {
            settings.Quality.Set(Mathf.Clamp(tier, 0, UiQualityTiers - 1));
            Apply(settings);
        }

        /// <summary>应用画质预设（幂等：<c>SetQualityLevel</c> 对同档重复调用不改变状态）。</summary>
        public void Apply(GameSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            int tier = Quality(settings);
            int target = tier;
            string[] names = QualitySettings.names;
            if (names != null && names.Length > 0)
            {
                int found = Array.FindIndex(names, n => string.Equals(n, TierNames[tier], StringComparison.OrdinalIgnoreCase));
                target = found >= 0 ? found : Mathf.Clamp(tier, 0, names.Length - 1);   // 无同名档位：按序号钳位
            }
            QualitySettings.SetQualityLevel(target, applyExpensiveChanges: true);
        }
    }
}
