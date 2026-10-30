using LiteSim;
using UnityEngine;

namespace LiteView
{
    /// <summary>
    /// 槽位视图生命周期与远端落位：活体建视图、死亡回收、远端按快照插值写 Transform。
    /// 槽位寻址由 <see cref="EntityViewMap"/> 负责；本类只管“何时建/回收、写到哪里”。
    /// </summary>
    internal sealed class EntityViewBinder
    {
        private readonly EntityViewMap _views;

        public EntityViewBinder(EntityViewMap views)
        {
            _views = views;
        }

        public int Count => _views.Count;

        public bool TryGet(int slotIndex, out GameObject view) => _views.TryGet(slotIndex, out view);

        public void Sync(SimWorldState sim, long localEntityId)
        {
            for (int i = 0; i < SimConfig.MaxEntities; i++)
            {
                bool alive = sim.IsAlive(i);
                bool hasView = _views.TryGet(i, out _);

                if (alive && !hasView)
                {
                    ref EntitySlot slot = ref sim.Entities[i];
                    var view = _views.Create(i, slot);
                    if (view != null)
                    {
                        SimViewMath.Place(view.transform, in slot.Pos, slot.Yaw);
                        // 远端实体禁 CC：实体间碰撞 Sim 未建模，实心胶囊会挡本地 CC 造成表现/权威分叉。
                        var cc = view.GetComponent<CharacterController>();
                        if (cc != null && slot.Id != localEntityId) cc.enabled = false;
                    }
                }
                else if (!alive && hasView)
                {
                    _views.Release(i);
                }
            }
        }

        public void UpdateRemote(SimWorldState sim, long localEntityId,
            SnapshotInterpolationBuffer snapshots, float snapDistance)
        {
            for (int i = 0; i < SimConfig.MaxEntities; i++)
            {
                if (!_views.TryGet(i, out var view)) continue;

                ref EntitySlot slot = ref sim.Entities[i];
                if (slot.Id != 0 && slot.Id == localEntityId) continue;

                if (!snapshots.TryInterpolate(slot.Id, snapDistance, out var pos, out float yaw))
                {
                    pos = slot.Pos;
                    yaw = slot.Yaw;
                }
                SimViewMath.Place(view.transform, in pos, yaw);
            }
        }
    }
}
