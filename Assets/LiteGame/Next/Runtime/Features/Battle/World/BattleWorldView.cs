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
        private readonly Dictionary<long, CharacterAnimPlayer> _anims =
            new Dictionary<long, CharacterAnimPlayer>();
        private readonly Dictionary<long, Vector3> _lastPositions = new Dictionary<long, Vector3>();
        private System.Collections.Generic.IReadOnlyDictionary<string, LiteGameNext.Platform.Config.AnimProfile> _animProfiles;
        private readonly BattleCameraRig _cameraRig = new BattleCameraRig();
        private readonly SnapshotInterpolationBuffer _interp = new SnapshotInterpolationBuffer();
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
                var position = new Vector3(slot.PosX, slot.PosY, slot.PosZ);
                bool isLocal = slot.Id == LocalEntityId;

                if (isLocal)
                {
                    // 本地玩家 0 延迟：直接落位。
                    view.transform.position = position;
                    view.transform.rotation = Quaternion.Euler(0f, slot.Yaw, 0f);
                    LocalPosition = position;
                    _localRoot = view.transform;
                }
                else
                {
                    // 远端：入插值缓冲，由 Tick 平滑推进。
                    _interp.Push(slot.Id, new PoseSample(snapshot.Frame, position, slot.Yaw,
                        (int)slot.Id * 31 + (slot.Hp > 0 ? 1 : 0)));
                }

                var flash = view.GetComponent<HitFlashTint>();
                if (flash != null)
                {
                    // 角色视图：受击泛红由 HP 下降触发；delta 快照 Hp=0 表示「未变化」，跳过。
                    if (slot.Hp > 0) flash.ObserveHp(slot.Hp);
                }
                else
                {
                    var block = new MaterialPropertyBlock();
                    block.SetColor("_Color", isLocal
                        ? new Color(0.2f, 0.9f, 0.4f)
                        : new Color(0.9f, 0.3f, 0.3f));
                    Renderer renderer = view.GetComponent<Renderer>();
                    if (renderer != null) renderer.SetPropertyBlock(block);
                }
            }

            if (_camera != null && _views.ContainsKey(LocalEntityId))
                _cameraRig.Apply(_camera, LocalPosition);
        }

        /// <summary>注入动画档案表（`tbanimationprofile` 投影）；null = 用内置速度阈值。</summary>
        public void SetAnimProfiles(
            System.Collections.Generic.IReadOnlyDictionary<string, LiteGameNext.Platform.Config.AnimProfile> profiles)
            => _animProfiles = profiles;

        /// <summary>逐帧推进远端插值并驱动角色动画（由 ClientApp.Tick 驱动）。</summary>
        public void Tick(float deltaTime)
        {
            _interp.Advance(deltaTime);
            float now = Time.realtimeSinceStartup;
            foreach (KeyValuePair<long, GameObject> pair in _views)
            {
                if (pair.Key == LocalEntityId) continue;
                if (!_interp.TrySample(pair.Key, out Vector3 position, out float yaw)) continue;
                if (pair.Value == null) continue;
                pair.Value.transform.position = position;
                pair.Value.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                DriveAnimation(pair.Key, pair.Value, position, now, deltaTime);
            }
        }

        /// <summary>按位移速度驱动角色动画（瞄准/换弹/受击态当前不喂，留默认）。</summary>
        private void DriveAnimation(long entityId, GameObject view, Vector3 position, float now, float deltaTime)
        {
            if (!_anims.TryGetValue(entityId, out CharacterAnimPlayer anim) || anim == null)
            {
                Animator animator = view.GetComponentInChildren<Animator>(true);
                anim = new CharacterAnimPlayer(animator, _animProfiles);
                _anims[entityId] = anim;
            }

            if (!_lastPositions.TryGetValue(entityId, out Vector3 last))
            {
                _lastPositions[entityId] = position;
                return;
            }

            Vector3 delta = position - last;
            _lastPositions[entityId] = position;
            float speed = deltaTime > 0f ? delta.magnitude / deltaTime : 0f;
            anim.Apply(new CharacterAnimFacts(false, false, false, speed, delta, -999f, now));
        }

        /// <summary>换局/重连清空插值缓冲与动画缓存。</summary>
        public void ResetInterpolation()
        {
            _interp.Reset();
            _anims.Clear();
            _lastPositions.Clear();
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
