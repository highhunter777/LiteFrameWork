namespace LiteSim.View
{
    /// <summary>
    /// 一条特效定义（"命名即引用"：**不做 Luban 表**，名字直接映射资源地址，见 <see cref="VfxCatalog"/>）。
    /// </summary>
    public readonly struct VfxDef
    {
        public readonly string Name;
        /// <summary>类别（低端降级白/黑名单用；默认 <see cref="VfxCategories.Default"/>）。</summary>
        public readonly string Category;
        /// <summary>资源地址（YooAsset location）。</summary>
        public readonly string Location;
        /// <summary>&gt;0 时覆盖自动推算的生命周期（秒）。</summary>
        public readonly float LifetimeOverride;

        public VfxDef(string name, string category, string location, float lifetimeOverride = 0f)
        {
            Name = name;
            Category = category;
            Location = location;
            LifetimeOverride = lifetimeOverride;
        }

        public bool IsValid => !string.IsNullOrEmpty(Location);
    }

    /// <summary>特效类别常量（低端降级按类别跳过）。</summary>
    public static class VfxCategories
    {
        public const string Default = "default";
        /// <summary>重特效（大范围/高粒子数）——低端机跳过。</summary>
        public const string Heavy = "heavy";
    }
}
