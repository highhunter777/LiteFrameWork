using System.Collections.Generic;
using Cinemachine;
using LiteClient;
using NUnit.Framework;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>团队视角镜像（<see cref="ICameraService.SetViewMirrored"/>）的适配器验收：
    /// 只翻转场景 FollowOffset 的 x/z（180° 世界系旋转），关闭/解绑还原场景基准；FFA 默认不镜像。</summary>
    public sealed class CameraViewMirrorEditModeTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();
        private CinemachineCameraService _service;
        private CinemachineTransposer _mainTransposer;
        private CinemachineTransposer _aimTransposer;

        [SetUp]
        public void SetUp()
        {
            _mainTransposer = NewVcam("Player Camera", 10, new Vector3(1.5f, 12f, -6f), hardLookAt: true);
            _aimTransposer = NewVcam("Aim Camera", 5, new Vector3(0f, 12f, -6f), hardLookAt: false);
            _service = new CinemachineCameraService();
        }

        [TearDown]
        public void TearDown()
        {
            _service?.Shutdown();
            _service = null;
            for (int i = 0; i < _objects.Count; i++)
            {
                if (_objects[i] != null) Object.DestroyImmediate(_objects[i]);
            }
            _objects.Clear();
        }

        [Test]
        public void 镜像_主瞄准两台相机offset做180度变换_关闭还原()
        {
            Assert.AreEqual(new Vector3(1.5f, 12f, -6f), _mainTransposer.m_FollowOffset, "主相机初始为场景原朝向");
            Assert.AreEqual(new Vector3(0f, 12f, -6f), _aimTransposer.m_FollowOffset, "瞄准机初始为场景原朝向");

            _service.SetViewMirrored(true);
            Assert.AreEqual(new Vector3(-1.5f, 12f, 6f), _mainTransposer.m_FollowOffset, "主相机 x/z 取反、y 不变");
            Assert.AreEqual(new Vector3(0f, 12f, 6f), _aimTransposer.m_FollowOffset, "瞄准机同款镜像");

            _service.SetViewMirrored(true);
            Assert.AreEqual(new Vector3(-1.5f, 12f, 6f), _mainTransposer.m_FollowOffset, "重复开启不叠加");

            _service.SetViewMirrored(false);
            Assert.AreEqual(new Vector3(1.5f, 12f, -6f), _mainTransposer.m_FollowOffset, "关闭还原主相机");
            Assert.AreEqual(new Vector3(0f, 12f, -6f), _aimTransposer.m_FollowOffset, "关闭还原瞄准机");
        }

        [Test]
        public void 关闭服务_归还场景基准_且关闭后不再写入()
        {
            _service.SetViewMirrored(true);
            _service.Shutdown();

            Assert.AreEqual(new Vector3(1.5f, 12f, -6f), _mainTransposer.m_FollowOffset, "Shutdown 归还主相机场景值");
            Assert.AreEqual(new Vector3(0f, 12f, -6f), _aimTransposer.m_FollowOffset, "Shutdown 归还瞄准机场景值");

            _service.SetViewMirrored(true);   // 已关闭：按服务现有契约静默 no-op
            Assert.AreEqual(new Vector3(1.5f, 12f, -6f), _mainTransposer.m_FollowOffset, "关闭后不再写相机");
            Assert.AreEqual(new Vector3(0f, 12f, -6f), _aimTransposer.m_FollowOffset, "关闭后不再写瞄准机");
        }

        private CinemachineTransposer NewVcam(string name, int priority, Vector3 offset, bool hardLookAt)
        {
            var go = new GameObject(name);
            _objects.Add(go);
            var vcam = go.AddComponent<CinemachineVirtualCamera>();
            vcam.Priority = priority;
            var transposer = vcam.AddCinemachineComponent<CinemachineOrbitalTransposer>();
            transposer.m_FollowOffset = offset;
            if (hardLookAt) vcam.AddCinemachineComponent<CinemachineHardLookAt>();
            else vcam.AddCinemachineComponent<CinemachineComposer>();
            return transposer;
        }
    }
}
