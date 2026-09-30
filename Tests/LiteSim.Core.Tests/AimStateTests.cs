using LiteSim;
using Xunit;

namespace LiteSim.Tests
{
    /// <summary>
    /// 瞄准态口径验收（《角色状态与动作专项设计》§7 的 **Sim 侧半边**，2026-09-27）：
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

        private static SimInputFrame[] Inputs(long id, float mx, float mz, float ax, float az, uint buttons = 0u)
            => new[] { new SimInputFrame { EntityId = id, MoveX = mx, MoveZ = mz, AimX = ax, AimZ = az, Buttons = buttons } };

        [Fact]
        public void 限速_瞄准移动降到走路档_松开恢复全速()
        {
            long id = Spawn(out var world, out int slot);

            InputSystem.Run(world, Inputs(id, 1f, 0f, 1f, 0f));
            Assert.Equal(CombatConfig.MoveSpeed, world.Entities[slot].Vel.X, 4);

            InputSystem.Run(world, Inputs(id, 1f, 0f, 1f, 0f, SimInputFrame.ButtonAim));
            Assert.Equal(CombatConfig.AimMoveSpeed, world.Entities[slot].Vel.X, 4);
            Assert.Equal(2.5f, CombatConfig.AimMoveSpeed, 4);   // = 视图 Walk 档上界（瞄准移动只需 AimWalk 一套片段）

            InputSystem.Run(world, Inputs(id, 1f, 0f, 1f, 0f));
            Assert.Equal(CombatConfig.MoveSpeed, world.Entities[slot].Vel.X, 4);
        }

        // ---- 开火驻留窗（批次C：Sim 权威开火态——三次裁决"移动腰射按 aimwalk 移动"的 Sim 侧落地）----

        [Fact]
        public void 开火窗_窗内限速走路档_窗尽回落全速_重放可重建()
        {
            long id = Spawn(out var world, out int slot);
            var fire = Inputs(id, 1f, 0f, 1f, 0f, SimInputFrame.ButtonFire);
            var move = Inputs(id, 1f, 0f, 1f, 0f);

            // 置窗帧：InputSystem 先跑（窗未置——仍全速），ShootingSystem 判定点置满窗（与 Fire 事件同点）
            InputSystem.Run(world, fire);
            Assert.True(world.Entities[slot].Vel.X == CombatConfig.MoveSpeed,
                "置窗帧仍全速——窗在判定点才置，限速自次帧起生效（系统序：输入 → 射击判定）");
            ShootingSystem.Run(world, fire);
            Assert.True(world.Entities[slot].FireStanceFrames == (byte)CombatConfig.FireStanceFrames,
                "开火判定置满窗（事件刷新制——上限即窗长）");

            // 窗内：不再开火、只移动 → 限速走路档（移动腰射按 aimwalk 移动）
            InputSystem.Run(world, move);
            Assert.True(world.Entities[slot].Vel.X == CombatConfig.AimMoveSpeed, "窗内移动被限到走路档");

            // 窗尽（自然结束）：递减到 0 → 回全速
            for (int i = 0; i < CombatConfig.FireStanceFrames; i++)
                InputSystem.Run(world, move);
            Assert.True(world.Entities[slot].FireStanceFrames == 0, "窗尽归零（离开开火态即取消限速）");
            Assert.True(world.Entities[slot].Vel.X == CombatConfig.MoveSpeed, "窗尽后移动回全速");

            // 重放可重建：同输入序列再跑一个世界——窗计数与全量 checksum 逐位一致（私有面已进全量口径）
            long id2 = Spawn(out var world2, out int slot2);
            var fire2 = Inputs(id2, 1f, 0f, 1f, 0f, SimInputFrame.ButtonFire);
            var move2 = Inputs(id2, 1f, 0f, 1f, 0f);
            InputSystem.Run(world2, fire2); ShootingSystem.Run(world2, fire2);
            InputSystem.Run(world2, move2);
            for (int i = 0; i < CombatConfig.FireStanceFrames; i++) InputSystem.Run(world2, move2);
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

            InputSystem.Run(world, fire);
            ShootingSystem.Run(world, fire);                       // 置满：60

            // 走 30 帧（窗 → 30）再开一枪 → 重置回满窗（上限即窗长，不累加）
            for (int i = 0; i < 30; i++) InputSystem.Run(world, move);
            Assert.True(world.Entities[slot].FireStanceFrames == (byte)(CombatConfig.FireStanceFrames - 30),
                "不开火的帧逐帧递减");
            InputSystem.Run(world, fire);
            ShootingSystem.Run(world, fire);
            Assert.True(world.Entities[slot].FireStanceFrames == (byte)CombatConfig.FireStanceFrames,
                "事件刷新＝重置满窗（上限即窗长——持续射击窗不落）");

            // 再走 30 帧：窗仍有余量 → 仍限速（若窗没重置，30+30=60 早应归零回全速）
            for (int i = 0; i < 30; i++) InputSystem.Run(world, move);
            Assert.True(world.Entities[slot].Vel.X == CombatConfig.AimMoveSpeed, "重置后的窗仍在——限速保持");
            Assert.True(world.Entities[slot].FireStanceFrames > 0, "重置后的窗尚有余量");
        }

        [Fact]
        public void 朝向_未瞄准朝移动方向_瞄准或开火朝准星_都没有则保持()
        {
            long id = Spawn(out var world, out int slot);
            ref EntitySlot e = ref world.Entities[slot];

            // 未瞄准 + 移动 → 朝移动方向（+Z 移动 → Yaw = atan2(1,0)）
            InputSystem.Run(world, Inputs(id, 0f, 1f, 1f, 0f));
            Assert.Equal(SimTrig.Atan2(1f, 0f), e.Yaw, 5);

            // 瞄准 → 朝准星（准星 +X → Yaw = 0；与移动方向无关，strafe 由此成立）
            InputSystem.Run(world, Inputs(id, 0f, 1f, 1f, 0f, SimInputFrame.ButtonAim));
            Assert.Equal(0f, e.Yaw, 5);

            // 腰射（未瞄准但开火）→ 也朝准星：射击方向来自 Aim，身位必须跟枪口一致，否则子弹像从侧面飞出
            InputSystem.Run(world, Inputs(id, 0f, 1f, -1f, 0f, SimInputFrame.ButtonFire));
            Assert.Equal(SimTrig.Atan2(0f, -1f), e.Yaw, 5);

            // 静止且未开火 → 保持上一帧（不拿零向量退化，且回放/重放可重建）
            float kept = e.Yaw;
            InputSystem.Run(world, Inputs(id, 0f, 0f, 0f, 0f));
            Assert.Equal(kept, e.Yaw, 6);
        }

        [Fact]
        public void 标志_瞄准位每帧覆写_与实体标志位空间同源()
        {
            long id = Spawn(out var world, out int slot);

            InputSystem.Run(world, Inputs(id, 1f, 0f, 1f, 0f, SimInputFrame.ButtonAim));
            Assert.True((world.Entities[slot].Flags & EntityFlags.Aiming) != 0u, "按住右键 = 瞄准位置位（远端经快照可见）");

            InputSystem.Run(world, Inputs(id, 1f, 0f, 1f, 0f));
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
            var sim = new RollbackSim(world, SimMapData.StandardBattleMap(), template);

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