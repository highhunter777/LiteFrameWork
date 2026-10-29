using LiteSim;
using LiteTesting;
using LiteTesting.Unity;
using LiteFramework.UI;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// HUD 面板驱动的 L2 EditMode 覆盖：HP 文本/填充随预测态（值变化跳过）、弹匣/剩余备弹读出/未装备/换弹标签
    /// （<see cref="WeaponSlotState"/> 私有面）、本地未对齐整组隐藏、Dispose 归零。
    /// 预测态与本地 Id 走构造注入——EditMode 无流程依赖，确定性。
    /// 真场景面板观感（Synty 素材布局）归 PlayMode/手测；模板结构另由 run_script 验收。
    /// </summary>
    public sealed class BattleHudEditModeTests : UnityTestBase
    {
        private (CanvasGroup Panel, HpBar Bar, TextMeshProUGUI Ammo, TextMeshProUGUI Reserve,
            GameObject Slash, GameObject Reload) BuildPanel()
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
            GameObject reserveGo = Scope.CreateGameObject("ReserveAmmo", typeof(RectTransform), typeof(TextMeshProUGUI));
            GameObject slashGo = Scope.CreateGameObject("Slash", typeof(RectTransform));
            GameObject reloadGo = Scope.CreateGameObject("Reload", typeof(RectTransform));
            reloadGo.SetActive(false);
            return (panelGo.GetComponent<CanvasGroup>(), hpBar,
                ammoGo.GetComponent<TextMeshProUGUI>(), reserveGo.GetComponent<TextMeshProUGUI>(), slashGo, reloadGo);
        }

        private static BattleHudDriver BuildDriver(SimWorldState world, long selfId,
            CanvasGroup panel, HpBar bar, TextMeshProUGUI ammo, GameObject reload,
            TextMeshProUGUI reserveAmmo = null, GameObject slash = null)
            => new BattleHudDriver(new LiteGame.BattleReadModel(() => world, () => selfId),
                panel, bar, ammo, reload, reserveAmmo, slash);

        [Test]
        [Category(TestCategory.Contract)]
        public void 血条_预测态HP写入文本与填充_值变化才刷新()
        {
            var world = new SimWorldState { RngState = 1UL };
            long selfId = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f) }, out int slot);
            var (panel, bar, ammo, reserve, slash, reload) = BuildPanel();
            var driver = BuildDriver(world, selfId, panel, bar, ammo, reload, reserve, slash);

            driver.Tick();
            Assert.AreEqual(1f, bar.Slider.value, 0.0001f);

            world.Entities[slot].Hp = 73;
            driver.Tick();
            Assert.AreEqual(0.73f, bar.Slider.value, 0.01f);   // 值变化才重写前条
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 弹药_弹匣与剩余备弹读出_未装备随占位整组收起()
        {
            var world = new SimWorldState { RngState = 1UL };
            // 未装备 = SelectedWeapon -1（约定见 EntitySlot）——显式置位表达意图；C# int 默认 0 是"装备槽 0"而非未装备
            long selfId = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f), SelectedWeapon = -1 }, out int slot);
            var (panel, bar, ammo, reserve, slash, reload) = BuildPanel();
            var driver = BuildDriver(world, selfId, panel, bar, ammo, reload, reserve, slash);

            driver.Tick();
            Assert.AreEqual(BattleHudDriver.UnarmedAmmoPlaceholder, ammo.text);   // 未装备 → 占位"--"
            Assert.IsFalse(reserve.gameObject.activeSelf, "未装备：备弹读出随占位收起（不显示陈旧备弹）");
            Assert.IsFalse(slash.activeSelf, "未装备：分隔符同收起（读出只剩占位）");

            world.Entities[slot].SelectedWeapon = 0;    // 装备槽 0：弹匣 17、备弹 90、换弹中
            ref WeaponRuntime weapon = ref world.Weapons[slot * SimConfig.WeaponSlotsPerEntity];
            weapon.MagAmmo = 17;
            weapon.ReserveAmmo = 90;
            weapon.State = WeaponSlotState.Reloading;
            driver.Tick();
            Assert.AreEqual("17", ammo.text);
            Assert.AreEqual("90", reserve.text, "备弹＝本人私有面 ReserveAmmo（「当前 / 备弹」读出右半边）");
            Assert.IsTrue(reserve.gameObject.activeSelf, "已装备：备弹读出在显");
            Assert.IsTrue(slash.activeSelf, "已装备：分隔符在显");
            Assert.IsTrue(reload.activeSelf, "Reloading 态应显示换弹标签");

            weapon.State = WeaponSlotState.Ready;
            driver.Tick();
            Assert.IsFalse(reload.activeSelf, "Ready 态应收起换弹标签");
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 备弹_换弹消耗递减写值_值未变不重写()
        {
            var world = new SimWorldState { RngState = 1UL };
            long selfId = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f), SelectedWeapon = 0 }, out int slot);
            var (panel, bar, ammo, reserve, slash, reload) = BuildPanel();
            var driver = BuildDriver(world, selfId, panel, bar, ammo, reload, reserve, slash);

            ref WeaponRuntime weapon = ref world.Weapons[slot * SimConfig.WeaponSlotsPerEntity];
            weapon.MagAmmo = 30;
            weapon.ReserveAmmo = 90;
            driver.Tick();
            Assert.AreEqual("90", reserve.text);

            reserve.text = "SENTINEL";                  // 人为改值（模拟外部写）
            driver.Tick();                              // 备弹未变 —— 应跳过写
            Assert.AreEqual("SENTINEL", reserve.text, "备弹未变不重写（脏位）");

            weapon.ReserveAmmo = 60;                    // 换弹从备弹扣（sim CompleteReload 口径）——值变化才写
            driver.Tick();
            Assert.AreEqual("60", reserve.text, "备弹递减后重写");
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 可见性_本地未对齐整组隐藏_对齐恢复()
        {
            var world = new SimWorldState { RngState = 1UL };
            world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f) }, out _);
            var (panel, bar, ammo, reserve, slash, reload) = BuildPanel();
            var driver = BuildDriver(world, 0L, panel, bar, ammo, reload, reserve, slash);   // Id=0：未对齐

            driver.Tick();
            Assert.AreEqual(0f, panel.alpha, "未对齐整组隐藏");
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void Dispose_面板归零不残留()
        {
            var world = new SimWorldState { RngState = 1UL };
            long selfId = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f) }, out _);
            var (panel, bar, ammo, reserve, slash, reload) = BuildPanel();
            var driver = BuildDriver(world, selfId, panel, bar, ammo, reload, reserve, slash);
            driver.Tick();
            Assert.AreEqual(1f, panel.alpha);

            driver.Dispose();
            Assert.AreEqual(0f, panel.alpha, "离场归零——不残留半透明面板");
            driver.Dispose();                           // 幂等
        }
    }
}
