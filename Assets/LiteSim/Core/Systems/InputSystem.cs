namespace LiteSim
{
    /// <summary>
    /// 输入系统（《状态同步实施方案》§3.3 顺序第 1 位 + M8 决策 #11/#12）：
    /// 应用移动向量、**瞄准方向**与**瞄准态**到玩家实体——朝向（`Yaw`）在此派生；
    /// 开火位由 ShootingSystem 直接读输入（签名按 #10 窄化）。
    /// 输入数组已由 SimStep 按 EntityId 升序排列（§3.3 同帧多请求的确定性来源）。
    ///
    /// **2026-09-27 瞄准态口径（用户裁决，见《角色状态与动作专项设计》§7）**：
    /// - **限速**：ADS 期间移动上限降到 <see cref="CombatConfig.AimMoveSpeed"/>（= 走路档）；
    /// - **朝向派生分两种**：瞄准中**或开火帧** → 朝准星（腰射的射击方向来自 `Aim`，身位必须跟枪口一致，
    ///   否则子弹看起来从侧面飞出）；否则 → 朝移动方向（非瞄准的走跑用"前进向"片段，侧移不再横着走）；
    ///   都不满足（静止且未开火）→ **保持上一帧 Yaw**（不拿零向量退化、且回放/重放可重建）。
    /// </summary>
    public static class InputSystem
    {
        /// <summary>移动向量判定阈值（平方口径；低于此视为"没有移动意图"，不更新朝向）。</summary>
        private const float MoveEpsilonSquared = 1e-6f;

        public static void Run(SimWorldState s, SimInputFrame[] inputs)
        {
            for (int i = 0; i < inputs.Length; i++)
            {
                // 目标已死 = 引用失效 = 正常路径（#7），本帧输入丢弃
                if (!s.TryResolve(inputs[i].EntityId, out int slotIndex)) continue;

                ref EntitySlot e = ref s.Entities[slotIndex];
                uint buttons = inputs[i].Buttons;
                bool aiming = (buttons & SimInputFrame.ButtonAim) != 0u;

                // 移动：瞄准限速（倍率 0.5 = 乘 2 的幂，位级精确——见 CombatConfig.AimMoveSpeed）
                float speed = aiming ? CombatConfig.AimMoveSpeed : CombatConfig.MoveSpeed;
                e.Vel.X = inputs[i].MoveX * speed;
                e.Vel.Z = inputs[i].MoveZ * speed;

                // 朝向派生（规则见类注释；Atan2(dz, dx) 查表，确定性）
                if (aiming || (buttons & (SimInputFrame.ButtonFire | SimInputFrame.ButtonFireFlag)) != 0u)
                {
                    e.Yaw = SimTrig.Atan2(inputs[i].AimZ, inputs[i].AimX);
                }
                else if (SimMath.MulAdd2(inputs[i].MoveX, inputs[i].MoveX, inputs[i].MoveZ, inputs[i].MoveZ) > MoveEpsilonSquared)
                {
                    e.Yaw = SimTrig.Atan2(inputs[i].MoveZ, inputs[i].MoveX);
                }

                // 瞄准态标志：**每帧从输入位覆写**（位定义见 EntityFlags——输入位与实体标志位是两个位空间）
                e.Flags = (e.Flags & ~EntityFlags.Aiming) | (aiming ? EntityFlags.Aiming : 0u);
            }
        }
    }
}