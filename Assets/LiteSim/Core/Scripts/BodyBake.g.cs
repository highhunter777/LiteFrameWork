// 本文件由 BodyCylinderBaker 生成（菜单 LiteGame/烘焙身位圆柱，或 run_script 调用）——勿手改。
// 来源：Assets/Prefab/Player(Rifle).prefab 的 CharacterController（美术调 CC 后重跑即重烘；
//       该 prefab 不入版本管理，重烘需本地 prefab）。
// 语义：Sim 物理体竖直圆柱 [pos.Y, pos.Y + Height]（CC center.y 已校验 == Height/2）。
// 消费：CombatConfig.BodyRadius（移动去穿插）+ HitscanHeight（站姿高/头带比例基底）+ 视图 CC。
// 注意：命中柱半径 HitscanRadius（0.45）是判定宽容裁决常量，不是本产物。
namespace LiteSim
{
    /// <summary>身位圆柱常量（烘焙值；= prefab CharacterController 的 radius/height）。</summary>
    public static class BodyBake
    {
        public const float Radius = 0.32f;
        public const float Height = 1.8f;
    }
}
