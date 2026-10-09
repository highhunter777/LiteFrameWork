using System.Collections.Generic;
using LiteFramework;
using LiteSim;
using LiteView;
using LiteView.DamageNumbers;
using LiteTesting;
using LiteTesting.Unity;
using NUnit.Framework;
using TMPro;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 伤害数字驱动（<see cref="BattleDamageNumberDriver"/>，《命中反馈与伤害数字专项设计》批B）的
    /// L2 EditMode 覆盖：本地造成/承受过滤（旁观不产实例）、档位色（承受染红/造成白/爆头独立成条）、
    /// 合并窗口（同目标**同档**二连击一条实例·文本累计；爆头/普通分离）、预算淘汰（超限不静默丢）、
    /// 到期收口（释放归池、复用不新建）、受击点锚定（不随实体视图移动；视图消失无影响）。
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
                Assert.IsNotNull(driver.TryGetActive(botA, HitLocalRole.Caused, crit: false));
                Assert.IsNotNull(driver.TryGetActive(self, HitLocalRole.Received, crit: false));
                Assert.IsNull(driver.TryGetActive(botA, HitLocalRole.Bystander, crit: false), "旁观被口径过滤");
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

                // **必须先 Tick 再读色/字号**：档位色与字号是**账本**（Active.R/G/B、FontSize），
                // 驱动每帧 Tick 才把它们刷到 TMP 组件（BattleDamageNumberDriver.Tick 末段）。
                // 不 Tick 就读组件，读到的是 TMP 默认白 + 默认字号 ⇒ 断言会把正确的承受档判成白色。
                driver.Tick();

                Color caused = driver.TryGetActive(botA, HitLocalRole.Caused, crit: false).color;
                Color received = driver.TryGetActive(self, HitLocalRole.Received, crit: false).color;
                Assert.Greater(caused.g, 0.9f, "造成档白色（绿分量≈1）");
                Assert.Less(received.g, caused.g - 0.5f, "承受档染红（绿分量显著低）");

                // 爆头独立成条（用户裁决"连击合并区分爆头/普通"）：普通条不被染红，爆头自成一条且字号更大
                Color normalBefore = driver.TryGetActive(self, HitLocalRole.Received, crit: false).color;
                driver.OnHitFeedback(new HitFeedbackContext(FrameEventKind.Crit, self, botA, 0, -1, 40,
                    Vector3.zero, HitLocalRole.Received));
                driver.Tick();                               // 新条目的档位同样要刷到 TMP
                TextMeshPro normal = driver.TryGetActive(self, HitLocalRole.Received, crit: false);
                TextMeshPro critTmp = driver.TryGetActive(self, HitLocalRole.Received, crit: true);
                Assert.IsNotNull(normal, "普通条仍在（未被爆头合并/覆盖）");
                Assert.IsNotNull(critTmp, "爆头独立成条");
                Assert.Greater(critTmp.fontSize, normal.fontSize, "爆头档字号更大（独立条）");
                Assert.AreEqual(normalBefore.g, normal.color.g, 1e-4f, "普通条颜色不被爆头污染");
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

                Assert.AreEqual(1, driver.ActiveCount, "窗口内同档二连击合并为一条");
                Assert.AreEqual("55", driver.TryGetActive(botA, HitLocalRole.Caused, crit: false).text, "文本=累计值");
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

                TextMeshPro oldest = driver.TryGetActive(1000, HitLocalRole.Caused, crit: false);
                Assert.IsNull(oldest, "最旧条目（首个）被淘汰");
                Assert.IsNotNull(driver.TryGetActive(1000 + BattleDamageNumberDriver.Budget, HitLocalRole.Caused, crit: false),
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
                TextMeshPro first = driver.TryGetActive(botA, HitLocalRole.Caused, crit: false);
                Assert.IsTrue(first.gameObject.activeSelf, "在场飘字激活");

                clock.NowValue += DamageNumberMotion.FadeEndSeconds + 0.1f;   // 拨针过寿命
                driver.Tick();
                Assert.AreEqual(0, driver.ActiveCount, "到期收口——在册清零");
                Assert.IsFalse(first.gameObject.activeSelf, "实例归池（翻转激活态隐藏）");

                driver.OnHitFeedback(Hit(botA, 1, self, 30, HitLocalRole.Caused, Vector3.zero));
                TextMeshPro reused = driver.TryGetActive(botA, HitLocalRole.Caused, crit: false);
                Assert.AreSame(first, reused, "池复用——同实例不新建");
            }
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 锚定_受击点_不随视图移动()
        {
            // 用户裁决："飘字应该在受击部位飘"——锚 = 命中事件世界位（弹道命中部位），不跟随实体。
            var (_, view, self, botA) = Build(new Vector3(5f, 1f, 0f));
            var hitPos = new Vector3(5f, 1.2f, 0.4f);
            using (var driver = new BattleDamageNumberDriver(view, NewTmp, new FakeClock()))
            {
                driver.OnHitFeedback(Hit(botA, 1, self, 30, HitLocalRole.Caused, hitPos));
                driver.Tick();
                Vector3 before = driver.TryGetActive(botA, HitLocalRole.Caused, crit: false).transform.position;
                Assert.AreEqual(hitPos.x, before.x, DamageNumberMotion.PushDistance + 0.01f, "横向锚在受击点（±推挤）");
                Assert.AreEqual(hitPos.z, before.z, DamageNumberMotion.PushDistance + 0.01f, "纵向锚在受击点（±推挤）");

                Assert.IsTrue(view.TryGetView(1, out var botView), "bot 视图可解析");
                botView.transform.position += new Vector3(5f, 0f, 0f);   // 目标移动 5m
                driver.Tick();
                Vector3 after = driver.TryGetActive(botA, HitLocalRole.Caused, crit: false).transform.position;

                Assert.AreEqual(before.x, after.x, 1e-3f, "不随实体移动（x 不动）");
                Assert.AreEqual(before.z, after.z, 1e-3f, "不随实体移动（z 不动）");
            }
        }

        [Test]
        [Category(TestCategory.Unit)]
        public void 视图消失_受击点锚不受影响_照常淡出收口()
        {
            var (_, view, self, botA) = Build(new Vector3(5f, 1f, 0f));
            var clock = new FakeClock();
            using (var driver = new BattleDamageNumberDriver(view, NewTmp, clock))
            {
                driver.OnHitFeedback(Hit(botA, 1, self, 30, HitLocalRole.Caused, new Vector3(5f, 1.2f, 0f)));
                driver.Tick();
                Vector3 before = driver.TryGetActive(botA, HitLocalRole.Caused, crit: false).transform.position;

                Assert.IsTrue(view.TryGetView(1, out var botView));
                Object.DestroyImmediate(botView);                              // 目标视图消失（despawn/回收）

                clock.NowValue += 0.1f;
                driver.Tick();
                Vector3 during = driver.TryGetActive(botA, HitLocalRole.Caused, crit: false).transform.position;
                Assert.AreEqual(before.x, during.x, 1e-3f, "受击点锚不受视图影响");
                Assert.Greater(during.y, before.y, "升浮照常继续");
                Assert.AreNotEqual(Vector3.zero, during, "不瞬移到原点——锚定非重锚");

                clock.NowValue += DamageNumberMotion.FadeEndSeconds + 0.2f;
                driver.Tick();
                Assert.AreEqual(0, driver.ActiveCount, "淡出完毕照常收口（不悬挂不泄漏）");
            }
        }
    }
}
