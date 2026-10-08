using LiteSim;
using Xunit;

namespace LiteSim.Tests
{
    /// <summary>
    /// 瞄准态口径验收（《角色状态与动作专项设计》§7 的 **Sim 侧半边**）：
    /// **限速**（ADS 期间移动上限 = 走路档）、**朝向派生**（瞄准/开火朝准星；否则朝移动方向；两者都没有则保持）、
    /// **标志位**（<see cref="EntityFlags"/> 每帧从输入位覆写）、**预测保留**（连续意图位在预测帧存活——
    /// "本地举枪不闪断"的 Sim 侧保证）。纯规则、零引擎。
    /// </summary>
    public sealed class AimStateTests
    {
        private static long Spawn(out SimWorldState world, out int slot)
        {
            world = new SimWorldState { RngState = 1UL };
            return world.Spawn(new EntitySlot { Hp = 100 }, out slot);
        }

        /// <summary>瞄准输入（AimPoint 单口径：点 = 本体（默认原点）远处 + 方向 × 10 ⇒ 与旧方向口径同 Yaw）。</summary>
        private static SimInputFrame[] Inputs(long id, float mx, float mz, float ax, float az, uint buttons = 0u)
            => new[] { new SimInputFrame { EntityId = id, MoveX = mx, MoveZ = mz, AimPointX = ax * 10f, AimPointY = 1f, AimPointZ = az * 10f, Buttons = buttons } };

        /// <summary>空障碍图（这些用例只验窗/朝向/限速——不参与障碍遮挡判定）。</summary>
        private static readonly SimMapData NoObstacles = new SimMapData();

        [Fact]
        public void 限速_瞄准移动降到走路档_松开窗内保持_窗尽恢复全速()
        {
            long id = Spawn(out var world, out int slot);

            InputSystem.Run(world, Inputs(id, 1f, 0f, 1f, 0f), CombatValues.Default);
            Assert.Equal(CombatConfig.MoveSpeed, world.Entities[slot].Vel.X, 4);

            InputSystem.Run(world, Inputs(id, 1f, 0f, 1f, 0f, SimInputFrame.ButtonAim), CombatValues.Default);
            Assert.Equal(CombatConfig.AimMoveSpeed, world.Entities[slot].Vel.X, 4);
            Assert.Equal(2.5f, CombatConfig.AimMoveSpeed, 4);   // = 视图 Walk 档上界（瞄准移动只需 AimWalk 一套片段）

            // 松开瞄准 → 瞄准帧已置满共用窗 ⇒ 窗内保持走路档（不瞬回全速）
            InputSystem.Run(world, Inputs(id, 1f, 0f, 1f, 0f), CombatValues.Default);
            Assert.Equal(CombatConfig.AimMoveSpeed, world.Entities[slot].Vel.X, 4);

            // 窗尽（递减 90 帧）→ 回全速
            for (int i = 0; i < CombatConfig.FireStanceFrames; i++)
                InputSystem.Run(world, Inputs(id, 1f, 0f, 1f, 0f), CombatValues.Default);
            Assert.Equal(CombatConfig.MoveSpeed, world.Entities[slot].Vel.X, 4);
        }

        // ---- 瞄准点按共用驻留窗——间隙帧不回摆/限速随窗/窗尽离场转向 ----

        [Fact]
        public void 朝向_点按瞄准_间隙帧不回移动向_窗尽速率回转()
        {
            long id = Spawn(out var world, out int slot);

            // 点按瞄准 + 反方向移动（与点射同款场景，共用一个窗口）：
            // 移动 +Z、准星 -X；按下瞄准→朝准星并置满共用窗；松开间隙帧窗内不回移动向
            var aimFrame = Inputs(id, 0f, 1f, -1f, 0f, SimInputFrame.ButtonAim);
            var gapFrame = Inputs(id, 0f, 1f, -1f, 0f);
            float crosshairYaw = SimTrig.Atan2(0f, -1f);
            float moveYaw = SimTrig.Atan2(1f, 0f);

            InputSystem.Run(world, aimFrame, CombatValues.Default);
            // 瞄准帧朝准星 + 置满共用窗
            Assert.Equal(crosshairYaw, world.Entities[slot].Yaw, 5);
            Assert.Equal((byte)CombatConfig.FireStanceFrames, world.Entities[slot].FireStanceFrames);

            InputSystem.Run(world, gapFrame, CombatValues.Default);
            // 点按间隙帧窗内保持准星向（不回移动向——与点射同一共用窗语义）
            Assert.Equal(crosshairYaw, world.Entities[slot].Yaw, 5);

            // 窗尽 → 离场转向（武装生效：按速率过渡回移动方向，不瞬切）
            for (int i = 0; i < CombatConfig.FireStanceFrames; i++)
                InputSystem.Run(world, gapFrame, CombatValues.Default);
            Assert.Equal(0, world.Entities[slot].FireStanceFrames);
            float before = world.Entities[slot].Yaw;
            float step = CombatConfig.FaceTurnRadPerSec * SimConfig.Dt;
            InputSystem.Run(world, gapFrame, CombatValues.Default);
            float after = world.Entities[slot].Yaw;
            Assert.True(after != before, "窗尽回转开始（不保持准星向）");
            Assert.True(System.Math.Abs(after - before) <= step + 1e-5f, "一帧一步过渡（不错切）");

            int guard = 0;
            while (world.Entities[slot].Yaw != moveYaw && guard++ < 300)
                InputSystem.Run(world, gapFrame, CombatValues.Default);
            Assert.Equal(moveYaw, world.Entities[slot].Yaw, 6);   // 最终精确落位移动方向
            Assert.Equal((byte)0, world.Entities[slot].FaceExitTurning);   // 到位解除武装
        }

        [Fact]
        public void 限速_点按瞄准_窗内保持走路档_窗尽回全速()
        {
            long id = Spawn(out var world, out int slot);
            var aimFrame = Inputs(id, 1f, 0f, 1f, 0f, SimInputFrame.ButtonAim);
            var gapFrame = Inputs(id, 1f, 0f, 1f, 0f);

            InputSystem.Run(world, aimFrame, CombatValues.Default);
            Assert.Equal(CombatConfig.AimMoveSpeed, world.Entities[slot].Vel.X, 4);   // 瞄准帧走路档

            InputSystem.Run(world, gapFrame, CombatValues.Default);
            Assert.Equal(CombatConfig.AimMoveSpeed, world.Entities[slot].Vel.X, 4);   // 间隙帧（窗内）保持走路档

            for (int i = 0; i < CombatConfig.FireStanceFrames; i++)
                InputSystem.Run(world, gapFrame, CombatValues.Default);
            Assert.Equal(CombatConfig.MoveSpeed, world.Entities[slot].Vel.X, 4);   // 窗尽回全速
        }

        // ---- 开火驻留窗（Sim 权威开火态——移动腰射按 aimwalk 移动）----

        [Fact]
        public void 开火窗_窗内限速走路档_窗尽回落全速_重放可重建()
        {
            long id = Spawn(out var world, out int slot);
            var fire = Inputs(id, 1f, 0f, 1f, 0f, SimInputFrame.ButtonFire);
            var move = Inputs(id, 1f, 0f, 1f, 0f);

            // 置窗帧：InputSystem 先跑（窗未置——仍全速），ShootingSystem 判定点置满窗（与 Fire 事件同点）
            InputSystem.Run(world, fire, CombatValues.Default);
            Assert.True(world.Entities[slot].Vel.X == CombatConfig.MoveSpeed,
                "置窗帧仍全速——窗在判定点才置，限速自次帧起生效（系统序：输入 → 射击判定）");
            ShootingSystem.Run(world, NoObstacles, fire, CombatValues.Default, WeaponTable.Default);
            Assert.True(world.Entities[slot].FireStanceFrames == (byte)CombatConfig.FireStanceFrames,
                "开火判定置满窗（事件刷新制——上限即窗长）");

            // 窗内：不再开火、只移动 → 限速走路档（移动腰射按 aimwalk 移动）
            InputSystem.Run(world, move, CombatValues.Default);
            Assert.True(world.Entities[slot].Vel.X == CombatConfig.AimMoveSpeed, "窗内移动被限到走路档");

            // 窗尽（自然结束）：递减到 0 → 回全速
            for (int i = 0; i < CombatConfig.FireStanceFrames; i++)
                InputSystem.Run(world, move, CombatValues.Default);
            Assert.True(world.Entities[slot].FireStanceFrames == 0, "窗尽归零（离开开火态即取消限速）");
            Assert.True(world.Entities[slot].Vel.X == CombatConfig.MoveSpeed, "窗尽后移动回全速");

            // 重放可重建：同输入序列再跑一个世界——窗计数与全量 checksum 逐位一致（私有面已进全量口径）
            long id2 = Spawn(out var world2, out int slot2);
            var fire2 = Inputs(id2, 1f, 0f, 1f, 0f, SimInputFrame.ButtonFire);
            var move2 = Inputs(id2, 1f, 0f, 1f, 0f);
            InputSystem.Run(world2, fire2, CombatValues.Default); ShootingSystem.Run(world2, NoObstacles, fire2, CombatValues.Default, WeaponTable.Default);
            InputSystem.Run(world2, move2, CombatValues.Default);
            for (int i = 0; i < CombatConfig.FireStanceFrames; i++) InputSystem.Run(world2, move2, CombatValues.Default);
            Assert.True(world.Entities[slot].FireStanceFrames == world2.Entities[slot2].FireStanceFrames,
                "同输入序列 ⇒ 同窗计数（确定性）");
            Assert.True(SimChecksum.ComputeStateChecksum(world) == SimChecksum.ComputeStateChecksum(world2),
                "两世界全量 checksum 逐位一致（开火窗进全量口径——重放对账可重建）");
        }

        [Fact]
        public void 开火窗_事件刷新重置满窗_持续射击窗不落()
        {
            long id = Spawn(out var world, out int slot);
            var fire = Inputs(id, 1f, 0f, 1f, 0f, SimInputFrame.ButtonFire);
            var move = Inputs(id, 1f, 0f, 1f, 0f);

            InputSystem.Run(world, fire, CombatValues.Default);
            ShootingSystem.Run(world, NoObstacles, fire, CombatValues.Default, WeaponTable.Default);                       // 置满：60

            // 走 30 帧（窗 → 30）再开一枪 → 重置回满窗（上限即窗长，不累加）
            for (int i = 0; i < 30; i++) InputSystem.Run(world, move, CombatValues.Default);
            Assert.True(world.Entities[slot].FireStanceFrames == (byte)(CombatConfig.FireStanceFrames - 30),
                "不开火的帧逐帧递减");
            InputSystem.Run(world, fire, CombatValues.Default);
            ShootingSystem.Run(world, NoObstacles, fire, CombatValues.Default, WeaponTable.Default);
            Assert.True(world.Entities[slot].FireStanceFrames == (byte)CombatConfig.FireStanceFrames,
                "事件刷新＝重置满窗（上限即窗长——持续射击窗不落）");

            // 再走 30 帧：窗仍有余量 → 仍限速（若窗没重置，30+30=60 早应归零回全速）
            for (int i = 0; i < 30; i++) InputSystem.Run(world, move, CombatValues.Default);
            Assert.True(world.Entities[slot].Vel.X == CombatConfig.AimMoveSpeed, "重置后的窗仍在——限速保持");
            Assert.True(world.Entities[slot].FireStanceFrames > 0, "重置后的窗尚有余量");
        }

        // ---- 朝向组合：窗内朝准星不回摆 ＋ 窗尽按转向速率平滑回转 ----

        [Fact]
        public void 朝向_开火窗内朝准星_点射间隙不回移动向()
        {
            long id = Spawn(out var world, out int slot);

            // 用户报告场景：移动 +Z、点射 -X（射击方向与移动方向相反）——期望窗内稳定面朝射击方向
            // （视图侧四向权重由"移动 vs 朝向"取角 ⇒ 稳定 AimWalk_B 的权威面）
            var fire = Inputs(id, 0f, 1f, -1f, 0f, SimInputFrame.ButtonFire);
            var gap = Inputs(id, 0f, 1f, -1f, 0f);                              // 间隙帧：同样移动/同样准星、不带开火位

            InputSystem.Run(world, fire, CombatValues.Default);
            ShootingSystem.Run(world, NoObstacles, fire, CombatValues.Default, WeaponTable.Default);                                    // 判定点置满窗 + 武装离场转向
            float crosshairYaw = SimTrig.Atan2(0f, -1f);
            Assert.True(world.Entities[slot].Yaw == crosshairYaw, "开火帧朝准星");

            // 窗内间隙帧：**不回移动方向**（否则视图四向权重 F/B 互顶——后退动画被淹没）
            for (int i = 0; i < CombatConfig.FireStanceFrames - 2; i++)
                InputSystem.Run(world, gap, CombatValues.Default);
            Assert.True(world.Entities[slot].Yaw == crosshairYaw,
                "窗内间隙帧保持朝准星——点射不逐拍回摆（AimWalk_B 的稳定前提）");
            Assert.True(world.Entities[slot].FireStanceFrames > 0, "断言前窗仍在");

            // 窗尽（自然结束）：武装的离场转向生效——首个移动帧不瞬切，按速率过渡
            for (int i = 0; i < 2; i++) InputSystem.Run(world, gap, CombatValues.Default);
            Assert.True(world.Entities[slot].FireStanceFrames == 0, "窗尽归零");
            float before = world.Entities[slot].Yaw;
            float step = CombatConfig.FaceTurnRadPerSec * SimConfig.Dt;
            InputSystem.Run(world, gap, CombatValues.Default);
            float after = world.Entities[slot].Yaw;
            Assert.True(after != before, "窗尽后回转开始（不保持准星向）");
            Assert.True(System.Math.Abs(after - before) <= step + 1e-5f,
                "窗尽回转按转向速率过渡（一帧一步，不瞬切）");

            // 过渡完成：精确落位移动方向，武装解除——后续移动恢复即时跟向（无速率过渡）
            float moveYaw = SimTrig.Atan2(1f, 0f);
            int guard = 0;
            while (world.Entities[slot].Yaw != moveYaw && guard++ < 300)
                InputSystem.Run(world, gap, CombatValues.Default);
            Assert.True(world.Entities[slot].Yaw == moveYaw, "转向速率最终精确落位移动方向");
            Assert.True(world.Entities[slot].FaceExitTurning == 0, "到位即解除武装");

            var east = Inputs(id, 1f, 0f, -1f, 0f);                            // 改向 +X（准星仍 -X 但无语境）
            InputSystem.Run(world, east, CombatValues.Default);
            Assert.True(world.Entities[slot].Yaw == SimTrig.Atan2(0f, 1f),
                "解除后恢复即时跟向（常态移动不受速率限制——既有手感不变）");
        }

        [Fact]
        public void 朝向_窗内静止保持准星向_窗尽静止不转向()
        {
            long id = Spawn(out var world, out int slot);

            // 站定点射（不移动）：窗内保持准星向；窗尽后仍静止 → 保持上一帧（离场转向无移动目标不触发）
            var fireStill = Inputs(id, 0f, 0f, -1f, 0f, SimInputFrame.ButtonFire);
            var stillGap = Inputs(id, 0f, 0f, -1f, 0f);

            InputSystem.Run(world, fireStill, CombatValues.Default);
            ShootingSystem.Run(world, NoObstacles, fireStill, CombatValues.Default, WeaponTable.Default);
            float crosshairYaw = SimTrig.Atan2(0f, -1f);
            Assert.True(world.Entities[slot].Yaw == crosshairYaw);

            for (int i = 0; i < CombatConfig.FireStanceFrames; i++)
                InputSystem.Run(world, stillGap, CombatValues.Default);                              // 窗内+窗尽：全程静止
            Assert.True(world.Entities[slot].FireStanceFrames == 0);
            Assert.True(world.Entities[slot].Yaw == crosshairYaw,
                "静止无移动目标 → 保持上一帧（离场转向不凭空转）");
            Assert.True(world.Entities[slot].FaceExitTurning != 0,
                "武装保持（下次移动才过渡）——由输入历史可重建");
        }

        [Fact]
        public void 朝向_未瞄准朝移动方向_瞄准或开火朝准星_都没有则保持()
        {
            long id = Spawn(out var world, out int slot);
            ref EntitySlot e = ref world.Entities[slot];

            // 未瞄准 + 移动 → 朝移动方向（+Z 移动 → Yaw = atan2(1,0)）
            InputSystem.Run(world, Inputs(id, 0f, 1f, 1f, 0f), CombatValues.Default);
            Assert.Equal(SimTrig.Atan2(1f, 0f), e.Yaw, 5);

            // 瞄准 → 朝准星（准星 +X → Yaw = 0；与移动方向无关，strafe 由此成立）
            InputSystem.Run(world, Inputs(id, 0f, 1f, 1f, 0f, SimInputFrame.ButtonAim), CombatValues.Default);
            Assert.Equal(0f, e.Yaw, 5);

            // 腰射（未瞄准但开火）→ 也朝准星：射击方向来自 Aim，身位必须跟枪口一致，否则子弹像从侧面飞出
            InputSystem.Run(world, Inputs(id, 0f, 1f, -1f, 0f, SimInputFrame.ButtonFire), CombatValues.Default);
            Assert.Equal(SimTrig.Atan2(0f, -1f), e.Yaw, 5);

            // 静止且未开火 → 保持上一帧（不拿零向量退化，且回放/重放可重建）
            float kept = e.Yaw;
            InputSystem.Run(world, Inputs(id, 0f, 0f, 0f, 0f), CombatValues.Default);
            Assert.Equal(kept, e.Yaw, 6);
        }

        [Fact]
        public void 标志_瞄准位每帧覆写_与实体标志位空间同源()
        {
            long id = Spawn(out var world, out int slot);

            InputSystem.Run(world, Inputs(id, 1f, 0f, 1f, 0f, SimInputFrame.ButtonAim), CombatValues.Default);
            Assert.True((world.Entities[slot].Flags & EntityFlags.Aiming) != 0u, "按住右键 = 瞄准位置位（远端经快照可见）");

            InputSystem.Run(world, Inputs(id, 1f, 0f, 1f, 0f), CombatValues.Default);
            Assert.True((world.Entities[slot].Flags & EntityFlags.Aiming) == 0u, "松开右键 = 瞄准位清零（连续状态，每帧覆写）");
        }

        [Fact]
        public void 预测_连续意图位存活_离散位与开火不进预测保留集()
        {
            var world = new SimWorldState { RngState = 1UL };
            long id = world.Spawn(new EntitySlot { Hp = 100 }, out int slot);

            // 冷启动模板带瞄准位；此后不喂任何真实输入 → 全部走预测帧
            var template = Inputs(id, 1f, 0f, 1f, 0f,
                SimInputFrame.ButtonAim | SimInputFrame.ButtonFire | SimInputFrame.ButtonReload);
            var sim = new RollbackSim(world, SimMapData.StandardBattleMap(), template, CombatValues.Default, WeaponTable.Default);

            sim.Tick(SimConfig.Dt);      // 第 1 步：吃模板
            sim.Tick(SimConfig.Dt);      // 第 2 步：预测帧（"沿用上一帧 + 只留连续位"）
            sim.Tick(SimConfig.Dt);      // 第 3 步

            Assert.True((sim.State.Entities[slot].Flags & EntityFlags.Aiming) != 0u,
                "连续意图位必须跟着预测帧沿用——否则本地举枪会在缺真实输入的帧闪断");

            // 保留集边界（守卫：新增连续位时改 PredictedButtons，别在回滚代码里散写位常量）
            Assert.True((SimInputFrame.PredictedButtons & SimInputFrame.DiscreteIntentButtons) == 0u,
                "离散意图位不得进入预测保留集（猜错代价极大）");
            Assert.True((SimInputFrame.PredictedButtons & SimInputFrame.ButtonFire) == 0u,
                "开火不预测（沿用模板的开火位会让预测帧持续开火）");
        }
    }
}