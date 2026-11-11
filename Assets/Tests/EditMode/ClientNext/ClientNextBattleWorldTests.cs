using LiteGameNext.Features.Battle.World;
using LiteNet.Proto;
using NUnit.Framework;
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
    }
}
