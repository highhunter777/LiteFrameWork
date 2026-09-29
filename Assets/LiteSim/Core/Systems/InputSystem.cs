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
    ///   2026-09-28 射击限速裁决：开火中（瞄准或腰射）同样限到走路档——射击移动上限与 ADS 同源，
    ///   预测/权威/回放三端同式（确定性位运算，见下）；
    /// - **开火驻留窗（2026-09-30 腰射批用户裁决）**：开火帧起连续 <see cref="CombatConfig.FireStanceFrames"/>
    ///   帧内保持"射击语境"（事件刷新制——窗内再开火即重置）——**限速与朝向一并窗内保持**：
    ///   移动点射的停火帧不再回跳全速、也不再从准星回跳移动向（表现层驻留窗同源派生，两层同进同退）；
    /// - **朝向派生分两种**：射击语境（瞄准中/开火帧/驻留窗内）→ 朝准星（腰射的射击方向来自 `Aim`，
    ///   身位必须跟枪口一致，否则子弹看起来从侧面飞出；窗内跟准星——准星零向量不派生、退回移动分支）；
    ///   否则 → 朝移动方向（非瞄准的走跑用"前进向"片段，侧移不再横着走）；
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
                bool firing = (buttons & (SimInputFrame.ButtonFire | SimInputFrame.ButtonFireFlag)) != 0u;

                // 开火驻留窗（2026-09-30 腰射批）：开火帧重置为 N（含服务器回溯补判位——它也是
                // "开了一枪"），其后每逻辑帧递减（整数计数 ⇒ 确定性；预测帧/服务器缺席空输入帧同样推进）
                if (firing) e.FireStanceFrames = CombatConfig.FireStanceFrames;
                else if (e.FireStanceFrames > 0) e.FireStanceFrames--;

                // 射击语境 = 瞄准中 / 开火帧 / 驻留窗内（限速与朝向同判——两层窗同源，同进同退）
                bool stance = aiming || firing || e.FireStanceFrames > 0;

                // 移动：射击语境限速（倍率 0.5 = 乘 2 的幂，位级精确——见 CombatConfig.AimMoveSpeed；
                // 开火中不降速会出现"全速走位 + 腰射"的火力机动优势，与 ADS 不对等——2026-09-28 裁决；
                // 窗内保持是 2026-09-30 扩展——点射停火帧不再回跳全速）
                float speed = stance ? CombatConfig.AimMoveSpeed : CombatConfig.MoveSpeed;
                e.Vel.X = inputs[i].MoveX * speed;
                e.Vel.Z = inputs[i].MoveZ * speed;

                // 朝向派生（规则见类注释；Atan2(dz, dx) 查表，确定性）。
                // 射击语境 → 朝准星；准星零向量不派生（"开火帧非零"是采集侧契约，非开火帧不保证——
                // 拿 (0,0) 算 Atan2 会得垃圾朝向；退回移动分支，保持"不拿零向量退化"纪律）
                bool aimUsable =
                    SimMath.MulAdd2(inputs[i].AimX, inputs[i].AimX, inputs[i].AimZ, inputs[i].AimZ) > MoveEpsilonSquared;
                if (stance && aimUsable)
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
