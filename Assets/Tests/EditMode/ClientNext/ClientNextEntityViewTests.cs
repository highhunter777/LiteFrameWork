using LiteGameNext.Features.Battle.World;
using LiteNet.Proto;
using NUnit.Framework;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>角色实体视图与受击反馈：工厂优先角色、缺资源回退立方体、HP 下降触发泛红。</summary>
    public sealed class ClientNextEntityViewTests
    {
        [Test]
        public void 角色工厂装载真实prefab_带受击反馈组件()
        {
            var content = new LiteGameNext.Platform.Content.NextContentService();
            var factory = new PrefabEntityViewFactory(
                name => content.InstantiateSync(
                    "Assets/LiteGame/Modules/Battle/Content/Characters/" + name + ".prefab"));
            GameObject view = factory.Create(10, isLocal: true);
            try
            {
                Assert.IsNotNull(view, "真实角色 prefab 应能装载");
                Assert.IsNotNull(view.GetComponent<HitFlashTint>(), "角色视图应挂受击反馈");
            }
            finally
            {
                if (view != null) Object.DestroyImmediate(view);
            }
        }

        [Test]
        public void 工厂装载失败_回退立方体不抛()
        {
            var factory = new PrefabEntityViewFactory(_ => null);
            var go = new GameObject("W", typeof(BattleWorldView));
            try
            {
                var world = go.GetComponent<BattleWorldView>();
                world.SetEntityFactory(factory);
                Assert.DoesNotThrow(() => world.ApplySnapshot(new StateSnapshot
                {
                    Slots = { new SlotDelta { Id = 10, PosX = 0f, PosY = 0f, PosZ = 0f } },
                    PrivateState = new PrivateStateDelta { EntityId = 10 },
                }));
                Assert.AreEqual(1, go.transform.childCount, "回退立方体仍应建出实体视图");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void HP下降_触发受击泛红且不抛()
        {
            var go = new GameObject("Tint");
            try
            {
                var tint = go.AddComponent<HitFlashTint>();
                tint.SetBaseColor(Color.green);
                tint.ObserveHp(100);
                Assert.DoesNotThrow(() => tint.ObserveHp(70));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
