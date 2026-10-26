using System;
using LiteClient;
using LiteSim;
using LiteView;
using UnityEngine;
using UnityEngine.Rendering;

namespace LiteGame
{
    /// <summary>
    /// 武器瞄准激光驱动（**激光器装在武器上**：束实例挂到武器挂载点、沿枪管方向出射，
    /// 端点只由枪口前向决定——不追鼠标，也不收敛到准心/AimPoint）：纯 View 层——每渲染帧把
    /// <see cref="LineRenderer"/>（Assets/ShareResource/FX/fx_lazer_sight.prefab——KriptoFX Lazer 的
    /// fx 归置副本，纯视觉资产零脚本）从枪口挂载点铺到枪管射线的截停点。
    ///
    /// **挂载与方向**（用户口径："做在武器上、只从武器激光挂载点射出"）：
    /// - 束实例 <c>SetParent</c> 到本地视图的 <c>Weapon_Rifle/Muzzle</c> 锚点（prefab 摆位+前向烘焙：
    ///   +Z = 枪管轴，构建器 BuildLaserAssets 落；美术转锚点即调出射方向）；
    /// - <c>useWorldSpace=false</c>——束两点写挂载点**本地空间**，随枪的姿态/动画自动跟转，
    ///   驱动不写束的位姿，只写端点；
    /// - **端点恒沿枪口前向**：瞄准/开火语境不改方向、不读准心点；与准心的视觉差由枪口/弹道视差
    ///   自然承担（用户裁决：瞄准线落点不用跟准心）。
    ///
    /// **截停**：<see cref="SimRaycast"/> 单源（实体圆柱 + 静态障碍取最近，与
    /// <see cref="ShootingSystem"/> 同一几何源）——**遇玩家/障碍截停不穿透**；射程
    /// <see cref="CombatConfig.HitscanRange"/>。方向取挂载点世界前向（含俯仰），不再做 XZ 水平投影。
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
        private readonly SimMapData _map;              // 障碍射线用图（两端单源：同份 tbmap 投影——经 BattleContext 注入）
        private readonly MatchTestRules _rules;        // 本局规则持有件（构造注入一次——不读 SimTestRules 静态读口）
        private readonly LineRenderer _beam;
        private bool _disposed;

        private GameObject _mountView;                 // 挂载点缓存键：所属视图实例（视图重建 → 重解析重挂）
        private Transform _mount;                     // 武器激光挂载点（Weapon_Rifle/Muzzle）

        /// <param name="rules">本局规则持有件（<see cref="BattleContext.MatchRules"/>——爆头带/爆头柱覆写随动；
        /// null = 无测试规则，恒用烘焙导出值）。**每局稳定引用**，构造注入一次，帧路径不碰静态读口。</param>
        public BattleLaserDriver(SimView view, IInputService input, RollbackSim sim, SimMapData map,
            LineRenderer beam, MatchTestRules rules = null)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _input = input;
            _sim = sim ?? throw new ArgumentNullException(nameof(sim));
            _map = map ?? throw new ArgumentNullException(nameof(map));
            _beam = beam ?? throw new ArgumentNullException(nameof(beam));
            _rules = rules;

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
    /// **单一模式：枪管射线**——端点 = 挂载点世界前向（含俯仰）的三维截停点；瞄准/开火语境不改方向，
    /// 不读准心/AimPoint。截停走 <see cref="TraceBeam"/>（实体三维 + 障碍折回同量纲 + 射程取最近）。
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

            // 枪管射线：方向 = 挂载点世界前向（含俯仰），起点 = 挂载点世界位；端点写回挂载点本地空间。
            // 不读 AimPoint/准心，也不因瞄准/开火窗改变方向（用户裁决：激光落点不用跟准心）。
            Vector3 forward = mount.forward;
            Vector3 origin = mount.position;
            float t = TraceBeam(origin.x, origin.y, origin.z,
                forward.x, forward.z, forward.y, CombatConfig.HitscanRange, slot);
            Vector3 endWorld = origin + forward * t;
            localEnd = mount.InverseTransformPoint(endWorld);
            return true;
        }

        /// <summary>
        /// 束的截停距离（**与 <see cref="ShootingSystem"/> 同式同量纲**，专项设计 §4/C9）：
        /// 实体走三维入口（方向含 Y），障碍仍在 XZ 平面按**水平距离**求解后折回三维参数
        /// （<c>t_3d = t_xz / h</c>，h = 水平投影长度），否则两类距离不同量纲、取近会取错。
        /// 近乎垂直（h 退化）时障碍在水平方向不可达 ⇒ 判未命中（不与实体争近远）。
        /// 返回值恒 ≤ <paramref name="maxT"/>（未命中即返回 maxT）。
        /// </summary>
        private float TraceBeam(float ox, float oy, float oz, float dx, float dz, float dy, float maxT, int skipSlot)
        {
            SimRaycast.RaycastEntities(_sim.State, skipSlot,
                new SimVector3(ox, oy, oz), new SimVector3(dx, dy, dz), maxT, out _, out float t, _rules);

            float h = Mathf.Sqrt(dx * dx + dz * dz);
            if (h > SimRaycast.ParallelEpsilon)
            {
                float invH = 1f / h;
                if (SimRaycast.RaycastObstacles(_map, ox, oy, oz, dx * invH, dz * invH, maxT, out float tFlat))
                {
                    float tObstacle = tFlat * invH;
                    if (tObstacle < t) t = tObstacle;
                }
            }
            return t;
        }

        /// <summary>武器激光挂载点（按视图实例缓存——视图回收/重建后重解析）。</summary>
        private Transform ResolveMount(int slot)
        {
            if (!_view.TryGetView(slot, out var view)) return null;
            if (_mount != null && _mountView == view) return _mount;

            _mountView = view;
            _mount = WeaponMounts.FindMuzzle(view.transform);   // 挂点解析单源（枪口火光同用）
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

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _beam.transform.gameObject.SetActive(false);             // 实例销毁归流程（随视图根拆）
        }
    }
}
