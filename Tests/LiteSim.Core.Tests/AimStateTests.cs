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