using System.Collections.Generic;
using LiteFramework;
using LiteSim;
using LiteSim.View;
using LiteSim.View.DamageNumbers;
using LiteTesting;
using LiteTesting.Unity;
using NUnit.Framework;
using TMPro;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 伤害数字驱动（<see cref="BattleDamageNumberDriver"/>，《命中反馈与伤害数字专项设计》批B）的
    /// L2 EditMode 覆盖：本地造成/承受过滤（旁观不产实例）、档位色（承受染红/造成白/暴击升级）、
    /// 合并窗口（同目标二连击一条实例·文本累计）、预算淘汰（超限不静默丢）、到期收口（释放归池、
    /// 复用不新建）、跟随（目标视图移动随锚）与 despawn 降级（冻结最后锚点继续淡出——不悬挂）。
    /// 路由/上下文解析由 HitFeedbackDispatcherEditModeTests 把守——此处直喂上下文。
    /// 时钟：FakeClock 拨针推进（世界钟口径——寿命/合并窗走 IWorldClock，变速/暂停语义内建）。
    /// </summary>
    public sealed class BattleDamageNumberEditModeTests : UnityTestBase
    {
        /// <summary>世界钟替身（VfxServiceEditModeTests.TestClock 同形）：测试拨针推进。</summary>
        private sealed class FakeClock : IWorldClock
        {
            public float NowValue;
            public float Now => NowValue;
            public float ScaledDelta => 0f;
            public float TimeScale { get; set; } = 1f;
            public bool Paused { get; set; }
            public string StatsName => "FakeClock";
            public void Snapshot(Dictionary<string, string> into) { }
            public void Tick(float realDelta) { }
        }

        /// <summary>本地实体 (0,1,0) + 逐参数追加 bot；视图对齐并落位（跟随/锚点依赖视图建立）。</summary>
        private (SimWorldState World, SimView View, long Self, long BotA) Build(params Vector3[] bots)
        {
            var world = new SimWorldState { RngState = 1UL };
            long self = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 1f, 0f) }, out _);
            long botA = 0;
            if (bots.Length > 0)
                botA = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(bots[0].x, bots[0].y, bots[0].z) }, out _);

            SimView view = new SimView(world, null,
                factory: (loc, parent) => Scope.CreateGameObject(loc),
                recycler: null);
            view.AlignLocal(self);
            view.Tick(1f / 60f);
            return (world, view, self, botA);
        }

        /// <summary>测试工厂：裸 TMP 组件（不依赖 prefab 资产——资产面归生产装配/手测）。</summary>
        private TextMeshPro NewTmp()
        {
            return Scope.CreateGameObject("DmgNum", typeof(TextMeshPro)).GetComponent<TextMeshPro>();
        }

        private static HitFeedbackContext Hit(long target, int slot, long shooter, int value,
            HitLocalRole role, Vector3 pos)
        {
            return new HitFeedbackContext(FrameEventKind.Hit, target, shooter, slot, -1, value, pos, role);
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 过滤_只显本地造成与承受_旁观不产实例()
        {
            var (_, view, self, botA) = Build(new Vector3(5f, 1f, 0f));
            using (var driver = new BattleDamageNumberDriver(view, NewTmp, new FakeClock()))
            {
                driver.OnHitFeedback(Hit(botA, 1, self, 30, HitLocalRole.Caused, Vector3.zero));     // 我打 bot
                driver.OnHitFeedback(Hit(self, 0, botA, 25, HitLocalRole.Received, Vector3.zero));   // bot 打我
                driver.OnHitFeedback(Hit(botA, 1, botA, 20, HitLocalRole.Bystander, Vector3.zero)); // bot 自残（旁观）

                Assert.AreEqual(2, driver.ActiveCount, "造成+承受各一条，旁观不产实例");
                Assert.IsNotNull(driver.TryGetActive(botA, HitLocalRole.Caused));
                Assert.IsNotNull(driver.TryGetActive(self, HitLocalRole.Received));
                Assert.IsNull(driver.TryGetActive(botA, HitLocalRole.Bystander), "旁观被口径过滤");
            }
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 档位_承受染红造成白_暴击升级字号()
        {
            var (_, view, self, botA) = Build(new Vector3(5f, 1f, 0f));
            using (var driver = new BattleDamageNumberDriver(view, NewTmp, new FakeClock()))
            {
                driver.OnHitFeedback(Hit(botA, 1, self, 30, HitLocalRole.Caused, Vector3.zero));
                driver.OnHitFeedback(Hit(self, 0, botA, 25, HitLocalRole.Received, Vector3.zero));

                Color caused = driver.TryGetActive(botA, HitLocalRole.Caused).color;
                Color received = driver.TryGetActive(self, HitLocalRole.Received).color;
                Assert.Greater(caused.g, 0.9f, "造成档白色（绿分量≈1）");
                Assert.Less(received.g, caused.g - 0.5f, "承受档染红（绿分量显著低）");

                // 暴击粘滞升级：普通承受 → 追加暴击命中 → 字号放大
                float before = driver.TryGetActive(self, HitLocalRole.Received).fontSize;
                driver.OnHitFeedback(new HitFeedbackContext(FrameEventKind.Crit, self, botA, 0, -1, 40,
                    Vector3.zero, HitLocalRole.Received));
                float after = driver.TryGetActive(self, HitLocalRole.Received).fontSize;
                Assert.Greater(after, before, "暴击命中并入后字号升级");
            }
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 合并窗口_同目标二连击_一条实例文本累计()
        {
            var (_, view, self, botA) = Build(new Vector3(5f, 1f, 0f));
            var clock = new FakeClock();
            using (var driver = new BattleDamageNumberDriver(view, NewTmp, clock))
            {
                driver.OnHitFeedback(Hit(botA, 1, self, 30, HitLocalRole.Caused, Vector3.zero));
                clock.NowValue += 0.1f;                                      // 窗口内（0.45s）——世界钟拨针
                driver.OnHitFeedback(Hit(botA, 1, self, 25, HitLocalRole.Caused, Vector3.zero));

                Assert.AreEqual(1, driver.ActiveCount, "窗口内二连击合并为一条");
                Assert.AreEqual("55", driver.TryGetActive(botA, HitLocalRole.Caused).text, "文本=累计值");
            }
        }

        [Test]
        [Category(TestCategory.Unit)]
        public void 预算_超限淘汰最旧_实例归池不静默丢()
        {
            var (_, view, _, _) = Build();
            using (var driver = new BattleDamageNumberDriver(view, NewTmp, new FakeClock()))
            {
                for (int i = 0; i < BattleDamageNumberDriver.Budget + 1; i++)
                    driver.OnHitFeedback(Hit(1000 + i, -1, 1, 10, HitLocalRole.Caused, Vector3.zero));

                Assert.AreEqual(BattleDamageNumberDriver.Budget, driver.ActiveCount,
                    "超预算一条——淘汰最旧后恰为预算值");

                TextMeshPro oldest = driver.TryGetActive(1000, HitLocalRole.Caused);
                Assert.IsNull(oldest, "最旧条目（首个）被淘汰");
                Assert.IsNotNull(driver.TryGetActive(1000 + BattleDamageNumberDriver.Budget, HitLocalRole.Caused),
                    "最新条目在场");
            }
        }

        [Test]
        [Category(TestCategory.Unit)]
        public void 到期_淡出终点释放归池_新命中复用不新建()
        {
            var (_, view, self, botA) = Build(new Vector3(5f, 1f, 0f));
            var clock = new FakeClock();
            using (var driver = new BattleDamageNumberDriver(view, NewTmp, clock))
            {
                driver.OnHitFeedback(Hit(botA, 1, self, 30, HitLocalRole.Caused, Vector3.zero));
                driver.Tick();
                TextMeshPro first = driver.TryGetActive(botA, HitLocalRole.Caused);
                Assert.IsTrue(first.gameObject.activeSelf, "在场飘字激活");

                clock.NowValue += DamageNumberMotion.FadeEndSeconds + 0.1f;   // 拨针过寿命
                driver.Tick();
                Assert.AreEqual(0, driver.ActiveCount, "到期收口——在册清零");
                Assert.IsFalse(first.gameObject.activeSelf, "实例归池（翻转激活态隐藏）");

                driver.OnHitFeedback(Hit(botA, 1, self, 30, HitLocalRole.Caused, Vector3.zero));
                TextMeshPro reused = driver.TryGetActive(botA, HitLocalRole.Caused);
                Assert.AreSame(first, reused, "池复用——同实例不新建");
            }
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 跟随_目标视图移动飘字随锚()
        {
            var (_, view, self, botA) = Build(new Vector3(5f, 1f, 0f));
            using (var driver = new BattleDamageNumberDriver(view, NewTmp, new FakeClock()))
            {
                driver.OnHitFeedback(Hit(botA, 1, self, 30, HitLocalRole.Caused, Vector3.zero));
                driver.Tick();
                Vector3 before = driver.TryGetActive(botA, HitLocalRole.Caused).transform.position;

                Assert.IsTrue(view.TryGetView(1, out var botView), "bot 视图可解析");
                botView.transform.position += new Vector3(5f, 0f, 0f);
                driver.Tick();
                Vector3 after = driver.TryGetActive(botA, HitLocalRole.Caused).transform.position;

                Assert.AreEqual(5f, after.x - before.x, 0.1f, "目标移动 5m——飘字随锚平移（推挤不变）");
            }
        }

        [Test]
        [Category(TestCategory.Unit)]
        public void despawn降级_视图消失冻结最后锚点_继续淡出不悬挂()
        {
            var (_, view, self, botA) = Build(new Vector3(5f, 1f, 0f));
            var clock = new FakeClock();
            using (var driver = new BattleDamageNumberDriver(view, NewTmp, clock))
            {
                driver.OnHitFeedback(Hit(botA, 1, self, 30, HitLocalRole.Caused, Vector3.zero));
                driver.Tick();
                Vector3 before = driver.TryGetActive(botA, HitLocalRole.Caused).transform.position;

                Assert.IsTrue(view.TryGetView(1, out var botView));
                Object.DestroyImmediate(botView);                              // 目标视图消失（despawn/回收）

                clock.NowValue += 0.1f;
                driver.Tick();
                Vector3 during = driver.TryGetActive(botA, HitLocalRole.Caused).transform.position;
                Assert.AreEqual(before.x, during.x, 0.05f, "横向冻结于最后锚点（不跳不漂）");
                Assert.Greater(during.y, before.y, "升浮照常继续");
                Assert.AreNotEqual(Vector3.zero, during, "不瞬移到原点/命中点——冻结非重锚");

                clock.NowValue += DamageNumberMotion.FadeEndSeconds + 0.2f;
                driver.Tick();
                Assert.AreEqual(0, driver.ActiveCount, "淡出完毕照常收口（不悬挂不泄漏）");
            }
        }
    }
}
