using System.Collections.Generic;
using LiteSim;
using LiteTesting;
using LiteTesting.Unity;
using NUnit.Framework;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 出生保护壳驱动的 L2 EditMode 覆盖（FX 域）：保护态/视图查询/壳工厂走**构造注入**，确定性驱动。
    /// 钉住：保护态（`Frame &lt; InvulnerableUntilFrame`，**端点不含**）开关、保护期内**跟随对应玩家视图**、
    /// 本地与远端各自独立壳、无视图不显示、Dispose 收起且幂等。
    /// 真场景观感（金色壳/包裹尺寸）归 PlayMode/手测。
    /// </summary>
    public sealed class BattleProtectionFxEditModeTests : UnityTestBase
    {
        private sealed class Harness
        {
            public SimWorldState World;
            public readonly Dictionary<int, GameObject> Views = new Dictionary<int, GameObject>();
            public readonly Dictionary<int, GameObject> Shells = new Dictionary<int, GameObject>();
            public BattleProtectionFxDriver Driver;

            public GameObject ViewOf(int slot) => Views.TryGetValue(slot, out var view) ? view : null;

            public bool IsProtected(int slot)
                => World.IsAlive(slot)
                && World.Entities[slot].Id != 0
                && World.Frame < World.Entities[slot].InvulnerableUntilFrame;
        }

        private Harness Build(int protection = 120, bool withView = true, int players = 1, int secondProtection = 0)
        {
            var world = new SimWorldState { RngState = 1UL };
            for (int i = 0; i < players; i++)
            {
                world.Spawn(new EntitySlot
                {
                    Hp = 100,
                    Pos = new SimVector3(i * 3f, 0f, 0f),
                    Flags = EntityFlags.Player,
                }, out int slot);
                world.Entities[slot].InvulnerableUntilFrame = world.Frame + (i == 0 ? protection : secondProtection);
            }

            var h = new Harness { World = world };
            if (withView)
            {
                for (int i = 0; i < players; i++)
                {
                    GameObject view = Scope.CreateGameObject("View_" + i, typeof(RectTransform));
                    view.transform.position = new Vector3(i * 3f, 0f, -2f);
                    h.Views[i] = view;
                }
            }

            h.Driver = new BattleProtectionFxDriver(
                h.IsProtected,
                h.ViewOf,
                slot =>
                {
                    GameObject shell = Scope.CreateGameObject("SpawnProtection_" + slot, typeof(RectTransform));
                    h.Shells[slot] = shell;
                    return shell;
                });
            return h;
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 保护期内_壳亮_并跟随本地视图位姿()
        {
            Harness h = Build(protection: 120);

            h.Driver.Tick();
            Assert.IsTrue(h.Driver.IsShellActive(0), "保护期内 → 壳亮");
            Assert.AreEqual(h.Views[0].transform.position + BattleProtectionFxDriver.ShellCenterOffset,
                h.Shells[0].transform.position, "壳按躯干中心跟随角色视图位姿");

            h.Views[0].transform.position = new Vector3(-5f, 1f, 7f);
            h.Driver.Tick();
            Assert.AreEqual(h.Views[0].transform.position + BattleProtectionFxDriver.ShellCenterOffset,
                h.Shells[0].transform.position, "逐帧跟随");
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 端点不含_到期或开火取消即关()
        {
            Harness h = Build(protection: 120);

            h.Driver.Tick();
            Assert.IsTrue(h.Driver.IsShellActive(0));

            h.World.Frame += 120;                            // Frame == 截止帧（端点不含）
            h.Driver.Tick();
            Assert.IsFalse(h.Driver.IsShellActive(0), "端点不含 → 到期即关");

            h.World.Frame = 0;
            h.World.Entities[0].InvulnerableUntilFrame = 0;   // 成功开火取消（Sim 侧置 0）
            h.Driver.Tick();
            Assert.IsFalse(h.Driver.IsShellActive(0), "开火取消 → 壳关");
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 远端保护_独立壳_各自跟随视图()
        {
            Harness h = Build(protection: 120, players: 2, secondProtection: 120);

            h.Driver.Tick();
            Assert.AreEqual(2, h.Driver.ActiveShellCount, "本地与远端各一个壳");
            Assert.AreEqual(h.Views[0].transform.position + BattleProtectionFxDriver.ShellCenterOffset,
                h.Shells[0].transform.position);
            Assert.AreEqual(h.Views[1].transform.position + BattleProtectionFxDriver.ShellCenterOffset,
                h.Shells[1].transform.position);

            h.World.Entities[1].InvulnerableUntilFrame = h.World.Frame;   // 只有远端到期
            h.Driver.Tick();
            Assert.IsTrue(h.Driver.IsShellActive(0), "本地保护仍在");
            Assert.IsFalse(h.Driver.IsShellActive(1), "远端到期即关");
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 无视图不显示_Dispose幂等()
        {
            Harness h = Build(protection: 120, withView: false);

            h.Driver.Tick();
            Assert.AreEqual(0, h.Driver.ActiveShellCount, "无视图 → 不造第二视觉源");

            h.Views[0] = Scope.CreateGameObject("View_0", typeof(RectTransform));
            h.Views[0].transform.position = new Vector3(4f, 0f, 4f);
            h.Driver.Tick();
            Assert.IsTrue(h.Driver.IsShellActive(0), "视图补齐后恢复显示");
            Assert.AreEqual(h.Views[0].transform.position + BattleProtectionFxDriver.ShellCenterOffset,
                h.Shells[0].transform.position);

            h.Views.Remove(0);                                // 视图回收（AOI/灰盒）
            h.Driver.Tick();
            Assert.IsFalse(h.Driver.IsShellActive(0), "视图缺失 → 收起");

            h.Views[0] = Scope.CreateGameObject("View_0b", typeof(RectTransform));
            h.Driver.Tick();
            Assert.IsTrue(h.Driver.IsShellActive(0));

            h.Driver.Dispose();
            Assert.IsFalse(h.Driver.IsShellActive(0));
            h.Driver.Dispose();                              // 幂等
        }
    }
}
