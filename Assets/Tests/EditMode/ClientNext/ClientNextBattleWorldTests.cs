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

        // C2 回归卡（S0 先红）：真实 BattleWorld.prefab 的相机被固定在
        // LocalPosition+(0,10,-12) 且 LookAt(LocalPosition+up*1.2)，屏幕中心射线与
        // y=1.2 平面交点恒等于自身位置——瞄准点即自己脚下，真实游玩命中率确定性为 0。
        // S2 搬入正式相机/瞄准链后本用例应转绿；转绿前不得声称「核心流程可玩」。
        [Test]
        [Explicit("C2 缺陷回归卡：S0 预期红，S2 修好相机/瞄准后转绿")]
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
                // 显式距离阈值：NUnit 对 Vector3 的 AreNotEqual 走 Unity == 容差，
                // 会把 ~1e-5 的退化差值误判为「不等」，必须用可读的几何距离。
                float aimDistance = Vector3.Distance(view.AimPoint, ownFeet);
                Assert.Greater(aimDistance, 0.5f,
                    "瞄准点贴近自身脚下位置（距离 " + aimDistance.ToString("F4") +
                    "）——相机/瞄准链退化（C2），真实游玩无法命中他人");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
