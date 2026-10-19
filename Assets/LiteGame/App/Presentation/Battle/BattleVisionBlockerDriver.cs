using System;
using System.Collections.Generic;
using LiteFramework;
using LiteSim;
using LiteView;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LiteGame
{
    /// <summary>
    /// 视野遮蔽与遮挡物渐隐驱动（《视野与遮挡专项设计》档 A1 复合方案·姿态动态判据 v3.1）：
    /// **遮蔽先行、渐隐即安全**——两机制一体，次序不可倒：
    /// ① **渐隐（找回自己）**：相机→本地玩家**当前柱顶**（<see cref="BodyBake.Height"/> 姿态单源；
    /// 蹲伏落地后换蹲柱烘焙值）的有界线段被哪个遮挡物挡住，就把该遮挡物切成渐隐材质变体
    /// （<c>sharedMaterial</c> 基准↔渐隐成对切换＋迟滞防闪烁，不新建材质）。
    /// ② **遮蔽（渐隐期间的影区）**：渐隐在场时，对**其他实体**各自的当前柱顶做同款线段求交——
    /// 被挡者维持隐藏（"掩体后的视野不可见"），未挡者照常显示（露头者不因他人渐隐而消失）。
    /// 因影区实体已被遮蔽藏住，渐隐只露出地板与自己——不透视任何实体。
    /// 不渐隐时不做任何实体隐藏——非渐隐遮挡由深度测试天然藏好（视觉网格与判定几何同源）。
    ///
    /// **边界（A1 表现级）**：只读 <see cref="SimMapData"/>（与瞄准激光同款只读源）与
    /// <see cref="SimView"/> 视图事实；隐藏＝渲染开关而非销毁/回收——快照/镜像/预测/重连数据面
    /// 零动（数据全量照常）；判定链零改动（"掩体仍挡子弹"由 SimRaycast 原路径保证）。
    /// **开火显形**：隐藏只作用于实体自有渲染器（构造注入 <c>viewRenderersOf</c> 解析）——
    /// 枪口火光/弹道/伤害数字是挂点/世界容器下的独立实例，不经该解析器，天然不隐藏
    /// （藏于掩体后开火即暴露方位）。
    /// </summary>
    public sealed class BattleVisionBlockerDriver : IDisposable
    {
        /// <summary>
        /// 探测节拍（秒）——"探测低频"口径：10Hz 对相机/实体移动的遮挡变化足够跟手
        /// （0.1s 内画面内实体位移 ≤ 一个身位），而把线段求交成本压到每秒常数次
        /// （≤256 障碍 × 实体数，非每帧 × 全图）。
        /// </summary>
        private const float ProbeIntervalSeconds = 0.1f;

        /// <summary>
        /// 切换迟滞（连续探针数）：进/出同门限 ≈0.2s——贴边掠过（单次抖动）不触发切换，
        /// 防边界上材质/显隐反复翻转（闪烁）。进出对称：漏挡与误挡同代价，不偏向哪一侧。
        /// </summary>
        private const int HoldProbes = 2;

        /// <summary>
        /// 障碍↔场景碰撞体的匹配门限（XZ 距离平方，=0.5m²）。烘焙两侧同源（场景碰撞体 ↔
        /// 毫米量化行），中心偏差只有毫米级量化误差；0.5m 远大于该误差、又远小于相邻掩体
        /// 中心距（≥2m——同规格箱体的最小布距），越门即判"该障碍无视觉件"（跳过，不猜）。
        /// </summary>
        private const float MatchDistance2 = 0.25f;

        private readonly SimView _view;
        private readonly SimMapData _map;
        private readonly Func<Camera> _cameraOf;              // 渲染相机解析（伤害飘字同款注入缝——null = 无相机形态）
        private readonly Material _baseMaterial;              // 掩体基准材质（渐隐回退目标；也是绑定判据）
        private readonly Material _fadeMaterial;             // 渐隐变体（同色半透明——成对裁决，勿拿无对材质充数）
        private readonly Func<GameObject, Renderer[]> _viewRenderersOf;   // 视图自有渲染器解析（prefab 路径单源——运行时挂载的 VFX 不在其中）
        private readonly List<Occluder> _occluders;          // 可渐隐绑定（材质成对的遮挡渲染件）
        private readonly SlotState[] _slots = new SlotState[SimConfig.MaxEntities];   // 影区隐藏状态（按槽位）
        private float _probeTimer;                            // 探测节拍累计（首拍即探）
        private bool _disposed;

        /// <summary>
        /// 构造即绑定：扫描已加载场景的 <see cref="MapTopology.MapCollisionRoot"/> 代理树（地图
        /// 场景约定拓扑），障碍按 XZ 近邻匹配碰撞体，<c>sharedMaterial</c> 与基准材质同引用者
        /// 入渐隐绑定。材质不成对的渲染件（如灰盒训练场的默认材质）不入绑定——**内容配对裁决**：
        /// 渐隐变体是"同色半透明"的成对资产，拿基准色之外的材质充数会渐隐变色。零绑定时驱动惰性
        /// （不渐隐⇒不隐藏⇒全部交给深度测试，现状即安全）。
        /// </summary>
        /// <param name="view">战斗视图（槽位视图/本地对齐/柱顶事实——只读）。</param>
        /// <param name="map">判定地图数据（与两端同源的只读障碍几何）。</param>
        /// <param name="cameraOf">对局渲染相机解析（Cinemachine brain 输出——非 Camera.main）；null = 无相机（保守全恢复）。</param>
        /// <param name="baseMaterial">掩体基准材质（不透明）——绑定判据＋渐隐回退目标。</param>
        /// <param name="fadeMaterial">渐隐变体（同色半透明）。</param>
        /// <param name="viewRenderersOf">视图实例→实体自有渲染器（prefab 路径单源，运行时 VFX 挂件不在其中）；null = 影区隐藏降级（渐隐照常）。</param>
        public BattleVisionBlockerDriver(SimView view, SimMapData map, Func<Camera> cameraOf,
            Material baseMaterial, Material fadeMaterial, Func<GameObject, Renderer[]> viewRenderersOf = null)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _map = map ?? throw new ArgumentNullException(nameof(map));
            _baseMaterial = baseMaterial ?? throw new ArgumentNullException(nameof(baseMaterial));
            _fadeMaterial = fadeMaterial ?? throw new ArgumentNullException(nameof(fadeMaterial));
            _cameraOf = cameraOf;
            _viewRenderersOf = viewRenderersOf;

            _occluders = BindOccluders();
            Log.Info($"vision-attached occluders={_occluders.Count}/{_map.ObstacleCount} "
                + (_occluders.Count == 0 && _map.ObstacleCount > 0 ? "（无成对材质绑定——渐隐惰性，深度测试兜底）" : string.Empty),
                LogTag.Battle);
        }

        /// <summary>
        /// 每渲染帧驱动（内部按 <see cref="ProbeIntervalSeconds"/> 节拍探测）：
        /// ① 相机→本地柱顶线段 → 逐绑定迟滞渐隐；② 任一渐隐在场 → 其他实体柱顶求交 →
        /// 影区隐藏；无渐隐在场 → 影区隐藏全恢复。无相机/未对齐/本地视图缺位（含阵亡）→
        /// 全恢复（保守：宁可露出自然遮挡，不留任何渐隐/隐藏残态）。
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (_disposed) return;

            _probeTimer -= deltaTime;
            if (_probeTimer > 0f) return;
            _probeTimer = ProbeIntervalSeconds;

            Camera camera = _cameraOf != null ? _cameraOf() : null;
            int localSlot = -1;
            GameObject localView = null;
            bool localReady = _view.LocalEntityId != 0
                && _view.TryGetSlot(_view.LocalEntityId, out localSlot)
                && _view.TryGetView(localSlot, out localView)
                && localView != null;

            if (camera == null || !localReady)
            {
                RestoreAll();
                return;
            }

            Vector3 eye = camera.transform.position;
            Vector3 localTop = localView.transform.position + Vector3.up * BodyBake.Height;

            // ① 渐隐：本地玩家"有效遮蔽"判据＝姿态动态（v3.1）——同一只 1m 箱，站姿柱顶越箱
            //    射线从上方过（不渐隐）；蹲姿柱顶被箱挡（渐隐）。逐障碍各测各的：谁挡谁渐隐。
            bool anyFade = false;
            for (int i = 0; i < _occluders.Count; i++)
            {
                Occluder oc = _occluders[i];
                if (oc.Renderer == null) continue;                       // 场景件已毁（理论不达——防御）
                bool blocked = VisionSegment.Blocked(in _map.Obstacles[oc.ObstacleIndex], eye, localTop);
                StepFade(oc, blocked);
                anyFade |= oc.Faded;
            }

            // ② 影区实体隐藏：只在渐隐在场时做（不渐隐＝深度遮挡天然藏好）；本地玩家豁免
            //    ——渐隐只为找回自己，本人永远全渲染。
            if (anyFade) ProbeShadowRegion(eye, localSlot);
            else RestoreHiddenSlots();
        }

        /// <summary>拆件：全部恢复（材质归基准、实体归可见）——对局收尾在场景卸载前必达，不留残态。</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            RestoreAll();
            _occluders.Clear();
            Array.Clear(_slots, 0, _slots.Length);
        }

        // ---- 渐隐面 ----

        /// <summary>迟滞推进单遮挡物：连续 <see cref="HoldProbes"/> 拍同向才切换，切换即换 <c>sharedMaterial</c>。</summary>
        private void StepFade(Occluder oc, bool blocked)
        {
            if (oc.Faded)
            {
                if (blocked) { oc.ClearRun = 0; return; }
                if (++oc.ClearRun < HoldProbes) return;
                oc.Faded = false;
                oc.ClearRun = 0;
                ApplyFade(oc, false);
            }
            else
            {
                if (!blocked) { oc.BlockedRun = 0; return; }
                if (++oc.BlockedRun < HoldProbes) return;
                oc.Faded = true;
                oc.BlockedRun = 0;
                ApplyFade(oc, true);
            }
        }

        private void ApplyFade(Occluder oc, bool fade)
        {
            if (oc.Renderer == null) return;
            oc.Renderer.sharedMaterial = fade ? _fadeMaterial : _baseMaterial;
        }

        // ---- 影区实体隐藏面 ----

        private void ProbeShadowRegion(Vector3 eye, int localSlot)
        {
            for (int slot = 0; slot < SimConfig.MaxEntities; slot++)
            {
                if (slot == localSlot) continue;                        // 本地玩家豁免（找回自己）
                if (!_view.TryGetView(slot, out GameObject view) || view == null)
                {
                    ReleaseSlot(slot);                                 // 无视图（阵亡回收/未出生）——状态清退并归还池前归位
                    continue;
                }

                SlotState state = EnsureSlotState(slot, view);
                Vector3 top = view.transform.position + Vector3.up * BodyBake.Height;
                bool blocked = VisionSegment.BlockedByAny(in _map, eye, top);
                StepHide(state, blocked);
            }
        }

        /// <summary>
        /// 取槽位隐藏状态；视图实例换代（复活/重连重建——池化复用同一 GameObject）时重绑：
        /// 旧实例若处于隐藏态先归位可见——池中件不带病（复用即得的实例不会"复活后永久隐形"）。
        /// </summary>
        private SlotState EnsureSlotState(int slot, GameObject view)
        {
            SlotState state = _slots[slot];
            if (state != null && state.View == view) return state;

            if (state != null && state.Hidden)
                SetRenderersEnabled(state.Renderers, true);             // 旧实例隐藏态归还（同一池件即新实例本身）

            state = new SlotState { View = view, Renderers = ResolveRenderers(view) };
            _slots[slot] = state;
            return state;
        }

        private void ReleaseSlot(int slot)
        {
            SlotState state = _slots[slot];
            if (state == null) return;
            if (state.Hidden) SetRenderersEnabled(state.Renderers, true);
            _slots[slot] = null;
        }

        /// <summary>迟滞推进单实体显隐：连续 <see cref="HoldProbes"/> 拍同向才切换——切出影区即恢复（渲染开关，不销毁）。</summary>
        private void StepHide(SlotState state, bool blocked)
        {
            if (state.Hidden)
            {
                if (blocked) { state.ClearRun = 0; return; }
                if (++state.ClearRun < HoldProbes) return;
                state.Hidden = false;
                state.ClearRun = 0;
                SetRenderersEnabled(state.Renderers, true);
            }
            else
            {
                if (!blocked) { state.BlockedRun = 0; return; }
                if (++state.BlockedRun < HoldProbes) return;
                state.Hidden = true;
                state.BlockedRun = 0;
                SetRenderersEnabled(state.Renderers, false);
            }
        }

        private Renderer[] ResolveRenderers(GameObject view)
            => _viewRenderersOf != null ? _viewRenderersOf(view) ?? Array.Empty<Renderer>() : Array.Empty<Renderer>();

        private static void SetRenderersEnabled(Renderer[] renderers, bool enabled)
        {
            if (renderers == null) return;
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null) renderers[i].enabled = enabled;   // 池中/已毁件 fake-null 跳过
        }

        // ---- 恢复面 ----

        private void RestoreHiddenSlots()
        {
            for (int slot = 0; slot < _slots.Length; slot++)
            {
                SlotState state = _slots[slot];
                if (state == null) continue;
                if (state.Hidden)
                {
                    state.Hidden = false;
                    SetRenderersEnabled(state.Renderers, true);
                }
                state.BlockedRun = 0;
                state.ClearRun = 0;
            }
        }

        private void RestoreAll()
        {
            RestoreHiddenSlots();
            for (int i = 0; i < _occluders.Count; i++)
            {
                Occluder oc = _occluders[i];
                if (oc.Faded)
                {
                    oc.Faded = false;
                    ApplyFade(oc, false);
                }
                oc.BlockedRun = 0;
                oc.ClearRun = 0;
            }
        }

        // ---- 绑定面 ----

        /// <summary>
        /// 扫描已加载场景的 <see cref="MapTopology.MapCollisionRoot"/> 代理树，按 XZ 近邻把
        /// <see cref="SimMapData"/> 障碍匹配到场景碰撞体（两侧同烘焙源——中心只有毫米量化偏差），
        /// 再取其渲染件做材质成对判定。多张已加载地图同扫无害：错图的碰撞体距任何本图障碍
        /// 都远超 <see cref="MatchDistance2"/> 门限，自然出局。
        /// </summary>
        private List<Occluder> BindOccluders()
        {
            var result = new List<Occluder>(_map.ObstacleCount);
            var colliders = new List<Collider>(64);
            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                Scene scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;
                GameObject[] roots = scene.GetRootGameObjects();
                for (int r = 0; r < roots.Length; r++)
                    if (roots[r].name == MapTopology.MapCollisionRoot)
                        colliders.AddRange(roots[r].GetComponentsInChildren<Collider>());
            }
            if (colliders.Count == 0 || _map.ObstacleCount == 0) return result;

            var used = new bool[colliders.Count];
            for (int i = 0; i < _map.ObstacleCount; i++)
            {
                SimObstacle obstacle = _map.Obstacles[i];

                int best = -1;
                float best2 = MatchDistance2;
                for (int c = 0; c < colliders.Count; c++)
                {
                    if (used[c]) continue;
                    Vector3 center = colliders[c].bounds.center;
                    float dx = center.x - obstacle.Center.X;
                    float dz = center.z - obstacle.Center.Z;
                    float d2 = dx * dx + dz * dz;
                    if (d2 < best2) { best2 = d2; best = c; }
                }
                if (best < 0) continue;                                 // 无近邻视觉件——该障碍不可渐隐
                used[best] = true;

                Renderer renderer = colliders[best].GetComponent<Renderer>();
                if (renderer == null) renderer = colliders[best].GetComponentInChildren<Renderer>();
                if (renderer == null || renderer.sharedMaterial != _baseMaterial) continue;   // 材质不成对——不入绑定
                result.Add(new Occluder { ObstacleIndex = i, Renderer = renderer });
            }
            return result;
        }

        /// <summary>单遮挡物绑定：障碍下标（求交用）＋场景渲染件（材质切换用）＋迟滞状态。</summary>
        private sealed class Occluder
        {
            public int ObstacleIndex;
            public Renderer Renderer;
            public bool Faded;
            public int BlockedRun;
            public int ClearRun;
        }

        /// <summary>单槽位隐藏状态：视图实例（换代检测）＋自有渲染器（显隐开关）＋迟滞计数。</summary>
        private sealed class SlotState
        {
            public GameObject View;
            public Renderer[] Renderers;
            public bool Hidden;
            public int BlockedRun;
            public int ClearRun;
        }
    }
}
