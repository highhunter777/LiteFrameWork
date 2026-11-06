namespace LiteFramework.Animation
{
    /// <summary>
    /// 登记行类别——装载器按类分流到 <see cref="AnimationProfile"/> 的登记面，一一对应不发明新面。
    /// </summary>
    public enum AnimationProfileRowKind
    {
        /// <summary>单片段定义 → <see cref="AnimationProfile.Register"/>（绑定/通道/循环/区间/持帧）。</summary>
        Single = 0,
        /// <summary>混合定义 → <see cref="AnimationProfile.RegisterBlend"/>（有序绑定槽位集）。</summary>
        Blend = 1,
        /// <summary>回退关系 → <see cref="AnimationProfile.RegisterFallback"/>：
        /// <see cref="AnimationProfileRow.Id"/>＝未登记的源语义 ID，
        /// <see cref="AnimationProfileRow.FallbackId"/>＝回退目标（须已登记——两遍扫保证行序无关）。</summary>
        Fallback = 2,
        /// <summary>叠加层 Mask 排除子树 → <see cref="AnimationProfile.RegisterOverlayMaskExclusions"/>
        ///（<see cref="AnimationProfileRow.Id"/> 即排除子树路径）。</summary>
        MaskExclusion = 3,
    }
}
