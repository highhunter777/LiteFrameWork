using System.Collections.Generic;

namespace LiteSim.View
{
    /// <summary>超预算策略（《VFX服务实施指导》§1 决策 7）。</summary>
    public enum VfxOverflowPolicy
    {
        /// <summary>回收最旧的活跃特效（默认——观感优先，不丢新表现）。</summary>
        RecycleOldest,
        /// <summary>拒绝新特效并计数（预算优先）。</summary>
        Reject,
    }

    /// <summary>
    /// 特效预算（《VFX服务实施指导》§1 决策 7/8）：同屏上限 + 低端类别跳过。
    /// 跨特效优先级/抢占/互斥**留位不建**（出现真实需求再加，见 §2.5）。
    /// </summary>
    public sealed class VfxBudget
    {
        public readonly int MaxActive;
        public readonly VfxOverflowPolicy Overflow;

        private readonly HashSet<string> _skipped;

        public VfxBudget(int maxActive, VfxOverflowPolicy overflow = VfxOverflowPolicy.RecycleOldest,
                         IEnumerable<string> skippedCategories = null)
        {
            MaxActive = maxActive < 1 ? 1 : maxActive;
            Overflow = overflow;
            _skipped = skippedCategories != null ? new HashSet<string>(skippedCategories) : new HashSet<string>();
        }

        /// <summary>该类别的特效是否被降级跳过（低端机）。</summary>
        public bool IsCategorySkipped(string category)
            => !string.IsNullOrEmpty(category) && _skipped.Contains(category);

        /// <summary>默认（桌面向）：64 同屏，超限回收最旧。</summary>
        public static VfxBudget Default() => new VfxBudget(64);

        /// <summary>低端机：32 同屏 + 跳过重特效类别（`heavy` 类别需显式 <see cref="VfxCatalog.Register"/> 才会生效）。</summary>
        public static VfxBudget LowEnd() => new VfxBudget(32, VfxOverflowPolicy.RecycleOldest, new[] { VfxCategories.Heavy });

        /// <summary>无上限（测试/调试）。</summary>
        public static VfxBudget Unlimited() => new VfxBudget(int.MaxValue, VfxOverflowPolicy.Reject);
    }
}
