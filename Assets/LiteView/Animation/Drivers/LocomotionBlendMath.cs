using UnityEngine;

namespace LiteSim.View.Animation
{
    /// <summary>
    /// 移动混合权重数学（《动画模块专项设计》§4"权重由 Driver 给"的**纯计算面**）：
    /// 速度轴三段线性插值 + 瞄准方向轴四向相邻插值——只有 float 入参，不读 Transform、不碰播放器。
    ///
    /// **为什么单独成类**：权重公式与"形态提交/迟滞/句柄"是两类变化理由——前者是数值手感
    /// （改锚点、改插值形状），后者是播放编排；分开后公式可用构造坐标**直接断言权重**，不需要
    /// 真资源、真视图或逐帧位移（见 <c>CharacterLocomotionEditModeTests</c> 的权重直测用例）。
    ///
    /// **调用方职责**：计算角度（`Vector3.SignedAngle(facing, moveDir, up)`）与容器复用归
    /// <see cref="CharacterLocomotionDriver"/>；本类只填 <paramref name="weights"/>，不分配。
    /// </summary>
    public static class LocomotionBlendMath
    {
        /// <summary>速度轴锚点（m/s）：≤ 此值 Idle 权重为 1，往上开始并入 Walk。</summary>
        public const float IdleBelowMps = 0.5f;

        /// <summary>速度轴锚点（m/s）：Walk 权重到 1、开始并入 Run——同时也是**瞄准限速后的满速**
        /// （`CombatConfig.MoveSpeed × AimMoveSpeedFactor` = 2.5）。</summary>
        public const float WalkFullMps = 2.5f;

        /// <summary>速度轴锚点（m/s）：Run 权重到 1（略低于 <c>CombatConfig.MoveSpeed</c> = 5，给斜向/边界留余量）。</summary>
        public const float RunFullMps = 4.5f;

        /// <summary>瞄准静止 ↔ 瞄准移动的**进入阈值**（m/s）——迟滞上沿（由移动事实锁存消费，
        /// 战斗根 idle↔walk 轴两族共用：AimIdle↔AimWalk / FireIdle↔FireWalk）。</summary>
        public const float AimMoveEnterMps = 0.6f;

        /// <summary>瞄准移动 → 静止的**退出阈值**（m/s）——迟滞下沿（与进入阈值拉开即迟滞，防单点来回切）。</summary>
        public const float AimMoveExitMps = 0.3f;

        /// <summary>
        /// **移动事实锁存**（迟滞公式单源——《层次动画机设计》§2：锁存是**原始速度的纯函数**，
        /// 由驱动器每帧算并作为事实喂状态机；机内无迟滞散字段）：上沿进（≥ 进阈值）、下沿出（> 退阈值），
        /// 中间带保持上一值。
        /// </summary>
        public static bool UpdateLatch(bool previousMoving, float speed)
            => previousMoving ? speed > AimMoveExitMps : speed >= AimMoveEnterMps;


        /// <summary>
        /// 非瞄准速度轴权重（槽位序 {Idle, Walk, Run}）：<c>IdleBelowMps → WalkFullMps → RunFullMps</c>
        /// 三段线性插值，总和恒 1、边界连续（在锚点上两侧算出的权重相同——不会有跳变）。
        /// </summary>
        public static void BuildSpeedWeights(float speed, float[] weights)
        {
            weights[0] = 0f;
            weights[1] = 0f;
            weights[2] = 0f;

            if (speed <= IdleBelowMps)
            {
                weights[0] = 1f;
                return;
            }
            if (speed < WalkFullMps)
            {
                float k = (speed - IdleBelowMps) / (WalkFullMps - IdleBelowMps);
                weights[0] = 1f - k;
                weights[1] = k;
                return;
            }
            if (speed < RunFullMps)
            {
                float k = (speed - WalkFullMps) / (RunFullMps - WalkFullMps);
                weights[1] = 1f - k;
                weights[2] = k;
                return;
            }
            weights[2] = 1f;
        }

        /// <summary>
        /// 瞄准移动方向轴权重（槽位序 {F, R, B, L}）：把"移动方向 vs 朝向"的夹角换算成槽位坐标
        /// （F=0 / R=+1 / B=±2 / L=−1，各槽位相隔 90°），取**相邻两片**按小数部分插值——权重连续，
        /// 跨扇区不跳变（所以不需要方向迟滞）。
        /// <paramref name="relativeAngleDeg"/> 符号约定：正 = 朝向的右侧
        /// （与 <c>Vector3.SignedAngle(facing, moveDir, up)</c> 同源）；模型前沿约定 +Z（与
        /// <c>SimView.Place</c> 的 <c>Quaternion.Euler(0, 90° − yaw)</c> 同源）。
        /// </summary>
        public static void BuildAimWeights(float relativeAngleDeg, float[] weights)
        {
            float t = relativeAngleDeg / 90f;                               // 槽位坐标：F=0 / R=1 / B=2 / L=-1
            int idx = Mathf.FloorToInt(t);
            float frac = t - idx;
            int slotA = ((idx % 4) + 4) % 4;
            int slotB = (slotA + 1) % 4;

            for (int i = 0; i < weights.Length; i++) weights[i] = 0f;
            weights[slotA] = 1f - frac;
            weights[slotB] = frac;
        }
    }
}