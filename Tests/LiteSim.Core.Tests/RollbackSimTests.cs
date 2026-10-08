using System;
using LiteSim;
using Xunit;

namespace LiteSim.Tests
{
    /// <summary>
    /// 回滚验收用例组（《M9 实施指导》§2.6/§3，对账 §9 M9 验收行）：
    /// 人为注入预测错误 → 回滚后状态与"全程真实输入"直跑**完全一致**（终态逐元素 + 重放段逐帧 checksum）；
    /// 回滚深度 8 可用；超出深度正确退化（停预测 + 恢复续跑）；单渲染帧上限 2；重复确认幂等。
    /// 全部用例 .NET 侧闭环（同运行时红线，M9 决策⑩）。
    ///
    /// 帧号/驱动约定（与 RollbackSim 决策②一致）：帧号 = 已执行步数；**第 k 步消费 script[k]**（script[0] 占位）。
    /// 延迟 D 确认：tick t 开头（Frame==t）确认步 k = t-D+1——回滚 target = 帧 k-1 = Frame-D，
    /// D=8 恰为环窗口最老帧（深度 8），D=9 越界。尾部 D 步逐帧排水确认（Tick(0f) 重置单渲染帧窗口且不推进）。
    /// 初始状态 = 帧 0 由 RollbackSim 构造期捕获——第 1 步的回滚天然可用，无需预喂。
    /// </summary>
    public class RollbackSimTests
    {
        private const int StepCount = 150;
        private const int Delay = 8;
        private const ulong ScriptSeed = 0x0C0FFEE0D15EA5EUL;

        // ---- 脚本与真值 ----

        private static SimInputFrame[][] MakeScript(ulong seed, int steps, long[] players)
        {
            var rng = new SimRng(seed);
            var script = new SimInputFrame[steps + 1][];
            script[0] = new SimInputFrame[players.Length]; // 占位（帧号从 1 起）
            for (int f = 1; f <= steps; f++)
            {
                var row = new SimInputFrame[players.Length];
                for (int i = 0; i < row.Length; i++)
                {
                    row[i].EntityId = players[i];
                    row[i].MoveX = rng.NextFloat01() * 2f - 1f;
                    row[i].MoveZ = rng.NextFloat01() * 2f - 1f;
                    // 随机瞄准点（AimPoint 单口径；两笔随机数消耗与旧方向口径相同）
                    row[i].AimPointX = (1f - 2f * rng.NextFloat01()) * 10f;
                    row[i].AimPointY = 1f;
                    row[i].AimPointZ = (1f - 2f * rng.NextFloat01()) * 10f;
                    row[i].Buttons = (rng.NextUInt32() & 3u) == 0u ? SimInputFrame.ButtonFire : 0u;
                }
                script[f] = row;
            }
            return script;
        }

        /// <summary>静止零输入脚本：预测从零值沿用（§5.3），真实输入与预测逐位相同——"预测正确"场景。</summary>
        private static SimInputFrame[][] MakeConstantScript(int steps, long[] players)
        {
            var script = new SimInputFrame[steps + 1][];
            script[0] = new SimInputFrame[players.Length];
            for (int f = 1; f <= steps; f++)
            {
                script[f] = new[]
                {
                    // 零输入（含 AimPoint 零——本脚本不开火，与冷启动模板 IdentityTemplate 的零值逐位一致，
                    // 这正是"预测正确"用例的前提）
                    new SimInputFrame { EntityId = players[0], MoveX = 0f, MoveZ = 0f, Buttons = 0u },
                    new SimInputFrame { EntityId = players[1], MoveX = 0f, MoveZ = 0f, Buttons = 0u },
                };
            }
            return script;
        }

        /// <summary>真值直跑：全程真实输入、无预测无回滚；seq[f] = 执行完第 f 步的 checksum。</summary>
        private static (uint[] Checksums, SimWorldState Final) RunTruth(SimInputFrame[][] script, SimMapData map, SimWorldState world)
        {
            int n = script.Length - 1;
            var seq = new uint[n + 1];
            for (int f = 1; f <= n; f++)
            {
                var inputs = (SimInputFrame[])script[f].Clone(); // Step 就地排序——不污染脚本
                SimStep.Step(world, map, inputs, CombatValues.Default, WeaponTable.Default);
                seq[f] = SimChecksum.ComputeChecksum(world);
            }
            return (seq, world);
        }

        /// <summary>身份模板：EntityId 必带、控制量零值——冷启动预测基线（否则预测 EntityId=0 与真实永不相等）。</summary>
        private static SimInputFrame[] IdentityTemplate(long[] players)
        {
            var template = new SimInputFrame[players.Length];
            for (int i = 0; i < players.Length; i++) template[i].EntityId = players[i];
            return template;
        }

        /// <summary>延迟确认驱动：tick t 开头确认步 k = t-delay+1（k≥1）；尾部 delay 步逐帧排水。</summary>
        private static RollbackSim RunDelayed(SimInputFrame[][] script, SimMapData map, long[] players, int delay)
        {
            int n = script.Length - 1;
            var sim = new RollbackSim(SimChecksumBaselineSpec.BuildWorld(), map, IdentityTemplate(players), CombatValues.Default, WeaponTable.Default);

            for (int t = 0; t < n; t++)
            {
                int k = t - delay + 1;
                if (k >= 1) sim.OnRealInput(k, script[k]);
                sim.Tick(SimConfig.Dt);
            }
            for (int k = n - delay + 1; k <= n; k++) // 尾部排水：剩余 delay 步逐帧确认（Tick(0) 重置回滚窗口）
            {
                sim.OnRealInput(k, script[k]);
                sim.Tick(0f);
            }
            return sim;
        }

        private static (SimMapData Map, long[] Players) Scenario()
        {
            var map = SimChecksumBaselineSpec.BuildMap();
            var world = SimChecksumBaselineSpec.BuildWorld();
            return (map, SimChecksumBaselineSpec.PlayerIds(world));
        }

        // ---- §9 M9 验收①：注入预测错误 → 回滚后与"全程真实输入"完全一致 ----

        [Fact]
        public void 回滚_注入预测错误后与全程真实输入逐帧一致()
        {
            var (map, players) = Scenario();
            var script = MakeScript(ScriptSeed, StepCount, players);

            var (truthChecksums, truthFinal) = RunTruth(script, map, SimChecksumBaselineSpec.BuildWorld());
            var sim = RunDelayed(script, map, players, Delay);

            Assert.True(sim.RollbackCount > 0);                    // 机制确被触发（随机脚本下沿用预测几乎必错）
            Assert.False(sim.Halted);                               // 深度 8 恰在环窗口——全程不越界
            AssertWorldsElementWiseEqual(sim.State, truthFinal);   // 终态逐元素一致（§9 验收）

            // 决策⑨：重放段逐帧修正——环内最近 9 帧 checksum 与真值逐帧对齐
            var probe = new SimWorldState();
            for (int f = StepCount - Delay; f <= StepCount; f++)
            {
                Assert.True(sim.Ring.TryRestore(f, probe));
                Assert.Equal(truthChecksums[f], SimChecksum.ComputeChecksum(probe));
            }
        }

        [Fact]
        public void 预测正确_零回滚且与真值一致()
        {
            var (map, players) = Scenario();
            var script = MakeConstantScript(StepCount, players);   // 静止零输入：预测（零起点沿用）== 真实

            var (_, truthFinal) = RunTruth(script, map, SimChecksumBaselineSpec.BuildWorld());
            var sim = RunDelayed(script, map, players, Delay);

            Assert.Equal(0, sim.RollbackCount);                    // §5.4：预测正确时零回滚
            Assert.Equal(0, sim.DeferredRollbacks);
            Assert.False(sim.Halted);
            AssertWorldsElementWiseEqual(sim.State, truthFinal);
        }

        // ---- §9 M9 验收②③：回滚深度 8 可用；超出深度正确退化 ----

        [Fact]
        public void 退化_超出深度_停预测且未来真实输入可恢复()
        {
            var (map, players) = Scenario();
            var script = MakeScript(ScriptSeed, 30, players);

            var sim = new RollbackSim(SimChecksumBaselineSpec.BuildWorld(), map, IdentityTemplate(players), CombatValues.Default, WeaponTable.Default);
            for (int t = 0; t <= 10; t++)
            {
                int k = t - Delay;                                 // 延迟 9：tick t 确认步 k = t-8（Frame==t）
                if (k >= 1) sim.OnRealInput(k, script[k]);
                sim.Tick(SimConfig.Dt);
            }

            // tick 9 确认步 1：target = 帧 0 已被帧 9 逐出（容量 9 持帧 1..9）→ 越界 → 停预测
            Assert.True(sim.Halted);
            Assert.Equal(1, sim.HaltCount);

            int frozen = sim.State.Frame;                           // = 9
            sim.Tick(SimConfig.Dt);
            sim.Tick(SimConfig.Dt);
            Assert.Equal(frozen, sim.State.Frame);                  // 停预测期间不推进

            // 决策④恢复语义：未来步真实输入到达 → 解锁续跑（不可恢复的过去由 M10 权威快照覆盖兜底）
            sim.OnRealInput(frozen + 5, script[30]);
            Assert.False(sim.Halted);
            sim.Tick(SimConfig.Dt);
            Assert.Equal(frozen + 1, sim.State.Frame);               // 续跑推进
        }

        // ---- §5.4：单渲染帧回滚上限 2 + 重复确认幂等 ----

        [Fact]
        public void 防雪崩_单渲染帧回滚上限2且回滚丢弃计数()
        {
            var (map, players) = Scenario();
            var script = MakeScript(ScriptSeed, 10, players);

            var sim = new RollbackSim(SimChecksumBaselineSpec.BuildWorld(), map, IdentityTemplate(players), CombatValues.Default, WeaponTable.Default);
            for (int t = 0; t < 5; t++) sim.Tick(SimConfig.Dt);     // 步 1..5 全预测推进（Frame=5，环持帧 0..5）

            // 同一渲染帧内 3 次不符确认 → 恰回滚 2 次、第 3 次丢弃
            sim.OnRealInput(1, OtherThan(script[1]));
            sim.OnRealInput(2, OtherThan(script[2]));
            sim.OnRealInput(3, OtherThan(script[3]));

            Assert.Equal(2, sim.RollbackCount);
            Assert.Equal(1, sim.DeferredRollbacks);

            // 重复确认已真实化的步 → 幂等，不再回滚（风险 5 对策）
            sim.OnRealInput(1, OtherThan(script[1]));
            Assert.Equal(2, sim.RollbackCount);
        }

        // ---- 辅助 ----

        private static SimInputFrame[] OtherThan(SimInputFrame[] inputs)
        {
            var copy = (SimInputFrame[])inputs.Clone();
            for (int i = 0; i < copy.Length; i++)
            {
                copy[i].MoveX = copy[i].MoveX + 1f;   // 必与沿用预测逐位不符
                copy[i].Buttons = 0u;
            }
            return copy;
        }

        /// <summary>逐元素比较两个世界（含全部槽位逻辑字段与平面数组——"完全一致"的可执行形态；
        /// 覆盖公共战斗面与全部运行态数组/Match）。</summary>
        private static void AssertWorldsElementWiseEqual(SimWorldState a, SimWorldState b)
        {
            Assert.Equal(b.Frame, a.Frame);
            Assert.Equal(b.RngState, a.RngState);
            Assert.Equal(b.Match.Phase, a.Match.Phase);
            Assert.Equal(b.Match.Team, a.Match.Team);
            Assert.Equal(b.Match.Score, a.Match.Score);
            Assert.Equal(b.Match.Timer, a.Match.Timer);
            Assert.Equal(b.Match.Round, a.Match.Round);
            Assert.Equal(b.Match.Winner, a.Match.Winner);

            for (int i = 0; i < SimConfig.MaxEntities; i++)
            {
                Assert.Equal(b.Entities[i].Id, a.Entities[i].Id);
                Assert.Equal(b.Entities[i].Pos.X, a.Entities[i].Pos.X);
                Assert.Equal(b.Entities[i].Pos.Y, a.Entities[i].Pos.Y);
                Assert.Equal(b.Entities[i].Pos.Z, a.Entities[i].Pos.Z);
                Assert.Equal(b.Entities[i].Vel.X, a.Entities[i].Vel.X);
                Assert.Equal(b.Entities[i].Vel.Y, a.Entities[i].Vel.Y);
                Assert.Equal(b.Entities[i].Vel.Z, a.Entities[i].Vel.Z);
                Assert.Equal(b.Entities[i].Yaw, a.Entities[i].Yaw);
                Assert.Equal(b.Entities[i].Hp, a.Entities[i].Hp);
                Assert.Equal(b.Entities[i].Flags, a.Entities[i].Flags);
                Assert.Equal(b.Entities[i].Shield, a.Entities[i].Shield);
                Assert.Equal(b.Entities[i].Kills, a.Entities[i].Kills);
                Assert.Equal(b.Entities[i].Deaths, a.Entities[i].Deaths);
                Assert.Equal(b.Entities[i].SelectedWeapon, a.Entities[i].SelectedWeapon);
            }

            for (int i = 0; i < a.AliveBitmap.Length; i++) Assert.Equal(b.AliveBitmap[i], a.AliveBitmap[i]);
            for (int i = 0; i < a.Globals.Length; i++) Assert.Equal(b.Globals[i], a.Globals[i]);
            for (int i = 0; i < a.CustomData.Length; i++) Assert.Equal(b.CustomData[i], a.CustomData[i]);
            for (int i = 0; i < a.Weapons.Length; i++)
            {
                Assert.Equal(b.Weapons[i].WeaponDefId, a.Weapons[i].WeaponDefId);
                Assert.Equal(b.Weapons[i].MagAmmo, a.Weapons[i].MagAmmo);
                Assert.Equal(b.Weapons[i].ReserveAmmo, a.Weapons[i].ReserveAmmo);
                Assert.Equal(b.Weapons[i].NextFireFrame, a.Weapons[i].NextFireFrame);
                Assert.Equal(b.Weapons[i].ReloadEndFrame, a.Weapons[i].ReloadEndFrame);
                Assert.Equal(b.Weapons[i].EquipEndFrame, a.Weapons[i].EquipEndFrame);
                Assert.Equal(b.Weapons[i].State, a.Weapons[i].State);
                Assert.Equal(b.Weapons[i].ShotSeq, a.Weapons[i].ShotSeq);
            }
            for (int i = 0; i < a.Actions.Length; i++)
            {
                Assert.Equal(b.Actions[i].ActionId, a.Actions[i].ActionId);
                Assert.Equal(b.Actions[i].StartFrame, a.Actions[i].StartFrame);
                Assert.Equal(b.Actions[i].Phase, a.Actions[i].Phase);
                Assert.Equal(b.Actions[i].CastToken, a.Actions[i].CastToken);
                Assert.Equal(b.Actions[i].CooldownEnd, a.Actions[i].CooldownEnd);
                Assert.Equal(b.Actions[i].Charges, a.Actions[i].Charges);
            }
            for (int i = 0; i < a.Status.Length; i++)
            {
                Assert.Equal(b.Status[i].EffectId, a.Status[i].EffectId);
                Assert.Equal(b.Status[i].EndFrame, a.Status[i].EndFrame);
                Assert.Equal(b.Status[i].Param, a.Status[i].Param);
            }
            for (int i = 0; i < a.MatchBag.Length; i++)
            {
                Assert.Equal(b.MatchBag[i].ItemDefId, a.MatchBag[i].ItemDefId);
                Assert.Equal(b.MatchBag[i].Count, a.MatchBag[i].Count);
                Assert.Equal(b.MatchBag[i].QuickSlot, a.MatchBag[i].QuickSlot);
            }
            for (int i = 0; i < a.Resources.Length; i++) Assert.Equal(b.Resources[i], a.Resources[i]);
        }

        // ---- P0 和解口径（《状态同步专项设计》§5.2 分层 + §6.1：私有面不进比对，公共面差异必纠）----

        [Fact]
        public void 和解口径_私有面差异不触发和解()
        {
            var (map, players) = Scenario();
            var sim = new RollbackSim(SimChecksumBaselineSpec.BuildWorld(), map, IdentityTemplate(players), CombatValues.Default, WeaponTable.Default);
            for (int t = 0; t < 5; t++) sim.Tick(SimConfig.Dt);   // 帧 0..5（环内）
            int frame = sim.State.Frame;

            var authoritative = new SimWorldState();
            sim.State.CopyTo(authoritative);

            // 服务器侧私有面推进（开火扣弹/技能 CD/拾取/资源/随机数）——客户端**永远无法重建**：
            // 全量口径必变，和解锚点（公共口径）不得包含，否则每份快照必假和解
            int slot = (int)(players[0] & 0xFFFFL);
            authoritative.Weapons[slot * SimConfig.WeaponSlotsPerEntity].MagAmmo = 29;
            authoritative.Actions[slot * SimConfig.ActionSlotsPerEntity + 2].CooldownEnd = 600;
            authoritative.Status[slot * SimConfig.StatusSlotsPerEntity].Param = 60;
            authoritative.MatchBag[slot * SimConfig.MatchBagSlotsPerEntity].ItemDefId = 701;
            authoritative.Resources[slot] = 5;
            authoritative.RngState ^= 0xABUL;
            Assert.NotEqual(SimChecksum.ComputeChecksum(sim.State), SimChecksum.ComputeChecksum(authoritative));

            Assert.False(sim.OnAuthoritativeSnapshot(frame, authoritative, SimChecksum.ComputePublicChecksum(authoritative)));
            Assert.Equal(0, sim.ReconcileCount);
        }

        [Fact]
        public void 和解口径_公共面差异触发和解并采纳权威()
        {
            var (map, players) = Scenario();
            var sim = new RollbackSim(SimChecksumBaselineSpec.BuildWorld(), map, IdentityTemplate(players), CombatValues.Default, WeaponTable.Default);
            for (int t = 0; t < 5; t++) sim.Tick(SimConfig.Dt);
            int frame = sim.State.Frame;

            var authoritative = new SimWorldState();
            sim.State.CopyTo(authoritative);
            int slot = (int)(players[0] & 0xFFFFL);
            authoritative.Entities[slot].Shield = 60;          // 服务器护盾事实（公共面）
            authoritative.Entities[slot].Kills = 3;
            authoritative.Actions[slot * SimConfig.ActionSlotsPerEntity].ActionId = 301;
            authoritative.Actions[slot * SimConfig.ActionSlotsPerEntity].Phase = ActionPhase.Active;
            authoritative.Match.Timer = 10800;

            Assert.True(sim.OnAuthoritativeSnapshot(frame, authoritative, SimChecksum.ComputePublicChecksum(authoritative)));
            Assert.Equal(1, sim.ReconcileCount);
            Assert.Equal(60, sim.State.Entities[slot].Shield); // 权威覆盖采纳
            Assert.Equal(3, sim.State.Entities[slot].Kills);
            Assert.Equal(10800, sim.State.Match.Timer);
        }

        // ---- 追帧补发读取（多逻辑帧渲染帧的沿用帧同样必须可上行）----

        [Fact]
        public void 补发读取_真实输入帧与追帧沿用帧都取得到_值与执行时一致()
        {
            var (map, players) = Scenario();
            var sim = new RollbackSim(SimChecksumBaselineSpec.BuildWorld(), map, IdentityTemplate(players), CombatValues.Default, WeaponTable.Default);

            // 第 1 帧真实输入（早到入史）——含开火位，验证沿用帧的开火不预测掩码
            var real = new SimInputFrame[players.Length];
            real[0] = new SimInputFrame
            {
                EntityId = players[0],
                MoveX = 0.5f, MoveZ = 0f, AimPointX = 10f, AimPointY = 1f, AimPointZ = 0f,
                Buttons = SimInputFrame.ButtonFire | SimInputFrame.ButtonAim,
            };
            real[1] = new SimInputFrame { EntityId = players[1] };
            sim.OnRealInput(1, real);
            sim.Tick(SimConfig.Dt);                       // 执行第 1 帧（真实输入）

            Assert.True(sim.TryGetExecutedInput(1, 0, out var usedReal));
            Assert.Equal(0.5f, usedReal.MoveX);
            Assert.Equal(SimInputFrame.ButtonFire | SimInputFrame.ButtonAim, usedReal.Buttons);

            sim.Tick(SimConfig.Dt * 2f);                  // 一个渲染帧跑 2 个逻辑帧（第 2、3 帧沿用推进）
            Assert.Equal(3, sim.State.Frame);

            // 沿用帧读回的就是本地实际执行的那份：移动沿用、开火被 PredictedButtons 掩掉——
            // 补发它们，服务器才不会对这些帧按空输入兜底执行（移动中分叉）。
            Assert.True(sim.TryGetExecutedInput(2, 0, out var used2));
            Assert.Equal(0.5f, used2.MoveX);
            Assert.Equal(SimInputFrame.ButtonAim, used2.Buttons);
            Assert.True(sim.TryGetExecutedInput(3, 0, out var used3));
            Assert.Equal(0.5f, used3.MoveX);
            Assert.Equal(SimInputFrame.ButtonAim, used3.Buttons);
            Assert.Equal(players[0], used2.EntityId);     // 身份随输入数组槽位携带（EntityId 不丢）
        }

        [Fact]
        public void 补发读取_未执行帧与越界玩家一律拒绝()
        {
            var (map, players) = Scenario();
            var sim = new RollbackSim(SimChecksumBaselineSpec.BuildWorld(), map, IdentityTemplate(players), CombatValues.Default, WeaponTable.Default);
            sim.Tick(SimConfig.Dt);                       // 执行第 1 帧（沿用零输入）
            Assert.Equal(1, sim.State.Frame);

            Assert.False(sim.TryGetExecutedInput(0, 0, out _), "帧 0 是初始锚定，不是已执行步");
            Assert.False(sim.TryGetExecutedInput(2, 0, out _), "未来帧不在史里");
            Assert.False(sim.TryGetExecutedInput(1, -1, out _), "负槽位拒绝");
            Assert.False(sim.TryGetExecutedInput(1, players.Length, out _), "越界槽位拒绝");
            Assert.True(sim.TryGetExecutedInput(1, 0, out _));   // 边界内的照常可读
        }
    }
}
