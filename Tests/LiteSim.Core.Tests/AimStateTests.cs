using LiteSim;
using Xunit;

namespace LiteSim.Tests
{
    /// <summary>
    /// 瞄准态口径验收（《角色状态与动作专项设计》§7 的 **Sim 侧半边**，2026-09-27）：
    /// **限速**（ADS 期间移动上限 = 走路档；**2026-09-28 射击限速裁决：开火中（瞄准或腰射）同样限到
    /// 走路档**——与 ADS 同源 <see cref="CombatConfig.AimMoveSpeed"/>，防"全速走位 + 腰射"的火力机动优势；
    /// **2026-09-30 腰射批：开火驻留窗内限速保持**——点射停火帧不回跳全速）、
    /// **朝向派生**（瞄准/开火朝准星；**驻留窗内同跟准星**；都没有则保持）、
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
        public void 限速_开火移动同样降到走路档_腰射与瞄准一致()
        {
            long id = Spawn(out var world, out int slot);

            // 腰射（未瞄准但开火）→ 与 ADS 同限（2026-09-28 裁决：火力机动不优于瞄准射击）
            InputSystem.Run(world, Inputs(id, 1f, 0f, 1f, 0f, SimInputFrame.ButtonFire));
            Assert.Equal(CombatConfig.AimMoveSpeed, world.Entities[slot].Vel.X, 4);

            // 驻留窗内（停火帧）→ 仍走路档（2026-09-30 腰射批：点射停火帧不回跳全速）
            InputSystem.Run(world, Inputs(id, 1f, 0f, 1f, 0f));
            Assert.Equal(CombatConfig.AimMoveSpeed, world.Entities[slot].Vel.X, 4);

            // 窗尽 → 恢复全速
            for (int i = 0; i < CombatConfig.FireStanceFrames; i++)
                InputSystem.Run(world, Inputs(id, 1f, 0f, 1f, 0f));
            Assert.Equal(CombatConfig.MoveSpeed, world.Entities[slot].Vel.X, 4);
        }

        [Fact]
        public void 驻留窗_窗内限速且跟准星_窗尽一起回落()
        {
            long id = Spawn(out var world, out int slot);
            ref EntitySlot e = ref world.Entities[slot];

            // 移动中腰射一枪（准星 +X、移动 +Z）：开火帧 → 走路档 + 朝准星（与移动方向无关）
            InputSystem.Run(world, Inputs(id, 0f, 1f, 1f, 0f, SimInputFrame.ButtonFire));
            Assert.Equal(CombatConfig.AimMoveSpeed, e.Vel.Z, 4);
            Assert.Equal(0f, e.Yaw, 5);
            Assert.Equal(CombatConfig.FireStanceFrames, (int)e.FireStanceFrames);

            // 停火帧（窗内剩余 41 帧）→ 限速与朝准星一并保持——点射主诉场景不再回跳
            for (int i = 1; i < CombatConfig.FireStanceFrames; i++)
            {
                InputSystem.Run(world, Inputs(id, 0f, 1f, 1f, 0f));
                Assert.Equal(CombatConfig.AimMoveSpeed, e.Vel.Z, 4);
                Assert.Equal(0f, e.Yaw, 5);
            }

            // 窗尽 → 速度回全速、朝向回落移动向（+Z → Yaw = atan2(1,0)）
            InputSystem.Run(world, Inputs(id, 0f, 1f, 1f, 0f));
            Assert.Equal(CombatConfig.MoveSpeed, e.Vel.Z, 4);
            Assert.Equal(SimTrig.Atan2(1f, 0f), e.Yaw, 5);
            Assert.Equal(0, (int)e.FireStanceFrames);
        }

        [Fact]
        public void 驻留窗_窗内再点射_窗口重置()
        {
            long id = Spawn(out var world, out int slot);
            ref EntitySlot e = ref world.Entities[slot];

            // 第 1 枪（准星 +X）→ 停火帧推进至窗口余 10（42 - 32 次递减）
            InputSystem.Run(world, Inputs(id, 0f, 1f, 1f, 0f, SimInputFrame.ButtonFire));
            for (int i = 0; i < CombatConfig.FireStanceFrames - 10; i++)
                InputSystem.Run(world, Inputs(id, 0f, 1f, 1f, 0f));

            // 第 2 枪（新准星 -X）→ 窗口重置为满、朝新枪口向
            InputSystem.Run(world, Inputs(id, 0f, 1f, -1f, 0f, SimInputFrame.ButtonFire));
            Assert.Equal(SimTrig.Atan2(0f, -1f), e.Yaw, 5);
            Assert.Equal(CombatConfig.FireStanceFrames, (int)e.FireStanceFrames);

            // 重置后的整窗仍朝新准星（连点不衰减）
            for (int i = 1; i < CombatConfig.FireStanceFrames; i++)
            {
                InputSystem.Run(world, Inputs(id, 0f, 1f, -1f, 0f));
                Assert.Equal(SimTrig.Atan2(0f, -1f), e.Yaw, 5);
            }
        }

        [Fact]
        public void 驻留窗_静止点射_零准星不派生_窗尽保持最后朝向()
        {
            long id = Spawn(out var world, out int slot);
            ref EntitySlot e = ref world.Entities[slot];

            // 静止腰射（准星 -Z）→ 朝准星
            InputSystem.Run(world, Inputs(id, 0f, 0f, 0f, -1f, SimInputFrame.ButtonFire));
            float fired = SimTrig.Atan2(-1f, 0f);
            Assert.Equal(fired, e.Yaw, 5);

            // 静止 + 零准星（0,0）：窗内不拿零向量算 Atan2（保持），窗尽后无移动同样保持
            for (int i = 0; i < CombatConfig.FireStanceFrames + 5; i++)
                InputSystem.Run(world, Inputs(id, 0f, 0f, 0f, 0f));

            Assert.Equal(fired, e.Yaw, 5);
            Assert.Equal(0, (int)e.FireStanceFrames);
        }

        [Fact]
        public void 驻留窗_随CopyTo深拷_回滚重放基点可重建()
        {
            long id = Spawn(out var world, out int slot);

            InputSystem.Run(world, Inputs(id, 0f, 1f, 1f, 0f, SimInputFrame.ButtonFire));
            Assert.Equal(CombatConfig.FireStanceFrames, (int)world.Entities[slot].FireStanceFrames);

            var snapshot = new SimWorldState();
            world.CopyTo(snapshot);                                        // 回滚环/重放基点的同款原语
            InputSystem.Run(world, Inputs(id, 0f, 1f, 1f, 0f));           // 原世界继续推进（窗内第 2 帧）

            // 快照不被后续推进扰动（深拷）：回滚 Restore 回本帧后，驻留窗原样可重建
            Assert.Equal(CombatConfig.FireStanceFrames, (int)snapshot.Entities[slot].FireStanceFrames);
            Assert.Equal(CombatConfig.FireStanceFrames - 1, (int)world.Entities[slot].FireStanceFrames);
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