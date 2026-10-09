using LiteFramework.Animation;

namespace LiteView.Animation
{
    /// <summary>
    /// 角色动画语义 ID（§4"游戏层定义语义，框架只处理类型化 ID"）。
    /// 语义与控制器状态解耦——状态绑定见 <see cref="CombatGirlsAnimationProfile"/>（独立文件：
    /// 本表只在"增删语义"时变，绑定只在"改配置/覆盖规则"时变）。
    /// </summary>
    public static class CharacterAnimationIds
    {
        // ---- 基础移动（Locomotion）----
        public static readonly AnimationId Idle = new AnimationId("Locomotion.Idle");
        public static readonly AnimationId Walk = new AnimationId("Locomotion.Walk");
        public static readonly AnimationId Run = new AnimationId("Locomotion.Run");
        /// <summary>瞄准站姿（§13 首角色覆盖表"瞄准"；同为 Locomotion 通道——移动语义的一种）。</summary>
        public static readonly AnimationId AimIdle = new AnimationId("Locomotion.AimIdle");

        // ---- 全身覆盖（FullBody）----
        /// <summary>换弹（§13"换弹"；一次性——时长按 Sim <c>ReloadFrames</c> 对齐播放倍率）。</summary>
        public static readonly AnimationId Reload = new AnimationId("Combat.Reload");
        /// <summary>受击（§13"受击"）。</summary>
        public static readonly AnimationId Hit = new AnimationId("Combat.Hit");
        /// <summary>死亡（§13"死亡"）。</summary>
        public static readonly AnimationId Death = new AnimationId("Combat.Death");
        /// <summary>回避（§13 附列；同包已有独立片段，也是 FullBody 打断的天然消费者）。</summary>
        public static readonly AnimationId Evade = new AnimationId("Combat.Evade");

        // ---- 混合形态（同通道多片段按权重；**权重由 Driver 给**——《动画模块专项设计》§4）----
        /// <summary>非瞄准移动的速度轴混合（槽位序 = {Idle, Walk, Run}；权重按速度连续插值，权重由 Driver 给）。</summary>
        public static readonly AnimationId MoveBlend = new AnimationId("Locomotion.MoveBlend");

        /// <summary>瞄准移动的方向轴混合（槽位序 = {AimWalk_F, AimWalk_R, AimWalk_B, AimWalk_L}；
        /// 相邻两片按"移动方向 vs 朝向"夹角插值——限速后瞄准移动只有走路一档，故不需要 AimJog）。</summary>
        public static readonly AnimationId AimMoveBlend = new AnimationId("Locomotion.AimMoveBlend");
    }
}