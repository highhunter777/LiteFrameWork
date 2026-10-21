using LiteSim;
using LiteTesting;
using LiteTesting.Unity;
using LiteView;
using NUnit.Framework;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// TPS 准心位置契约：准心恒钉**屏心**（画布中心系原点），不再跟随鼠标屏幕位——
    /// 指针锁定形态下鼠标只驱动相机（AddLookDelta），"屏幕光标位"语义整体退役。
    /// 散布/后坐（随命中反馈并入 localPosition）不在此面——既有 BattleCrosshairEditModeTests 覆盖。
    /// </summary>
    public sealed class TpsCrosshairCenterEditModeTests : UnityTestBase
    {
        [Test]
        [Category(TestCategory.Contract)]
        public void 准心_位置恒屏心()
        {
            var world = new SimWorldState { RngState = 1UL };
            long selfId = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f), Yaw = 0f }, out _);
            SimView view = BuildLocalView(world, selfId);
            var (plane, reticle) = BuildCanvasRects();
            var (hip, ads) = BuildFormObjects(reticle);

            var driver = new BattleCrosshairDriver(view, null, plane, reticle, hip, ads);   // 位置恒屏心——无位置注入位（TPS 形态）

            driver.Tick(0f);
            Assert.AreEqual(0f, reticle.localPosition.x, 1e-3f, "准心 X 恒屏心（画布中心系原点）");
            Assert.AreEqual(0f, reticle.localPosition.y, 1e-3f, "准心 Y 恒屏心——后坐上跳并位以屏心为基");

            driver.Dispose();
        }

        // ---- 底座（与 BattleCrosshairEditModeTests 同形态：灰盒视图 + 画布几何）----

        private SimView BuildLocalView(SimWorldState world, long selfId)
        {
            SimView view = new SimView(world, null,
                factory: (loc, parent) => Scope.CreateGameObject(loc),
                recycler: null);
            view.AlignLocal(selfId);
            view.Tick(1f / 60f);                        // 首帧落位：HasLocalDisplay 置真
            return view;
        }

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
    }
}
