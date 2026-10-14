using System;
using LiteSim;
using LiteTesting;
using LiteTesting.Unity;
using LiteView;
using NUnit.Framework;
using TMPro;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 死亡/复活 HUD 驱动的 L2 EditMode 覆盖（阵亡批）：镜像与本地判据走**构造注入**（免构造 SimView——
    /// 与面板驱动同款），确定性驱动。钉住：阵亡显隐、倒计时**整秒脏位**（同秒不写、跨秒才写）、
    /// 复活收起并清归属、**归属只吃 Death×我阵亡**且带占位名（与排行榜共用身份序号单源）、
    /// 保护角标开关（**端点不含**）、未对齐整体收起、Dispose 幂等。
    /// 真场景观感（字号/落位/金色壳）归 PlayMode/手测。
    /// </summary>
    public sealed class BattleRespawnHudEditModeTests : UnityTestBase
    {
        private sealed class Harness
        {
            public SimWorldState World;
            public int LocalSlot = -1;
            public long LocalId;
            public long KillerId;
            public bool Dead;
            public RectTransform Respawn;
            public TextMeshProUGUI Title;
            public TextMeshProUGUI Countdown;
            public RectTransform Protection;
            public BattleRespawnHudDriver Driver;
        }

        private static long SpawnPlayer(SimWorldState world, int hp)
            => world.Spawn(new EntitySlot
            {
                Hp = hp,
                Pos = new SimVector3(0f, 0f, 0f),
                Flags = EntityFlags.Player,
            }, out _);

        /// <param name="spawnKillerFirst">先额外出一个玩家（占更小 Id → 身份序号 1）——归属断言用。</param>
        private Harness Build(bool dead = false, int respawnDelay = 180, int protection = 0,
            bool spawnKillerFirst = false)
        {
            var world = new SimWorldState { RngState = 1UL };
            long killerId = spawnKillerFirst ? SpawnPlayer(world, 100) : 0L;
            long localId = SpawnPlayer(world, dead ? 0 : 100);
            if (!world.TryResolve(localId, out int slot)) throw new InvalidOperationException("生成后解析不到槽位");
            if (dead) world.Entities[slot].RespawnFrame = world.Frame + respawnDelay;
            world.Entities[slot].InvulnerableUntilFrame = world.Frame + protection;

            GameObject respawnGo = Scope.CreateGameObject("Respawn", typeof(RectTransform));
            GameObject titleGo = Scope.CreateGameObject("Title", typeof(RectTransform), typeof(TextMeshProUGUI));
            titleGo.transform.SetParent(respawnGo.transform, false);
            GameObject cdGo = Scope.CreateGameObject("Countdown", typeof(RectTransform), typeof(TextMeshProUGUI));
            cdGo.transform.SetParent(respawnGo.transform, false);
            GameObject protGo = Scope.CreateGameObject("Protection", typeof(RectTransform));

            var h = new Harness
            {
                World = world,
                LocalSlot = slot,
                LocalId = localId,
                KillerId = killerId,
                Dead = dead,
                Respawn = (RectTransform)respawnGo.transform,
                Title = titleGo.GetComponent<TextMeshProUGUI>(),
                Countdown = cdGo.GetComponent<TextMeshProUGUI>(),
                Protection = (RectTransform)protGo.transform,
            };
            h.Driver = new BattleRespawnHudDriver(world, () => h.LocalSlot, () => h.Dead,
                h.Respawn, h.Title, h.Countdown, h.Protection);
            return h;
        }

        private static HitFeedbackContext Death(long entityId, long otherId, HitLocalRole role)
            => new HitFeedbackContext(FrameEventKind.Death, entityId, otherId,
                slot: 0, otherSlot: 0, value: 0, worldPos: Vector3.zero, localRole: role);

        [Test]
        [Category(TestCategory.Contract)]
        public void 阵亡_面板显示_倒计时整秒脏位_复活收起()
        {
            Harness h = Build(dead: true, respawnDelay: 180);   // 3s

            h.Driver.Tick();
            Assert.IsTrue(h.Respawn.gameObject.activeSelf, "阵亡 → 中央面板显示");
            Assert.AreEqual("3", h.Countdown.text, "剩余 180 帧 = 3 秒");

            h.World.Frame += 30;                            // 同秒内推进
            h.Countdown.text = "SENTINEL";
            h.Driver.Tick();
            Assert.AreEqual("SENTINEL", h.Countdown.text, "整秒未变 → 不写（脏位）");

            h.World.Frame += 30;                            // 跨到 2 秒
            h.Driver.Tick();
            Assert.AreEqual("2", h.Countdown.text, "跨秒才写");

            h.Dead = false;                                 // 复活
            h.Driver.Tick();
            Assert.IsFalse(h.Respawn.gameObject.activeSelf, "复活 → 收起");
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 归属_只吃Death且我阵亡_带占位名()
        {
            Harness h = Build(dead: true, spawnKillerFirst: true);   // 击杀者先出 → 身份序号 1

            h.Driver.OnHitFeedback(Death(h.LocalId, h.KillerId, HitLocalRole.Caused));
            h.Driver.Tick();
            Assert.AreEqual(BattleRespawnHudDriver.DeathTitle, h.Title.text, "非我阵亡的 Death 不写归属");

            h.Driver.OnHitFeedback(Death(h.LocalId, h.KillerId, HitLocalRole.Received));
            h.Driver.Tick();
            Assert.AreEqual("被 玩家1 击杀", h.Title.text, "我阵亡 → 归属带击杀者身份序号（占位名与排行榜同源）");

            h.Driver.OnHitFeedback(Death(h.LocalId, 0L, HitLocalRole.Received));   // 自杀/环境（无击杀者）
            h.Driver.Tick();
            Assert.AreEqual(BattleRespawnHudDriver.DeathTitle, h.Title.text, "无归属 → 回到'已阵亡'");
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 保护角标_端点不含_开火取消即关()
        {
            Harness h = Build(protection: 120);              // 2s 保护

            h.Driver.Tick();
            Assert.IsTrue(h.Protection.gameObject.activeSelf, "保护期内 → 角标亮");

            h.World.Frame += 119;                            // Frame < 截止帧
            h.Driver.Tick();
            Assert.IsTrue(h.Protection.gameObject.activeSelf, "端点前一帧仍在保护");

            h.World.Frame += 1;                              // Frame == 截止帧（端点不含）
            h.Driver.Tick();
            Assert.IsFalse(h.Protection.gameObject.activeSelf, "端点不含 → 到期即关");

            h.World.Frame = 0;
            h.World.Entities[h.LocalSlot].InvulnerableUntilFrame = 999;
            h.Driver.Tick();
            Assert.IsTrue(h.Protection.gameObject.activeSelf);

            h.World.Entities[h.LocalSlot].InvulnerableUntilFrame = 0;   // 成功开火取消（Sim 侧置 0）
            h.Driver.Tick();
            Assert.IsFalse(h.Protection.gameObject.activeSelf, "开火取消 → 角标关");
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 未对齐_整体收起_Dispose幂等()
        {
            Harness h = Build(dead: true);
            h.Driver.Tick();
            Assert.IsTrue(h.Respawn.gameObject.activeSelf);

            h.LocalSlot = -1;                                // 未对齐（视图未建/拆）
            h.Driver.Tick();
            Assert.IsFalse(h.Respawn.gameObject.activeSelf, "未对齐 → 收起（不猜）");
            Assert.IsFalse(h.Protection.gameObject.activeSelf);

            h.Driver.Dispose();
            Assert.IsFalse(h.Respawn.gameObject.activeSelf);
            h.Driver.Dispose();                              // 幂等
        }
    }
}
