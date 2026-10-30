using System.Collections.Generic;
using LiteFramework;
using LiteFramework;
using LiteSim;
using LiteTesting;
using LiteTesting.Unity;
using LiteGame.View;
using NUnit.Framework;
using UnityEngine;
using LiteFramework.UI;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 远端开火火光（公共开火面）的 L2 EditMode 覆盖：
    /// ① <see cref="PublicCombatFeed"/>——首份快照/重连不重播、重复重发去重、新条入队、Reset 复位；
    /// ② <see cref="BattleRemoteMuzzleFlashDriver"/>——他人开火落其枪口挂点播放、本地开火不双闪、
    /// 视图缺位/无挂点静默退化、Dispose 后静默。真实资源/观感归 PlayMode/手测。
    /// </summary>
    public sealed class BattleRemoteFireEditModeTests : UnityTestBase
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
                public Call(string name, Transform attach, bool follow, Vector3 pos, bool at)
                { Name = name; Attach = attach; Follow = follow; Position = pos; At = at; }
            }

            public readonly List<Call> Calls = new List<Call>(8);

            public VfxHandle Play(string name, Transform attach, bool follow, float scale = 1f, object owner = null)
            {
                Calls.Add(new Call(name, attach, follow, default, at: false));
                return new VfxHandle(1);
            }

            public VfxHandle PlayAt(string name, Vector3 position, float scale = 1f, object owner = null)
            {
                Calls.Add(new Call(name, null, follow: false, position, at: true));
                return new VfxHandle(1);
            }

            public void Stop(VfxHandle handle) { }
            public void StopAll(Transform attach) { }
            public void StopAll(object owner) { }
            public string StatsName => "recording-vfx";
            public void Snapshot(Dictionary<string, string> into) { }
        }

        /// <summary>构一批事件（Fire 型：A = 开火者，无对象）。</summary>
        private static LiteNet.Proto.CombatEventBatch Batch(params (long shooter, int frame)[] records)
        {
            var batch = new LiteNet.Proto.CombatEventBatch { Frame = 100 };
            foreach ((long shooter, int frame) r in records)
            {
                batch.Records.Add(new LiteNet.Proto.CombatEventEntry
                {
                    Kind = (int)FrameEventKind.Fire,
                    A = r.shooter,
                    Frame = r.frame,
                });
            }
            return batch;
        }

        // ---- ① PublicCombatFeed ----

        [Test]
        public void 开火面_首份快照不重播_新条入队_重复重发去重()
        {
            var feed = new PublicCombatFeed();
            feed.Ingest(Batch((1L, 10)));
            Assert.AreEqual(0, feed.FireCount, "首份快照：窗口内既有记录标为已见，不重播");

            feed.Ingest(Batch((1L, 10)));
            Assert.AreEqual(0, feed.FireCount, "窗口随每份快照全量重发——已见记录不重复入队");

            feed.Ingest(Batch((1L, 10), (2L, 11)));
            Assert.AreEqual(1, feed.FireCount, "只有新开火入队");
            Assert.IsTrue(feed.TryDequeueFire(out PublicCombatFeed.FireEntry entry));
            Assert.AreEqual(2L, entry.Shooter);
            Assert.AreEqual(11, entry.Frame);
            Assert.IsFalse(feed.TryDequeueFire(out _), "取出后队列空");
        }

        [Test]
        public void 开火面_Reset后按首份处理_不重播断线前开火()
        {
            var feed = new PublicCombatFeed();
            feed.Ingest(Batch((1L, 10)));
            feed.Ingest(Batch((1L, 10), (2L, 11)));
            Assert.AreEqual(1, feed.FireCount);

            feed.Reset();                                 // 重连：清队列 + 复位已见记忆
            Assert.AreEqual(0, feed.FireCount);

            feed.Ingest(Batch((1L, 10), (2L, 11)));
            Assert.AreEqual(0, feed.FireCount, "Reset 后首份快照重新标记已见——不重播断线前开火");
        }

        // ---- ② BattleRemoteMuzzleFlashDriver ----

        /// <summary>本地实体槽 0 + bot 槽 1（视图已建——枪口挂点依赖视图层级）。</summary>
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

        /// <summary>先喂一份空窗口建立基线（首份不重播），再喂目标条目。</summary>
        private static PublicCombatFeed FeedWith(params (long shooter, int frame)[] records)
        {
            var feed = new PublicCombatFeed();
            feed.Ingest(new LiteNet.Proto.CombatEventBatch());          // 空批建立基线（首份不重播）
            feed.Ingest(Batch(records));
            return feed;
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 远端火光_队列条目_落开火者枪口挂点播放()
        {
            var (_, view, self, bot) = Build();
            var vfx = new RecordingVfx();
            using (var driver = new BattleRemoteMuzzleFlashDriver(view, FeedWith((bot, 42)), vfx))
            {
                Transform muzzle = MountMuzzle(view, 1);   // bot 在槽 1

                driver.Tick();

                Assert.AreEqual(1, driver.RemoteFlashCount);
                Assert.AreEqual(1, vfx.Calls.Count);
                Assert.AreEqual(BattleMuzzleFlashDriver.EffectName, vfx.Calls[0].Name);
                Assert.AreSame(muzzle, vfx.Calls[0].Attach, "火光挂在开火者武器 Muzzle 挂点");
                Assert.IsTrue(vfx.Calls[0].Follow, "跟随挂点（开火动画的枪姿态带走火光）");
                Assert.IsFalse(vfx.Calls[0].At, "火光不是世界位播放（挂点系）");
            }
            _ = self;
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 远端火光_本地开火与无挂点与无效实体_不播()
        {
            var (_, view, self, _) = Build();
            var vfx = new RecordingVfx();
            using (var driver = new BattleRemoteMuzzleFlashDriver(view, FeedWith((self, 42)), vfx))
            {
                MountMuzzle(view, 0);
                driver.Tick();
                Assert.AreEqual(0, vfx.Calls.Count, "本地开火走本地帧事件即时播——不消费公共开火面（不双闪）");
            }

            var (_, bareView, _, bareBot) = Build();   // 独立世界：bot 视图保持灰盒（无 Muzzle 挂点）
            var vfx2 = new RecordingVfx();
            using (var driver = new BattleRemoteMuzzleFlashDriver(bareView, FeedWith((bareBot, 43)), vfx2))
            {
                driver.Tick();
                Assert.AreEqual(0, vfx2.Calls.Count, "无挂点静默退化");
                Assert.AreEqual(0, driver.RemoteFlashCount);
            }

            var vfx3 = new RecordingVfx();
            using (var driver = new BattleRemoteMuzzleFlashDriver(bareView, FeedWith((999999L, 44)), vfx3))   // 实体已离场/未对齐
            {
                driver.Tick();
                Assert.AreEqual(0, vfx3.Calls.Count, "无效实体静默跳过");
            }
        }

        [Test]
        [Category(TestCategory.Unit)]
        public void 远端火光_Dispose后不再播()
        {
            var (_, view, _, bot) = Build();
            var vfx = new RecordingVfx();
            var feed = new PublicCombatFeed();
            feed.Ingest(new LiteNet.Proto.CombatEventBatch());          // 空批建立基线（首份不重播）
            feed.Ingest(Batch((bot, 44)));
            var driver = new BattleRemoteMuzzleFlashDriver(view, feed, vfx);
            MountMuzzle(view, 1);

            driver.Tick();
            Assert.AreEqual(1, vfx.Calls.Count);

            feed.Ingest(Batch((bot, 44), (bot, 45)));           // 新一条待播
            driver.Dispose();
            driver.Tick();
            Assert.AreEqual(1, vfx.Calls.Count, "Dispose 后不再播放");
        }
    }
}
