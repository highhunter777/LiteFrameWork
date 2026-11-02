using System;

namespace LiteFramework.Animation
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
        /// （表列 aim_move_speed 默认值 = 2.5）。</summary>
        public const float WalkFullMps = 2.5f;

        /// <summary>速度轴锚点（m/s）：Run 权重到 1（略低于 <c>CombatConfig.MoveSpeed</c> = 5，给斜向/边界留余量）。</summary>
        public const float RunFullMps = 4.5f;

        /// <summary>Walk 片段的**脚程速度**（m/s——该片段按原生节拍播放时"贴地"的地面速度；
        /// 步幅对齐的参考值）。**手测定档初始值**（= 走档设计速度）。</summary>
        public const float WalkNaturalMps = 2.5f;

        /// <summary>Run 片段的**脚程速度**（m/s——同上语义）。**手测定档初始值**（= 满速档设计速度）。</summary>
        public const float RunNaturalMps = 5f;

        /// <summary>步幅对齐的相位速率（圈/秒）钳制下界——低速/权重趋零时换算会放大（除爆保护）。</summary>
        public const float PhaseRateMin = 0.4f;

        /// <summary>步幅对齐的相位速率（圈/秒）钳制上界（**手测定档**）。</summary>
        public const float PhaseRateMax = 2.2f;

        /// <summary>瞄准移动步频倍率（跟脚程）的钳制下界（原 LocomotionRootStage 内联魔法数收口）。</summary>
        public const float AimStepRateMin = 1f;

        /// <summary>瞄准移动步频倍率的钳制上界（同上收口）。</summary>
        public const float AimStepRateMax = 2f;

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
        /// **移动混合的相位/步幅对齐净速率**（槽位序 {Idle, Walk, Run}；<paramref name="durations"/>
        /// = 各片段时长（秒）、<paramref name="speeds"/> = 输出净播放速率——逐槽原位写、零分配）。
        ///
        /// 口径（步幅对齐——"速率＋相位"）：步态片段**相位锁定 + 脚程匹配**——设公共相位速率
        /// Φ（圈/秒），步态槽净速率 = Φ × 时长（同 Φ ⇒ 各片段归一化相位同步，混合不打滑、不踩乱拍）；
        /// Φ 由"加权脚程 = 实际速度"反解：<c>Φ = speed / Σ(wᵢ·时长ᵢ·脚程ᵢ)</c>
        /// （脚程：Walk = <see cref="WalkNaturalMps"/>、Run = <see cref="RunNaturalMps"/>）。
        /// **Idle 豁免**：无步态——净速率恒 1（随 Φ 缩放会按权重放大"呼吸"频率）。
        /// **退化**：有效加权脚程趋零（全静止/全零权重）→ 全槽 1×。
        /// Φ 钳制 [<see cref="PhaseRateMin"/>, <see cref="PhaseRateMax"/>]——**钳在 Φ 上**：
        /// 各步态槽同乘时长，相位锁定不被逐槽钳制破坏。
        /// </summary>
        public static void BuildMoveSpeeds(float speed, float[] weights, float[] durations, float[] speeds)
        {
            speeds[0] = 1f;                                              // Idle 豁免：无步态，恒 1×
            float weighted = 0f;                                         // Σ(wᵢ·Dᵢ·脚程ᵢ)——Idle 脚程 0 不入和
            if (weights[1] > 0f) weighted += weights[1] * durations[1] * WalkNaturalMps;
            if (weights[2] > 0f) weighted += weights[2] * durations[2] * RunNaturalMps;

            if (weighted <= 1e-6f)                                       // 全静止/零权重：无步态可对——全 1×
            {
                speeds[1] = 1f;
                speeds[2] = 1f;
                return;
            }

            float phi = speed / weighted;
            if (phi < PhaseRateMin) phi = PhaseRateMin;
            else if (phi > PhaseRateMax) phi = PhaseRateMax;

            speeds[1] = phi * durations[1];                              // 相位锁定：净速率 ∝ 时长（同 Φ）
            speeds[2] = phi * durations[2];
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
            int idx = (int)Math.Floor(t);
            float frac = t - idx;
            int slotA = ((idx % 4) + 4) % 4;
            int slotB = (slotA + 1) % 4;

            for (int i = 0; i < weights.Length; i++) weights[i] = 0f;
            weights[slotA] = 1f - frac;
            weights[slotB] = frac;
        }
    }
}