using System;
using System.Reflection;
using LiteSim;
using Xunit;

namespace LiteSim.Tests
{
    /// <summary>
    /// SimWorldDiff 覆盖面守卫：**每个进全量 checksum 的字段，差异报告都必须能指出来**。
    ///
    /// **为什么需要**：checksum 只能回答"是否相同"，回答不了"哪里不同"——后者正是
    /// <see cref="SimWorldDiff"/> 存在的意义。但它的字段清单是手写的：checksum 侧经
    /// [StateLayer] 生成物自动跟上，本类若漏扩展，"checksum 报警但报告无差异"的静默漏检
    /// 就回来了（分型表曾整层缺失——checksum 报警、报告空白）。
    ///
    /// 守卫方法与 <see cref="SyncCoverageTests"/> 同款**扰动法**：逐字段把一侧改成非默认值，
    /// <see cref="SimWorldDiff.Compare"/> 必须产出至少一条差异，且路径含该字段名
    /// （钉"能指出"，不是笼统的"报了不同"）。
    /// </summary>
    public class SimWorldDiffCoverageTests
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        // ---- ① 实体槽全字段（活体槽位扰动；Pos/Vel 报告拆分量路径）----

        [Fact]
        public void 实体槽字段_扰动后报告必指出()
        {
            foreach (FieldInfo f in typeof(EntitySlot).GetFields(Instance))
            {
                var (a, b) = TwoWorlds();
                object boxed = b.Entities[0];
                f.SetValue(boxed, Nudged(f, f.GetValue(boxed)));
                b.Entities[0] = (EntitySlot)boxed;
                AssertFieldReported(a, b, $"EntitySlot.{f.Name}");
            }
        }

        // ---- ② 每实体运行态数组与分型表行（含主动作摘要槽与私有技能账本槽）----

        [Fact]
        public void 运行态数组与分型表字段_扰动后报告必指出()
        {
            AssertArrayFieldReported(typeof(WeaponRuntime), "Weapons", 0);
            AssertArrayFieldReported(typeof(ActionRuntime), "Actions", 0);                          // 主动作摘要槽
            AssertArrayFieldReported(typeof(ActionRuntime), "Actions", SimConfig.ActionSlotsPerEntity + 1);   // 私有技能账本槽
            AssertArrayFieldReported(typeof(StatusSlotData), "Status", 0);
            AssertArrayFieldReported(typeof(MatchBagSlot), "MatchBag", 0);
            AssertArrayFieldReported(typeof(ItemState), "Items", 0);
            AssertArrayFieldReported(typeof(ProjectileState), "Projectiles", 0);
            AssertArrayFieldReported(typeof(ZoneState), "Zones", 0);
        }

        // ---- ③ Match 全字段 / 头部 / 平面 blob / 资源账本 ----

        [Fact]
        public void Match字段_扰动后报告必指出()
        {
            foreach (FieldInfo f in typeof(MatchStateData).GetFields(Instance))
            {
                var (a, b) = TwoWorlds();
                object boxed = b.Match;
                f.SetValue(boxed, Nudged(f, f.GetValue(boxed)));
                b.Match = (MatchStateData)boxed;
                AssertFieldReported(a, b, $"Match.{f.Name}");
            }
        }

        [Fact]
        public void 头部与blob_扰动后报告必指出()
        {
            var (a, b) = TwoWorlds();
            b.Frame += 7;
            AssertFieldReported(a, b, "Frame");

            var (a2, b2) = TwoWorlds();
            b2.RngState ^= 0xDEADBEEFUL;
            AssertFieldReported(a2, b2, "RngState");

            var (a3, b3) = TwoWorlds();
            b3.Globals[5] = 0xAB;
            AssertFieldReported(a3, b3, "Globals");

            var (a4, b4) = TwoWorlds();
            b4.CustomData[3] = 0xCD;
            AssertFieldReported(a4, b4, "CustomData");

            var (a5, b5) = TwoWorlds();
            b5.Resources[0] = 7;
            AssertFieldReported(a5, b5, "Resources");
        }

        // ---- 辅助 ----

        /// <summary>两个同种子世界（拷贝关系）——扰动 b 后比对。</summary>
        private static (SimWorldState a, SimWorldState b) TwoWorlds()
        {
            var a = NewWorld();
            var b = new SimWorldState();
            a.CopyTo(b);
            return (a, b);
        }

        private static void AssertArrayFieldReported(Type elementType, string arrayName, int index)
        {
            FieldInfo arrField = typeof(SimWorldState).GetField(arrayName, Instance);
            Assert.NotNull(arrField);

            foreach (FieldInfo f in elementType.GetFields(Instance))
            {
                if (!f.FieldType.IsValueType) continue;

                var (a, b) = TwoWorlds();
                Array arr = (Array)arrField.GetValue(b);
                object boxed = arr.GetValue(index);
                f.SetValue(boxed, Nudged(f, f.GetValue(boxed)));
                arr.SetValue(boxed, index);

                AssertFieldReported(a, b, $"{elementType.Name}.{f.Name}");
            }
        }

        /// <summary>比对并断言：至少一条差异，且某条路径含字段名（"能指出"，非笼统报不同）。</summary>
        private static void AssertFieldReported(SimWorldState a, SimWorldState b, string fieldDesc)
        {
            var report = SimWorldDiff.Compare(a, b);
            string fieldName = fieldDesc.Substring(fieldDesc.LastIndexOf('.') + 1);
            Assert.True(report.Differences.Count > 0,   // lint-allow R3（int 计数判等，非浮点精度比较）
                $"{fieldDesc} 进了 checksum 但差异报告无输出——SimWorldDiff 漏比对该字段"
                + "（checksum 报警、报告空白——漏检 = 回到\"不知道为什么\"）");
            Assert.Contains(report.Differences, d => d.Path.Contains(fieldName));
        }

        private static SimWorldState NewWorld()
        {
            var w = new SimWorldState { RngState = 12345UL };
            long id0 = w.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(-5f, 0f, 0f) }, out _);
            w.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(5f, 0f, 0f) }, out _);
            w.Match = new MatchStateData { Phase = 1, Timer = 10800, Round = 1 };

            // 运行态与分型行给非默认值（确保扰动有意义）
            w.Items[0] = new ItemState { ItemDefId = 501, Count = 1, OwnerId = id0, AgeFrames = 3 };
            w.Projectiles[0] = new ProjectileState { ItemDefId = 601, Speed = 12f, DetonateFrame = 30, OwnerId = id0 };
            w.Zones[0] = new ZoneState { ItemDefId = 701, Radius = 3f, RemainingFrames = 180, OwnerId = id0 };
            w.Weapons[0] = new WeaponRuntime { WeaponDefId = 1, MagAmmo = 10, ReserveAmmo = 30 };
            w.Actions[SimConfig.ActionSlotsPerEntity + 1] = new ActionRuntime { ActionId = 305, CooldownEnd = 100 };
            w.Status[0] = new StatusSlotData { EffectId = 51, Param = 30 };
            w.MatchBag[0] = new MatchBagSlot { ItemDefId = 801, Count = 2 };
            w.Resources[0] = 1;
            return w;
        }

        /// <summary>按字段类型给一个"与当前值不同"的确定值（枚举按值域轮转）。</summary>
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
                    if (f.FieldType.IsEnum)
                    {
                        return Convert.ChangeType(
                            (Convert.ToInt32(current, System.Globalization.CultureInfo.InvariantCulture) + 1)
                                % Enum.GetValues(f.FieldType).Length,
                            Enum.GetUnderlyingType(f.FieldType));
                    }
                    throw new InvalidOperationException("未覆盖的字段类型：" + f.FieldType.Name);
            }
        }
    }
}
