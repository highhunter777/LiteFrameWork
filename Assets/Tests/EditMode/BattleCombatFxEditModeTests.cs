using System.Collections.Generic;
using LiteFramework;
using LiteSim;
using LiteView;
using LiteTesting;
using LiteTesting.Unity;
using NUnit.Framework;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 战斗特效驱动（<see cref="BattleMuzzleFlashDriver"/> / <see cref="BattleImpactFxDriver"/>）的
    /// L2 EditMode 覆盖：**假 VFX 服务**记录播放调用——
    /// 火光：Fire 事件 → 武器挂点（`Weapon_Rifle/Muzzle`）跟随播放；非 Fire、无挂点、无槽位不播；
    /// 命中特效：**本地造成的 Hit/Crit → 受击点世界位**（PlayAt）；承受/旁观/非命中不播；爆头放大；
    /// Dispose 后不再播放。真实资源/观感归 PlayMode/手测（资源经构建器归置 `Assets/FX/fx_*`）。
    /// </summary>
    public sealed class BattleCombatFxEditModeTests : UnityTestBase
    {
        /// <summary>录制型 VFX 替身（只记调用，不建实例——零素材验证接线面）。</summary>
        private sealed class RecordingVfx : IVFXService
        {
            public readonly struct Call
            {
                public readonly string Name;
                public readonly Transform Attach;
                public readonly bool Follow;
                public readonly Vector3 Position;
                public readonly bool At;
                public readonly float Scale;
                public Call(string name, Transform attach, bool follow, Vector3 pos, bool at, float scale)
                { Name = name; Attach = attach; Follow = follow; Position = pos; At = at; Scale = scale; }
            }

            public readonly List<Call> Calls = new List<Call>(8);

            public VfxHandle Play(string name, Transform attach, bool follow, float scale = 1f)
            {
                Calls.Add(new Call(name, attach, follow, default, at: false, scale));
                return new VfxHandle(1);
            }

            public VfxHandle PlayAt(string name, Vector3 position, float scale = 1f)
            {
                Calls.Add(new Call(name, null, follow: false, position, at: true, scale));
                return new VfxHandle(1);
            }

            public void Stop(VfxHandle handle) { }
            public void StopAll(Transform attach) { }

            public string StatsName => "recording-vfx";
            public void Snapshot(Dictionary<string, string> into) { }
        }

        /// <summary>本地实体槽 0 + bot 槽 1（视图已建——火光挂点依赖视图层级）。</summary>
        private (SimWorldState World, SimView View, long Self, long Bot) Build()
        {
            var world = new SimWorldState { RngState = 1UL };
            long self = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 1f, 0f) }, out _);
            long bot = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(5f, 0f, 0f) }, out _);

            SimView view = new SimView(world, null,
                factory: (loc, parent) => Scope.CreateGameObject(loc),
                recycler: null);
            view.AlignLocal(self);
            view.Tick(1f / 60f);
            return (world, view, self, bot);
        }

        /// <summary>给某槽视图挂一个 Muzzle 挂点（模拟武器锚点；`Weapon_Rifle` 中间层只为贴近真层级）。</summary>
        private Transform MountMuzzle(SimView view, int slot)
        {
            Assert.IsTrue(view.TryGetView(slot, out var go), "槽位视图应已建立");
            var weapon = Scope.CreateGameObject("Weapon_Rifle");
            weapon.transform.SetParent(go.transform, false);
            var muzzle = Scope.CreateGameObject("Muzzle");
            muzzle.transform.SetParent(weapon.transform, false);
            return muzzle.transform;
        }

        private static HitFeedbackContext Fire(long shooter, int slot)
            => new HitFeedbackContext(FrameEventKind.Fire, shooter, 0L, slot, -1, 0, new Vector3(1f, 1f, 0f), HitLocalRole.Caused);

        private static HitFeedbackContext HitEvent(FrameEventKind kind, long target, int slot, HitLocalRole role, Vector3 pos)
            => new HitFeedbackContext(kind, target, 1L, slot, -1, 30, pos, role);

        [Test]
        [Category(TestCategory.Contract)]
        public void 火光_Fire事件_武器挂点跟随播放()
        {
            var (_, view, self, _) = Build();
            var vfx = new RecordingVfx();
            using (var driver = new BattleMuzzleFlashDriver(view, vfx))
            {
                Transform muzzle = MountMuzzle(view, 0);

                driver.OnHitFeedback(Fire(self, 0));

                Assert.AreEqual(1, driver.FlashCount);
                Assert.AreEqual(1, vfx.Calls.Count);
                Assert.AreEqual(BattleMuzzleFlashDriver.EffectName, vfx.Calls[0].Name);
                Assert.AreSame(muzzle, vfx.Calls[0].Attach, "火光挂在武器 Muzzle 挂点");
                Assert.IsTrue(vfx.Calls[0].Follow, "跟随挂点（开火动画的枪姿态带走火光）");
                Assert.IsFalse(vfx.Calls[0].At, "火光不是世界位播放（挂点系）");
            }
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 火光_非Fire与无挂点与无效槽位_不播()
        {
            var (_, view, self, bot) = Build();
            var vfx = new RecordingVfx();
            using (var driver = new BattleMuzzleFlashDriver(view, vfx))
            {
                // 非 Fire：命中/暴击不喷火光
                driver.OnHitFeedback(HitEvent(FrameEventKind.Hit, bot, 0, HitLocalRole.Caused, Vector3.one));
                driver.OnHitFeedback(HitEvent(FrameEventKind.Crit, bot, 0, HitLocalRole.Caused, Vector3.one));
                Assert.AreEqual(0, vfx.Calls.Count);

                // Fire 但视图无 Muzzle 挂点（灰盒形态）→ 静默退化
                driver.OnHitFeedback(Fire(self, 0));
                Assert.AreEqual(0, vfx.Calls.Count);
                Assert.AreEqual(0, driver.FlashCount);

                // Fire 但槽位无效
                driver.OnHitFeedback(Fire(self, -1));
                Assert.AreEqual(0, vfx.Calls.Count);
            }
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 命中特效_本地造成HitCrit_受击点世界位播放_爆头放大()
        {
            var (_, view, self, bot) = Build();
            var vfx = new RecordingVfx();
            using (var driver = new BattleImpactFxDriver(vfx))
            {
                var hitPos = new Vector3(5f, 1.6f, 0.2f);
                driver.OnHitFeedback(HitEvent(FrameEventKind.Hit, bot, 1, HitLocalRole.Caused, hitPos));

                Assert.AreEqual(1, driver.ImpactCount);
                Assert.AreEqual(1, vfx.Calls.Count);
                Assert.AreEqual(BattleImpactFxDriver.EffectName, vfx.Calls[0].Name);
                Assert.IsTrue(vfx.Calls[0].At, "命中特效走世界位播放（打哪留哪）");
                Assert.AreEqual(hitPos, vfx.Calls[0].Position, "落点 = 事件命中点（受击部位）");
                Assert.AreEqual(1f, vfx.Calls[0].Scale, 1e-5f, "普通命中不放大");

                driver.OnHitFeedback(HitEvent(FrameEventKind.Crit, bot, 1, HitLocalRole.Caused, hitPos));
                Assert.AreEqual(2, vfx.Calls.Count);
                Assert.Greater(vfx.Calls[1].Scale, 1f, "爆头档放大");
                _ = view; _ = self;
            }
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 命中特效_承受与旁观与Fire_不播()
        {
            var (_, view, self, bot) = Build();
            var vfx = new RecordingVfx();
            using (var driver = new BattleImpactFxDriver(vfx))
            {
                driver.OnHitFeedback(HitEvent(FrameEventKind.Hit, self, 0, HitLocalRole.Received, Vector3.one));
                driver.OnHitFeedback(HitEvent(FrameEventKind.Crit, self, 0, HitLocalRole.Received, Vector3.one));
                driver.OnHitFeedback(HitEvent(FrameEventKind.Hit, bot, 1, HitLocalRole.Bystander, Vector3.one));
                driver.OnHitFeedback(Fire(self, 0));

                Assert.AreEqual(0, vfx.Calls.Count, "只播我打中敌人——承受/旁观/开火不弹命中特效");
                Assert.AreEqual(0, driver.ImpactCount);
                _ = view;
            }
        }

        [Test]
        [Category(TestCategory.Unit)]
        public void Dispose后_两个驱动都不再播放()
        {
            var (_, view, self, bot) = Build();
            var vfx = new RecordingVfx();
            var flash = new BattleMuzzleFlashDriver(view, vfx);
            var impact = new BattleImpactFxDriver(vfx);
            MountMuzzle(view, 0);

            flash.OnHitFeedback(Fire(self, 0));
            impact.OnHitFeedback(HitEvent(FrameEventKind.Hit, bot, 1, HitLocalRole.Caused, Vector3.one));
            Assert.AreEqual(2, vfx.Calls.Count);

            flash.Dispose();
            impact.Dispose();
            flash.OnHitFeedback(Fire(self, 0));
            impact.OnHitFeedback(HitEvent(FrameEventKind.Hit, bot, 1, HitLocalRole.Caused, Vector3.one));
            Assert.AreEqual(2, vfx.Calls.Count, "Dispose 后不再播放");
        }
    }
}
