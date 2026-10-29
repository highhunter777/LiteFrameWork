// 本文件由 MuzzleOffsetBaker 生成（烘焙工具箱：菜单 LiteGame/烘焙工具箱，或 run_script 直调）——勿手改。
// 来源：Assets/ShareResource/Characters/Player(Rifle).prefab 的 Weapon_Rifle/Muzzle 锚点，
//       参考动画姿态 AimIdle t=0（射弹时刻视觉姿态单族：瞄准=AimIdle、腰射开火窗亦以 AimIdle 填窗）。
// 坐标：相对 prefab 根的视觉局部系（+Z 前 → Sim 前向；+X 右 → Sim 右向；+Y → 高度），
//       与 SimView 的 FacingRotation（yaw → 旋转 90°−yaw）配套。
namespace LiteSim
{
    /// <summary>逻辑枪口常量（烘焙值；消费见 CombatConfig.MuzzleOrigin——三常量随代码进 buildHash 闭包——AOT 稳定层，运行期摘要只覆盖判定面表字节）。</summary>
    public static class MuzzleBake
    {
        public const float Forward = 0.7739859f;
        public const float Right = 0.0829204246f;
        public const float Height = 1.29482734f;
    }
}
