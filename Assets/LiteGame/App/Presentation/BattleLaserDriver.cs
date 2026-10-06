using System;
using LiteClient;
using LiteSim;
using LiteSim.View;
using UnityEngine;
using UnityEngine.Rendering;

namespace LiteGame
{
    /// <summary>
    /// 武器瞄准激光驱动（**激光器装在武器上**：束实例挂到武器挂载点、沿枪管方向出射，
    /// 不追鼠标；**射击语境内收敛**——瞄准/开火窗内端点收敛到开火射线终点，根治激光与
    /// 准心的偏移）：纯 View 层——每渲染帧把 <see cref="LineRenderer"/>（Assets/FX/fx_lazer_sight.prefab——
    /// KriptoFX Lazer 的 fx 归置副本，纯视觉资产零脚本）从枪口挂载点铺到端点（模式见 TryResolveFacts）。
    ///
    /// **挂载与方向**（用户口径："做在武器上、只从武器激光挂载点射出"）：
    /// - 束实例 <c>SetParent</c> 到本地视图的 <c>Weapon_Rifle/Muzzle</c> 锚点（prefab 摆位+前向烘焙：
    ///   +Z = 枪管轴，构建器 BuildLaserAssets 落；美术转锚点即调出射方向）；
    /// - <c>useWorldSpace=false</c>——束两点写挂载点**本地空间**，随枪的姿态/动画自动跟转，
    ///   驱动不写束的位姿，只写端点；
    /// - **语境外不追鼠标**（方向来自枪）；**射击语境内端点收敛到开火射线终点**（"激光归零"——
    ///   消掉动画姿态/出射原点的残差，激光点落在准心线上；同语境非瞄准向量时落回枪管模式）。
    ///
    /// **截停（保留原需求）**：<see cref="SimRaycast"/> 单源（实体圆柱 + 静态障碍取最近，与
    /// <see cref="ShootingSystem"/> 同一几何源）——**遇玩家/障碍截停不穿透**；射程
    /// <see cref="CombatConfig.HitscanRange"/>。
    ///
    /// **支持位**：<see cref="LaserSightPolicy"/>（按武器 def id；测试模式直通——任何武器都支持）。
    /// **可见性门**：本地未对齐/死亡/未装备武器/挂载点缺失（灰盒无武器 → 无激光，诚实退化）/输入被拦。
    /// **不做**：不改 Sim 状态；弹着/命中特效归命中分发批次。
    /// </summary>
    public sealed class BattleLaserDriver : IDisposable
    {
        private readonly SimView _view;
        private readonly IInputService _input;          // 仅被拦门（模态 UI）；null = 无输入形态（不拦）
        private readonly RollbackSim _sim;             // 本地预测态（SelectedWeapon/WeaponDefId——只读）
        private readonly SimMapData _map;              // 障碍射线用图（两端单源 StandardBattleMap）
        private readonly LineRenderer _beam;
        private readonly Func<Vector3?> _aimPointOf;   // 瞄准地面点注入源（准心同源——TryGetAimPoint）；null = 无该事实
        private bool _disposed;

        private GameObject _mountView;                 // 挂载点缓存键：所属视图实例（视图重建 → 重解析重挂）
        private Transform _mount;                     // 武器激光挂载点（Weapon_Rifle/Muzzle）

        public BattleLaserDriver(SimView view, IInputService input, RollbackSim sim, SimMapData map,
            LineRenderer beam, Func<Vector3?> aimPointOf = null)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _input = input;
            _sim = sim ?? throw new ArgumentNullException(nameof(sim));
            _map = map ?? throw new ArgumentNullException(nameof(map));
            _beam = beam ?? throw new ArgumentNullException(nameof(beam));
            _aimPointOf = aimPointOf;

            // 实例一次性配置（prefab 是 2 点/宽 0.03 的美术资产，驱动只改驱动面）：
            // 挂载点本地空间 + 不投影——发光束不该在场景里拉出影子（束位姿归挂载点，驱动不写）
            _beam.useWorldSpace = false;
            _beam.shadowCastingMode = ShadowCastingMode.Off;
            _beam.receiveShadows = false;
            _beam.positionCount = 2;
            _beam.transform.gameObject.SetActive(false);
        }

        /// <summary>每渲染帧驱动（ProcedureBattle.OnUpdate 末段——视图/准心之后）。</summary>
        public void Tick()
        {
            if (_disposed) return;

            bool localReady = _view.HasLocalDisplay && _view.LocalEntityId != 0;
            bool blocked = _input != null && _input.IsBlocked;
            Vector3 localEnd = Vector3.zero;
            bool show = localReady && !blocked && TryResolveFacts(out localEnd);

            _beam.transform.gameObject.SetActive(show);
            if (!show) return;

            _beam.SetPosition(0, Vector3.zero);         // 挂载点本位
            _beam.SetPosition(1, localEnd);              // 挂载点本地空间端点（模式见 TryResolveFacts）
        }

        /// <summary>
        /// 解算本帧激光端点（挂载点本地空间；门内各条任一不成立 → 藏）。
        ///
        /// **双模式（"激光视觉上指向准心"——端点直落准心标记的地面点）**：
        /// - **收敛模式**（射击语境：瞄准 ∨ 开火驻留窗——与限速/朝向派生同一语境口径）：端点 = **瞄准地面点**
        ///   （注入源与准心同源 `TryGetAimPoint`——点恰好压在准心上）；途中被玩家/障碍截停 ⇒ 端点按
        ///   水平截停比例落在束段上（读作"激光打到挡的东西"）；瞄准点超射程 ⇒ 按弹程截断（打不到的地方
        ///   束到不了）。束起点仍是枪口挂载点（"做在武器上"）。**子弹语义不动**：子弹过准心正上方平飞
        ///   （瞄准锚=逻辑枪口），激光落点标记"准心指哪"。无瞄准点事实（无鼠标/未采到）→ 落回开火射线
        ///   终点（瞄准向量版），再无 → 枪管模式。
        /// - **枪管模式**（语境外：待机/移动）：沿挂载点前向的 XZ 水平投影出射（枪口朝下自然扎地）。
        /// </summary>
        private bool TryResolveFacts(out Vector3 localEnd)
        {
            localEnd = Vector3.zero;
            if (!_view.TryGetSlot(_view.LocalEntityId, out int slot)) return false;
            if (_view.IsDead(slot)) return false;                    // 尸体不出激光（本地槽读预测态）

            // 武器支持位：未装备 → 藏；def id 走策略（测试模式直通——任何武器都支持）
            ref EntitySlot e = ref _sim.State.Entities[slot];
            if (e.SelectedWeapon < 0 || e.SelectedWeapon >= SimConfig.WeaponSlotsPerEntity) return false;
            int defId = _sim.State.Weapons[slot * SimConfig.WeaponSlotsPerEntity + e.SelectedWeapon].WeaponDefId;
            if (!LaserSightPolicy.Supports(defId)) return false;

            // 激光器装在武器上：无挂载点（灰盒无武器）→ 无激光——诚实退化，不拿 Sim 原点造第二视觉源
            Transform mount = ResolveMount(slot);
            if (mount == null) return false;
            AttachBeam(mount);

            // 收敛模式：射击语境内端点 = 瞄准地面点（准心同源注入）
            bool fireContext = _view.IsAiming(slot) || e.FireStanceFrames > 0;
            if (fireContext && _aimPointOf != null && _aimPointOf() is Vector3 aimPoint)
            {
                // 截停/射程在水平面上判定（与子弹同一几何源）：从逻辑枪口指向瞄准点水平位置，
                // maxT = min(瞄准点距离, 弹程)；端点按水平进度比例落在束段（挂载点 → 瞄准点）上
                var muzzle = CombatConfig.MuzzleOrigin(e.Pos, e.Yaw);
                float dx = aimPoint.x - muzzle.X;
                float dz = aimPoint.z - muzzle.Z;
                float aimDist2 = dx * dx + dz * dz;
                if (aimDist2 > 0.0000001f)
                {
                    float aimDist = Mathf.Sqrt(aimDist2);
                    float inv = 1f / aimDist;
                    dx *= inv;
                    dz *= inv;

                    float maxT = Mathf.Min(aimDist, CombatConfig.HitscanRange);
                    SimRaycast.RaycastEntities(_sim.State, slot, muzzle.X, muzzle.Y, muzzle.Z, dx, dz, maxT, out _, out float t);
                    if (SimRaycast.RaycastObstacles(_map, muzzle.X, muzzle.Y, muzzle.Z, dx, dz, maxT, out float tObstacle)
                        && tObstacle < t)
                        t = tObstacle;

                    // 无遮挡且瞄准点在射程内 → f=1 端点恰为瞄准点；遮挡/超程 → 按截断比例落段上
                    float f = maxT > 0f ? t / maxT : 1f;
                    if (f > 1f) f = 1f;
                    Vector3 endWorld = Vector3.Lerp(mount.position, aimPoint, f);
                    localEnd = mount.InverseTransformPoint(endWorld);
                    return true;
                }
                // 瞄准点与枪口重合（鼠标压枪）——落回瞄准向量版
            }

            // 回退：开火射线终点（瞄准向量版——无瞄准点事实时仍与子弹同线；
            // 与 ShootingSystem 同式——逻辑枪口出射 + SimRaycast 截停取最近），写回挂载点本地空间
            if (fireContext)
            {
                var pending = _input != null ? _input.Pending : default;
                float ax = pending.AimX;
                float az = pending.AimZ;
                if (ax * ax + az * az > 0.0000001f)
                {
                    float inv = 1f / Mathf.Sqrt(ax * ax + az * az);
                    ax *= inv;
                    az *= inv;

                    var muzzle = CombatConfig.MuzzleOrigin(e.Pos, e.Yaw);

                    float range = CombatConfig.HitscanRange;
                    SimRaycast.RaycastEntities(_sim.State, slot, muzzle.X, muzzle.Y, muzzle.Z, ax, az, range, out _, out float t);
                    if (SimRaycast.RaycastObstacles(_map, muzzle.X, muzzle.Y, muzzle.Z, ax, az, range, out float tObstacle)
                        && tObstacle < t)
                        t = tObstacle;

                    Vector3 endWorld = new Vector3(muzzle.X + ax * t, muzzle.Y, muzzle.Z + az * t);
                    localEnd = mount.InverseTransformPoint(endWorld);
                    return true;
                }
                // 零瞄准向量（被拦清零/无输入形态）——落回枪管模式
            }

            // 枪管模式：沿挂载点前向的 XZ 水平投影（2.5D 射线），原点/眼高 = 挂载点世界位（枪口高度）
            Vector3 forward = mount.forward;
            float bx = forward.x;
            float bz = forward.z;
            if (bx * bx + bz * bz <= 0.0000001f) return false;       // 枪口朝天/朝地（无水平分量）→ 藏
            float inv2 = 1f / Mathf.Sqrt(bx * bx + bz * bz);
            bx *= inv2;
            bz *= inv2;

            Vector3 origin = mount.position;
            float range2 = CombatConfig.HitscanRange;
            SimRaycast.RaycastEntities(_sim.State, slot, origin.x, origin.y, origin.z, bx, bz, range2, out _, out float t2);
            if (SimRaycast.RaycastObstacles(_map, origin.x, origin.y, origin.z, bx, bz, range2, out float tOb2)
                && tOb2 < t2)
                t2 = tOb2;
            localEnd = new Vector3(0f, 0f, t2);
            return true;
        }

        /// <summary>武器激光挂载点（按视图实例缓存——视图回收/重建后重解析）。</summary>
        private Transform ResolveMount(int slot)
        {
            if (!_view.TryGetView(slot, out var view)) return null;
            if (_mount != null && _mountView == view) return _mount;

            _mountView = view;
            _mount = FindDescendant(view.transform, "Muzzle");
            return _mount;
        }

        /// <summary>束挂到挂载点（挂载点换代时重挂；本地位/转/缩放归零——束几何完全由本地空间点表达）。</summary>
        private void AttachBeam(Transform mount)
        {
            if (_beam.transform.parent == mount) return;             // 已挂当前挂载点（幂等）

            _beam.transform.SetParent(mount, false);
            _beam.transform.localPosition = Vector3.zero;
            _beam.transform.localRotation = Quaternion.identity;
            _beam.transform.localScale = Vector3.one;
        }

        /// <summary>深度找子（Transform.Find 只认全路径；挂载点深度随骨架走——按名遍历）。</summary>
        private static Transform FindDescendant(Transform root, string name)
        {
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child.name == name) return child;
                Transform found = FindDescendant(child, name);
                if (found != null) return found;
            }
            return null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _beam.transform.gameObject.SetActive(false);             // 实例销毁归流程（随视图根拆）
        }
    }
}
