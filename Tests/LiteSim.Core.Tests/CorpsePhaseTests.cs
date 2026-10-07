using LiteSim;
using Xunit;

namespace LiteSim.Tests
{
    /// <summary>
    /// 尸体期集成链（死亡链路审计的根治面）：死亡帧 → **事件可达**（槽位未回收——
    /// 驱动器 TryGetSlot 不再门口丢失）→ **尸体期槽位存活**（视图载体/快照重建/重连可见）→
    /// 期满回收。覆盖此前的审计盲区：Cleanup 同帧回收让死亡事件在驱动器门口丢失、视图载体即逝。
    /// 全链走真实 <see cref="SimStep.Step"/>（非单系统直调）。
    /// </summary>
    public sealed class CorpsePhaseTests
    {
        private static SimMapData Map() => new SimMapData { GroundY = 0f, HalfWidth = 50f, HalfDepth = 50f };

        private static (SimWorldState world, long shooter, long target, int shooterSlot, int targetSlot)
            SpawnPair(int targetHp = 100)
        {
            var world = new SimWorldState { RngState = 1UL };
            long shooter = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f) }, out int ss);
            long target = world.Spawn(new EntitySlot { Hp = targetHp, Pos = new SimVector3(3f, 0f, 0f) }, out int ts);
            return (world, shooter, target, ss, ts);
        }

        private static SimInputFrame[] FireAt(long id, float px = 3f, float pz = 0f)
            => new[] { new SimInputFrame { EntityId = id, AimPointX = px, AimPointY = 1f, AimPointZ = pz, Buttons = SimInputFrame.ButtonFire } };

        private static bool HasEvent(SimWorldState world, FrameEventKind kind)
        {
            for (int i = 0; i < world.Events.Count; i++)
                if (world.Events.Items[i].Kind == kind)
                    return true;
            return false;
        }

        private static bool Alive(SimWorldState world, int slot)
            => (world.AliveBitmap[slot >> 5] & (1u << (slot & 31))) != 0u;

        [Fact]
        public void 死亡帧_事件可达_尸体期槽位存活()
        {
            var (world, shooter, target, _, targetSlot) = SpawnPair(targetHp: 1);

            SimStep.Step(world, Map(), FireAt(shooter));

            // Step 后（Cleanup 已跑）事件缓冲仍含 Death——事件交付在逻辑帧边界，未被清理吞掉
            Assert.True(HasEvent(world, FrameEventKind.Death), "死亡帧应写出 Death 事件");
            // **路由可达性**（审计缺口①）：槽位未同帧回收——驱动器 TryGetSlot 不再门口丢失
            Assert.True(world.TryResolve(target, out int slot), "尸体期内槽位存活——死亡事件可路由");
            Assert.True(world.Entities[slot].Hp <= 0, "死亡事实（Hp≤0）");
            Assert.True(Alive(world, targetSlot), "载体存续（bitmap 保持——视图不即逝、远端可见）");
        }

        [Fact]
        public void 尸体期_零交互_定身不滑行()
        {
            var (world, shooter, target, _, targetSlot) = SpawnPair(targetHp: 1);

            // 先让目标跑起来（获得末速度），再打死——定身判据：清水平速度
            SimStep.Step(world, Map(), new[] { new SimInputFrame { EntityId = target, MoveX = 1f } });
            SimStep.Step(world, Map(), FireAt(shooter));
            Assert.True(world.Entities[targetSlot].Hp <= 0, "前置：目标已死");

            float velX = world.Entities[targetSlot].Vel.X;
            Assert.Equal(0f, velX, 4);                     // 跨线定身：不清则按末速度滑行整个尸体期

            // 尸体期内：被打不命中、自己开火无产出（InputSystem/ShootingSystem 双守卫）。
            // 事件缓冲由驱动消费后清（测试里手动模拟驱动清空——否则上一段的事件会污染断言）
            world.Events.Clear();
            SimStep.Step(world, Map(), FireAt(target));           // 尸体扣扳机
            Assert.False(HasEvent(world, FrameEventKind.Fire), "尸体不开火");
            world.Events.Clear();
            SimStep.Step(world, Map(), FireAt(shooter));          // 活人打尸体
            Assert.False(HasEvent(world, FrameEventKind.Hit), "尸体不可命中");
        }

        [Fact]
        public void 尸体期满_递减回收_槽位清零()
        {
            var (world, shooter, target, _, targetSlot) = SpawnPair(targetHp: 1);
            long targetId = target;

            SimStep.Step(world, Map(), FireAt(shooter));
            Assert.True(Alive(world, targetSlot), "前置：尸体期内存活");

            for (int i = 0; i < CombatConfig.CorpseFrames; i++)
                SimStep.Step(world, Map(), new SimInputFrame[0]);  // 空输入推进——含死亡帧本身的一次递减

            Assert.False(Alive(world, targetSlot), "期满回收（bitmap 清）");
            Assert.Equal(default, world.Entities[targetSlot]);   // 槽位清零——空槽校验值恒定（§3.6）
            Assert.False(world.TryResolve(targetId, out _), "回收后 Id 失效（版本语义不变——版本只在 Spawn 递增）");

            // 回收后 Spawn 正常：游标轮转（_nextFree）优先取新空槽——回收槽位不立即复用
            // （Id 防复用语义）；版本只在 Spawn 递增（#8）
            long respawned = world.Spawn(new EntitySlot { Hp = 100 }, out int newSlot);
            Assert.NotEqual(0L, respawned);
            Assert.True(Alive(world, newSlot));
            Assert.NotEqual(targetId, respawned);
        }

        [Fact]
        public void 尸体期_快照携带死亡状态_远端可重建()
        {
            var (world, shooter, target, _, targetSlot) = SpawnPair(targetHp: 1);
            SimStep.Step(world, Map(), FireAt(shooter));

            var snap = new SimWorldState();
            world.CopyTo(snap);

            // 远端/重连口径：公共快照按位重建——IsDead 的读数面（Hp≤0 + bitmap 保持）
            Assert.True(snap.Entities[targetSlot].Hp <= 0, "快照携带死亡事实（Hp）");
            Assert.True((snap.AliveBitmap[targetSlot >> 5] & (1u << (targetSlot & 31))) != 0u,
                "快照携带载体存续（bitmap）");
        }
    }
}
