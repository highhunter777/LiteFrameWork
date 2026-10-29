using LiteFramework;
using LiteSim;
using LiteTesting;
using LiteTesting.Unity;
using NUnit.Framework;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 采集侧瞄准解算的 TPS 契约（第三人称屏心射线）：AimPoint 由相机**屏心射线**解算（不依赖
    /// 鼠标屏幕位）；射线无地面交（瞄天）与地面交点超程都落到**射程截断点**——与服务器
    /// InputGate 距离闸（HitscanRange×1.1）口径兼容（截断后 |P−射手| 恒在闸内），
    /// 消灭"瞄天开火 = 无点非法帧 / 超程整帧丢弃（含 Move）"两类丢帧。
    /// </summary>
    public sealed class TpsAimPointEditModeTests : UnityTestBase
    {
        /// <summary>共享 Action 资产实例：源不持所有权 ⇒ Dispose 只 Disable 不销毁内嵌 asset——
        /// 绕开生成类 <c>PlayerInputActions.Dispose()</c> 的 <c>Object.Destroy</c> 在 EditMode 的报错
        /// （生成物不可手改，已按格式登记审计）。</summary>
        private static PlayerInputActions _sharedActions;

        private Camera _camera;
        private NewInputIntentSource _source;

        [SetUp]
        public void SetUp()
        {
            _camera = Scope.CreateGameObject("AimCam", typeof(Camera)).GetComponent<Camera>();
            _camera.fieldOfView = 60f;                       // 竖直 FOV 钉住——用例几何推导的前提
            if (_sharedActions == null) _sharedActions = new PlayerInputActions();
            _source = new NewInputIntentSource(_sharedActions, _camera);
        }

        [TearDown]
        public void TearDown()
        {
            _source?.Dispose();
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 屏心射线解算_瞄准点落屏心射线与地面交点()
        {
            _camera.transform.position = new Vector3(5f, 10f, 7f);
            _camera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);   // 正下视：屏心射线打 (5,0,7)

            _source.Sample(new SimVector3(5f, 0f, 7f));
            bool has = _source.TryGetAimPoint(out Vector3 point);

            Assert.IsTrue(has, "屏心射线必交地面 ⇒ 恒有点");
            Assert.AreEqual(5f, point.x, 0.01f, "屏心交点 X（不随鼠标位漂移）");
            Assert.AreEqual(0f, point.y, 0.01f, "地面点 Y=0");
            Assert.AreEqual(7f, point.z, 0.01f, "屏心交点 Z");
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 瞄天_无地面交_射程截断点有效且距离闸内()
        {
            _camera.transform.position = new Vector3(0f, 1.6f, 0f);
            _camera.transform.rotation = Quaternion.Euler(-30f, 0f, 0f);  // 上仰 30°：竖直 FOV60 ⇒ 全屏射线水平或朝上
            var shooter = new SimVector3(0f, 0f, 0f);

            _source.Sample(shooter);
            bool has = _source.TryGetAimPoint(out Vector3 point);

            Assert.IsTrue(has, "瞄天也要有射程截断点（开火帧恒有有效点——无点开火在服务器是非法帧）");
            Assert.LessOrEqual(Vector3.Distance(point, ToWorld(shooter)), CombatConfig.HitscanRange * 1.1f,
                "截断点在服务器距离闸内（HitscanRange×1.1）");
            Assert.Greater(point.y, _camera.transform.position.y, "截断点沿上行射线（瞄天方向保留——所见即所打）");
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 远地超程_地面交点截断到距离闸内()
        {
            _camera.transform.position = new Vector3(0f, 1.7f, 0f);
            _camera.transform.rotation = Quaternion.Euler(0.5f, 0f, 0f);  // 近肩浅俯 0.5°：屏心射线地面交点 ≈ 195m——超程；相机在射程球内（真实 rig 相机距射手 ~3.8m 恒在球内——截断几何前提）
            var shooter = new SimVector3(0f, 0f, 0f);

            _source.Sample(shooter);
            bool has = _source.TryGetAimPoint(out Vector3 point);

            Assert.IsTrue(has, "超程地面点也要有截断值（不产生无点帧）");
            Assert.LessOrEqual(Vector3.Distance(point, ToWorld(shooter)), CombatConfig.HitscanRange * 1.1f,
                "截断到服务器距离闸内（消灭 InputGate 超程整帧丢弃——丢的是含 Move 的整帧）");
        }

        private static Vector3 ToWorld(in SimVector3 p) => new Vector3(p.X, p.Y, p.Z);
    }
}
