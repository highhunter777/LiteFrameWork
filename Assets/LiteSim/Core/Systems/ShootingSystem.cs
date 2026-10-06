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
                // **三维化（俯视角爆头——《俯视角三维命中与爆头判定专项设计》§3.1/§4.1）**：
                // 采集侧给出含 Y 分量的单位瞄准向量（旧口径恒 dy=0 ⇒ 射线恒水平）。
                float dy = inputs[i].AimY;

                // **逻辑枪口**（子弹出射点＝逻辑枪口而非本体中心）——<see cref="CombatConfig.MuzzleOrigin"/>
                // 单源（服务器可重建、回溯用历史帧 Yaw）。视觉枪口位属表现层（服务器无模型/动画、
                // 回溯无历史姿态）——常量偏移保留权威/确定/可回溯（朝向系 = 枪随身体转）。
                SimVector3 muzzle = CombatConfig.MuzzleOrigin(shooter.Pos, shooter.Yaw);

                // 判定单源（SimRaycast）：实体圆柱最近者 + 障碍最近者取近——
                // 障碍更近 ⇒ 截停（实体命中与并列时实体优先：障碍仅以严格更近获胜）。
                // **实体走三维入口（带 dy）**；障碍保持 2.5D（关卡障碍为地面级圆/盒，三维化无收益——
                // 见专项设计 §4.1「仅实体圆柱三维化」）。
                SimRaycast.RaycastEntities(s, shooterSlot, muzzle.X, muzzle.Y, muzzle.Z, dx, dz, dy,
                    CombatConfig.HitscanRange, out int hitSlot, out float hitT);
                // 障碍仍在XZ 平面按水平距离求解⇒**须折回三维参数**才能与 hitT 同量纲比较
                // （hitT 是沿三维单位方向的长度；障碍 t 是水平距离）。
                bool blockedByObstacle = RaycastObstacles3D(map, muzzle.X, muzzle.Y, muzzle.Z,
                    dx, dz, dy, CombatConfig.HitscanRange, out float obstacleT)
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
                    int dmg = CombatConfig.BaseDamage + rng.NextRange(-CombatConfig.DamageSpread, CombatConfig.DamageSpread + 1);
                    s.RngState = rng.State;

                    // **命中点取三维**（`+ dy * hitT`）：爆头判据的输入。旧 2.5D 恒用 muzzle.Y（水平），
                    // 三维化后命中点高度随仰角与距离变化——**这正是平地可爆头的物理来源**。
                    var hitPos = new SimVector3(
                        muzzle.X + dx * hitT,
                        muzzle.Y + dy * hitT,
                        muzzle.Z + dz * hitT);

                    // **爆头＝命中点落在头部带**（专项设计 §4.2）：头部带 = 身位顶部子区间
                    // **[HeadHitLine, HitscanHeight]**（相对目标脚底，闭区间）——非 >= 单边。
                    // 单边 `>= HeadHitLine` 有漏洞：高差位俯射**越过头顶**仍被判爆头；
                    // 与 Duckov HitBox 语义一致（头是一个区间而非半空间）。
                    //
                    // **边界须与 SimRaycast 的 Y 带闸同口径（闭区间含端点）**：既有用例
                    // 「高差位命中头部带」（射手 Y=1⇒ 眼高恰= 身位顶 2.0）依赖含端点语义——
                    // 单方面把上界收紧为 `<` 会与Y 带闸分叉（闸放行、爆头判据落空 ⇒ 判为未命中）。
                    // 上界"越过头顶"的漏洞由 SimRaycast 的 Y 带闸（`yHit > 顶` 排除）承担，
                    // 此处只需 `>= 下沿`（`>` 上界不可能到达：命中点已过Y 带闸）。
                    float relY = hitPos.Y - hit.Pos.Y;
                    bool headshot = relY >= CombatConfig.HeadHitLine;
                    if (headshot) dmg <<= CombatConfig.HeadshotDamageShift;

                    s.Cmds.Write(SimCommandKind.Damage, hit.Id, shooter.Id, dmg);
                    // **爆头发`Crit`、普通发 `Hit`**（专项设计 §4.2）：`Crit` 枚举早已预留且
                    // ShootingSystem 从不写它；**不进 checksum、不进快照**（帧内瞬态），故协议/基线
                    // 零改动。表现层链路已通（HitFeedbackDispatcher 传 Kind →伤害数字 Crit 档 → 红字）。
                    s.Events.Write(headshot ? FrameEventKind.Crit : FrameEventKind.Hit,
                        hit.Id, shooter.Id, dmg, hitPos);
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
