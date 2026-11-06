namespace LiteGame
{
    /// <summary>
    /// CombatGirls 角色族的动画装配常量（内容路径与模型族标识）。
    /// </summary>
    public static class CombatGirlsAnimationProfile
    {
        /// <summary>模型族标识（tbanimationprofile 的 model_family 分派键——装载按此过滤本族行）。</summary>
        public const string ModelFamily = "CombatGirls";

        /// <summary>该包的动画控制器（清单构建器的片段解析源；测试校验用单源路径）。</summary>
        public const string ControllerPath = "Assets/ThirdParty/CombatGirlsCharacterPack/RifleGirl/Animations/Rifle_Controller.controller";

        /// <summary>
        /// 对局实体视图 prefab（缺包克隆走 SimView 灰盒兜底降级）。
        /// </summary>
        public const string ViewPrefabPath = SimView.DefaultEntityPrefab;

        /// <summary>该角色族的片段清单资产（去 AC 主路径的片源载体——键→Clip 显式引用的纯配置）。
        /// 键单源在 tbanimationprofile 表行；改绑定键后经构建器（`AgentScripts/BuildClipManifestAssets.cs`）
        /// 从 <see cref="ControllerPath"/> 同表重建。与 <see cref="ViewPrefabPath"/> 同置 `Assets/LiteGame/Modules/Battle/Content/Characters/`
        /// （Characters 收集组——随包进 Player）。</summary>
        public const string ClipManifestPath = "Assets/LiteGame/Modules/Battle/Content/Characters/CombatGirlsAnimationClips.asset";
    }
}
