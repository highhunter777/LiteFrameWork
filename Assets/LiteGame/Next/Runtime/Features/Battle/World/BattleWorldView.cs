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
        private IBattleEntityViewFactory _entityFactory;
        private Transform _entities;
        private Transform _localRoot;

        /// <summary>注入实体视图工厂（角色 prefab）；未注入或工厂回退时用调试立方体。</summary>
        public void SetEntityFactory(IBattleEntityViewFactory factory) => _entityFactory = factory;

        public Vector3 LocalPosition { get; private set; }

        public long LocalEntityId { get; private set; }

        /// <summary>视角旋转（由输入层每帧喂入鼠标增量）。</summary>
        public void Look(float deltaX, float deltaY) => _cameraRig.Look(deltaX, deltaY);

        /// <summary>瞄准点：有相机走中心射线（排除本地自身碰撞体）；无相机给前向回退点。</summary>
        public Vector3 AimPoint => _camera != null
            ? BattleCameraRig.ResolveAimPoint(_camera, _localRoot)
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
                var flash = view.GetComponent<HitFlashTint>();
                if (flash != null)
                {
                    // 角色视图：受击泛红由 HP 下降触发；delta 快照 Hp=0 表示「未变化」，跳过。
                    if (slot.Hp > 0) flash.ObserveHp(slot.Hp);
                }
                else
                {
                    var block = new MaterialPropertyBlock();
                    block.SetColor("_Color", slot.Id == LocalEntityId
                        ? new Color(0.2f, 0.9f, 0.4f)
                        : new Color(0.9f, 0.3f, 0.3f));
                    Renderer renderer = view.GetComponent<Renderer>();
                    if (renderer != null) renderer.SetPropertyBlock(block);
                }
                if (slot.Id == LocalEntityId)
                {
                    LocalPosition = view.transform.position;
                    _localRoot = view.transform;
                }
            }

            if (_camera != null && _views.ContainsKey(LocalEntityId))
                _cameraRig.Apply(_camera, LocalPosition);
        }

        private GameObject GetOrCreate(long entityId)
        {
            if (_views.TryGetValue(entityId, out GameObject view)) return view;
            bool isLocal = entityId == LocalEntityId;
            view = _entityFactory != null ? _entityFactory.Create(entityId, isLocal) : null;
            if (view == null)
            {
                view = GameObject.CreatePrimitive(PrimitiveType.Cube);
                view.name = "Entity_" + entityId;
            }
            view.transform.SetParent(_entities, false);
            _views[entityId] = view;
            return view;
        }
    }
}
