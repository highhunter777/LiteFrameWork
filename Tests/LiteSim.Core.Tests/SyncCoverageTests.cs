using System;
using System.Collections.Generic;
using System.Reflection;
using LiteSim;
using Xunit;

namespace LiteSim.Tests
{
    /// <summary>
    /// **同步覆盖度守卫**（第三档）：每个进同步的状态字段，都必须"三处一致"——
    /// ① 有 <see cref="StateLayerAttribute"/> 表态；② 进了全量 checksum；③ 公共面还须进公共 checksum。
    ///
    /// **为什么用扰动法而不是代码生成器**：本工程真正的问题是"**漏改**"（改了字段却忘了同步
    /// checksum / proto / 差分器），而不是"手写太慢"。生成器要设计清单 DSL、把 5 个文件改成生成物，
    /// 工期与风险远超收益；而**扰动法直接验证结果**——把字段改成非默认值，看两套 checksum 是否按
    /// 预期变化。它抓的错与生成器完全相同，成本低一个数量级。
    ///
    /// **实证价值**：<c>CorpseFrames</c> 曾"注释说公共面、wire 却没有"→ 尸体期内每帧假和解；
    /// 第二档的守卫当场抓出 <c>StatusSlotData</c> 三字段漏标。本类是同一思路的系统化版本：
    /// 覆盖**全部**参与同步的结构体与数组，而不只是 <see cref="EntitySlot"/>。
    /// </summary>
    public class SyncCoverageTests
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        // ---- ① 全量口径：每个运行态结构体的每个字段都必须进 ----

        [Fact]
        public void 全量口径_覆盖全部运行态数组字段()
        {
            // 逐类型逐字段扰动：改一个字段 → 全量 checksum 必变。
            // 漏了任何一个 = 该字段的分叉在重放对账中不可见（回滚/重放静默漂移）。
            AssertArrayCovered("Weapons", typeof(WeaponRuntime));
            AssertArrayCovered("Actions", typeof(ActionRuntime));
            AssertArrayCovered("Status", typeof(StatusSlotData));
            AssertArrayCovered("MatchBag", typeof(MatchBagSlot));
        }

        [Fact]
        public void 生成物_覆盖全部标注字段_防漏跑生成器()
        {
            // 第三档的收口断言：每个标了 [StateLayer] 的字段都必须能经生成物影响 checksum。
            // 若有人加了字段却忘记跑 gen-sync-code.ps1，这里当场红——
            // 而不是等到线上出现"某字段分叉但和解不报"。
            AssertAllAnnotatedFieldsAffectFull(typeof(EntitySlot), "Entities");
            AssertAllAnnotatedFieldsAffectFull(typeof(WeaponRuntime), "Weapons");
            AssertAllAnnotatedFieldsAffectFull(typeof(ActionRuntime), "Actions");
            AssertAllAnnotatedFieldsAffectFull(typeof(StatusSlotData), "Status");
            AssertAllAnnotatedFieldsAffectFull(typeof(MatchBagSlot), "MatchBag");
        }

        /// <summary>逐字段扰动：改了它，全量 checksum 必变（= 确实被生成物折叠进去了）。</summary>
        private static void AssertAllAnnotatedFieldsAffectFull(Type elementType, string arrayName)
        {
            FieldInfo arrField = typeof(SimWorldState).GetField(arrayName, Instance);
            Assert.NotNull(arrField);

            foreach (FieldInfo f in elementType.GetFields(Instance))
            {
                if (f.GetCustomAttribute<StateLayerAttribute>() == null) continue;   // 未标注 = 不同步
                if (!f.FieldType.IsValueType) continue;

                var world = NewWorld();
                uint before = SimChecksum.ComputeChecksum(world);

                Array arr = (Array)arrField.GetValue(world);
                object boxed = arr.GetValue(0);
                f.SetValue(boxed, Nudged(f, f.GetValue(boxed)));
                arr.SetValue(boxed, 0);

                Assert.True(before != SimChecksum.ComputeChecksum(world),   // lint-allow R3
                    $"{elementType.Name}.{f.Name} 标注了同步层次，但改动后全量 checksum 未变——"
                    + "生成物没覆盖它（跑 scripts/codegen/gen-sync-code.ps1 后重试）");
            }
        }

        [Fact]
        public void 全量口径_覆盖资源数组()
        {
            var world = NewWorld();
            uint before = SimChecksum.ComputeChecksum(world);
            world.Resources[0] = 42;
            Assert.True(before != SimChecksum.ComputeChecksum(world),   // lint-allow R3
                "Resources 未进全量口径——技能资源账本的分叉会对账不可见");
        }

        [Fact]
        public void 全量口径_覆盖分型表与扩展blob()
        {
            // 分型表（道具/投掷物/区域）与 Globals/CustomData：三面缺一即隐形分叉
            var world = NewWorld();

            uint c0 = SimChecksum.ComputeChecksum(world);
            world.Items[0].ItemDefId = 901;
            uint c1 = SimChecksum.ComputeChecksum(world);
            Assert.True(c0 != c1, "Items 未进全量口径");   // lint-allow R3

            world.Globals[5] = 0xAB;
            uint c2 = SimChecksum.ComputeChecksum(world);
            Assert.True(c1 != c2, "Globals 未进全量口径");   // lint-allow R3

            world.CustomData[3] = 0xCD;
            uint c3 = SimChecksum.ComputeChecksum(world);
            Assert.True(c2 != c3, "CustomData 未进全量口径");   // lint-allow R3

            world.Match.Timer = 999;
            uint c4 = SimChecksum.ComputeChecksum(world);
            Assert.True(c3 != c4, "Match 未进全量口径");   // lint-allow R3
        }

        // ---- ② 公共口径：公共面必进、私有面必不进 ----

        [Fact]
        public void 公共口径_不覆盖私有运行态()
        {
            var world = NewWorld();
            uint before = SimChecksum.ComputePublicChecksum(world);

            // 私有面全改一遍——公共口径**必须不变**（客户端重建不了，进比对口径 = 永假和解）
            world.Weapons[0].MagAmmo = 30;
            world.Weapons[0].NextFireFrame = 45;
            world.Actions[SimConfig.ActionSlotsPerEntity + 1].CooldownEnd = 600;
            world.Actions[SimConfig.ActionSlotsPerEntity + 1].Charges = 2;
            world.Status[0].EffectId = 51;
            world.Status[0].Param = 60;
            world.MatchBag[0].ItemDefId = 701;
            world.Resources[0] = 7;
            world.RngState = 999UL;

            Assert.True(before == SimChecksum.ComputePublicChecksum(world),   // lint-allow R3
                "私有面/RngState 进了公共口径——客户端无法重建它们，会制造每快照必假和解");
        }

        [Fact]
        public void 公共口径_主动作槽摘要必进()
        {
            // 主动作槽（索引 0）是 SlotDelta 的一部分——公共面
            var world = NewWorld();
            uint before = SimChecksum.ComputePublicChecksum(world);

            world.Actions[0 * SimConfig.ActionSlotsPerEntity].ActionId = 301;
            Assert.True(before != SimChecksum.ComputePublicChecksum(world),   // lint-allow R3
                "主动作摘要未进公共口径——远端看不到他人的动作表现");

            uint before2 = SimChecksum.ComputePublicChecksum(world);
            world.Actions[0 * SimConfig.ActionSlotsPerEntity].Phase = ActionPhase.Active;
            Assert.True(before2 != SimChecksum.ComputePublicChecksum(world),   // lint-allow R3
                "主动作 Phase 未进公共口径");
        }

        [Fact]
        public void 公共口径_分型表必进()
        {
            // 道具/投掷物/区域是世界可见面（全端可重建）——线上和解口径必须覆盖
            var world = NewWorld();

            uint c0 = SimChecksum.ComputePublicChecksum(world);
            world.Items[2].Count = 3;
            Assert.True(c0 != SimChecksum.ComputePublicChecksum(world), "Items 未进公共口径");   // lint-allow R3

            uint c1 = SimChecksum.ComputePublicChecksum(world);
            world.Projectiles[1].Speed = 20f;
            Assert.True(c1 != SimChecksum.ComputePublicChecksum(world), "Projectiles 未进公共口径");   // lint-allow R3

            uint c2 = SimChecksum.ComputePublicChecksum(world);
            world.Zones[0].Radius = 5f;
            Assert.True(c2 != SimChecksum.ComputePublicChecksum(world), "Zones 未进公共口径");   // lint-allow R3
        }

        [Fact]
        public void 公共口径_活体位图必进()
        {
            // 槽位存在性差异（死亡/生成）必须进公共口径——否则"一方有实体一方没有"不被发现
            var a = NewWorld();
            var b = NewWorld();
            b.Despawn(b.Entities[1].Id);

            Assert.NotEqual(SimChecksum.ComputePublicChecksum(a), SimChecksum.ComputePublicChecksum(b));
        }

        // ---- ③ 两套口径的关系：公共 ⊆ 全量 ----

        [Fact]
        public void 公共面变动_必同时反映到两套口径()
        {
            // 全量口径是公共口径的超集：公共面变了，两套都必须变。
            // 反过来说：只有全量变、公共不变 = 私有面（正确）；两套都不变 = 漏进 checksum（错误）。
            foreach (FieldInfo f in typeof(EntitySlot).GetFields(Instance))
            {
                if (f.GetCustomAttribute<StateLayerAttribute>()?.Layer != StateLayer.Public) continue;

                var world = NewWorld();
                uint pubBefore = SimChecksum.ComputePublicChecksum(world);
                uint fullBefore = SimChecksum.ComputeChecksum(world);

                object boxed = world.Entities[1];
                f.SetValue(boxed, Nudged(f, f.GetValue(boxed)));
                world.Entities[1] = (EntitySlot)boxed;

                Assert.True(pubBefore != SimChecksum.ComputePublicChecksum(world),   // lint-allow R3
                    $"公共面 {f.Name} 未进公共口径");
                Assert.True(fullBefore != SimChecksum.ComputeChecksum(world),        // lint-allow R3
                    $"公共面 {f.Name} 未进全量口径（全量是超集，必含）");
            }
        }

        // ---- ④ 快照摘要覆盖：SnapshotDiffer 靠它判定"该槽位变了" ----

        [Fact]
        public void 快照摘要_公共面字段全覆盖_漏一个即差分漏发()
        {
            // EntitySnapshotEntry 是差分基线的金标——它漏字段 = 该字段变化不被判为"变了" = 增量漏发；
            // 而 proto 有该字段 ⇒ 客户端永远收不到更新 = 静默分叉。
            var snap = new SimWorldStateSnapshot();
            var world = NewWorld();
            snap.CaptureFull(world);

            foreach (FieldInfo f in typeof(EntitySlot).GetFields(Instance))
            {
                if (f.GetCustomAttribute<StateLayerAttribute>()?.Layer != StateLayer.Public) continue;

                var mutated = NewWorld();
                object boxed = mutated.Entities[1];
                f.SetValue(boxed, Nudged(f, f.GetValue(boxed)));
                mutated.Entities[1] = (EntitySlot)boxed;

                Assert.False(snap.Matches(1, mutated),
                    $"EntitySnapshotEntry 漏了公共面 {f.Name}——差分器不会判它'变了'，"
                    + "该字段的更新永远发不出去（客户端静默分叉）");
            }
        }

        [Fact]
        public void 快照摘要_私有面字段不进_防无谓流量()
        {
            // 快照摘要只服务公共面差分；私有面进它 = 每帧判"变了"却发不出对应字段 = 白白转全量
            var snap = new SimWorldStateSnapshot();
            var world = NewWorld();
            snap.CaptureFull(world);

            // 私有面字段变化不应影响 Matches 判定（它们不在 EntitySnapshotEntry 里）
            var mutated = NewWorld();
            mutated.Entities[1].FaceExitTurning = (byte)(mutated.Entities[1].FaceExitTurning + 1);
            Assert.True(snap.Matches(1, mutated),
                "FaceExitTurning 是纯私有面（由输入历史可重建）——不应进差分摘要");
        }

        // ---- 辅助 ----

        private static void AssertArrayCovered(string arrayName, Type elementType)
        {
            // 这些数组在 SimWorldState 上是**公开字段**（非属性）——反射取字段
            FieldInfo arrField = typeof(SimWorldState).GetField(arrayName, Instance);
            Assert.NotNull(arrField);

            foreach (FieldInfo f in elementType.GetFields(Instance))
            {
                if (!f.FieldType.IsValueType) continue;

                var world = NewWorld();
                uint before = SimChecksum.ComputeChecksum(world);

                Array arr = (Array)arrField.GetValue(world);
                object boxed = arr.GetValue(0);
                f.SetValue(boxed, Nudged(f, f.GetValue(boxed)));
                arr.SetValue(boxed, 0);

                Assert.True(before != SimChecksum.ComputeChecksum(world),   // lint-allow R3
                    $"{arrayName}[0].{f.Name} 未进全量口径——该字段的分叉在重放对账中不可见");
            }
        }

        private static SimWorldState NewWorld()
        {
            var w = new SimWorldState { RngState = 12345UL };
            long id0 = w.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(-5f, 0f, 0f) }, out _);
            w.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(5f, 0f, 0f) }, out _);
            w.Match = new MatchStateData { Phase = 1, Timer = 10800, Round = 1 };

            // 分型表三行都置"行有效"以便扰动（kind 位与实际行数据配套）
            w.Items[2] = new ItemState { ItemDefId = 501, Count = 1, OwnerId = id0, AgeFrames = 0 };
            w.Projectiles[1] = new ProjectileState { ItemDefId = 601, Speed = 12f, OwnerId = id0 };
            w.Zones[0] = new ZoneState { ItemDefId = 701, Radius = 3f, OwnerId = id0 };

            // 私有面给非默认值（确保扰动有意义）
            w.Weapons[0] = new WeaponRuntime { WeaponDefId = 1, MagAmmo = 10, ReserveAmmo = 30 };
            w.Actions[SimConfig.ActionSlotsPerEntity + 1] = new ActionRuntime { ActionId = 305, CooldownEnd = 100 };
            w.Status[0] = new StatusSlotData { EffectId = 51, Param = 30 };
            w.MatchBag[0] = new MatchBagSlot { ItemDefId = 801, Count = 2 };
            w.Resources[0] = 1;
            return w;
        }

        /// <summary>按字段类型给"与当前值不同"的确定值。</summary>
        private static object Nudged(FieldInfo f, object current)
        {
            switch (current)
            {
                case long v: return v + 1000L;
                case int v: return v + 1000;
                case uint v: return v + 1000u;
                case byte v: return (byte)(v + 7);
                case float v: return v + 1.25f;
                case SimVector3 v: return new SimVector3(v.X + 1f, v.Y + 1f, v.Z + 1f);
                default:
                    // 枚举：取一个与当前不同的值（值域至少 2 个的枚举恒可扰动）
                    if (f.FieldType.IsEnum)
                    {
                        object next = Convert.ChangeType(
                            (Convert.ToInt32(current, System.Globalization.CultureInfo.InvariantCulture) + 1)
                                % Enum.GetValues(f.FieldType).Length,
                            Enum.GetUnderlyingType(f.FieldType));
                        return next;
                    }
                    throw new InvalidOperationException("未覆盖的字段类型：" + f.FieldType.Name);
            }
        }
    }
}
