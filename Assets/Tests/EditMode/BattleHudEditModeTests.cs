using LiteSim;
using LiteTesting;
using LiteTesting.Unity;
using LiteGame.UI;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// HUD 面板驱动的 L2 EditMode 覆盖：HP 文本/填充随预测态（值变化跳过）、弹匣/未装备/换弹标签
    /// （<see cref="WeaponSlotState"/> 私有面）、本地未对齐整组隐藏、Dispose 归零。
    /// 预测态与本地 Id 走构造注入——EditMode 无流程依赖，确定性。
    /// 真场景面板观感（Synty 素材布局）归 PlayMode/手测；模板结构另由 run_script 验收。
    /// </summary>
    public sealed class BattleHudEditModeTests : UnityTestBase
    {
        private (CanvasGroup Panel, HpBar Bar, TextMeshProUGUI Ammo, GameObject Reload) BuildPanel()
        {
            GameObject panelGo = Scope.CreateGameObject("Panel", typeof(RectTransform), typeof(CanvasGroup));
            GameObject barGo = Scope.CreateGameObject("HpBar", typeof(RectTransform));
            var sliderGo = Scope.CreateGameObject("Slider", typeof(RectTransform), typeof(Slider));
            sliderGo.transform.SetParent(barGo.transform, false);
            var slider = sliderGo.GetComponent<Slider>();
            slider.interactable = false;
            var hpBar = barGo.AddComponent<HpBar>();
            hpBar.Slider = slider;
            GameObject ammoGo = Scope.CreateGameObject("Ammo", typeof(RectTransform), typeof(TextMeshProUGUI));
            GameObject reloadGo = Scope.CreateGameObject("Reload", typeof(RectTransform));
            reloadGo.SetActive(false);
            return (panelGo.GetComponent<CanvasGroup>(), hpBar,
                ammoGo.GetComponent<TextMeshProUGUI>(), reloadGo);
        }

        private static BattleHudDriver BuildDriver(SimWorldState world, long selfId,
            CanvasGroup panel, HpBar bar, TextMeshProUGUI ammo, GameObject reload)
            => new BattleHudDriver(world, () => selfId, panel, bar, ammo, reload);

        [Test]
        [Category(TestCategory.Contract)]
        public void 血条_预测态HP写入文本与填充_值变化才刷新()
        {
            var world = new SimWorldState { RngState = 1UL };
            long selfId = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f) }, out int slot);
            var (panel, bar, ammo, reload) = BuildPanel();
            var driver = BuildDriver(world, selfId, panel, bar, ammo, reload);

            driver.Tick();
            Assert.AreEqual(1f, bar.Slider.value, 0.0001f);

            world.Entities[slot].Hp = 73;
            driver.Tick();
            Assert.AreEqual(0.73f, bar.Slider.value, 0.01f);   // 值变化才重写前条
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 弹药_装备槽弹匣数与换弹态_未装备显示占位()
        {
            var world = new SimWorldState { RngState = 1UL };
            long selfId = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f) }, out int slot);
            var (panel, bar, ammo, reload) = BuildPanel();
            var driver = BuildDriver(world, selfId, panel, bar, ammo, reload);

            driver.Tick();
            Assert.AreEqual("--", ammo.text);           // 未装备（懒装备前）

            world.Entities[slot].SelectedWeapon = 0;    // 装备槽 0：弹匣 17、换弹中
            world.Weapons[slot * SimConfig.WeaponSlotsPerEntity].MagAmmo = 17;
            world.Weapons[slot * SimConfig.WeaponSlotsPerEntity].State = WeaponSlotState.Reloading;
            driver.Tick();
            Assert.AreEqual("17", ammo.text);
            Assert.IsTrue(reload.activeSelf, "Reloading 态应显示换弹标签");

            world.Weapons[slot * SimConfig.WeaponSlotsPerEntity].State = WeaponSlotState.Ready;
            driver.Tick();
            Assert.IsFalse(reload.activeSelf, "Ready 态应收起换弹标签");
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 可见性_本地未对齐整组隐藏_对齐恢复()
        {
            var world = new SimWorldState { RngState = 1UL };
            world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f) }, out _);
            var (panel, bar, ammo, reload) = BuildPanel();
            var driver = BuildDriver(world, 0L, panel, bar, ammo, reload);   // Id=0：未对齐

            driver.Tick();
            Assert.AreEqual(0f, panel.alpha, "未对齐整组隐藏");
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void Dispose_面板归零不残留()
        {
            var world = new SimWorldState { RngState = 1UL };
            long selfId = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f) }, out _);
            var (panel, bar, ammo, reload) = BuildPanel();
            var driver = BuildDriver(world, selfId, panel, bar, ammo, reload);
            driver.Tick();
            Assert.AreEqual(1f, panel.alpha);

            driver.Dispose();
            Assert.AreEqual(0f, panel.alpha, "离场归零——不残留半透明面板");
            driver.Dispose();                           // 幂等
        }
    }
}
