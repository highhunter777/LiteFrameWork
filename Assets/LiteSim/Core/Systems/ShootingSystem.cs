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

                // **武器门**（WeaponSystem 单源）：已装备实体需过"节拍 ∧ 弹匣 ∧ 非换弹/切枪"——
                // 拦截发生在**一切副作用之前**（不写 Fire 事件、不置开火窗、不消费 RngState）；
                // 该帧不开火 = 整条跳过。伤害/射程随武器表（tb_weapon）。
                // 未装备实体（测试/沙盒直调本系统的形态）：维持旧行为（逐帧可开火 + 兜底伤害/射程）。
                bool equipped = WeaponSystem.IsEquipped(s, shooterSlot, out WeaponDef wdef, out _);
                if (equipped && !WeaponSystem.TryConsumeShot(s, shooterSlot, s.Frame, out wdef)) continue;
                float range = equipped ? wdef.Range : CombatConfig.HitscanRange;
                int baseDamage = equipped ? wdef.Damage : CombatConfig.BaseDamage;

                // **判定方向 = 从逻辑枪口指向 AimPoint**（AimPoint 单口径，所见即所判——
                // 《固定斜视角射击方案专项设计》§5/§4）。
                //
                // AimPoint = 相机屏幕射线与「实体圆柱 →（未命中）地面」的交点（采集侧解算）。
                // 指向 P 求交：命中点**就是 P**（方向口径「从枪口沿方向」命中圆柱近弧、与准心所指
                // 存在高度偏差的旧缺陷消亡——实测俯角 30°/目标 20m/准心恰在头部下沿时旧口径命中
                // 1.672m ⇒「瞄着下沿却打不中」）。掩体天然处理：从枪口到 P 的射线上障碍更近即截停
                // （子弹不穿墙，与激光同源同向）。
                //
                // **零值/退化点（无点帧）= 无效开火**：只写 Fire 事件与开火窗，不产命中——
                // 方向字段已退役，不再有"回退方向口径"路径（旧客户端被 buildHash 门禁拒绝）。
                // **逻辑枪口**（子弹出射点＝逻辑枪口而非本体中心）——<see cref="CombatConfig.MuzzleOrigin"/>
                // 单源（服务器可重建、回溯用历史帧 Yaw）。视觉枪口位属表现层（服务器无模型/动画、
                // 回溯无历史姿态）——常量偏移保留权威/确定/可回溯（朝向系 = 枪随身体转）。
                var muzzle = CombatConfig.MuzzleOrigin(shooter.Pos, shooter.Yaw);

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

                // 无效开火（无点/与枪口重合）：开火窗与 Fire 事件照写（节奏/表现语义不变），不产命中
                if (!hasPoint)
                {
                    shooter.FireStanceFrames = (byte)CombatConfig.FireStanceFrames;
                    s.Events.Write(FrameEventKind.Fire, shooter.Id, 0L, 0, shooter.Pos);
                    continue;
                }

                // 判定单源（SimRaycast）：实体圆柱最近者 + 障碍最近者取近——
                // 障碍更近 ⇒ 截停（实体命中与并列时实体优先：障碍仅以严格更近获胜）。
                // **实体走三维入口（带 dy）**；障碍保持 2.5D（关卡障碍为地面级圆/盒，三维化无收益——
                // 见专项设计 §4.1「仅实体圆柱三维化」）。
                SimRaycast.RaycastEntities(s, shooterSlot, in muzzle,
                    new SimVector3(dx, dy, dz), range, out int hitSlot, out float hitT);
                // 障碍仍在XZ 平面按水平距离求解⇒**须折回三维参数**才能与 hitT 同量纲比较
                // （hitT 是沿三维单位方向的长度；障碍 t 是水平距离）。
                bool blockedByObstacle = RaycastObstacles3D(map, muzzle.X, muzzle.Y, muzzle.Z,
                    dx, dz, dy, range, out float obstacleT)
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
                    // base/spread 走 CombatConfig（数值参数化）。
                    var rng = new SimRng(s.RngState);
                    int dmg = baseDamage + rng.NextRange(-CombatConfig.DamageSpread, CombatConfig.DamageSpread + 1);
                    s.RngState = rng.State;

                    // **命中点取三维**（`+ dy * hitT`）：爆头判据的输入。旧 2.5D 恒用 muzzle.Y（水平），
                    // 三维化后命中点高度随仰角与距离变化——**这正是平地可爆头的物理来源**。
                    var hitPos = new SimVector3(
                        muzzle.X + dx * hitT,
                        muzzle.Y + dy * hitT,
                        muzzle.Z + dz * hitT);

                    // **爆头判定高度 = 准心射线命中点 AimPoint.Y**（"准心指到哪就按哪判"），
                    // 而非子弹射线交点 hitPos.Y。
                    //
                    // **为什么不能用 hitPos.Y**：子弹射线自**枪口**出发，与相机屏幕射线**起点不同**
                    // ⇒ 两者与目标圆柱的**首次交点（近弧）也不同**。实测俯角 30°/目标 20m/准心
                    // 恰在头部下沿时：准心射线命中 1.70m，而子弹近弧落在 ≈1.67m ⇒ 跌出爆头带 ⇒
                    //「明明瞄着头却只是普通命中」。AimPoint 携带的正是准心射线那一交点的高度。
                    //
                    // **两件事分工**：**命中谁**由子弹射线求交决定（含掩体遮挡——这正是
                    // 「从枪口到 AimPoint 再做一次射线」的作用）；**爆头判定**用 AimPoint 的高度。
                    //
                    // **归属校验**：AimPoint 必须确实落在**这个**命中目标身上（水平距离 ≤ 半径），
                    // 否则回退子弹交点——近处有遮挡物时子弹抓到的是它、而准心指着远处目标，
                    // 拿远处的 AimPoint 去判近处的目标会误判。
                    float judgeY = hitPos.Y;
                    if (hasPoint)
                    {
                        float ddx = inputs[i].AimPointX - hit.Pos.X;
                        float ddz = inputs[i].AimPointZ - hit.Pos.Z;
                        if (SimMath.MulAdd2(ddx, ddx, ddz, ddz)
                            <= CombatConfig.HitscanRadius * CombatConfig.HitscanRadius)
                            judgeY = inputs[i].AimPointY;
                    }

                    // **爆头＝判定高度落在头部带**（专项设计 §4.2/§5）：命中几何已是**双柱阶梯**
                    // （<c>SimRaycast</c>——身体柱 [0,HeadHitLine)×HitscanRadius ＋ 爆头柱
                    // [HeadHitLine,HitscanHeight]×HeadshotRadius）⇒ **判定点高度 ≥ 下沿 ⇔ 命中在爆头柱**，
                    // 水平归属由几何承载——判定侧不再设事后水平闸（事后闸会拒掉"瞄头"的柱面 AimPoint：
                    // 单柱下瞄头的射线在 0.45 柱面取点、水平距恒 ≈0.45，实测"准心瞄头打中却是白字"）。
                    //
                    // **边界须与 SimRaycast 的 Y 带闸同口径（闭区间含端点）**：既有用例
                    // 「高差位擦顶命中」（弹道掠过柱顶、区间恰好相交）依赖含端点语义——
                    // 单方面把上界收紧为 `<` 会与 Y 带闸分叉（闸放行、爆头判据落空 ⇒ 判为未命中）。
                    // 上界"越过头顶"的漏洞由 SimRaycast 的 Y 带闸（`yHit > 顶` 排除）承担。
                    float relY = judgeY - hit.Pos.Y;
                    bool headshot = relY >= CombatConfig.HeadHitLineLive;       // Live：测试模式滑杆覆写优先（release 恒 = HeadHitLine）
                    if (headshot) dmg <<= CombatConfig.HeadshotDamageShift;

                    s.Cmds.Write(SimCommandKind.Damage, hit.Id, shooter.Id, dmg);
                    // **爆头发`Crit`、普通发 `Hit`**（专项设计 §4.2）：`Crit` 枚举早已预留且
                    // ShootingSystem 从不写它；**不进 checksum、不进快照**（帧内瞬态），故协议/基线
                    // 零改动。表现层链路已通（HitFeedbackDispatcher 传 Kind →伤害数字 Crit 档 → 红字）。
                    s.Events.Write(headshot ? FrameEventKind.Crit : FrameEventKind.Hit,
                        hit.Id, shooter.Id, dmg, new SimVector3(hitPos.X, judgeY, hitPos.Z));
                }
            }
        }

        /// <summary>
        /// 障碍求交的**三维折算**（专项设计 §4.1「仅实体圆柱三维化」）：
        /// <see cref="SimRaycast.RaycastObstacles"/> 在 XZ 平面按**水平距离**求解（口径不变——
        /// 关卡障碍均为地面级圆/盒，三维化收益为零而破坏面大），但调用方需要与实体
        /// <c>hitT</c> **同量纲**（沿三维单位方向的长度）比较，故此处把水平距离折回三维参数：
        /// <c>t_3d = t_xz / h</c>（h = 水平投影长度 √(dx²+dz²)）。
        /// **dy = 0 时 h = 1 ⇒ 逐位等价旧的二维调用**（既有沙盒/激光口径不变）。
        /// 近乎垂直（h ≤ <see cref="SimRaycast.ParallelEpsilon"/>）时水平投影退化，
        /// 障碍在水平方向不可达 ⇒ 判未命中（不与实体争近远）。
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
