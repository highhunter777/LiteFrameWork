// 本文件由 WeaponGripBaker 生成（烘焙工具箱：菜单 LiteGame/烘焙工具箱，或 run_script 直调）——勿手改。
// 来源：Assets/ShareResource/Characters/Player(Rifle).prefab 的 RightHand/add_weapon_r 局部变换 @ AimIdle t=0（烘焙工具箱共享参考姿态）。
// 语义：武器骨相对跟随骨（持枪手 RightHand）的局部位姿——换弹上半身叠加期姿态覆写（WeaponGripConstraint）的唯一来源；
//       跟随右手而非躯干：add_weapon_r 与右手世界位全同（差 0.6mm）、右手是枪挂点父级——跟躯干会在拉栓（右手位移 21cm）时脱节。
// 落点：LiteView（纯表现参数，刻意不进 LiteSim/Core/Scripts 的 BuildHash 源集——表现值变化不踢两端握手）。
// 幅度：TorsoWeight 烘焙值 1（0=不约束 1=完全跟随）；对局内实时调参走 GM 面板（GripTuning 覆写，不落盘）。
namespace LiteView.Animation
{
    /// <summary>持枪偏移常量（烘焙值；换弹期武器骨跟随持枪手——消费见 WeaponGripConstraint/GripTuning）。</summary>
    public static class GripBake
    {
        /// <summary>跟随骨路径（相对 Animator 根）：持枪手。</summary>
        public const string FollowPath = "root/pelvis/spine_01/spine_02/spine_03/clavicle_r/upperarm_r/lowerarm_r/hand_r";
        /// <summary>武器骨路径（相对 Animator 根；枪模型 Weapon_Rifle 是它的子节点，枪口火光/激光挂点随之跟随）。</summary>
        public const string WeaponPath = "root/add_weapon_r";
        /// <summary>枪骨相对跟随骨的局部位置（持枪位偏移，参考姿态烘焙）。</summary>
        public const float LocalPosX = -0.000616349746f;
        public const float LocalPosY = 0.000167606049f;
        public const float LocalPosZ = -0.000107894019f;
        /// <summary>枪骨相对跟随骨的局部旋转（欧拉角，度；消费侧 Quaternion.Euler）。</summary>
        public const float LocalEulerX = -5.11717E-05f;
        public const float LocalEulerY = 270.000031f;
        public const float LocalEulerZ = -4.806444E-05f;
        /// <summary>幅度缩放：0 = 不约束（用片段曲线）、1 = 完全跟随持枪手。</summary>
        public const float TorsoWeight = 1f;
    }
}
