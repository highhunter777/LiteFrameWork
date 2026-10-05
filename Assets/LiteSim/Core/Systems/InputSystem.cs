namespace LiteSim
{
    /// <summary>
    /// 输入系统（《状态同步实施方案》§3.3 顺序第 1 位）：
    /// 应用移动向量、**瞄准方向**与**瞄准态**到玩家实体——朝向（`Yaw`）在此派生；
    /// 开火位由 ShootingSystem 直接读输入。
    /// 输入数组已由 SimStep 按 EntityId 升序排列（§3.3 同帧多请求的确定性来源）。
    ///
    /// **瞄准态口径（见《角色状态与动作专项设计》§7）**：
    /// - **限速**：**瞄准中 ∨ 开火态**（开火态 = <see cref="EntitySlot.FireStanceFrames"/> &gt; 0
    ///   ——移动腰射按 aimwalk 移动）→ 移动上限
    ///   <see cref="CombatConfig.AimMoveSpeed"/>（= 走路档）；
    /// - **朝向派生（债 #4 根治）**：
    ///   **射击语境（瞄准 ∨ 开火帧 ∨ 窗内）且准星向量有效** → 朝准星（即时跟枪——点射间隙帧不回摆，
    ///   视图侧由此得到稳定的 AimWalk 四向权重）并**武装离场转向**；
    ///   否则移动 → 朝移动方向——武装态按 <see cref="CombatConfig.FaceTurnRadPerSec"/> 逐帧过渡
    ///   （窗尽回转不瞬切），到位解除恢复即时跟向；都没有 → **保持上一帧 Yaw**（不拿零向量退化，
    ///   且回放/重放可重建）；
    /// - **开火驻留窗递减**：Run 顶部对**全槽位**统一推进——缺席/空输入帧与死亡实体照常衰减
    ///   （整数计数 ⇒ 确定性）；窗随 <see cref="ShootingSystem"/> 判定点置满、本系统先跑 ⇒ 置窗次帧起限速生效。
    /// </summary>
    public static class InputSystem
    {
        /// <summary>移动向量判定阈值（平方口径；低于此视为"没有移动意图"，不更新朝向）。</summary>
        private const float MoveEpsilonSquared = 1e-6f;

        public static void Run(SimWorldState s, SimInputFrame[] inputs)
        {
            // 开火驻留窗统一递减（全槽位——含缺席/死亡：窗自然衰减，死亡不特判；"离开开火态即取消"的自然结束面）
            EntitySlot[] entities = s.Entities;
            for (int i = 0; i < SimConfig.MaxEntities; i++)
                if (entities[i].FireStanceFrames > 0) entities[i].FireStanceFrames--;

            for (int i = 0; i < inputs.Length; i++)
            {
                // 目标已死 = 引用失效 = 正常路径（#7），本帧输入丢弃
                if (!s.TryResolve(inputs[i].EntityId, out int slotIndex)) continue;

                ref EntitySlot e = ref s.Entities[slotIndex];

                // 死亡实体输入作废（Hp≤0：移动/朝向/开火窗/标志位全不写——尸体不受操控；
                // NoDeath 测试房 Hp 跨死线保底 1，永不触发本守卫）
                if (e.Hp <= 0) continue;

                uint buttons = inputs[i].Buttons;
                bool aiming = (buttons & SimInputFrame.ButtonAim) != 0u;

                // **瞄准帧也置满同一个驻留窗**——频繁点按瞄准与点射共用
                // `FireStanceFrames`（同一字段/同一递减/同一快照公共面——协议零改动）：点按间隙帧
                // 不回移动向（朝准星+限速走路档随窗——视图 AimWalk 四向稳定的前提），窗尽离场转向。
                if (aiming) e.FireStanceFrames = (byte)CombatConfig.FireStanceFrames;

                // 移动：瞄准 ∨ 开火态 → 限速走路档（瞄准倍率 0.5 = 乘 2 的幂，位级精确；
                // 开火态限速改写 Vel ⇒ 窗计数是**判定输入**——已进全量 checksum，见 EntitySlot.FireStanceFrames）
                float speed = aiming || e.FireStanceFrames > 0
                    ? CombatConfig.AimMoveSpeed
                    : CombatConfig.MoveSpeed;
                e.Vel.X = inputs[i].MoveX * speed;
                e.Vel.Z = inputs[i].MoveZ * speed;

                // 朝向派生（规则见类注释）：射击语境 → 朝准星——**准星向量必须有效**（Atan2(0,0) 无意义，
                // 零向量不派生、落入保持）；窗内间隙帧不回移动向（点射"逐拍回摆"的根治面）
                bool fireFrame = (buttons & (SimInputFrame.ButtonFire | SimInputFrame.ButtonFireFlag)) != 0u;
                bool aimValid = SimMath.MulAdd2(inputs[i].AimX, inputs[i].AimX, inputs[i].AimZ, inputs[i].AimZ)
                    > MoveEpsilonSquared;
                if ((aiming || fireFrame || e.FireStanceFrames > 0) && aimValid)
                {
                    e.Yaw = SimTrig.Atan2(inputs[i].AimZ, inputs[i].AimX);
                    e.FaceExitTurning = 1;   // 武装离场转向：语境解除后的首个移动帧起按速率平滑转回
                }
                else if (SimMath.MulAdd2(inputs[i].MoveX, inputs[i].MoveX, inputs[i].MoveZ, inputs[i].MoveZ)
                         > MoveEpsilonSquared)
                {
                    // 朝移动方向：武装态（离场转向中）按转向速率逐帧过渡（窗尽回转不瞬切），
                    // 步长内到位＝精确落位并解除武装（分支即语义——**禁浮点等值比较**，R3）；
                    // 非武装态即时跟向（常态移动，既有手感不变）。差值/结果归约到 (-π, π]。
                    float target = SimTrig.Atan2(inputs[i].MoveZ, inputs[i].MoveX);
                    float maxStep = CombatConfig.FaceTurnRadPerSec * SimConfig.Dt;
                    float d = target - e.Yaw;
                    while (d > SimTrig.Pi) d -= SimTrig.TwoPi;
                    while (d < -SimTrig.Pi) d += SimTrig.TwoPi;

                    if (e.FaceExitTurning != 0 && (d > maxStep || d < -maxStep))
                    {
                        e.Yaw += d > 0f ? maxStep : -maxStep;      // 一步过渡
                        while (e.Yaw > SimTrig.Pi) e.Yaw -= SimTrig.TwoPi;      // 结果归约——值域与 Atan2 一致
                        while (e.Yaw <= -SimTrig.Pi) e.Yaw += SimTrig.TwoPi;
                    }
                    else
                    {
                        e.Yaw = target;                            // 步长内到位（解除武装）/ 常态即时跟向
                        e.FaceExitTurning = 0;
                    }
                }
                // else：静止且无语境 → 保持上一帧（不更新）

                // 瞄准态标志：**每帧从输入位覆写**（位定义见 EntityFlags——输入位与实体标志位是两个位空间）
                e.Flags = (e.Flags & ~EntityFlags.Aiming) | (aiming ? EntityFlags.Aiming : 0u);
            }
        }
    }
}
