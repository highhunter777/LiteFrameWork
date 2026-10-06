using System;
using System.Collections.Generic;
using System.Reflection;
using LiteSim;
using Xunit;

namespace LiteSim.Tests
{
    /// <summary>
    /// 公开/私有面**编译期边界守卫**（第二档）。
    ///
    /// **为什么需要**：该边界原本只写在注释里，而注释会漂移——实证两例：
    /// ① <c>EntitySlot.FireStanceFrames</c> 的注释长期写"私有面"，而 proto（<c>fire_stance_frames=19</c>）
    ///    与 <c>ComputePublicChecksum</c> 都表明它早已公共化；<c>SimChecksum</c> 内部两处注释互相矛盾。
    /// ② <c>CorpseFrames</c> 曾"注释说公共面、wire 却没字段"→ 客户端重建恒 0 → 尸体期内每帧假和解。
    ///
    /// 本类把边界钉成**可执行的断言**：标注（<see cref="StateLayerAttribute"/>）与
    /// 行为（两套 checksum）必须一致，任何一侧漂移即红。
    /// </summary>
    public class StateLayerContractTests
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static StateLayer? LayerOf(FieldInfo f)
        {
            var attr = f.GetCustomAttribute<StateLayerAttribute>();
            return attr?.Layer;
        }

        // ---- ① 反身一致：EntitySlot 每个字段的标注 ⇔ 它在两套 checksum 中的实际参与 ----

        [Fact]
        public void EntitySlot_公共面标注字段_必须在公共口径中可观测()
        {
            // 逐个公共面字段做"扰动实验"：改它 → 公共口径 checksum **必变**。
            // 这直接钉死"标注说公共面、实际没进公共口径"的漏改（正是 CorpseFrames 曾犯的错）。
            foreach (FieldInfo f in typeof(EntitySlot).GetFields(Instance))
            {
                if (LayerOf(f) != StateLayer.Public) continue;
                if (f.FieldType.IsValueType && f.FieldType.IsGenericType) continue;   // 无泛型字段，防御

                uint before, after;
                bool changed = PerturbAndMeasure(f, out before, out after);
                Assert.True(changed,
                    $"EntitySlot.{f.Name} 标注为 Public，但改动后公共口径 checksum 未变——"
                    + "要么它其实不是公共面（改标注），要么 ComputePublicChecksum 漏了它（漏 = 静默分叉）");
            }
        }

        [Fact]
        public void EntitySlot_未标注字段_不得进公共口径()
        {
            // 反向：未标注（纯私有/瞬态）= 不进公共口径。
            // FaceExitTurning 是当前唯一的这类字段——它可由输入历史重放重建，故不占协议字段号。
            foreach (FieldInfo f in typeof(EntitySlot).GetFields(Instance))
            {
                if (LayerOf(f) != null) continue;

                uint before, after;
                bool changed = PerturbAndMeasure(f, out before, out after);
                Assert.False(changed,
                    $"EntitySlot.{f.Name} 未标注层次，但改动后公共口径 checksum 变了——"
                    + "它事实上是公共面（必须补标注），否则协议字段会被漏发");
            }
        }

        [Fact]
        public void EntitySlot_私有面标注字段_不进公共口径但进全量口径()
        {
            foreach (FieldInfo f in typeof(EntitySlot).GetFields(Instance))
            {
                if (LayerOf(f) != StateLayer.Private) continue;

                uint pubBefore, pubAfter, fullBefore, fullAfter;
                PerturbAndMeasure(f, out pubBefore, out pubAfter, out fullBefore, out fullAfter);

                Assert.False(pubBefore != pubAfter,      // lint-allow R3（uint 位级判等）
                    $"EntitySlot.{f.Name} 标注为 Private，但公共口径 checksum 变了——"
                    + "私有面进公共比对口径会制造永假和解（客户端重建不了）");
                Assert.True(fullBefore != fullAfter,     // lint-allow R3（uint 位级判等）
                    $"EntitySlot.{f.Name} 标注为 Private，但全量口径 checksum 也没变——"
                    + "私有面必须进全量口径（回滚/重放对账的确定性状态）");
            }
        }

        // ---- ② 边界穷尽：每个字段都必须被显式表态（防"忘了标"的静默漏网）----

        [Fact]
        public void EntitySlot_字段清单与标注面显式对账_防静默漏标()
        {
            // 快照契约的字段清单（契约冻结）。新增字段必须在此登记并同时打标注——
            // 本断言的存在意义就是"逼后来者做一次有意识的决定"，而不是默默加个字段。
            string[] expected = {
                "Id", "Pos", "Vel", "Yaw", "Hp", "Flags",
                "Shield", "Kills", "Deaths", "SelectedWeapon",
                "FireStanceFrames", "FaceExitTurning", "CorpseFrames",
            };

            var actual = new List<string>();
            foreach (FieldInfo f in typeof(EntitySlot).GetFields(Instance)) actual.Add(f.Name);

            Assert.Equal(expected.Length, actual.Count);
            foreach (string name in expected)
                Assert.Contains(name, actual);
        }

        [Fact]
        public void 公共面字段清单_与公开私有边界文档一致()
        {
            // 公共面的**权威清单**（与 proto SlotDelta 的字段一一对应）。
            // proto 侧对账在 LiteNet 的 SnapshotCodecTests（本工程不引 LiteNet）——
            // 这里锁定 LiteSim 侧那一半，两侧各守一半、合成完整闭环。
            string[] publicFields = {
                "Id", "Pos", "Vel", "Yaw", "Hp", "Flags",
                "Shield", "Kills", "Deaths", "SelectedWeapon",
                "FireStanceFrames", "CorpseFrames",
            };

            var actual = new List<string>();
            foreach (FieldInfo f in typeof(EntitySlot).GetFields(Instance))
                if (LayerOf(f) == StateLayer.Public) actual.Add(f.Name);

            Assert.Equal(publicFields.Length, actual.Count);
            foreach (string name in publicFields) Assert.Contains(name, actual);
        }

        // ---- ③ 变更检测：公共面变动必须同时改两套口径（防止只改一处）----

        [Fact]
        public void 公共面字段变动_公共与全量两套口径都必变()
        {
            foreach (FieldInfo f in typeof(EntitySlot).GetFields(Instance))
            {
                if (LayerOf(f) != StateLayer.Public) continue;

                uint pubBefore, pubAfter, fullBefore, fullAfter;
                PerturbAndMeasure(f, out pubBefore, out pubAfter, out fullBefore, out fullAfter);

                Assert.True(pubBefore != pubAfter,       // lint-allow R3
                    $"公共面字段 {f.Name} 改了但公共口径没变");
                Assert.True(fullBefore != fullAfter,     // lint-allow R3
                    $"公共面字段 {f.Name} 改了但全量口径没变（全量口径是超集，必含公共面）");
            }
        }

        // ---- ④ 运行态结构体的标注（ActionRuntime 槽内混合是结构性的）----

        [Fact]
        public void ActionRuntime_主动作槽字段公共_技能账本字段私有()
        {
            // 主动作槽（索引 0）的 Id/Frame/Phase 进 SlotDelta 的主动作摘要 → 公共
            Assert.Equal(StateLayer.Public, LayerOf(typeof(ActionRuntime).GetField("ActionId", Instance)));
            Assert.Equal(StateLayer.Public, LayerOf(typeof(ActionRuntime).GetField("StartFrame", Instance)));
            Assert.Equal(StateLayer.Public, LayerOf(typeof(ActionRuntime).GetField("Phase", Instance)));

            // 技能账本（冷却/充能/去重令牌）是本人私有面
            Assert.Equal(StateLayer.Private, LayerOf(typeof(ActionRuntime).GetField("CooldownEnd", Instance)));
            Assert.Equal(StateLayer.Private, LayerOf(typeof(ActionRuntime).GetField("Charges", Instance)));
            Assert.Equal(StateLayer.Private, LayerOf(typeof(ActionRuntime).GetField("CastToken", Instance)));
        }

        [Fact]
        public void 运行态结构体_字段必须全部显式表态()
        {
            // 与 EntitySlot 同纪律：结构体的每个字段都要有层次表态（防漏标）
            AssertAllAnnotated(typeof(ActionRuntime));
            AssertAllAnnotated(typeof(StatusSlotData));
            AssertAllAnnotated(typeof(MatchBagSlot));
        }

        [Fact]
        public void WeaponRuntime_整表私有_不得有公共面字段()
        {
            // 弹药/换弹/节拍全属本人私有面（他人只见 EntitySlot.SelectedWeapon 的投影）
            foreach (FieldInfo f in typeof(WeaponRuntime).GetFields(Instance))
            {
                if (!f.FieldType.IsValueType) continue;
                Assert.NotEqual(StateLayer.Public, LayerOf(f));
            }
        }

        // ---- ⑤ 标注本身不改运行时行为（纯元数据）----

        [Fact]
        public void 标注_不影响checksum_纯元数据()
        {
            // 同构世界（字段值相同）在不同标注下 checksum 必须一致——标注不上 wire、不进哈希
            var a = new SimWorldState { RngState = 7UL };
            a.Spawn(new EntitySlot { Hp = 60 }, out _);
            var b = new SimWorldState { RngState = 7UL };
            b.Spawn(new EntitySlot { Hp = 60 }, out _);

            Assert.Equal(SimChecksum.ComputeChecksum(a), SimChecksum.ComputeChecksum(b));
            Assert.Equal(SimChecksum.ComputePublicChecksum(a), SimChecksum.ComputePublicChecksum(b));
        }

        // ---- 辅助 ----

        private static void AssertAllAnnotated(Type t)
        {
            foreach (FieldInfo f in t.GetFields(Instance))
            {
                if (!f.FieldType.IsValueType) continue;
                Assert.True(LayerOf(f) != null,
                    $"{t.Name}.{f.Name} 未标注同步层次——运行态字段必须显式表态（Public/Private），"
                    + "否则公开/私有边界又回到只活在注释里的状态");
            }
        }

        /// <summary>把某字段设为"非默认值"后，观察两套口径是否变化。</summary>
        private static bool PerturbAndMeasure(FieldInfo f, out uint pubBefore, out uint pubAfter)
        {
            return PerturbAndMeasure(f, out pubBefore, out pubAfter, out _, out _);
        }

        private static bool PerturbAndMeasure(FieldInfo f, out uint pubBefore, out uint pubAfter,
            out uint fullBefore, out uint fullAfter)
        {
            var world = NewWorld();
            pubBefore = SimChecksum.ComputePublicChecksum(world);
            fullBefore = SimChecksum.ComputeChecksum(world);

            // 装箱 → 反射改字段 → 拆箱写回（值类型字段的通用扰动手法）
            object boxed = world.Entities[0];
            f.SetValue(boxed, NextValue(f, f.GetValue(boxed)));
            world.Entities[0] = (EntitySlot)boxed;

            pubAfter = SimChecksum.ComputePublicChecksum(world);
            fullAfter = SimChecksum.ComputeChecksum(world);
            return pubBefore != pubAfter;   // lint-allow R3（uint 位级判等）
        }

        private static SimWorldState NewWorld()
        {
            var w = new SimWorldState { RngState = 1UL };
            w.Spawn(new EntitySlot
            {
                Hp = 100,
                Pos = new SimVector3(1f, 2f, 3f),
                Vel = new SimVector3(0.5f, 0f, -0.5f),
                Yaw = 0.25f,
                Flags = 3u,
                Shield = 10,
                Kills = 2,
                Deaths = 1,
                SelectedWeapon = 1,
                FireStanceFrames = 30,
                CorpseFrames = 0,
                FaceExitTurning = 0,
            }, out _);
            return w;
        }

        /// <summary>按字段类型给一个"与当前值不同"的确定值（扰动实验的输入）。</summary>
        private static object NextValue(FieldInfo f, object current)
        {
            switch (current)
            {
                case long v: return v + 1000L;
                case int v: return v + 1000;
                case uint v: return v + 1000u;
                case byte v: return (byte)(v + 7);
                case float v: return v + 1.25f;
                case SimVector3 v: return new SimVector3(v.X + 1f, v.Y + 1f, v.Z + 1f);
                default: throw new InvalidOperationException("未覆盖的字段类型：" + f.FieldType.Name);
            }
        }
    }
}
