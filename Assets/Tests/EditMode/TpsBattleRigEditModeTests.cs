using System.Collections.Generic;
using Cinemachine;
using LiteFramework;
using LiteView;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// BattleRig 场景的 **TPS 结构守卫**（越肩 rig 形态防回潮）：双机轨道无遗留输入源（视角唯一写者＝
    /// 相机服务）、POV 轴无输入源/不回正（视线由服务写）、双机带收臂件且忽略玩家自身碰撞、越肩近距形态
    /// （水平幅 ≤ 5m——斜视角 12m 高位回潮即红）、主相机机位调参件在场（俯仰弧可配）。
    /// **只断言结构不变量、不断言具体数值**——Inspector 调参（距离/肩偏/FOV/跟随/枢轴）自由，形态回退才红。
    /// **场景所有权**：已加载则复用、不重复 Open/Close——重复句柄会把测试进程卡进场景保存死锁。
    /// </summary>
    public sealed class TpsBattleRigEditModeTests
    {
        private const string RigScenePath = "Assets/Scenes/BattleRig.unity";

        private readonly List<CinemachineVirtualCamera> _vcams = new List<CinemachineVirtualCamera>();
        private Scene _rigScene;
        private bool _openedHere;

        [SetUp]
        public void SetUp()
        {
            _rigScene = SceneManager.GetSceneByPath(RigScenePath);
            _openedHere = !_rigScene.IsValid();
            if (_openedHere) _rigScene = EditorSceneManager.OpenScene(RigScenePath, OpenSceneMode.Additive);

            _vcams.Clear();
            foreach (CinemachineVirtualCamera vcam in Object.FindObjectsByType<CinemachineVirtualCamera>(
                         FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (vcam.gameObject.scene == _rigScene) _vcams.Add(vcam);
            Assert.AreEqual(2, _vcams.Count, "BattleRig 应恰有双机（Player/Aim）——多机即装配漂移");
        }

        [TearDown]
        public void TearDown()
        {
            if (_openedHere && _rigScene.IsValid()) EditorSceneManager.CloseScene(_rigScene, true);
            _openedHere = false;
            _vcams.Clear();
        }

        [Test]
        public void 越肩rig_双机轨道无输入源且不回正()
        {
            foreach (var vcam in _vcams)
            {
                var orbital = vcam.GetCinemachineComponent<CinemachineOrbitalTransposer>();
                Assert.IsNotNull(orbital, $"{vcam.name} 应为 OrbitalTransposer（视角派生的轨道面）");
                Assert.AreEqual(string.Empty, orbital.m_XAxis.m_InputAxisName,
                    $"{vcam.name} 轨道不得挂输入源——视角唯一写者是相机服务（AddLookDelta）");
                Assert.IsFalse(orbital.m_RecenterToTargetHeading.m_enabled,
                    $"{vcam.name} 轨道不得自动回正——回正会与自持视角态打架");
            }
        }

        [Test]
        public void 越肩rig_双机POV视线_非Composer()
        {
            foreach (var vcam in _vcams)
            {
                Assert.IsNull(vcam.GetCinemachineComponent<CinemachineComposer>(),
                    $"{vcam.name} 不得用 Composer（固定视点会令 yaw 不跟手）——TPS 用 POV 由服务写轴");
                Assert.IsNull(vcam.GetCinemachineComponent<CinemachineHardLookAt>(),
                    $"{vcam.name} 不得用 HardLookAt（瞄根=脚 ⇒ 屏心朝下）");
                var pov = vcam.GetCinemachineComponent<CinemachinePOV>();
                Assert.IsNotNull(pov, $"{vcam.name} 应有 CinemachinePOV（视线 1:1 跟手）");
                Assert.AreEqual(string.Empty, pov.m_HorizontalAxis.m_InputAxisName,
                    $"{vcam.name} POV 水平轴不得挂输入源——唯一写者是相机服务");
                Assert.AreEqual(string.Empty, pov.m_VerticalAxis.m_InputAxisName,
                    $"{vcam.name} POV 垂直轴不得挂输入源——唯一写者是相机服务");
                Assert.IsFalse(pov.m_HorizontalRecentering.m_enabled,
                    $"{vcam.name} POV 水平轴不得回正——回正会与自持视角态打架");
                Assert.IsFalse(pov.m_VerticalRecentering.m_enabled,
                    $"{vcam.name} POV 垂直轴不得回正");
                Assert.LessOrEqual(pov.m_VerticalAxis.m_MinValue, -70f,
                    $"{vcam.name} 俯仰下限应覆盖 PUBG 量级抬头（≤ -70°）");
                Assert.GreaterOrEqual(pov.m_VerticalAxis.m_MaxValue, 70f,
                    $"{vcam.name} 俯仰上限应覆盖 PUBG 量级低头（≥ 70°）");

                // 机位调参只归主相机：ADS 位置由主视线+前移派生，瞄准机再挂一份就是死配置。
                if (vcam.name == "Player Camera")
                {
                    var rig = vcam.GetComponent<TpsCameraRigTuning>();
                    Assert.IsNotNull(rig, "Player 机应挂 TpsCameraRigTuning（俯仰弧场景单源）");
                    Assert.Greater(rig.PitchFollow, 0f,
                        "俯仰跟随系数应 > 0（否则角色完全不跟俯仰、相机臂不旋）");
                    Assert.LessOrEqual(rig.PitchFollow, 1f, "俯仰跟随系数不得超过 1（否则臂长弧越界）");
                    Assert.Greater(rig.PivotHeight, 0f, "俯仰枢轴高度应 > 0（臂长弧的旋转中心）");
                    Assert.Greater(rig.MinBoomHeight, 0f, "相机地面下限应 > 0（防穿地）");
                }
                else
                {
                    Assert.IsNull(vcam.GetComponent<TpsCameraRigTuning>(),
                        "Aim 机不挂机位调参——ADS 位置由主视线前移派生");
                }
            }
        }

        [Test]
        public void 越肩rig_双机带收臂件且近肩距离()
        {
            foreach (var vcam in _vcams)
            {
                var collider = vcam.GetComponent<CinemachineCollider>();
                Assert.IsNotNull(collider, $"{vcam.name} 应带 CinemachineCollider（贴墙收臂）");
                Assert.AreEqual("Player", collider.m_IgnoreTag,
                    $"{vcam.name} 相机碰撞必须忽略玩家自身胶囊——否则往地看会自激抖动");
                var orbital = vcam.GetCinemachineComponent<CinemachineOrbitalTransposer>();
                Vector3 o = orbital.m_FollowOffset;
                float horizontal = Mathf.Sqrt(o.x * o.x + o.z * o.z);
                Assert.LessOrEqual(horizontal, 5f,
                    $"{vcam.name} 水平距离应近肩（≤5m）——斜视角远距（h≈6m/12m 高）回潮即红");
                Assert.Less(o.y, 3f, $"{vcam.name} 高度应近头位（<3m）——俯视高位回潮即红");
            }
        }

        [Test]
        public void 越肩rig_玩家prefab带Player标记_供相机忽略自身()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SimView.DefaultEntityPrefab);
            Assert.IsNotNull(prefab, $"玩家 prefab 缺失：{SimView.DefaultEntityPrefab}");
            Assert.AreEqual("Player", prefab.tag,
                "玩家根必须带 Player tag——CinemachineCollider.m_IgnoreTag 靠它忽略自身胶囊");
        }
    }
}
