using LiteSim;
using LiteTesting;
using LiteTesting.Unity;
using NUnit.Framework;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 出生保护壳驱动的 L2 EditMode 覆盖（FX 域）：镜像与视图查询走**构造注入**，确定性驱动。
    /// 钉住：保护态（`Frame &lt; InvulnerableUntilFrame`，**端点不含**）开关、保护期内**跟随**本地角色视图位姿、
    /// 无视图（灰盒/缺失）不猜位姿、未对齐收起、Dispose 收起且幂等。
    /// 真场景观感（金色壳/包裹尺寸）归 PlayMode/手测。
    /// </summary>
    public sealed class BattleProtectionFxEditModeTests : UnityTestBase
    {
        private sealed class Harness
        {
            public SimWorldState World;
            public int LocalSlot = -1;
            public GameObject View;          // 跟随目标（null = 无视图）
            public GameObject Shell;
            public BattleProtectionFxDriver Driver;
        }

        private Harness Build(int protection = 120, bool withView = true)
        {
            var world = new SimWorldState { RngState = 1UL };
            long id = world.Spawn(new EntitySlot
            {
                Hp = 100,
                Pos = new SimVector3(0f, 0f, 0f),
                Flags = EntityFlags.Player,
            }, out int slot);
            world.Entities[slot].InvulnerableUntilFrame = world.Frame + protection;

            var h = new Harness
            {
                World = world,
                LocalSlot = slot,
                View = withView ? Scope.CreateGameObject("LocalView", typeof(RectTransform)) : null,
                Shell = Scope.CreateGameObject("SpawnProtection", typeof(RectTransform)),
            };
            if (withView) h.View.transform.position = new Vector3(3f, 0f, -2f);
            h.Driver = new BattleProtectionFxDriver(world, () => h.LocalSlot, _ => h.View, h.Shell);
            return h;
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 保护期内_壳亮_并跟随本地视图位姿()
        {
            Harness h = Build(protection: 120);

            h.Driver.Tick();
            Assert.IsTrue(h.Shell.activeSelf, "保护期内 → 壳亮");
            Assert.AreEqual(h.View.transform.position, h.Shell.transform.position, "壳跟随本地角色视图位姿");

            h.View.transform.position = new Vector3(-5f, 1f, 7f);
            h.Driver.Tick();
            Assert.AreEqual(h.View.transform.position, h.Shell.transform.position, "逐帧跟随");
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 端点不含_到期或开火取消即关()
        {
            Harness h = Build(protection: 120);

            h.Driver.Tick();
            Assert.IsTrue(h.Shell.activeSelf);

            h.World.Frame += 120;                            // Frame == 截止帧（端点不含）
            h.Driver.Tick();
            Assert.IsFalse(h.Shell.activeSelf, "端点不含 → 到期即关");

            h.World.Frame = 0;
            h.World.Entities[h.LocalSlot].InvulnerableUntilFrame = 0;   // 成功开火取消（Sim 侧置 0）
            h.Driver.Tick();
            Assert.IsFalse(h.Shell.activeSelf, "开火取消 → 壳关");
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 无视图不猜位姿_未对齐收起_Dispose幂等()
        {
            Harness h = Build(protection: 120, withView: false);

            h.Driver.Tick();
            Assert.IsTrue(h.Shell.activeSelf, "无视图仍亮壳（只不跟随）");
            Assert.AreEqual(Vector3.zero, h.Shell.transform.position, "无视图 → 不猜位姿（保持原处）");

            h.LocalSlot = -1;                                // 未对齐
            h.Driver.Tick();
            Assert.IsFalse(h.Shell.activeSelf, "未对齐 → 收起");

            h.LocalSlot = 0;
            h.Driver.Tick();
            Assert.IsTrue(h.Shell.activeSelf);

            h.Driver.Dispose();
            Assert.IsFalse(h.Shell.activeSelf);
            h.Driver.Dispose();                              // 幂等
        }
    }
}
