namespace LiteSim
{
    /// <summary>
    /// 射击判定系统（§3.3 顺序第 3 位，§3.4 hitscan）：
    /// 实体圆柱 + **静态障碍**取最近交点（几何单源 <see cref="SimRaycast"/>，与瞄准激光同源）；
    /// 命中实体 → Cmds.Write(Damage)；开火/命中 → Events.Write(Fire/Hit)；
    /// **障碍更近 → 子弹被截停**（只写 Fire，不写 Damage/Hit——本批起子弹不再穿墙；
    /// 与障碍并列（同 t）时实体优先——"墙面前的人"优先命中）。
    /// 本系统是唯一消费 RngState 的系统（确定性审计写在签名上——伤害浮动 ±1）；
    /// 打空/被墙截停不消耗 RngState（只有真实命中才进入伤害分支）。
    ///
    /// **服务器回溯**：LagCompensator 会把本系统**单独**跑在历史帧状态上（不 Step），
    /// 因此本系统必须满足两条：① 不改 Frame/时序；② 只读输入 + 写 Cmds/Events/RngState +
    /// 槽位开火窗（FireStanceFrames——与 Fire 事件同点置窗；回溯副本上的写入随副本丢弃，不入权威态）。
    /// 回溯判定用**静态地图**（障碍不随帧变化——历史帧与当前帧同一份），实体位取历史帧位。
    /// 回调方负责还原 RngState（回溯判定不该消费权威随机数）。
    /// </summary>
    public static class ShootingSystem
    {
        public static void Run(SimWorldState s, in SimMapData map, SimInputFrame[] inputs)
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

                // **逻辑枪口**（子弹出射点＝逻辑枪口而非本体中心）——<see cref="CombatConfig.MuzzleOrigin"/>
                // 单源（服务器可重建、回溯用历史帧 Yaw）。视觉枪口位属表现层（服务器无模型/动画、
                // 回溯无历史姿态）——常量偏移保留权威/确定/可回溯（朝向系 = 枪随身体转）。
                // 高度默认=眼高 ⇒ y 带闸与爆头带判据口径不变。
                SimVector3 muzzle = CombatConfig.MuzzleOrigin(shooter.Pos, shooter.Yaw);

                // 判定单源（SimRaycast）：实体圆柱最近者 + 障碍最近者取近——
                // 障碍更近 ⇒ 截停（实体命中与并列时实体优先：障碍仅以严格更近获胜）
                SimRaycast.RaycastEntities(s, shooterSlot, muzzle.X, muzzle.Y, muzzle.Z, dx, dz,
                    CombatConfig.HitscanRange, out int hitSlot, out float hitT);
                bool blockedByObstacle = SimRaycast.RaycastObstacles(map, muzzle.X, muzzle.Y, muzzle.Z,
                    dx, dz, CombatConfig.HitscanRange, out float obstacleT)
                    && (hitSlot < 0 || obstacleT < hitT);

                // 开火驻留窗置满（与 Fire 事件**同点**——View 侧窗口同触发同长度同刷新，
                // 事件刷新制重置满窗、上限即窗长；限速由 InputSystem 次帧起生效——本系统在输入之后跑）。
                // 服务器回溯：本字段随回溯副本丢弃，不入权威态——契约见类注释②。
                shooter.FireStanceFrames = (byte)CombatConfig.FireStanceFrames;

                s.Events.Write(FrameEventKind.Fire, shooter.Id, 0L, 0, shooter.Pos);

                if (hitSlot >= 0 && !blockedByObstacle)
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
                        muzzle.X + dx * hitT,
                        muzzle.Y,
                        muzzle.Z + dz * hitT);

                    if (hitPos.Y >= hit.Pos.Y + CombatConfig.HeadHitLine)
                        dmg <<= CombatConfig.HeadshotDamageShift;

                    s.Cmds.Write(SimCommandKind.Damage, hit.Id, shooter.Id, dmg);
                    s.Events.Write(FrameEventKind.Hit, hit.Id, shooter.Id, dmg, hitPos);
                }
            }
        }
    }
}
