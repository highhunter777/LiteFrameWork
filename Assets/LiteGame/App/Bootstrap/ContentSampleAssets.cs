using System.Collections.Generic;

namespace LiteGame
{
    /// <summary>
    /// 样例①（真实资产包段）的入口资产清单（单源）：健康探针（入口资源可加载性）与 Editor 侧
    /// 样例构建器共用。
    ///
    /// 标记素材位于**共享资源目录** `Assets/ShareResource/SampleContent/`——它只存在于候选资产包
    /// （收集组按 `Assets/ShareResource` 整目录收集，内置包不含）；入口探针能加载出来即证明
    /// 资源来自**候选包根**而非内置包（详见《样例①真实资源包段-运行手册》）。
    /// </summary>
    public static class ContentSampleAssets
    {
        /// <summary>样例资源子目录（共享资源目录下；只收纳必要配套：标记 Prefab + 其纹理）。</summary>
        public const string Root = "Assets/ShareResource/SampleContent";

        /// <summary>标记 Prefab（最小标记素材；唯一依赖为同目录纹理）。</summary>
        public const string MarkerPrefabLocation = Root + "/CandidateMarker.prefab";

        /// <summary>标记纹理（标记 Prefab 的唯一依赖；Editor 构建器生成用）。</summary>
        public const string MarkerTextureLocation = Root + "/CandidateMarkerTexture.png";

        /// <summary>入口资源清单（入口资产探针的 location 列表；location = 资源完整路径）。</summary>
        public static IReadOnlyList<string> EntryLocations { get; } = new[] { MarkerPrefabLocation };
    }
}