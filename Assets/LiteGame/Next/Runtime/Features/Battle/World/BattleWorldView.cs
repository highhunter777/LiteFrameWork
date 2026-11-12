using System.Collections.Generic;
using LiteNet.Proto;
using UnityEngine;

namespace LiteGameNext.Features.Battle.World
{
    /// <summary>最小战斗世界视图：按快照槽位渲染立方体 + 跟随相机；只读快照，不写判定。</summary>
    public sealed class BattleWorldView : MonoBehaviour
    {
        public const string PrefabPath =
            "Assets/LiteGame/Next/Runtime/Features/Battle/Content/BattleWorld.prefab";

        [SerializeField] private Camera _camera;

        private readonly Dictionary<long, GameObject> _views = new Dictionary<long, GameObject>();
        private readonly BattleCameraRig _cameraRig = new BattleCameraRig();
        private Transform _entities;

        public Vector3 LocalPosition { get; private set; }

        public long LocalEntityId { get; private set; }

        /// <summary>视角旋转（由输入层每帧喂入鼠标增量）。</summary>
        public void Look(float deltaX, float deltaY) => _cameraRig.Look(deltaX, deltaY);

        /// <summary>瞄准点：有相机走中心射线（可命中他人）；无相机给前向回退点（保持可射击）。</summary>
        public Vector3 AimPoint => _camera != null
            ? BattleCameraRig.ResolveAimPoint(_camera)
            : LocalPosition + new Vector3(0f, 1.2f, 10f);

        public void ApplySnapshot(StateSnapshot snapshot)
        {
            if (snapshot == null) return;
            if (_camera == null) _camera = GetComponentInChildren<Camera>();
            if (_entities == null) _entities = transform;

            LocalEntityId = snapshot.PrivateState == null ? LocalEntityId : snapshot.PrivateState.EntityId;
            for (int index = 0; index < snapshot.Slots.Count; index++)
            {
                SlotDelta slot = snapshot.Slots[index];
                if (slot.Id == 0) continue;
                GameObject view = GetOrCreate(slot.Id);
                view.transform.position = new Vector3(slot.PosX, slot.PosY, slot.PosZ);
                view.transform.rotation = Quaternion.Euler(0f, slot.Yaw, 0f);
                var block = new MaterialPropertyBlock();
                block.SetColor("_Color", slot.Id == LocalEntityId
                    ? new Color(0.2f, 0.9f, 0.4f)
                    : new Color(0.9f, 0.3f, 0.3f));
                view.GetComponent<Renderer>().SetPropertyBlock(block);
                if (slot.Id == LocalEntityId) LocalPosition = view.transform.position;
            }

            if (_camera != null && _views.ContainsKey(LocalEntityId))
                _cameraRig.Apply(_camera, LocalPosition);
        }

        private GameObject GetOrCreate(long entityId)
        {
            if (_views.TryGetValue(entityId, out GameObject view)) return view;
            view = GameObject.CreatePrimitive(PrimitiveType.Cube);
            view.name = "Entity_" + entityId;
            view.transform.SetParent(_entities, false);
            _views[entityId] = view;
            return view;
        }
    }
}
