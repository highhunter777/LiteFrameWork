using LiteFramework;
using LiteSim;
using UnityEngine;

namespace LiteView
{
    /// <summary>
    /// 本地玩家表现态：跟预测态 0 延迟，和解后指数衰减；复活/传送/异常单帧跳变语义硬切。
    /// 拥有本地显示位、Yaw、生命基线、CC 代理与复活/瞬移计数。
    /// </summary>
    internal sealed class LocalReconciler
    {
        private const float TeleportSqrDistance = 4f;   // 2m：兜底异常单帧跳变（语义硬切不看距离）

        private SimVector3 _display;
        private float _yaw;
        private bool _hasDisplay;
        private int _lifeStart;
        private bool _hasLife;
        private CharacterController _cc;

        public int RespawnSerial { get; private set; }

        public int TeleportCount { get; private set; }

        public bool HasDisplay => _hasDisplay;

        public SimVector3 Display => _display;

        public float Yaw => _yaw;

        /// <summary>换实体/重连：重新落位（不从上一条命的位置飞过去），生命基线随实体重取。</summary>
        public void Align()
        {
            _hasDisplay = false;
            _hasLife = false;
            _cc = null;
        }

        public LocalStep Step(SimWorldState sim, long localEntityId, EntityViewMap views,
            float snapDistance, float sharpness, float dt)
        {
            if (localEntityId == 0) return Snapshot(hasDisplay: _hasDisplay, cameraReset: false);
            if (!sim.TryResolve(localEntityId, out int slotIndex)) return Snapshot(hasDisplay: _hasDisplay, cameraReset: false);

            ref EntitySlot slot = ref sim.Entities[slotIndex];

            bool hardCut = false;
            int lifeStart = slot.LifeStartFrame;
            if (!_hasLife) { _hasLife = true; _lifeStart = lifeStart; }
            else if (lifeStart != _lifeStart)
            {
                _lifeStart = lifeStart;
                ResetLocalViewAfterRespawn(slotIndex, lifeStart, views);
                hardCut = true;
            }

            bool cameraReset = false;
            if (!_hasDisplay)
            {
                _display = slot.Pos;
                _yaw = slot.Yaw;
                _hasDisplay = true;
                cameraReset = true;
            }
            else if (hardCut || ViewTransformMath.ShouldSnap(_display, slot.Pos, snapDistance))
            {
                _display = slot.Pos;
                _yaw = slot.Yaw;
                hardCut = true;
                cameraReset = true;
            }
            else
            {
                _display = ViewTransformMath.Decay(_display, slot.Pos, sharpness, dt);
                _yaw = ViewTransformMath.YawDecay(_yaw, slot.Yaw, sharpness, dt);
            }

            if (views.TryGet(slotIndex, out var view))
                PlaceLocal(view, dt, hardCut);

            return Snapshot(hasDisplay: _hasDisplay, cameraReset: cameraReset);
        }

        private LocalStep Snapshot(bool hasDisplay, bool cameraReset)
            => new LocalStep(hasDisplay, cameraReset, _yaw);

        /// <summary>复活时强制恢复本地视图可见性（渲染器/Animator 可能被死亡或池化路径留在旧姿态）。</summary>
        private void ResetLocalViewAfterRespawn(int slotIndex, int lifeStart, EntityViewMap views)
        {
            RespawnSerial++;
            if (!views.TryGet(slotIndex, out GameObject view) || view == null) return;
            if (!view.activeSelf) view.SetActive(true);

            var renderers = view.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].enabled = true;
                if (renderers[i] is SkinnedMeshRenderer skinned) skinned.updateWhenOffscreen = true;
            }

            var animators = view.GetComponentsInChildren<Animator>(true);
            for (int i = 0; i < animators.Length; i++)
                if (!animators[i].enabled) animators[i].enabled = true;

            int enabledCount = 0;
            for (int i = 0; i < renderers.Length; i++) if (renderers[i] != null && renderers[i].enabled) enabledCount++;
            Log.Info($"local-respawn life={lifeStart} view={view.name} renderers={renderers.Length}/{enabledCount}"
                + $" animators={animators.Length} culling={CullingOf(animators)}", LogTag.View);
        }

        private static string CullingOf(Animator[] animators)
            => animators != null && animators.Length > 0 && animators[0] != null
                ? animators[0].cullingMode.ToString()
                : "none";

        /// <summary>普通收敛走 CC.Move（贴地/防穿模）；硬切/瞬移关 CC 直写，避免被中途墙体拦住。</summary>
        private void PlaceLocal(GameObject view, float dt, bool hardCut)
        {
            CharacterController cc = EnsureLocalCc(view);
            if (cc == null)
            {
                SimViewMath.Place(view.transform, in _display, _yaw);
                return;
            }

            Vector3 target = new Vector3(_display.X, _display.Y, _display.Z);
            Vector3 delta = target - view.transform.position;
            if (hardCut || delta.sqrMagnitude > TeleportSqrDistance)
            {
                cc.enabled = false;
                view.transform.position = target;
                cc.enabled = true;
                TeleportCount++;
                Log.Info($"local-teleport delta={delta.magnitude:0.00}m hardCut={hardCut}"
                    + $" view=({target.x:0.0},{target.y:0.0},{target.z:0.0})", LogTag.View);
            }
            else if (delta.sqrMagnitude > 0.0000001f)
            {
                cc.Move(delta);
            }
            view.transform.rotation = SimViewMath.FacingRotation(_yaw);
        }

        private CharacterController EnsureLocalCc(GameObject view)
        {
            if (_cc != null && !ReferenceEquals(_cc, null)) return _cc;

            var cc = view.GetComponent<CharacterController>();
            if (cc != null && !cc.enabled) cc.enabled = true;
            _cc = cc;
            return cc;
        }
    }

    internal readonly struct LocalStep
    {
        public readonly bool HasDisplay;
        public readonly bool CameraReset;
        public readonly float Yaw;

        public LocalStep(bool hasDisplay, bool cameraReset, float yaw)
        {
            HasDisplay = hasDisplay;
            CameraReset = cameraReset;
            Yaw = yaw;
        }
    }
}
