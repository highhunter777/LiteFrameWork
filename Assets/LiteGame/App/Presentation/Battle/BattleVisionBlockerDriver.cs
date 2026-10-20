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
    /// 视野遮蔽与遮挡物渐隐驱动（《视野与遮挡专项设计》档 A1·v3.4 角色视觉权威）：
    /// **显隐层（角色视觉）**：本地角色**眼位**→目标**身体多点采样**（头/胸/髋 × 中/左右缘；
    /// 高度/半径取 <see cref="BodyBake"/>，眼位由头部带 <see cref="CombatConfig.HeadHitLine"/> 派生）——
    /// 任一点无遮挡＝角色看得见＝渲染，全挡＝隐藏；本地玩家恒渲染。目标位置取**渲染帧快照位**
    /// （<see cref="SimView.TryGetRenderPosition"/>），每拍判；切换沿用 2 拍迟滞＋0.5s 最短保持。
    /// **不再使用"柱顶↔柱顶"单线**：单线无法表达"身体边缘露出/拐角探头"，会把角色看得见的敌人误藏。
    /// 真单向可见（拐角/高低差/距离差）按掩体优势接受，不再要求两端同结论；快照位＋最短保持只防
    /// 延迟导致的假单向与闪隐。
    /// **渐隐层（相机补位）**：触发源＝本地玩家（恒可见）＋**所有角色可见的远端**；对每个触发目标做
    /// `相机→目标柱顶` 三维线段求交，挡住哪个可渐隐掩体就渐隐哪个；同一掩体按 OR 汇总。
    /// **全挡敌人不参与渐隐**（renderer 已关，渐隐也透不出来）——渐隐只补"角色看得见但相机被挡"的
    /// 第三人称盲区，不是相机透视。相机→柱顶线段只可能与相机侧（屏幕上角色下方）的掩体相交；
    /// 角色上方/更远的掩体挡的是角色前方空间，天然不触发。
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
        /// **最短保持拍数**（0.5s）：状态一旦切换，至少保持这么久才允许再切。迟滞只挡单拍抖动，
        /// 挡不住"两拍真两拍假"的 0.2s 周期振荡（实测日志连续 9 次翻转＝敌人闪隐）；两端快照时间线
        /// 的渲染帧不可能逐位一致，掩体边缘的结论允许有出入，但**不允许闪**——最短保持把边界振荡
        /// 压成单次切换。新状态的初始保持直接给满（首次判定不被延迟）。
        /// </summary>
        private const int MinHoldProbes = 5;

        /// <summary>
        /// 障碍↔场景碰撞体的匹配门限（XZ 距离平方，=0.5m²）。烘焙两侧同源（场景碰撞体 ↔
        /// 毫米量化行），中心偏差只有毫米级量化误差；0.5m 远大于该误差、又远小于相邻掩体
        /// 中心距（≥2m——同规格箱体的最小布距），越门即判"该障碍无视觉件"（跳过，不猜）。
        /// </summary>
        private const float MatchDistance2 = 0.25f;

        /// <summary>腰际采样高度（占身高的比例）——头/胸之下再取一档，覆盖低掩体露身。</summary>
        private const float WaistHeightRatio = 0.5f;

        /// <summary>观察者眼位（头部带中点）——头部带由 HeadBake 单源烘焙；不用柱顶 1.8m 冒充眼睛。</summary>
        private static readonly float EyeHeight =
            (CombatConfig.HeadHitLine + CombatConfig.HitscanHeight) * 0.5f;

        /// <summary>目标身体采样高度（头顶/头部带下沿/腰际）——与身体烘焙同源；B1 蹲伏后换蹲柱高度。</summary>
        private static readonly float[] TargetSampleHeights =
        {
            BodyBake.Height,
            BodyBake.Height * HeadBake.Ratio,
            BodyBake.Height * WaistHeightRatio,
        };

        /// <summary>目标身体横向采样因子（中/两侧缘，乘身体半径）——任一点露出即判可见。</summary>
        private static readonly float[] TargetSampleSideFactors = { 0f, 1f, -1f };

        private readonly SimView _view;
        private readonly SimMapData _map;
        private readonly Func<Camera> _cameraOf;              // 渲染相机解析（伤害飘字同款注入缝——null = 无相机形态）
        private readonly Material _baseMaterial;              // 掩体基准材质（渐隐回退目标；也是绑定判据）
        private readonly Material _fadeMaterial;             // 渐隐变体（同色半透明——成对裁决，勿拿无对材质充数）
        private readonly Func<GameObject, Renderer[]> _viewRenderersOf;   // 视图自有渲染器解析（prefab 路径单源——运行时挂载的 VFX 不在其中）
        private readonly List<Occluder> _occluders;          // 可渐隐绑定（材质成对的遮挡渲染件）
        private readonly SlotState[] _slots = new SlotState[SimConfig.MaxEntities];   // 显隐状态（按槽位）
        private readonly List<int> _visibleSlots = new List<int>(SimConfig.MaxEntities);   // 本拍角色可见的远端（渐隐触发源）
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
        /// <param name="viewRenderersOf">视图实例→实体自有渲染器（prefab 路径单源，运行时 VFX 挂件不在其中）；null = 显隐降级（渐隐照常）。</param>
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
        /// ① **显隐层**：角色眼位→目标身体采样，任一点可见即显示、全挡隐藏（先行，产出渐隐触发源）；
        /// ② **渐隐层**：自身（相机→本地柱顶）＋所有角色可见远端（相机→其柱顶）——挡住哪个可渐隐掩体
        /// 就渐隐哪个，全挡敌人不参与。无相机/未对齐/本地视图缺位（含阵亡）→ 全恢复
        /// （保守：宁可露出自然遮挡，不留任何渐隐/隐藏残态）。
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

            Vector3 cameraPos = camera.transform.position;
            Vector3 localTop = localView.transform.position + Vector3.up * BodyBake.Height;   // 自身渐隐：视图位（镜头挡没挡"画面上的人"）
            Vector3 localEye = BaseOf(localSlot, localView) + Vector3.up * EyeHeight;          // 角色视觉：渲染帧快照位

            ProbeVisibility(localEye, localSlot);
            ProbeFade(cameraPos, localTop);
        }

        /// <summary>拆件：全部恢复（材质归基准、实体归可见）——对局收尾在场景卸载前必达，不留残态。</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            RestoreAll();
            _occluders.Clear();
            _visibleSlots.Clear();
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

        /// <summary>
        /// 相机补位渐隐（v3.4）：自身（恒可见）＋所有角色可见远端各测一次 `相机→目标柱顶`；
        /// 同一遮挡物按 OR 汇总，任一可见目标被它挡住即渐隐。全挡远端不在可见集合内，
        /// 因此不会因相机能看见而触发渐隐（防透视）。
        /// </summary>
        private void ProbeFade(Vector3 cameraPos, Vector3 localTop)
        {
            for (int i = 0; i < _occluders.Count; i++)
                _occluders[i].BlockedThisProbe = false;

            for (int i = 0; i < _occluders.Count; i++)
            {
                Occluder oc = _occluders[i];
                if (oc.Renderer == null) continue;                       // 场景件已毁（理论不达——防御）
                if (VisionSegment.Blocked(in _map.Obstacles[oc.ObstacleIndex], cameraPos, localTop))
                    oc.BlockedThisProbe = true;
            }

            for (int v = 0; v < _visibleSlots.Count; v++)
            {
                int slot = _visibleSlots[v];
                if (!_view.TryGetView(slot, out GameObject remoteView) || remoteView == null) continue;
                Vector3 remoteTop = TopOf(slot, remoteView);
                for (int i = 0; i < _occluders.Count; i++)
                {
                    Occluder oc = _occluders[i];
                    if (oc.Renderer == null || oc.BlockedThisProbe) continue;
                    if (VisionSegment.Blocked(in _map.Obstacles[oc.ObstacleIndex], cameraPos, remoteTop))
                        oc.BlockedThisProbe = true;
                }
            }

            for (int i = 0; i < _occluders.Count; i++)
                StepFade(_occluders[i], _occluders[i].BlockedThisProbe);
        }

        // ---- 显隐层（角色视觉） ----

        /// <summary>
        /// 角色可见性（v3.4）：对每个其他实体测「本地眼位→目标身体采样」——任一点无遮挡即显示，
        /// 全挡才隐藏；本地玩家豁免。远端位置取渲染帧快照位，避免预测/插值差把显隐判据拉成两条线。
        /// 隐藏每拍判，切换走 <see cref="StepHide"/> 的迟滞＋最短保持。
        /// </summary>
        private void ProbeVisibility(Vector3 localEye, int localSlot)
        {
            _visibleSlots.Clear();
            for (int slot = 0; slot < SimConfig.MaxEntities; slot++)
            {
                if (slot == localSlot) continue;                        // 本地玩家豁免（自己永远全渲染）
                if (!_view.TryGetView(slot, out GameObject view) || view == null)
                {
                    ReleaseSlot(slot);                                 // 无视图（阵亡回收/未出生）——状态清退并归还池前归位
                    continue;
                }

                SlotState state = EnsureSlotState(slot, view);
                bool blocked = !IsVisible(localEye, slot, view);
                StepHide(state, blocked);
                if (!state.Hidden) _visibleSlots.Add(slot);             // 可参与相机补位渐隐的可见远端
            }
        }

        /// <summary>
        /// 目标身体是否有任一点能从本地眼位看见：头/胸/髋 × 中/左右缘（横向轴垂直于观察方向）；
        /// 顶中采样排首位——常见"完全可见"只需一次线段求交。
        /// </summary>
        private bool IsVisible(Vector3 localEye, int slot, GameObject view)
        {
            Vector3 targetBase = BaseOf(slot, view);
            Vector3 flat = targetBase - localEye;
            flat.y = 0f;
            Vector3 side = flat.sqrMagnitude > 0f
                ? Vector3.Cross(Vector3.up, flat.normalized) * BodyBake.Radius
                : Vector3.zero;

            for (int h = 0; h < TargetSampleHeights.Length; h++)
            {
                Vector3 center = targetBase + Vector3.up * TargetSampleHeights[h];
                for (int s = 0; s < TargetSampleSideFactors.Length; s++)
                {
                    Vector3 sample = center + side * TargetSampleSideFactors[s];
                    if (!VisionSegment.BlockedByAny(in _map, localEye, sample)) return true;
                }
            }
            return false;
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

            state = new SlotState { View = view, Renderers = ResolveRenderers(view), Dwell = MinHoldProbes };
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

        /// <summary>迟滞推进单实体显隐：连续 <see cref="HoldProbes"/> 拍同向且当前状态已保持
        /// <see cref="MinHoldProbes"/> 拍才切换——切出遮挡即恢复（渲染开关，不销毁）。</summary>
        private void StepHide(SlotState state, bool blocked)
        {
            state.Dwell++;
            if (state.Hidden)
            {
                if (blocked) { state.ClearRun = 0; return; }
                if (++state.ClearRun < HoldProbes) return;
                if (state.Dwell < MinHoldProbes) return;
                state.Hidden = false;
                state.Dwell = 0;
                state.ClearRun = 0;
                SetRenderersEnabled(state.Renderers, true);
            }
            else
            {
                if (!blocked) { state.BlockedRun = 0; return; }
                if (++state.BlockedRun < HoldProbes) return;
                if (state.Dwell < MinHoldProbes) return;
                state.Hidden = true;
                state.Dwell = 0;
                state.BlockedRun = 0;
                SetRenderersEnabled(state.Renderers, false);
            }
        }

        /// <summary>目标脚下位置：**优先取渲染帧快照位**（<see cref="SimView.TryGetRenderPosition"/>，按槽位；
        /// 本地/远端同一条时间线）；无快照源（单帧/直连装配）退回视图位。角色视觉采样与相机补位端点共用。
        /// </summary>
        private Vector3 BaseOf(int slot, GameObject view)
        {
            if (_view.TryGetRenderPosition(slot, out SimVector3 pos))
                return new Vector3(pos.X, pos.Y, pos.Z);
            return view.transform.position;
        }

        /// <summary>目标柱顶（相机补位渐隐的端点）＝脚下位置 + 站姿烘焙身高。</summary>
        private Vector3 TopOf(int slot, GameObject view) => BaseOf(slot, view) + Vector3.up * BodyBake.Height;

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
                state.Dwell = MinHoldProbes;          // 保守恢复后首次判定不被最短保持延迟
            }
        }

        private void RestoreAll()
        {
            RestoreHiddenSlots();
            _visibleSlots.Clear();
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
                oc.BlockedThisProbe = false;
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

        /// <summary>单遮挡物绑定：障碍下标（求交用）＋场景渲染件（材质切换用）＋迟滞状态＋本拍触发。</summary>
        private sealed class Occluder
        {
            public int ObstacleIndex;
            public Renderer Renderer;
            public bool Faded;
            public int BlockedRun;
            public int ClearRun;
            public bool BlockedThisProbe;   // 自身或任一可见远端被本掩体挡住（OR 汇总后喂 StepFade）
        }

        /// <summary>单槽位隐藏状态：视图实例（换代检测）＋自有渲染器（显隐开关）＋迟滞计数＋状态已保持拍数。</summary>
        private sealed class SlotState
        {
            public GameObject View;
            public Renderer[] Renderers;
            public bool Hidden;
            public int BlockedRun;
            public int ClearRun;
            public int Dwell;              // 当前状态已保持的探针数（最短保持用；切换即清零）
        }
    }
}
