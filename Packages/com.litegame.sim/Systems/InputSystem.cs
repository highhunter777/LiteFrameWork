namespace LiteSim
{
    /// <summary>
    /// 输入系统（《状态同步实施方案》§3.3 顺序第 1 位）： 应用移动向量、**瞄准点**与**瞄准态**到玩家实体——朝向（`Yaw`）在此从 AimPoint 派生。
    /// </summary>
    public static class InputSystem
    {
        /// <summary>移动/瞄准点判定阈值（平方口径；低于此视为"没有移动意图/瞄准点退化"，不更新朝向）。</summary>
        private const float MoveEpsilonSquared = 1e-6f;

        /// <summary>
        /// 瞄准点**最小水平距离平方**（0.1 m）：低于它视为退化瞄准点、不派生朝向。
        /// </summary>
        private const float AimPointMinDistanceSquared = 0.01f;

        public static void Run(SimWorldState s, SimInputFrame[] inputs, in CombatValues values)
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
                bool fireFrame = (buttons & (SimInputFrame.ButtonFire | SimInputFrame.ButtonFireFlag)) != 0u;

                // **瞄准帧与开火帧都置满同一个驻留窗**（判据与朝向派生同一条——射击语境单源）： 扣扳机即进语境、**不问武器门结果**——空仓/换弹/冷却/切枪中被拦的扣扳机同置窗； 若朝向认开火帧而窗不认，身体转向准星但表现语境缺席（视图 MoveBlend 无方向性。）。
                if (aiming || fireFrame) e.FireStanceFrames = (byte)values.FireStanceFrames;

                // 射击语境单源（瞄准 ∨ 开火驻留窗）——朝向派生与限速共用同一条判据。
                bool combatContext = aiming || e.FireStanceFrames > 0;

                // 朝向派生（规则见类注释）： - **射击语境 → 朝准星**（AimPoint 必须有效：非零值且水平距本体 > 0.1m；退化点不派生、 落入保持）——ADS/腰射时身体锁朝准星，视图走四向 strafe。
                float pdx = inputs[i].AimPointX - e.Pos.X;
                float pdz = inputs[i].AimPointZ - e.Pos.Z;
                // 协议约定**全零 = 无瞄准点**（"非零值"判定的落地）：缺它就等于把世界原点当成
                // 有效瞄准点——任何不在原点的实体会朝原点转身。
                bool aimPointSet = (inputs[i].AimPointX != 0f)
                                   | (inputs[i].AimPointY != 0f)
                                   | (inputs[i].AimPointZ != 0f);            // lint-allow R3（与常量 0 的明确比较）
                bool aimValid = aimPointSet
                                && SimMath.MulAdd2(pdx, pdx, pdz, pdz) > AimPointMinDistanceSquared;
                if (aimValid && combatContext)
                {
                    e.Yaw = SimTrig.Atan2(pdz, pdx);
                    e.FaceExitTurning = 1;   // 武装离场转向：语境解除后的首个移动帧起按速率平滑转回
                }
                else if (SimMath.MulAdd2(inputs[i].MoveX, inputs[i].MoveX, inputs[i].MoveZ, inputs[i].MoveZ)
                         > MoveEpsilonSquared)
                {
                    // 朝移动方向：**始终按转向速率**过渡（射击语境解除后回转不瞬切，常态移动同理—— PUBG 式转身跑需要可见的转身过程，即时跟向会让 A/D 起步瞬间扭脖子）。
                    float target = SimTrig.Atan2(inputs[i].MoveZ, inputs[i].MoveX);
                    float maxStep = values.Geometry.FaceTurnRadPerSec * SimConfig.Dt;
                    float d = target - e.Yaw;
                    while (d > SimTrig.Pi) d -= SimTrig.TwoPi;
                    while (d < -SimTrig.Pi) d += SimTrig.TwoPi;

                    if (d > maxStep)
                    {
                        e.Yaw += maxStep;                          // 一步过渡
                        while (e.Yaw > SimTrig.Pi) e.Yaw -= SimTrig.TwoPi;      // 结果归约——值域与 Atan2 一致
                        while (e.Yaw <= -SimTrig.Pi) e.Yaw += SimTrig.TwoPi;
                    }
                    else if (d < -maxStep)
                    {
                        e.Yaw -= maxStep;
                        while (e.Yaw > SimTrig.Pi) e.Yaw -= SimTrig.TwoPi;
                        while (e.Yaw <= -SimTrig.Pi) e.Yaw += SimTrig.TwoPi;
                    }
                    else
                    {
                        e.Yaw = target;                            // 步长内到位（解除武装）
                        e.FaceExitTurning = 0;
                    }
                }
                // else：静止且无语境 → 保持上一帧（不更新）

                // 移动：射击语境（瞄准 ∨ 开火窗）→ 限速走路档；否则全速。
                float speed = combatContext ? values.AimMoveSpeed : values.MoveSpeed;
                e.Vel.X = inputs[i].MoveX * speed;
                e.Vel.Z = inputs[i].MoveZ * speed;

                // 瞄准态标志：**每帧从输入位覆写**（位定义见 EntityFlags——输入位与实体标志位是两个位空间）
                e.Flags = (e.Flags & ~EntityFlags.Aiming) | (aiming ? EntityFlags.Aiming : 0u);
            }
        }
    }
}
