using System.Collections.Generic;
using LiteClient;
using LiteFramework;
using LiteClient.UI;
using LiteSim;
using LiteTesting.Unity;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 全局击杀提示的 L2 EditMode 覆盖（击杀提示批）：
    /// ① <see cref="PublicCombatFeed"/>——首份快照/重连不重播、重复重发去重、新条入队、Reset 复位；
    /// ② <see cref="BattleKillFeedDriver"/>——多条堆叠、行格式（"你"/"玩家{k}"/"玩家{v} 阵亡"）、
    /// 武器图标按装备槽取图、驻留到时回收（计时走 <see cref="UiAnimationClock"/> 可控假件）。
    /// 真场景观感（字号/落位）归 PlayMode/手测。
    /// </summary>
    public sealed class BattleKillFeedEditModeTests : UnityTestBase
    {
        /// <summary>可控 IUIClock 假件（ScaledDelta 由测试直接设定）。</summary>
        private sealed class FakeUIClock : IUIClock
        {
            public float TimeScale { get; set; } = 1f;
            public bool Paused { get; set; }
            public float Now { get; private set; }
            public float ScaledDelta { get; set; }
            public void Tick(float realDelta) { }
            public string StatsName => "FakeUIClock";
            public void Snapshot(Dictionary<string, string> into) => into.Clear();
        }

        private FakeUIClock _clock;

        [SetUp]
        public void BindClock()
        {
            _clock = new FakeUIClock { ScaledDelta = 0f };
            UiAnimationClock.Bind(_clock);
        }

        [TearDown]
        public void UnbindClock()
        {
            UiAnimationClock.ResetForEditorReload();      // 不留全局残留（其他用例依赖未绑定形态）
        }

        /// <summary>构一批事件（Death 型；Value = 击杀帧装备槽——与 proto 的 kind 释义一致）。</summary>
        private static LiteNet.Proto.CombatEventBatch Batch(
            params (long killer, long victim, int frame, int slot)[] records)
        {
            var batch = new LiteNet.Proto.CombatEventBatch { Frame = 100 };
            foreach ((long killer, long victim, int frame, int slot) r in records)
            {
                batch.Records.Add(new LiteNet.Proto.CombatEventEntry
                {
                    Kind = (int)FrameEventKind.Death,
                    A = r.victim,        // A = 死者
                    B = r.killer,        // B = 击杀者（0 = 无归属）
                    Frame = r.frame,
                    Value = r.slot,
                });
            }
            return batch;
        }

        // ---- ① PublicCombatFeed ----

        [Test]
        public void 击杀面_首份快照不重播_新条入队_重复重发去重()
        {
            var feed = new PublicCombatFeed();
            feed.Ingest(Batch((1L, 2L, 10, 0)));
            Assert.AreEqual(0, feed.KillCount, "首份快照：窗口内既有记录标为已见，不重播");

            feed.Ingest(Batch((1L, 2L, 10, 0)));
            Assert.AreEqual(0, feed.KillCount, "窗口随每份快照全量重发——已见记录不重复入队");

            feed.Ingest(Batch((1L, 2L, 10, 0), (3L, 4L, 11, 1)));
            Assert.AreEqual(1, feed.KillCount, "只有新击杀入队");
            Assert.IsTrue(feed.TryDequeueKill(out PublicCombatFeed.KillEntry entry));
            Assert.AreEqual(3L, entry.Killer);
            Assert.AreEqual(4L, entry.Victim);
            Assert.AreEqual(11, entry.Frame);
            Assert.AreEqual(1, entry.WeaponSlot);
            Assert.IsFalse(feed.TryDequeueKill(out _), "取出后队列空");
        }

        [Test]
        public void 击杀面_Reset后按首份处理_不重播断线前击杀()
        {
            var feed = new PublicCombatFeed();
            feed.Ingest(Batch((1L, 2L, 10, 0)));
            feed.Ingest(Batch((1L, 2L, 10, 0), (3L, 4L, 11, 1)));
            Assert.AreEqual(1, feed.KillCount);

            feed.Reset();                                 // 重连：清队列 + 复位已见记忆
            Assert.AreEqual(0, feed.KillCount);

            feed.Ingest(Batch((1L, 2L, 10, 0), (3L, 4L, 11, 1)));
            Assert.AreEqual(0, feed.KillCount, "Reset 后首份快照重新标记已见——不重播断线前击杀");
        }

        // ---- ② BattleKillFeedDriver ----

        private sealed class Harness
        {
            public SimWorldState World;
            public long LocalId;
            public long OtherId;
            public PublicCombatFeed Feed;
            public BattleKillFeedDriver Driver;
            public GameObject[] Rows;
            public TextMeshProUGUI[] Killers;
            public Image[] Weapons;
            public TextMeshProUGUI[] Victims;
            public Sprite Rifle;
            public Sprite Shotgun;
        }

        private Harness Build()
        {
            var world = new SimWorldState { RngState = 1UL };
            long localId = world.Spawn(new EntitySlot { Hp = 100, Flags = EntityFlags.Player }, out _);   // 身份序号 1
            long otherId = world.Spawn(new EntitySlot { Hp = 100, Flags = EntityFlags.Player }, out _);   // 身份序号 2

            var rifle = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);
            var shotgun = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);

            var rows = new GameObject[BattleKillFeedDriver.RowCount];
            var killers = new TextMeshProUGUI[BattleKillFeedDriver.RowCount];
            var weapons = new Image[BattleKillFeedDriver.RowCount];
            var victims = new TextMeshProUGUI[BattleKillFeedDriver.RowCount];
            for (int i = 0; i < BattleKillFeedDriver.RowCount; i++)
            {
                GameObject row = Scope.CreateGameObject("Row" + i, typeof(RectTransform));
                GameObject killerGo = Scope.CreateGameObject("Killer" + i, typeof(RectTransform), typeof(TextMeshProUGUI));
                killerGo.transform.SetParent(row.transform, false);
                GameObject weaponGo = Scope.CreateGameObject("Weapon" + i, typeof(RectTransform), typeof(Image));
                weaponGo.transform.SetParent(row.transform, false);
                GameObject victimGo = Scope.CreateGameObject("Victim" + i, typeof(RectTransform), typeof(TextMeshProUGUI));
                victimGo.transform.SetParent(row.transform, false);

                rows[i] = row;
                killers[i] = killerGo.GetComponent<TextMeshProUGUI>();
                weapons[i] = weaponGo.GetComponent<Image>();
                victims[i] = victimGo.GetComponent<TextMeshProUGUI>();
            }

            var feed = new PublicCombatFeed();
            var h = new Harness
            {
                World = world,
                LocalId = localId,
                OtherId = otherId,
                Feed = feed,
                Rows = rows,
                Killers = killers,
                Weapons = weapons,
                Victims = victims,
                Rifle = rifle,
                Shotgun = shotgun,
            };
            h.Driver = new BattleKillFeedDriver(world, () => h.LocalId, feed,
                new[] { rifle, shotgun }, rows, killers, weapons, victims);
            return h;
        }

        [Test]
        public void 击杀行_他人击杀_你击杀_自杀三种格式_武器图标按槽取图()
        {
            Harness h = Build();
            h.Feed.Ingest(Batch());                                    // 首份：种已见
            h.Feed.Ingest(Batch((h.OtherId, h.LocalId, 10, 1)));       // 他人用槽 1 击杀我
            h.Driver.Tick();

            Assert.IsTrue(h.Rows[0].activeSelf, "首条占第 1 行");
            Assert.AreEqual("玩家2", h.Killers[0].text, "他人击杀 → 占位名（身份序号 2）");
            Assert.AreEqual("玩家1", h.Victims[0].text, "死者 = 本地 → 占位名（身份序号 1）");
            Assert.IsTrue(h.Weapons[0].gameObject.activeSelf, "有武器槽 → 图标显示");
            Assert.AreSame(h.Shotgun, h.Weapons[0].sprite, "图标按击杀帧装备槽取图（槽 1）");

            h.Feed.Ingest(Batch((h.OtherId, h.LocalId, 10, 1), (h.LocalId, h.OtherId, 11, 0)));   // 我用槽 0 击杀他人
            h.Driver.Tick();
            Assert.IsTrue(h.Rows[1].activeSelf, "多条堆叠：第二条占第 2 行，不覆盖第一条");
            Assert.IsTrue(h.Rows[0].activeSelf, "第一条仍在（堆叠保持不变）");
            Assert.AreEqual(BattleKillFeedDriver.LocalKillerLabel, h.Killers[1].text, "我击杀 → 击杀者文案为\"你\"");
            Assert.AreEqual("玩家2", h.Victims[1].text);
            Assert.AreSame(h.Rifle, h.Weapons[1].sprite, "图标按槽 0 取图");

            h.Feed.Ingest(Batch((h.OtherId, h.LocalId, 10, 1), (h.LocalId, h.OtherId, 11, 0), (0L, h.LocalId, 12, -1)));
            h.Driver.Tick();
            Assert.IsTrue(h.Rows[2].activeSelf, "第三条占第 3 行");
            Assert.IsFalse(h.Killers[2].gameObject.activeSelf, "无归属（自杀/环境） → 击杀者件隐藏");
            Assert.IsFalse(h.Weapons[2].gameObject.activeSelf, "无归属 → 武器图标隐藏");
            Assert.AreEqual("玩家1 " + BattleKillFeedDriver.DeathVerb, h.Victims[2].text, "无归属行文案");
        }

        [Test]
        public void 击杀行_驻留到时整组回收()
        {
            Harness h = Build();
            h.Feed.Ingest(Batch());
            for (int i = 0; i < BattleKillFeedDriver.RowCount; i++)
                h.Feed.Ingest(Batch((h.OtherId, h.LocalId, 10 + i, 0)));
            h.Driver.Tick();
            for (int i = 0; i < BattleKillFeedDriver.RowCount; i++)
                Assert.IsTrue(h.Rows[i].activeSelf, $"第 {i + 1} 行占满");
            Assert.AreEqual(0, h.Feed.KillCount, "全部入行，无留队");

            _clock.ScaledDelta = BattleKillFeedDriver.RowDwellSeconds;    // 驻留到期
            h.Driver.Tick();
            for (int i = 0; i < BattleKillFeedDriver.RowCount; i++)
                Assert.IsFalse(h.Rows[i].activeSelf, "驻留到期整组回收（队空 → 不重填）");
        }

        [Test]
        public void 击杀行_行满留队等待_不丢弃不覆盖()
        {
            Harness h = Build();
            h.Feed.Ingest(Batch());
            for (int i = 0; i < BattleKillFeedDriver.RowCount; i++)
                h.Feed.Ingest(Batch((h.OtherId, h.LocalId, 10 + i, 0)));
            h.Driver.Tick();
            Assert.AreEqual(0, h.Feed.KillCount, "四行占满");
            string firstVictimFrame = h.Killers[0].text;

            h.Feed.Ingest(Batch((h.OtherId, h.LocalId, 99, 0)));       // 第 5 条：行满
            h.Driver.Tick();
            Assert.AreEqual(1, h.Feed.KillCount, "行满时新条留队，不丢弃");
            Assert.AreEqual(firstVictimFrame, h.Killers[0].text, "已有行不被覆盖");
            for (int i = 0; i < BattleKillFeedDriver.RowCount; i++)
                Assert.IsTrue(h.Rows[i].activeSelf);
        }

        [Test]
        public void 击杀行_不在册击杀者按无归属_Dispose归位()
        {
            Harness h = Build();
            h.Feed.Ingest(Batch());
            h.Feed.Ingest(Batch((123456L, h.LocalId, 10, 0)));         // 击杀者不在册（无占位名）
            h.Driver.Tick();
            Assert.IsTrue(h.Rows[0].activeSelf, "行仍显示（死者可解析）");
            Assert.IsFalse(h.Killers[0].gameObject.activeSelf, "不在册击杀者 → 无归属口径（击杀者件隐藏，不显空壳）");
            Assert.IsFalse(h.Weapons[0].gameObject.activeSelf, "无归属 → 武器图标隐藏");
            Assert.AreEqual("玩家1 " + BattleKillFeedDriver.DeathVerb, h.Victims[0].text);

            h.Driver.Dispose();
            for (int i = 0; i < BattleKillFeedDriver.RowCount; i++)
                Assert.IsFalse(h.Rows[i].activeSelf, "Dispose 行归位");
            Assert.DoesNotThrow(() => h.Driver.Dispose(), "Dispose 幂等");
        }
    }
}
