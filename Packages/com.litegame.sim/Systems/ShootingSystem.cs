namespace LiteSim
{
    /// <summary>
    /// 射击判定系统（§3.3 顺序第 3 位，§3.4 hitscan）： 实体圆柱 + **静态障碍**取最近交点（几何单源 <see cref="SimRaycast"/>，与瞄准激光同源）。
    /// </summary>
    public static class ShootingSystem
    {
        /// <summary><paramref name="rules"/> = 本局测试规则（null = 无规则——沿传 WeaponSystem.TryConsumeShot）。
        /// <paramref name="resolveCombat"/> = false 时进入**预测模式**：武器消费/开火窗/Fire/ObstacleHit
        /// 与 RNG 推进照常，但不写 Damage 命令、不产 Hit/Crit 事件——客户端不能自行决定命中/伤害/
        /// 死亡（《状态同步专项设计》§6.1/§6.2，权威结果只由快照与权威反馈面驱动）。</summary>
        public static void Run(SimWorldState s, in SimMapData map, SimInputFrame[] inputs, in CombatValues values,
            WeaponTable weapons, MatchTestRules rules = null, bool resolveCombat = true)
        {
            if (s.Match.Phase == SimMatchPhase.Finished) return; // lint-allow R3（整数阶段）
            for (int i = 0; i < inputs.Length; i++)
            {
                if ((inputs[i].Buttons & SimInputFrame.ButtonFire) == 0u) continue;
                if (!s.TryResolve(inputs[i].EntityId, out int shooterSlot)) continue;

                ref EntitySlot shooter = ref s.Entities[shooterSlot];

                // 死亡射手不开火（Hp≤0：窗/事件/命中判定全跳过——尸体不受操控）
                if (shooter.Hp <= 0) continue;

                // **武器门**（WeaponSystem 单源）：已装备实体需过"节拍 ∧ 弹匣 ∧ 非换弹/切枪 ∧ 半自动扳机"—— 拦截发生在**一切副作用之前**（不写 Fire 事件、不置开火窗、不消费 RngState）； 该帧不开火 = 整条跳过。
                bool fireFlag = (inputs[i].Buttons & SimInputFrame.ButtonFireFlag) != 0u;
                bool equipped = WeaponSystem.IsEquipped(s, shooterSlot, weapons, out WeaponDef wdef, out _);
                if (equipped && !fireFlag && !WeaponSystem.TryConsumeShot(s, shooterSlot, s.Frame, weapons, out wdef, rules)) continue;
                if (!fireFlag) shooter.InvulnerableUntilFrame = 0;
                float range = equipped ? wdef.Range : values.HitscanRange;
                int baseDamage = equipped ? MatchTestRules.ResolveWeaponDamage(rules, wdef.Damage) : values.BaseDamage;
                int pellets = equipped ? wdef.Pellets : 1;
                if (pellets < 1) pellets = 1;

                // **判定方向 = 从逻辑枪口指向 AimPoint**（AimPoint 单口径，所见即所判—— 《固定斜视角射击方案专项设计》§5/§4）。
                var muzzle = values.Geometry.MuzzleOrigin(shooter.Pos, shooter.Yaw);

                float dx = 0f, dy = 0f, dz = 0f;
                bool hasPoint = SimMath.MulAdd3(inputs[i].AimPointX, inputs[i].AimPointX,
                    inputs[i].AimPointY, inputs[i].AimPointY,
                    inputs[i].AimPointZ, inputs[i].AimPointZ) > 0f;
                if (hasPoint)
                {
                    double ax = inputs[i].AimPointX - muzzle.X;
                    double ay = inputs[i].AimPointY - muzzle.Y;
                    double az = inputs[i].AimPointZ - muzzle.Z;
                    double len = SimMath.Sqrt((float)(ax * ax + ay * ay + az * az));
                    if (len > 1e-6)
                    {
                        float inv = (float)(1.0 / len);
                        dx = (float)ax * inv; dy = (float)ay * inv; dz = (float)az * inv;
                    }
                    else { hasPoint = false; }                     // 与枪口重合 → 无效开火
                }

                // 开火驻留窗置满（与 Fire 事件**同点**——View 侧窗口同触发同长度同刷新， 事件刷新制重置满窗、上限即窗长；限速由 InputSystem 次帧起生效——本系统在输入之后跑）。
                shooter.FireStanceFrames = (byte)values.FireStanceFrames;

                s.Events.Write(FrameEventKind.Fire, shooter.Id, 0L, 0, shooter.Pos);

                // 无效开火（无点/与枪口重合）：窗与 Fire 事件照写（节奏/表现语义不变），不产命中
                if (!hasPoint) continue;

                // **散布**（一次扣扳机算一次口径——每弹丸各自偏转；瞄准态取本帧 Flags，回溯取历史帧）： 表值 = 瞄准态最大偏转角；腰射 × HipSpreadFactor（"瞄准降低散布，腰射反之"）。
                float spreadDeg = equipped ? wdef.Spread : 0f;
                if ((shooter.Flags & EntityFlags.Aiming) == 0u) spreadDeg *= values.Geometry.HipSpreadFactor;
                float spreadRad = spreadDeg * SimSpread.DegToRad;

                // **弹丸环**（pellets=1 即单弹丸原口径）：每弹丸独立「偏转 → 求交 → 伤害」；
                // 弹丸序恒定（0..pellets-1）⇒ 随机流逐位确定。
                var aimDir = new SimVector3(dx, dy, dz);
                for (int p = 0; p < pellets; p++)
                {
                    var dir = aimDir;
                    if (spreadRad > 0f)
                    {
                        var rng = new SimRng(s.RngState);
                        float angle = rng.NextFloat01() * spreadRad;      // 偏转角：均匀 [0, spread]
                        float azimuth = rng.NextFloat01() * SimTrig.TwoPi; // 方位角：均匀 [0, 2π)
                        s.RngState = rng.State;
                        dir = SimSpread.Deflect(in aimDir, angle, azimuth);
                    }

                    // 判定单源（SimRaycast）：实体圆柱最近者 + 障碍最近者取近—— 障碍更近 ⇒ 截停（实体命中与并列时实体优先：障碍仅以严格更近获胜）。
                    SimRaycast.RaycastEntities(s, shooterSlot, in muzzle,
                        in dir, range, out int hitSlot, out float hitT, values.Geometry);
                    // 障碍仍在XZ 平面按水平距离求解 ⇒ **须折回三维参数**才能与 hitT 同量纲比较
                    // （hitT 是沿三维单位方向的长度；障碍 t 是水平距离）。
                    bool blockedByObstacle = RaycastObstacles3D(map, muzzle.X, muzzle.Y, muzzle.Z,
                        dir.X, dir.Z, dir.Y, range, out float obstacleT)
                        && (hitSlot < 0 || obstacleT < hitT);

                    if (hitSlot < 0 || blockedByObstacle)
                    {
                        // 着墙：该弹丸被障碍截停 → ObstacleHit 帧事件（弹孔/着墙反馈的权威产出点）。
                        if (blockedByObstacle)
                        {
                            s.Events.Write(FrameEventKind.ObstacleHit, shooter.Id, 0L, 0,
                                new SimVector3(muzzle.X + dir.X * obstacleT,
                                               muzzle.Y + dir.Y * obstacleT,
                                               muzzle.Z + dir.Z * obstacleT));
                        }

                        continue;                                     // 该弹丸未命中实体（截停/脱靶）
                    }

                    ref EntitySlot hit = ref s.Entities[hitSlot];
                    if (s.Frame < hit.InvulnerableUntilFrame) continue; // 保护目标挡子弹，但不产命中/伤害

                    // 伤害浮动 ±DamageSpread（消费 RngState——局部副本推进后写回，SimRng 使用约定）；
                    // base/spread 走装载实例（数值参数化——债 #1 根治面）。
                    var rngDmg = new SimRng(s.RngState);
                    int dmg = baseDamage + rngDmg.NextRange(-values.DamageSpread, values.DamageSpread + 1);
                    s.RngState = rngDmg.State;

                    // **命中点取三维**（沿弹丸方向）：爆头判据的输入。
                    var hitPos = new SimVector3(
                        muzzle.X + dir.X * hitT,
                        muzzle.Y + dir.Y * hitT,
                        muzzle.Z + dir.Z * hitT);

                    // **爆头判定高度 = 准心射线命中点 AimPoint.Y**（"准心指到哪就按哪判"）， 而非子弹射线交点 hitPos.Y。
                    float judgeY = hitPos.Y;
                    if (hasPoint)
                    {
                        float ddx = inputs[i].AimPointX - hit.Pos.X;
                        float ddz = inputs[i].AimPointZ - hit.Pos.Z;
                        if (SimMath.MulAdd2(ddx, ddx, ddz, ddz)
                            <= values.Geometry.HitscanRadius * values.Geometry.HitscanRadius)
                            judgeY = inputs[i].AimPointY;
                    }

                    // **爆头＝判定高度落在头部带**（专项设计 §4.2/§5）：命中几何已是**双柱阶梯** （<c>SimRaycast</c>——身体柱 [0,HeadHitLine)×HitscanRadius ＋ 爆头柱 [HeadHitLine,HitscanHeight]×Headsh。）。
                    float relY = judgeY - hit.Pos.Y;
                    bool headshot = relY >= values.Geometry.HeadHitLine;   // 本局几何；事后水平闸已退役
                    if (headshot) dmg <<= values.Geometry.HeadshotDamageShift;

                    // 回溯请求只延后命中副作用；权威帧的散布/随机流仍正常推进一次。
                    if ((inputs[i].Buttons & SimInputFrame.ButtonDeferHit) != 0u) continue;
                    // 客户端预测模式：散布/伤害随机流已按上方消费（与服务端前向步进同序），但
                    // 命中/伤害/死亡是权威事实——不写命令、不产 Hit/Crit 事件（权威反馈面另行下发）。
                    if (!resolveCombat) continue;
                    s.Cmds.Write(SimCommandKind.Damage, hit.Id, shooter.Id, dmg);
                    // **爆头发`Crit`、普通发 `Hit`**（专项设计 §4.2）：`Crit` 枚举早已预留且 ShootingSystem 从不写它；**不进 checksum、不进快照**（帧内瞬态），故协议/基线 零改动。
                    s.Events.Write(headshot ? FrameEventKind.Crit : FrameEventKind.Hit,
                        hit.Id, shooter.Id, dmg, new SimVector3(hitPos.X, judgeY, hitPos.Z));
                }
            }
        }

        /// <summary>
        /// 障碍求交的**三维折算**（专项设计 §4.1「仅实体圆柱三维化」）： <see cref="SimRaycast.RaycastObstacles"/> 在 XZ 平面按**水平距离**求解（口径不变—— 关卡障碍均为。）。
        /// </summary>
        private static bool RaycastObstacles3D(in SimMapData map,
            float ox, float oy, float oz, float dx, float dz, float dy,
            float maxT, out float t)
        {
            float h2 = SimMath.MulAdd2(dx, dx, dz, dz);
            float h = SimMath.Sqrt(h2);
            if (h <= SimRaycast.ParallelEpsilon) { t = maxT; return false; }

            // 障碍侧仍按 XZ 平面求解：方向需归一到水平单位向量（长度 1），
            // 使其返回值是"水平距离"而非三维长度。
            float invH = 1f / h;
            if (!SimRaycast.RaycastObstacles(map, ox, oy, oz, dx * invH, dz * invH, maxT, out float tFlat))
            {
                t = maxT;
                return false;
            }

            t = tFlat * invH;         // 水平距离 → 三维参数（与 hitT 同量纲）
            return true;
        }
    }
}
