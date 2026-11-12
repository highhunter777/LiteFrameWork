using LiteGameNext.Features.Battle.World;
using LiteNet.Proto;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>最小世界视图：快照槽位 → 立方体位置/本地实体跟随；只读渲染。</summary>
    public sealed class ClientNextBattleWorldTests
    {
        [Test]
        public void 快照槽位_创建立方体并记录本地位置()
        {
            var go = new GameObject("WorldTest", typeof(BattleWorldView));
            try
            {
                var view = go.GetComponent<BattleWorldView>();
                var snapshot = new StateSnapshot
                {
                    Slots =
                    {
                        new SlotDelta { Id = 10, PosX = 1f, PosY = 0f, PosZ = 2f, Yaw = 90f },
                        new SlotDelta { Id = 11, PosX = -1f, PosY = 0f, PosZ = -2f },
                    },
                    PrivateState = new PrivateStateDelta { EntityId = 10 },
                };

                view.ApplySnapshot(snapshot);

                Assert.AreEqual(new Vector3(1f, 0f, 2f), view.LocalPosition);
                Assert.AreEqual(10, view.LocalEntityId);
                Assert.AreEqual(2, go.transform.childCount - 0, "两个实体立方体应挂在根下");
                Assert.AreNotEqual(Vector3.zero, view.AimPoint, "无相机时也应给前方瞄准点");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void 空快照_不抛异常()
        {
            var go = new GameObject("WorldTest", typeof(BattleWorldView));
            try
            {
                Assert.DoesNotThrow(() => go.GetComponent<BattleWorldView>().ApplySnapshot(null));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        // C2 回归卡：S0 时瞄准点恒等于自身脚下（距离 1.35e-5），真实游玩命中率为 0；
        // S2 引入 BattleCameraRig（第三人称臂长 + 中心射线瞄准）后转绿。
        // 保留为长期回归：瞄准点必须远离自身且在可射击距离内。
        [Test]
        [Explicit("C2 回归卡：需真实 prefab 相机；验收时显式跑")]
        public void 真实prefab_瞄准点不得等于自身脚下位置()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/LiteGame/Next/Runtime/Features/Battle/Content/BattleWorld.prefab");
            Assert.IsNotNull(prefab, "BattleWorld.prefab 应存在");

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                var view = go.GetComponent<BattleWorldView>();
                Assert.IsNotNull(view, "prefab 根应挂 BattleWorldView");

                var snapshot = new StateSnapshot
                {
                    Slots =
                    {
                        new SlotDelta { Id = 10, PosX = 1f, PosY = 0f, PosZ = 2f },
                        new SlotDelta { Id = 11, PosX = -1f, PosY = 0f, PosZ = -2f },
                    },
                    PrivateState = new PrivateStateDelta { EntityId = 10 },
                };
                view.ApplySnapshot(snapshot);

                Vector3 ownFeet = view.LocalPosition + new Vector3(0f, 1.2f, 0f);
                // NUnit 对 Vector3 的 AreNotEqual 走 Unity == 容差，须用几何距离断言。
                float aimDistance = Vector3.Distance(view.AimPoint, ownFeet);
                Assert.Greater(aimDistance, 0.5f,
                    "瞄准点贴近自身脚下位置（距离 " + aimDistance.ToString("F4") +
                    "）——相机/瞄准链退化（C2），真实游玩无法命中他人");
                Assert.Less(aimDistance, 200f, "瞄准点距离异常（应落在中心射线可及范围）");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void 视角旋转_改变相机朝向并带动瞄准点()
        {
            var go = new GameObject("WorldAim", typeof(BattleWorldView));
            var camGo = new GameObject("Cam");
            try
            {
                var cam = camGo.AddComponent<Camera>();
                camGo.transform.SetParent(go.transform, false);
                var view = go.GetComponent<BattleWorldView>();
                view.ApplySnapshot(new StateSnapshot
                {
                    Slots = { new SlotDelta { Id = 10, PosX = 0f, PosY = 0f, PosZ = 0f } },
                    PrivateState = new PrivateStateDelta { EntityId = 10 },
                });

                Vector3 before = view.AimPoint;
                view.Look(90f, 0f);
                view.ApplySnapshot(new StateSnapshot
                {
                    Slots = { new SlotDelta { Id = 10, PosX = 0f, PosY = 0f, PosZ = 0f } },
                    PrivateState = new PrivateStateDelta { EntityId = 10 },
                });
                Vector3 after = view.AimPoint;

                Assert.Greater(Vector3.Distance(before, after), 1f,
                    "视角旋转 90° 后瞄准点应明显改变（相机朝向驱动瞄准）");
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(camGo);
            }
        }
    }
}
