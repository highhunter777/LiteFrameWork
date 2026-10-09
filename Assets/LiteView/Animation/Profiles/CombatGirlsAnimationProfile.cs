namespace LiteView.Animation
{
    /// <summary>
    /// CombatGirls 角色族的动画装配常量（内容路径与模型族标识）。
    ///
    /// **绑定与循环策略已表源化**（tbanimationprofile 数据表）——语义/绑定/通道/区间/持帧/Mask 排除的
    /// 唯一登记源是表行（`Luban/Data/#animationprofile.xlsx`）：运行时经 `ConfigService` 读口 +
    /// `AnimationProfileLoader.FromRows(模型族)` 装配；编辑器工具与测试经
    /// `AnimationProfileTableSource.Load` 直载同一张表——语义链零分叉。
    /// 本文件只剩"资产在哪"的路径常量与族标识：**改绑定去表，改资产路径来这**。
    ///
    /// **与 <see cref="CharacterAnimationIds"/> 分层**：那边是游戏层语义词汇表（加语义才动），
    /// 这边是资源路径与族标识（改资产/挪目录才动）——两件事不同变更理由，故分文件。
    /// </summary>
    public static class CombatGirlsAnimationProfile
    {
        /// <summary>模型族标识（tbanimationprofile 的 model_family 分派键——装载按此过滤本族行）。</summary>
        public const string ModelFamily = "CombatGirls";

        /// <summary>该包的动画控制器（清单构建器的片段解析源；测试校验用单源路径）。</summary>
        public const string ControllerPath = "Assets/CombatGirlsCharacterPack/RifleGirl/Animations/Rifle_Controller.controller";

        /// <summary>对局实体视图 prefab（缺包克隆走 SimView 灰盒兜底降级）。
        /// 位于 `Assets/Prefab/`（+MagicaCloth 布料/头发物理；依赖资产在 `CombatGirlsCharacterPack/`
        /// ——收集组需同时覆盖两处）。
        /// **必须与 <see cref="SimView.DefaultEntityPrefab"/> 同值**——两处都是"对局实体用哪个 prefab"
        /// 的单源，分叉会让动画绑定与实际视图对不上（绑定按名字解析，换 prefab 后状态名不变，
        /// 但视图与动画配置指向不同文件时排查成本高）。</summary>
        public const string ViewPrefabPath = "Assets/Prefab/Player(Rifle).prefab";

        /// <summary>该角色族的片段清单资产（去 AC 主路径的片源载体——键→Clip 显式引用的纯配置）。
        /// 键单源在 tbanimationprofile 表行；改绑定键后经构建器（`AgentScripts/BuildClipManifestAssets.cs`）
        /// 从 <see cref="ControllerPath"/> 同表重建。与 <see cref="ViewPrefabPath"/> 同置 `Assets/Prefab/`
        /// （Characters 收集组——随包进 Player）。</summary>
        public const string ClipManifestPath = "Assets/Prefab/CombatGirlsAnimationClips.asset";
    }
}
