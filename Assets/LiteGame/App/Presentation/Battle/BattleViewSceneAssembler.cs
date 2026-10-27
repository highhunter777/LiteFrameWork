using System;
using LiteClient;
using LiteFramework;
using LiteFramework.Animation;
using LiteSim;
using LiteView;
using LiteView.Animation;
using UnityEngine;

namespace LiteGame
{
    /// <summary>
    /// 对局**视图本体装配器**：<see cref="SimView"/>（镜像/插值/静默门/相机服务）→ 移动动画驱动 →
    /// 瞄准激光 → 出生保护壳。视图根与 prefab 归调用方（离场由 rig 销毁视图根）；
    /// 驱动实例归本件，按相位拆除（移动动画先于视图本体；激光/保护壳随视图根）。
    /// </summary>
    internal sealed class BattleViewSceneAssembler
    {
        private Transform _viewRoot;
        private GameObject _prefab;
        private AssetLease<GameObject> _protectionShellLease;

        public SimView View { get; private set; }
        public CharacterLocomotionDriver Locomotion { get; private set; }
        public BattleLaserDriver Laser { get; private set; }
        public BattleProtectionFxDriver ProtectionFx { get; private set; }

        /// <summary>建视图 + 挂上下文 + 移动动画驱动（须在 StartGame 之后调用）。</summary>
        public void AttachCore(BattleContext context, Transform viewRoot, GameObject prefab,
            ICameraService camera, AnimationClipManifest manifest)
        {
            _viewRoot = viewRoot;
            _prefab = prefab;
            View = new SimView(context.Sim.State, viewRoot,
                factory: InstantiateView,
                recycler: null,                                   // 自有池（EntityService 接线归表现域批）
                camera: camera);
            context.AttachView(View);

            // 动画 Profile：表单源（读口行 → 按模型族装载——缺行/坏行在装配期显性失败）
            var animationProfile = AnimationProfileLoader.FromRows(
                AnimationProfileConfig.Rows, CombatGirlsAnimationProfile.ModelFamily);
            Locomotion = new CharacterLocomotionDriver(View, animationProfile,
                manifest: manifest,                               // 清单在 = 去 AC 主路径供片；缺清单回退控制器便利源
                cameraPitchOf: camera != null ? (Func<float>)(() => camera.LookPitch) : null);   // TPS 俯仰姿态源
        }

        /// <summary>挂瞄准激光（端点判定与显隐归驱动；本方法只装配）。</summary>
        public void AttachLaser(BattleContext context, IInputService input, GameObject laserBeam)
        {
            if (laserBeam == null) return;                        // 资产缺失已在取件处降级（警告过）
            if (context.Sim == null)
            {
                Log.Warning("Sim 未建——瞄准激光本局降级（Attach 早于 StartGame 的装配序）", LogTag.Battle);
                return;
            }
            var line = laserBeam.GetComponent<UnityEngine.LineRenderer>();
            if (line == null)
            {
                Log.Warning($"激光束 prefab 缺 LineRenderer——瞄准激光降级:{BattleViewAssets.LaserSightPrefab}", LogTag.Battle);
                return;
            }
            // 本局规则随装配注入（爆头带覆写随动；驱动不读 SimTestRules 静态读口）
            Laser = new BattleLaserDriver(View, input, context.Sim, context.Map, line, context.MatchRules);
            Log.Info("laser-attached", LogTag.Battle);
        }

        /// <summary>挂出生保护壳（保护态内按槽位跟随对应玩家视图显隐；壳件挂视图根，避免回池复用错位）。</summary>
        public void AttachProtectionFx(AssetLease<GameObject> shellLease)
        {
            if (shellLease == null) return;                       // 取件降级已警告
            _protectionShellLease = shellLease;
            ProtectionFx = new BattleProtectionFxDriver(
                View.IsProtected,
                ViewOf,
                slot =>
                {
                    GameObject shell = UnityEngine.Object.Instantiate(_protectionShellLease.Asset, _viewRoot);
                    shell.name = "SpawnProtection_" + slot;
                    shell.SetActive(false);
                    return shell;
                });
            Log.Info("protection-fx-attached", LogTag.Battle);
        }

        /// <summary>移动动画（视图位置已更新，再解析目标姿态）。</summary>
        public void TickMovement(float elapseSeconds) => Locomotion?.Tick(elapseSeconds);

        /// <summary>世界表现件：保护壳 → 瞄准激光（同帧顺序固定）。</summary>
        public void TickWorldFx()
        {
            ProtectionFx?.Tick();
            Laser?.Tick();
        }

        public void DisposeMovement()
        {
            Locomotion?.Dispose();               // 先停动画驱动（视图消费者），再拆视图本体
            Locomotion = null;
        }

        public void DisposeLaser()
        {
            Laser?.Dispose();                    // 束实例随 _viewRoot 销毁；租约随作用域归还
            Laser = null;
        }

        public void DisposeProtectionFx()
        {
            ProtectionFx?.Dispose();             // 壳实例随 _viewRoot 销毁、租约随作用域归还
            ProtectionFx = null;
            _protectionShellLease = null;
        }

        /// <summary>视图引用清空（视图根销毁前调用；租约引用归 assets 清）。</summary>
        public void Detach()
        {
            View = null;
            _prefab = null;
            _viewRoot = null;
        }

        /// <summary>本地槽位（未对齐/解析失败 = -1）——死亡 HUD / 保护壳驱动的注入缝。</summary>
        public int LocalSlot()
        {
            if (View == null || View.LocalEntityId == 0L) return -1;
            return View.TryGetSlot(View.LocalEntityId, out int slot) ? slot : -1;
        }

        /// <summary>本地是否阵亡（预测态锁存判据——<see cref="SimView.IsDead"/> 单源）。</summary>
        public bool IsLocalDead()
        {
            int slot = LocalSlot();
            return slot >= 0 && View.IsDead(slot);
        }

        /// <summary>本地玩家是否处于瞄准态（右键 ADS）——喂相机语义源。</summary>
        public bool IsLocalAiming()
        {
            return View != null && View.LocalEntityId != 0
                && View.TryGetSlot(View.LocalEntityId, out int slot)
                && View.IsAiming(slot);
        }

        public long LocalEntityId => View != null ? View.LocalEntityId : 0L;

        /// <summary>槽位 → 角色视图（保护壳跟随目标；缺 = null）。</summary>
        private GameObject ViewOf(int slot)
            => View != null && View.TryGetView(slot, out GameObject view) ? view : null;

        private GameObject InstantiateView(string location, Transform parent)
        {
            if (_prefab == null) return null;
            var go = UnityEngine.Object.Instantiate(_prefab, parent);
            go.name = location;

            // 复活/传送会把视图瞬移到出生点：预制体部分 SkinnedMeshRenderer 关闭了离屏更新，
            // 死亡姿态留下的包围盒仍停在旧位置，传送后渲染器会被永久剔除（模型消失）。
            var skinned = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < skinned.Length; i++) skinned[i].updateWhenOffscreen = true;
            return go;
        }
    }
}
