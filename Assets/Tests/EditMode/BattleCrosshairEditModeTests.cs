using System.Collections.Generic;
using LiteSim;
using LiteSim.View;
using LiteTesting;
using LiteTesting.Unity;
using NUnit.Framework;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 对局准心驱动（2026-10-02 战斗准心批）的 L2 EditMode 覆盖：位置换算（注入屏幕点 → 画布中心系）、
    /// 形态切换（本地预测态 Aiming 位 → Hip/Ads 两组）、可见性与系统光标（上下文门被拦 → 藏准心还光标；
    /// 本地表现未建 → 整体隐藏；Dispose 恢复光标）。位置与光标走注入位——EditMode 无鼠标/画布依赖，确定性。
    /// 真场景（训练场 /Battle HUD）与真鼠标的手感对位归 PlayMode/手测；构建器幂等另由 run_script 验收。
    /// </summary>
    public sealed class BattleCrosshairEditModeTests : UnityTestBase
    {
        /// <summary>灰盒视图 + 本地对齐（IsAiming 走预测态分支，同 CharacterLocomotionEditModeTests 形态）。</summary>
        private SimView BuildLocalView(SimWorldState world, long selfId)
        {
            SimView view = new SimView(world, null,
                factory: (loc, parent) => Scope.CreateGameObject(loc),
                recycler: null);
            view.AlignLocal(selfId);
            view.Tick(1f / 60f);                        // 首帧落位：HasLocalDisplay 置真
            return view;
        }

        /// <summary>
        /// 画布根几何（对齐 Overlay Canvas 世界系：轴心=中心、位在 (960,540)、尺寸 1920x1080——
        /// 该系下"世界单位 = 屏幕像素"，ScreenPointToLocalPointInRectangle(cam=null) 与线上同一条数学路径）。
        /// </summary>
        private (RectTransform Plane, RectTransform Reticle) BuildCanvasRects()
        {
            GameObject planeGo = Scope.CreateGameObject("Plane", typeof(RectTransform));
            RectTransform plane = (RectTransform)planeGo.transform;
            plane.position = new Vector3(960f, 540f, 0f);
            plane.sizeDelta = new Vector2(1920f, 1080f);

            GameObject reticleGo = Scope.CreateGameObject("Reticle", typeof(RectTransform));
            RectTransform reticle = (RectTransform)reticleGo.transform;
            reticle.SetParent(plane, false);
            return (plane, reticle);
        }

        private (GameObject Hip, GameObject Ads) BuildFormObjects(RectTransform reticle)
        {
            GameObject hip = Scope.CreateGameObject("Hip", typeof(RectTransform));
            ((RectTransform)hip.transform).SetParent(reticle, false);
            GameObject ads = Scope.CreateGameObject("Ads", typeof(RectTransform));
            ((RectTransform)ads.transform).SetParent(reticle, false);
            return (hip, ads);
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 准心_位置随注入屏幕点_画布中心系换算()
        {
            var world = new SimWorldState { RngState = 1UL };
            long selfId = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f), Yaw = 0f }, out _);
            SimView view = BuildLocalView(world, selfId);
            var (plane, reticle) = BuildCanvasRects();
            var (hip, ads) = BuildFormObjects(reticle);

            var driver = new BattleCrosshairDriver(view, null, plane, reticle, hip, ads,
                screenPosition: () => new Vector2(500f, 300f));
            driver.Tick();

            // 屏幕点 (500,300) 在 1920x1080 中心系 = (-460, -240)——准心贴鼠标位（= 腰射瞄准落点）
            Assert.AreEqual(new Vector3(-460f, -240f, 0f), reticle.localPosition);
            driver.Dispose();
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 准心_本地预测态瞄准位切Ads组_松开回Hip组()
        {
            var world = new SimWorldState { RngState = 1UL };
            long selfId = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f), Yaw = 0f }, out _);
            SimView view = BuildLocalView(world, selfId);
            var (plane, reticle) = BuildCanvasRects();
            var (hip, ads) = BuildFormObjects(reticle);

            var driver = new BattleCrosshairDriver(view, null, plane, reticle, hip, ads,
                screenPosition: () => Vector2.zero);

            world.Entities[0].Flags |= EntityFlags.Aiming;        // 本地预测态瞄准位（驱动读 SimView.IsAiming）
            driver.Tick();
            Assert.IsTrue(ads.activeSelf, "瞄准位 → Ads 形");
            Assert.IsFalse(hip.activeSelf, "瞄准位 → Hip 收");

            world.Entities[0].Flags &= ~(uint)EntityFlags.Aiming;  // 松开右键
            driver.Tick();
            Assert.IsTrue(hip.activeSelf, "松开 → 回腰射形");
            Assert.IsFalse(ads.activeSelf);

            driver.Dispose();
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 准心_上下文门被拦_藏准心还光标_解拦恢复()
        {
            var world = new SimWorldState { RngState = 1UL };
            long selfId = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f), Yaw = 0f }, out _);
            SimView view = BuildLocalView(world, selfId);
            var (plane, reticle) = BuildCanvasRects();
            var (hip, ads) = BuildFormObjects(reticle);

            var input = new InputService();
            bool modalOpen = false;
            input.RegisterBlocker(new IntentGate.BlockerKey("ui.modal", "模态打开"), () => modalOpen);

            var cursor = new List<bool>();
            var driver = new BattleCrosshairDriver(view, input, plane, reticle, hip, ads,
                screenPosition: () => Vector2.zero, setCursorVisible: cursor.Add);

            modalOpen = true;
            input.SampleOnRenderFrame(default);                   // 渲染帧采样 = 门在此裁决（生产序）
            driver.Tick();
            Assert.IsFalse(reticle.gameObject.activeSelf, "被拦 → 藏准心");
            Assert.AreEqual(true, cursor[cursor.Count - 1], "被拦 → 还系统光标（模态要点按钮）");

            modalOpen = false;
            input.SampleOnRenderFrame(default);
            driver.Tick();
            Assert.IsTrue(reticle.gameObject.activeSelf, "解拦 → 准心恢复");
            Assert.AreEqual(false, cursor[cursor.Count - 1], "对局内准心替代光标");

            driver.Dispose();
            Assert.AreEqual(true, cursor[cursor.Count - 1], "离场 → 光标恢复（不遗留隐藏态）");
        }

        [Test]
        [Category(TestCategory.Unit)]
        public void 准心_本地表现未建_整体隐藏还光标()
        {
            var world = new SimWorldState { RngState = 1UL };
            world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f), Yaw = 0f }, out _);
            SimView view = new SimView(world, null,
                factory: (loc, parent) => Scope.CreateGameObject(loc),
                recycler: null);                                  // 未 AlignLocal/未 Tick：LocalEntityId=0、HasLocalDisplay=false
            var (plane, reticle) = BuildCanvasRects();
            var (hip, ads) = BuildFormObjects(reticle);

            var cursor = new List<bool>();
            var driver = new BattleCrosshairDriver(view, null, plane, reticle, hip, ads,
                screenPosition: () => Vector2.zero, setCursorVisible: cursor.Add);
            driver.Tick();
            Assert.IsFalse(reticle.gameObject.activeSelf, "本地表现未建 → 整体隐藏");
            Assert.AreEqual(true, cursor[cursor.Count - 1], "未建局 → 光标可见");

            driver.Dispose();
        }
    }
}
