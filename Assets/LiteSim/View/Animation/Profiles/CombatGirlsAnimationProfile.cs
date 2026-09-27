using LiteFramework.Animation;

namespace LiteSim.View.Animation
{
    /// <summary>
    /// CombatGirls 角色动画 Profile（《动画模块专项设计》§3/§4：ID → 控制器状态绑定的唯一登记点；
    /// §4"不得到处散写参数字符串"）。
    ///
    /// **资源来源（2026-09-25 资源指令）**：模型与主要动画均取自 `CombatGirlsCharacterPack`
    /// ——绑定即该包 `Rifle_Controller` 的状态名；包未覆盖的语义（受击/死亡表现细化等）
    /// 该包内亦备有状态（Hit1/Hit2/Die1/Die2/Stun/Evade/Reload/Aim*），出现消费者时按需登记，
    /// 不预建无消费者的绑定。
    ///
    /// **与 <see cref="CharacterAnimationIds"/> 分层**：那边是游戏层语义词汇表（加语义才动），
    /// 本文件是该角色的绑定与循环策略登记（改绑定/覆盖规则才动）——两件事不同变更理由，故分文件。
    /// </summary>
    public static class CombatGirlsAnimationProfile
    {
        /// <summary>该包的动画控制器（装配与测试校验用单源路径）。</summary>
        public const string ControllerPath = "Assets/CombatGirlsCharacterPack/RifleGirl/Animations/Rifle_Controller.controller";

        /// <summary>对局实体视图 prefab（缺包克隆走 SimView 灰盒兜底降级）。
        /// **2026-09-26 换 prefab**：`RifleGirl_View` → `Player(Rifle)`（+MagicaCloth 布料/头发物理），
        /// 位于 `Assets/Prefab/`（依赖资产在 `CombatGirlsCharacterPack/`——收集组需同时覆盖两处）。
        /// **必须与 <see cref="SimView.DefaultEntityPrefab"/> 同值**——两处都是"对局实体用哪个 prefab"
        /// 的单源，分叉会让动画绑定与实际视图对不上（绑定按名字解析，换 prefab 后状态名不变，
        /// 但视图与动画配置指向不同文件时排查成本高）。</summary>
        public const string ViewPrefabPath = "Assets/Prefab/Player(Rifle).prefab";

        /// <summary>
        /// 构建对局角色的动画 Profile（不可变共享数据——每实体播放器引用同一份）。
        /// **绑定 = 片段名**（`Rifle_Controller.animationClips` 已覆盖全部登记片段，`RequiresLoad:false`，
        /// 后端按「Clip 资源键」直驱——§4 允许的分支；控制器参数全是 Trigger，参数驱动不可用）。
        /// 移动/瞄准态循环（§5"循环播放不会自然 Completed"）；开火/换弹/受击/死亡/回避均为一次性。
        /// **不预建无消费者的绑定**（Crouch×Aim 家族、PutGun/TakeGun/AimTurn、Stun、Hit2/Die2——
        /// 出现消费者再登记）。
        /// </summary>
        public static AnimationProfile Build()
            => new AnimationProfile()
                // 基础移动：循环，速度区间 0.01–2
                .Register(new AnimationDefinition(CharacterAnimationIds.Idle, AnimationChannel.Locomotion,
                    binding: "Idle", loop: true, minSpeed: 0.01f, maxSpeed: 2f))
                .Register(new AnimationDefinition(CharacterAnimationIds.Walk, AnimationChannel.Locomotion,
                    binding: "Walk", loop: true, minSpeed: 0.01f, maxSpeed: 2f))
                .Register(new AnimationDefinition(CharacterAnimationIds.Run, AnimationChannel.Locomotion,
                    binding: "Run", loop: true, minSpeed: 0.01f, maxSpeed: 2f))
                .Register(new AnimationDefinition(CharacterAnimationIds.AimIdle, AnimationChannel.Locomotion,
                    binding: "AimIdle", loop: true, minSpeed: 0.01f, maxSpeed: 2f))
                // 上半身叠加：一次性（开火片段很短，允许提速到 2×）
                .Register(new AnimationDefinition(CharacterAnimationIds.Fire, AnimationChannel.UpperBody,
                    binding: "AimIdle_Shoot", loop: false, minSpeed: 0.8f, maxSpeed: 2f))
                .Register(new AnimationDefinition(CharacterAnimationIds.Reload, AnimationChannel.UpperBody,
                    binding: "Reload", loop: false, minSpeed: 0.5f, maxSpeed: 2f))
                // 全身覆盖：一次性
                .Register(new AnimationDefinition(CharacterAnimationIds.Hit, AnimationChannel.FullBody,
                    binding: "Hit1", loop: false, minSpeed: 0.5f, maxSpeed: 2f))
                .Register(new AnimationDefinition(CharacterAnimationIds.Death, AnimationChannel.FullBody,
                    binding: "Die1", loop: false, minSpeed: 0.5f, maxSpeed: 1.5f))
                .Register(new AnimationDefinition(CharacterAnimationIds.Evade, AnimationChannel.FullBody,
                    binding: "Evade", loop: false, minSpeed: 0.7f, maxSpeed: 1.5f))
                // 混合形态（§4 Blend 的登记面；权重由 Driver 给，槽位序即权重数组次序）：
                // 非瞄准移动 = 速度轴 1D 混合；瞄准移动 = 4 向 strafe 按夹角插值（限速后只有走路一档）
                .RegisterBlend(new AnimationBlendDefinition(CharacterAnimationIds.MoveBlend, AnimationChannel.Locomotion,
                    new[] { "Idle", "Walk", "Run" }, minSpeed: 0.01f, maxSpeed: 2f))
                .RegisterBlend(new AnimationBlendDefinition(CharacterAnimationIds.AimMoveBlend, AnimationChannel.Locomotion,
                    new[] { "AimWalk_F", "AimWalk_R", "AimWalk_B", "AimWalk_L" }, minSpeed: 0.01f, maxSpeed: 2f));
    }
}