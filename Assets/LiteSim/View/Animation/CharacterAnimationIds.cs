using LiteFramework;

namespace LiteSim.View.Animation
{
    /// <summary>
    /// 角色动画语义 ID（§4"游戏层定义语义，框架只处理类型化 ID"）。
    /// 语义与控制器状态解耦——状态绑定见 <see cref="CombatGirlsAnimationProfile"/>。
    /// </summary>
    public static class CharacterAnimationIds
    {
        public static readonly AnimationId Idle = new AnimationId("Locomotion.Idle");
        public static readonly AnimationId Walk = new AnimationId("Locomotion.Walk");
        public static readonly AnimationId Run = new AnimationId("Locomotion.Run");
    }

    /// <summary>
    /// CombatGirls 角色动画 Profile（《动画模块专项设计》§3/§4：ID → 控制器状态绑定的唯一登记点；
    /// §4"不得到处散写参数字符串"）。
    ///
    /// **资源来源（2026-09-25 资源指令）**：模型与主要动画均取自 `CombatGirlsCharacterPack`
    /// ——绑定即该包 `Rifle_Controller` 的状态名；包未覆盖的语义（受击/死亡表现细化等）
    /// 该包内亦备有状态（Hit1/Hit2/Die1/Die2/Stun/Evade/Reload/Aim*），出现消费者时按需登记，
    /// 不预建无消费者的绑定。
    /// </summary>
    public static class CombatGirlsAnimationProfile
    {
        /// <summary>该包的动画控制器（装配与测试校验用单源路径）。</summary>
        public const string ControllerPath = "Assets/CombatGirlsCharacterPack/RifleGirl/Animations/Rifle_Controller.controller";

        /// <summary>对局实体视图 prefab（模型只用该包；缺包克隆走 SimView 灰盒兜底降级）。</summary>
        public const string ViewPrefabPath = "Assets/CombatGirlsCharacterPack/Runtime/RifleGirl_View.prefab";

        /// <summary>
        /// 构建对局角色的动画 Profile（不可变共享数据——每实体播放器引用同一份）。
        /// 移动三态均循环（§5"循环播放不会自然 Completed"）。
        /// </summary>
        public static AnimationProfile Build()
            => new AnimationProfile()
                .Register(new AnimationDefinition(CharacterAnimationIds.Idle, AnimationChannel.Locomotion,
                    binding: "Idle", loop: true, minSpeed: 0.01f, maxSpeed: 2f))
                .Register(new AnimationDefinition(CharacterAnimationIds.Walk, AnimationChannel.Locomotion,
                    binding: "Walk", loop: true, minSpeed: 0.01f, maxSpeed: 2f))
                .Register(new AnimationDefinition(CharacterAnimationIds.Run, AnimationChannel.Locomotion,
                    binding: "Run", loop: true, minSpeed: 0.01f, maxSpeed: 2f));
    }
}
