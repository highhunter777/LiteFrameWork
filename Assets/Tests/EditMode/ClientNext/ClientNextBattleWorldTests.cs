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

        /// <summary>C2 回归卡：须注入真实角色 prefab（带 CharacterController）复现生产碰撞布局；
        /// 枢轴 PivotHeight=1.5 在胶囊体（高 1.8/中心 0.9）内，无自身排除则射线先命中自己。</summary>
        [Test]
        [Category("ClientNextE2E")]
        [Explicit("C2 回归卡：需真实角色 prefab；验收时显式跑")]
        public void 真实prefab_瞄准点不得命中自身()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/LiteGame/Next/Runtime/Features/Battle/Content/BattleWorld.prefab");
            Assert.IsNotNull(prefab, "BattleWorld.prefab 应存在");

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var content = new LiteGameNext.Platform.Content.NextContentService();
            try
            {
                var view = go.GetComponent<BattleWorldView>();
                Assert.IsNotNull(view, "prefab 根应挂 BattleWorldView");
                // 关键：注入真实角色工厂，使实体带真实 CharacterController 碰撞体。
                view.SetEntityFactory(new PrefabEntityViewFactory(
                    name => content.InstantiateSync(
                        "Assets/LiteGame/Modules/Battle/Content/Characters/" + name + ".prefab")));

                var snapshot = new StateSnapshot
                {
                    Slots =
                    {
                        new SlotDelta { Id = 10, PosX = 1f, PosY = 0f, PosZ = 2f },
                        new SlotDelta { Id = 11, PosX = 1f, PosY = 0f, PosZ = 12f },
                    },
                    PrivateState = new PrivateStateDelta { EntityId = 10 },
                };
                view.ApplySnapshot(snapshot);
                view.Look(0f, 0f);                       // 默认 pitch 18°
                view.ApplySnapshot(snapshot);

                // 枪口高度 1.2948（CombatGeometry）—— 瞄准点作为射击基准须远离它。
                Vector3 muzzle = view.LocalPosition + new Vector3(0f, 1.2948f, 0f);
                float aimDistance = Vector3.Distance(view.AimPoint, muzzle);
                Assert.Greater(aimDistance, 3f,
                    "瞄准点距枪口仅 " + aimDistance.ToString("F4") +
                    "m——射线命中了自身碰撞体（枢轴在胶囊体内且未排除自身），真实游玩无法命中他人");
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
