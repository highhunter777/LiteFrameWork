namespace LiteSim
{
    /// <summary>
    /// 射击判定系统（§3.3 顺序第 3 位，§3.4 hitscan）：
    /// 对活体做圆柱求交（半径 + y 区间），按距离取最近（并列取低槽位——遍历顺序恒定）；
    /// 命中 → Cmds.Write(Damage)；开火/命中 → Events.Write(Fire/Hit)。
    /// 本系统是唯一消费 RngState 的系统（确定性审计写在签名上——伤害浮动 ±1）。
    ///
    /// **服务器回溯**：LagCompensator 会把本系统**单独**跑在历史帧状态上（不 Step），
    /// 因此本系统必须满足两条：① 不改 Frame/时序；② 只读输入 + 写 Cmds/Events/RngState +
    /// 槽位开火窗（FireStanceFrames——与 Fire 事件同点置窗；回溯副本上的写入随副本丢弃，不入权威态）。
    /// 回调方负责还原 RngState（回溯判定不该消费权威随机数）。
    /// </summary>
    public static class ShootingSystem
    {
        public static void Run(SimWorldState s, SimInputFrame[] inputs)
        {
            for (int i = 0; i < inputs.Length; i++)
            {
                if ((inputs[i].Buttons & SimInputFrame.ButtonFire) == 0u) continue;
                if (!s.TryResolve(inputs[i].EntityId, out int shooterSlot)) continue;

                ref EntitySlot shooter = ref s.Entities[shooterSlot];

                // 死亡射手不开火（Hp≤0：窗/事件/命中判定全跳过——尸体不受操控）
                if (shooter.Hp <= 0) continue;

                // 射线方向 = 输入瞄准向量本身（Aim 即事实，省一次三角函数往返；
                // 零向量不会命中任何目标——采集侧契约要求非零）
                float dx = inputs[i].AimX;
                float dz = inputs[i].AimZ;
                float originY = shooter.Pos.Y + CombatConfig.HitscanHeight * 0.5f;

                int hitSlot = -1;
                float hitT = CombatConfig.HitscanRange;
                for (int j = 0; j < SimConfig.MaxEntities; j++)
                {
                    if (j == shooterSlot) continue; // lint-allow R3（整型等值，非浮点精度比较）
                    if ((s.AliveBitmap[j >> 5] & (1u << (j & 31))) == 0u) continue;

                    ref EntitySlot tgt = ref s.Entities[j];

                    // 死亡目标不可命中（尸体非有效目标——命中反馈/伤害/爆头全不发生）
                    if (tgt.Hp <= 0) continue;

                    // 圆柱 y 区间：射线在 [tgt.Pos.Y, tgt.Pos.Y + Height] 内才算
                    if (originY < tgt.Pos.Y) continue;
                    if (originY > tgt.Pos.Y + CombatConfig.HitscanHeight) continue;

                    // XZ 平面射线-圆求交：m = C-O；b = m·D（前向投影）；c2 = |m|² - b²（垂距平方）
                    // 融合安全：`a*b + c*d` 形态一律走 SimMath 双精度累积件（Mono 会自动 FMA，.NET 不会）
                    float mx = tgt.Pos.X - shooter.Pos.X;
                    float mz = tgt.Pos.Z - shooter.Pos.Z;
                    float b = SimMath.MulAdd2(mx, dx, mz, dz);
                    if (b < 0f) continue; // 目标在身后

                    float r2 = CombatConfig.HitscanRadius * CombatConfig.HitscanRadius;
                    float c2 = SimMath.MulAddSub3(mx, mx, mz, mz, b, b);
                    if (c2 > r2) continue; // 垂距超出圆柱半径

                    float t = b - SimMath.Sqrt(r2 - c2);
                    if (t < 0f) t = 0f; // 起点已在圆柱内

                    if (t < hitT)
                    {
                        hitT = t;
                        hitSlot = j;
                    }
                }

                // 开火驻留窗置满（与 Fire 事件**同点**——View 侧窗口同触发同长度同刷新，
                // 事件刷新制重置满窗、上限即窗长；限速由 InputSystem 次帧起生效——本系统在输入之后跑）。
                // 服务器回溯：本字段随回溯副本丢弃，不入权威态——契约见类注释②。
                shooter.FireStanceFrames = (byte)CombatConfig.FireStanceFrames;

                s.Events.Write(FrameEventKind.Fire, shooter.Id, 0L, 0, shooter.Pos);

                if (hitSlot >= 0)
                {
                    ref EntitySlot hit = ref s.Entities[hitSlot];

                    // 伤害浮动 ±DamageSpread（消费 RngState——局部副本推进后写回，SimRng 使用约定）；
                    // base/spread 走 CombatConfig（数值参数化）。爆头带判定（命中高度 ≥ 目标脚底 +
                    // HeadHitLine）→ 倍率移位（位级精确）——倍率在命中判定处应用，
                    // Damage 命令/Hit 事件携带即最终值，结算侧无需知部位。
                    var rng = new SimRng(s.RngState);
                    int dmg = CombatConfig.BaseDamage + rng.NextRange(-CombatConfig.DamageSpread, CombatConfig.DamageSpread + 1);
                    s.RngState = rng.State;

                    var hitPos = new SimVector3(
                        shooter.Pos.X + dx * hitT,
                        originY,
                        shooter.Pos.Z + dz * hitT);

                    if (hitPos.Y >= hit.Pos.Y + CombatConfig.HeadHitLine)
                        dmg <<= CombatConfig.HeadshotDamageShift;

                    s.Cmds.Write(SimCommandKind.Damage, hit.Id, shooter.Id, dmg);
                    s.Events.Write(FrameEventKind.Hit, hit.Id, shooter.Id, dmg, hitPos);
                }
            }
        }
    }
}
