using Xunit;

namespace LiteSim.Tests
{
    /// <summary>
    /// 槽位/系统链/命令轮次/帧事件用例（《M8 实施指导》§3 测试组）。
    /// 确定性与基线用例见 <see cref="SimDeterminismTests"/>；布局契约见 <see cref="SimLayoutContractTests"/>。
    /// </summary>
    public class SimWorldTests
    {
        private static SimMapData TestMap()
        {
            return new SimMapData { GroundY = 0f, HalfWidth = 50f, HalfDepth = 50f };
        }

        [Fact]
        public void 槽位_同槽复用65536次_Id不复用()
        {
            var s = new SimWorldState();

            long[] ids = new long[SimConfig.MaxEntities];
            for (int i = 0; i < SimConfig.MaxEntities; i++)
            {
                ids[i] = s.Spawn(new EntitySlot { Hp = 1 }, out int _);
            }

            long firstId = ids[0];
            long currentId = firstId;
            for (int i = 0; i < 65536; i++)
            {
                s.Despawn(currentId);
                currentId = s.Spawn(new EntitySlot { Hp = 1 }, out int slot);
                Assert.Equal(0, slot); // 游标环形扫描回到同一槽
            }

            // 16 位 version 耗尽后 Id 仍不复用（#7：48 位防重绕）
            Assert.NotEqual(firstId, currentId);
            Assert.False(s.TryResolve(firstId, out int stale));
        }

        [Fact]
        public void 系统顺序_一帧内完成射击到清理链路()
        {
            var s = new SimWorldState();
            SimMapData map = TestMap();

            long shooter = s.Spawn(new EntitySlot { Hp = CombatConfig.EntityHp, Pos = new SimVector3(0f, 0f, 0f), Yaw = 0f }, out int _);
            long target = s.Spawn(new EntitySlot { Hp = 1, Pos = new SimVector3(10f, 0f, 0f) }, out int targetSlot);

            // 朝向 0 = +X（Cos(0)=1）：目标在正前 10m，一击致死（Hp=1 < 伤害）
            var inputs = new[]
            {
                // AimPoint 单口径：点取目标（10,0,0）正前方眼高位 ⇒ 弹道水平掠过目标（普通命中）
                new SimInputFrame { EntityId = shooter, AimPointX = 10f, AimPointY = 1f, AimPointZ = 0f, Buttons = SimInputFrame.ButtonFire },
            };

            // 零散布表：本用例钉"系统链时序"（Fire→Hit→Death 同帧）——命中几何不与散布随机耦合
            SimStep.Step(s, map, inputs, CombatValues.Default, TestWeapons.NoSpread());

            // 顺序可观测：射击 → 命中 → 伤害结算 → 死亡事件，全部发生在同一逻辑帧
            Assert.Equal(1, s.Frame);
            Assert.Equal(3, s.Events.Count);
            Assert.Equal(FrameEventKind.Fire, s.Events.Items[0].Kind);
            Assert.Equal(FrameEventKind.Hit, s.Events.Items[1].Kind);
            Assert.Equal(FrameEventKind.Death, s.Events.Items[2].Kind);
            Assert.Equal(target, s.Events.Items[2].EntityId);

            // 尸体期语义（死亡链路根治）：同帧不再回收——槽位保留为尸体载体
            // （bitmap 保持、CorpseFrames 递减）；期满才回收。回收断言见 CorpsePhaseTests。
            Assert.True(s.IsAlive(targetSlot), "尸体期内槽位保留（死亡表现载体）");
            Assert.Equal(2, s.AliveCount());

            // 命令缓冲帧末清空；Step 不清帧事件（由驱动消费后清，决策⑥）
            Assert.Equal(0, s.Cmds.Count);
            Assert.Equal(3, s.Events.Count);
            s.Events.Clear();
            Assert.Equal(0, s.Events.Count);
        }

        [Fact]
        public void 系统顺序_非致命伤害存活无死亡事件()
        {
            var s = new SimWorldState();
            SimMapData map = TestMap();

            long shooter = s.Spawn(new EntitySlot { Hp = CombatConfig.EntityHp, Pos = new SimVector3(0f, 0f, 0f), Yaw = 0f }, out int _);
            s.Spawn(new EntitySlot { Hp = CombatConfig.EntityHp, Pos = new SimVector3(10f, 0f, 0f) }, out int targetSlot);

            var inputs = new[]
            {
                new SimInputFrame { EntityId = shooter, AimPointX = 10f, AimPointY = 1f, AimPointZ = 0f, Buttons = SimInputFrame.ButtonFire },
            };

            SimStep.Step(s, map, inputs, CombatValues.Default, TestWeapons.NoSpread());   // 零散布——钉系统链不钉弹道

            Assert.Equal(2, s.Events.Count); // Fire + Hit，无 Death
            Assert.True(s.IsAlive(targetSlot));
            Assert.Equal(2, s.AliveCount());
        }

        [Fact]
        public void 同帧多请求_乱序输入与升序结果一致()
        {
            const int frames = 120;
            SimMapData map = TestMap();

            // 两个世界同构；A 的输入数组每帧乱序（固定置换），B 恒升序——SimStep 就地排序归一
            var a = new SimWorldState { RngState = 777UL };
            var b = new SimWorldState { RngState = 777UL };
            long[] playersA = SpawnThree(a);
            long[] playersB = SpawnThree(b);

            var inputRngA = new SimRng(4242UL);
            var inputRngB = new SimRng(4242UL);
            var asc = new SimInputFrame[3];
            var shuffled = new SimInputFrame[3];

            for (int f = 0; f < frames; f++)
            {
                MakeThree(inputRngA, playersA, asc);
                MakeThree(inputRngB, playersB, asc);

                shuffled[0] = asc[2];
                shuffled[1] = asc[0];
                shuffled[2] = asc[1];

                SimStep.Step(a, map, shuffled, CombatValues.Default, WeaponTable.Default);
                SimStep.Step(b, map, asc, CombatValues.Default, WeaponTable.Default);

                Assert.Equal(SimChecksum.ComputeChecksum(a), SimChecksum.ComputeChecksum(b));
            }
        }

        [Fact]
        public void 命令缓冲_手动Damage命令经FlushCommands结算()
        {
            var s = new SimWorldState();
            long id = s.Spawn(new EntitySlot { Hp = CombatConfig.EntityHp }, out int slot);

            s.Cmds.Write(SimCommandKind.Damage, id, 0L, 30);
            SimStep.FlushCommands(s);
            Assert.Equal(70, s.Entities[slot].Hp);
            Assert.Equal(0, s.Cmds.Count); // 轮末清空
            Assert.Equal(0, s.Events.Count);

            // 致命伤害：Hp 跨越死亡线 → Kill 命令（第 2 轮窗口，M8 无消费者）+ Death 事件
            s.Cmds.Write(SimCommandKind.Damage, id, 0L, 70);
            SimStep.FlushCommands(s);
            Assert.True(s.Entities[slot].Hp <= 0);
            Assert.Equal(1, s.Events.Count);
            Assert.Equal(FrameEventKind.Death, s.Events.Items[0].Kind);
            Assert.Equal(0, s.Cmds.Count);

            // FlushCommands 不做清理（管道分工）：槽位仍标记存活，等 CleanupSystem
            Assert.True(s.IsAlive(slot));
        }

        [Fact]
        public void 测试房免死_跨死线保底1且不写Death()
        {
            var s = new SimWorldState();
            long id = s.Spawn(new EntitySlot { Hp = CombatConfig.EntityHp }, out int slot);

            SimTestRules.NoDeath = true;
            try
            {
                s.Cmds.Write(SimCommandKind.Damage, id, 0L, CombatConfig.EntityHp + 50);
                SimStep.FlushCommands(s);

                Assert.Equal(1, s.Entities[slot].Hp);   // 保命不消失：跨死线保底 1
                Assert.Equal(0, s.Events.Count);        // 无 Death 事件
                Assert.Equal(0, s.Cmds.Count);          // 无 Kill 产出
                Assert.True(s.IsAlive(slot));
            }
            finally
            {
                SimTestRules.NoDeath = false;           // 静态规则：用例毕复位（防跨用例污染）
            }
        }

        [Fact]
        public void 命令缓冲_死亡目标命令作废不重复击杀()
        {
            var s = new SimWorldState();
            long victim = s.Spawn(new EntitySlot { Hp = 1 }, out int slot);

            // 同轮两条伤害：第一条致死，第二条目标已死（经 TryResolve 失效）→ 只一次 Death
            s.Cmds.Write(SimCommandKind.Damage, victim, 0L, 10);
            s.Cmds.Write(SimCommandKind.Damage, victim, 0L, 10);
            SimStep.FlushCommands(s);

            Assert.True(s.Entities[slot].Hp <= 0);
            Assert.Equal(1, s.Events.Count);
        }

        [Fact]
        public void 帧事件_追帧5帧事件全部交付()
        {
            var s = new SimWorldState();
            SimMapData map = TestMap();

            long p0 = s.Spawn(new EntitySlot { Hp = CombatConfig.EntityHp, Pos = new SimVector3(0f, 0f, 0f), Yaw = 0f }, out int _);
            long p1 = s.Spawn(new EntitySlot { Hp = CombatConfig.EntityHp, Pos = new SimVector3(40f, 0f, 40f), Yaw = SimTrig.Pi }, out int _);

            // 两玩家相距 40m 且相背而立：每帧都开火但互不命中（也无第三方）→ 每帧恰 2 个 Fire
            var inputs = new[]
            {
                new SimInputFrame { EntityId = p0, AimPointX = 100f, AimPointY = 1f, AimPointZ = 0f, Buttons = SimInputFrame.ButtonFire },
                new SimInputFrame { EntityId = p1, AimPointX = -60f, AimPointY = 1f, AimPointZ = 40f, Buttons = SimInputFrame.ButtonFire },
            };

            var driver = new FrameDriver();
            int deliveries = 0;
            int fireEvents = 0;

            driver.Tick(5f * SimConfig.Dt, s, map, inputs, CombatValues.Default, WeaponTable.Default, w =>
            {
                deliveries++;
                fireEvents += w.Events.Count;
            });

            Assert.Equal(5, deliveries);       // 5 个逻辑帧都交付（不只剩最后一帧，§3.7）
            // **武器节拍语义**：装备后按住开火受射速节拍约束（600rpm ⇒ 6 帧/发）——5 帧窗口内仅首帧真开火
            // （2 名玩家 × 1 发 = 2 个 Fire；其余帧被武器门拦下，不产事件）。事件交付面（deliveries=5）与本断言正交。
            Assert.Equal(2, fireEvents);       // 首帧 2 个 Fire，其余帧节拍拦截
            Assert.Equal(0, s.Events.Count);   // 每帧末消费后清空
            Assert.Equal(5, s.Frame);
            Assert.Equal(5, driver.StepsLastTick);
        }

        // ---- 辅助 ----

        private static long[] SpawnThree(SimWorldState s)
        {
            long p0 = s.Spawn(new EntitySlot { Hp = CombatConfig.EntityHp, Pos = new SimVector3(0f, 0f, 0f) }, out int _);
            long p1 = s.Spawn(new EntitySlot { Hp = CombatConfig.EntityHp, Pos = new SimVector3(20f, 0f, 0f) }, out int _);
            long p2 = s.Spawn(new EntitySlot { Hp = CombatConfig.EntityHp, Pos = new SimVector3(0f, 0f, 20f) }, out int _);
            return new[] { p0, p1, p2 };
        }

        private static void MakeThree(SimRng rng, long[] players, SimInputFrame[] inputs)
        {
            for (int i = 0; i < inputs.Length; i++)
            {
                inputs[i].EntityId = players[i];
                inputs[i].MoveX = rng.NextFloat01() * 2f - 1f;
                inputs[i].MoveZ = rng.NextFloat01() * 2f - 1f;
                // 随机瞄准点（AimPoint 单口径；两笔随机数消耗与旧方向口径相同——对拍两世界同流）
                inputs[i].AimPointX = (1f - 2f * rng.NextFloat01()) * 10f;
                inputs[i].AimPointY = 1f;
                inputs[i].AimPointZ = (1f - 2f * rng.NextFloat01()) * 10f;
                inputs[i].Buttons = (rng.NextUInt32() & 1u) == 0u ? SimInputFrame.ButtonFire : 0u;
            }
        }
    }
}
